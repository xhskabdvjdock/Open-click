using System.Windows;
using System.Windows.Controls;
using OpenClick.App.Services;
using OpenClick.Core;

namespace OpenClick.App.Views;

public partial class FilterView : UserControl
{
    private readonly AppState _s = AppState.Current;
    private bool _loading;

    public FilterView()
    {
        InitializeComponent();
        ModeCombo.ItemsSource = Enum.GetValues<FilterMode>();
        PresetCombo.ItemsSource = FilterConfig.PresetsMs.Select(p => p == 0 ? "0 (Disabled)" : $"{p} ms").ToList();
        _s.StateChanged += Refresh;
        _s.Localization.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
        Refresh();
    }

    private void ApplyLanguage()
    {
        var L = _s.Localization;
        TitleText.Text = L.T("ClickFilter");
        EnabledCheck.Content = L.T("EnableFilter");
        ModeLabel.Text = L.T("Mode");
        IntervalLabel.Text = L.T("Interval") + " (25–2000 ms, 0 = disabled)";
        ApplyIntervalButton.Content = L.T("Save");
        WarnLong.Text = L.T("WarningLongInterval");
        DragNote.Text = L.T("DragNote");
        ButtonsTitle.Text = L.T("Mode");
        LeftCheck.Content = L.T("LeftButton");
        RightCheck.Content = L.T("RightButton");
        MiddleCheck.Content = L.T("MiddleButton");
        X1Check.Content = "XButton1";
        X2Check.Content = "XButton2";
        BypassLabel.Text = "Bypass processes (comma-separated, optional)";
        UipiNote.Text = L.T("UipiNote");
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        _loading = true;
        try
        {
            var cfg = _s.Engine.GetConfig();
            EnabledCheck.IsChecked = cfg.Enabled;
            ModeCombo.SelectedItem = cfg.Mode;
            CustomIntervalBox.Text = cfg.IntervalMs.ToString();
            LeftCheck.IsChecked = cfg.EnabledButtons.Contains(MouseButton.Left);
            RightCheck.IsChecked = cfg.EnabledButtons.Contains(MouseButton.Right);
            MiddleCheck.IsChecked = cfg.EnabledButtons.Contains(MouseButton.Middle);
            X1Check.IsChecked = cfg.EnabledButtons.Contains(MouseButton.XButton1);
            X2Check.IsChecked = cfg.EnabledButtons.Contains(MouseButton.XButton2);
            bool leftOnly = cfg.Mode == FilterMode.LeftOnly;
            LeftCheck.IsEnabled = RightCheck.IsEnabled = MiddleCheck.IsEnabled = X1Check.IsEnabled = X2Check.IsEnabled = !leftOnly;
            BypassProcessBox.Text = string.Join(", ", _s.Hook.BypassProcesses);
            HookErrorText.Visibility = _s.LastHookError != null ? Visibility.Visible : Visibility.Collapsed;
            HookErrorText.Text = _s.LastHookError ?? "";
        }
        finally { _loading = false; }
    }

    private void EnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = EnabledCheck.IsChecked == true;
        if (!_s.SetFilterEnabled(on, out var err) && err != null)
            MessageBox.Show(err, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Config_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            var cfg = _s.Engine.GetConfig();
            if (ModeCombo.SelectedItem is FilterMode m) cfg.Mode = m;
            cfg.EnabledButtons = new HashSet<MouseButton>();
            if (LeftCheck.IsChecked == true) cfg.EnabledButtons.Add(MouseButton.Left);
            if (RightCheck.IsChecked == true) cfg.EnabledButtons.Add(MouseButton.Right);
            if (MiddleCheck.IsChecked == true) cfg.EnabledButtons.Add(MouseButton.Middle);
            if (X1Check.IsChecked == true) cfg.EnabledButtons.Add(MouseButton.XButton1);
            if (X2Check.IsChecked == true) cfg.EnabledButtons.Add(MouseButton.XButton2);
            _s.ApplyConfig(cfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
        }
    }

    private void PresetCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetCombo.SelectedIndex < 0) return;
        try
        {
            var cfg = _s.Engine.GetConfig();
            cfg.IntervalMs = FilterConfig.PresetsMs[PresetCombo.SelectedIndex];
            _s.ApplyConfig(cfg);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ApplyIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(CustomIntervalBox.Text.Trim(), out int v))
            {
                MessageBox.Show("Enter a number 0–2000.", "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var cfg = _s.Engine.GetConfig();
            cfg.IntervalMs = v;
            _s.ApplyConfig(cfg);
            // Persist bypass list too.
            ApplyBypass();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ApplyBypass()
    {
        _s.Hook.BypassProcesses.Clear();
        foreach (var p in BypassProcessBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _s.Hook.BypassProcesses.Add(p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p);
    }
}
