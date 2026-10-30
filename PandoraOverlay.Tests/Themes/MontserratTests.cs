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
    /// plain tests. For every face file in the family's folder, read straight
    /// from the disk: "Stamina" formatted through the theme's address at that
    /// face's weight, glyph by glyph, is as wide as the file's own advance
    /// widths say, within 1%. A silent fallback would measure another font's
    /// widths, and the width checks would pass in it unseen (the Pandora
    /// build's first run did, while a stale resource file left its fonts out
    /// of the build; only the faces' check failed). Glyph by glyph, because a
    /// whole line is kerned and the advances are not: Cinzel Bold kerns
    /// "Stamina" 1.4% tighter than its advances (the fit factor's Nightshade
    /// trial, Oct 30 2026). Until the fit factor's release this compared with
    /// the fallback's width and asked for 5% apart, which a font as wide as
    /// Segoe UI could not pass (Nunito, 40.8 px against 40.2; the Nightshade
    /// attempt, Oct 29 2026).
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void FormattedTextMeasuresWithTheBundledTypeface(string family)
    {
        const string text = "Stamina";
        const double size = 11;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(FontsFolder, family), "*.ttf"))
        {
            var own = new GlyphTypeface(new Uri(file));
            var expected = text.Sum(c => own.AdvanceWidths[own.CharacterToGlyphMap[c]]) * size;
            var typeface = new Typeface(Theme.FontFamilyOf(AddressOf(family)), FontStyles.Normal, own.Weight, FontStretches.Normal);
            var measured = text.Sum(c => Width(c.ToString(), typeface));

            Assert.True(Math.Abs(measured - expected) <= expected * 0.01,
                $"{Path.GetFileName(file)}: \"{text}\" measures {measured:0.00} px, the file's own advances {expected:0.00} px: the bundled face was not used");
        }
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
    //
    // The strings themselves are the app's frame contract since the fit factor's
    // release (Themes/Fit/FrameContract.cs: each a string, a size, a weight and a
    // box from the frame constants), measured here at every theme's factor, and
    // at every candidate factor by FitFactorTests. A theme whose font doesn't fit
    // is drawn smaller by its factor, or refused below the floor: never a size of
    // its own, never another wording (question 28).

    /// <summary>Every theme, Default included (the layout was made with it, so it is the control).</summary>
    public static IEnumerable<object[]> EveryTheme() => Theme.All.Select(e => new object[] { e.Id });

    private static List<string> MissesIn(string id, string where) =>
        FrameContract.Misses(Theme.All.Single(e => e.Id == id).Tokens).Where(m => m.Where == where).Select(m => m.ToString()).ToList();

    /// <summary>
    /// Every Prime condition, at every row state's weight (a newly met row is
    /// the widest), fits the room a row has: the content width less the list
    /// rule and its gap and the icon column.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void EveryPrimeRowFits(string id)
    {
        var over = MissesIn(id, FrameContract.PrimeRows);
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    /// <summary>The bars' row names in their 58 px column, at the body weight.</summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void TheBarLabelsFitTheirColumn(string id)
    {
        var over = MissesIn(id, FrameContract.BarLabels);
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    /// <summary>
    /// The three headers, in every theme's fonts: the title (its own family
    /// and weight; the stats panel's after the theme's title glyph and its
    /// 4 px gap) beside its right-hand readout, inside the content width.
    /// Prime's and Activity's titles don't trim, so their headers must fit
    /// whole. The stats panel's dino name ends in an ellipsis where it runs
    /// long (MainWindow.xaml's DinoText: an ellipsis, never another wording,
    /// the owner's rule), so there the readout and the glyph must still leave
    /// the name its first letter and the ellipsis, whatever the font.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryTheme))]
    public void EveryHeaderFitsOrItsNameTrims(string id)
    {
        var problems = MissesIn(id, FrameContract.Headers);
        Assert.True(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
    }

    /// <summary>
    /// The contract catches what it is for: Default's tokens at a factor of
    /// 2 (its body text twice as big, as a font far wider and taller than any
    /// bundled one would be) miss in every group, and a miss names its string;
    /// at its own 1.00 nothing misses.
    /// </summary>
    [Fact]
    public void TheContractFindsAStringThatDoesNotFit()
    {
        var d = DefaultTheme.Tokens;
        var wide = d with { Fit = 2 };
        var misses = FrameContract.Misses(wide).ToList();
        foreach (var where in new[] { FrameContract.PrimeRows, FrameContract.BarLabels, FrameContract.Headers, FrameContract.ControlPanel })
            Assert.Contains(misses, m => m.Where == where);
        Assert.Contains(misses, m => m.Name == "Stamina");
        Assert.Empty(FrameContract.Misses(d));
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
