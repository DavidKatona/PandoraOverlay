using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What the metadata can't see, read in the sources: the engine is not a game
/// (A21); a game's chrome holds no literal colour, corner, font, size or text
/// (A24, A25); its XAML, all of it under Views/, names nothing that leaves the
/// frame or takes the keyboard, which the compiled BAML would hide from the
/// forbidden list (A33, A42), and opens none of XAML's own doors: no data
/// provider, no namespace mapped but the games' own, no x:Static but through one
/// (A60); and no source under Games/ holds an address, a bare scheme included
/// (A45, A60), or a written file path (A72). The files are read where they are, a
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
        // The scan matches whole words: a name of another shape (with a space, a
        // hyphen, a digit) would pass it unseen, so it must be taught that shape first.
        Assert.All(species, name => Assert.True(Regex.IsMatch(name, "^[A-Za-z]+$"),
            $"the species \"{name}\" is not one run of letters: the engine scan matches whole words and must be taught a name of this shape before it can hold it"));
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
    // The colour takes 3 to 8 hex digits (A42), so #RGB and #ARGB are found beside the
    // long forms.
    private static readonly Regex XamlColour = new(@"=""#[0-9A-Fa-f]{3,8}""", RegexOptions.Compiled);
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
    // the app is absent (A25).
    private static readonly Regex XamlStatic = new("StaticResource", RegexOptions.Compiled);

    // Every string is the game's text class's (A24): a Text, Content or Header written
    // in the XAML is a literal unless it is markup ({x:Static ...}).
    private static readonly Regex XamlLiteralText = new(@"\b(?:Text|Content|Header)=""(?!\{)", RegexOptions.Compiled);

    // ---- What the BAML hides from the forbidden list (A42) ----
    // A type used only in XAML is written into the compiled BAML, never into the
    // metadata the forbidden list reads, so the XAML's own source is read for it: its
    // start tags (not </, <! or <?), their attributes, and the text between tags.
    // A '>' inside an attribute's value is read as the tag's end: write it &gt;.
    private static readonly Regex StartTag = new(@"<(?![/!?])([^\s/>]+)([^<>]*)>", RegexOptions.Compiled);
    private static readonly Regex AttributeOf = new(@"([^\s=""'/<>]+)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.Compiled);
    private static readonly Regex BetweenTags = new(@">([^<]*)(?=<)", RegexOptions.Compiled);

    /// <summary>
    /// Elements that open a surface of their own, play, animate or take the keyboard
    /// (A33, A42), and XAML's own doors out (A60): an ObjectDataProvider calls any
    /// method by its name, a framework type's static included, and an
    /// XmlDataProvider fetches its Source. Any part of an element's name counts
    /// (Button.ToolTip is ToolTip).
    /// </summary>
    internal static readonly HashSet<string> ForbiddenElements = new(StringComparer.Ordinal)
    {
        "Window", "NavigationWindow", "Popup", "ToolTip", "ContextMenu", "Menu", "MenuItem", "ComboBox", "DatePicker", "Frame",
        "WebBrowser", "MediaElement", "SoundPlayerAction", "KeyBinding", "TextBox", "PasswordBox", "RichTextBox",
        "Storyboard", "BeginStoryboard", "EventTrigger", "VisualStateManager",
        "ObjectDataProvider", "XmlDataProvider",
    };

    /// <summary>The endings of the effects' and the animations' type names: DropShadowEffect, DoubleAnimation, ColorAnimationUsingKeyFrames.</summary>
    internal static readonly string[] ForbiddenElementEndings = { "Effect", "Animation", "AnimationUsingKeyFrames" };

    /// <summary>Attributes, and a setter's property, that set a tooltip, a context menu or an effect, or listen to the keyboard (A33, A42), by the NAME after the last dot.</summary>
    internal static readonly HashSet<string> ForbiddenAttributes = new(StringComparer.Ordinal)
    {
        "ToolTip", "ContextMenu", "Effect", "BitmapEffect", "KeyDown", "KeyUp", "PreviewKeyDown", "PreviewKeyUp", "TextInput", "PreviewTextInput",
    };

    /// <summary>The OWNERS (before the last dot) whose every attached property is a tooltip's or a context menu's.</summary>
    internal static readonly HashSet<string> ForbiddenOwners = new(StringComparer.Ordinal) { "ToolTipService", "ContextMenuService" };

    /// <summary>The brush properties a colour by name would be written on (A42): Background="Black" is a literal colour as much as "#000".</summary>
    internal static readonly HashSet<string> BrushProperties = new(StringComparer.Ordinal) { "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color" };

    private static readonly HashSet<string> TextProperties = new(StringComparer.Ordinal) { "Text", "Content", "Header" };

    /// <summary>A name with its prefix dropped (up to and including ':'), cut at its last '.': OWNER before it ("" when none), NAME after.</summary>
    internal static (string Owner, string Name) OwnerAndName(string raw)
    {
        var colon = raw.IndexOf(':');
        var name = colon >= 0 ? raw[(colon + 1)..] : raw;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? (name[..dot], name[(dot + 1)..]) : ("", name);
    }

    private static bool IsForbiddenElement(string raw)
    {
        var colon = raw.IndexOf(':');
        var name = colon >= 0 ? raw[(colon + 1)..] : raw;
        return name.Split('.').Any(part => ForbiddenElements.Contains(part) || ForbiddenElementEndings.Any(e => part.EndsWith(e, StringComparison.Ordinal)));
    }

    private static bool IsForbiddenProperty(string raw)
    {
        var (owner, name) = OwnerAndName(raw);
        return ForbiddenAttributes.Contains(name) || ForbiddenOwners.Contains(owner);
    }

    // Transparent is not a colour: it is how a part with nothing drawn takes clicks.
    private static bool IsColourByName(string property, string? value) =>
        BrushProperties.Contains(OwnerAndName(property).Name) && value is not null && !value.StartsWith('{') && value != "Transparent";

    /// <summary>What a game's view XAML holds that it may not: each problem as the scan names it. Comments are not read.</summary>
    internal static IReadOnlyList<string> XamlProblems(string xaml)
    {
        var text = XamlComment.Replace(xaml, "");
        var problems = new List<string>();
        foreach (var (regex, what) in new[]
                 {
                     (XamlColour, "a colour literal"), (XamlCorner, "a corner literal"), (XamlFont, "a font named"),
                     (XamlStatic, "a StaticResource"), (XamlLiteralText, "literal text"),
                 })
            foreach (Match m in regex.Matches(text))
                problems.Add($"{what}: {m.Value}");
        // A size written as a number, but beside the title family: a title keeps its size.
        foreach (Match element in XamlElement.Matches(text))
            if (XamlSizeLiteral.IsMatch(element.Value) && !XamlTitleFamily.IsMatch(element.Value))
                problems.Add("a size literal: " + Regex.Replace(element.Value, @"\s+", " "));

        foreach (Match tag in StartTag.Matches(text))
        {
            var element = tag.Groups[1].Value;
            if (IsForbiddenElement(element))
                problems.Add($"an element that leaves the frame, takes the keyboard or reaches out: <{element}>");
            var attributes = AttributeOf.Matches(tag.Groups[2].Value)
                .Select(a => (Name: a.Groups[1].Value, Value: a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value))
                .ToList();
            foreach (var (name, value) in attributes)
            {
                if (IsForbiddenProperty(name)) problems.Add($"an attribute that leaves the frame or takes the keyboard: {name}");
                if (IsColourByName(name, value)) problems.Add($"a colour by name: {name}=\"{value}\"");
            }
            if (OwnerAndName(element).Name != "Setter") continue;
            // A setter is read as the attribute it sets: its Property, and its Value.
            var property = attributes.Where(a => a.Name == "Property").Select(a => a.Value).FirstOrDefault();
            var setValue = attributes.Where(a => a.Name == "Value").Select(a => a.Value).FirstOrDefault();
            if (property is null) continue;
            if (IsForbiddenProperty(property)) problems.Add($"a setter that leaves the frame or takes the keyboard: {property}");
            if (IsColourByName(property, setValue)) problems.Add($"a setter's colour by name: {property}=\"{setValue}\"");
            if (TextProperties.Contains(OwnerAndName(property).Name) && setValue is not null && !setValue.StartsWith('{'))
                problems.Add($"a setter's literal text: {property}=\"{setValue}\"");
        }

        foreach (Match between in BetweenTags.Matches(text))
            if (!string.IsNullOrWhiteSpace(between.Groups[1].Value))
                problems.Add("literal text between tags: " + between.Groups[1].Value.Trim());
        return problems;
    }

    // ---- Every game's XAML is under Views/ (A42) ----

    /// <summary>Whether a path from the root is a game's XAML where the chrome scans read it: Games/&lt;project&gt;/Views/...</summary>
    internal static bool XamlIsUnderViews(string relative)
    {
        var segments = relative.Split('/');
        return segments.Length >= 4 && segments[0] == "Games" && segments[2] == "Views";
    }

    /// <summary>Every source file under Games/ of the extension, the tests' own folder and a build's output left out.</summary>
    internal static IEnumerable<string> GamesSources(string extension) =>
        Repo.Files(Repo.Games, "*" + extension).Where(f =>
        {
            var segments = Repo.Relative(f).Split('/');
            return !(segments.Length > 1 && segments[1].Equals(TestsFolder, StringComparison.OrdinalIgnoreCase));
        });

    /// <summary>
    /// The chrome scans read Views/ alone, and BAML is read nowhere: so a
    /// game's every XAML file lives under its project's Views/, or a popup in
    /// it would pass every guard (A42).
    /// </summary>
    [Fact]
    public void EveryGameXamlIsUnderViews()
    {
        var outside = GamesSources(".xaml").Select(Repo.Relative).Where(f => !XamlIsUnderViews(f)).ToList();

        Assert.True(outside.Count == 0, "Every game's XAML is under its project's Views/, where the chrome scans read it (A42):\n" + string.Join("\n", outside));
    }

    [Fact]
    public void TheViewsRuleFindsWhatItIsFor()
    {
        Assert.False(XamlIsUnderViews("Games/PandoraOverlay.Games.Sample/Board.xaml"));
        Assert.False(XamlIsUnderViews("Games/PandoraOverlay.Games.Sample/Pages/Camp.xaml"));
        Assert.False(XamlIsUnderViews("Games/PandoraOverlay.Games.Engine/Themes/Generic.xaml"));
        Assert.True(XamlIsUnderViews("Games/PandoraOverlay.Games.Sample/Views/Camp.xaml"));
        Assert.True(XamlIsUnderViews("Games/PandoraOverlay.Games.Sample/Views/Pages/Camp.xaml"));
    }

    // ---- XAML's own doors (A60) ----
    // The metadata never sees what XAML resolves by itself: a clr-namespace mapping
    // makes every type of a namespace, the framework's included, writable as an
    // element or an extension's argument, and {x:Static} reads a static member of any
    // type it can name ({x:Static Application.Current} through the default namespace,
    // {x:Static sys:Environment.UserName} through a mapping) with no member reference
    // in the compiled code. So a game's XAML maps only the games' own namespaces, and
    // every x:Static names its member through one of those mappings.

    // Every namespace declaration, the default one included (prefix ""), with its value.
    private static readonly Regex XmlnsMapping = new(@"\bxmlns(?::([^\s=]+))?\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.Compiled);

    // The one form a clr-namespace mapping of a game may take, exactly: a namespace under
    // PandoraOverlay.Games., and, when an assembly is named, one of the games' own.
    // Anything else that maps a CLR namespace (another namespace, another assembly,
    // spaces, an empty assembly) is refused rather than read generously.
    private static readonly Regex GameClrNamespace = new(@"^clr-namespace:PandoraOverlay\.Games\.[A-Za-z0-9_.]+(?:;assembly=PandoraOverlay\.Games\.[A-Za-z0-9_.]+)?$", RegexOptions.Compiled);

    // {x:Static ...} or {x:StaticExtension ...}, under any prefix or none (the language
    // namespace may be mapped to another prefix): its arguments, up to the first '}',
    // so a nested extension inside (MemberType={x:Type ...}) is read as a bad argument
    // instead of hiding the outer one.
    private static readonly Regex StaticExtension = new(@"\{\s*(?:[^\s{}:=,]+:)?(?:Static|StaticExtension)(?=[\s}])([^}]*)\}", RegexOptions.Compiled);

    // The one argument an x:Static may have: <prefix>:<Type>.<Member>, after an optional "Member=".
    private static readonly Regex StaticMember = new(@"^(?:Member\s*=\s*)?([^\s:=,{}]+):([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

    /// <summary>Every namespace declaration in <paramref name="xaml"/> (comments not read): its prefix, "" for the default one, and its value.</summary>
    internal static IReadOnlyList<(string Prefix, string Value)> XamlMappings(string xaml) =>
        XmlnsMapping.Matches(XamlComment.Replace(xaml, ""))
            .Select(m => (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value))
            .ToList();

    private static bool MapsAClrNamespace(string value) => value.TrimStart().StartsWith("clr-namespace", StringComparison.OrdinalIgnoreCase);

    /// <summary>The prefixes <paramref name="xaml"/> maps to a game's own namespace, by the one form a game's mapping may take.</summary>
    internal static IReadOnlySet<string> GamePrefixes(string xaml) =>
        XamlMappings(xaml).Where(m => m.Prefix.Length > 0 && GameClrNamespace.IsMatch(m.Value)).Select(m => m.Prefix).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every clr-namespace mapping in a game's XAML that is not the games' own (A60): each as the scan names it. Comments are not read.</summary>
    internal static IReadOnlyList<string> ClrNamespaceProblems(string xaml) =>
        XamlMappings(xaml)
            .Where(m => MapsAClrNamespace(m.Value) && !GameClrNamespace.IsMatch(m.Value))
            .Select(m => $"a namespace that is not a game's: xmlns{(m.Prefix.Length > 0 ? ":" + m.Prefix : "")}=\"{m.Value}\"")
            .ToList();

    /// <summary>Every x:Static in a game's XAML that does not name its member through one of the file's own game mappings (A60), the extension and the element form: each as the scan names it. Comments are not read.</summary>
    internal static IReadOnlyList<string> StaticProblems(string xaml)
    {
        var text = XamlComment.Replace(xaml, "");
        var prefixes = GamePrefixes(text);
        var problems = new List<string>();

        bool ThroughAGame(string argument)
        {
            var m = StaticMember.Match(argument.Trim());
            return m.Success && prefixes.Contains(m.Groups[1].Value);
        }

        foreach (Match use in StaticExtension.Matches(text))
            if (!ThroughAGame(use.Groups[1].Value))
                problems.Add("an x:Static not through a game's namespace: " + use.Value);

        // The element form, <x:Static Member="..."/>: the same extension, written as a tag.
        foreach (Match tag in StartTag.Matches(text))
        {
            var raw = tag.Groups[1].Value;
            var colon = raw.IndexOf(':');
            var local = colon >= 0 ? raw[(colon + 1)..] : raw;
            var head = local.Split('.')[0];
            if (head != "Static" && head != "StaticExtension") continue;
            var member = AttributeOf.Matches(tag.Groups[2].Value)
                .Where(a => a.Groups[1].Value == "Member")
                .Select(a => a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value)
                .FirstOrDefault();
            if (local.Contains('.') || member is null || !ThroughAGame(member))
                problems.Add($"an x:Static not through a game's namespace: <{raw}{tag.Groups[2].Value}>");
        }
        return problems;
    }

    /// <summary>
    /// A game's XAML maps no namespace but the games' own (A60): a mapping of
    /// System, System.Diagnostics or any framework namespace would make its types
    /// writable in XAML, where the metadata never sees them.
    /// </summary>
    [Fact]
    public void EveryGameXamlMapsOnlyTheGamesNamespaces()
    {
        var files = GamesSources(".xaml").ToList();
        Assert.NotEmpty(files); // Hatchling's views are there to read

        var problems = files.SelectMany(f => ClrNamespaceProblems(File.ReadAllText(f)).Select(p => $"{Repo.Relative(f)}: {p}")).ToList();
        Assert.True(problems.Count == 0,
            "A game's XAML maps only the games' own namespaces (A60): clr-namespace:PandoraOverlay.Games.<...>, with no assembly or one of PandoraOverlay.Games.*; the metadata never sees a type XAML resolves by itself:\n"
            + string.Join("\n", problems));
    }

    /// <summary>
    /// Every x:Static of a game names its member through a mapping of the games'
    /// own (A60): an x:Static of an unprefixed WPF type ({x:Static
    /// Application.Current}) or of a mapped framework one reads what no game may,
    /// with no member reference for the forbidden list to see.
    /// </summary>
    [Fact]
    public void EveryGameXStaticNamesAGamesMember()
    {
        var files = GamesSources(".xaml").ToList();
        Assert.NotEmpty(files);

        var problems = files.SelectMany(f => StaticProblems(File.ReadAllText(f)).Select(p => $"{Repo.Relative(f)}: {p}")).ToList();
        Assert.True(problems.Count == 0,
            "Every x:Static of a game is <prefix>:<Type>.<Member>, the prefix mapped in the same file to a game's own namespace (A60), as Hatchling's {x:Static h:HatchlingText...} are:\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void TheNamespaceRuleFindsWhatItIsFor()
    {
        foreach (var bad in new[]
                 {
                     """<Grid xmlns:sys="clr-namespace:System;assembly=mscorlib"/>""",
                     """<Grid xmlns:d="clr-namespace:System.Diagnostics;assembly=System.Diagnostics.Process"/>""",
                     """<Grid xmlns:w="clr-namespace:System.Windows;assembly=PresentationFramework"/>""",
                     """<Grid xmlns="clr-namespace:System;assembly=System.Runtime"/>""",
                     """<Grid xmlns:app="clr-namespace:PandoraOverlay"/>""",
                     """<Grid xmlns:app="clr-namespace:PandoraOverlay;assembly=PandoraOverlay"/>""",
                     """<Grid xmlns:g="clr-namespace:PandoraOverlay.GamesX"/>""",
                     """<Grid xmlns:g="clr-namespace:PandoraOverlay.Games.Sample;assembly=PandoraOverlay"/>""",
                     """<Grid xmlns:g="clr-namespace:PandoraOverlay.Games.Sample;assembly=mscorlib"/>""",
                     """<Grid xmlns:g='clr-namespace: System'/>""",
                     """<Grid><Border xmlns:sys="clr-namespace:System;assembly=mscorlib"/></Grid>""",
                 })
            Assert.True(ClrNamespaceProblems(bad).Count > 0, bad + " should be found");
        foreach (var good in new[]
                 {
                     """<Grid xmlns:h="clr-namespace:PandoraOverlay.Games.Hatchling"/>""",
                     """<Grid xmlns:e="clr-namespace:PandoraOverlay.Games.Engine;assembly=PandoraOverlay.Games.Engine"/>""",
                     """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>""",
                     """<!-- xmlns:sys="clr-namespace:System;assembly=mscorlib" --><Grid/>""",
                 })
            Assert.True(ClrNamespaceProblems(good).Count == 0, good + " should pass, but: " + string.Join("; ", ClrNamespaceProblems(good)));
    }

    [Fact]
    public void TheStaticRuleFindsWhatItIsFor()
    {
        const string Mapped = """<Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:h="clr-namespace:PandoraOverlay.Games.Hatchling" xmlns:sys="clr-namespace:System;assembly=mscorlib">""";
        foreach (var bad in new[]
                 {
                     """<TextBlock Text="{x:Static Application.Current}"/>""",
                     """<TextBlock Text="{x:Static sys:Environment.UserName}"/>""",
                     """<TextBlock Text="{x:Static Member=sys:Environment.UserName}"/>""",
                     """<TextBlock Text="{x:StaticExtension sys:Environment.UserName}"/>""",
                     """<TextBlock Text="{x:Static SystemParameters.PrimaryScreenWidth}"/>""",
                     """<TextBlock Text="{x:Static MemberType={x:Type Application}, Member=Current}"/>""",
                     """<TextBlock Text="{Binding Source={x:Static Application.Current}, Path=MainWindow}"/>""",
                     """<TextBlock Text="{lang:Static sys:Environment.UserName}"/>""",
                     """<TextBlock Text="{x:Static q:HatchlingText.TabCamp}"/>""", // q is mapped nowhere
                     """<TextBlock Text="{x:Static h:HatchlingText}"/>""",
                     """<x:Static Member="sys:Environment.UserName"/>""",
                     "<x:Static/>",
                     "<x:Static.Member>",
                 })
            Assert.True(StaticProblems(Mapped + bad + "</Grid>").Count > 0, bad + " should be found");
        foreach (var good in new[]
                 {
                     """<TextBlock Text="{x:Static h:HatchlingText.TabCamp}"/>""",
                     """<TextBlock Text="{x:Static Member=h:HatchlingText.TabCamp}"/>""",
                     """<TextBlock Text="{x:Static  h:HatchlingText.TabCamp }"/>""",
                     """<Setter Property="Content" Value="{x:Static h:HatchlingText.Back}"/>""",
                     """<x:Static Member="h:HatchlingText.TabCamp"/>""",
                     """<TextBlock Text="{DynamicResource Theme.Text.Primary}" Tag="{x:Null}"/>""",
                     """<!-- {x:Static Application.Current} --><Border/>""",
                 })
            Assert.True(StaticProblems(Mapped + good + "</Grid>").Count == 0, good + " should pass, but: " + string.Join("; ", StaticProblems(Mapped + good + "</Grid>")));

        // The prefix counts only when this file maps it to a game: the same use with no
        // mapping, or with h mapped to a framework namespace, is refused.
        Assert.NotEmpty(StaticProblems("""<TextBlock Text="{x:Static h:HatchlingText.TabCamp}"/>"""));
        Assert.NotEmpty(StaticProblems("""<Grid xmlns:h="clr-namespace:System;assembly=mscorlib"><TextBlock Text="{x:Static h:Environment.UserName}"/></Grid>"""));
    }

    // ---- No address under Games/ (A45) ----

    // XAML's namespace declarations are names, never fetched: they are taken out first.
    private static readonly Regex XmlnsDeclaration = new(@"\bxmlns(?::[^\s=]+)?\s*=\s*(?:""[^""]*""|'[^']*')", RegexOptions.Compiled);
    // A "://", or the web's two schemes with their colon in any case (A60): an address
    // split as "https:" + "//" + host has no "://" in it, and "HTTPS:" works as well as
    // "https:". The scheme must start a word, so HttpStatusCode and a bare "http" (a
    // word, with no colon) are not addresses. One match per address: the run of
    // characters round what it found, so "https://x" is reported once.
    private static readonly Regex Address = new(@"[^\s""'<>()]*(?:://|\bhttps?:)[^\s""'<>()]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Every "://", "http:" or "https:" (any case) in a source text, with what stands round it; in XAML, after its namespace declarations are taken out.</summary>
    internal static IReadOnlyList<string> AddressesIn(string text, bool xaml)
    {
        if (xaml) text = XmlnsDeclaration.Replace(text, "");
        return Address.Matches(text).Select(m => m.Value).ToList();
    }

    /// <summary>
    /// No source under Games/ holds an address, comments included (A45): no
    /// "://", and no "http:" or "https:" in any case (A60). A game makes no
    /// request, and WPF itself fetches an http address given to an Image, a
    /// BitmapImage, a Frame or a MediaElement, with no System.Net type for the
    /// forbidden list to see.
    /// </summary>
    [Fact]
    public void NoSourceUnderGamesHoldsAnAddress()
    {
        var files = GamesSources(".cs").Select(f => (File: f, Xaml: false)).Concat(GamesSources(".xaml").Select(f => (File: f, Xaml: true))).ToList();
        Assert.NotEmpty(files);

        var problems = files
            .SelectMany(f => AddressesIn(File.ReadAllText(f.File), f.Xaml).Select(a => $"{Repo.Relative(f.File)}: {a}"))
            .ToList();
        Assert.True(problems.Count == 0,
            "No address under Games/ (A45, A60): no \"://\", \"http:\" or \"https:\" in any case, XAML namespace declarations aside. A game makes no request, and WPF itself fetches an http address given to an Image, a BitmapImage, a Frame or a MediaElement, with no System.Net type for the forbidden list to see. A comment cites a page without its scheme (example.org/page):\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void TheAddressScanFindsWhatItIsFor()
    {
        Assert.NotEmpty(AddressesIn("""<Image Source="https://example.org/a.png"/>""", xaml: true));
        Assert.NotEmpty(AddressesIn("""<Image Source="pack://siteoforigin:,,,/a.png"/>""", xaml: true));
        Assert.NotEmpty(AddressesIn("""var u = new Uri("http://example.org");""", xaml: false));
        Assert.NotEmpty(AddressesIn("// see https://example.org/hexes", xaml: false));
        // An address is reported once, not once for its "://" and again for its scheme.
        Assert.Equal(new[] { "https://example.org/hexes" }, AddressesIn("// see https://example.org/hexes", xaml: false));

        // A60: the schemes alone, in any case, so a split address is found (C# and XAML).
        Assert.Equal(new[] { "https:" }, AddressesIn("""var s = "https:" + "//" + host;""", xaml: false));
        Assert.Equal(new[] { "HTTP:" }, AddressesIn("""var s = "HTTP:" + rest;""", xaml: false));
        Assert.Equal(new[] { "https:" }, AddressesIn("// https: see the page", xaml: false));
        Assert.NotEmpty(AddressesIn("""var s = "Https:" + "//" + host;""", xaml: false));
        Assert.NotEmpty(AddressesIn("""<TextBlock Tag="http:example.org"/>""", xaml: true));

        Assert.Empty(AddressesIn("""<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:PandoraOverlay.Games.Sample.Views"/>""", xaml: true));
        Assert.Empty(AddressesIn("""new Uri("/PandoraOverlay.Games.Sample;component/views/camp.xaml", UriKind.Relative)""", xaml: false));
        Assert.Empty(AddressesIn("// see example.org/hexes", xaml: false));
        // A word is not a scheme: no colon, or a longer word before it.
        Assert.Empty(AddressesIn("if (code == HttpStatusCode.OK) return;", xaml: false));
        Assert.Empty(AddressesIn("""var word = "http"; // the word http, no colon""", xaml: false));
        Assert.Empty(AddressesIn("""case "https": break;""", xaml: false));
    }

    // ---- No written file path under Games/ (A72) ----
    // WPF reads the file a plain path names: in a view's <Image Source>, <ImageBrush
    // ImageSource>, Cursor and <ResourceDictionary Source> (loose XAML that no scan
    // reads), and in code through a decoder's or a font's Uri, with no type in the
    // metadata and no "://" for the address scan. So no string, and in XAML no
    // attribute value, starts with a written path: a drive letter and a slash or
    // backslash (C:\art, c:/art); two backslashes and a name, a UNC path (\\host\share,
    // four backslashes in a regular C# string), or two slashes and a name
    // (//host/share, which Uri reads as the same UNC path), each a read from another
    // machine with no System.Net type; or file: and something (file:C:/x). Case is
    // ignored. In C# a string opens with a double quote (a single one is an apostrophe
    // or a char there), in XAML a value with either. A path inside a longer string is
    // not read (KNOWN GAP 4: a computed path is A60's gap). A false alarm is possible,
    // and cheap: a regular string that starts with one letter and a colon before an
    // escape ("a:\n"), or with an escaped backslash before a letter ("\\d+"); write
    // those another way (a verbatim @"\d+"). ThePathScanFindsWhatItIsFor pins both.
    private static readonly Regex CodePath = new(@"""((?:[A-Za-z]:[\\/]|\\\\(?:\\\\)?(?=\w)|//(?=\w)|file:(?=[^\s""']))[^""\s]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex XamlPath = new(@"[""']((?:[A-Za-z]:[\\/]|\\\\(?=\w)|//(?=\w)|file:(?=[^\s""']))[^""'\s]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Every written file path in a source text (A72): a string, or in XAML an attribute value, that starts with a drive letter, a UNC path or file:, each as the path it found.</summary>
    internal static IReadOnlyList<string> PathsIn(string text, bool xaml) =>
        (xaml ? XamlPath : CodePath).Matches(text).Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    /// No source under Games/ holds a written file path (A72): no string, and in
    /// XAML no attribute value, starts with a drive letter, a UNC path or file:.
    /// WPF reads the file such a path names with no type for the forbidden list
    /// to see.
    /// </summary>
    [Fact]
    public void NoSourceUnderGamesHoldsAFilePath()
    {
        var files = GamesSources(".cs").Select(f => (File: f, Xaml: false)).Concat(GamesSources(".xaml").Select(f => (File: f, Xaml: true))).ToList();
        Assert.NotEmpty(files);

        var problems = files
            .SelectMany(f => PathsIn(File.ReadAllText(f.File), f.Xaml).Select(p => $"{Repo.Relative(f.File)}: {p}"))
            .ToList();
        Assert.True(problems.Count == 0,
            "No written file path under Games/ (A72): no string or attribute value starts with a drive letter and a slash (C:\\, c:/), two backslashes or two slashes and a name (a UNC path, a read from another machine), or file:. WPF reads the file such a path names in an Image's Source, a Cursor or a ResourceDictionary's Source, with no type for the forbidden list to see; a game's art is embedded and read by the engine's atlas (A28), and its storage is the host's (A7). A regular string that only looks like one (\"a:\\n\", \"\\\\d+\") is written another way:\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void ThePathScanFindsWhatItIsFor()
    {
        // C#: a drive letter, a UNC path (two backslashes or two slashes), file:, in a
        // verbatim, a regular or a raw string.
        Assert.Equal(new[] { @"C:\art\x.png" }, PathsIn("""var u = new Uri(@"C:\art\x.png");""", xaml: false));
        Assert.Equal(new[] { @"C:\\art\\x.png" }, PathsIn("""var u = new Uri("C:\\art\\x.png");""", xaml: false));
        Assert.Equal(new[] { "c:/art/x.png" }, PathsIn("""var u = new Uri("c:/art/x.png");""", xaml: false));
        Assert.Equal(new[] { @"\\host\share\x.cur" }, PathsIn("""var c = new Cursor(@"\\host\share\x.cur");""", xaml: false));
        Assert.Equal(new[] { @"\\\\host\\share\\x.cur" }, PathsIn("""var c = new Cursor("\\\\host\\share\\x.cur");""", xaml: false));
        Assert.Equal(new[] { "//host/share/x.png" }, PathsIn("""var u = new Uri("//host/share/x.png");""", xaml: false));
        Assert.Equal(new[] { "FILE:c:/x.png" }, PathsIn("""var u = new Uri("FILE:c:/x.png");""", xaml: false));
        Assert.Equal(new[] { @"C:\x.png" }, PathsIn(""""var r = """C:\x.png""";"""", xaml: false));

        // XAML: a value in double or single quotes.
        Assert.Equal(new[] { @"C:\art\x.png" }, PathsIn("""<Image Source="C:\art\x.png"/>""", xaml: true));
        Assert.Equal(new[] { "c:/art/x.png" }, PathsIn("""<Image Source='c:/art/x.png'/>""", xaml: true));
        Assert.Equal(new[] { @"\\host\share\x.png" }, PathsIn("""<ImageBrush ImageSource="\\host\share\x.png"/>""", xaml: true));
        Assert.Equal(new[] { @"C:\x.cur" }, PathsIn("""<Border Cursor="C:\x.cur"/>""", xaml: true));
        Assert.Equal(new[] { @"C:\x.xaml" }, PathsIn("""<ResourceDictionary Source="C:\x.xaml"/>""", xaml: true));
        Assert.Equal(new[] { "//host/share/x.png" }, PathsIn("""<Image Source="//host/share/x.png"/>""", xaml: true));
        Assert.Equal(new[] { "file:C:/x.png" }, PathsIn("""<Image Source="file:C:/x.png"/>""", xaml: true));

        // Not a path: a view's own relative Uri, escaped quotes and backslashes, a colon
        // before an escape after a longer word, "File:" with a space, "//" alone or
        // inside a string, a comment, a path inside a longer string (the stated gap).
        Assert.Empty(PathsIn("""new Uri("/PandoraOverlay.Games.Sample;component/views/camp.xaml", UriKind.Relative)""", xaml: false));
        Assert.Empty(PathsIn("""throw new ArgumentException($"No species is named \"{species}\".", nameof(species));""", xaml: false));
        Assert.Empty(PathsIn("""var s = "\\"; var c = '\\'; var t = "\\\\";""", xaml: false));
        Assert.Empty(PathsIn("""var m = "original's:\n";""", xaml: false));
        Assert.Empty(PathsIn("""var m = $"{x}:\n";""", xaml: false));
        Assert.Empty(PathsIn("""var k = "Problems:\n";""", xaml: false));
        Assert.Empty(PathsIn("""var s = "File: saved"; var t = "//"; var c = "a // b";""", xaml: false));
        Assert.Empty(PathsIn("""// see C:\ in a comment, no quote""", xaml: false));
        Assert.Empty(PathsIn("""var s = "see C:\x.png";""", xaml: false));

        // XAML that is not a path: an x:Static, the art's own relative path, the
        // namespace declarations, a named cursor, the word "file".
        Assert.Empty(PathsIn("""<TextBlock Text="{x:Static h:HatchlingText.TabCamp}"/>""", xaml: true));
        Assert.Empty(PathsIn("""<Image Source="/Art/x.png"/>""", xaml: true));
        Assert.Empty(PathsIn("""<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>""", xaml: true));
        Assert.Empty(PathsIn("""<Border Cursor="Hand"/>""", xaml: true));
        Assert.Empty(PathsIn("""<TextBlock Text="{x:Static h:T.File}" Tag="file"/>""", xaml: true));

        // The scan's known false alarms, held so that a change to the pattern is seen: a
        // regular string starting with an escaped backslash before a letter, or with one
        // letter and a colon before an escape. Written another way (@"\d+") they pass.
        Assert.Equal(new[] { @"\\d+" }, PathsIn("""var r = new Regex("\\d+");""", xaml: false));
        Assert.Equal(new[] { @"a:\n" }, PathsIn("""var m = "a:\n";""", xaml: false));
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
    /// in its views (a colour by name included), no body size written as a
    /// number, nothing read by StaticResource; its XAML holds no literal text
    /// (A24); and it names nothing that leaves the frame or takes the keyboard
    /// (A33, A42). Data colours (G21) are the game's palette class's, outside Views/.
    /// </summary>
    [Fact]
    public void AGamesChromeHoldsNoLiteral()
    {
        var problems = ViewFiles(".xaml").SelectMany(f => XamlProblems(File.ReadAllText(f)).Select(p => $"{Repo.Relative(f)}: {p}"))
            .Concat(ViewFiles(".cs").SelectMany(f => CodeProblems(File.ReadAllText(f)).Select(p => $"{Repo.Relative(f)}: {p}")))
            .ToList();

        Assert.True(problems.Count == 0,
            "A game's chrome is the theme's and stays in its view (A24, A25, A33, A42, A60): read a token by {DynamicResource Theme....}, a size by Theme.Size.N, a string from the game's text class; no popup, tooltip, menu, effect, animation, sound, keyboard or data provider:\n"
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

        // A setter's literal, and text between tags (A42).
        Assert.NotEmpty(XamlProblems("""<Setter Property="Text" Value="Start"/>"""));
        Assert.NotEmpty(XamlProblems("""<Setter Property="TextBlock.Text" Value="Start"/>"""));
        Assert.NotEmpty(XamlProblems("""<TextBlock>Start a run</TextBlock>"""));
        Assert.NotEmpty(XamlProblems("""<Button>Back</Button>"""));
        Assert.NotEmpty(XamlProblems("""<Run>x</Run>"""));
        Assert.Empty(XamlProblems("""<Setter Property="Content" Value="{x:Static t:T.Go}"/>"""));
        Assert.Empty(XamlProblems("""<TextBlock Text="{x:Static t:T.Go}"></TextBlock>"""));
        Assert.Empty(XamlProblems("<Grid>\n    <Border/>\n</Grid>"));
    }

    /// <summary>Elements that open a surface, animate, play or take the keyboard, which the BAML hides from the forbidden list (A42).</summary>
    [Fact]
    public void TheElementScanFindsWhatItIsFor()
    {
        foreach (var bad in new[]
                 {
                     """<Popup IsOpen="True"/>""", "<ToolTip/>", "<Button.ToolTip>", "<Grid.ContextMenu>", "<Border.Effect>",
                     """<DropShadowEffect BlurRadius="4"/>""", "<Storyboard>", """<DoubleAnimation Duration="0:0:1"/>""",
                     "<ColorAnimationUsingKeyFrames>", """<EventTrigger RoutedEvent="Loaded">""", "<VisualStateManager.VisualStateGroups>",
                     """<KeyBinding Key="Space" Command="{x:Static local:Cmd.Go}"/>""", "<MediaElement/>", "<TextBox/>", "<ComboBox/>",
                     "<controls:WebBrowser/>",
                     // A60: XAML's own doors, a call by name and a fetch.
                     """<ObjectDataProvider x:Key="p" ObjectType="{x:Type sys:Environment}" MethodName="GetEnvironmentVariable"/>""",
                     """<ObjectDataProvider.MethodParameters>""",
                     """<XmlDataProvider x:Key="feed" Source="example.org/feed.xml"/>""",
                 })
            Assert.True(XamlProblems(bad).Count > 0, bad + " should be found");
        foreach (var good in new[]
                 {
                     "<TextBlock/>", "<CheckBox/>", "<ToggleButton/>", "<Border/>", "<Grid.RowDefinitions>", "<Style.Triggers>",
                     """<Trigger Property="IsMouseOver" Value="True">""", "<local:BoardView/>", "<local:FrameCounter/>", "<Image/>",
                 })
            Assert.True(XamlProblems(good).Count == 0, good + " should pass, but: " + string.Join("; ", XamlProblems(good)));
    }

    /// <summary>Attributes and setters that set a tooltip, a context menu or an effect, or listen to the keyboard (A42).</summary>
    [Fact]
    public void TheAttributeScanFindsWhatItIsFor()
    {
        foreach (var bad in new[]
                 {
                     """<Button ToolTip="{x:Static t:T.Go}"/>""", """<Button ToolTipService.ToolTip="{x:Static t:T.Go}"/>""",
                     """<Button ToolTipService.ShowDuration="1000"/>""", """<Border ContextMenu="{DynamicResource M}"/>""",
                     """<Border Effect="{DynamicResource E}"/>""", """<Grid KeyDown="OnKey"/>""", """<Grid UIElement.PreviewKeyUp="OnKey"/>""",
                     """<Grid PreviewTextInput="OnText"/>""", """<Setter Property="ToolTip" Value="{x:Static t:T.Go}"/>""",
                     """<Setter Property="UIElement.Effect" Value="{DynamicResource E}"/>""",
                 })
            Assert.True(XamlProblems(bad).Count > 0, bad + " should be found");
        foreach (var good in new[]
                 {
                     """<Grid MouseDown="OnDown" MouseLeftButtonUp="OnUp"/>""", """<Grid KeyboardNavigation.TabNavigation="None"/>""",
                     """<Border x:Name="EffectHost"/>""",
                 })
            Assert.True(XamlProblems(good).Count == 0, good + " should pass, but: " + string.Join("; ", XamlProblems(good)));
    }

    /// <summary>A colour in its short hex forms, or by name on a brush property, is a literal too; Transparent is not a colour (A42).</summary>
    [Fact]
    public void TheColourScanFindsWhatItIsFor()
    {
        foreach (var bad in new[]
                 {
                     """<Path Fill="#FFF"/>""", """<Path Fill="#8FFF"/>""", """<Border Background="Black"/>""",
                     """<TextBlock TextElement.Foreground="White"/>""", """<Setter Property="Background" Value="Red"/>""",
                     """<Setter Property="Border.BorderBrush" Value="Gold"/>""", """<SolidColorBrush Color="White"/>""",
                 })
            Assert.True(XamlProblems(bad).Count > 0, bad + " should be found");
        foreach (var good in new[]
                 {
                     """<Border Background="Transparent"/>""", """<Setter Property="Background" Value="Transparent"/>""",
                     """<TextBlock Foreground="{DynamicResource Theme.Text.Dim}"/>""",
                     """<Setter Property="Foreground" Value="{DynamicResource Theme.Text.Bright}"/>""",
                     """<DockPanel LastChildFill="True"/>""", """<Image Stretch="Fill"/>""",
                     """<GradientStop Color="{DynamicResource Theme.Card.Line.Color}" Offset="0"/>""",
                 })
            Assert.True(XamlProblems(good).Count == 0, good + " should pass, but: " + string.Join("; ", XamlProblems(good)));

        // Comments are still not read.
        Assert.Empty(XamlProblems("""<!-- <Popup/> ToolTip="x" Background="Red" Start --><Grid/>"""));
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
