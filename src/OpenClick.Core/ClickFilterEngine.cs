namespace OpenClick.Core;

/// <summary>
/// Snapshot of engine counters. Updated under lock; safe to poll from UI on a timer.
/// </summary>
public readonly struct FilterSnapshot
{
    public readonly long ObservedDowns;
    public readonly long SuppressedDowns;
    public readonly long AllowedDowns;
    public readonly long ObservedUps;
    public readonly long SuppressedUps;
    public readonly long AllowedUps;

    public FilterSnapshot(long observedDowns, long suppressedDowns, long allowedDowns,
        long observedUps, long suppressedUps, long allowedUps)
    {
        ObservedDowns = observedDowns;
        SuppressedDowns = suppressedDowns;
        AllowedDowns = allowedDowns;
        ObservedUps = observedUps;
        SuppressedUps = suppressedUps;
        AllowedUps = allowedUps;
    }
}

sealed class ButtonState
{
    public long LastAcceptedDownMs;
    public bool HasAccepted;
    public bool LogicalDown;      // an accepted DOWN is outstanding (button logically held)
    public int SuppressedDowns;   // suppressed DOWNs awaiting their matching UP to consume
}

/// <summary>
/// Core click-filtering engine. Pure logic, no OS calls, thread-safe, testable.
///
/// Safety rules (spec section 6):
/// - Only DOWN events are ever filtered by timing. UPs are suppressed ONLY to consume
///   the matching UP of a previously suppressed DOWN (keeps press/release pairing coherent).
/// - Movement / wheel are never touched (handled by the hook layer, which passes them through).
/// - If the button is already logically held (dragging), new DOWNs are allowed through
///   (favor drag-and-drop over aggressive filtering).
/// - Monotonic timestamps (ms) must be supplied by the caller (Stopwatch-based in production).
/// </summary>
public sealed class ClickFilterEngine
{
    private readonly object _lock = new();
    private FilterConfig _config;
    private readonly IMonotonicClock _clock;
    private readonly Dictionary<MouseButton, ButtonState> _states = new();

    private long _lastAcceptedGlobalMs;
    private bool _hasGlobalAccepted;

    private long _observedDowns;
    private long _suppressedDowns;
    private long _allowedDowns;
    private long _observedUps;
    private long _suppressedUps;
    private long _allowedUps;

    /// <summary>Fired after each decision (for statistics / tester). Kept minimal; subscribers must not block.</summary>
    public event Action<MouseButton, ButtonEventType, FilterDecision, long>? DecisionMade;

    private static readonly MouseButton[] AllButtons =
        [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2];

    public ClickFilterEngine(FilterConfig? config = null, IMonotonicClock? clock = null)
    {
        _config = (config ?? new FilterConfig()).Clone();
        _clock = clock ?? new StopwatchClock();
        foreach (var b in AllButtons)
            _states[b] = new ButtonState();
    }

    public FilterConfig GetConfig()
    {
        lock (_lock) return _config.Clone();
    }

    public void UpdateConfig(FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = config.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid filter configuration: " + string.Join("; ", errors));
        lock (_lock)
        {
            bool timingScopeChanged = TimingScopeChanged(_config, config);
            _config = config.Clone();
            if (timingScopeChanged)
            {
                // Scope/mode/interval change: clear timing windows but preserve
                // outstanding press state so held buttons are not stranded.
                foreach (var s in _states.Values)
                {
                    s.HasAccepted = false;
                    s.LastAcceptedDownMs = 0;
                }
                _hasGlobalAccepted = false;
                _lastAcceptedGlobalMs = 0;
            }
        }
    }

    private static bool TimingScopeChanged(FilterConfig oldC, FilterConfig newC)
        => oldC.Mode != newC.Mode || oldC.IntervalMs != newC.IntervalMs
           || !oldC.EnabledButtons.SetEquals(newC.EnabledButtons ?? []);

    public void SetEnabled(bool enabled)
    {
        lock (_lock) _config.Enabled = enabled;
    }

    /// <summary>Clear timing windows (e.g., on profile switch). Preserves held-button state.</summary>
    public void ResetTiming()
    {
        lock (_lock)
        {
            foreach (var s in _states.Values)
            {
                s.HasAccepted = false;
                s.LastAcceptedDownMs = 0;
            }
            _hasGlobalAccepted = false;
            _lastAcceptedGlobalMs = 0;
        }
    }

    /// <summary>Clear counters only.</summary>
    public void ResetCounters()
    {
        lock (_lock)
        {
            _observedDowns = _suppressedDowns = _allowedDowns = 0;
            _observedUps = _suppressedUps = _allowedUps = 0;
        }
    }

