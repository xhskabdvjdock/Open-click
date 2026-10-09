using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClick.Core.Settings;

/// <summary>Versioned application settings (schema v1).</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "en";       // en | ar
    public string Theme { get; set; } = "dark";        // dark | light
    public bool CompactMode { get; set; }

    public FilterConfig Filter { get; set; } = new()
    {
        Enabled = false, // default OFF on first install (fail-safe)
        IntervalMs = 100,
        Mode = FilterMode.PerButtonDebounce,
        EnabledButtons = [MouseButton.Left, MouseButton.Right, MouseButton.Middle],
    };
    public string? ActiveProfileId { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    public int TesterDoubleClickThresholdMs { get; set; } = 500;
    public int TesterHistoryLimit { get; set; } = 300;
    public int TesterDiagnosticThresholdMs { get; set; } = 100;
    public bool DiagnosticHighlight { get; set; } = true;

    public string ToggleHotkey { get; set; } = "Ctrl+Alt+M";
    public string? BypassHotkey { get; set; }
    public bool TrayIndicator { get; set; } = true;

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Language != "en" && Language != "ar") errors.Add("Language must be 'en' or 'ar'.");
        if (Theme != "dark" && Theme != "light") errors.Add("Theme must be 'dark' or 'light'.");
        errors.AddRange(Filter?.Validate() ?? ["Missing filter config."]);
        if (TesterHistoryLimit is < 20 or > 2000) errors.Add("Tester history limit must be 20..2000.");
        if (TesterDoubleClickThresholdMs is < 50 or > 2000) errors.Add("Double-click threshold must be 50..2000 ms.");
        if (TesterDiagnosticThresholdMs is < 10 or > 2000) errors.Add("Diagnostic threshold must be 10..2000 ms.");
        return errors;
    }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClick");
    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public static string ProfilesDir => Path.Combine(AppDataDir, "profiles");
    public static string LogsDir => Path.Combine(AppDataDir, "logs");

    public AppSettings Current { get; private set; } = new();

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(LogsDir);
    }

    public AppSettings Load()
    {
        EnsureDirectories();
        if (!File.Exists(SettingsPath))
        {
            Current = new AppSettings();
            Save();
            return Current;
        }
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            if (parsed == null) throw new InvalidDataException("Empty settings.");
            Migrate(parsed);
            var errors = parsed.Validate();
            if (errors.Count > 0)
            {
                // Back up corrupt file, fall back to defaults (never crash).
                try { File.Copy(SettingsPath, SettingsPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", overwrite: true); } catch { }
                Current = new AppSettings();
            }
            else Current = parsed;
        }
        catch
        {
            try { File.Copy(SettingsPath, SettingsPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", overwrite: true); } catch { }
            Current = new AppSettings();
        }
        return Current;
    }

    private static void Migrate(AppSettings s)
    {
        if (s.SchemaVersion < 1) s.SchemaVersion = 1;
        s.Filter ??= new FilterConfig();
        s.Filter.EnabledButtons ??= [MouseButton.Left];
    }

    public void Save()
    {
        EnsureDirectories();
        Current.SchemaVersion = 1;
        var json = JsonSerializer.Serialize(Current, JsonOpts);
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, SettingsPath, overwrite: true);
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        var errors = Current.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
        Save();
    }

    public void Reset()
    {
        Current = new AppSettings();
        Save();
    }
}
