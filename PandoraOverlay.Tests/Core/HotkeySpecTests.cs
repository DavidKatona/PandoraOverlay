using System.Windows.Input;
using Xunit;

namespace PandoraOverlay.Tests;

public class HotkeySpecTests
{
    [Theory]
    [InlineData("Ctrl+F8", ModifierKeys.Control, Key.F8)]
    [InlineData("ctrl+shift+m", ModifierKeys.Control | ModifierKeys.Shift, Key.M)]
    [InlineData("Alt+D1", ModifierKeys.Alt, Key.D1)]
    [InlineData("Win+Control+F12", ModifierKeys.Windows | ModifierKeys.Control, Key.F12)]
    public void ParsesValidCombos(string text, ModifierKeys mods, Key key)
    {
        var spec = HotkeySpec.TryParse(text);
        Assert.NotNull(spec);
        Assert.Equal(mods, spec!.Modifiers);
        Assert.Equal(key, spec.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F8")]              // no modifier — would swallow the key from the game
    [InlineData("Ctrl+")]           // no main key
    [InlineData("Ctrl+Shift")]      // modifiers only
    [InlineData("Ctrl+NotAKey")]    // unknown key name
    [InlineData("Ctrl+F8+M")]       // two main keys
    public void RejectsInvalidCombos(string? text) => Assert.Null(HotkeySpec.TryParse(text));

    [Fact]
    public void RoundTripsThroughToString()
    {
        var spec = new HotkeySpec(ModifierKeys.Control | ModifierKeys.Shift, Key.F8);
        Assert.Equal("Ctrl+Shift+F8", spec.ToString());
        Assert.Equal(spec, HotkeySpec.TryParse(spec.ToString()));
    }
}
