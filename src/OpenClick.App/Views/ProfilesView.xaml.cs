using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenClick.App.Services;
using OpenClick.Core;

namespace OpenClick.App.Views;

public partial class ProfilesView : UserControl
{
    private readonly AppState _s = AppState.Current;

    public ProfilesView()
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
        TitleText.Text = L.T("Profiles");
        SwitchButton.Content = "Switch";
        DefaultButton.Content = L.T("SetDefault");
        CreateButton.Content = L.T("Create");
        RenameButton.Content = L.T("Rename");
        DuplicateButton.Content = L.T("Duplicate");
        DeleteButton.Content = L.T("Delete");
        ImportButton.Content = L.T("Import");
        ExportButton.Content = L.T("Export");
        NameBox.Text = "";
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        var selected = (ProfileList.SelectedItem as string)?.Split('|')[0].Trim();
        ProfileList.Items.Clear();
        foreach (var p in _s.Profiles.List())
        {
            string mark = p.Id == _s.Settings.ActiveProfileId ? " ●" : "";
            ProfileList.Items.Add($"{p.Id} | {p.Name}{mark} — {p.Config.Mode.ToDisplayName()}, {p.Config.IntervalMs}ms, {(p.Config.Enabled ? "on" : "off")}");
        }
        if (selected != null)
        {
            for (int i = 0; i < ProfileList.Items.Count; i++)
                if ((ProfileList.Items[i] as string)?.StartsWith(selected) == true)
                    ProfileList.SelectedIndex = i;
        }
        DetailText.Text = "Ideal interval depends on your mouse, behavior, and app. No preset is universally correct. Switching validates settings and never leaves the hook half-initialized.";
    }

    private string? SelectedId()
    {
        var s = ProfileList.SelectedItem as string;
        return s?.Split('|')[0].Trim();
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedId();
        if (id == null) return;
        try
        {
            var p = _s.Profiles.Get(id);
            if (p == null) return;
            // Validate before applying; reset timing so no half-initialized hook state.
            var errors = p.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
            _s.Engine.ResetTiming();
            _s.ApplyConfig(p.Config);
            _s.Settings.ActiveProfileId = id;
            _s.SettingsService.Save();
            _s.Profiles.SetActive(id);
            // If the new config enables filtering, restart hook cleanly.
            if (p.Config.Enabled)
            {
                _s.Hook.Stop();
                if (!_s.Hook.Start(out var err))
                {
                    _s.Engine.SetEnabled(false);
                    MessageBox.Show(err, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                _s.SetFilterEnabled(false, out _);
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedId();
        if (id == null) return;
        _s.Profiles.SetDefault(id);
        MessageBox.Show("Default profile set.", "Open Click", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Custom" : NameBox.Text.Trim();
            _s.Profiles.Create(name, _s.Engine.GetConfig());
            Refresh();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedId();
        if (id == null || string.IsNullOrWhiteSpace(NameBox.Text)) return;
        try { _s.Profiles.Rename(id, NameBox.Text.Trim()); Refresh(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedId();
        if (id == null) return;
        try { _s.Profiles.Duplicate(id); Refresh(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedId();
        if (id == null) return;
        try { _s.Profiles.Delete(id); Refresh(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = System.IO.File.ReadAllText(dlg.FileName); // untrusted: validated inside
                _s.Profiles.Import(json);
                Refresh();
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedId();
        if (id == null) return;
        try
        {
            var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "profile.json" };
            if (dlg.ShowDialog() == true)
                System.IO.File.WriteAllText(dlg.FileName, _s.Profiles.Export(id));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
