using System.Windows.Input;

namespace OpenClick.App.Input;

/// <summary>Global hotkeys (toggle + temporary bypass) via RegisterHotKey.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private IntPtr _hwnd = IntPtr.Zero;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x100;
    private bool _disposed;

    public event Action<string>? RegistrationFailed;

    public void AttachHwnd(IntPtr hwnd) => _hwnd = hwnd;

    public bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_CONTROL;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_ALT;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_SHIFT;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_WIN;
            else return false;
        }
        var keyName = parts[^1];
        try
        {
            var key = (Key)Enum.Parse(typeof(Key), keyName, ignoreCase: true);
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }
        catch { return false; }
    }

    public int? Register(string hotkeyText, Action action, string purpose)
    {
        if (_hwnd == IntPtr.Zero) return null;
        if (!TryParse(hotkeyText, out uint mod, out uint vk)) return null;
        int id = _nextId++;
        try
        {
            if (!NativeMethods.RegisterHotKey(_hwnd, id, mod, vk))
            {
                RegistrationFailed?.Invoke($"Could not register the {purpose} hotkey ({hotkeyText}). It may be in use by another app.");
                return null;
            }
            _actions[id] = action;
            return id;
        }
        catch (Exception)
        {
            RegistrationFailed?.Invoke($"Could not register the {purpose} hotkey ({hotkeyText}).");
            return null;
        }
    }

    public void Unregister(int id)
    {
        _actions.Remove(id);
        if (_hwnd != IntPtr.Zero)
        {
            try { NativeMethods.UnregisterHotKey(_hwnd, id); } catch { }
        }
    }

    public bool HandleHotkeyMessage(IntPtr wParam)
    {
        int id = wParam.ToInt32();
        if (_actions.TryGetValue(id, out var a))
        {
            try { a(); } catch { }
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _actions.Keys.ToList())
        {
            try { if (_hwnd != IntPtr.Zero) NativeMethods.UnregisterHotKey(_hwnd, id); } catch { }
        }
        _actions.Clear();
    }
}
