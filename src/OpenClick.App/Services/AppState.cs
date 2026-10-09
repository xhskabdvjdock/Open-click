using OpenClick.App.Input;
using OpenClick.Core;
using OpenClick.Core.Diagnostics;
using OpenClick.Core.Profiles;
using OpenClick.Core.Settings;
using OpenClick.Core.Tester;

namespace OpenClick.App.Services;

/// <summary>Central application state: engine, hook, settings, profiles, tester aggregates.</summary>
public sealed class AppState : IDisposable
{
    public static AppState Current { get; } = new();

    public SettingsService SettingsService { get; } = new();
    public AppSettings Settings => SettingsService.Current;
    public ProfileManager Profiles { get; private set; } = null!;
    public ClickFilterEngine Engine { get; private set; } = null!;
    public StopwatchClock Clock { get; } = new();
    public MouseHookService Hook { get; private set; } = null!;
    public GlobalHotkeyService Hotkeys { get; } = new();
    public SimpleLogger Logger { get; private set; } = null!;
    public LocalizationService Localization { get; } = new();

    public ClickCounter TesterCounter { get; } = new();
    public DoubleClickAnalyzer TimingAnalyzer { get; private set; } = new(500);
    public long ScrollEvents;
    public long ScrollDeltaTotal;
    public int LastScrollDelta;
    public DateTimeOffset? LastScrollTime;
    public long SuspiciousRapidClicks;
    public DateTimeOffset? TestSessionStart;
    public bool TesterPaused;
    public bool TesterListening;
    public string? LastHookError;
    private readonly List<ButtonEventRecord> _eventHistory = new();
    private readonly object _historyLock = new();
    private long _lastHistoryMs;

    /// <summary>Temporary bypass until this UTC time (null = no bypass).</summary>
    public DateTimeOffset? BypassUntilUtc;

    public bool BypassActive => BypassUntilUtc.HasValue && DateTimeOffset.UtcNow < BypassUntilUtc.Value;

    public event Action? StateChanged;

    private System.Windows.Threading.DispatcherTimer? _pollTimer;
    private bool _initialized;

    private AppState() { }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        SettingsService.EnsureDirectories();
        Logger = new SimpleLogger(System.IO.Path.Combine(SettingsService.LogsDir, "openclick.log"));
        try
        {
            SettingsService.Load();
        }
        catch (Exception ex) { Logger.Error("Settings load failed; using defaults.", ex); }

        Profiles = new ProfileManager(SettingsService.ProfilesDir);
        try { Profiles.Load(); }
        catch (Exception ex) { Logger.Error("Profile load failed.", ex); }
        Logger.Info("Open Click started.");

        Engine = new ClickFilterEngine(Settings.Filter?.Clone() ?? new FilterConfig(), Clock);
        Hook = new MouseHookService(Engine, Clock, (m, e) => Logger.Error(m, e));
        Hook.TemporaryBypassCheck = () => BypassActive;
        Hook.RunningChanged += (running, err) =>
        {
            if (!running && err != null) LastHookError = err;
            if (running) LastHookError = null;
            NotifyChanged();
        };

        Localization.SetLanguage(Settings.Language ?? "en");
        TimingAnalyzer = new DoubleClickAnalyzer(Settings.TesterDoubleClickThresholdMs);

