using System.Reflection;
using System.Runtime.InteropServices;
using OpenClick.App.Input;
using OpenClick.Core;

namespace OpenClick.Tests;

/// <summary>Hook registration/cleanup lifecycle + message-mapping (no real mouse events needed).</summary>
[TestClass]
public sealed class HookLifecycleTests
{
    private static MouseHookService CreateService(ClickFilterEngine? engine = null)
    {
        engine ??= new ClickFilterEngine(new FilterConfig { Enabled = true, IntervalMs = 100 }, new ManualClock());
        return new MouseHookService(engine, new ManualClock(), (_, __) => { });
    }

    [TestMethod]
    public void StartStop_Lifecycle_IsClean()
    {
        using var hook = CreateService();
        Assert.IsFalse(hook.IsRunning);
        Assert.IsTrue(hook.Start(out var err), "Start failed: " + err);
        Assert.IsTrue(hook.IsRunning);
        Assert.IsTrue(hook.Start(out _)); // idempotent
        Assert.IsTrue(hook.IsRunning);
        hook.Stop();
        Assert.IsFalse(hook.IsRunning);
        hook.Stop(); // idempotent, must not throw
        Assert.IsFalse(hook.IsRunning);
    }

    private static (MouseButton? Button, ButtonEventType? Type) Map(int msg, uint mouseDataHi = 0)
    {
        var t = typeof(MouseHookService);
        var m = t.GetMethod("MapButton", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(m);
        IntPtr mem = IntPtr.Zero;
        try
        {
            // MSLLHOOKSTRUCT: POINT(8) + mouseData u32 @offset 8 + flags + time + extra.
            mem = Marshal.AllocHGlobal(32);
            for (int i = 0; i < 32; i++) Marshal.WriteByte(mem, i, 0);
            Marshal.WriteInt32(mem, 8, unchecked((int)(mouseDataHi << 16)));
            object?[] args = [msg, mem, null];
            var ret = m.Invoke(null, args);
            return ((MouseButton?)ret, (ButtonEventType?)args[2]);
        }
        finally { if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem); }
    }

    [TestMethod]
    public void MapButton_MapsAllButtons()
    {
        Assert.AreEqual((MouseButton.Left, ButtonEventType.Down), Map(0x0201));
        Assert.AreEqual((MouseButton.Left, ButtonEventType.Up), Map(0x0202));
        Assert.AreEqual((MouseButton.Right, ButtonEventType.Down), Map(0x0204));
        Assert.AreEqual((MouseButton.Right, ButtonEventType.Up), Map(0x0205));
        Assert.AreEqual((MouseButton.Middle, ButtonEventType.Down), Map(0x0207));
        Assert.AreEqual((MouseButton.Middle, ButtonEventType.Up), Map(0x0208));
        Assert.AreEqual((MouseButton.XButton1, ButtonEventType.Down), Map(0x020B, 1));
        Assert.AreEqual((MouseButton.XButton2, ButtonEventType.Down), Map(0x020B, 2));
        Assert.AreEqual((MouseButton.XButton1, ButtonEventType.Up), Map(0x020C, 1));
        Assert.AreEqual((MouseButton.XButton2, ButtonEventType.Up), Map(0x020C, 2));
    }

    [TestMethod]
    public void MapButton_MoveWheelUnknown_PassThrough()
    {
        Assert.AreEqual((null, null), Map(0x0200)); // move
        Assert.AreEqual((null, null), Map(0x020A)); // wheel
        Assert.AreEqual((null, null), Map(0x020E)); // h-wheel
        Assert.AreEqual((null, null), Map(0x1234)); // unknown
    }

    [TestMethod]
    public void HookCallback_EndToEnd_FiltersRapidClicks()
    {
        // Real callback code path (bypass checks -> engine -> bounded queue -> return code),
        // driven without OS input delivery so the test has no side effects.
        var engine = new ClickFilterEngine(new FilterConfig
        {
            Enabled = true, Mode = FilterMode.PerButtonDebounce, IntervalMs = 100,
            EnabledButtons = [MouseButton.Left],
        }, new ManualClock());
        using var hook = CreateService(engine);
        Assert.IsTrue(hook.Start(out var err), "Start failed: " + err);

        var cb = typeof(MouseHookService).GetMethod("HookCallback", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(cb);
        object?[] Call(int nCode, int msg) => [(object)nCode, (object)(IntPtr)msg, (object)IntPtr.Zero];

        var r1 = (IntPtr)cb.Invoke(hook, Call(0, 0x0201))!; // WM_LBUTTONDOWN -> allowed
        var r2 = (IntPtr)cb.Invoke(hook, Call(0, 0x0202))!; // WM_LBUTTONUP   -> allowed
        var r3 = (IntPtr)cb.Invoke(hook, Call(0, 0x0201))!; // rapid 2nd DOWN -> suppressed (same ms)
        var r4 = (IntPtr)cb.Invoke(hook, Call(0, 0x0202))!; // paired UP      -> suppressed
        var rMove = (IntPtr)cb.Invoke(hook, Call(0, 0x0200))!; // move passes through
        var rNeg = (IntPtr)cb.Invoke(hook, Call(-1, 0x0201))!; // nCode<0 passes through

        Assert.AreEqual(IntPtr.Zero, r1);
        Assert.AreEqual(IntPtr.Zero, r2);
        Assert.AreEqual((IntPtr)1, r3);
        Assert.AreEqual((IntPtr)1, r4);
        Assert.AreEqual(IntPtr.Zero, rMove);
        Assert.AreEqual(IntPtr.Zero, rNeg);

        var drained = hook.DrainObservations();
        // Move and nCode<0 produce no observations (pass-through without enqueue).
        Assert.AreEqual(4, drained.Count);
        Assert.AreEqual(FilterDecision.Allow, drained[0].Decision);
        Assert.AreEqual(FilterDecision.Allow, drained[1].Decision);
        Assert.AreEqual(FilterDecision.Suppress, drained[2].Decision);
        Assert.AreEqual(FilterDecision.Suppress, drained[3].Decision);
        Assert.AreEqual(MouseButton.Left, drained[2].Button);
        hook.Stop();
    }
}
