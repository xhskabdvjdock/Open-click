using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using OpenClick.App.Input;
using OpenClick.App.Services;
using OpenClick.App.Views;
using OpenClick.Core;

namespace OpenClick.App;

public partial class MainWindow : Window
{
    private readonly AppState _s = AppState.Current;
    private readonly TrayService _tray = new();
    private readonly UserControl[] _pages;
    private int _page;
    private int? _toggleHotkeyId;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        _pages = [new DashboardView(), new FilterView(), new TesterView(), new ProfilesView(), new SettingsView()];
        PageHost.Content = _pages[0];

        _s.StateChanged += Refresh;
        _s.Localization.LanguageChanged += ApplyLanguage;
        _s.Hotkeys.RegistrationFailed += msg => Dispatcher.Invoke(() =>
            MessageBox.Show(msg, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning));

        _tray.Initialize();
        _tray.OpenRequested += () => Dispatcher.Invoke(() => ShowAndActivate());
        _tray.ToggleRequested += () => Dispatcher.Invoke(() => ToggleFilter());
        _tray.EnableRequested += () => Dispatcher.Invoke(() => SetFilter(true));
        _tray.DisableRequested += () => Dispatcher.Invoke(() => SetFilter(false));
        _tray.TesterRequested += () => Dispatcher.Invoke(() => { ShowAndActivate(); Navigate(2); });
        _tray.SettingsRequested += () => Dispatcher.Invoke(() => { ShowAndActivate(); Navigate(4); });
        _tray.ExitRequested += () => Dispatcher.Invoke(() => ExitApp());

        Loaded += (_, __) => AfterLoaded();
        Closing += OnClosing;

        ApplyLanguage();
        Refresh();
    }

    private void AfterLoaded()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _s.Hotkeys.AttachHwnd(hwnd);
        var src = HwndSource.FromHwnd(hwnd);
        src?.AddHook(WndProc);

        _toggleHotkeyId = _s.Hotkeys.Register(_s.Settings.ToggleHotkey, ToggleFilter, "toggle");
        if (!string.IsNullOrWhiteSpace(_s.Settings.BypassHotkey))
            _s.Hotkeys.Register(_s.Settings.BypassHotkey, () => _s.TemporaryBypass(TimeSpan.FromSeconds(10)), "bypass");

        // Apply persisted opt-in startup activation (default OFF on first install).
        if (_s.Settings.Filter.Enabled)
        {
            if (!_s.SetFilterEnabled(true, out var err) && err != null)
                MessageBox.Show(err, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (_s.Settings.StartMinimizedToTray && WasLaunchedMinimized())
        {
            Hide();
        }
    }

    private static bool WasLaunchedMinimized() =>
        Environment.GetCommandLineArgs().Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            if (_s.Hotkeys.HandleHotkeyMessage(wParam))
                handled = true;
        }
        return IntPtr.Zero;
    }

    private void ApplyLanguage()
    {
        var L = _s.Localization;
        AppTitle.Text = L.T("AppTitle");
        NavDashboard.Content = L.T("Dashboard");
        NavFilter.Content = L.T("ClickFilter");
        NavTester.Content = L.T("MouseTester");
        NavProfiles.Content = L.T("Profiles");
        NavSettings.Content = L.T("Settings");
        HeaderBypass.Content = L.T("BypassActive") + " (10s)";
        FlowDirection = L.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FontFamily = new System.Windows.Media.FontFamily(L.FontFamilyName);
        RefreshTray();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        var L = _s.Localization;
        var cfg = _s.Engine.GetConfig();
        bool on = cfg.Enabled && _s.Hook.IsRunning;
        bool err = _s.LastHookError != null;
        HeaderDot.Fill = err ? (System.Windows.Media.Brush)FindResource("DangerBrush") : (on ? (System.Windows.Media.Brush)FindResource("AccentBrush") : (System.Windows.Media.Brush)FindResource("MutedBrush"));
        HeaderStatus.Text = err ? L.T("FilterError") : (on ? $"{L.T("FilterEnabled")} · {cfg.Mode.ToDisplayName()} · {cfg.IntervalMs}{L.T("Ms")}" : L.T("FilterDisabled"));
        HeaderToggle.Content = on ? L.T("DisableFilter") : L.T("EnableFilter");
        HighlightNav();
        RefreshTray();
    }

    private void RefreshTray()
    {
        try
        {
            var cfg = _s.Engine.GetConfig();
            bool on = cfg.Enabled && _s.Hook.IsRunning;
            var active = _s.Settings.ActiveProfileId != null ? _s.Profiles.Get(_s.Settings.ActiveProfileId)?.Name : null;
            _tray.RebuildMenu(on, active ?? "—");
        }
        catch { }
    }

    private void HighlightNav()
    {
        var acc = (System.Windows.Media.Brush)FindResource("PrimaryBrush");
        var transparent = System.Windows.Media.Brushes.Transparent;
        Button[] navs = [NavDashboard, NavFilter, NavTester, NavProfiles, NavSettings];
        for (int i = 0; i < navs.Length; i++)
            navs[i].Background = i == _page ? acc : transparent;
    }

    private void Navigate(int page)
    {
        _page = page;
        PageHost.Content = _pages[page];
        HighlightNav();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && int.TryParse(b.Tag?.ToString(), out int p))
            Navigate(p);
    }

    private void ToggleFilter()
    {
        var cfg = _s.Engine.GetConfig();
        SetFilter(!cfg.Enabled);
    }

    private void SetFilter(bool on)
    {
        if (!_s.SetFilterEnabled(on, out var err) && err != null)
            MessageBox.Show(err, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void HeaderToggle_Click(object sender, RoutedEventArgs e) => ToggleFilter();
    private void HeaderBypass_Click(object sender, RoutedEventArgs e) => _s.TemporaryBypass(TimeSpan.FromSeconds(10));

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting) return;
        if (_s.Settings.MinimizeToTray && !_s.Settings.CloseToTray)
        {
            // minimize instead
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
        if (_s.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloon("Open Click", "Running in the tray. Right-click the icon to exit.");
            return;
        }
        // else fall through to real exit
        ExitApp();
        e.Cancel = true;
    }

    private void ExitApp()
    {
        _isExiting = true;
        try { _tray.Dispose(); } catch { }
        try { _s.Shutdown(); } catch { }
        System.Windows.Application.Current.Shutdown();
    }

    public void ExternalExit() => ExitApp();
}
