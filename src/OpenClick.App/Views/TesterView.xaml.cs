using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenClick.App.Services;
using OpenClick.Core;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush = System.Windows.Media.Brush;

namespace OpenClick.App.Views;

public partial class TesterView : UserControl
{
    private readonly AppState _s = AppState.Current;
    private readonly HashSet<MouseButton> _pressed = new();
    private int _lastEventCount;

    public TesterView()
    {
        InitializeComponent();
        _s.StateChanged += Refresh;
        _s.Localization.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) Flash(MouseButton.Left, true);
            if (e.ChangedButton == System.Windows.Input.MouseButton.Right) Flash(MouseButton.Right, true);
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) Flash(MouseButton.Middle, true);
        };
        PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) Flash(MouseButton.Left, false);
            if (e.ChangedButton == System.Windows.Input.MouseButton.Right) Flash(MouseButton.Right, false);
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) Flash(MouseButton.Middle, false);
        };
        Refresh();
    }

    private void ApplyLanguage()
    {
        var L = _s.Localization;
        TitleText.Text = L.T("MouseTester");
        DiagramTitle.Text = L.T("MouseTester");
        CountersTitle.Text = "Clicks & timing";
        EventsTitle.Text = "Events";
        ClearEventsButton.Content = L.T("ClearEvents");
        CopySummaryButton.Content = L.T("CopySummary");
        LeftLabel.Text = L.T("LeftButton");
        RightLabel.Text = L.T("RightButton");
        MiddleLabel.Text = L.T("MiddleButton");
        WheelLabel.Text = "Wheel";
        HardwareNote.Text = L.T("HardwareNote");
    }

    private void Flash(MouseButton b, bool down)
    {
        if (down) _pressed.Add(b); else _pressed.Remove(b);
        PaintDiagram();
    }

    private void PaintDiagram()
    {
        var on = (WpfBrush)FindResource("PrimaryBrush");
        var off = (WpfBrush)FindResource("BorderBrush");
        LeftZone.BorderBrush = _pressed.Contains(MouseButton.Left) ? on : off;
        RightZone.BorderBrush = _pressed.Contains(MouseButton.Right) ? on : off;
        MiddleZone.BorderBrush = _pressed.Contains(MouseButton.Middle) ? on : off;
        LeftZone.Background = _pressed.Contains(MouseButton.Left) ? on : WpfBrushes.Transparent;
        RightZone.Background = _pressed.Contains(MouseButton.Right) ? on : WpfBrushes.Transparent;
        MiddleZone.Background = _pressed.Contains(MouseButton.Middle) ? on : WpfBrushes.Transparent;
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        var L = _s.Localization;
        ListenButton.Content = _s.TesterListening ? "Stop listening" : "Start listening";
        PauseButton.Content = _s.TesterPaused ? "Resume" : "Pause";
        StartSessionButton.Content = _s.TestSessionStart.HasValue ? L.T("StopTest") : L.T("StartTest");
        ResetCountersButton.Content = L.T("ResetStats");

        var c = _s.TesterCounter.Snapshot();
        CountersText.Text = $"L:{c.GetValueOrDefault(MouseButton.Left)} R:{c.GetValueOrDefault(MouseButton.Right)} M:{c.GetValueOrDefault(MouseButton.Middle)} X1:{c.GetValueOrDefault(MouseButton.XButton1)} X2:{c.GetValueOrDefault(MouseButton.XButton2)} Total:{_s.TesterCounter.Total}";
        var t = _s.TimingAnalyzer;
        TimingText.Text = $"Intervals: {t.Count} · Avg: {t.AverageMs:F0} ms · Min: {t.MinMs} · Max: {t.MaxMs} · Threshold: {t.ClassificationThresholdMs} ms · Suspicious: {_s.SuspiciousRapidClicks}";
        ScrollText.Text = $"Scroll events: {_s.ScrollEvents} · Total delta: {_s.ScrollDeltaTotal} · Last: {_s.LastScrollDelta}";

        if (_s.TestSessionStart.HasValue)
            SessionText.Text = $"Session running: {DateTimeOffset.Now - _s.TestSessionStart.Value:hh\\:mm\\:ss}";
        else if (_lastSummary != null)
            SessionText.Text = _lastSummary;
        else
            SessionText.Text = "No active session.";

        // Bounded event list: rebuild only when new events arrived.
        var hist = _s.GetEventHistorySnapshot();
        if (hist.Count != _lastEventCount)
        {
            _lastEventCount = hist.Count;
            EventsList.Items.Clear();
            foreach (var r in hist.TakeLast(120))
            {
                string flag = r.SuppressedByFilter ? "[SUPPRESSED] " : "";
                string warn = (!r.SuppressedByFilter && r.MsSincePrevious > 0 && r.MsSincePrevious < _s.Settings.TesterDiagnosticThresholdMs && r.Type == ButtonEventType.Down) ? " (!rapid)" : "";
                EventsList.Items.Add($"{flag}{r.WallTime:HH:mm:ss.fff} {r.Button} {r.Type} +{r.MsSincePrevious}ms{warn}");
            }
            if (EventsList.Items.Count > 0)
                EventsList.ScrollIntoView(EventsList.Items[^1]);
        }

        // Diagram press state from recent downs/ups (global hook, polled).
        // Light up buttons currently held: derive from last down/up per button.
        _pressed.Clear();
        var seen = new HashSet<MouseButton>();
        for (int i = hist.Count - 1; i >= 0 && seen.Count < 5; i--)
        {
            var r = hist[i];
            if (seen.Contains(r.Button)) continue;
            seen.Add(r.Button);
            if (r.Type == ButtonEventType.Down && !r.SuppressedByFilter)
                _pressed.Add(r.Button);
        }
        PaintDiagram();
    }

    private string? _lastSummary;

    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_s.SetTesterListening(!_s.TesterListening, out var err) && err != null)
            MessageBox.Show(err, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _s.TesterPaused = !_s.TesterPaused;
        _s.NotifyChanged();
    }

    private void StartSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_s.TestSessionStart.HasValue)
        {
            var sum = _s.BuildSessionSummary();
            _lastSummary = sum.ToClipboardText();
            _s.TestSessionStart = null;
        }
        else
        {
            _s.TestSessionStart = DateTimeOffset.Now;
            _lastSummary = null;
            if (!_s.TesterListening)
                SetTesterListeningSilent();
        }
        _s.NotifyChanged();
    }

    private void SetTesterListeningSilent()
    {
        _s.SetTesterListening(true, out _);
    }

    private void CopySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_s.BuildSessionSummary().ToClipboardText()); } catch { }
    }

    private void ResetCountersButton_Click(object sender, RoutedEventArgs e)
    {
        _s.TesterCounter.Reset();
        _s.TimingAnalyzer.Reset();
        _s.ScrollEvents = 0;
        _s.SuspiciousRapidClicks = 0;
        _s.ClearEventHistory();
        _lastSummary = null;
        _s.NotifyChanged();
    }

    private void ClearEventsButton_Click(object sender, RoutedEventArgs e) => _s.ClearEventHistory();
}