        // UI polling timer: drains hook queue + refreshes stats without redrawing everything per event.
        _pollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _pollTimer.Tick += (_, __) => PollHookQueue();
        _pollTimer.Start();
    }

    public void NotifyChanged() => StateChanged?.Invoke();

    private void PollHookQueue()
    {
        try
        {
            if (Hook == null) return;
            var obs = Hook.DrainObservations();
            if (obs.Count == 0) return;
            foreach (var o in obs)
            {
                if (o.IsScroll)
                {
                    System.Threading.Interlocked.Increment(ref ScrollEvents);
                    ScrollDeltaTotal += o.ScrollDelta;
                    LastScrollDelta = o.ScrollDelta;
                    LastScrollTime = DateTimeOffset.Now;
                    continue;
                }
                ObserveForTester(o);
            }
            NotifyChanged();
        }
        catch { }
    }

    private void ObserveForTester(HookObservation o)
    {
        // Bounded event history (UI thread only — PollHookQueue runs on UI dispatcher).
        if (!TesterPaused)
        {
            lock (_historyLock)
            {
                long sincePrev = _lastHistoryMs == 0 ? 0 : o.MonotonicMs - _lastHistoryMs;
                if (sincePrev < 0) sincePrev = 0;
                _lastHistoryMs = o.MonotonicMs;
                _eventHistory.Add(new ButtonEventRecord
                {
                    WallTime = DateTimeOffset.Now,
                    MonotonicMs = o.MonotonicMs,
                    Button = o.Button,
                    Type = o.Type,
                    MsSincePrevious = sincePrev,
                    SuppressedByFilter = o.Decision == FilterDecision.Suppress,
                });
                int limit = Math.Clamp(Settings.TesterHistoryLimit, 20, 2000);
                while (_eventHistory.Count > limit)
                    _eventHistory.RemoveAt(0);
            }
        }
        if (TesterPaused) return;
        bool suppressed = o.Decision == FilterDecision.Suppress;
        if (o.Type == ButtonEventType.Down)
        {
            TesterCounter.OnDown(o.Button, suppressed);
            if (!suppressed)
            {
                var interval = TimingAnalyzer.OnDown(o.Button, o.MonotonicMs);
                if (interval.HasValue && interval.Value < Settings.TesterDiagnosticThresholdMs)
                    System.Threading.Interlocked.Increment(ref SuspiciousRapidClicks);
            }
        }
        else
        {
            TesterCounter.OnUp(o.Button, suppressed);
        }
    }

    public void ObserveScroll(int delta)
    {
        System.Threading.Interlocked.Increment(ref ScrollEvents);
        NotifyChanged();
    }

    public List<ButtonEventRecord> GetEventHistorySnapshot()
    {
        lock (_historyLock) return _eventHistory.ToList();
    }

    public void ClearEventHistory()
    {
        lock (_historyLock) { _eventHistory.Clear(); _lastHistoryMs = 0; }
        NotifyChanged();
    }

    /// <summary>Tester works without the filter: run the hook in observe-only mode.</summary>
    public bool SetTesterListening(bool listening, out string? error)
    {
        error = null;
        TesterListening = listening;
        try
        {
            if (listening)
            {
                if (!Hook.IsRunning)
                {
                    if (!Hook.Start(out error))
                    {
                        TesterListening = false;
                        NotifyChanged();
                        return false;
                    }
                }
            }
            else if (!Engine.GetConfig().Enabled)
            {
                Hook.Stop();
            }
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.Error("SetTesterListening failed.", ex);
            return false;
        }
    }

    public bool SetFilterEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            if (enabled)
            {
                // Expire bypass when explicitly enabling.
                BypassUntilUtc = null;
                Engine.SetEnabled(true);
                if (!Hook.Start(out error))
                {
                    Engine.SetEnabled(false);
                    NotifyChanged();
                    return false;
                }
            }
            else
            {
                BypassUntilUtc = null;
                Engine.SetEnabled(false);
                if (!TesterListening)
                    Hook.Stop(); // immediate: input returns to normal
            }
            Settings.Filter.Enabled = enabled;
            SettingsService.Save();
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.Error("SetFilterEnabled failed.", ex);
            try { Engine.SetEnabled(false); Hook.Stop(); } catch { }
            NotifyChanged();
            return false;
        }
    }

    public void ApplyConfig(FilterConfig config)
    {
        Engine.UpdateConfig(config);
        Settings.Filter = config.Clone();
        SettingsService.Save();
        NotifyChanged();
    }

    public void TemporaryBypass(TimeSpan duration)
    {
        BypassUntilUtc = DateTimeOffset.UtcNow.Add(duration);
        NotifyChanged();
    }

    public void ResetStatistics()
    {
        Engine.ResetCounters();
        TesterCounter.Reset();
        TimingAnalyzer.Reset();
        ScrollEvents = 0;
        ScrollDeltaTotal = 0;
        SuspiciousRapidClicks = 0;
        TestSessionStart = null;
        ClearEventHistory();
        NotifyChanged();
    }

    public TestSessionSummary BuildSessionSummary()
    {
        var duration = TestSessionStart.HasValue ? DateTimeOffset.Now - TestSessionStart.Value : TimeSpan.Zero;
        var snap = TesterCounter.Snapshot();
        var intervals = TimingAnalyzer.IntervalsSnapshot();
        return new TestSessionSummary
        {
            Duration = duration,
            ClicksPerButton = new Dictionary<MouseButton, long>(snap),
            TotalClicks = TesterCounter.Total,
            AverageIntervalMs = TimingAnalyzer.AverageMs,
            FastestIntervalMs = intervals.Count == 0 ? 0 : intervals.Min(),
            SlowestIntervalMs = intervals.Count == 0 ? 0 : intervals.Max(),
            DoubleClickIntervalsMeasured = TimingAnalyzer.Count,
            SuspiciousRapidClicks = SuspiciousRapidClicks,
            ScrollEvents = ScrollEvents,
        };
    }

    public void Shutdown()
    {
        try { _pollTimer?.Stop(); } catch { }
        try { Engine?.SetEnabled(false); } catch { }
        try { Hook?.Dispose(); } catch { }
        try { Hotkeys?.Dispose(); } catch { }
        try { SettingsService.Save(); } catch { }
        try { Logger?.Dispose(); } catch { }
    }

    public void Dispose() => Shutdown();
}
