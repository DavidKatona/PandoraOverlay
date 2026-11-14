using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the metadata can't see, read in the sources: the engine is not a game
/// (A21), and a game's chrome holds no literal colour, corner, font, size or
/// text (A24, A25) and no tooltip (A33). The files are read where they are, a
/// build's output and these tests' own folder left out. With no game yet the
/// chrome scans find no file, which is expected: each rule's self-test, a bad
/// line and a good one, is what proves it today, as the app's own
/// TheLiteralScanFindsWhatItIsFor does for the app's windows.
/// </summary>
public class SourceScanTests
{
    private const string EngineFolder = "PandoraOverlay.Games.Engine";
    private const string TestsFolder = "PandoraOverlay.Games.Tests";

    // ---- The engine is not a game (A21) -------------------------------------------------

    /// <summary>
    /// Hatchling's own words, beside the species read from the rules: the
    /// currency's placeholder name, the game's, and its upgrade tree's. Anything
    /// that is Hatchling's stays in Hatchling until a second game asks (A21).
    /// </summary>
    internal static readonly string[] GameWords = { "amber", "hatchling", "lineage" };

    /// <summary>The server's species, as the app's bundled rules list them (its pack-limit table): read at test time, never typed here, so a species the server adds is held too.</summary>
    internal static IReadOnlyList<string> SpeciesFromTheRules()
    {
        using var rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root, "Assets", "rules.json")));
        return rules.RootElement.GetProperty("PackLimits").EnumerateArray()
            .SelectMany(category => category.GetProperty("Species").EnumerateArray())
            .Select(species => species.GetProperty("Name").GetString()!)
            .ToList();
    }

    // A word of a text or of an identifier: a run of letters, cut where a lower-case
    // letter meets a capital ("AmberWallet" is Amber and Wallet) and before the last
    // capital of a run that starts a word ("HTTPServer" is HTTP and Server).
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.Compiled);
    private static readonly Regex CamelCut = new("(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>
    /// Every word in <paramref name="text"/> that is one of <paramref name="terms"/>,
    /// as a whole word and ignoring case: a word of the prose, or of an
    /// identifier (its CamelCase or snake_case parts), with a plural 's'
    /// allowed. "chambers" is not "amber"; "AmberWallet", "HATCHLING_COUNT"
    /// and "Troodons" are what they look like.
    /// </summary>
    internal static IReadOnlyList<string> GameWordsIn(string text, IEnumerable<string> terms)
    {
        var wanted = terms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();
        foreach (Match run in Letters.Matches(text))
            foreach (var word in CamelCut.Split(run.Value))
            {
                var singular = word.Length > 1 && (word[^1] == 's' || word[^1] == 'S') ? word[..^1] : word;
                if (wanted.Contains(word) || wanted.Contains(singular)) found.Add(word);
            }
        return found;
    }

    [Fact]
    public void TheEngineNamesNoSpeciesAndNoGameWord()
    {
        var species = SpeciesFromTheRules();
        Assert.NotEmpty(species);
        var terms = species.Concat(GameWords).ToList();

        var files = Repo.Files(Path.Combine(Repo.Games, EngineFolder), "*.cs").ToList();
        Assert.NotEmpty(files); // the engine has source to read

        var problems = files
            .SelectMany(f => GameWordsIn(File.ReadAllText(f), terms).Distinct(StringComparer.OrdinalIgnoreCase).Select(w => $"{Repo.Relative(f)}: {w}"))
            .ToList();
        Assert.True(problems.Count == 0,
            "The engine is not a game (A21): no species, no currency, no Hatchling term. The engine's code says what it does, not whose it is:\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void TheGameWordScanFindsWhatItIsFor()
    {
        var terms = new[] { "Dryosaurus", "Tyrannosaurus" }.Concat(GameWords).ToList();

        Assert.Equal(new[] { "Dryosaurus" }, GameWordsIn("// a Dryosaurus walks two cells", terms));
        Assert.Equal(new[] { "Amber" }, GameWordsIn("public sealed class AmberWallet", terms));
        Assert.Equal(new[] { "HATCHLING" }, GameWordsIn("const int HATCHLING_STAGE = 0;", terms));
        Assert.Equal(new[] { "hatchlings" }, GameWordsIn("// hatchlings start at the edge", terms));
        Assert.Equal(new[] { "Lineage" }, GameWordsIn("var tree = new LineageTree();", terms));
        Assert.Equal(new[] { "tyrannosaurus" }, GameWordsIn("\"tyrannosaurus\"", terms));
        // Whole words only.
        Assert.Empty(GameWordsIn("// chambers of the heart; a Dryosaurid; ambergris", terms));
        Assert.Empty(GameWordsIn("public sealed class Wallet { public long Balance; }", terms));
    }

    // ---- A game's chrome (A24, A25, A33) ----------------------------------------------------

    /// <summary>Every file of a game's chrome: under a project's Views/ folder (Games/&lt;project&gt;/Views/**), the tests' own folder left out.</summary>
    internal static IEnumerable<string> ViewFiles(string extension) =>
        Directory.Exists(Repo.Games)
            ? Directory.EnumerateDirectories(Repo.Games)
                .Where(project => !Path.GetFileName(project).Equals(TestsFolder, StringComparison.OrdinalIgnoreCase))
                .Select(project => Path.Combine(project, "Views"))
                .SelectMany(views => Repo.Files(views, "*" + extension))
            : Enumerable.Empty<string>();

    // XAML comments say why, and may name what they forbid: they are not read.
    private static readonly Regex XamlComment = new("<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    // The app's own (ThemedWindowsTests), copied: the two test projects share no code.
    private static readonly Regex XamlColour = new(@"=""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);
    private static readonly Regex XamlCorner = new(@"CornerRadius=""[0-9]", RegexOptions.Compiled);
    private static readonly Regex XamlElement = new(@"<(?!!--)[^<>]*>", RegexOptions.Compiled);
    private static readonly Regex XamlSizeLiteral = new(@"FontSize=""[0-9]|Property=""FontSize""\s+Value=""[0-9]", RegexOptions.Compiled);
    private static readonly Regex XamlTitleFamily = new(@"FontFamily=""\{DynamicResource Theme\.Font\.TitleFamily\}""", RegexOptions.Compiled);
    private static readonly Regex CodeBytes = new(@"\(\s*0x[0-9A-Fa-f]{2}\s*,\s*0x[0-9A-Fa-f]{2}\s*,\s*0x[0-9A-Fa-f]{2}", RegexOptions.Compiled);

    // Stricter than the app's, as the brief has it: a game names no font at all
    // (the app keeps two glyph and code fonts), and builds no colour in code by any
    // arguments (its data colours are its palette class's, which lives outside Views/).
    private static readonly Regex XamlFont = new(@"FontFamily=""(?!\{)", RegexOptions.Compiled);
    private static readonly Regex CodeColour = new(@"\bFrom(A)?Rgb\(", RegexOptions.Compiled);

    // A theme key read by StaticResource never follows a switch and fails to load where
    // the app is absent (A25); a tooltip is a second surface over the game (A33).
    private static readonly Regex XamlStatic = new("StaticResource", RegexOptions.Compiled);
    private static readonly Regex XamlToolTip = new("ToolTip=", RegexOptions.Compiled);

    // Every string is the game's text class's (A24): a Text, Content or Header written
    // in the XAML is a literal unless it is markup ({x:Static ...}).
    private static readonly Regex XamlLiteralText = new(@"\b(?:Text|Content|Header)=""(?!\{)", RegexOptions.Compiled);

    /// <summary>What a game's view XAML holds that it may not: each problem as the scan names it.</summary>
    internal static IReadOnlyList<string> XamlProblems(string xaml)
    {
        var text = XamlComment.Replace(xaml, "");
        var problems = new List<string>();
        foreach (var (regex, what) in new[]
                 {
                     (XamlColour, "a colour literal"), (XamlCorner, "a corner literal"), (XamlFont, "a font named"),
                     (XamlStatic, "a StaticResource"), (XamlToolTip, "a ToolTip"), (XamlLiteralText, "literal text"),
                 })
            foreach (Match m in regex.Matches(text))
                problems.Add($"{what}: {m.Value}");
        // A size written as a number, but beside the title family: a title keeps its size.
        foreach (Match element in XamlElement.Matches(text))
            if (XamlSizeLiteral.IsMatch(element.Value) && !XamlTitleFamily.IsMatch(element.Value))
                problems.Add("a size literal: " + Regex.Replace(element.Value, @"\s+", " "));
        return problems;
    }

    /// <summary>What a game's view code holds that it may not: a colour built in code.</summary>
    internal static IReadOnlyList<string> CodeProblems(string code)
    {
        var problems = new List<string>();
        foreach (var (regex, what) in new[] { (CodeColour, "a colour built in code"), (CodeBytes, "a colour as three hex bytes") })
            foreach (Match m in regex.Matches(code))
                problems.Add($"{what}: {m.Value}");
        return problems;
    }

    /// <summary>
    /// A game's chrome is the theme's (A25): no colour, corner or font literal
    /// in its views, no body size written as a number, nothing read by
    /// StaticResource, no tooltip; and its XAML holds no literal text (A24).
    /// Data colours (G21) are the game's palette class's, outside Views/.
    /// </summary>
    [Fact]
    public void AGamesChromeHoldsNoLiteral()
    {
        var problems = ViewFiles(".xaml").SelectMany(f => XamlProblems(File.ReadAllText(f)).Select(p => $"{Repo.Relative(f)}: {p}"))
            .Concat(ViewFiles(".cs").SelectMany(f => CodeProblems(File.ReadAllText(f)).Select(p => $"{Repo.Relative(f)}: {p}")))
            .ToList();

        Assert.True(problems.Count == 0,
            "A game's chrome is the theme's (A24, A25, A33): read a token by {DynamicResource Theme....}, a size by Theme.Size.N, a string from the game's text class:\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void TheChromeScanFindsWhatItIsFor()
    {
        // Colours, corners and fonts.
        Assert.NotEmpty(XamlProblems("""<Border Background="#CC10151B"/>"""));
        Assert.NotEmpty(XamlProblems("""<Border Background="#10151B"/>"""));
        Assert.Empty(XamlProblems("""<Border Background="{DynamicResource Theme.Glass.Widget}"/>"""));
        Assert.NotEmpty(XamlProblems("""<Border CornerRadius="6"/>"""));
        Assert.Empty(XamlProblems("""<Border CornerRadius="{DynamicResource Theme.Corner.Widget}"/>"""));
        Assert.NotEmpty(XamlProblems("""<TextBlock FontFamily="Segoe UI"/>"""));
        Assert.NotEmpty(XamlProblems("""<TextBlock FontFamily="Consolas"/>""")); // the app's exception is not a game's
        Assert.Empty(XamlProblems("""<TextBlock FontFamily="{DynamicResource Theme.Font.Family}"/>"""));

        // Sizes: a body size is the theme's; a title keeps its own.
        Assert.NotEmpty(XamlProblems("""<TextBlock FontSize="11"/>"""));
        Assert.NotEmpty(XamlProblems("""<Setter Property="FontSize" Value="9.5"/>"""));
        Assert.Empty(XamlProblems("""<TextBlock FontSize="{DynamicResource Theme.Size.11}"/>"""));
        Assert.Empty(XamlProblems("""<TextBlock FontSize="14" FontFamily="{DynamicResource Theme.Font.TitleFamily}"/>"""));

        // StaticResource and tooltips.
        Assert.NotEmpty(XamlProblems("""<Button Style="{StaticResource SaveButtonStyle}"/>"""));
        Assert.Empty(XamlProblems("""<Button Style="{DynamicResource SaveButtonStyle}"/>"""));
        Assert.NotEmpty(XamlProblems("""<Button ToolTip="Start a run"/>"""));
        Assert.NotEmpty(XamlProblems("""<Button ToolTipService.ToolTip="{x:Static local:HatchText.Start}"/>"""));

        // Comments are not read.
        Assert.Empty(XamlProblems("""<!-- no ToolTip="..." here (A33), no "#FFFFFF" --><Grid/>"""));

        // Code.
        Assert.NotEmpty(CodeProblems("var c = Color.FromRgb(255, 200, 100);"));
        Assert.NotEmpty(CodeProblems("var c = System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);"));
        Assert.NotEmpty(CodeProblems("private static readonly Brush Soft = Frozen(0x7B, 0x87, 0x90);"));
        Assert.Empty(CodeProblems("label.SetResourceReference(TextBlock.ForegroundProperty, \"Theme.Text.Primary\");"));
        Assert.Empty(CodeProblems("var index = Palette.Wrap(0x01);"));
    }

    [Fact]
    public void TheTextScanFindsWhatItIsFor()
    {
        Assert.NotEmpty(XamlProblems("""<TextBlock Text="Start a run"/>"""));
        Assert.NotEmpty(XamlProblems("""<Button Content="Back"/>"""));
        Assert.NotEmpty(XamlProblems("""<GroupBox Header="Lineage"/>"""));
        Assert.NotEmpty(XamlProblems("""<Run TextBlock.Text="x"/>"""));
        Assert.Empty(XamlProblems("""<TextBlock Text="{x:Static local:GameText.StartRun}"/>"""));
        Assert.Empty(XamlProblems("""<Button Content="{x:Static local:GameText.Back}"/>"""));
        Assert.Empty(XamlProblems("""<TextBlock x:Name="HeaderText" TextTrimming="CharacterEllipsis"/>"""));
    }

    /// <summary>The scans read a project's Views/ folder and nothing else: never the tests' own, never a build's output.</summary>
    [Fact]
    public void TheChromeScanReadsViewsOnly()
    {
        var files = ViewFiles(".xaml").Concat(ViewFiles(".cs")).Select(Repo.Relative).ToList();

        Assert.All(files, f =>
        {
            var segments = f.Split('/');
            Assert.Equal("Games", segments[0]);
            Assert.Equal("Views", segments[2]);
            Assert.NotEqual(TestsFolder, segments[1]);
            Assert.False(Repo.IsBuildOutput(f));
        });
        Assert.True(Repo.IsBuildOutput("Games/PandoraOverlay.Games.Sample/obj/Debug/Views/X.g.cs"));
        Assert.True(Repo.IsBuildOutput("Games/PandoraOverlay.Games.Sample/bin/Views/X.xaml"));
        Assert.False(Repo.IsBuildOutput("Games/PandoraOverlay.Games.Sample/Views/Board.xaml"));
    }
}
