namespace OpenClick.Core;

/// <summary>
/// Filtering modes (spec section 6).
/// </summary>
public enum FilterMode
{
    /// <summary>Mode A: each button has its own independent timing window.</summary>
    PerButtonDebounce = 0,

    /// <summary>Mode B: shared interval across selected buttons. Optional; may interfere with multi-button use.</summary>
    GlobalDebounce = 1,

    /// <summary>Mode C: filter left-button downs only.</summary>
    LeftOnly = 2,

    /// <summary>Mode D: filter a user-selected set of buttons, each with its own window.</summary>
    SelectedButtons = 3,
}

public static class FilterModeExtensions
{
    public static string ToDisplayName(this FilterMode mode) => mode switch
    {
        FilterMode.PerButtonDebounce => "Per-Button Debounce",
        FilterMode.GlobalDebounce => "Global Debounce",
        FilterMode.LeftOnly => "Left-Click Only",
        FilterMode.SelectedButtons => "Selected Buttons",
        _ => mode.ToString(),
    };
}
