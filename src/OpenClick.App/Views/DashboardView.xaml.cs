using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenClick.App.Services;
using OpenClick.Core;

namespace OpenClick.App.Views;

public partial class DashboardView : UserControl
{
    private readonly AppState _s = AppState.Current;

    public DashboardView()
    {
        InitializeComponent();
        _s.StateChanged += Refresh;
        _s.Localization.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
        Refresh();
    }

    private void ApplyLanguage()
    {
        var L = _s.Localization;
        TitleText.Text = L.T("Dashboard");
        StatsTitle.Text = "Session statistics";
        ObservedCap.Text = L.T("Observed") + " (downs)";
        SuppressedCap.Text = L.T("Suppressed") + " (downs)";
        AllowedCap.Text = L.T("Allowed") + " (downs)";
        ModeCap.Text = L.T("Mode");
        IntervalCap.Text = L.T("Interval") + " (ms)";
        ProfileCap.Text = L.T("ActiveProfile");
        ResetStatsButton.Content = L.T("ResetStats");
        ButtonsTitle.Text = "Buttons & tester";
        BypassButton.Content = L.T("BypassActive") + " (10s)";
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        var L = _s.Localization;
        var cfg = _s.Engine.GetConfig();
        bool on = cfg.Enabled && _s.Hook.IsRunning;
        bool err = _s.LastHookError != null;

        StatusLabel.Text = err ? L.T("FilterError") : (on ? L.T("FilterEnabled") : L.T("FilterDisabled"));
        StatusDot.Fill = err ? (System.Windows.Media.Brush)FindResource("DangerBrush") : (on ? (System.Windows.Media.Brush)FindResource("AccentBrush") : (System.Windows.Media.Brush)FindResource("MutedBrush"));
        StatusDetail.Text = $"{cfg.Mode.ToDisplayName()} · {cfg.IntervalMs} {L.T("Ms")}" + (_s.BypassActive ? $" · {L.T("BypassActive")}" : "");
        ToggleButton.Content = on ? L.T("DisableFilter") : L.T("EnableFilter");
        HookErrorText.Visibility = err ? Visibility.Visible : Visibility.Collapsed;
        HookErrorText.Text = _s.LastHookError ?? "";

        var snap = _s.Engine.GetSnapshot();
        ObservedVal.Text = snap.ObservedDowns.ToString();
        SuppressedVal.Text = snap.SuppressedDowns.ToString();
        AllowedVal.Text = snap.AllowedDowns.ToString();
        ModeVal.Text = cfg.Mode.ToDisplayName();
        IntervalVal.Text = cfg.IntervalMs.ToString();
        var active = _s.Settings.ActiveProfileId != null ? _s.Profiles.Get(_s.Settings.ActiveProfileId)?.Name : null;
        ProfileVal.Text = active ?? "—";

        var c = _s.TesterCounter.Snapshot();
        ButtonsDetail.Text = $"L:{c.GetValueOrDefault(Core.MouseButton.Left)}  R:{c.GetValueOrDefault(Core.MouseButton.Right)}  M:{c.GetValueOrDefault(Core.MouseButton.Middle)}  X1:{c.GetValueOrDefault(Core.MouseButton.XButton1)}  X2:{c.GetValueOrDefault(Core.MouseButton.XButton2)}";
        TesterDetail.Text = $"Tester total: {_s.TesterCounter.Total} · Scrolls: {_s.ScrollEvents} · Suspicious: {_s.SuspiciousRapidClicks}";
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _s.Engine.GetConfig();
        if (!_s.SetFilterEnabled(!cfg.Enabled, out var err) && err != null)
            MessageBox.Show(err, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BypassButton_Click(object sender, RoutedEventArgs e)
    {
        _s.TemporaryBypass(TimeSpan.FromSeconds(10));
    }

    private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => _s.ResetStatistics();
}
