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
/// <b>Windows says what a repeat is, not WPF</b> (A53). WPF's
/// <c>KeyEventArgs.IsRepeat</c> only compares the key with the last key WPF
/// itself saw go down, and forgets it on any key-up. The J of the Ctrl+J never
/// reached WPF (the hotkey took it), so the first bare repeat after Ctrl is let
/// go would arrive as a first press, be remembered, and its release would close
/// the window. The window therefore also asks <see cref="HeldBefore"/>: bit 30 of
/// the key message's lParam, Windows' "the key was already down" flag. It reads
/// our own window's own message (the one WPF is raising the event for): nothing
/// of the game, no hook.
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

    /// <summary>Windows' key-down message.</summary>
    internal const int KeyDownMessage = 0x0100;

    /// <summary>Windows' key-down message for a key pressed with Alt or F10 (a "system" key).</summary>
    internal const int SystemKeyDownMessage = 0x0104;

    /// <summary>A key message's lParam bit 30, the previous key state: set when the key was already down before this message.</summary>
    internal const long PreviousKeyStateBit = 1L << 30;

    /// <summary>
    /// The key message is a key-down whose key was already down: Windows'
    /// own repeat, whoever saw the first press (A53). False for anything
    /// else, an empty message included (a test's raised event).
    /// </summary>
    internal static bool HeldBefore(int message, long lParam) =>
        message is KeyDownMessage or SystemKeyDownMessage && (lParam & PreviousKeyStateBit) != 0;

    /// <summary>The two keys that close the window: Esc and J (G12).</summary>
    internal static bool Closes(Key key) => key is Key.Escape or Key.J;
}
