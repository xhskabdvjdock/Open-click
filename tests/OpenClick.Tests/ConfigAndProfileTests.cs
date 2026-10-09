using OpenClick.Core;
using OpenClick.Core.Profiles;
using OpenClick.Core.Settings;
using OpenClick.Core.Tester;

namespace OpenClick.Tests;

[TestClass]
public sealed class ConfigAndProfileTests
{
    [TestMethod]
    public void FilterConfig_RequiresButtonSelectionWhenEnabled()
    {
        var cfg = new FilterConfig
        {
            Enabled = true, IntervalMs = 100, Mode = FilterMode.SelectedButtons,
            EnabledButtons = [],
        };
        Assert.IsFalse(cfg.IsValid(out var errors));
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void FilterConfig_LeftOnly_IgnoresButtonSet()
    {
        var cfg = new FilterConfig
        {
            Enabled = true, IntervalMs = 100, Mode = FilterMode.LeftOnly,
            EnabledButtons = [],
        };
        Assert.IsTrue(cfg.IsValid(out _));
        Assert.IsTrue(cfg.IsButtonFiltered(MouseButton.Left));
        Assert.IsFalse(cfg.IsButtonFiltered(MouseButton.Right));
    }

    [TestMethod]
    public void ProfileManager_BuiltInsLoad_WhenEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pm = new ProfileManager(dir);
            pm.Load();
            var list = pm.List();
            Assert.IsTrue(list.Count >= 6);
            Assert.IsTrue(list.Any(p => p.Name == "Filter Off"));
            Assert.IsTrue(list.Any(p => p.Name == "Standard Protection"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ProfileManager_CreateRenameDuplicateDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pm = new ProfileManager(dir);
            pm.Load();
            int before = pm.List().Count;
            var cfg = new FilterConfig { Enabled = true, IntervalMs = 120, Mode = FilterMode.PerButtonDebounce, EnabledButtons = [MouseButton.Left] };
            var created = pm.Create("My Profile", cfg);
            Assert.AreEqual(before + 1, pm.List().Count);
            pm.Rename(created.Id, "Renamed");
            Assert.AreEqual("Renamed", pm.Get(created.Id)!.Name);
            var dup = pm.Duplicate(created.Id);
            Assert.IsTrue(dup.Name.Contains("Copy"));
            pm.Delete(dup.Id);
            pm.Delete(created.Id);
            Assert.AreEqual(before, pm.List().Count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ProfileManager_RejectsInvalidImport()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pm = new ProfileManager(dir);
            pm.Load();
            try { pm.Import("not json{{"); Assert.Fail("Expected ArgumentException."); }
            catch (ArgumentException) { }
            try { pm.Import("{\"Id\":\"x\",\"Name\":\"\",\"Config\":{\"Enabled\":true,\"IntervalMs\":9999}}"); Assert.Fail("Expected ArgumentException."); }
            catch (ArgumentException) { }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ProfileManager_ExportImport_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pm = new ProfileManager(dir);
            pm.Load();
            var first = pm.List().First();
            var json = pm.Export(first.Id);
            var dir2 = Path.Combine(Path.GetTempPath(), "oc-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var pm2 = new ProfileManager(dir2);
                pm2.Load();
                var imported = pm2.Import(json);
                Assert.AreEqual(first.Name, imported.Name);
            }
            finally { try { Directory.Delete(dir2, true); } catch { } }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ClickCounter_CountsCompletedClicks_NotDowns()
    {
        var c = new ClickCounter();
        c.OnDown(MouseButton.Left, suppressed: false);
        Assert.AreEqual(0, c.GetCount(MouseButton.Left)); // held: not yet counted
        Assert.IsTrue(c.OnUp(MouseButton.Left, suppressed: false));
        Assert.AreEqual(1, c.GetCount(MouseButton.Left));
        // Suppressed downs never produce counts.
        c.OnDown(MouseButton.Left, suppressed: true);
        Assert.IsFalse(c.OnUp(MouseButton.Left, suppressed: true));
        Assert.AreEqual(1, c.GetCount(MouseButton.Left));
        // Double click = 2.
        c.OnDown(MouseButton.Left, false); c.OnUp(MouseButton.Left, false);
        Assert.AreEqual(2, c.GetCount(MouseButton.Left));
        c.Reset(MouseButton.Left);
        Assert.AreEqual(0, c.GetCount(MouseButton.Left));
    }

    [TestMethod]
    public void DoubleClickAnalyzer_MeasuresAvgMinMax()
    {
        var a = new DoubleClickAnalyzer(500);
        Assert.IsNull(a.OnDown(MouseButton.Left, 1000));
        Assert.AreEqual(200, a.OnDown(MouseButton.Left, 1200));
        Assert.AreEqual(100, a.OnDown(MouseButton.Left, 1300));
        Assert.AreEqual(3 - 1, a.Count);
        Assert.AreEqual(150.0, a.AverageMs, 0.001);
        Assert.AreEqual(100, a.MinMs);
        Assert.AreEqual(200, a.MaxMs);
        var hist = a.Histogram();
        Assert.IsTrue(hist.Values.Sum() == 2);
        a.Reset();
        Assert.AreEqual(0, a.Count);
    }

    [TestMethod]
    public void TestSessionSummary_ClipboardAndCsv_NotEmpty()
    {
        var s = new TestSessionSummary
        {
            Duration = TimeSpan.FromSeconds(60),
            ClicksPerButton = new Dictionary<MouseButton, long> { [MouseButton.Left] = 10, [MouseButton.Right] = 2 },
            TotalClicks = 12,
            AverageIntervalMs = 150.5,
            FastestIntervalMs = 40,
            SlowestIntervalMs = 600,
            DoubleClickIntervalsMeasured = 11,
            SuspiciousRapidClicks = 1,
            ScrollEvents = 5,
        };
        Assert.IsTrue(s.ToClipboardText().Contains("Total completed clicks: 12"));
        Assert.IsTrue(s.ToCsv().Contains("total_clicks,12"));
    }
}
