using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The windows on the theme's tokens (the plan's step 2): Default's frames
/// are the plain Border (ThemedBorder draws nothing of its own with Default's
/// numbers), code paints from Theme.Current through kept brushes, and the
/// converted windows hold no chrome literal any more. The pixels themselves
/// are the golden renders' windows mode (outside the repo).
/// </summary>
public class ThemedWindowsTests
{
    private static ThemeTokens D => DefaultTheme.Tokens;

    /// <summary>Every Default frame, locked, lit or the control panel's, is the Border's own drawing: identical to 1.38 by construction.</summary>
    [Fact]
    public void DefaultsFramesAreThePlainBorder()
    {
        foreach (var glow in new[] { D.Glow.Corners, D.Glow.Edit, D.Glow.ControlPanel })
            Assert.True(ThemedBorder.Plain(D.Cut.Widget, D.Lit.Width, D.Lit.Top, glow));
    }

    [Theory]
    [InlineData(10, 0, 0, 0)] // a cut
    [InlineData(0, 1.5, 0, 0)] // a lit corner line
    [InlineData(0, 0, 1.5, 0)] // a lit top line
    [InlineData(0, 0, 0, 3)] // a glow
    public void AnyCutLitLineOrGlowIsDrawnByTheFrame(double cut, double litWidth, double litTop, double glow) =>
        Assert.False(ThemedBorder.Plain(cut, litWidth, litTop, glow));

