namespace OpenClick.App.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        // Pack URI resolved against the local assembly (assembly name: OpenClick).
        var uri = new Uri($"pack://application:,,,/Themes/{(theme == "light" ? "Light" : "Dark")}.xaml", UriKind.Absolute);
        var dict = new System.Windows.ResourceDictionary { Source = uri };
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(dict);
    }
}
