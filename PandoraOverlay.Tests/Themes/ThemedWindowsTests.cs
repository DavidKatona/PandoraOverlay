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
    /// The windows on the tokens so far; each later step adds its own (the
    /// maps in step 4, Settings and the cards in step 5).
    /// </summary>
    private static readonly string[] Converted =
    {
        "Shell/MainWindow.xaml", "Shell/MainWindow.Stats.cs",
        "Prime/PrimeWindow.xaml", "Prime/PrimeWindow.xaml.cs",
        "Activity/ActivityWindow.xaml", "Activity/ActivityWindow.xaml.cs",
        "Shell/ControlPanelWindow.xaml", "Shell/ControlPanelWindow.xaml.cs",
        "Shell/OverlayWindowBase.cs",
    };

    private static readonly Regex XamlColour = new(@"=""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);
    private static readonly Regex XamlCorner = new(@"CornerRadius=""[0-9]", RegexOptions.Compiled);
    private static readonly Regex XamlFont = new(@"FontFamily=""[^{]", RegexOptions.Compiled);
    private static readonly Regex CodeColour = new(@"Color\.From(A)?Rgb\(\s*0x", RegexOptions.Compiled);

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
                : new[] { CodeColour };
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
        Assert.DoesNotMatch(XamlColour, """<Border Background="{DynamicResource Theme.Glass.Widget}"/>""");
        Assert.DoesNotMatch(XamlFont, """<Window FontFamily="{DynamicResource Theme.Font.Family}"/>""");
        Assert.DoesNotMatch(CodeColour, "Color.FromArgb(alpha, glass.R, glass.G, glass.B)");
    }
}
