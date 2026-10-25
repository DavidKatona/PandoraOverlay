using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The themes as data (Themes/ThemeFile.cs, Theme.cs, DefaultTheme.cs): a
/// sparse file lays over Default and nothing else moves; a key Default lacks
/// or a value that doesn't parse is a failure, never a silent inheritance;
/// Default is today's look; every theme key the XAML reads names a token.
/// The bundled files are checked as the app loads them (Theme.All). These
/// read no file of the player's and need no Application.
/// </summary>
public class ThemeFileTests
{
    private static ThemeTokens D => DefaultTheme.Tokens;
    private static ThemeColor C(string hex) => ThemeColor.Parse(hex);

    // A sample theme of the tests' own (Isle's bundled file is checked further down).
    private const string Sample = """
        {
          // Comments and trailing commas are allowed, as in any theme file.
          "Name": "Sample",
          "Order": 3,
          "Tokens": {
            "Text": { "Title": "#B8E6EC" },
            "Glass": { "Widget": "#D1050B0E" },
            "Shapes": { "Radio": "hexagon", "Badge": "Hexagon", "ActivityBullet": "⬢ " },
            "Bars": {
              "Health": { "Below25": { "Fill": "#F06A5A", "Percent": "#FF9B8F" } },
              "Height": 8,
            },
            "Corner": { "Widget": 0 },
            "Font": { "Family": "Assets/Fonts/Montserrat/#Montserrat", "TitleWeight": "SemiBold" },
            "Map": { "WindowRounded": false },
          },
        }
        """;

    private static ThemeEntry Parse(string json) => ThemeFile.Parse("sample", json, D);

    /// <summary>A file with only the given tokens (the failure cases below).</summary>
    private static string WithTokens(string tokens) => $$"""{ "Name": "Sample", "Order": 1, "Tokens": {{tokens}} }""";

    // ---- A sparse file lays over Default ---------------------------------------

    [Fact]
    public void ASampleFileParsesAndLaysOverDefault()
    {
        var entry = Parse(Sample);

        Assert.Equal("sample", entry.Id);
        Assert.Equal("Sample", entry.Name);
        Assert.Equal(3, entry.Order);

        // Exactly Default with the file's changes: every token it doesn't name is Default's.
        var expected = D with
        {
            Text = D.Text with { Title = C("#B8E6EC") },
            Glass = D.Glass with { Widget = C("#D1050B0E") },
            Shapes = D.Shapes with { Radio = ShapeKind.Hexagon, Badge = ShapeKind.Hexagon, ActivityBullet = "⬢ " },
            Bars = D.Bars with
            {
                Health = D.Bars.Health with { Below25 = D.Bars.Health.Below25 with { Fill = C("#F06A5A"), Percent = C("#FF9B8F") } },
                Height = 8,
            },
            Corner = D.Corner with { Widget = 0 },
            Font = D.Font with { Family = new ThemeFont("Assets/Fonts/Montserrat/#Montserrat"), TitleWeight = ThemeWeight.SemiBold },
            Map = D.Map with { WindowRounded = false },
        };
        Assert.Equal(expected, entry.Tokens);
        Assert.NotEqual(D, entry.Tokens);
        // Spot checks that the inheritance is real, state by state.
        Assert.Equal(D.Bars.Health.Below25.TimeLeft, entry.Tokens.Bar(BarKind.Health, BarState.Below25).TimeLeft);
        Assert.Equal(D.Bar(BarKind.Health, BarState.Above75), entry.Tokens.Bar(BarKind.Health, BarState.Above75));
        Assert.Equal(D.Text.Dim, entry.Tokens.Text.Dim);
    }

    [Fact]
    public void AnEmptyOverrideIsDefault() => Assert.Equal(D, Parse(WithTokens("{}")).Tokens);

    /// <summary>The titles' family is a family like the body's: a file may set it alone, and everything else keeps Default's.</summary>
    [Fact]
    public void ATitleFamilyLaysOverAlone()
    {
        var tokens = Parse(WithTokens("""{ "Font": { "TitleFamily": "Assets/Fonts/Montserrat/#Montserrat" } }""")).Tokens;

        Assert.Equal(new ThemeFont("Assets/Fonts/Montserrat/#Montserrat"), tokens.Font.TitleFamily);
        Assert.True(tokens.Font.TitleFamily.Bundled);
        Assert.Equal(D.Font.Family, tokens.Font.Family);
        Assert.Equal(D with { Font = D.Font with { TitleFamily = tokens.Font.TitleFamily } }, tokens);
    }

