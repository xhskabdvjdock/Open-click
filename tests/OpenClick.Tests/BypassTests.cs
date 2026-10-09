using OpenClick.Core;

namespace OpenClick.Tests;

[TestClass]
public sealed class BypassTests
{
    [TestMethod]
    public void PassThrough_DoesNotConsumeTimingWindow()
    {
        var cfg = new FilterConfig
        {
            Enabled = true, Mode = FilterMode.PerButtonDebounce, IntervalMs = 200,
            EnabledButtons = [MouseButton.Left],
        };
        var e = new ClickFilterEngine(cfg, new ManualClock());
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1000));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1010));
        // Bypassed event inside the window must not move/consume timing...
        Assert.AreEqual(FilterDecision.Allow, e.RecordPassThrough(MouseButton.Left, ButtonEventType.Down));
        Assert.AreEqual(FilterDecision.Allow, e.RecordPassThrough(MouseButton.Left, ButtonEventType.Up));
        // ...so a click 50ms after the original accept is still suppressed,
        // and one outside the window is allowed.
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessDown(MouseButton.Left, 1050));
        Assert.AreEqual(FilterDecision.Suppress, e.ProcessUp(MouseButton.Left, 1055));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessDown(MouseButton.Left, 1300));
        Assert.AreEqual(FilterDecision.Allow, e.ProcessUp(MouseButton.Left, 1310));
    }
}
