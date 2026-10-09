using System.Text.Json.Serialization;

namespace OpenClick.Core;

/// <summary>
/// Filter configuration. IntervalMs==0 means disabled (pass-through).
/// Valid range: 0..2000 ms. Custom values 25..2000 recommended; presets include 50/100/150/200/300/500.
/// </summary>
public sealed class FilterConfig
{
    public const int MinIntervalMs = 0;
    public const int MaxIntervalMs = 2000;
    public static readonly int[] PresetsMs = [0, 50, 100, 150, 200, 300, 500];

    public bool Enabled { get; set; }
    public FilterMode Mode { get; set; } = FilterMode.PerButtonDebounce;
    public int IntervalMs { get; set; } = 100;

    /// <summary>Buttons participating in filtering (used by PerButton/Global/Selected modes).</summary>
    public HashSet<MouseButton> EnabledButtons { get; set; } = [MouseButton.Left, MouseButton.Right, MouseButton.Middle];

    [JsonIgnore]
    public bool IsEffectivelyEnabled => Enabled && IntervalMs > 0;

    public bool IsButtonFiltered(MouseButton button)
    {
        if (!IsEffectivelyEnabled)
            return false;
        return Mode switch
        {
            FilterMode.LeftOnly => button == MouseButton.Left,
            FilterMode.PerButtonDebounce => EnabledButtons.Contains(button),
            FilterMode.GlobalDebounce => EnabledButtons.Contains(button),
            FilterMode.SelectedButtons => EnabledButtons.Contains(button),
            _ => false,
        };
    }

    public static FilterConfig Disabled() => new() { Enabled = false, IntervalMs = 0, Mode = FilterMode.PerButtonDebounce };

    /// <summary>Validate. Returns list of error messages (empty = valid).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (IntervalMs < MinIntervalMs || IntervalMs > MaxIntervalMs)
            errors.Add($"Interval must be between {MinIntervalMs} and {MaxIntervalMs} ms.");
        if (Enabled && IntervalMs > 0)
        {
            if (Mode != FilterMode.LeftOnly && (EnabledButtons == null || EnabledButtons.Count == 0))
                errors.Add("Select at least one mouse button to filter.");
        }
        foreach (var b in EnabledButtons ?? Enumerable.Empty<MouseButton>())
        {
            if (!Enum.IsDefined(typeof(MouseButton), b))
                errors.Add($"Unknown mouse button: {(int)b}.");
        }
        if (!Enum.IsDefined(typeof(FilterMode), Mode))
            errors.Add("Unknown filtering mode.");
        return errors;
    }

    public bool IsValid(out List<string> errors)
    {
        errors = Validate();
        return errors.Count == 0;
    }

    public FilterConfig Clone() => new()
    {
        Enabled = Enabled,
        Mode = Mode,
        IntervalMs = IntervalMs,
        EnabledButtons = new HashSet<MouseButton>(EnabledButtons ?? []),
    };
}
