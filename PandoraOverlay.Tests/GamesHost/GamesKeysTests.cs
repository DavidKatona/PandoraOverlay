using System.Windows.Input;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's closing keys (G12, <see cref="GamesKeys"/>): Esc and a
/// bare J close on their release, both the press and the release handled; a
/// modifier makes neither ours; a repeat that was never pressed here (the J of
/// the Ctrl+J that opened the window, repeating bare once Ctrl is let go) is
/// not remembered, and its release closes nothing.
/// </summary>
public class GamesKeysTests
{
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.J)]
    public void EscAndJCloseOnTheirRelease(Key key)
    {
        var keys = new GamesKeys();

        Assert.True(keys.Down(key, ModifierKeys.None, isRepeat: false)); // handled, not closed
        Assert.True(keys.Down(key, ModifierKeys.None, isRepeat: true));  // its own repeat: handled too
        Assert.True(keys.Up(key));                                       // handled, and closes
        Assert.False(keys.Up(key));                                      // once
    }

    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Shift)]
    [InlineData(ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift)]
    public void AModifierMakesNeitherOurs(ModifierKeys modifiers)
    {
        var keys = new GamesKeys();

        Assert.False(keys.Down(Key.J, modifiers, isRepeat: false));
        Assert.False(keys.Down(Key.Escape, modifiers, isRepeat: false));
        Assert.False(keys.Up(Key.J));
        Assert.False(keys.Up(Key.Escape));
    }

    [Fact]
    public void TheHotkeysJRepeatingBareIsIgnoredAndItsReleaseDoesNotClose()
    {
        var keys = new GamesKeys();

        Assert.False(keys.Down(Key.J, ModifierKeys.Control, isRepeat: false)); // Ctrl+J: the hotkey's, it opened the window
        Assert.False(keys.Down(Key.J, ModifierKeys.Control, isRepeat: true));
        Assert.False(keys.Down(Key.J, ModifierKeys.None, isRepeat: true));     // Ctrl let go, J still repeating
        Assert.False(keys.Down(Key.J, ModifierKeys.None, isRepeat: true));
        Assert.False(keys.Up(Key.J));                                          // the window stays

        Assert.True(keys.Down(Key.J, ModifierKeys.None, isRepeat: false));     // a real press now
        Assert.True(keys.Up(Key.J));
    }

    [Fact]
    public void OtherKeysAreNeverOurs()
    {
        var keys = new GamesKeys();

        Assert.False(keys.Down(Key.M, ModifierKeys.None, isRepeat: false));
        Assert.False(keys.Down(Key.Space, ModifierKeys.None, isRepeat: false));
        Assert.False(keys.Up(Key.M));
        Assert.True(keys.Down(Key.Escape, ModifierKeys.None, isRepeat: false));
        Assert.False(keys.Up(Key.J)); // not the key pressed here
        Assert.True(keys.Up(Key.Escape));
    }

    [Fact]
    public void ForgetDropsWhatWasPressedHere()
    {
        var keys = new GamesKeys();

        Assert.True(keys.Down(Key.Escape, ModifierKeys.None, isRepeat: false));
        keys.Forget(); // the window lost activation with Esc down
        Assert.False(keys.Up(Key.Escape));
        Assert.False(keys.Down(Key.Escape, ModifierKeys.None, isRepeat: true)); // nor is its repeat ours now
    }
}
