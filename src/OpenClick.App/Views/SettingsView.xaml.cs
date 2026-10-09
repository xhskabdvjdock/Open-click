using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using OpenClick.App.Services;
using OpenClick.Core.Settings;

namespace OpenClick.App.Views;

public partial class SettingsView : UserControl
{
    private readonly AppState _s = AppState.Current;
    private bool _loading;

    public SettingsView()
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
        TitleText.Text = L.T("Settings");
        AppearanceTitle.Text = L.T("Appearance");
        DarkButton.Content = L.T("DarkTheme");
        LightButton.Content = L.T("LightTheme");
        EnglishButton.Content = "English";
        ArabicButton.Content = "العربية";
        CompactCheck.Content = "Compact interface";
        HotkeyTitle.Text = "Filter & tester";
        ApplyHotkeyButton.Content = L.T("Save");
        ApplyTesterButton.Content = L.T("Save");
        StartEnabledCheck.Content = "Start with filtering enabled (opt-in)";
        ThresholdLabel.Text = "Double-click threshold (ms) + history limit";
        SystemTitle.Text = "System";
        StartWithWindowsCheck.Content = "Start with Windows (consent)";
        StartMinimizedCheck.Content = "Start minimized to tray";
        MinimizeToTrayCheck.Content = "Minimize to tray";
        CloseToTrayCheck.Content = "Close to tray";
        OpenFolderButton.Content = "Open settings folder";
        ExportLogsButton.Content = "Export diagnostic logs";
        ResetButton.Content = "Reset settings";
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        _loading = true;
        try
        {
            CompactCheck.IsChecked = _s.Settings.CompactMode;
            HotkeyBox.Text = _s.Settings.ToggleHotkey;
            StartEnabledCheck.IsChecked = _s.Settings.Filter.Enabled;
            ThresholdBox.Text = _s.Settings.TesterDoubleClickThresholdMs.ToString();
            HistoryBox.Text = _s.Settings.TesterHistoryLimit.ToString();
            StartWithWindowsCheck.IsChecked = StartupService.IsEnabled();
            StartMinimizedCheck.IsChecked = _s.Settings.StartMinimizedToTray;
            MinimizeToTrayCheck.IsChecked = _s.Settings.MinimizeToTray;
            CloseToTrayCheck.IsChecked = _s.Settings.CloseToTray;
        }
        finally { _loading = false; }
    }

    private void DarkButton_Click(object sender, RoutedEventArgs e)
    {
        _s.Settings.Theme = "dark";
        _s.SettingsService.Save();
        ThemeService.Apply("dark");
    }

    private void LightButton_Click(object sender, RoutedEventArgs e)
    {
        _s.Settings.Theme = "light";
        _s.SettingsService.Save();
        ThemeService.Apply("light");
    }

    private void EnglishButton_Click(object sender, RoutedEventArgs e)
    {
        _s.Settings.Language = "en";
        _s.SettingsService.Save();
        _s.Localization.SetLanguage("en");
    }

    private void ArabicButton_Click(object sender, RoutedEventArgs e)
    {
        _s.Settings.Language = "ar";
        _s.SettingsService.Save();
        _s.Localization.SetLanguage("ar");
    }

    private void CompactCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.CompactMode = CompactCheck.IsChecked == true;
        _s.SettingsService.Save();
    }

    private void ApplyHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _s.Settings.ToggleHotkey = HotkeyBox.Text.Trim();
        try { _s.SettingsService.Save(); MessageBox.Show("Hotkey saved. It applies after restart of the window.", "Open Click", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void StartEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // Opt-in startup activation preference (persisted; applied on next launch).
        _s.Settings.Filter.Enabled = StartEnabledCheck.IsChecked == true;
        _s.SettingsService.Save();
    }

    private void ApplyTesterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (int.TryParse(ThresholdBox.Text.Trim(), out int th)) _s.Settings.TesterDoubleClickThresholdMs = th;
            if (int.TryParse(HistoryBox.Text.Trim(), out int hl)) _s.Settings.TesterHistoryLimit = hl;
            _s.SettingsService.Update(_ => { });
            _s.TimingAnalyzer.ClassificationThresholdMs = _s.Settings.TesterDoubleClickThresholdMs;
            _s.NotifyChanged();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void StartWithWindowsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { StartupService.SetEnabled(StartWithWindowsCheck.IsChecked == true); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SimpleSave_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Settings.StartMinimizedToTray = StartMinimizedCheck.IsChecked == true;
        _s.Settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _s.Settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _s.SettingsService.Save();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start("explorer.exe", SettingsService.AppDataDir); } catch { }
    }

    private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "openclick-diagnostics.log", Filter = "Log (*.log)|*.log" };
            if (dlg.ShowDialog() == true)
            {
                var src = System.IO.Path.Combine(SettingsService.LogsDir, "openclick.log");
                System.IO.File.Copy(src, dlg.FileName, overwrite: true);
                MessageBox.Show("Logs exported.", "Open Click", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset all settings?", "Open Click", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _s.SettingsService.Reset();
            ThemeService.Apply(_s.Settings.Theme);
            _s.Localization.SetLanguage(_s.Settings.Language);
            _s.ApplyConfig(_s.Settings.Filter);
            Refresh();
        }
    }
}