    [Fact]
    public void LayingOverNeverChangesDefault()
    {
        var before = D with { };
        Parse(Sample);
        Assert.Equal(before, D);
        Assert.Equal(C("#ECF2F8"), D.Text.Title);
    }

    // ---- Failures, never a silent inheritance ----------------------------------

    /// <summary>A key the record does not have, at any depth or in another case, fails and names the key.</summary>
    [Theory]
    [InlineData("""{ "Txt": { "Title": "#FFFFFF" } }""", "Tokens.Txt is not a token")]
    [InlineData("""{ "Text": { "Titel": "#FFFFFF" } }""", "Tokens.Text.Titel is not a token")]
    [InlineData("""{ "text": { "Title": "#FFFFFF" } }""", "Tokens.text is not a token")]
    [InlineData("""{ "Bars": { "Health": { "Below20": { "Fill": "#FFFFFF" } } } }""", "Tokens.Bars.Health.Below20 is not a token")]
    [InlineData("""{ "Frame": { "Locked": { "Width": 2 } } }""", "Tokens.Frame.Locked.Width is not a token")]
    public void AnUnknownKeyFails(string tokens, string message)
    {
        var ex = Assert.Throws<ThemeFileException>(() => Parse(WithTokens(tokens)));
        Assert.Contains(message, ex.Message);
        Assert.StartsWith("sample.json: ", ex.Message);
    }

    /// <summary>A value its token's type can't read fails, named by its path.</summary>
    [Theory]
    [InlineData("""{ "Text": { "Title": "#GGGGGG" } }""", "Tokens.Text.Title")]
    [InlineData("""{ "Text": { "Title": "white" } }""", "Tokens.Text.Title")]
    [InlineData("""{ "Text": { "Title": "#FFF" } }""", "Tokens.Text.Title")]
    [InlineData("""{ "Text": { "Title": 16777215 } }""", "Tokens.Text.Title")]
    [InlineData("""{ "Bars": { "Height": "8" } }""", "Tokens.Bars.Height")]
    [InlineData("""{ "Map": { "WindowRounded": "yes" } }""", "Tokens.Map.WindowRounded")]
    [InlineData("""{ "Corner": { "Widget": -2 } }""", "Tokens.Corner.Widget")]
    [InlineData("""{ "Corner": { "Widget": "10" } }""", "Tokens.Corner.Widget")]
    [InlineData("""{ "Shapes": { "Radio": "star" } }""", "Tokens.Shapes.Radio")]
    [InlineData("""{ "Shapes": { "Radio": 1 } }""", "Tokens.Shapes.Radio")]
    [InlineData("""{ "Shapes": { "ActivityBullet": 7 } }""", "Tokens.Shapes.ActivityBullet")]
    [InlineData("""{ "Font": { "TitleWeight": "Heavy" } }""", "Tokens.Font.TitleWeight")]
    [InlineData("""{ "Font": { "TitleWeight": "600" } }""", "Tokens.Font.TitleWeight")]
    [InlineData("""{ "Font": { "Family": "" } }""", "Tokens.Font.Family")]
    [InlineData("""{ "Font": { "TitleFamily": "" } }""", "Tokens.Font.TitleFamily")]
    [InlineData("""{ "Lit": { "Top": "1.5" } }""", "Tokens.Lit.Top")]
    public void ABadValueFails(string tokens, string path)
    {
        var ex = Assert.Throws<ThemeFileException>(() => Parse(WithTokens(tokens)));
        Assert.Contains(path, ex.Message);
    }

    /// <summary>A value where a group belongs, a group where a value belongs, a null: failures.</summary>
    [Theory]
    [InlineData("""{ "Text": "#FFFFFF" }""", "Tokens.Text is a group of tokens")]
    [InlineData("""{ "Text": { "Title": { "A": 1 } } }""", "Tokens.Text.Title is a value")]
    [InlineData("""{ "Text": { "Title": ["#FFFFFF"] } }""", "Tokens.Text.Title is a value")]
    [InlineData("""{ "Text": { "Title": null } }""", "Tokens.Text.Title is null")]
    public void AWrongShapeOfValueFails(string tokens, string message) =>
        Assert.Contains(message, Assert.Throws<ThemeFileException>(() => Parse(WithTokens(tokens))).Message);

