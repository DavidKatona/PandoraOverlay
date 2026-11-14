using System.Windows.Input;

namespace PandoraOverlay;

/// <summary>
/// The Games window's two closing keys, pure (G12, A14): Esc and a bare J
/// close it on their RELEASE, and both the press and the release are marked
/// handled, so the game sees neither (.claude/rules/shell.md: acted on at the
/// press, the focus went back to the game with the key still down, and The
/// Isle opened its menu on the release; a press left unhandled is handed to
/// whichever of our windows gets the focus next). J is fixed, as M is for the
/// map, whatever the Games hotkey is rebound to. The window's PreviewKeyDown
/// and PreviewKeyUp only call these and set <c>e.Handled</c>.
/// <para>
/// <b>A repeat is not a first press.</b> The window opens on Ctrl+J: when Ctrl
/// is let go before J, the J still held keeps repeating, now bare. Were a
/// bare repeat remembered, the J's release would close the window it had just
/// opened. So only a press made here (not a repeat) is remembered; a repeat
/// of the remembered key is still ours (handled, as its first press was), a
/// repeat of any other key is not.
/// </para>
/// <para>
/// A press with a modifier is never ours (the combos are the global hotkeys'
/// business), and nothing is remembered across a loss of activation
/// (<see cref="Forget"/>): a key released in another window was never ours to
/// close on.
/// </para>
/// </summary>
internal sealed class GamesKeys
{
    /// <summary>The closing key pressed here, awaiting its release; null when none.</summary>
    private Key? _down;

    /// <summary>A key went down (<paramref name="isRepeat"/>: the keyboard's auto-repeat). True: ours, mark the event handled.</summary>
    public bool Down(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!Closes(key) || modifiers != ModifierKeys.None) return false;
        if (isRepeat) return _down == key; // the J of Ctrl+J, repeating bare: not remembered, not ours
        _down = key;
        return true;
    }

    /// <summary>A key went up. True: ours, mark it handled AND close the window.</summary>
    public bool Up(Key key)
    {
        if (_down != key) return false;
        _down = null;
        return true;
    }

    /// <summary>The window lost activation: what was pressed here is no longer awaited.</summary>
    public void Forget() => _down = null;

    /// <summary>The two keys that close the window: Esc and J (G12).</summary>
    internal static bool Closes(Key key) => key is Key.Escape or Key.J;
}
