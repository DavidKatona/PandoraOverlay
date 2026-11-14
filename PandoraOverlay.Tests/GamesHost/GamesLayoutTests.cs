using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window in every theme (A26): every view it can show, laid out
/// unshown at the theme's own fit factor, keeps the frame
/// (<see cref="GamesFrame"/>, 800 x 552), has no line cut off (the app's walk,
/// <see cref="FitProbe"/>: it fits, or it ends in an ellipsis that shows) and
/// nothing clipped by layout (<see cref="Clipped"/>, this test's own: a tile
/// and a game's pages are fixed-height boxes, not scroll areas, and a font
/// with a taller line box would push a tile's button row out of its tile with
/// nothing in the walk to see it). The rules of the framework, the same for
/// every theme; no theme is named (the owner, Oct 27 2026). A theme's
/// recorded fit factor must not move for this window: the window gives way,
/// never the theme's file (FitFactorTests walks <see cref="EveryView"/> too).
/// <para>
/// The views: the empty grid, nine tiles whose every text is far too long,
/// one tile of each condition with one armed, the two refusals, the grid and
/// a game under the health warning, a game with the watch note, and every
/// listed game's every layout state (none until a game is listed: it joins by
/// being listed). Fresh objects each time over a temporary games folder,
/// never the real one; windows never shown.
/// </para>
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class GamesLayoutTests
{
    public static IEnumerable<object[]> Themes() => Theme.All.Select(e => new object[] { e.Id });

    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryViewKeepsTheFrameWithNothingCutOffOrClipped(string id) => OnScreen(id, () =>
    {
        var problems = new List<string>();
        var views = 0;
        foreach (var (where, root) in EveryView())
        {
            views++;
            problems.AddRange(Problems(where, root));
        }
        Assert.True(views >= 7, $"only {views} views were laid out");
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    });

    /// <summary>The clip check sees what it is for: a wrapping sentence in a box too short for it, and a fixed row pushed out of a fixed card.</summary>
    [Fact]
    public void TheClipCheckFindsWhatItIsFor() => OnScreen(Theme.DefaultId, () =>
    {
        var sentence = new TextBlock { Text = string.Join(" ", Enumerable.Repeat("words that wrap", 30)), TextWrapping = TextWrapping.Wrap };
        var tooShort = new Border { Width = 200, Height = 40, Child = sentence };
        var row = new Border { Height = 30 };
        var card = new StackPanel { Height = 100 };
        card.Children.Add(new Border { Height = 90 });
        card.Children.Add(row);
        var fine = new Border { Width = 200, Height = 200, Child = new TextBlock { Text = "short" } };
        var root = new StackPanel { Width = 300 };
        root.Children.Add(tooShort);
        root.Children.Add(card);
        root.Children.Add(fine);
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();

        var clipped = Clipped(root).ToList();

        Assert.Contains(clipped, c => c.Contains("TextBlock") && c.Contains("words that wrap"));
        Assert.Contains(clipped, c => c.StartsWith("StackPanel"));
        Assert.DoesNotContain(clipped, c => c.Contains("\"short\""));
    });

    /// <summary>
    /// And it reaches the tiles inside the grid's scroll area: a tile whose
    /// texts ran taller (a font with a taller line box, played here by a
    /// larger size) shows as clipped, its button row pushed out of the tile.
    /// </summary>
    [Fact]
    public void TheClipCheckReachesATileInTheGrid() => OnScreen(Theme.DefaultId, () =>
    {
        using var bench = new GamesBench(new FakeGame());
        var window = bench.Window();
        Assert.Empty(Clipped(LaidOut(window)));

        GamesBench.Tile(window, 0).Pitch.FontSize = 40;

        Assert.Contains(Clipped(LaidOut(window)), c => c.StartsWith("StackPanel") || c.StartsWith("DockPanel"));
    });

    // ---- What a view must hold to ------------------------------------------------------

    /// <summary>Every problem of one laid-out view: not the frame, a line cut off, an element clipped by layout.</summary>
    internal static IEnumerable<string> Problems(string where, FrameworkElement root)
    {
        if (root.DesiredSize != new Size(GamesFrame.Width, GamesFrame.Height))
            yield return FormattableString.Invariant($"{where}: the window is {root.DesiredSize.Width:0.##} x {root.DesiredSize.Height:0.##}, not its frame");
        foreach (var problem in DialogLayoutTests.LineProblems(where, root)) yield return problem;
        foreach (var clip in Clipped(root)) yield return $"{where}: {clip}";
    }

    /// <summary>
    /// Every visible element WPF's layout clipped because it wanted more room
    /// than its slot gave it (<see cref="LayoutInformation.GetLayoutClip"/> is
    /// non-null exactly then), by its type, its name or text, and its size.
    /// Not looked at, their children still are: an element that clips by its
    /// own choice (<c>ClipToBounds</c>), and a ScrollViewer's own parts (its
    /// content presenter clips the content it scrolls, by design, and its
    /// scrollbar is the template's). The content inside a ScrollViewer IS
    /// walked: the grid's tiles live there, and they are the fixed-height boxes
    /// this check is for. Hatchling's tests reuse it.
    /// </summary>
    internal static IEnumerable<string> Clipped(FrameworkElement root)
    {
        foreach (var element in Visible(root))
        {
            if (element is not FrameworkElement fe || fe.ClipToBounds || fe is ScrollContentPresenter) continue;
            if (LayoutInformation.GetLayoutClip(fe) is null) continue;
            var slot = LayoutInformation.GetLayoutSlot(fe);
            yield return FormattableString.Invariant(
                $"{fe.GetType().Name} {Describe(fe)} is clipped: {fe.DesiredSize.Width:0.##} x {fe.DesiredSize.Height:0.##} wanted in a slot of {slot.Width:0.##} x {slot.Height:0.##}");
        }
    }

    private static string Describe(FrameworkElement fe) => fe switch
    {
        TextBlock text => $"\"{(text.Inlines.Count == 0 ? text.Text : new TextRange(text.ContentStart, text.ContentEnd).Text)}\"",
        _ when fe.Name.Length > 0 => fe.Name,
        _ => "(unnamed)",
    };

    /// <summary>Every visible element under the root (a collapsed or hidden one takes its subtree with it), a scrollbar's template left out.</summary>
    private static IEnumerable<DependencyObject> Visible(DependencyObject d)
    {
        if (d is UIElement { Visibility: not Visibility.Visible } || d is ScrollBar) yield break;
        yield return d;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            foreach (var child in Visible(VisualTreeHelper.GetChild(d, i))) yield return child;
    }

    // ---- Every view ---------------------------------------------------------------------

    /// <summary>
    /// Every view of the window worth laying out, each laid out unshown at the
    /// window's own size and closed once the caller has walked it (the
    /// enumerator's finally: a caller that stops early still closes it). In
    /// the theme on screen, which the caller chose.
    /// </summary>
    internal static IEnumerable<(string Where, FrameworkElement Root)> EveryView()
    {
        // The empty grid (G19: the library exists with nothing in it).
        using (var bench = new GamesBench())
        {
            yield return ("grid, empty", LaidOut(bench.Window()));
        }

        // Nine tiles, every word far too long, the first one marked as played last.
        using (var bench = new GamesBench(Enumerable.Range(1, 9).Select(LongGame).ToArray()))
        {
            bench.Config.GamesLastPlayed = "long-1";
            yield return ("grid, nine long tiles", LaidOut(bench.Window()));
        }

        // One tile of each condition, one armed.
        using (var bench = Conditions())
        {
            var window = bench.Window();
            GamesBench.Click(GamesBench.Tile(window, 0).Button); // the Reset, armed
            yield return ("grid, every condition, one armed", LaidOut(window));
        }

        // The two refusals, each on its tile's detail line.
        using (var bench = Refusals(out var refuse))
        {
            var window = bench.Window();
            refuse(window);
            yield return ("grid, both refusals", LaidOut(window));
        }

        // The grid under the health warning, with the watch note.
        using (var bench = new GamesBench(new FakeGame("first"), new FakeGame("second")))
        {
            var window = bench.Window();
            window.SetWatch(GamesWatch.Disconnected);
            window.ShowHealthWarning(62);
            yield return ("grid, under the warning", LaidOut(window));
        }

        // A game, its title as long as a title gets.
        using (var bench = new GamesBench(new FakeGame("fake-game") { TitleText = "A game whose title is far longer than any header could hold" }))
        {
            var window = bench.Window();
            window.ShowGame(bench.Host.Games[0]);
            GamesBench.Dress(bench.Games[0].Views[^1]);
            yield return ("a game", LaidOut(window));
        }

        // A game under the warning, with the watch note: every part of the header at once.
        using (var bench = new GamesBench(new FakeGame("fake-game") { TitleText = "A game whose title is far longer than any header could hold" }))
        {
            var window = bench.Window();
            window.ShowGame(bench.Host.Games[0]);
            GamesBench.Dress(bench.Games[0].Views[^1]);
            window.SetWatch(GamesWatch.SignedOut);
            window.ShowHealthWarning(100);
            yield return ("a game, under the warning, not signed in", LaidOut(window));
        }

        // Every listed game, each of its layout states (A26). None until one is listed.
        foreach (var entry in GameCatalog.All)
        {
            using var bench = new GamesBench(new[] { entry });
            var game = bench.Host.Games[0];
            var window = bench.Window();
            window.ShowGame(game);
            if (window.ViewOnScreen is not { } view)
                throw new InvalidOperationException($"{entry.Id} shows no view in the window ({game.Condition})");
            foreach (var state in view.LayoutStates)
            {
                view.ShowLayoutState(state);
                yield return ($"{entry.Id}, {state}", LaidOut(window));
            }
        }
    }

    private static FakeGame LongGame(int n) => new($"long-{n}")
    {
        TitleText = $"Game {n} with a title far longer than its tile could ever show",
        PitchText = "A pitch that goes on and on well past the edge of the tile it sits on",
        TileToShow = new GameTile("A status line that is much too long for one tile's width", (GameTileTone)(n % 3),
                                  "A detail line, also much too long for the width of one tile", "A tag far too long", CanReset: n % 2 == 1),
    };

    /// <summary>A grid of every condition: Ready with a Reset, Ready without, stopped, a save that can't be read, a save from a newer overlay; the second marked as played last.</summary>
    internal static GamesBench Conditions() =>
        new(new FakeGame("ready-reset"),
            new FakeGame("ready") { TileToShow = new GameTile("Run 3 in progress", GameTileTone.Good, "Best: 14 turns", "Played", CanReset: false) },
            new FakeGame("stopped") { Throws = FakeThrows.Attach },
            new FakeGame("unreadable") { Answer = AttachResult.SaveUnreadable },
            new FakeGame("newer") { Answer = AttachResult.SaveFromNewerVersion })
        {
            Config = { GamesLastPlayed = "ready" },
        };

    /// <summary>
    /// A Ready game whose Reset the host refuses and a stopped one whose "Start
    /// it again" it refuses: each has a save pending that the disk won't take
    /// (its temp file held open, as the host's own tests do it), so the host
    /// writes it first and gives up (A38, A49). <paramref name="refuse"/> makes
    /// both clicks on a window of the bench.
    /// </summary>
    internal static GamesBench Refusals(out Action<GamesWindow> refuse)
    {
        var reset = new FakeGame("reset-refused") { DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now) };
        var again = new FakeGame("again-refused") { DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now) };
        var bench = new GamesBench(reset, again);
        reset.Host!.Storage.Write(1, "accrued", SaveUrgency.Soon);
        again.Host!.Storage.Write(1, "handed over", SaveUrgency.Soon);
        bench.Host.Fault(bench.Host.Games[1], new InvalidOperationException("made up"));
        refuse = window =>
        {
            using (bench.FailWrites("reset-refused"))
            {
                GamesBench.Click(GamesBench.Tile(window, 0).Button);
                GamesBench.Click(GamesBench.Tile(window, 0).Button);
            }
            using (bench.FailWrites("again-refused")) GamesBench.Click(GamesBench.Tile(window, 1).Button);
        };
        return bench;
    }

    /// <summary>The window's root laid out at its own size, as WPF lays the shown window out.</summary>
    internal static FrameworkElement LaidOut(Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        return root;
    }

    /// <summary>The theme on screen for the test, on an STA thread, and Default again after it, whatever happened.</summary>
    internal static void OnScreen(string id, Action test) => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        try
        {
            Theme.Apply(id);
            test();
        }
        finally
        {
            Theme.Apply(Theme.DefaultId);
        }
    });
}

