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
/// four faces and the licence are in the build. The width checks of the
/// strings with the least room come with step 3, when Isle exists.
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
}
