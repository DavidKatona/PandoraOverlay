using System.Windows.Input;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The map's closing keys (<see cref="MapKeys"/>, G34): Esc and a bare M close
/// on their release, both the press and the release handled; a modifier makes
/// neither ours; a repeat that was never pressed here (the M of the Ctrl+M that
/// opened the map, repeating bare once Ctrl is let go) is not remembered, and its
/// release closes nothing.
/// </summary>
public class MapKeysTests
{
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.M)]
    public void EscAndMCloseOnTheirRelease(Key key)
    {
        var keys = new MapKeys();

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
        var keys = new MapKeys();

        Assert.False(keys.Down(Key.M, modifiers, isRepeat: false));
        Assert.False(keys.Down(Key.Escape, modifiers, isRepeat: false));
        Assert.False(keys.Up(Key.M));
        Assert.False(keys.Up(Key.Escape));
    }

    [Fact]
    public void TheHotkeysMRepeatingBareIsIgnoredAndItsReleaseDoesNotClose()
    {
        var keys = new MapKeys();

        // Ctrl+M opened the map; its M never reached the map. Ctrl let go, M still repeating:
        Assert.False(keys.Down(Key.M, ModifierKeys.None, isRepeat: true));
        Assert.False(keys.Down(Key.M, ModifierKeys.None, isRepeat: true));
        Assert.False(keys.Up(Key.M)); // the map stays

        Assert.True(keys.Down(Key.M, ModifierKeys.None, isRepeat: false)); // a real press now
        Assert.True(keys.Up(Key.M));
    }

    [Fact]
    public void OtherKeysAreNeverOurs()
    {
        var keys = new MapKeys();

        Assert.False(keys.Down(Key.J, ModifierKeys.None, isRepeat: false));
        Assert.False(keys.Down(Key.Space, ModifierKeys.None, isRepeat: false)); // the window's own: it centres, closes nothing
        Assert.False(keys.Up(Key.J));
        Assert.True(keys.Down(Key.Escape, ModifierKeys.None, isRepeat: false));
        Assert.False(keys.Up(Key.M)); // not the key pressed here
        Assert.True(keys.Up(Key.Escape));
    }
}
