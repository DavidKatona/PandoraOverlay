using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PandoraOverlay.Games;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Hatchling inside the Games window (task H16): what only the app, the games and
/// the themes can show together. The game is the catalog's first entry, its tile
/// is on the grid with its art and its words, a click opens it in the fixed frame,
/// Back returns, and a second window opens where the first one was (G14), mid-run
/// included. Every one of its layout states (A26) is laid out in every theme by
/// <see cref="GamesLayoutTests"/>, the window's own test, with no cut line and
/// nothing clipped; this class holds what that walk passes but a reader would not:
/// a trimmed line is "fine" to the walk, so the lines that must never lose their
/// number are held here, in EVERY theme at its own factor (the rule is the
/// framework's, never a theme's: no theme is named, the owner, Oct 27 2026).
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class HatchlingInTheWindowTests
{
    private const string Id = "hatchling";

    private static void Run(Action test) => GamesLayoutTests.OnScreen(Theme.DefaultId, test);

    public static IEnumerable<object[]> Themes() => Theme.All.Select(e => new object[] { e.Id });

    private static HatchlingGame TheGame(GamesBench bench) =>
        (HatchlingGame)(bench.Host.Games.Single(g => g.Id == Id).Instance ?? throw new InvalidOperationException("Hatchling has no instance"));

    // ---- The catalog (G7, G19, A37) -----------------------------------------------------------

    [Fact]
    public void TheCatalogListsHatchlingFirstWithAnIdItsInstanceShares() => Run(() =>
    {
        var entry = GameCatalog.All[0];

        Assert.Equal(Id, entry.Id);
        Assert.True(GameIds.IsValid(Id));
        var first = entry.Create();
        var second = entry.Create();
        Assert.IsType<HatchlingGame>(first);
        Assert.Equal(entry.Id, first.Id);
        Assert.NotSame(first, second); // "Start it again" needs a new instance
        Assert.Empty(GameCatalogTests.CatalogProblems(GameCatalog.All));
    });

    [Fact]
    public void TheConstructorTouchesNoStorageAndLoadsNoArt() => Run(() =>
    {
        // A host over the real catalog, never attached: nothing may exist on disk, in the games
        // folder or anywhere under the test's, and the instance has loaded no art (it loads at
        // the first picture, never in the constructor: a game's constructor does no IO).
        using var temp = new TempGamesRoot();
        var host = new GamesHost(new GameSaveStore(temp.Root, () => DateTime.UtcNow), GameCatalog.All, new TestGameClock());

        var hosted = host.Games.Single(g => g.Id == Id);
        var game = Assert.IsType<HatchlingGame>(hosted.Instance);

        Assert.False(Directory.Exists(temp.Root), "making the game created the games folder");
        Assert.False(Directory.Exists(temp.FolderOf(Id)));
        Assert.Null(ArtOf(game));
        Assert.Equal("Hatchling", hosted.Title);

        var direct = new HatchlingGame();
        Assert.Null(ArtOf(direct));
        Assert.False(direct.HasReading);
    });

    private static object? ArtOf(HatchlingGame game) =>
        typeof(HatchlingGame).GetField("_art", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game);

    // ---- The grid (G1) -------------------------------------------------------------------------

    [Fact]
    public void TheGridShowsHatchlingsTileWithItsArtAndItsWords() => Run(() =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        var root = GamesLayoutTests.LaidOut(window);

        var tile = GamesBench.Tile(window, 0);
        var told = bench.Host.Games[0].Tile() ?? throw new InvalidOperationException("no tile");

        Assert.Equal("Hatchling", tile.Title.Text);
        Assert.Equal("Grow from hatchling to elder. Outlast the apex.", tile.Pitch.Text);
        Assert.Equal("Turn-based", tile.Tag.Text);
        Assert.Equal(told.Status, tile.Status.Text);
        Assert.Equal("2 of 21 species · 0 amber", tile.Status.Text); // a fresh save: the two starters, nothing earned
        Assert.Equal("0 of 24 achievements", tile.Detail.Text);
        Assert.False(told.CanReset); // nothing differs from a fresh state yet

        // The art: the game's own surface, 232 x 96 in its well, painted (not a blank ground).
        var art = Assert.IsType<PixelSurface>(tile.Well.Child);
        Assert.Equal(new Size(GamesFrame.TileArtWidth, GamesFrame.TileArtHeight), new Size(art.ActualWidth, art.ActualHeight));
        Assert.Equal((232, 96), (art.Buffer.Width, art.Buffer.Height));
        Assert.True(art.Buffer.Pixels.Distinct().Count() > 12, "the tile's picture is not a painted strip");
        Assert.Equal(new Size(GamesFrame.Width, GamesFrame.Height), root.DesiredSize);
    });

    // ---- A game in the frame (G13, G14) ---------------------------------------------------------

    [Fact]
    public void AClickOpensItInTheFrameAndBackReturnsToTheGrid() => Run(() =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();

        GamesBench.ClickCard(GamesBench.Tile(window, 0).Card);

        Assert.Same(bench.Host.Games[0], window.OnScreen);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        Assert.Same(view.Root, window.GameHost.Child);
        Assert.Equal("Hatchling", window.TitleText.Text);
        Assert.Equal(Visibility.Visible, window.BackButton.Visibility);
        Assert.Equal((Id, Id), (bench.Config.GamesView, bench.Config.GamesLastPlayed));
        var root = GamesLayoutTests.LaidOut(window);
        Assert.Equal(new Size(GamesFrame.Width, GamesFrame.Height), root.DesiredSize);
        Assert.Equal(new Size(GameFrame.ViewWidth, GameFrame.ViewHeight), new Size(view.Root.ActualWidth, view.Root.ActualHeight));
        Assert.Equal(PageKind.Camp, view.Shown);

        GamesBench.Click(window.BackButton);

        Assert.Null(window.OnScreen);
        Assert.Null(window.GameHost.Child);
        Assert.Equal(("", Id), (bench.Config.GamesView, bench.Config.GamesLastPlayed));
        Assert.NotNull(GamesBench.Tile(window, 0).Well.Child);
        window.Close();
        Assert.Equal(0, GameLoop.LiveTimers);
    });

    [Fact]
    public void ReopeningShowsWhereItWasEvenMidRun() => Run(() =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var first = bench.Window();
        first.ShowGame(bench.Host.Games[0]);
        Assert.True(TheGame(bench).StartRun());
        first.Close();
        Assert.Equal(Id, bench.Config.GamesView); // closing keeps it: that is where you left off (G14)
        Assert.Equal(0, GameLoop.LiveTimers);

        var second = bench.Window();

        Assert.Same(bench.Host.Games[0], second.OnScreen);
        var view = Assert.IsType<HatchlingView>(second.ViewOnScreen);
        Assert.Equal(PageKind.Run, view.Shown); // the run under way, from the save the start wrote
        Assert.NotEmpty(view.RunPage.RowsShown); // the turn list as last shown (H28)
        second.Close();
        Assert.Equal(0, GameLoop.LiveTimers);
    });

    // ---- The layout states (A26) ----------------------------------------------------------------

    [Fact]
    public void ItHasTwentySevenLayoutStatesAndAnUnknownNameThrows() => Run(() =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = window.ViewOnScreen ?? throw new InvalidOperationException("no view");

        Assert.Equal(27, view.LayoutStates.Count);   // the plan's seventeen and one for each of the Guide's ten sections (H94)
        Assert.Equal(view.LayoutStates.Count, view.LayoutStates.Distinct().Count());
        Assert.Equal(HatchlingView.AllStates, view.LayoutStates);
        Assert.Throws<ArgumentException>(() => view.ShowLayoutState("no-such-state"));
    });

    [Fact]
    public void TheStatesTakeAnyOrderTouchNoStorageAndLeaveNoTimer() => Run(() =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = window.ViewOnScreen ?? throw new InvalidOperationException("no view");
        var before = Files(bench.Temp.Root);

        foreach (var state in view.LayoutStates.Reverse().Concat(view.LayoutStates).Concat(new[] { "run-won", "camp-first", "run-won" }))
        {
            view.ShowLayoutState(state);
            GamesLayoutTests.LaidOut(window);
            Assert.Equal(0, GameLoop.LiveTimers);
        }

        Assert.Equal(before, Files(bench.Temp.Root)); // a state reads and writes no storage
        window.Close();
        Assert.Equal(0, GameLoop.LiveTimers);
    });

    private static List<string> Files(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks).OrderBy(s => s, StringComparer.Ordinal).ToList()
            : new List<string>();

    // ---- What the walk passes, held in every theme -----------------------------------------------

    /// <summary>
    /// A turn list row that carries a number is whole in every theme (H41, H80: the health lost
    /// must be findable in the list), and so is a name with its ordinal: the walk passes a line
    /// that ends in an ellipsis, so here no such line may. The samples hold the widest of both
    /// ("spotted you · bit you: 12", "defeated Carnotaurus 12", "Herrerasaurus 10").
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void ATurnListRowThatCarriesANumberIsWholeInEveryTheme(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        var problems = new List<string>();
        var seen = 0;

        foreach (var state in new[] { "run-first", "run-mid", "run-playback", "run-apex", "run-lost", "run-won" })
        {
            view.ShowLayoutState(state);
            var root = GamesLayoutTests.LaidOut(window);
            var numbered = view.RunPage.RowsShown
                .SelectMany(row => new[] { row.Name, row.Text })
                .Where(text => text.Any(char.IsDigit))
                .ToHashSet(StringComparer.Ordinal);
            seen += numbered.Count;
            foreach (var line in FitProbe.LongLines(root).Where(l => numbered.Contains(l.Text)))
                problems.Add($"{state}: \"{line.Text}\" is {line.Natural:0.0} wide in {line.Room:0.0}");
        }

        Assert.True(seen >= 10, $"only {seen} numbered rows were looked at");
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    });

    /// <summary>
    /// Every name and every row with an amount fits the turn list's columns in every theme (H87).
    /// It measures instead of walking the samples: the samples hold the longest name, and twice the
    /// longest line was another (H80: "Herrerasaurus 10"; H87: "Herrerasaurus 40", whose digits are
    /// wider than a one's in Isle's font, so 25 names from 20 to 99 lost their number in 94 units).
    /// A name with its number never ends in an ellipsis (H38, H41: the list always has the whole
    /// number) and keeps 2 units of air before the row's words (a space is 2.8 to 3.0 in the
    /// bundled fonts); what a row did holds every amount to 199 and every miss. Your own
    /// "defeated &lt;name&gt;" is left out on purpose (H87): with the Herrerasaurus' name it may end in an ellipsis,
    /// its number naming a creature your hit just removed, which the banner has named whole.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryNameAndEveryRowWithAnAmountFitsTheTurnListInEveryTheme(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        view.ShowLayoutState("run-apex");
        GamesLayoutTests.LaidOut(window);

        var row = (Grid)view.RunPage.TurnRows.Children[1];
        var name = (TextBlock)row.Children[1];
        var did = (TextBlock)row.Children[2];
        Assert.Equal(RunPage.NameWidth, name.ActualWidth, 0.01);
        Assert.Equal(138, did.ActualWidth, 0.01);

        var problems = new List<string>();
        void Fits(TextBlock block, string text, double air)
        {
            var width = NaturalWidth(block, text);
            if (width + air > block.ActualWidth)
                problems.Add($"\"{text}\" is {width:0.0} in {block.ActualWidth:0.0}");
        }

        // Names: every predator but the apex with every number to 99 (the apex is one a run and
        // carries none), keeping each type's widest number for the "hit <name>" rows below.
        var widest = new Dictionary<PredatorType, int>();
        foreach (var type in Enum.GetValues<PredatorType>().Where(t => t != Tuning.Apex))
        {
            var best = 1;
            var bestWidth = 0.0;
            for (var n = 1; n <= 99; n++)
            {
                var text = HatchlingText.Name(type, n);
                Fits(name, text, 2);
                var width = NaturalWidth(name, text);
                if (width > bestWidth) (best, bestWidth) = (n, width);
            }
            widest[type] = best;
        }
        Fits(name, HatchlingText.Name(Tuning.Apex, 1), 2);

        // Rows with an amount, 1 to 199.
        for (var n = 1; n <= 199; n++)
        {
            Fits(did, HatchlingText.ItBit(n), 0);
            Fits(did, HatchlingText.Joined(HatchlingText.ItSpotted, HatchlingText.ItBit(n)), 0);
            Fits(did, HatchlingText.Joined(HatchlingText.ItHeard, HatchlingText.ItBit(n)), 0);
            Fits(did, HatchlingText.Joined(HatchlingText.ItBit(n), HatchlingText.ItDefeated), 0);
            Fits(did, HatchlingText.DidHitGame(n), 0);
            Fits(did, HatchlingText.DidHit(Tuning.Apex, 1, n), 0);
            foreach (var (type, k) in widest)
                Fits(did, HatchlingText.DidHit(type, k, n), 0);
        }

        // The misses.
        Fits(did, HatchlingText.Joined(HatchlingText.ItSpotted, HatchlingText.ItMissed), 0);
        Fits(did, HatchlingText.Joined(HatchlingText.ItHeard, HatchlingText.ItMissed), 0);
        Fits(did, HatchlingText.Joined(HatchlingText.ItMissed, HatchlingText.ItDefeated), 0);

        Assert.True(problems.Count == 0, $"{problems.Count} texts do not fit: " + string.Join("; ", problems.Distinct().Take(12)));
    });

    /// <summary>The width a text takes on one line in a block's own font: what it needs to show whole, trailing space included.</summary>
    private static double NaturalWidth(TextBlock block, string text)
    {
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, block.FontSize, Brushes.White, 1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    /// <summary>
    /// The first minute's hint is the lesson the context line is there for: whole in every theme,
    /// given a second line inside the turn bar's 30 where a font runs wide (never cut, as Camp's
    /// note, H82). A trimmed context line is still fine for what the mouse is over; this holds the
    /// hint a new player reads at turn 0.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheFirstMinutesHintIsWholeInEveryTheme(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        view.ShowLayoutState("run-first");
        GamesLayoutTests.LaidOut(window);

        var context = view.RunPage.Context;

        Assert.Equal(HatchlingText.HintFirst, context.Text);
        Assert.True(WrappedHeight(context) <= context.ActualHeight + 0.5,
            $"the hint needs {WrappedHeight(context):0.0} in {context.ActualHeight:0.0} (width {context.ActualWidth:0.0}): it would end in an ellipsis");
        Assert.True(context.ActualHeight <= view.RunPage.TurnBar.Height, "the context line outgrew the turn bar");
    });

    /// <summary>The height a text block's whole text takes at its own width, in its own font: what it would need to show whole.</summary>
    private static double WrappedHeight(TextBlock text)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var formatted = new FormattedText(text.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, text.FontSize, Brushes.White, 1.0)
        {
            MaxTextWidth = text.ActualWidth,
        };
        return formatted.Height;
    }

    // ---- The Guide (H94), held in every theme ------------------------------------------------------

    /// <summary>
    /// Every section of the Guide fits its card whole in every theme at its fit factor: no
    /// line on the page trimmed (the walk passes an ellipsis; a guide that cuts a word is a
    /// guide that lies by leaving out), no sentence wanting more height than the card gives
    /// (the sentences wrap freely and are never held to a height, so the card's room is
    /// what holds them), and the list of sections inside its column. The sections were cut
    /// to fit: fewer words, never cut words.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryGuideSectionFitsItsCardWholeInEveryTheme(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        var problems = new List<string>();
        var seen = 0;

        foreach (var section in HatchlingText.Guide)
        {
            view.ShowLayoutState(HatchlingView.GuidePrefix + section.Id);
            GamesLayoutTests.LaidOut(window);
            var page = view.GuidePage;
            seen++;
            foreach (var line in FitProbe.LongLines(page))
                problems.Add(FormattableString.Invariant($"{section.Id}: \"{line.Text}\" is {line.Natural:0.0} wide in {line.Room:0.0}"));
            var card = page.SectionCard;
            var room = card.ActualHeight - card.Padding.Top - card.Padding.Bottom - card.BorderThickness.Top - card.BorderThickness.Bottom;
            var body = page.SectionBody;
            body.Measure(new Size(card.ActualWidth - card.Padding.Left - card.Padding.Right - card.BorderThickness.Left - card.BorderThickness.Right, double.PositiveInfinity));
            if (body.DesiredSize.Height > room + FitProbe.Slack)
                problems.Add(FormattableString.Invariant($"{section.Id}: the section is {body.DesiredSize.Height:0.0} high in a card of {room:0.0}"));
            var list = page.SectionList;
            list.Measure(new Size(list.ActualWidth, double.PositiveInfinity));
            if (list.DesiredSize.Height > card.ActualHeight)
                problems.Add(FormattableString.Invariant($"{section.Id}: the list is {list.DesiredSize.Height:0.0} high beside a card of {card.ActualHeight:0.0}"));
            foreach (var text in WrappingTexts(page.SectionBody))
                if (WrappedHeight(text) > text.ActualHeight + FitProbe.Slack)
                    problems.Add(FormattableString.Invariant($"{section.Id}: \"{text.Text}\" needs {WrappedHeight(text):0.0} in {text.ActualHeight:0.0}"));
        }

        Assert.Equal(HatchlingText.Guide.Count, seen);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    });

    /// <summary>
    /// The game's bar holds its five tabs and the widest wallet side by side, whole, in every
    /// theme (H94: the Guide tab at the end of the row). The tabs and the wallet share one
    /// cell, so neither the walk nor the clip check would see them overlap: this measures it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheFiveTabsAndTheWidestWalletFitTheBarInEveryTheme(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        view.ShowLayoutState(HatchlingView.GuidePrefix + HatchlingText.Guide[0].Id);
        GamesLayoutTests.LaidOut(window);

        var root = (HatchlingViewRoot)view.Root;
        Assert.Equal(HatchlingText.Money(Samples.Balance), root.Wallet.Text);
        Assert.Equal(HatchlingText.TabCamp, (string)root.FirstTab.Content);   // the longer of Camp and Run
        Assert.Empty(FitProbe.LongLines(root.GameBar));
        // The row's panel stretches across the bar; its last tab is where the words end.
        var last = root.GuideTab;
        Assert.Same(last, ((Panel)last.Parent).Children[^1]);
        var tabsEnd = last.TranslatePoint(new Point(last.ActualWidth, 0), root.GameBar).X;
        var walletStart = root.Wallet.TranslatePoint(new Point(0, 0), root.GameBar).X;
        Assert.True(tabsEnd + 12 <= walletStart, FormattableString.Invariant($"the tabs end at {tabsEnd:0.0} and the wallet starts at {walletStart:0.0}"));
    });

    /// <summary>The hint that points at the Guide is whole in every theme, as the first minute's (H94, H86).</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheGuidesHintIsWholeInEveryTheme(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(GameCatalog.All);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = Assert.IsType<HatchlingView>(window.ViewOnScreen);
        view.ShowLayoutState("run-first");
        var context = view.RunPage.Context;
        context.Text = HatchlingText.HintGuide;   // the turn bar at rest, as on turn 1: "Your turn" beside it
        GamesLayoutTests.LaidOut(window);

        Assert.True(WrappedHeight(context) <= context.ActualHeight + 0.5,
            $"the hint needs {WrappedHeight(context):0.0} in {context.ActualHeight:0.0} (width {context.ActualWidth:0.0})");
    });

    private static IEnumerable<TextBlock> WrappingTexts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { TextWrapping: TextWrapping.Wrap } text) yield return text;
            foreach (var deeper in WrappingTexts(child)) yield return deeper;
        }
    }

    // ---- The resource keys the game reads --------------------------------------------------------

    private static readonly Regex XamlKey = new(@"\{DynamicResource\s+(?:ResourceKey\s*=\s*)?(?<key>[A-Za-z][A-Za-z0-9_.]*)\s*\}", RegexOptions.Compiled);
    private static readonly Regex XamlStatic = new(@"\{StaticResource\s", RegexOptions.Compiled);
    private static readonly Regex CodeKey = new(@"SetResourceReference\([^,]+,\s*""(?<key>[A-Za-z][A-Za-z0-9_.]*)""", RegexOptions.Compiled);
    private static readonly Regex ThemeLiteral = new(@"""(?<key>Theme\.[A-Za-z][A-Za-z0-9_.]*)""", RegexOptions.Compiled);

    /// <summary>
    /// Every key the game's XAML and code read, theme tokens and the app's control styles alike
    /// (the app's own scan, <c>ThemeFileTests.EveryThemeKeyInTheRepoNamesAToken</c>, covers the
    /// "Theme." keys over Games/ too; a style key such as <c>SaveButtonStyle</c> is not a
    /// "Theme." key, so this reads them as well), and each resolves in every theme from the
    /// application's resources: a typo is a blank brush at run time with no error anywhere.
    /// </summary>
    [Fact]
    public void EveryKeyTheGamesViewsReadExistsInEveryTheme()
    {
        var folder = Path.Combine(ThemeFileTests.RepoRoot(), "Games", "PandoraOverlay.Games.Hatchling");
        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .ToList();
        Assert.Contains(files, f => f.EndsWith("RunPage.xaml", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("Quarters.cs", StringComparison.Ordinal));

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var statics = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in XamlKey.Matches(text)) keys.Add(m.Groups["key"].Value);
                if (XamlStatic.IsMatch(text)) statics.Add(Path.GetFileName(file));
            }
            else
            {
                foreach (Match m in CodeKey.Matches(text)) keys.Add(m.Groups["key"].Value);
                foreach (Match m in ThemeLiteral.Matches(text)) keys.Add(m.Groups["key"].Value);
            }
        }

        Assert.Empty(statics); // a StaticResource would freeze a key at the first theme (A25)
        Assert.True(keys.Count > 40, $"only {keys.Count} keys were read from the game's files");
        Assert.Contains("SaveButtonStyle", keys);
        Assert.Contains("Theme.Bars.Health.Below25.Fill", keys);

        var missing = new List<string>();
        GamesLayoutTests.OnScreen(Theme.DefaultId, () =>
        {
            foreach (var theme in Theme.All)
            {
                Theme.Apply(theme.Id);
                foreach (var key in keys)
                    if (Application.Current.TryFindResource(key) is null) missing.Add($"{theme.Id}: {key}");
            }
        });
        Assert.True(missing.Count == 0, string.Join("; ", missing));
    }
}
