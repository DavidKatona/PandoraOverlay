using System.Windows.Input;

namespace PandoraOverlay;

/// <summary>
/// The map's two closing keys, pure: Esc and a bare M close it on their RELEASE
/// (1.38: closed on the press, the release reached the game and The Isle opened
/// its menu), and both the press and the release are marked handled. A press
/// with a modifier is never ours (the combos are the global hotkeys' business).
/// The window's PreviewKeyDown and PreviewKeyUp only call these and set
/// <c>e.Handled</c>; Space, which closes nothing, stays the window's.
/// <para>
/// <b>A repeat is not a first press</b> (the owner, Nov 16 2026, G34; found by the
/// games run's review). The map opens on Ctrl+M: held past the keyboard's repeat
/// delay with Ctrl let go first, the M keeps repeating bare into the map, now
/// active, and its release closed the map it had just opened. So only a press made
/// here is remembered; a repeat of the remembered key is still ours, a repeat of
/// any other key is not. The same rule as the Games window's J (<see cref="GamesKeys"/>),
/// and the same reading of a repeat: Windows' own flag, bit 30 of the key message
/// (<see cref="GamesKeys.HeldBefore"/>, A53), since WPF's <c>IsRepeat</c> never saw
/// the M the hotkey took.
/// </para>
/// <para>
/// Nothing is forgotten on a loss of activation, unlike the Games window's keys:
/// the map closes when it loses activation.
/// </para>
/// </summary>
internal sealed class MapKeys
{
    /// <summary>The closing key pressed here, awaiting its release; null when none.</summary>
    private Key? _down;

    /// <summary>A key went down (<paramref name="isRepeat"/>: the keyboard's auto-repeat). True: ours, mark the event handled.</summary>
    public bool Down(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!Closes(key) || modifiers != ModifierKeys.None) return false;
        if (isRepeat) return _down == key; // the M of Ctrl+M, repeating bare: not remembered, not ours
        _down = key;
        return true;
    }

    /// <summary>A key went up. True: ours, mark it handled AND close the map.</summary>
    public bool Up(Key key)
    {
        if (_down != key) return false;
        _down = null;
        return true;
    }

    /// <summary>The two keys that close the map: Esc and M.</summary>
    internal static bool Closes(Key key) => key is Key.Escape or Key.M;
}