    /// <summary>Clear timing, press state and counters.</summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            foreach (var s in _states.Values)
            {
                s.HasAccepted = false;
                s.LastAcceptedDownMs = 0;
                s.LogicalDown = false;
                s.SuppressedDowns = 0;
            }
            _hasGlobalAccepted = false;
            _lastAcceptedGlobalMs = 0;
            _observedDowns = _suppressedDowns = _allowedDowns = 0;
            _observedUps = _suppressedUps = _allowedUps = 0;
        }
    }

    public FilterSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new FilterSnapshot(_observedDowns, _suppressedDowns, _allowedDowns,
                _observedUps, _suppressedUps, _allowedUps);
        }
    }

    public FilterDecision ProcessDown(MouseButton button, long timestampMs) =>
        ProcessEvent(button, ButtonEventType.Down, timestampMs);

    public FilterDecision ProcessUp(MouseButton button, long timestampMs) =>
        ProcessEvent(button, ButtonEventType.Up, timestampMs);

    /// <summary>Process using the engine clock for timestamp.</summary>
    public FilterDecision ProcessDown(MouseButton button) => ProcessEvent(button, ButtonEventType.Down, _clock.GetMilliseconds());
    public FilterDecision ProcessUp(MouseButton button) => ProcessEvent(button, ButtonEventType.Up, _clock.GetMilliseconds());

    public FilterDecision ProcessEvent(MouseButton button, ButtonEventType type, long timestampMs)
    {
        FilterDecision decision;
        lock (_lock)
        {
            var state = _states[button];
            if (type == ButtonEventType.Down)
            {
                _observedDowns++;
                if (!_config.IsButtonFiltered(button))
                {
                    // Pass-through: record press state so the matching UP stays coherent.
                    state.LogicalDown = true;
                    _allowedDowns++;
                    decision = FilterDecision.Allow;
                }
                else if (state.LogicalDown)
                {
                    // Drag-and-drop protection: button already logically held.
                    // A new DOWN while held is anomalous; allow it rather than
                    // risk breaking a drag. Do NOT move the timing window.
                    _allowedDowns++;
                    decision = FilterDecision.Allow;
                }
                else
                {
                    bool hasPrev;
                    long last;
                    if (_config.Mode == FilterMode.GlobalDebounce)
                    {
                        hasPrev = _hasGlobalAccepted;
                        last = _lastAcceptedGlobalMs;
                    }
                    else
                    {
                        hasPrev = state.HasAccepted;
                        last = state.LastAcceptedDownMs;
                    }

                    bool withinWindow = false;
                    if (hasPrev && timestampMs >= last)
                    {
                        long elapsed = timestampMs - last;
                        withinWindow = elapsed < _config.IntervalMs;
                    }
                    // If timestamp < last (non-monotonic caller), treat as outside window (allow).

                    if (withinWindow)
                    {
                        state.SuppressedDowns++;
                        _suppressedDowns++;
                        decision = FilterDecision.Suppress;
                    }
                    else
                    {
                        state.LastAcceptedDownMs = timestampMs;
                        state.HasAccepted = true;
                        state.LogicalDown = true;
                        if (_config.Mode == FilterMode.GlobalDebounce)
                        {
                            _lastAcceptedGlobalMs = timestampMs;
                            _hasGlobalAccepted = true;
                        }
                        _allowedDowns++;
                        decision = FilterDecision.Allow;
                    }
                }
            }
            else // Up
            {
                _observedUps++;
                if (state.SuppressedDowns > 0)
                {
                    // Consume the UP that pairs with a suppressed DOWN so the
                    // target app never sees an unmatched release.
                    state.SuppressedDowns--;
                    _suppressedUps++;
                    decision = FilterDecision.Suppress;
                }
                else
                {
                    state.LogicalDown = false;
                    _allowedUps++;
                    decision = FilterDecision.Allow;
                }
            }
        }

        try { DecisionMade?.Invoke(button, type, decision, timestampMs); }
        catch { /* subscribers must never break the hook path */ }
        return decision;
    }

    /// <summary>
    /// Record a pass-through event during bypass: keeps press/release pairing coherent
    /// without consuming or moving any timing window. Always allows.
    /// </summary>
    public FilterDecision RecordPassThrough(MouseButton button, ButtonEventType type)
    {
        lock (_lock)
        {
            var state = _states[button];
            if (type == ButtonEventType.Down)
            {
                _observedDowns++;
                state.LogicalDown = true;
                _allowedDowns++;
            }
            else
            {
                _observedUps++;
                if (state.SuppressedDowns > 0)
                {
                    state.SuppressedDowns--;
                    _suppressedUps++;
                    return FilterDecision.Suppress;
                }
                state.LogicalDown = false;
                _allowedUps++;
            }
            return FilterDecision.Allow;
        }
    }
}
