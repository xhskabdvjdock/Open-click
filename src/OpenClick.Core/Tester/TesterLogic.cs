namespace OpenClick.Core.Tester;

/// <summary>Single observed button event for the tester event monitor (bounded history).</summary>
public sealed class ButtonEventRecord
{
    public DateTimeOffset WallTime { get; init; } = DateTimeOffset.Now;
    public long MonotonicMs { get; init; }
    public MouseButton Button { get; init; }
    public ButtonEventType Type { get; init; }
    public long MsSincePrevious { get; init; }
    public bool SuppressedByFilter { get; init; }
}

/// <summary>
/// Counts completed clicks (Down followed by Up). A held button counts once on release.
/// Incomplete presses (Down without Up yet) are pending, not counted. Documents counting rule.
/// </summary>
public sealed class ClickCounter
{
    private readonly object _lock = new();
    private readonly Dictionary<MouseButton, bool> _pending = new();
    private readonly Dictionary<MouseButton, long> _completed = new();

    private static readonly MouseButton[] AllButtons =
        [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2];

    public ClickCounter()
    {
        foreach (var b in AllButtons)
        {
            _pending[b] = false;
            _completed[b] = 0;
        }
    }

    /// <param name="suppressed">If the DOWN was suppressed by the filter, do not start a pending click.</param>
    public void OnDown(MouseButton button, bool suppressed)
    {
        lock (_lock)
        {
            if (suppressed) return;
            _pending[button] = true;
        }
    }

    /// <returns>True if a completed click was counted.</returns>
    public bool OnUp(MouseButton button, bool suppressed)
    {
        lock (_lock)
        {
            if (suppressed) return false;
            if (_pending[button])
            {
                _pending[button] = false;
                _completed[button]++;
                return true;
            }
            return false;
        }
    }

    public long GetCount(MouseButton button)
    {
        lock (_lock) return _completed[button];
    }

    public long Total
    {
        get
        {
            lock (_lock)
            {
                long t = 0;
                foreach (var v in _completed.Values) t += v;
                return t;
            }
        }
    }

    public Dictionary<MouseButton, long> Snapshot()
    {
        lock (_lock) return new Dictionary<MouseButton, long>(_completed);
    }

    public void Reset(MouseButton? button = null)
    {
        lock (_lock)
        {
            if (button.HasValue)
            {
                _completed[button.Value] = 0;
                _pending[button.Value] = false;
            }
            else
            {
                foreach (var b in AllButtons)
                {
                    _completed[b] = 0;
                    _pending[b] = false;
                }
            }
        }
    }
}

/// <summary>
/// Measures intervals between successive completed clicks of the same button (DOWN-to-DOWN).
/// </summary>
public sealed class DoubleClickAnalyzer
{
    private readonly object _lock = new();
    private readonly Dictionary<MouseButton, long> _lastDownMs = new();
    private readonly Dictionary<MouseButton, bool> _hasLast = new();
    private readonly List<long> _intervals = new();
    private const int MaxIntervals = 1000;

    public int ClassificationThresholdMs { get; set; } = 500;

    public DoubleClickAnalyzer(int thresholdMs = 500) => ClassificationThresholdMs = thresholdMs;

    /// <summary>Call on each accepted DOWN. Returns the measured interval, or null for the first click.</summary>
    public long? OnDown(MouseButton button, long monotonicMs)
    {
        lock (_lock)
        {
            if (!_hasLast.TryGetValue(button, out bool has) || !has)
            {
                _lastDownMs[button] = monotonicMs;
                _hasLast[button] = true;
                return null;
            }
            long prev = _lastDownMs[button];
            _lastDownMs[button] = monotonicMs;
            if (monotonicMs < prev) return null;
            long interval = monotonicMs - prev;
            _intervals.Add(interval);
            if (_intervals.Count > MaxIntervals)
                _intervals.RemoveAt(0);
            return interval;
        }
    }

    public int Count { get { lock (_lock) return _intervals.Count; } }

    public double AverageMs
    {
        get
        {
            lock (_lock)
            {
                if (_intervals.Count == 0) return 0;
                double sum = 0;
                foreach (var v in _intervals) sum += v;
                return sum / _intervals.Count;
            }
        }
    }

    public long MinMs { get { lock (_lock) return _intervals.Count == 0 ? 0 : _intervals.Min(); } }
    public long MaxMs { get { lock (_lock) return _intervals.Count == 0 ? 0 : _intervals.Max(); } }

    public IReadOnlyList<long> IntervalsSnapshot()
    {
        lock (_lock) return _intervals.ToList();
    }

    /// <summary>Simple histogram buckets for UI timeline.</summary>
    public Dictionary<string, int> Histogram()
    {
        var buckets = new Dictionary<string, int>
        {
            ["<50"] = 0, ["50-100"] = 0, ["100-200"] = 0,
            ["200-300"] = 0, ["300-500"] = 0, [">500"] = 0,
        };
        lock (_lock)
        {
            foreach (var v in _intervals)
            {
                string k = v < 50 ? "<50" : v < 100 ? "50-100" : v < 200 ? "100-200"
                    : v < 300 ? "200-300" : v <= 500 ? "300-500" : ">500";
                buckets[k]++;
            }
        }
        return buckets;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _intervals.Clear();
            _hasLast.Clear();
            _lastDownMs.Clear();
        }
    }
}