/// <summary>
/// A Games window's world for a test: a real <see cref="GamesHost"/> over a
/// temporary games folder of its own (never the real one), its games
/// attached, a fresh config, and the windows it made, closed with it. On the
/// test's STA thread.
/// </summary>
internal sealed class GamesBench : IDisposable
{
    /// <summary>A monitor where the frame fits at its size: no scale in the layout tests.</summary>
    public static readonly Rect Screen = new(0, 0, 1920, 1080);

    private readonly TempGamesRoot _temp = new();
    private readonly List<GamesWindow> _windows = new();

    public GamesBench(params FakeGame[] games)
        : this(games.Select(g => new GameEntry(g.Id, () => g)).ToArray())
    {
        Games = games;
    }

    public GamesBench(IReadOnlyList<GameEntry> entries)
    {
        Store = new GameSaveStore(_temp.Root, () => Clock.UtcNow);
        Host = new GamesHost(Store, entries, Clock, id => Logs.TryGetValue(id, out var log) ? log : Logs[id] = new TestGameLog());
        Host.AttachAll();
    }

    public TestGameClock Clock { get; } = new();

    public GameSaveStore Store { get; }

    public GamesHost Host { get; }

    public OverlayConfig Config { get; init; } = new();

    public Dictionary<string, TestGameLog> Logs { get; } = new();

