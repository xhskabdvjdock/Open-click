using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClick.Core.Profiles;

/// <summary>Named filter configuration.</summary>
public sealed class FilterProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom";
    public FilterConfig Config { get; set; } = new();
    public string? ToggleHotkey { get; set; }
    public bool IsBuiltIn { get; set; }

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Profile name must not be empty.");
        else if (Name.Length > 64)
            errors.Add("Profile name is too long (max 64).");
        errors.AddRange(Config?.Validate() ?? ["Missing filter configuration."]);
        return errors;
    }

    public FilterProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Config = Config?.Clone() ?? new FilterConfig(),
        ToggleHotkey = ToggleHotkey,
        IsBuiltIn = IsBuiltIn,
    };
}

public sealed class ProfileManager
{
    private readonly string _directory;
    private readonly Dictionary<string, FilterProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string? ActiveProfileId { get; private set; }
    public string? DefaultProfileId { get; private set; }

    public ProfileManager(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public IReadOnlyList<FilterProfile> List()
    {
        lock (_profiles)
            return _profiles.Values.Select(p => p.Clone()).OrderBy(p => p.Name).ToList();
    }

    public void Load()
    {
        lock (_profiles)
        {
            _profiles.Clear();
            foreach (var file in Directory.GetFiles(_directory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var p = JsonSerializer.Deserialize<FilterProfile>(json, JsonOpts);
                    if (p == null) continue;
                    if (p.Validate().Count > 0) continue; // skip invalid, don't crash
                    if (string.IsNullOrWhiteSpace(p.Id))
                        p.Id = Path.GetFileNameWithoutExtension(file);
                    _profiles[p.Id] = p;
                }
                catch { /* malformed file: skip, never crash startup */ }
            }
            if (_profiles.Count == 0)
            {
                foreach (var p in BuiltInProfiles())
                    _profiles[p.Id] = p;
                SaveAll();
            }
        }
    }

    public void SaveAll()
    {
        lock (_profiles)
        {
            foreach (var p in _profiles.Values)
            {
                var path = Path.Combine(_directory, SanitizeFileName(p.Id) + ".json");
                WriteSafe(path, JsonSerializer.Serialize(p, JsonOpts));
            }
        }
    }

    public FilterProfile Create(string name, FilterConfig config)
    {
        var p = new FilterProfile { Name = name, Config = config.Clone() };
        var errors = p.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
        lock (_profiles) _profiles[p.Id] = p.Clone();
        Persist(p);
        return p.Clone();
    }

    public void Rename(string id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("Name must not be empty.");
        lock (_profiles)
        {
            if (!_profiles.TryGetValue(id, out var p)) throw new KeyNotFoundException("Profile not found.");
            p.Name = newName.Trim();
            var errors = p.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
            Persist(p);
        }
    }

    public FilterProfile Duplicate(string id)
    {
        lock (_profiles)
        {
            if (!_profiles.TryGetValue(id, out var p)) throw new KeyNotFoundException("Profile not found.");
            var copy = p.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = p.Name + " (Copy)";
            copy.IsBuiltIn = false;
            _profiles[copy.Id] = copy;
            Persist(copy);
            return copy.Clone();
        }
    }

    public void Delete(string id)
    {
        lock (_profiles)
        {
            if (!_profiles.TryGetValue(id, out var p)) throw new KeyNotFoundException("Profile not found.");
            if (p.IsBuiltIn) throw new InvalidOperationException("Built-in profiles cannot be deleted.");
            _profiles.Remove(id);
            var path = Path.Combine(_directory, SanitizeFileName(id) + ".json");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    public FilterProfile? Get(string id)
    {
        lock (_profiles)
            return _profiles.TryGetValue(id, out var p) ? p.Clone() : null;
    }

    public void SetActive(string id)
    {
        lock (_profiles)
        {
            if (!_profiles.ContainsKey(id)) throw new KeyNotFoundException("Profile not found.");
            ActiveProfileId = id;
        }
    }

    public void SetDefault(string id)
    {
        lock (_profiles)
        {
            if (!_profiles.ContainsKey(id)) throw new KeyNotFoundException("Profile not found.");
            DefaultProfileId = id;
        }
    }

    /// <summary>Treat imported JSON as untrusted: validate, regenerate Id if colliding, never execute anything.</summary>
    public FilterProfile Import(string json)
    {
        FilterProfile? p;
        try { p = JsonSerializer.Deserialize<FilterProfile>(json, JsonOpts); }
        catch (Exception ex) { throw new ArgumentException("Invalid profile file: " + ex.Message); }
        if (p == null) throw new ArgumentException("Invalid profile file.");
        p.IsBuiltIn = false;
        var errors = p.Validate();
        if (errors.Count > 0) throw new ArgumentException("Invalid profile: " + string.Join("; ", errors));
        lock (_profiles)
        {
            if (_profiles.ContainsKey(p.Id))
                p.Id = Guid.NewGuid().ToString("N");
            _profiles[p.Id] = p.Clone();
            Persist(p);
            return p.Clone();
        }
    }

    public string Export(string id)
    {
        lock (_profiles)
        {
            if (!_profiles.TryGetValue(id, out var p)) throw new KeyNotFoundException("Profile not found.");
            return JsonSerializer.Serialize(p, JsonOpts);
        }
    }

    public static List<FilterProfile> BuiltInProfiles() => new()
    {
        new FilterProfile { Id = "builtin-off", Name = "Filter Off", IsBuiltIn = true,
            Config = new FilterConfig { Enabled = false, IntervalMs = 0, Mode = FilterMode.PerButtonDebounce } },
        new FilterProfile { Id = "builtin-gentle", Name = "Gentle Protection", IsBuiltIn = true,
            Config = new FilterConfig { Enabled = true, IntervalMs = 50, Mode = FilterMode.PerButtonDebounce,
                EnabledButtons = [MouseButton.Left, MouseButton.Right, MouseButton.Middle] } },
        new FilterProfile { Id = "builtin-standard", Name = "Standard Protection", IsBuiltIn = true,
            Config = new FilterConfig { Enabled = true, IntervalMs = 100, Mode = FilterMode.PerButtonDebounce,
                EnabledButtons = [MouseButton.Left, MouseButton.Right, MouseButton.Middle] } },
        new FilterProfile { Id = "builtin-strict", Name = "Strict Protection", IsBuiltIn = true,
            Config = new FilterConfig { Enabled = true, IntervalMs = 200, Mode = FilterMode.PerButtonDebounce,
                EnabledButtons = [MouseButton.Left, MouseButton.Right, MouseButton.Middle] } },
        new FilterProfile { Id = "builtin-leftonly", Name = "Left Button Only", IsBuiltIn = true,
            Config = new FilterConfig { Enabled = true, IntervalMs = 100, Mode = FilterMode.LeftOnly,
                EnabledButtons = [MouseButton.Left] } },
        new FilterProfile { Id = "builtin-custom", Name = "Custom", IsBuiltIn = true,
            Config = new FilterConfig { Enabled = false, IntervalMs = 150, Mode = FilterMode.SelectedButtons,
                EnabledButtons = [MouseButton.Left] } },
    };

    private void Persist(FilterProfile p)
    {
        var path = Path.Combine(_directory, SanitizeFileName(p.Id) + ".json");
        WriteSafe(path, JsonSerializer.Serialize(p, JsonOpts));
    }

    private static void WriteSafe(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
