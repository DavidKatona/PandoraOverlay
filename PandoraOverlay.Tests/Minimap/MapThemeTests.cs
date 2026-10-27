using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The maps on the theme's tokens (the themes' step 4): the trail's casing
/// draws nothing at all until a theme gives it a colour, the names' cache
/// is forgotten on a switch, and every theme keeps the maps' rules. The
/// pixels themselves are the golden renders' (outside the repo).
/// </summary>
public class MapThemeTests
{
    private static ThemeColor C(string hex) => ThemeColor.Parse(hex);

    // ---- The trail's casing (PLAN.md question 26) ------------------------------------

    [Fact]
    public void ATransparentCasingDrawsNothing()
    {
        Assert.Null(MapCanvas.CasingWidth(ThemeColor.None, 2));
        Assert.Null(MapCanvas.CasingWidth(C("#00FFFFFF"), 2)); // any colour at alpha 0 is none
    }

    [Fact]
    public void ACasingIsTheTrailPlusAPixelASide()
    {
        Assert.Equal(4, MapCanvas.CasingWidth(C("#000000"), 2));
        Assert.Equal(4, MapCanvas.CasingWidth(C("#40000000"), 2)); // a faint casing is still drawn
        Assert.Equal(5, MapCanvas.CasingWidth(C("#000000"), 3));
    }

    /// <summary>No bundled theme has a casing yet (it is the Clear theme's, not bundled), so the trail is drawn exactly as in 1.38.</summary>
    [Fact]
    public void NoBundledThemeHasACasingYet()
    {
        foreach (var theme in Theme.All)
            Assert.Null(MapCanvas.CasingWidth(theme.Tokens.Map.You.TrailOutline, 2));
    }

    // ---- The names' cache --------------------------------------------------------------

    [Fact]
    public void ANameIsPreparedOnceAndForgottenOnASwitch()
    {
        NameLayer.ForgetPrepared();
        Assert.Equal(0, NameLayer.PreparedCount);

        var brush = DefaultTheme.Tokens.Map.Name.ToBrush();
        var first = NameLayer.Measure("Swamps", 12, false, brush, 1);
        var again = NameLayer.Measure("Swamps", 12, false, brush, 1);
        Assert.Equal(first, again);
        Assert.Equal(1, NameLayer.PreparedCount); // the second ask reused the first drawing

        NameLayer.Measure("Swamps", 12, true, brush, 1); // another weight is another drawing
        Assert.Equal(2, NameLayer.PreparedCount);

        NameLayer.ForgetPrepared();
        Assert.Equal(0, NameLayer.PreparedCount);
        Assert.Equal(first, NameLayer.Measure("Swamps", 12, false, brush, 1)); // prepared afresh, the same box
    }

    /// <summary>Default's arrow has no glow: the painted glow (PaintedGlow) draws nothing at a radius of 0.</summary>
    [Fact]
    public void DefaultsArrowHasNoGlow()
    {
        Assert.Equal(0, DefaultTheme.Tokens.Glow.You);
        Assert.Equal(ThemeColor.None, DefaultTheme.Tokens.Map.You.Glow);
    }

    /// <summary>The snap guides keep two colours apart in every theme, or a drag could not tell the screen from a panel.</summary>
    [Fact]
    public void EveryThemeTellsItsTwoSnapGuidesApart()
    {
        foreach (var theme in Theme.All)
            Assert.NotEqual(theme.Tokens.Map.SnapGuide.Screen, theme.Tokens.Map.SnapGuide.Peer);
    }

    [Fact]
    public void TheMapsDataColoursAreNoThemes()
    {
        // The picture's sea and the area borders' navy (MapBrushes): the same in every theme.
        Assert.Equal(Color.FromRgb(0x0A, 0x1D, 0x45), ((SolidColorBrush)MapBrushes.Sea).Color);
        Assert.Equal(Color.FromRgb(0x00, 0x14, 0x30), ((SolidColorBrush)MapBrushes.AreaBorders).Color);
        Assert.True(MapBrushes.Sea.IsFrozen && MapBrushes.AreaBorders.IsFrozen);
    }
}
