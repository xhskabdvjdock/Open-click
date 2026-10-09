using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenClick.Core;

namespace OpenClick.App.Input;

/// <summary>
/// Low-level observation record passed from the hook thread to the UI via a bounded queue.
/// The hook callback itself never touches the UI or disk.
/// </summary>
public sealed class HookObservation
{
    public MouseButton Button;
    public ButtonEventType Type;
    public long MonotonicMs;
    public FilterDecision Decision;
    public long MsSincePrevious;
    public bool IsScroll;
    public int ScrollDelta;
}

/// <summary>
/// WH_MOUSE_LL hook manager. Fast, fail-safe, UI-thread friendly.
/// - Only button DOWN/UP messages are evaluated; move/wheel always pass through.
/// - Callback does no I/O, no allocations beyond the queue slot, guarded by try/catch.
/// - Consecutive failures trigger automatic disable (fail-safe).
/// </summary>
public sealed class MouseHookService : IDisposable
{
    private readonly ClickFilterEngine _engine;
    private readonly IMonotonicClock _clock;
    private readonly Action<string, Exception?> _logError;
    private NativeMethods.LowLevelMouseProc? _proc; // keep alive
    private IntPtr _hookId = IntPtr.Zero;
    private readonly object _lock = new();
    private int _consecutiveErrors;
    private long _lastEventMs;
    private bool _disposed;

    private readonly ConcurrentQueue<HookObservation> _queue = new();
    private int _queuedCount;
    private const int MaxQueued = 2000;

    public event Action<bool, string?>? RunningChanged; // (isRunning, error)

    public bool IsRunning
    {
        get { lock (_lock) return _hookId != IntPtr.Zero; }
    }

    /// <summary>Optional foreground-process bypass list (process names without .exe). Empty = disabled.</summary>
    public HashSet<string> BypassProcesses { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set and returning true, all button events pass through without timing effects.</summary>
    public Func<bool>? TemporaryBypassCheck { get; set; }
    private string? _cachedProc = "";
    private long _cachedProcMs;
    private IntPtr _cachedHwnd;

    public MouseHookService(ClickFilterEngine engine, IMonotonicClock clock, Action<string, Exception?> logError)
    {
        _engine = engine;
        _clock = clock;
        _logError = logError;
    }

    public bool Start(out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (_disposed) { error = "Hook service disposed."; return false; }
            if (_hookId != IntPtr.Zero) return true;
            try
            {
                _proc = HookCallback;
                IntPtr hMod = NativeMethods.GetModuleHandle(null);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
                if (_hookId == IntPtr.Zero)
                {
                    int code = Marshal.GetLastWin32Error();
                    error = $"Could not install the mouse hook (Win32 error {code}). " +
                            "Filtering stays OFF; your mouse works normally. " +
                            "This can happen in some elevated / remote / secured sessions (UIPI).";
                    _logError(error, null);
                    RunningChanged?.Invoke(false, error);
                    return false;
                }
                _consecutiveErrors = 0;
                RunningChanged?.Invoke(true, null);
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not install the mouse hook: " + ex.Message + " Filtering stays OFF.";
                _logError(error, ex);
                RunningChanged?.Invoke(false, error);
                return false;
            }
        }
    }

