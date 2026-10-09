using System.Windows;
using System.Windows.Threading;
using OpenClick.App.Services;

namespace OpenClick.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                AppState.Current.Logger?.Error("Unhandled exception.", args.ExceptionObject as Exception);
                // Fail safe: never leave the mouse hooked after a fatal error.
                AppState.Current.Engine?.SetEnabled(false);
                AppState.Current.Hook?.Stop();
            }
            catch { }
        };
        DispatcherUnhandledException += OnDispatcherException;
        try
        {
            AppState.Current.Initialize();
            ThemeService.Apply(AppState.Current.Settings.Theme);
        }
        catch (Exception ex)
        {
            try { AppState.Current.Logger?.Error("Startup failed: " + ex.ToString()); } catch { }
            MessageBox.Show("Startup failed: " + ex.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            AppState.Current.Logger?.Error("UI thread error: " + e.Exception.ToString());
            // Fail safe on input-related errors.
            AppState.Current.Engine?.SetEnabled(false);
        }
        catch { }
        MessageBox.Show("An error occurred: " + e.Exception.Message, "Open Click", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // keep app alive; filter is OFF (safe state)
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppState.Current.Shutdown(); } catch { }
        base.OnExit(e);
    }
}