    [Fact]
    public void ACodeColourIsOneKeptFrozenBrush()
    {
        var gold = ThemeColor.Parse("#FFC864");
        var brush = gold.ToBrush();

        Assert.True(brush.IsFrozen);
        Assert.Equal(Color.FromRgb(0xFF, 0xC8, 0x64), brush.Color);
        Assert.Same(brush, gold.ToBrush());
        Assert.Equal(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), ThemeColor.Parse("#33FFFFFF").ToBrush().Color);
    }

    [Fact]
    public void AWeightIsWpfsOwn()
    {
        Assert.Equal(FontWeights.Normal, ThemeWeight.Normal.ToFontWeight());
        Assert.Equal(FontWeights.SemiBold, ThemeWeight.SemiBold.ToFontWeight());
        Assert.Equal(FontWeights.Bold, ThemeWeight.Bold.ToFontWeight());
    }

    // ---- No chrome literal in a converted window ---------------------------------

    /// <summary>
    /// The windows on the tokens so far; each later step adds its own
    /// (Settings and the cards in step 5). The maps' data colours (the sea,
    /// the area borders, the zone hues, the waypoint palette) live in
    /// MapBrushes and the scrim's black in ScrimWindow, never a theme's, so
    /// neither file is here.
    /// </summary>
    private static readonly string[] Converted =
    {
        "Shell/MainWindow.xaml", "Shell/MainWindow.Stats.cs",
        "Prime/PrimeWindow.xaml", "Prime/PrimeWindow.xaml.cs",
        "Activity/ActivityWindow.xaml", "Activity/ActivityWindow.xaml.cs",
        "Shell/ControlPanelWindow.xaml", "Shell/ControlPanelWindow.xaml.cs",
        "Shell/OverlayWindowBase.cs",
        "Settings/SettingsWindow.Themes.cs", // step 3's page: built in code, no literal of its own
        // Step 4, the maps:
        "Minimap/MinimapWindow.xaml", "Minimap/MinimapWindow.xaml.cs", "Minimap/MinimapWindow.Area.cs",
        "Minimap/MinimapWindow.Markers.cs", "Minimap/MinimapWindow.Friends.cs", "Minimap/MinimapWindow.Menu.cs",
        "Minimap/MapCanvas.xaml", "Minimap/MapCanvas.xaml.cs", "Minimap/MapCanvas.Markers.cs",
        "Minimap/NameLayer.cs", "Minimap/MapMenuBuilder.cs",
        "BigMap/BigMapWindow.xaml", "BigMap/BigMapWindow.xaml.cs", "BigMap/BigMapWindow.Layers.cs",
        "Shell/SnapGuideWindow.cs",
        "Themes/Controls.xaml", // the shared templates: the panel's, the map menus', the dialogs'
        // Step 5, the dialogs. The website's and Discord's own colours live in
        // Settings/SiteBrushes.cs (data, never a theme's), so it is not here.
        "Settings/SettingsWindow.xaml", "Settings/SettingsWindow.xaml.cs", "Settings/SettingsWindow.Apply.cs",
        "Settings/SettingsWindow.Account.cs", "Settings/SettingsWindow.About.cs",
        "Settings/SettingsWindow.Waypoints.cs", "Settings/SettingsWindow.WaypointPacks.cs", "Settings/SettingsWindow.Friends.cs",
        "Settings/SettingsWindow.Skins.cs", "Settings/SettingsWindow.SkinPictures.cs", "Settings/SettingsWindow.SkinFavourites.cs",
        "Settings/SettingsWindow.Storage.cs", "Settings/SettingsWindow.StorageDetail.cs", "Settings/SettingsWindow.Rules.cs",
        "Updates/UpdateCardWindow.xaml", "Updates/UpdateCardWindow.xaml.cs",
        "Account/SignInWindow.xaml", "Account/SignInWindow.xaml.cs",
    };

    private static readonly Regex XamlColour = new(@"=""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);
    private static readonly Regex XamlCorner = new(@"CornerRadius=""[0-9]", RegexOptions.Compiled);
    // A font named in XAML, but the two that are never a theme's (PLAN.md
    // section 5): the glyph font of the ✓ and ✕ icons, and the monospace of
    // the settings folder's path and the sign-in's code.
    private static readonly Regex XamlFont = new(@"FontFamily=""(?!\{|Segoe MDL2 Assets""|Consolas"")", RegexOptions.Compiled);
    private static readonly Regex CodeColour = new(@"Color\.From(A)?Rgb\(\s*0x", RegexOptions.Compiled);
    // A colour as three hex bytes in code, whatever the helper (the update card had Frozen(0x7B, 0x87, 0x90)).
    private static readonly Regex CodeBytes = new(@"\(\s*0x[0-9A-Fa-f]{2}\s*,\s*0x[0-9A-Fa-f]{2}\s*,\s*0x[0-9A-Fa-f]{2}", RegexOptions.Compiled);

    /// <summary>
    /// A colour, a corner or a font in a converted window is a token
    /// (.claude/rules/themes.md): no "#RRGGBB" or CornerRadius="10" in its
    /// XAML, no font named there, no Color.FromRgb(0x…) in its code. Data
    /// colours (a friend's) come from their own palettes, never a literal here.
    /// </summary>
    [Fact]
    public void TheConvertedWindowsHoldNoChromeLiteral()
    {
        var root = ThemeFileTests.RepoRoot();
        var problems = new List<string>();
        foreach (var file in Converted)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            var regexes = file.EndsWith(".xaml", StringComparison.Ordinal)
                ? new[] { XamlColour, XamlCorner, XamlFont }
                : new[] { CodeColour, CodeBytes };
            foreach (var regex in regexes)
                foreach (Match m in regex.Matches(text))
                    problems.Add($"{file}: {m.Value}");
        }
        Assert.Empty(problems);
    }

    /// <summary>The scan itself catches what it is for.</summary>
    [Fact]
    public void TheLiteralScanFindsWhatItIsFor()
    {
        Assert.Matches(XamlColour, """<Border Background="#CC10151B"/>""");
        Assert.Matches(XamlCorner, """<Border CornerRadius="10"/>""");
        Assert.Matches(XamlFont, """<Window FontFamily="Segoe UI"/>""");
        Assert.Matches(CodeColour, "new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90))");
        Assert.Matches(CodeBytes, "private static readonly Brush Soft = Frozen(0x7B, 0x87, 0x90);");
        Assert.DoesNotMatch(XamlColour, """<Border Background="{DynamicResource Theme.Glass.Widget}"/>""");
        Assert.DoesNotMatch(XamlFont, """<Window FontFamily="{DynamicResource Theme.Font.Family}"/>""");
        Assert.DoesNotMatch(XamlFont, """<TextBlock Text="&#xE73E;" FontFamily="Segoe MDL2 Assets"/>""");
        Assert.DoesNotMatch(XamlFont, """<TextBlock FontFamily="Consolas"/>""");
        Assert.Matches(XamlFont, """<TextBlock FontFamily="Consolas Bold"/>""");
        Assert.DoesNotMatch(CodeColour, "Color.FromArgb(alpha, glass.R, glass.G, glass.B)");
        Assert.DoesNotMatch(CodeBytes, "WaypointPalette.Wrap(0x01)");
    }
}