    /// <summary>A shape the catalogue knows but the slot doesn't offer fails too.</summary>
    [Theory]
    [InlineData("""{ "Shapes": { "Radio": "box" } }""", "Shapes.Radio")]
    [InlineData("""{ "Shapes": { "Badge": "stock" } }""", "Shapes.Badge")]
    [InlineData("""{ "Shapes": { "TitleGlyph": "circle" } }""", "Shapes.TitleGlyph")]
    public void APickTheSlotDoesNotOfferFails(string tokens, string slot) =>
        Assert.Contains(slot, Assert.Throws<ThemeFileException>(() => Parse(WithTokens(tokens))).Message);

    [Theory]
    [InlineData("""{ "Order": 1, "Tokens": {} }""", "has no Name")]
    [InlineData("""{ "Name": " ", "Order": 1, "Tokens": {} }""", "has no Name")]
    [InlineData("""{ "Name": "Sample", "Tokens": {} }""", "has no Order")]
    [InlineData("""{ "Name": "Sample", "Order": 1.5, "Tokens": {} }""", "has no Order")]
    [InlineData("""{ "Name": "Sample", "Order": 1 }""", "has no Tokens")]
    [InlineData("""{ "Name": "Sample", "Order": 1, "Tokens": {}, "Id": "x" }""", "\"Id\" is not one of")]
    [InlineData("""[ 1, 2 ]""", "is not a JSON object")]
    [InlineData("""{ "Name": """, "is not JSON")]
    public void ABrokenFileFails(string json, string message) =>
        Assert.Contains(message, Assert.Throws<ThemeFileException>(() => Parse(json)).Message);

    // ---- Default is today's look ------------------------------------------------

