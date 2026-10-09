using WinForms = System.Windows.Forms;

namespace OpenClick.App.Services;

/// <summary>Native tray icon backed by WinForms NotifyIcon (no extra dependencies).</summary>
public sealed class TrayService : IDisposable
{
    private WinForms.NotifyIcon? _icon;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? ToggleRequested;
    public event Action? EnableRequested;
    public event Action? DisableRequested;
    public event Action? TesterRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = "Open Click",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application,
        };
        _icon.DoubleClick += (_, __) => OpenRequested?.Invoke();
        RebuildMenu(false, "Filter Off");
    }

    public void RebuildMenu(bool filterOn, string profileName)
    {
        if (_icon == null) return;
        var loc = AppState.Current.Localization;
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(loc.T("TrayOpen"), null, (_, __) => OpenRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(filterOn ? loc.T("DisableFilter") : loc.T("EnableFilter"), null,
            (_, __) => { if (filterOn) DisableRequested?.Invoke(); else EnableRequested?.Invoke(); });
        menu.Items.Add(loc.T("TrayToggle"), null, (_, __) => ToggleRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var profileItem = new WinForms.ToolStripMenuItem($"Profile: {profileName}") { Enabled = false };
        menu.Items.Add(profileItem);
        menu.Items.Add(loc.T("TrayTester"), null, (_, __) => TesterRequested?.Invoke());
        menu.Items.Add(loc.T("TraySettings"), null, (_, __) => SettingsRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(loc.T("TrayExit"), null, (_, __) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.Text = filterOn ? "Open Click - filter ON" : "Open Click - filter OFF";
        _icon.Icon = filterOn ? System.Drawing.SystemIcons.Shield : System.Drawing.SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text)
    {
        try { _icon?.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.Info); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
        catch { }
    }
}