    public void Stop()
    {
        IntPtr id = IntPtr.Zero;
        lock (_lock)
        {
            id = _hookId;
            _hookId = IntPtr.Zero;
        }
        if (id != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWindowsHookEx(id); } catch { }
        }
        RunningChanged?.Invoke(false, null);
    }

    /// <summary>Drain observations for the UI/tester (call on a timer, e.g. every 100 ms).</summary>
    public List<HookObservation> DrainObservations()
    {
        var list = new List<HookObservation>(Math.Min(_queuedCount, 256));
        int n = 0;
        while (n < 256 && _queue.TryDequeue(out var o))
        {
            Interlocked.Decrement(ref _queuedCount);
            list.Add(o);
            n++;
        }
        return list;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        try
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_MOUSEWHEEL || msg == NativeMethods.WM_MOUSEHWHEEL)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    int delta = (short)NativeMethods.HiWord(data.mouseData);
                    EnqueueScroll(delta, _clock.GetMilliseconds());
                }
                catch { }
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            var btn = MapButton(msg, lParam, out var type);
            if (btn == null || type == null)
            {
                // Move, wheel, or unknown: always pass through. Never filter scroll.
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            long now = _clock.GetMilliseconds();

            // Temporary bypass (hotkey / button): pass through with no timing effects.
            bool tempBypass = false;
            try { tempBypass = TemporaryBypassCheck?.Invoke() == true; } catch { tempBypass = false; }
            if (tempBypass)
            {
                _engine.RecordPassThrough(btn.Value, type.Value);
                Enqueue(btn.Value, type.Value, now, FilterDecision.Allow);
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Optional transparent foreground-process bypass (read-only, cached).
            if (BypassProcesses.Count > 0 && IsBypassedForeground(now))
            {
                _engine.RecordPassThrough(btn.Value, type.Value);
                Enqueue(btn.Value, type.Value, now, FilterDecision.Allow);
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            var decision = _engine.ProcessEvent(btn.Value, type.Value, now);
            Enqueue(btn.Value, type.Value, now, decision);
            _consecutiveErrors = 0;
            if (decision == FilterDecision.Suppress)
                return (IntPtr)1; // swallow
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            _consecutiveErrors++;
            try { _logError("Mouse hook callback error.", ex); } catch { }
            if (_consecutiveErrors >= 5)
            {
                // Fail-safe: release the hook so input returns to normal.
                try { FailSafeDisable("The mouse hook encountered repeated errors and was disabled to protect your input."); } catch { }
            }
            try { return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam); }
            catch { return IntPtr.Zero; }
        }
    }

    private void Enqueue(MouseButton b, ButtonEventType t, long now, FilterDecision d)
    {
        if (_queuedCount >= MaxQueued) return; // bounded: drop rather than grow
        long sincePrev = now - Interlocked.Exchange(ref _lastEventMs, now);
        if (sincePrev < 0) sincePrev = 0;
        _queue.Enqueue(new HookObservation { Button = b, Type = t, MonotonicMs = now, Decision = d, MsSincePrevious = sincePrev });
        Interlocked.Increment(ref _queuedCount);
    }

    private void EnqueueScroll(int delta, long now)
    {
        if (_queuedCount >= MaxQueued) return;
        _queue.Enqueue(new HookObservation { IsScroll = true, ScrollDelta = delta, MonotonicMs = now, Decision = FilterDecision.Allow });
        Interlocked.Increment(ref _queuedCount);
    }

    private static MouseButton? MapButton(int msg, IntPtr lParam, out ButtonEventType? type)
    {
        type = null;
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN: type = ButtonEventType.Down; return MouseButton.Left;
            case NativeMethods.WM_LBUTTONUP: type = ButtonEventType.Up; return MouseButton.Left;
            case NativeMethods.WM_RBUTTONDOWN: type = ButtonEventType.Down; return MouseButton.Right;
            case NativeMethods.WM_RBUTTONUP: type = ButtonEventType.Up; return MouseButton.Right;
            case NativeMethods.WM_MBUTTONDOWN: type = ButtonEventType.Down; return MouseButton.Middle;
            case NativeMethods.WM_MBUTTONUP: type = ButtonEventType.Up; return MouseButton.Middle;
            case NativeMethods.WM_XBUTTONDOWN:
            case NativeMethods.WM_XBUTTONUP:
                type = msg == NativeMethods.WM_XBUTTONDOWN ? ButtonEventType.Down : ButtonEventType.Up;
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    int hi = NativeMethods.HiWord(data.mouseData);
                    if (hi == NativeMethods.XBUTTON1) return MouseButton.XButton1;
                    if (hi == NativeMethods.XBUTTON2) return MouseButton.XButton2;
                    return null;
                }
                catch { return null; }
            default:
                return null; // move / wheel / hwheel pass through
        }
    }

    private bool IsBypassedForeground(long now)
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == _cachedHwnd && now - _cachedProcMs < 500)
                return _cachedProc != null && BypassProcesses.Contains(_cachedProc);
            _cachedHwnd = hwnd;
            _cachedProcMs = now;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string? name = null;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                name = proc.ProcessName;
            }
            catch { name = ""; }
            _cachedProc = name ?? "";
            return BypassProcesses.Contains(_cachedProc);
        }
        catch { return false; }
    }

    private void FailSafeDisable(string message)
    {
        Stop();
        try { _engine.SetEnabled(false); } catch { }
        RunningChanged?.Invoke(false, message);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