    /// <summary>
    /// Default's record pinned to the literals the 1.38 code drew (PLAN.md's
    /// survey): the glass, the frame line and the edit gold, the title, text
    /// and dim greys, the bar track and the four fills, the health steps, the
    /// corner radii, the bar height, the font; no cut, no glow, no hexagon,
    /// no list rule. Default never moves: a change here is a visible change
    /// to every player's overlay.
    /// </summary>
    [Fact]
    public void DefaultIsTodaysLook()
    {
        Assert.Equal(C("#10151B"), D.Glass.Widget);
        Assert.Equal(C("#F210151B"), D.Glass.Dialog);
        Assert.Equal(C("#F210151B"), D.Glass.Menu);
        Assert.Equal(C("#9910151B"), D.Glass.Pill);
        Assert.Equal(C("#B310151B"), D.Glass.MapPill);
        Assert.Equal(C("#10151B"), D.Glass.MapWindow);
        Assert.Equal(C("#33FFFFFF"), D.Frame.Locked.Line);
        Assert.Equal(C("#FFC864"), D.Frame.Edit.Line);
        Assert.Equal(C("#FFC864"), D.Frame.ControlPanel.Line);

        Assert.Equal(C("#ECF2F8"), D.Text.Title);
        Assert.Equal(C("#C7D1DA"), D.Text.Text);
        Assert.Equal(C("#9AA7B0"), D.Text.Label);
        Assert.Equal(C("#7B8790"), D.Text.Dim);

        Assert.Equal(C("#3D202830"), D.Bars.Health.Track);
        Assert.Equal(C("#4CAF50"), D.Bar(BarKind.Health, BarState.Above75).Fill);
        Assert.Equal(C("#4CAF50"), D.Bar(BarKind.Health, BarState.From50To75).Fill);
        Assert.Equal(C("#FFB300"), D.Bar(BarKind.Health, BarState.From25To50).Fill);
        Assert.Equal(C("#E53935"), D.Bar(BarKind.Health, BarState.Below25).Fill);
        Assert.Equal(C("#FFD54F"), D.Bar(BarKind.Stamina, BarState.Above75).Fill);
        Assert.Equal(C("#FF9800"), D.Bar(BarKind.Hunger, BarState.Above75).Fill);
        Assert.Equal(C("#4FC3F7"), D.Bar(BarKind.Thirst, BarState.Above75).Fill);
        Assert.Equal(C("#E53935"), D.Bar(BarKind.Damage, BarState.Above75).Fill);
        Assert.Equal(C("#FFE082"), D.Bar(BarKind.Stamina, BarState.Above75).TimeLeft);
        Assert.Equal(C("#FFB74D"), D.Bar(BarKind.Hunger, BarState.Above75).TimeLeft);
        Assert.Equal(C("#81D4FA"), D.Bar(BarKind.Thirst, BarState.Above75).TimeLeft);
        Assert.Equal(12, D.Bars.Height);
        Assert.Equal(0, D.Bars.TrackLineWidth);
        Assert.Equal(0, D.Bars.Below25Halo);

        Assert.Equal(C("#C7D1DA"), D.Bars.Label);
        Assert.Equal(C("#C7D1DA"), D.Bar(BarKind.Health, BarState.Below25).Percent);
        Assert.Equal(C("#10151B"), D.Bars.Hunger.OnFill);
        Assert.Equal(C("#9AA7B0"), D.Readout(BarState.Above75));
        Assert.Equal(C("#FFB300"), D.Readout(BarState.From25To50));
        Assert.Equal(C("#E53935"), D.Readout(BarState.Below25));

        // Prime: the row states' colours and weights (a flipped row SemiBold), the rows before a check.
        Assert.Equal((C("#7CC884"), C("#C7D1DA"), ThemeWeight.Normal), (D.Prime.Met.Icon, D.Prime.Met.Text, D.Prime.Met.Weight));
        Assert.Equal((C("#9A5F5F"), C("#7B8790"), ThemeWeight.Normal), (D.Prime.Unmet.Icon, D.Prime.Unmet.Text, D.Prime.Unmet.Weight));
        Assert.Equal((C("#9A5F5F"), C("#FFB300"), ThemeWeight.SemiBold), (D.Prime.Lost.Icon, D.Prime.Lost.Text, D.Prime.Lost.Weight));
        Assert.Equal((C("#7CC884"), C("#ECF2F8"), ThemeWeight.SemiBold), (D.Prime.Fresh.Icon, D.Prime.Fresh.Text, D.Prime.Fresh.Weight));
        Assert.Equal((C("#7B8790"), C("#7B8790"), ThemeWeight.Normal), (D.Prime.Unchecked.Icon, D.Prime.Unchecked.Text, D.Prime.Unchecked.Weight));

        Assert.Equal(C("#14FFFFFF"), D.Badge.Intact.Fill);
        Assert.Equal(C("#4A5560"), D.Badge.Intact.Text);
        Assert.Equal(C("#B3402020"), D.Badge.Fractured.Fill);
        Assert.Equal(C("#FF8A80"), D.Badge.Fractured.Text);
        Assert.Equal(C("#FFC864"), D.Activity.Mine);
        Assert.Equal(C("#FFC864"), D.Prime.Status.PrimeElder);
        Assert.Equal(C("#FFC864"), D.Map.You.Arrow);
        Assert.Equal(C("#FFC864"), D.Map.You.Trail);

        Assert.Equal(new ThemeRadius(10), D.Corner.Widget);
        Assert.Equal(new ThemeRadius(10), D.Corner.Dialog);
        Assert.Equal(new ThemeRadius(6), D.Corner.Menu);
        Assert.Equal(new ThemeRadius(4), D.Corner.Pill);
        Assert.Equal(new ThemeRadius(2), D.Corner.Button);
        Assert.Equal(new ThemeRadius(4), D.Corner.Card);
        Assert.Equal(new ThemeRadius(2), D.Corner.Well);
        Assert.Equal(new ThemeRadius(6), D.Corner.Bar);
        Assert.Equal(new ThemeRadius(2), D.Corner.Check);
        Assert.Equal(new ThemeRadius(4), D.Corner.ScrollThumb);
        Assert.Equal(new ThemeRadius(4), D.Corner.Badge);

        Assert.Equal(new ThemeFont("Segoe UI"), D.Font.Family);
        Assert.Equal(D.Font.Family, D.Font.TitleFamily); // the titles share the family
        Assert.Equal(ThemeWeight.Bold, D.Font.TitleWeight);
        Assert.Equal(12, D.Font.ControlPanelButtonSize);
        Assert.Equal(10, D.Font.BadgeSize);

        // No cut, no lit corner, no glow anywhere.
        Assert.Equal(new ThemeTokens.CutNumbers { Widget = 0, MapSquare = 0, Dialog = 0 }, D.Cut);
        Assert.Equal(new ThemeTokens.LitNumbers { Edge = 0, Side = 0, Width = 0, Top = 0 }, D.Lit); // and no lit top line
        Assert.Equal(new ThemeTokens.GlowNumbers { Corners = 0, Edit = 0, ControlPanel = 0, Radio = 0, Thumb = 0, You = 0 }, D.Glow);
        Assert.Equal(ThemeColor.None, D.Frame.Locked.Glow);
        Assert.Equal(ThemeColor.None, D.Frame.Edit.Glow);
        // No hexagon: today's circle radio, box badge, WPF's own slider, no title glyph.
        Assert.Equal(ShapeKind.Circle, D.Shapes.Radio);
        Assert.Equal(ShapeKind.Stock, D.Shapes.SliderThumb);
        Assert.Equal(ShapeKind.Box, D.Shapes.Badge);
        Assert.Equal(ShapeKind.None, D.Shapes.TitleGlyph);
        Assert.Equal(ThemeColor.None, D.Text.Glyph);
        Assert.Equal("● ", D.Shapes.ActivityBullet);
        // No list rule, no nav bar: truly zero in the layout.
        Assert.Equal(0, D.ListRule.Width);
        Assert.Equal(0, D.ListRule.Gap);
        Assert.Equal(0, D.Nav.BarWidth);
        Assert.True(D.Map.WindowRounded);
    }

