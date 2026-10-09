namespace OpenClick.Core;

/// <summary>Monotonic time source. Implementations must never go backwards under normal operation.</summary>
public interface IMonotonicClock
{
    long GetMilliseconds();
}

/// <summary>Production clock based on <see cref="System.Diagnostics.Stopwatch"/> (monotonic).</summary>
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public long GetMilliseconds() => _sw.ElapsedMilliseconds;
}

/// <summary>Manually advanced clock for deterministic unit tests.</summary>
public sealed class ManualClock : IMonotonicClock
{
    private long _nowMs;
    public ManualClock(long startMs = 0) => _nowMs = startMs;
    public long GetMilliseconds() => _nowMs;
    public void Advance(long deltaMs) => _nowMs += deltaMs;
    public void Set(long valueMs) => _nowMs = valueMs;
}
