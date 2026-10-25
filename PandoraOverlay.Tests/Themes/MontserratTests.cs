using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The bundled Montserrat (1.39, the Isle theme's font). First the two proofs
/// the plan asked for before anything is measured with it (PLAN.md section
/// 5, REVIEW.md objection 7): text formats on the bundled typeface inside
/// the test run, with no Application, and the ;component address resolves
/// to Montserrat itself rather than a silent fallback font. Then that the
/// four faces and the licence are in the build, that every weight a theme
/// names is a real face of its family (question 27), and the width checks of
/// the strings with the least room, in every bundled theme's font (section
/// 5, question 28). The control panel's row is ControlPanelWidthTests'.
/// The font is never installed into Windows on the owner's PC: an installed
/// copy would let these pass with a bundling mistake.
/// </summary>
public class MontserratTests
{
    private static readonly string[] Faces =
        { "Montserrat-Regular.ttf", "Montserrat-Medium.ttf", "Montserrat-SemiBold.ttf", "Montserrat-Bold.ttf" };

    /// <summary>The address a theme file gives (the family's folder, then # and the family's own name).</summary>
    private const string Address = "Assets/Fonts/Montserrat/#Montserrat";

    private static FontFamily Montserrat() => Theme.FontFamilyOf(Address);

    // ---- The two proofs ---------------------------------------------------------

    /// <summary>
    /// Proof 1: the ;component address resolves to the bundled faces. A family
    /// WPF can't find throws nothing and draws in a fallback font, so this asks
    /// the typeface for its glyphs and checks whose they are, weight by weight.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    [InlineData(600)]
    [InlineData(700)]
    public void TheComponentAddressResolvesToTheBundledFaces(int weight)
    {
        var typeface = new Typeface(Montserrat(), FontStyles.Normal, FontWeight.FromOpenTypeWeight(weight), FontStretches.Normal);

        Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), "no glyphs: the address did not resolve");
        // The typographic family (the Win32 name carries the weight for Medium and SemiBold: "Montserrat SemiBold").
        Assert.Equal("Montserrat", glyphs.FamilyNames[CultureInfo.GetCultureInfo("en-US")]);
        Assert.Equal(weight, glyphs.Weight.ToOpenTypeWeight());
        // From inside this assembly's resources, never an installed copy.
        Assert.Equal("pack", glyphs.FontUri.Scheme);
        Assert.Contains("/PandoraOverlay;component/assets/fonts/montserrat/", glyphs.FontUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Proof 2: FormattedText measures with the bundled typeface in the test
    /// run (no Application, xunit's own threads), so the width checks can be
    /// plain tests. Montserrat is wider than the fallback WPF picks for a family
    /// it can't find, which is how a silent fallback would show.
    /// </summary>
    [Fact]
    public void FormattedTextMeasuresWithTheBundledTypeface()
    {
        var bundled = Width("Stamina", new Typeface(Montserrat(), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal));
        var fallback = Width("Stamina", new Typeface(new FontFamily("No Such Font Anywhere"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal));

        Assert.True(bundled > 0);
        Assert.True(bundled > fallback * 1.05, $"Montserrat {bundled:0.0} px, the fallback {fallback:0.0} px: the bundled face was not used");
    }

    // ---- In the build -----------------------------------------------------------

    /// <summary>The four weights Isle uses are WPF Resources of the app (PandoraOverlay.g.resources).</summary>
    [Fact]
    public void TheFourFacesAreInTheBuild()
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("PandoraOverlay.g.resources");
        Assert.NotNull(stream);
        using var reader = new ResourceReader(stream!);
        var names = reader.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        foreach (var face in Faces)
            Assert.Contains("assets/fonts/montserrat/" + face.ToLowerInvariant(), names);
    }

    /// <summary>The licence travels with the font: embedded, and copied beside the program.</summary>
    [Fact]
    public void TheLicenceIsInTheBuild()
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("PandoraOverlay.Assets.Fonts.Montserrat.OFL.txt");
        Assert.NotNull(stream);
        using var text = new StreamReader(stream!);
        Assert.Contains("SIL Open Font License", text.ReadToEnd());

        var beside = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Montserrat", "OFL.txt");
        Assert.True(File.Exists(beside), "OFL.txt is not copied beside the program");
    }

    private static double Width(string text, Typeface typeface) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, Brushes.White, 1.0)
            .WidthIncludingTrailingWhitespace;

    // ---- Every weight a theme names is a real face (question 27) -------------------

    /// <summary>
    /// For every theme, each weight its tokens name is a real face of the
    /// bundled family it is read with (the titles' weights of Font.TitleFamily,
    /// the rest of Font.Family), and so is the code's own Bold (the badges'
    /// text, Prime's ✓ and ✗): otherwise WPF picks the nearest face or fakes a
    /// bold, smeared, and nobody would notice. An installed family by name
    /// (Default's Segoe UI) is the theme editor's release, not checked here.
    /// </summary>
    [Fact]
    public void EveryNamedWeightHasAFace()
    {
        var problems = new List<string>();
        var looked = 0;
        foreach (var theme in Theme.All)
        {
            var font = theme.Tokens.Font;
            var prime = theme.Tokens.Prime;
            var named = new (ThemeFont Family, string Role, ThemeWeight Weight)[]
            {
                (font.TitleFamily, "Font.TitleWeight", font.TitleWeight),
                (font.TitleFamily, "Font.MapTitleWeight", font.MapTitleWeight),
                (font.Family, "Font.ValueWeight", font.ValueWeight),
                (font.Family, "Font.BodyWeight", font.BodyWeight),
                (font.Family, "Font.CaptionWeight", font.CaptionWeight),
                (font.Family, "Nav.SelectedWeight", theme.Tokens.Nav.SelectedWeight),
                (font.Family, "Prime.Met.Weight", prime.Met.Weight),
                (font.Family, "Prime.Unmet.Weight", prime.Unmet.Weight),
                (font.Family, "Prime.Lost.Weight", prime.Lost.Weight),
                (font.Family, "Prime.Fresh.Weight", prime.Fresh.Weight),
                (font.Family, "Prime.Unchecked.Weight", prime.Unchecked.Weight),
                (font.Family, "the code's own Bold", ThemeWeight.Bold),
            };
            foreach (var (family, role, weight) in named)
            {
                if (!family.Bundled) continue;
                looked++;
                var typeface = new Typeface(Theme.FontFamilyOf(family.Address), FontStyles.Normal, weight.ToFontWeight(), FontStretches.Normal);
                var own = family.Address[(family.Address.LastIndexOf('#') + 1)..];
                if (!typeface.TryGetGlyphTypeface(out var face))
                    problems.Add($"{theme.Id}: {role} {weight}: {family} gives no face at all");
                else if (face.FamilyNames[CultureInfo.GetCultureInfo("en-US")] != own)
                    problems.Add($"{theme.Id}: {role} {weight}: {family} fell back to {face.FamilyNames[CultureInfo.GetCultureInfo("en-US")]}");
                else if (face.Weight.ToOpenTypeWeight() != (int)weight || face.StyleSimulations != StyleSimulations.None)
                    problems.Add($"{theme.Id}: {role} {weight}: {family} has no such face (WPF would draw {face.Weight}, {face.StyleSimulations})");
            }
        }
        Assert.True(looked > 0, "no theme names a bundled family: nothing was checked");
        Assert.Empty(problems);
    }

    // ---- The strings with the least room, in every theme's font (section 5, question 28) ----

    /// <summary>A widget's content width: the 298 frame less its border (1) and padding (12) a side, as every widget's XAML has them.</summary>
    private const double Content = WidgetFrame.Width - 2 * 1 - 2 * 12;

    /// <summary>Prime's ✓ / ✗ column (PrimeWindow.BuildRows).</summary>
    private const double PrimeIcon = 16;

    /// <summary>The stats panel's row names' column (MainWindow.xaml).</summary>
    private const double LabelColumn = 58;

    private static readonly string[] BarLabels = { "Health", "Stamina", "Hunger", "Thirst", "Damage", "Speed" };

    private static double Measure(string text, ThemeFont font, ThemeWeight weight, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.FontFamilyOf(font.Address), FontStyles.Normal, weight.ToFontWeight(), FontStretches.Normal),
                size, Brushes.White, 1.0)
            .WidthIncludingTrailingWhitespace;

    /// <summary>The themes whose type is a bundled family: the ones these checks are for (Default's Segoe UI is what the layout was made with).</summary>
    public static IEnumerable<object[]> BundledThemes() =>
        Theme.All.Where(e => e.Tokens.Font.Family.Bundled || e.Tokens.Font.TitleFamily.Bundled).Select(e => new object[] { e.Id });

    private static ThemeTokens TokensOf(string id) => Theme.All.Single(e => e.Id == id).Tokens;

    /// <summary>
    /// Every Prime condition, at every row state's weight (a newly met row is
    /// the widest), fits the room a row has: the content width less the list
    /// rule and its gap and the icon column. A row that doesn't fit ends in an
    /// ellipsis in game: the owner hears it before the theme ships, and the
    /// remedy is the theme's font or weights, never a size (question 28).
    /// </summary>
    [Theory]
    [MemberData(nameof(BundledThemes))]
    public void EveryPrimeRowFits(string id)
    {
        var t = TokensOf(id);
        var room = Content - t.ListRule.Width - t.ListRule.Gap - PrimeIcon;
        var rows = new[] { t.Prime.Met, t.Prime.Unmet, t.Prime.Lost, t.Prime.Fresh, t.Prime.Unchecked };
        var over = (from text in PrimeConditions.Texts
                    from row in rows
                    let w = Measure(text, t.Font.Family, row.Weight, 11)
                    where w > room
                    select $"\"{text}\" at {row.Weight}: {w:0.0} px in {room:0.0}").Distinct().ToList();
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    /// <summary>The bars' row names in their 58 px column, at the body weight.</summary>
    [Theory]
    [MemberData(nameof(BundledThemes))]
    public void TheBarLabelsFitTheirColumn(string id)
    {
        var t = TokensOf(id);
        var over = BarLabels.Select(l => (l, w: Measure(l, t.Font.Family, t.Font.BodyWeight, 11)))
                            .Where(x => x.w > LabelColumn).Select(x => $"\"{x.l}\" {x.w:0.0} px").ToList();
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    /// <summary>
    /// What fits in Default but not in a theme's font, and ends in an
    /// ellipsis BY DESIGN: the owner's call at the first look in game (Oct 25
    /// 2026; question 4), an ellipsis wherever a string no longer fits, never
    /// another wording per theme and no size of its own. The elements trim
    /// (TextTrimming: the stats panel's dino name and status line, Prime's two
    /// footer lines). Listed one by one, so a NEW overflow still fails and
    /// reaches the owner before the theme ships.
    /// </summary>
    private static readonly HashSet<(string Theme, string Text)> TrimmedByDesign = new()
    {
        // The stats panel's header: the hexagon title glyph's 16 px tip
        // "Deinosuchus ♂" and a growth line over 272 (without it, 270): the
        // name ends in an ellipsis while growing. The glyph stays (the owner).
        ("isle", "Deinosuchus ♂ · Growth 99.9% · ~23h 59m"),
        ("isle", "Deinosuchus ♂ · Growth 62.5% · ~2h 18m"),
        // Four long footer lines, Montserrat being about an eighth wider than
        // Segoe UI at 10 px: two on the stats panel's status line, Prime's two.
        ("isle", "No favourite skin for this dino: star one in Settings › Skins"),
        ("isle", "Skin not applied: give it a few seconds between applies"),
        ("isle", "Not checked yet — Check Prime: control panel or hotkey"),
        ("isle", "Checked Oct 19 14:00 as Herrerasaurus — now Deinosuchus"),
    };

    /// <summary>
    /// A string that fits in Default's Segoe UI must fit in every theme's font
    /// too, or a theme would cut short what players read today (question 28);
    /// what Default itself cuts short is not the theme's doing, and the ones
    /// trimmed by design are listed (<see cref="TrimmedByDesign"/>).
    /// </summary>
    private static void AssertStillFits(string id, IEnumerable<(string Text, double Default, double Themed, double Room)> strings)
    {
        var over = strings.Where(s => s.Default <= s.Room && s.Themed > s.Room && !TrimmedByDesign.Contains((id, s.Text)))
                          .Select(s => $"\"{s.Text}\": {s.Themed:0.0} px in {s.Room:0.0} (Default {s.Default:0.0})")
                          .ToList();
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    /// <summary>
    /// The three headers: the title (its own family and weight, after the
    /// theme's title glyph, which takes its 12 px and a 4 px gap) beside its
    /// right-hand readout, together inside the content width. The stats
    /// panel's with "Deinosuchus ♂" and a growth line (the longest, and a
    /// common one), and the combat view's corner; Prime's with its longest
    /// status; Activity's with a long friends count.
    /// </summary>
    [Theory]
    [MemberData(nameof(BundledThemes))]
    public void TheHeadersStillFit(string id)
    {
        var t = TokensOf(id);
        var d = DefaultTheme.Tokens;
        double Header(ThemeTokens k, string title, string corner, bool glyph) =>
            (glyph && ShapeCatalogue.NaturalSize(ShapeSlot.TitleGlyph, k.Shapes.TitleGlyph) is { Width: > 0 } g ? g.Width + 4 : 0)
            + Measure(title, k.Font.TitleFamily, k.Font.TitleWeight, 14) + Measure(corner, k.Font.Family, k.Font.BodyWeight, 12);

        var headers = new[]
        {
            ("Deinosuchus ♂", "Growth 99.9% · ~23h 59m", true),
            ("Deinosuchus ♂", "Growth 62.5% · ~2h 18m", true),
            ("Deinosuchus ♂", "Wounded", true),
            ("Prime", "Not ready · 10/10 (7 needed)", false),
            ("Activity", "12 of 30 friends in game", false),
        };
        AssertStillFits(id, headers.Select(h => ($"{h.Item1} · {h.Item2}", Header(d, h.Item1, h.Item2, h.Item3), Header(t, h.Item1, h.Item2, h.Item3), Content)));
    }

    /// <summary>The long one-line footers (10 px, the body weight): the stats panel's status lines and Prime's two lines.</summary>
    [Theory]
    [MemberData(nameof(BundledThemes))]
    public void TheLongStatusLinesStillFit(string id)
    {
        var t = TokensOf(id);
        var d = DefaultTheme.Tokens;
        var lines = new[]
        {
            "Hotkey Ctrl+Shift+F12 in use by another app — rebind in Settings",
            "Hotkey Ctrl+Shift+F10 unavailable — keeping Ctrl+F10",
            "Connected · checking every 60s · 12:00:00",
            "Connected · waiting for spawn · 12:00:00",
            "Disconnected · retrying (secure connection failed)",
            "Session ended · sign in again from the tray",
            "Open Settings from the tray icon to sign in",
            "No favourite skin for this dino: star one in Settings › Skins",
            "Skin not applied: give it a few seconds between applies",
            "Not checked yet — Check Prime: control panel or hotkey",
            "Checked Oct 19 14:00 as Herrerasaurus — now Deinosuchus",
            "check failed (HttpRequestException) · retry in 4:59",
        };
        AssertStillFits(id, lines.Select(l => (l, Measure(l, d.Font.Family, d.Font.BodyWeight, 10), Measure(l, t.Font.Family, t.Font.BodyWeight, 10), Content)));
    }

    /// <summary>The check itself catches a missing face: Montserrat bundles no Light (300) and no ExtraBold (800).</summary>
    [Theory]
    [InlineData(ThemeWeight.Light)]
    [InlineData(ThemeWeight.ExtraBold)]
    public void AWeightTheFamilyLacksIsNoFace(ThemeWeight weight)
    {
        var typeface = new Typeface(Montserrat(), FontStyles.Normal, weight.ToFontWeight(), FontStretches.Normal);
        Assert.True(typeface.TryGetGlyphTypeface(out var face));
        Assert.True(face.Weight.ToOpenTypeWeight() != (int)weight || face.StyleSimulations != StyleSimulations.None);
    }
}
