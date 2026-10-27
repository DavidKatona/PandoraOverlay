using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The bundled fonts (1.39), every family under Assets/Fonts, whichever theme
/// reads it: nothing here names a family or a theme, so a theme that brings
/// its own font is checked by adding its folder (PLAN.md section 10, "a third
/// theme is a file"). First the two proofs the plan asked for before anything
/// is measured with a bundled font (PLAN.md section 5, REVIEW.md objection 7):
/// text formats on a bundled typeface inside the test run, with no
/// Application, and the ;component address resolves to the family itself
/// rather than a silent fallback font. Then that every face and licence is in
/// the build, that every weight a theme names is a real face of its family
/// (question 27) and every face bundled is one a theme names, and the width
/// checks of the strings with the least room, in every bundled theme's font
/// (section 5, question 28). The control panel's row is
/// ControlPanelWidthTests'. A font is never installed into Windows on the
/// owner's PC: an installed copy would let these pass with a bundling mistake.
/// </summary>
public class MontserratTests
{
    private static string FontsFolder => Path.Combine(ThemeFileTests.RepoRoot(), "Assets", "Fonts");

    /// <summary>Every bundled family: one folder each, named as the family is.</summary>
    public static IEnumerable<object[]> Families() =>
        Directory.EnumerateDirectories(FontsFolder).Select(d => new object[] { Path.GetFileName(d)! }).OrderBy(f => (string)f[0], StringComparer.Ordinal);

    /// <summary>The address a theme file gives a bundled family (its folder, then # and the family's own name).</summary>
    private static string AddressOf(string family) => $"Assets/Fonts/{family}/#{family}";

    /// <summary>A family's face files, lower-cased as the build's resources name them.</summary>
    private static List<string> FilesOf(string family) =>
        Directory.EnumerateFiles(Path.Combine(FontsFolder, family), "*.ttf")
                 .Select(f => Path.GetFileName(f).ToLowerInvariant()).OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Each face the family's address reaches, as WPF lists them (its upright ones; the slanted are simulations of the same files).</summary>
    private static List<GlyphTypeface> FacesOf(string family)
    {
        var faces = new List<GlyphTypeface>();
        foreach (var typeface in Theme.FontFamilyOf(AddressOf(family)).GetTypefaces().Where(t => t.Style == FontStyles.Normal))
            if (typeface.TryGetGlyphTypeface(out var face)) faces.Add(face);
        return faces;
    }

    // ---- The two proofs ---------------------------------------------------------

