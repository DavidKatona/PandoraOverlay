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

    // ---- Text sizes through the fit factor (FIT.md section 5) ----------------------

    // An element or a Setter, from its < to its >, comments left out.
    private static readonly Regex XamlElement = new(@"<(?!!--)[^<>]*>", RegexOptions.Compiled);
    // A size written as a number: FontSize="11", or a Setter's Value="11" for FontSize.
    private static readonly Regex XamlSizeLiteral = new(@"FontSize=""[0-9]|Property=""FontSize""\s+Value=""[0-9]", RegexOptions.Compiled);
    private static readonly Regex XamlTitleFamily = new(@"FontFamily=""\{DynamicResource Theme\.Font\.TitleFamily\}""", RegexOptions.Compiled);
    private static readonly Regex XamlSizeKey = new(@"Theme\.Size\.[0-9][0-9.]*", RegexOptions.Compiled);
    // A size set in code as a number (FontSize = 11), not through Theme.Current.Size or a Theme.Size key.
    private static readonly Regex CodeSizeLiteral = new(@"\bFontSize\s*=\s*[0-9]", RegexOptions.Compiled);

    /// <summary>The repo's own XAML or code files (not the build's, not the tests').</summary>
    private static IEnumerable<string> RepoFiles(string extension)
    {
        var root = ThemeFileTests.RepoRoot();
        return Directory.EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => f.Split('/')[0] is not ("bin" or "obj" or "PandoraOverlay.Tests" or "publish" or "Releases" or ".git" or ".vs" or "tools"));
    }

    /// <summary>
    /// Every body text size is the theme's (the fit factor, FIT.md section 5):
    /// no FontSize written as a number in the repo's XAML, but beside the
    /// title family (a title keeps its size: FIT.md section 1), and none
    /// in its code (Theme.Current.Size(11), or the Theme.Size.11 key). A
    /// literal would keep its size whatever the theme's font, and a font
    /// held to the frames would no longer be.
    /// </summary>
    [Fact]
    public void EveryBodySizeIsTheThemes()
    {
        var root = ThemeFileTests.RepoRoot();
        var problems = new List<string>();
        var titles = 0;
        foreach (var file in RepoFiles(".xaml"))
        {
            foreach (Match element in XamlElement.Matches(File.ReadAllText(Path.Combine(root, file))))
            {
                if (!XamlSizeLiteral.IsMatch(element.Value)) continue;
                if (XamlTitleFamily.IsMatch(element.Value)) titles++;
                else problems.Add($"{file}: {Regex.Replace(element.Value, @"\s+", " ")}");
            }
        }
        foreach (var file in RepoFiles(".cs"))
            foreach (Match m in CodeSizeLiteral.Matches(File.ReadAllText(Path.Combine(root, file))))
                problems.Add($"{file}: {m.Value}");
        Assert.Empty(problems);
        Assert.True(titles > 0, "no title kept its size: the scan saw no title at all");
    }

    /// <summary>Every Theme.Size key the XAML and the code name is one a theme's dictionary holds (a typo is a size of WPF's default, 12, silently).</summary>
    [Fact]
    public void EverySizeKeyIsOneTheDictionaryHas()
    {
        var root = ThemeFileTests.RepoRoot();
        var dictionary = Theme.BuildDictionary(DefaultTheme.Tokens);
        var named = RepoFiles(".xaml").Concat(RepoFiles(".cs"))
            .SelectMany(f => XamlSizeKey.Matches(File.ReadAllText(Path.Combine(root, f))).Select(m => (File: f, Key: m.Value.TrimEnd('.'))))
            .ToList();
        Assert.NotEmpty(named);
        Assert.Empty(named.Where(n => !dictionary.Contains(n.Key)).Select(n => $"{n.File}: {n.Key}"));
        // And code that reads a key builds it by Theme.SizeKey: the names are the list's.
        Assert.All(Theme.BodySizes, s => Assert.True(dictionary.Contains(Theme.SizeKey(s))));
    }

    /// <summary>The scan itself catches what it is for.</summary>
    [Fact]
    public void TheSizeScanFindsWhatItIsFor()
    {
        Assert.Matches(XamlSizeLiteral, """<TextBlock FontSize="11"/>""");
        Assert.Matches(XamlSizeLiteral, """<Setter Property="FontSize" Value="9.5"/>""");
        Assert.DoesNotMatch(XamlSizeLiteral, """<TextBlock FontSize="{DynamicResource Theme.Size.11}"/>""");
        Assert.DoesNotMatch(XamlSizeLiteral, """<Setter Property="FontSize" Value="{DynamicResource Theme.Size.12}"/>""");
        Assert.Matches(XamlTitleFamily, """<TextBlock FontSize="14" FontFamily="{DynamicResource Theme.Font.TitleFamily}"/>""");
        Assert.Matches(CodeSizeLiteral, "new TextBlock { FontSize = 10.5 }");
        Assert.DoesNotMatch(CodeSizeLiteral, "new TextBlock { FontSize = Theme.Current.Size(10.5) }");
        Assert.Equal(2, XamlElement.Matches("""<!-- a FontSize="11" in a comment --><A FontSize="1"/><B/>""").Count);
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
