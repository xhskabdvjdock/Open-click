namespace OpenClick.Core;

/// <summary>Mouse buttons supported by the filter and tester.</summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    XButton1 = 3,
    XButton2 = 4,
}

public enum ButtonEventType
{
    Down = 0,
    Up = 1,
}

/// <summary>Decision returned by the filter engine for a single event.</summary>
public enum FilterDecision
{
    Allow = 0,
    Suppress = 1,
}