    // ---- The bundled themes, as the app loads them ---------------------------------

    [Fact]
    public void EveryBundledThemeLoadsDefaultFirst()
    {
        var all = Theme.All; // throws on a broken file
        Assert.Equal(Theme.DefaultId, all[0].Id);
        Assert.Equal("Default", all[0].Name);
        Assert.Same(DefaultTheme.Tokens, all[0].Tokens);
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var entry in all) ShapeCatalogue.Check(entry.Tokens.Shapes);
    }

    /// <summary>Every file in Assets/Themes is embedded (the csproj's wildcard), and nothing else is.</summary>
    [Fact]
    public void EveryFileInTheFolderIsBundled()
    {
        var folder = Path.Combine(RepoRoot(), "Assets", "Themes");
        var files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.json").Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).OrderBy(s => s).ToList()
            : new List<string>();
        var bundled = Theme.All.Skip(1).Select(e => e.Id).OrderBy(s => s).ToList();
        Assert.Equal(files, bundled);
    }

    // ---- Isle, the bundled file ----------------------------------------------------

    private static ThemeEntry Isle => Theme.All.Single(e => e.Id == "isle");

    [Fact]
    public void IsleIsBundledAfterDefault()
    {
        Assert.Equal(new[] { "default", "isle" }, Theme.All.Take(2).Select(e => e.Id));
        Assert.Equal("Isle", Isle.Name);
        Assert.Same(Isle, Theme.Resolve("Isle")); // config's id, in any case
    }

    /// <summary>
    /// Cyan and red, nothing else (DESIGN decision 3, PLAN.md questions 3 and
    /// 10): none of Isle's colours, resolved over Default, is today's amber,
    /// the edit and "you" gold, or the update card's orange, at any alpha. A
    /// token Isle forgets to name inherits Default's, which this catches.
    /// </summary>
    [Fact]
    public void IsleHasNoAmber()
    {
        var banned = new[] { C("#FFB300"), C("#FFC864"), C("#FFAA00") };
        var found = Theme.Leaves(Isle.Tokens)
            .Where(l => l.Value is ThemeColor c && banned.Any(b => b.R == c.R && b.G == c.G && b.B == c.B))
            .Select(l => l.Key)
            .ToList();
        Assert.Empty(found);
    }

    /// <summary>The cut corners (widgets 10, the minimap's map square 6, Settings 14) and the painted glows' radii (the lit corners 3, edit mode 5, the control panel 8).</summary>
    [Fact]
    public void IslesCutsAndGlowsAreDesigns()
    {
        var t = Isle.Tokens;
        Assert.Equal((10.0, 6.0, 14.0), (t.Cut.Widget, t.Cut.MapSquare, t.Cut.Dialog));
        Assert.Equal((3.0, 5.0, 8.0), (t.Glow.Corners, t.Glow.Edit, t.Glow.ControlPanel));
        Assert.Equal((26.0, 20.0, 1.5), (t.Lit.Edge, t.Lit.Side, t.Lit.Width));
        Assert.Equal(0, t.Lit.Top); // no lit top line in Isle (question 21)
        Assert.Equal(new ThemeRadius(0), t.Corner.Widget); // square, but for the cuts
    }

    /// <summary>The control panel's glow is painted inside its outline, so it must fit the panel's padding (10 × 8, ControlPanelWindow.xaml) or a button would sit on it.</summary>
    [Fact]
    public void EveryControlPanelGlowFitsThePanelsPadding()
    {
        foreach (var theme in Theme.All) Assert.InRange(theme.Tokens.Glow.ControlPanel, 0, 8);
    }

    /// <summary>Montserrat for both families: the titles' does not follow the body's on its own (question 20).</summary>
    [Fact]
    public void IsleSetsBothFamiliesToTheBundledMontserrat()
    {
        var montserrat = new ThemeFont("Assets/Fonts/Montserrat/#Montserrat");
        Assert.Equal(montserrat, Isle.Tokens.Font.Family);
        Assert.Equal(montserrat, Isle.Tokens.Font.TitleFamily);
        Assert.True(montserrat.Bundled);
    }

    /// <summary>
    /// A drawn slider thumb needs the themed slider's Style in Controls.xaml
    /// (Theme.Apply looks it up by key, the themes' step 5 adds it): until it
    /// is there, every theme keeps WPF's own slider, or Theme.Apply would fail.
    /// </summary>
    [Fact]
    public void NoThemePicksADrawnThumbBeforeTheThemedSliderExists()
    {
        var controls = File.ReadAllText(Path.Combine(RepoRoot(), "Themes", "Controls.xaml"));
        if (controls.Contains($"x:Key=\"{Theme.ThemedSliderKey}\"", StringComparison.Ordinal)) return;
        foreach (var theme in Theme.All) Assert.Equal(ShapeKind.Stock, theme.Tokens.Shapes.SliderThumb);
    }

    [Fact]
    public void ThemesAreOrderedByTheirOrderThenId()
    {
        var b = new ThemeEntry("b", "B", 2, D);
        var a = new ThemeEntry("a", "A", 2, D);
        var first = new ThemeEntry("z", "Z", 1, D);
        Assert.Equal(new[] { "default", "z", "a", "b" }, Theme.Ordered(new[] { b, a, first }).Select(e => e.Id));
    }

    // ---- The resource keys ------------------------------------------------------

    /// <summary>
    /// Every {DynamicResource Theme.…} in the repo's XAML (and every "Theme.…"
    /// key in its code) names a key a theme's dictionary holds: a typo there is
    /// a blank brush at run time, with no error anywhere. And none is read by
    /// StaticResource, which would freeze it at the first theme.
    /// </summary>
    [Fact]
    public void EveryThemeKeyInTheRepoNamesAToken()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOrTests(Path.GetRelativePath(root, f)))
            .ToList();
        Assert.Contains(files, f => f.EndsWith("App.xaml", StringComparison.OrdinalIgnoreCase)); // the scan sees the app's XAML

        var problems = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var (key, isStatic) in ThemeKeysIn(text, xaml: file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
            {
                if (!Theme.Keys.Contains(key)) problems.Add($"{Path.GetRelativePath(root, file)}: {key} is not a token");
                if (isStatic) problems.Add($"{Path.GetRelativePath(root, file)}: {key} is read by StaticResource (never follows a switch)");
            }
        }
        Assert.Empty(problems);
    }

    /// <summary>The scan itself catches what it is for.</summary>
    [Fact]
    public void TheKeyScanFindsTyposAndStaticReads()
    {
        var found = ThemeKeysIn("""
            <Border Background="{DynamicResource Theme.Glass.Widget}" BorderBrush="{StaticResource Theme.Frame.Locked.Line}"/>
            <TextBlock Foreground="{DynamicResource ResourceKey=Theme.Text.Titel}"/>
            """, xaml: true).ToList();

        Assert.Equal(3, found.Count);
        Assert.Contains(("Theme.Glass.Widget", false), found);
        Assert.Contains(("Theme.Frame.Locked.Line", true), found);
        Assert.Contains(("Theme.Text.Titel", false), found);
        Assert.DoesNotContain("Theme.Text.Titel", Theme.Keys);
        Assert.Contains("Theme.Text.Title", Theme.Keys);
        Assert.Contains("Theme.Text.Title.Color", Theme.Keys);
        Assert.Contains("Theme.Map.WindowRounded.Visibility", Theme.Keys);
        Assert.Contains(Theme.SliderStyleKey, Theme.Keys);
    }

    /// <summary>The dictionary a theme fills holds exactly the keys the scan checks against, each as the WPF object a window reads.</summary>
    [Fact]
    public void TheDictionaryHoldsEveryKeyAsItsWpfObject()
    {
        var dictionary = Theme.BuildDictionary(D);

        Assert.Equal(Theme.Keys.OrderBy(k => k), dictionary.Keys.Cast<string>().OrderBy(k => k));

        var title = Assert.IsType<SolidColorBrush>(dictionary["Theme.Text.Title"]);
        Assert.True(title.IsFrozen);
        Assert.Equal(Color.FromRgb(0xEC, 0xF2, 0xF8), title.Color);
        Assert.Equal(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), dictionary["Theme.Frame.Locked.Line.Color"]);
        Assert.Equal(new CornerRadius(10), dictionary["Theme.Corner.Widget"]);
        Assert.Equal(12.0, dictionary["Theme.Bars.Height"]);
        Assert.Equal(FontWeights.Bold, dictionary["Theme.Font.TitleWeight"]);
        Assert.Equal("Segoe UI", Assert.IsType<FontFamily>(dictionary["Theme.Font.Family"]).Source);
        Assert.Equal("Segoe UI", Assert.IsType<FontFamily>(dictionary["Theme.Font.TitleFamily"]).Source);
        Assert.Equal(true, dictionary["Theme.Map.WindowRounded"]);
        Assert.Equal(Visibility.Visible, dictionary["Theme.Map.WindowRounded.Visibility"]);
        Assert.Equal(ShapeKind.Circle, dictionary["Theme.Shapes.Radio"]);
        Assert.Equal("● ", dictionary["Theme.Shapes.ActivityBullet"]);

        // A Stock thumb: an empty Slider Style, which leaves WPF's own slider as today's unstyled ones.
        var slider = Assert.IsType<Style>(dictionary[Theme.SliderStyleKey]);
        Assert.Equal(typeof(Slider), slider.TargetType);
        Assert.Empty(slider.Setters);
        Assert.Null(slider.BasedOn);
    }

    /// <summary>The tokens carry no identity: no code outside Themes/ can ask which theme is on (PLAN.md section 0).</summary>
    [Fact]
    public void TheTokensCarryNoIdentity()
    {
        var top = typeof(ThemeTokens).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Id", top);
        Assert.DoesNotContain("Name", top);
        // The one text a theme holds is a bullet; an id or a name would be another string (Map.Name is a colour).
        var strings = Theme.Leaves(D).Where(l => l.Value is string).Select(l => l.Key);
        Assert.Equal(new[] { "Theme.Shapes.ActivityBullet" }, strings);
    }

    // ---- Helpers ------------------------------------------------------------------

    private static readonly Regex XamlKey = new(
        @"\{(?<kind>Dynamic|Static)Resource\s+(?:ResourceKey\s*=\s*)?(?<key>Theme\.[A-Za-z][A-Za-z0-9_.]*)\s*\}", RegexOptions.Compiled);

    private static readonly Regex CodeKey = new(@"""(?<key>Theme\.[A-Za-z][A-Za-z0-9_.]*)""", RegexOptions.Compiled);

    private static IEnumerable<(string Key, bool Static)> ThemeKeysIn(string text, bool xaml)
    {
        if (xaml)
        {
            foreach (Match m in XamlKey.Matches(text)) yield return (m.Groups["key"].Value, m.Groups["kind"].Value == "Static");
        }
        else
        {
            foreach (Match m in CodeKey.Matches(text)) yield return (m.Groups["key"].Value, false);
        }
    }

    private static bool IsBuildOrTests(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first is "bin" or "obj" or "PandoraOverlay.Tests" or "publish" or "Releases" or ".git" or ".vs";
    }

    /// <summary>The repo's root: up from the test's bin folder to the folder holding PandoraOverlay.csproj.</summary>
    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PandoraOverlay.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repo root (PandoraOverlay.csproj) is not above the test's folder");
    }
}