    /// <summary>The fakes behind the entries, when the bench was made from fakes.</summary>
    public IReadOnlyList<FakeGame> Games { get; } = Array.Empty<FakeGame>();

    public TempGamesRoot Temp => _temp;

    /// <summary>A new window over the bench, never shown.</summary>
    public GamesWindow Window()
    {
        var window = new GamesWindow(Host, Config, Screen);
        _windows.Add(window);
        return window;
    }

    /// <summary>The store's writes for this game fail while the answer is held: its temp file open with no sharing.</summary>
    public FileStream FailWrites(string id)
    {
        Directory.CreateDirectory(_temp.FolderOf(id));
        return new FileStream(_temp.FileOf(id, GameSaveStore.TempFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose()
    {
        foreach (var window in _windows) window.Close();
        _temp.Dispose();
    }

    // ---- Reading and touching a window, as a player would ------------------------------

    /// <summary>One tile's parts, read off its card (GamesWindow.Tiles.cs builds them in this order).</summary>
    public sealed record TileParts(Border Card, Border Well, TextBlock Title, TextBlock Tag, TextBlock Pitch, TextBlock Status, TextBlock Detail, Button Button)
    {
        public string Label => ((TextBlock)Button.Content).Text;
    }

    public static IReadOnlyList<Border> Cards(GamesWindow window) => window.Tiles.Children.Cast<Border>().ToList();

    public static TileParts Tile(GamesWindow window, int index) => Parts((Border)window.Tiles.Children[index]);

    public static TileParts Parts(Border card)
    {
        var dock = (DockPanel)card.Child;
        var buttonRow = (Grid)dock.Children[1];
        var texts = (StackPanel)dock.Children[2];
        var titleRow = (DockPanel)texts.Children[0];
        return new TileParts(card, (Border)dock.Children[0], (TextBlock)titleRow.Children[1], (TextBlock)titleRow.Children[0],
            (TextBlock)texts.Children[1], (TextBlock)texts.Children[2], (TextBlock)texts.Children[3], (Button)buttonRow.Children[0]);
    }

    /// <summary>A press anywhere: the window's preview sees it first, from this element (it tunnels from the window down).</summary>
    public static void Press(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });

    /// <summary>A click on a button: its press seen by the window, then its Click.</summary>
    public static void Click(Button button)
    {
        Press(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    /// <summary>A click on a card itself (not its button): press and release on it.</summary>
    public static void ClickCard(Border card)
    {
        Press(card);
        card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    }

    public static void Enter(UIElement element) =>
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

    public static void Leave(UIElement element) =>
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

    /// <summary>
    /// A fake game's view made to look like a game's page: a heading, a
    /// paragraph that wraps, a button and a long line that trims, in the
    /// theme's sizes, filling the view area (its Root is a Grid made on first
    /// use, which the window has asked for by now).
    /// </summary>
    public static void Dress(FakeGameView view)
    {
        var root = (Grid)view.Root;
        root.SetResourceReference(TextElement.FontFamilyProperty, "Theme.Font.Family");
        var page = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var heading = new TextBlock { Text = "Camp", TextTrimming = TextTrimming.CharacterEllipsis };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.18");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Bright");
        var paragraph = new TextBlock
        {
            Text = "A fake game's page: a paragraph long enough to wrap onto a second and a third line inside the view area, as a game's own text does when it explains a rule or tells a little of its story.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        paragraph.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.12");
        paragraph.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Text");
        var line = new TextBlock { Text = string.Join(" · ", Enumerable.Repeat("a line that runs far past the view's edge", 6)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) };
        line.SetResourceReference(TextBlock.FontSizeProperty, "Theme.Size.11");
        line.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Text.Dim");
        var button = new Button { Content = "Start a run", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SaveButtonStyle");
        page.Children.Add(heading);
        page.Children.Add(paragraph);
        page.Children.Add(line);
        page.Children.Add(button);
        root.Children.Add(page);
    }
}
