using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PandoraOverlay;

/// <summary>
/// The global edit-mode hotkey: converts between the config string
/// ("Ctrl+F3") and what Win32 RegisterHotKey wants. WPF's ModifierKeys flag
/// values equal the Win32 MOD_* flags, so the cast is direct. Also hosts the
/// RegisterHotKey/UnregisterHotKey p/invokes shared by MainWindow (the real
/// registration) and SettingsWindow (the availability test during capture).
/// </summary>
public sealed record HotkeySpec(ModifierKeys Modifiers, Key Key)
{
    public static HotkeySpec Default { get; } = new(ModifierKeys.Control, Key.F3);

    // Without MOD_NOREPEAT, holding the combo past the key-repeat delay fires
    // the hotkey twice — edit mode toggles on and instantly off, which reads
    // as "the hotkey didn't work".
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static bool Register(IntPtr hwnd, int id, HotkeySpec spec) =>
        RegisterHotKey(hwnd, id, (uint)spec.Modifiers | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(spec.Key));

    public static bool Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

    /// <summary>
    /// "Ctrl+Shift+F8" → spec. Null when unusable: no modifier (a bare global
    /// hotkey would swallow that key from the game), or an unknown key name.
    /// </summary>
    public static HotkeySpec? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var mods = ModifierKeys.None;
        var key = Key.None;
        foreach (var token in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (key != Key.None || !Enum.TryParse(token, ignoreCase: true, out key) || key == Key.None)
                        return null;
                    break;
            }
        }
        return mods == ModifierKeys.None || key == Key.None ? null : new HotkeySpec(mods, key);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