    /// <summary>
    /// Proof 1: the ;component address resolves to the bundled faces, every
    /// file in the family's folder and nothing else. A family WPF can't find
    /// throws nothing and draws in a fallback font, so this asks each typeface
    /// for its glyphs and checks whose they are, and that asking for a face's
    /// own weight gives that very face, unsimulated.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void TheComponentAddressResolvesToTheBundledFaces(string family)
    {
        var faces = FacesOf(family);
        Assert.NotEmpty(faces);
        foreach (var glyphs in faces)
        {
            // The typographic family (the Win32 name carries the weight for Medium and SemiBold: "Montserrat SemiBold").
            Assert.Equal(family, glyphs.FamilyNames[English]);
            Assert.Equal(StyleSimulations.None, glyphs.StyleSimulations);
            // From inside this assembly's resources, never an installed copy.
            Assert.Equal("pack", glyphs.FontUri.Scheme);
            Assert.Contains($"/PandoraOverlay;component/assets/fonts/{family}/", glyphs.FontUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

            var asked = new Typeface(Theme.FontFamilyOf(AddressOf(family)), FontStyles.Normal, glyphs.Weight, FontStretches.Normal);
            Assert.True(asked.TryGetGlyphTypeface(out var same), $"{family} {glyphs.Weight}: no glyphs, the address did not resolve");
            Assert.Equal(glyphs.FontUri, same.FontUri);
            Assert.Equal(StyleSimulations.None, same.StyleSimulations);
        }
        Assert.Equal(FilesOf(family), faces.Select(g => g.FontUri.Segments[^1].ToLowerInvariant()).Distinct().OrderBy(f => f, StringComparer.Ordinal));
    }

    /// <summary>
    /// Proof 2: FormattedText measures with the bundled typeface in the test
    /// run (no Application, xunit's own threads), so the width checks can be
    /// plain tests. A bundled family measures well apart from the fallback WPF
    /// picks for a family it can't find (wider or narrower), which is how a
    /// silent fallback would show: the width checks below would pass in it
    /// unseen (the Pandora build's first run did, while a stale resource file
    /// left its fonts out of the build; only the faces' check failed).
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void FormattedTextMeasuresWithTheBundledTypeface(string family)
    {
        var weight = FacesOf(family).First().Weight;
        var bundled = Width("Stamina", new Typeface(Theme.FontFamilyOf(AddressOf(family)), FontStyles.Normal, weight, FontStretches.Normal));
        var fallback = Width("Stamina", new Typeface(new FontFamily("No Such Font Anywhere"), FontStyles.Normal, weight, FontStretches.Normal));

        Assert.True(bundled > 0);
        Assert.True(Math.Abs(bundled - fallback) > fallback * 0.05, $"{family} {bundled:0.0} px, the fallback {fallback:0.0} px: the bundled face was not used");
    }

    // ---- In the build -----------------------------------------------------------

    /// <summary>Every face in the font folders is a WPF Resource of the app (PandoraOverlay.g.resources), and no other font is.</summary>
    [Fact]
    public void TheFacesAreInTheBuild()
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("PandoraOverlay.g.resources");
        Assert.NotNull(stream);
        using var reader = new ResourceReader(stream!);
        var built = reader.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key)
                          .Where(n => n.StartsWith("assets/fonts/", StringComparison.Ordinal))
                          .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var onDisk = Families().Select(f => (string)f[0])
                               .SelectMany(family => FilesOf(family).Select(file => $"assets/fonts/{family.ToLowerInvariant()}/{file}"))
                               .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(onDisk);
        Assert.Equal(onDisk, built);
    }

    /// <summary>Each family's licence travels with its font: embedded, and copied beside the program.</summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void TheLicenceIsInTheBuild(string family)
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream($"PandoraOverlay.Assets.Fonts.{family}.OFL.txt");
        Assert.NotNull(stream);
        using var text = new StreamReader(stream!);
        Assert.Contains("SIL Open Font License", text.ReadToEnd());

        var beside = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", family, "OFL.txt");
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
            foreach (var (family, role, weight) in NamedWeights(theme.Tokens))
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

    /// <summary>
    /// The other way round: every face in the font folders is one a theme
    /// names, with its family and its weight (or the code's own Bold in a
    /// theme's text family). A face nobody reads is weight in every install
    /// for nothing; a later theme that needs it adds it then (PLAN.md section 5).
    /// </summary>
    [Fact]
    public void EveryBundledFaceIsOneAThemeNames()
    {
        var named = Theme.All.SelectMany(t => NamedWeights(t.Tokens))
                             .Where(n => n.Family.Bundled)
                             .Select(n => (n.Family.Address, (int)n.Weight))
                             .ToHashSet();
        var unread = Families().Select(f => (string)f[0])
            .SelectMany(family => FacesOf(family).Select(face => (family, weight: face.Weight.ToOpenTypeWeight(), file: face.FontUri.Segments[^1])))
            .Where(f => !named.Contains((AddressOf(f.family), f.weight)))
            .Select(f => $"{f.file} ({f.family} {f.weight})")
            .ToList();
        Assert.True(unread.Count == 0, "bundled, but no theme names it: " + string.Join(", ", unread));
    }

    /// <summary>
    /// Every weight a theme's tokens name, with the family it is read in: the
    /// titles' weights in Font.TitleFamily, the rest in Font.Family, and the
    /// code's own Bold there (the badges' text, Prime's ✓ and ✗).
    /// </summary>
    private static IEnumerable<(ThemeFont Family, string Role, ThemeWeight Weight)> NamedWeights(ThemeTokens t)
    {
        var font = t.Font;
        var prime = t.Prime;
        return new (ThemeFont Family, string Role, ThemeWeight Weight)[]
        {
            (font.TitleFamily, "Font.TitleWeight", font.TitleWeight),
            (font.TitleFamily, "Font.MapTitleWeight", font.MapTitleWeight),
            (font.Family, "Font.ValueWeight", font.ValueWeight),
            (font.Family, "Font.BodyWeight", font.BodyWeight),
            (font.Family, "Font.CaptionWeight", font.CaptionWeight),
            (font.Family, "Nav.SelectedWeight", t.Nav.SelectedWeight),
            (font.Family, "Prime.Met.Weight", prime.Met.Weight),
            (font.Family, "Prime.Unmet.Weight", prime.Unmet.Weight),
            (font.Family, "Prime.Lost.Weight", prime.Lost.Weight),
            (font.Family, "Prime.Fresh.Weight", prime.Fresh.Weight),
            (font.Family, "Prime.Unchecked.Weight", prime.Unchecked.Weight),
            (font.Family, "the code's own Bold", ThemeWeight.Bold),
        };
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
    /// The three headers, in every theme's fonts: the title (its own family
    /// and weight, after the theme's title glyph and its 4 px gap) beside its
    /// right-hand readout, inside the content width. Prime's and Activity's
    /// titles don't trim, so their headers must fit whole. The stats panel's
    /// dino name ends in an ellipsis where it runs long (MainWindow.xaml's
    /// DinoText: an ellipsis, never another wording, the owner's rule), so
    /// there the readout and the glyph must still leave the name its first
    /// letter and the ellipsis, whatever the font. With "Deinosuchus ♂" and a
    /// growth line (the longest, and a common one), the combat view's corner,
    /// the longest species the server has (Server rules' list), and the words
    /// that stand there out of game.
    /// </summary>
    [Theory]
    [MemberData(nameof(BundledThemes))]
    public void EveryHeaderFitsOrItsNameTrims(string id)
    {
        var t = TokensOf(id);
        var glyph = ShapeCatalogue.NaturalSize(ShapeSlot.TitleGlyph, t.Shapes.TitleGlyph) is { Width: > 0 } g ? g.Width + 4 : 0;
        double Title(string s) => Measure(s, t.Font.TitleFamily, t.Font.TitleWeight, 14);
        double Corner(string s) => Measure(s, t.Font.Family, t.Font.BodyWeight, 12);

        var stats = new[]
        {
            ("Deinosuchus ♂", "Growth 99.9% · ~23h 59m"),
            ("Deinosuchus ♂", "Growth 62.5% · ~2h 18m"),
            ("Deinosuchus ♂", "Wounded"),
            ("Pachycephalosaurus ♀", "Fully grown"),
            ("Pachycephalosaurus ♀", "Growth 62.5% · ~2h 18m"),
            ("Pachycephalosaurus ♀", "Wounded"),
            ("Not in-game", ""),
            ("Not signed in", ""),
            ("Connecting…", ""),
        };
        var whole = new[] { ("Prime", "Not ready · 10/10 (7 needed)"), ("Activity", "12 of 30 friends in game") };

        var problems = new List<string>();
        foreach (var (name, corner) in stats)
        {
            var least = glyph + Title(name[..1] + "…") + Corner(corner);
            if (least > Content) problems.Add($"\"{name} · {corner}\": the readout leaves the name no room ({least:0.0} px in {Content:0.0})");
        }
        foreach (var (title, corner) in whole)
        {
            var width = Title(title) + Corner(corner);
            if (width > Content) problems.Add($"\"{title} · {corner}\": {width:0.0} px in {Content:0.0}, and the title does not trim");
        }
        Assert.True(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
    }

    /// <summary>
    /// The one-line texts whose words a theme's wider font can push past
    /// their room end in an ellipsis, never cut off (the owner's rule: an
    /// ellipsis, never another wording, never a size of its own): the stats
    /// panel's dino name and status line, Prime's two footer lines. So any
    /// theme's font, a player's own included later, can only shorten them.
    /// </summary>
    [Theory]
    [InlineData("Shell/MainWindow.xaml", "DinoText")]
    [InlineData("Shell/MainWindow.xaml", "StatusText")]
    [InlineData("Prime/PrimeWindow.xaml", "InfoText")]
    [InlineData("Prime/PrimeWindow.xaml", "NoticeText")]
    public void TheLinesThatCanRunLongEndInAnEllipsis(string file, string name)
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeFileTests.RepoRoot(), file));
        var at = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{file} has no {name}");
        var start = xaml.LastIndexOf('<', at);
        var element = xaml[start..(xaml.IndexOf('>', at) + 1)];
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", element);
    }

    /// <summary>
    /// The check itself catches a missing face: for every bundled family, a
    /// weight it has no file for gives its own nearest face (or a simulated
    /// one), never that weight; a fallback family would be no check at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void AWeightTheFamilyLacksIsNoFace(string family)
    {
        var has = FacesOf(family).Select(f => f.Weight.ToOpenTypeWeight()).ToHashSet();
        var lacks = Enum.GetValues<ThemeWeight>().Where(w => !has.Contains((int)w)).ToList();
        Assert.NotEmpty(lacks); // no family bundles every weight
        foreach (var weight in lacks)
        {
            var typeface = new Typeface(Theme.FontFamilyOf(AddressOf(family)), FontStyles.Normal, weight.ToFontWeight(), FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var face));
            Assert.Equal(family, face.FamilyNames[English]); // the family's own nearest face, not a fallback
            Assert.True(face.Weight.ToOpenTypeWeight() != (int)weight || face.StyleSimulations != StyleSimulations.None);
        }
    }
}
