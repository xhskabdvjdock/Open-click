using OpenClick.Core;

namespace OpenClick.Tests;

[TestClass]
public sealed class FilterEngineTests
{
    private static ClickFilterEngine PerButtonEngine(long intervalMs = 100, params MouseButton[] buttons)
    {
        var cfg = new FilterConfig
        {
            Enabled = true,
            Mode = FilterMode.PerButtonDebounce,
            IntervalMs = (int)intervalMs,
            EnabledButtons = buttons.Length == 0
                ? [MouseButton.Left, MouseButton.Right, MouseButton.Middle]
                : new HashSet<MouseButton>(buttons),
        };
        return new ClickFilterEngine(cfg, new ManualClock());
    }

    private static void Click(ClickFilterEngine e, MouseButton b, long downMs)
    {
        e.ProcessDown(b, downMs);
        e.ProcessUp(b, downMs + 10);
    }

    [TestMethod]
    public void EventsOutsideInterval_AreAllowed()
    {
        var e = PerButtonEngine(100);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1010));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1200)); // 200ms later
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1210));
        var s = e.GetSnapshot();
        Assert.AreEqual(2, s.AllowedDowns);
        Assert.AreEqual(0, s.SuppressedDowns);
    }

    [TestMethod]
    public void RepeatedEventsWithinInterval_AreSuppressed_WithPairedUp()
    {
        var e = PerButtonEngine(100);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1010));
        // Bounce 30ms later -> suppressed DOWN + suppressed UP (pairing coherent).
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 1040));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Left, 1050));
        // Next genuine click after window -> allowed again.
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1200));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1210));
        var s = e.GetSnapshot();
        Assert.AreEqual(2, s.AllowedDowns);
        Assert.AreEqual(1, s.SuppressedDowns);
        Assert.AreEqual(1, s.SuppressedUps);
    }

    [TestMethod]
    public void PerButton_TimingIsIndependent()
    {
        var e = PerButtonEngine(100);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1005));
        // Immediate right click must NOT be suppressed in per-button mode.
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Right, 1010));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Right, 1015));
        // But immediate second left click IS suppressed.
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 1020));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Left, 1025));
    }

    [TestMethod]
    public void GlobalMode_SharesWindowAcrossButtons()
    {
        var cfg = new FilterConfig
        {
            Enabled = true, Mode = FilterMode.GlobalDebounce, IntervalMs = 100,
            EnabledButtons = [MouseButton.Left, MouseButton.Right],
        };
        var e = new ClickFilterEngine(cfg, new ManualClock());
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1005));
        // Right click 20ms after accepted left -> suppressed in global mode.
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Right, 1020));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Right, 1025));
    }

    [TestMethod]
    public void LeftOnlyMode_FiltersOnlyLeft()
    {
        var cfg = new FilterConfig
        {
            Enabled = true, Mode = FilterMode.LeftOnly, IntervalMs = 200,
            EnabledButtons = [MouseButton.Left, MouseButton.Right, MouseButton.Middle],
        };
        var e = new ClickFilterEngine(cfg, new ManualClock());
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1005));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 1050));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Left, 1055));
        // Right passes even immediately after left.
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Right, 1060));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Right, 1065));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Right, 1070));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Right, 1075));
    }

    [TestMethod]
    public void ZeroInterval_DisablesFiltering()
    {
        var cfg = new FilterConfig { Enabled = true, Mode = FilterMode.PerButtonDebounce, IntervalMs = 0 };
        var e = new ClickFilterEngine(cfg, new ManualClock());
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000 + i * 5));
            Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1002 + i * 5));
        }
        Assert.AreEqual(0, e.GetSnapshot().SuppressedDowns);
    }

    [TestMethod]
    public void InvalidInterval_RejectedByValidation()
    {
        var cfg = new FilterConfig { Enabled = true, IntervalMs = -5 };
        Assert.IsFalse(cfg.IsValid(out _));
        cfg.IntervalMs = 5000;
        Assert.IsFalse(cfg.IsValid(out _));
        var engine = new ClickFilterEngine();
        try { engine.UpdateConfig(cfg); Assert.Fail("Expected ArgumentException."); }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void DragProtection_AllowsDownWhileLogicallyHeld()
    {
        var e = PerButtonEngine(200);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        // Anomalous second DOWN while still held (no UP yet): allow, don't break drag.
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1010));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1500));
        // Timing window must not have moved: next click 100ms after ORIGINAL accept is still suppressed.
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 1100 - 1000 + 1000)); // 1100 -> 100ms after 1000
    }

    [TestMethod]
    public void HeldButton_DoesNotBecomeStuck()
    {
        var e = PerButtonEngine(100);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        // Disable filter mid-hold: UP must still be allowed.
        e.SetEnabled(false);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1100));
        // Re-enable: next DOWN allowed (timing preserved but hold released).
        e.SetEnabled(true);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 2000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 2010));
    }

    [TestMethod]
    public void RapidTripleClick_FirstAllowed_RestSuppressed()
    {
        var e = PerButtonEngine(100);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 0));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 5));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 30));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Left, 35));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 60));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Left, 65));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 200));
    }

    [TestMethod]
    public void ResetTiming_ClearsWindow_ButPreservesHeldState()
    {
        var e = PerButtonEngine(500);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1010));
        e.ResetTiming();
        // Window cleared -> immediate click allowed.
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1020));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1030));
    }

    [TestMethod]
    public void SimultaneousButtons_BothCoherent()
    {
        var e = PerButtonEngine(100);
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Right, 1005));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1010));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Right, 1015));
        var s = e.GetSnapshot();
        Assert.AreEqual(2, s.AllowedDowns);
        Assert.AreEqual(2, s.AllowedUps);
    }

    [TestMethod]
    public void Counters_DistinguishObservedSuppressedAllowed()
    {
        var e = PerButtonEngine(100);
        Click(e, MouseButton.Left, 1000);   // allowed
        e.ProcessDown(MouseButton.Left, 1030); // suppressed
        e.ProcessUp(MouseButton.Left, 1035);
        Click(e, MouseButton.Left, 2000);   // allowed
        var s = e.GetSnapshot();
        Assert.AreEqual(3, s.ObservedDowns);
        Assert.AreEqual(1, s.SuppressedDowns);
        Assert.AreEqual(2, s.AllowedDowns);
    }
}
