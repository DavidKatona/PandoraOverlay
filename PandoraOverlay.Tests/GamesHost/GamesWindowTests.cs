using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's behaviour (G1, G13, G14, G18, G19, A11, A15, A16, A31,
/// A38, A49), built unshown as the Settings dialog's tests build theirs, over
/// a real <see cref="GamesHost"/> in a temporary games folder (never the real
/// one) with games that record every call. What only a shown window over the
/// game can prove (real keys, real focus, real clicks) is the owner's to try.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class GamesWindowTests
{
    private static void Run(Action test) => GamesLayoutTests.OnScreen(Theme.DefaultId, test);

    private static FakeGame[] Fakes(int count) =>
        Enumerable.Range(1, count).Select(n => new FakeGame($"game-{n}") { TitleText = $"Game {n}" }).ToArray();

    private static object Resource(GamesWindow window, string key) => window.FindResource(key);

    // ---- The grid (G1, G19) ---------------------------------------------------------------

    [Theory]
    [InlineData(0, "0 games")]
    [InlineData(1, "1 game")]
    [InlineData(6, "6 games")]
    [InlineData(9, "9 games")]
    public void TheGridShowsEveryGameInTheCatalogsOrder(int count, string subtitle) => Run(() =>
    {
        using var bench = new GamesBench(Fakes(count));
        var window = bench.Window();

        Assert.Null(window.OnScreen);
        Assert.Equal("Games", window.TitleText.Text);
        Assert.Equal(subtitle, window.SubtitleText.Text);
        Assert.Equal(Visibility.Collapsed, window.BackButton.Visibility);
        Assert.Equal(Enumerable.Range(1, count).Select(n => $"Game {n}"),
                     GamesBench.Cards(window).Select(c => GamesBench.Parts(c).Title.Text));
    });

    /// <summary>Three to a row at fixed places: the seventh game makes the grid scroll and moves no tile (G1).</summary>
    [Fact]
    public void TilesKeepTheirPlacesWhenTheGridScrolls() => Run(() =>
    {
        Point[] Places(int count, out double scrollable)
        {
            using var bench = new GamesBench(Fakes(count));
            var window = bench.Window();
            var root = GamesLayoutTests.LaidOut(window);
            scrollable = window.GridScroll.ScrollableHeight;
            return GamesBench.Cards(window).Select(c => c.TranslatePoint(new Point(0, 0), root)).ToArray();
        }

        var six = Places(6, out var sixScroll);
        var nine = Places(9, out var nineScroll);

        Assert.Equal(0, sixScroll);
        Assert.True(nineScroll > 0);
        Assert.Equal(six, nine.Take(6));
        Assert.Equal(new Point(1 + GamesFrame.GridSidePadding, 1 + GamesFrame.HeaderHeight + GamesFrame.GridTopPadding), six[0]);
        Assert.Equal(six[0].X + 2 * (GamesFrame.TileWidth + GamesFrame.TileGap), six[2].X);
        Assert.Equal(six[0].Y + GamesFrame.TileHeight + GamesFrame.TileGap, six[3].Y);
    });

    [Fact]
    public void AReadyTileShowsTheGamesOwnWordsAndArt() => Run(() =>
    {
        var game = new FakeGame { TileToShow = new GameTile("Run 2 in progress", GameTileTone.Good, "Best: 9 turns", "New", CanReset: true) };
        using var bench = new GamesBench(game);
        var tile = GamesBench.Tile(bench.Window(), 0);

        Assert.Equal(("Fake game", "New", "A game that records what it is asked.", "Run 2 in progress", "Best: 9 turns"),
                     (tile.Title.Text, tile.Tag.Text, tile.Pitch.Text, tile.Status.Text, tile.Detail.Text));
        Assert.Same(Resource(bench.Window(), "Theme.Text.Good"), tile.Status.Foreground);
        Assert.Equal(GamesFrame.TileArtWidth, ((FrameworkElement)tile.Well.Child).Width);
        Assert.Equal("Reset", tile.Label);
        Assert.Equal(Visibility.Visible, tile.Button.Visibility);
        Assert.Equal(GamesFrame.TileButtonWidth, tile.Button.Width);
    });

    [Fact]
    public void ATileWithoutResetHasNoButtonAndKeepsItsRow() => Run(() =>
    {
        using var bench = new GamesBench(new FakeGame { TileToShow = new GameTile("Ready", GameTileTone.Attention, null, null, CanReset: false) });
        var window = bench.Window();
        var tile = GamesBench.Tile(window, 0);

        Assert.Equal(Visibility.Hidden, tile.Button.Visibility);
        Assert.Same(Resource(window, "Theme.Text.Caution"), tile.Status.Foreground);
        Assert.Equal("", tile.Detail.Text);
        Assert.Equal("", tile.Tag.Text);
    });

    [Fact]
    public void EachConditionShowsTheHostsWordsAndItsOneButton() => Run(() =>
    {
        using var bench = GamesLayoutTests.Conditions();
        var window = bench.Window();
        var tiles = Enumerable.Range(0, 5).Select(i => GamesBench.Tile(window, i)).ToList();

        Assert.Equal(("Stopped after an error", "Nothing is lost: it starts from its last save.", "Start it again"),
                     (tiles[2].Status.Text, tiles[2].Detail.Text, tiles[2].Label));
        Assert.Equal(("Its save could not be read", "The file is kept, even if you start fresh.", "Start fresh"),
                     (tiles[3].Status.Text, tiles[3].Detail.Text, tiles[3].Label));
        Assert.Equal(("Its save is from a newer overlay", "Update the overlay to keep playing it.", "Start fresh"),
                     (tiles[4].Status.Text, tiles[4].Detail.Text, tiles[4].Label));
        foreach (var stopped in tiles.Skip(2))
        {
            // Its title where its art would be; nothing of the game's own.
            Assert.Equal(stopped.Title.Text, Assert.IsType<TextBlock>(stopped.Well.Child).Text);
            Assert.Equal("", stopped.Tag.Text);
        }
    });

    /// <summary>The tile played last wears the highlight, under the mouse too (A46); another wears the hover line under the mouse.</summary>
    [Fact]
    public void ThePlayedLastTileIsMarked() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(3)) { Config = { GamesLastPlayed = "game-2" } };
        var window = bench.Window();
        var (first, second) = (GamesBench.Tile(window, 0), GamesBench.Tile(window, 1));

        Assert.Same(Resource(window, "Theme.Card.Line"), first.Card.BorderBrush);
        Assert.Same(Resource(window, "Theme.Text.Highlight"), second.Card.BorderBrush);
        GamesBench.Enter(first.Card);
        GamesBench.Enter(second.Card);
        Assert.Same(Resource(window, "Theme.Card.HoverLine"), first.Card.BorderBrush);
        Assert.Same(Resource(window, "Theme.Text.Highlight"), second.Card.BorderBrush);
        GamesBench.Leave(first.Card);
        Assert.Same(Resource(window, "Theme.Card.Line"), first.Card.BorderBrush);
    });

    // ---- The window's scale (GamesScale, A75) -------------------------------------------------

    /// <summary>
    /// The whole window is drawn at the player's scale by one transform over
    /// the fixed layout: the frame stays 800 x 552 and the view area 798 x 510
    /// inside it (nothing is re-laid out), the window takes the frame times the
    /// scale, centred on the monitor on whole units, and the view area's
    /// transform to the window is the scale, which is what the presenter
    /// multiplies the display scale by (PresenterTests).
    /// </summary>
    [Theory]
    [InlineData(1.0, 2560, 1440, 1.0)]
    [InlineData(1.5, 2560, 1440, 1.5)]
    [InlineData(2.0, 3840, 2160, 2.0)]
    [InlineData(2.0, 1920, 1080, 1032.0 / 552)] // clamped to the screen
    [InlineData(0.75, 1920, 1080, 0.75)]
    public void TheWholeWindowIsDrawnAtTheChosenScale(double chosen, double width, double height, double drawn) => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1)) { Config = new OverlayConfig { GamesScale = chosen } };
        var window = bench.Window(new Rect(0, 0, width, height));
        var root = GamesLayoutTests.LaidOut(window);

        if (drawn == 1) Assert.True(window.RootPanel.LayoutTransform.Value.IsIdentity);
        else Assert.Equal(new Matrix(drawn, 0, 0, drawn, 0, 0), window.RootPanel.LayoutTransform.Value);

        Assert.Equal((GamesFrame.Width, GamesFrame.Height), (window.RootPanel.ActualWidth, window.RootPanel.ActualHeight));
        Assert.Equal(GamesFrame.Width * drawn, root.DesiredSize.Width, 6);
        Assert.Equal(GamesFrame.Height * drawn, root.DesiredSize.Height, 6);
        Assert.Equal(Math.Round((width - GamesFrame.Width * drawn) / 2), window.Left);
        Assert.Equal(Math.Round((height - GamesFrame.Height * drawn) / 2), window.Top);

        // Inside the frame nothing is scaled: the view area (where a game's
        // board sits) and the grid are at 1 to the root panel, so their
        // transform to the window (what PixelSurface reads, A17; an unshown
        // window's panel has no visual parent to walk to) is the panel's one
        // scale above, asserted first.
        foreach (var inside in new FrameworkElement[] { window.GameHost, window.GridScroll })
        {
            var toPanel = ((Transform)inside.TransformToAncestor(window.RootPanel)).Value;
            Assert.Equal((1.0, 0.0, 0.0, 1.0), (toPanel.M11, toPanel.M12, toPanel.M21, toPanel.M22));
        }
    });

    // ---- A game in the frame (G13, G14, A31) ------------------------------------------------

    [Fact]
    public void ATileOpensItsGameAndBackReturnsWithTheViewDisposed() => Run(() =>
    {
        var games = Fakes(2);
        using var bench = new GamesBench(games);
        var window = bench.Window();

        GamesBench.ClickCard(GamesBench.Tile(window, 1).Card);

        var hosted = bench.Host.Games[1];
        Assert.Same(hosted, window.OnScreen);
        var view = Assert.Single(games[1].Views);
        Assert.Same(view.Root, window.GameHost.Child);
        Assert.Equal(Visibility.Visible, window.GameHost.Visibility);
        Assert.Equal(Visibility.Collapsed, window.GridScroll.Visibility);
        Assert.Equal(Visibility.Visible, window.BackButton.Visibility);
        Assert.Equal(("Game 2", ""), (window.TitleText.Text, window.SubtitleText.Text));
        Assert.Equal(("game-2", "game-2"), (bench.Config.GamesView, bench.Config.GamesLastPlayed));

        GamesBench.Click(window.BackButton);

        Assert.Null(window.OnScreen);
        Assert.Equal(new[] { "Dispose" }, view.Calls);
        Assert.Null(window.GameHost.Child);
        Assert.Equal(("Games", "2 games"), (window.TitleText.Text, window.SubtitleText.Text));
        Assert.Equal(("", "game-2"), (bench.Config.GamesView, bench.Config.GamesLastPlayed));
        Assert.Same(Resource(window, "Theme.Text.Highlight"), GamesBench.Tile(window, 1).Card.BorderBrush);
    });

    /// <summary>The button of a tile is not the tile: its click does not open the game.</summary>
    [Fact]
    public void ATilesButtonDoesNotOpenTheGame() => Run(() =>
    {
        using var bench = new GamesBench(new FakeGame());
        var window = bench.Window();

        GamesBench.Click(GamesBench.Tile(window, 0).Button);

        Assert.Null(window.OnScreen);
    });

    [Fact]
    public void ANotReadyTileDoesNotOpen() => Run(() =>
    {
        using var bench = GamesLayoutTests.Conditions();
        var window = bench.Window();

        foreach (var i in new[] { 2, 3, 4 }) GamesBench.ClickCard(GamesBench.Tile(window, i).Card);

        Assert.Null(window.OnScreen);
        Assert.Equal("", bench.Config.GamesView);
    });

    /// <summary>Where you left off (G14): a second window from that config opens on the game, a fresh view of it.</summary>
    [Fact]
    public void ReopeningShowsTheGameItWasOn() => Run(() =>
    {
        var games = Fakes(2);
        using var bench = new GamesBench(games);
        var first = bench.Window();
        first.ShowGame(bench.Host.Games[0]);
        first.Close();

        Assert.Equal("game-1", bench.Config.GamesView); // closing keeps it: that is where you left off
        Assert.Equal(new[] { "Dispose" }, games[0].Views[0].Calls);

        var second = bench.Window();
        Assert.Same(bench.Host.Games[0], second.OnScreen);
        Assert.Equal(2, games[0].Views.Count);
        Assert.Same(games[0].Views[1].Root, second.GameHost.Child);
    });

    [Fact]
    public void ReopeningOnAGameThatCantPlayOrIsGoneShowsTheGrid() => Run(() =>
    {
        using var bench = GamesLayoutTests.Conditions();
        bench.Config.GamesView = "stopped";
        var window = bench.Window();
        Assert.Null(window.OnScreen);
        Assert.Equal("", bench.Config.GamesView); // the grid is on screen now

        bench.Config.GamesView = "no-longer-listed";
        window = bench.Window();
        Assert.Null(window.OnScreen);
        Assert.Equal("", bench.Config.GamesView);
        Assert.Equal(5, window.Tiles.Children.Count);
    });

    [Fact]
    public void AGameThatFaultsMakingItsViewStaysOnTheGrid() => Run(() =>
    {
        var game = new FakeGame { Throws = FakeThrows.CreateView };
        using var bench = new GamesBench(game);
        var window = bench.Window();

        GamesBench.ClickCard(GamesBench.Tile(window, 0).Card);

        Assert.Null(window.OnScreen);
        Assert.Equal(GameCondition.Faulted, bench.Host.Games[0].Condition);
        Assert.Equal("Stopped after an error", GamesBench.Tile(window, 0).Status.Text);
        Assert.Equal("", bench.Config.GamesView);
    });

    /// <summary>The game on screen faulting (its own handler reported it): its view is disposed and the grid shows its tile as stopped.</summary>
    [Fact]
    public void TheGameOnScreenFaultingReturnsToTheGrid() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);

        game.Host!.Fault(new InvalidOperationException("made up"));

        Assert.Null(window.OnScreen);
        Assert.Equal(new[] { "Dispose" }, game.Views[0].Calls);
        Assert.Equal(Visibility.Visible, window.GridScroll.Visibility);
        Assert.Equal("Stopped after an error", GamesBench.Tile(window, 0).Status.Text);
        Assert.Equal("", bench.Config.GamesView);
    });

    [Fact]
    public void ClosingDisposesTheViewAndKeepsWhereItWas() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);

        window.Close();

        Assert.Equal(new[] { "Dispose" }, game.Views[0].Calls);
        Assert.Equal(Id, bench.Config.GamesView);
        game.Host!.TileChanged(); // unsubscribed: nothing of the closed window runs
    });

    private const string Id = "fake-game";

    // ---- The tile's button (A38, A49) ------------------------------------------------------------

    [Fact]
    public void ResetArmsOnTheFirstClickAndResetsOnTheSecond() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        var tile = GamesBench.Tile(window, 0);

        GamesBench.Click(tile.Button);

        Assert.Equal("Click again to reset", tile.Label);
        Assert.Same(Resource(window, "DangerButtonStyle"), tile.Button.Style);
        Assert.Equal(GamesFrame.TileButtonWidth, tile.Button.Width); // nothing moves between the two clicks
        Assert.DoesNotContain("Reset", game.Calls);

        GamesBench.Click(tile.Button);

        Assert.Contains("Reset", game.Calls);
        var after = GamesBench.Tile(window, 0);
        Assert.Same(tile.Card, after.Card); // redrawn in place (the host's TileChanged)
        Assert.Equal("Reset", after.Label);
        Assert.Same(Resource(window, "NeutralButtonStyle"), after.Button.Style);
    });

    [Fact]
    public void LeavingTheTileDisarms() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        var tile = GamesBench.Tile(window, 0);
        GamesBench.Click(tile.Button);

        GamesBench.Leave(tile.Card);

        Assert.Equal("Reset", tile.Label);
        Assert.Same(Resource(window, "NeutralButtonStyle"), tile.Button.Style);
        GamesBench.Click(tile.Button); // arms again, does not reset
        Assert.DoesNotContain("Reset", game.Calls);
    });

    [Fact]
    public void AClickAnywhereElseDisarms() => Run(() =>
    {
        var games = Fakes(2);
        using var bench = new GamesBench(games);
        var window = bench.Window();
        var (first, second) = (GamesBench.Tile(window, 0), GamesBench.Tile(window, 1));

        GamesBench.Click(first.Button);
        GamesBench.Press(window.SubtitleText); // the header
        Assert.Equal("Reset", first.Label);

        GamesBench.Click(first.Button);
        GamesBench.Click(second.Button); // another tile's button: the first disarms, the second arms
        Assert.Equal(("Reset", "Click again to reset"), (first.Label, second.Label));
        GamesBench.Click(first.Button); // a first click again: arms, resets nothing
        Assert.DoesNotContain("Reset", games[0].Calls);
        Assert.DoesNotContain("Reset", games[1].Calls);
    });

    [Fact]
    public void StartFreshOnAHeldSaveArmsThenSetsItAside() => Run(() =>
    {
        using var bench = GamesLayoutTests.Conditions();
        var window = bench.Window();
        var tile = GamesBench.Tile(window, 3);

        GamesBench.Click(tile.Button);
        Assert.Equal("Click again to start fresh", tile.Label);
        Assert.Equal(GameCondition.SaveUnreadable, bench.Host.Games[3].Condition);

        GamesBench.Click(tile.Button);

        Assert.Equal(GameCondition.Ready, bench.Host.Games[3].Condition);
        var rebuilt = GamesBench.Tile(window, 3);
        Assert.NotSame(tile.Card, rebuilt.Card); // a new condition: the tile rebuilt
        Assert.Equal("Ready to play", rebuilt.Status.Text);
        Assert.Equal(5, window.Tiles.Children.Count);
    });

    [Fact]
    public void StartItAgainIsOneClick() => Run(() =>
    {
        var made = new List<FakeGame>();
        var entry = new GameEntry(Id, () =>
        {
            var game = new FakeGame(Id) { Throws = made.Count == 0 ? FakeThrows.Attach : FakeThrows.None };
            made.Add(game);
            return game;
        });
        using var bench = new GamesBench(new[] { entry });
        var window = bench.Window();
        Assert.Equal("Start it again", GamesBench.Tile(window, 0).Label);

        GamesBench.Click(GamesBench.Tile(window, 0).Button);

        Assert.Equal(2, made.Count);
        Assert.Equal(GameCondition.Ready, bench.Host.Games[0].Condition);
        Assert.Equal("Ready to play", GamesBench.Tile(window, 0).Status.Text);
    });

    /// <summary>Both refusals say so on the tile's detail line, in place (A38, A49), and arming or clicking again takes the line away.</summary>
    [Fact]
    public void ARefusalSaysSoOnItsTileUntilTheButtonIsUsedAgain() => Run(() =>
    {
        using var bench = GamesLayoutTests.Refusals(out var refuse);
        var window = bench.Window();
        var (reset, again) = (GamesBench.Tile(window, 0), GamesBench.Tile(window, 1));

        refuse(window);

        Assert.Equal("Not reset: its save could not be kept first.", reset.Detail.Text);
        Assert.Same(Resource(window, "Theme.Text.Caution"), reset.Detail.Foreground);
        Assert.Equal("Reset", reset.Label); // disarmed
        Assert.Equal(GameCondition.Ready, bench.Host.Games[0].Condition);
        Assert.Equal("Not started: it could not be saved first.", again.Detail.Text);
        Assert.Equal(GameCondition.Faulted, bench.Host.Games[1].Condition);
        Assert.Same(again.Card, GamesBench.Tile(window, 1).Card); // nothing changed, nothing rebuilt

        GamesBench.Click(reset.Button); // armed again: the line is the game's again
        Assert.Equal("No runs yet", reset.Detail.Text);
        Assert.Same(Resource(window, "Theme.Text.Dim"), reset.Detail.Foreground);

        GamesBench.Click(again.Button); // the disk works again: started
        Assert.Equal(GameCondition.Ready, bench.Host.Games[1].Condition);
        Assert.Equal("Ready to play", GamesBench.Tile(window, 1).Status.Text);
    });

    /// <summary>A Reset that throws answers false too; the game is then stopped and its tile a stopped game's, without the refusal line.</summary>
    [Fact]
    public void AResetThatThrowsShowsTheStoppedGameNotARefusal() => Run(() =>
    {
        using var bench = new GamesBench(new FakeGame { Throws = FakeThrows.Reset });
        var window = bench.Window();
        GamesBench.Click(GamesBench.Tile(window, 0).Button);
        GamesBench.Click(GamesBench.Tile(window, 0).Button);

        var tile = GamesBench.Tile(window, 0);
        Assert.Equal(GameCondition.Faulted, bench.Host.Games[0].Condition);
        Assert.Equal(("Stopped after an error", "Nothing is lost: it starts from its last save."), (tile.Status.Text, tile.Detail.Text));
    });

    [Fact]
    public void ATileChangeRedrawsInPlace() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        var before = GamesBench.Tile(window, 0);

        game.TileToShow = new GameTile("Run 4 in progress", GameTileTone.Attention, "Hungry", "Run", CanReset: false);
        game.Host!.TileChanged();

        var after = GamesBench.Tile(window, 0);
        Assert.Same(before.Card, after.Card);
        Assert.Same(before.Status, after.Status);
        Assert.Equal(("Run 4 in progress", "Hungry", "Run"), (after.Status.Text, after.Detail.Text, after.Tag.Text));
        Assert.Equal(Visibility.Hidden, after.Button.Visibility);
    });

    /// <summary>A redraw does not disarm (A54): the game's tile changing by itself between the two clicks keeps the Reset armed, and the next click resets.</summary>
    [Fact]
    public void ARedrawKeepsAnArmedResetArmed() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        var tile = GamesBench.Tile(window, 0);
        GamesBench.Click(tile.Button);

        game.TileToShow = new GameTile("5 of 21 species", GameTileTone.Neutral, "1,240 amber", "New", CanReset: true);
        game.Host!.TileChanged();

        Assert.Equal("Click again to reset", tile.Label);
        Assert.Same(Resource(window, "DangerButtonStyle"), tile.Button.Style);
        Assert.Equal("5 of 21 species", tile.Status.Text);

        GamesBench.Click(tile.Button);

        Assert.Single(game.Calls, "Reset");
    });

    /// <summary>A redraw that takes the button away disarms: when the tile offers it again, it is a first click again.</summary>
    [Fact]
    public void ARedrawWithoutTheButtonDisarms() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        var tile = GamesBench.Tile(window, 0);
        GamesBench.Click(tile.Button);

        game.TileToShow = new GameTile("Mid-run", GameTileTone.Neutral, null, null, CanReset: false);
        game.Host!.TileChanged();
        Assert.Equal(Visibility.Hidden, tile.Button.Visibility);

        game.TileToShow = new GameTile("Ready to play", GameTileTone.Neutral, null, null, CanReset: true);
        game.Host!.TileChanged();
        Assert.Equal("Reset", tile.Label);
        Assert.Same(Resource(window, "NeutralButtonStyle"), tile.Button.Style);

        GamesBench.Click(tile.Button);
        Assert.Equal("Click again to reset", tile.Label); // only armed
        Assert.DoesNotContain("Reset", game.Calls);
    });

    /// <summary>A game that reports a fault of its own while making its view, and still hands the view back: nothing of it goes on screen.</summary>
    [Fact]
    public void AGameThatFaultsWhileMakingItsViewStaysOnTheGrid() => Run(() =>
    {
        var game = new FakeGame { DuringCreateView = host => host.Fault(new InvalidOperationException("made up")) };
        using var bench = new GamesBench(game);
        var window = bench.Window();

        GamesBench.ClickCard(GamesBench.Tile(window, 0).Card);

        Assert.Null(window.OnScreen);
        Assert.Null(window.GameHost.Child);
        Assert.Equal(new[] { "Dispose" }, Assert.Single(game.Views).Calls);
        Assert.Equal("Stopped after an error", GamesBench.Tile(window, 0).Status.Text);
        Assert.Equal("", bench.Config.GamesView);
    });

    /// <summary>A game that keeps its tile's picture and hands back the same element every time: every tile lets go of it, so a second build and a second window show it again.</summary>
    [Fact]
    public void ATileLetsGoOfItsArt() => Run(() =>
    {
        var art = new Border { Width = GameFrame.TileArtWidth, Height = GameFrame.TileArtHeight };
        var game = new FakeGame { KeptTileArt = art };
        using var bench = new GamesBench(game);
        var first = bench.Window();
        GamesBench.ClickCard(GamesBench.Tile(first, 0).Card);
        GamesBench.Click(first.BackButton);
        Assert.Same(art, GamesBench.Tile(first, 0).Well.Child);
        first.Close();

        var second = bench.Window();

        Assert.Equal(GameCondition.Ready, bench.Host.Games[0].Condition);
        Assert.Same(art, GamesBench.Tile(second, 0).Well.Child);
    });

    [Fact]
    public void ATileThatThrowsWhenRedrawnShowsTheStoppedGame() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();

        game.Throws = FakeThrows.Tile;
        game.Host!.TileChanged();

        Assert.Equal(GameCondition.Faulted, bench.Host.Games[0].Condition);
        Assert.Single(window.Tiles.Children);
        Assert.Equal("Stopped after an error", GamesBench.Tile(window, 0).Status.Text);
    });

    // ---- The health warning (G18, A15) and the watch note (A16) -----------------------------------

    [Fact]
    public void TheWarningOverTheGrid() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(2));
        var window = bench.Window();
        var acknowledged = 0;
        window.WarningAcknowledged += () => acknowledged++;

        window.ShowHealthWarning(62);

        Assert.Equal(Visibility.Visible, window.WarningPanel.Visibility);
        Assert.Equal("Health 62%", window.WarningHealth.Text);
        Assert.Equal("Back to Games", window.WarningButtonText.Text);
        Assert.Equal("2 games", window.SubtitleText.Text);

        GamesBench.Click(window.WarningButton);

        Assert.Equal(Visibility.Collapsed, window.WarningPanel.Visibility);
        Assert.Equal(1, acknowledged);
    });

    [Fact]
    public void TheWarningOverAGamePausesItAndKeepPlayingResumesIt() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var view = game.Views[0];
        var acknowledged = 0;
        window.WarningAcknowledged += () => acknowledged++;

        window.ShowHealthWarning(62);
        window.ShowHealthWarning(48); // again while up: only the percent

        Assert.Equal(new[] { "Pause" }, view.Calls);
        Assert.Equal("Health 48%", window.WarningHealth.Text);
        Assert.Equal("Keep playing", window.WarningButtonText.Text);
        Assert.Equal("Paused", window.SubtitleText.Text);
        Assert.Equal(0, acknowledged);

        GamesBench.Click(window.WarningButton);

        Assert.Equal(new[] { "Pause", "Resume" }, view.Calls);
        Assert.Equal(Visibility.Collapsed, window.WarningPanel.Visibility);
        Assert.Equal("", window.SubtitleText.Text);
        Assert.Equal(1, acknowledged);
    });

    /// <summary>A window opened on a game under a warning already up shows the view paused; back to the grid, the button says so.</summary>
    [Fact]
    public void TheWarningFollowsTheView() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        window.ShowHealthWarning(30);
        window.ShowGame(bench.Host.Games[0]);

        Assert.Equal(new[] { "Pause" }, game.Views[0].Calls);
        Assert.Equal(("Keep playing", "Paused"), (window.WarningButtonText.Text, window.SubtitleText.Text));

        GamesBench.Click(window.BackButton);

        Assert.Equal(new[] { "Pause", "Dispose" }, game.Views[0].Calls);
        Assert.Equal(("Back to Games", "1 game"), (window.WarningButtonText.Text, window.SubtitleText.Text));
    });

    [Theory]
    [InlineData("Watching", "")]
    [InlineData("NotInGame", "")]
    [InlineData("Disconnected", "Disconnected · health not watched")]
    [InlineData("SignedOut", "Not signed in · health not watched")]
    public void TheWatchNoteSaysWhenHealthIsNotWatched(string state, string note) => Run(() =>
    {
        using var bench = new GamesBench();
        var window = bench.Window();
        window.SetWatch(GamesWatch.Disconnected);

        window.SetWatch(Enum.Parse<GamesWatch>(state));

        Assert.Equal(note, window.WatchNote.Text);
        Assert.Equal(note.Length > 0 ? Visibility.Visible : Visibility.Collapsed, window.WatchNote.Visibility);
    });

    // ---- The sound group (A82) --------------------------------------------------------------

    /// <summary>The pips lit (the growth bar's fill) up to the step and unlit (the slots meter's track, A89) above it, every one unlit while muted.</summary>
    private static void AssertPips(GamesWindow window, int lit)
    {
        var pips = window.SoundPips.Children.Cast<Border>().ToList();
        Assert.Equal(GamesFrame.SoundPips, pips.Count);
        for (var i = 0; i < pips.Count; i++)
            Assert.Same(Resource(window, i < lit ? "Theme.Storage.Growth" : "Theme.Storage.SlotsTrack"), pips[i].Background);
    }

    /// <summary>In every theme, each pip's corner is the theme's progress corner held to the frame's <see cref="GamesFrame.SoundPipMaxCorner"/>, on all four corners: rising bars, never dots (A89).</summary>
    [Theory]
    [MemberData(nameof(GamesLayoutTests.Themes), MemberType = typeof(GamesLayoutTests))]
    public void EveryPipsCornerIsTheThemesHeldToTheFramesMost(string id) => GamesLayoutTests.OnScreen(id, () =>
    {
        using var bench = new GamesBench(Fakes(1));
        var window = bench.Window();
        var most = Math.Min(Theme.Current.Corner.Progress.Value, GamesFrame.SoundPipMaxCorner);

        var pips = window.SoundPips.Children.Cast<Border>().ToList();
        Assert.Equal(GamesFrame.SoundPips, pips.Count);
        Assert.All(pips, p => Assert.Equal(new CornerRadius(most), p.CornerRadius));
    });

    /// <summary>A fresh config: sound on at step 3, the speaker's glyph, both steps enabled, three pips lit; the pips' shapes are the frame's.</summary>
    [Fact]
    public void TheGroupShowsTheLevelItOpensWith() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1));
        var window = bench.Window();

        Assert.Equal(GamesText.SoundOn, window.SoundToggle.Content);
        Assert.True(window.SoundDown.IsEnabled);
        Assert.True(window.SoundUp.IsEnabled);
        AssertPips(window, 3);
        var pips = window.SoundPips.Children.Cast<Border>().ToList();
        Assert.Equal(new double[] { 4, 6, 8, 10, 12 }, pips.Select(p => p.Height));
        Assert.All(pips, p => Assert.Equal((GamesFrame.SoundPipWidth, VerticalAlignment.Bottom), (p.Width, p.VerticalAlignment)));
        Assert.All(new[] { window.SoundToggle, window.SoundDown, window.SoundUp }, b => Assert.False(b.Focusable)); // Esc and J stay the window's
    });

    [Fact]
    public void AWindowOpenedMutedShowsTheMutedSpeakerAndNoPip() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1));
        bench.Config.GamesSoundMuted = true;
        var window = bench.Window();

        Assert.Equal(GamesText.SoundOff, window.SoundToggle.Content);
        AssertPips(window, 0);
    });

    /// <summary>The speaker (PLAN-SOUND 4.3): on → muted, every sound stopped, nothing previewed; muted → on, the preview at the step.</summary>
    [Fact]
    public void TheSpeakerMutesAndUnmutes() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1));
        var window = bench.Window();
        var stops = bench.Speaker.Stops;

        GamesBench.Click(window.SoundToggle);

        Assert.True(bench.Config.GamesSoundMuted);
        Assert.Equal(stops + 1, bench.Speaker.Stops);
        Assert.Empty(bench.Speaker.Played);
        Assert.Equal(GamesText.SoundOff, window.SoundToggle.Content);
        AssertPips(window, 0);

        GamesBench.Click(window.SoundToggle);

        Assert.False(bench.Config.GamesSoundMuted);
        var (samples, gain) = Assert.Single(bench.Speaker.Played);
        Assert.Equal(GamesSound.Preview.Samples.ToArray(), samples);
        Assert.Equal(GamesSound.GainOf(3), gain);
        Assert.Equal(GamesText.SoundOn, window.SoundToggle.Content);
        AssertPips(window, 3);
    });

    /// <summary>`−` and `+` (4.3): a step each way, sound on, the preview at the new step; `−` disabled at 1 and `+` at 5.</summary>
    [Fact]
    public void TheStepsMoveOneAtATimeAndStopAtTheEnds() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1));
        var window = bench.Window();
        void Click(Button button)
        {
            GamesBench.Click(button);
            bench.Clock.Advance(TimeSpan.FromSeconds(1)); // past the preview's own 40 ms
        }

        Click(window.SoundDown);
        Assert.Equal(2, bench.Config.GamesSoundVolume);
        AssertPips(window, 2);
        Click(window.SoundDown);
        Assert.Equal(1, bench.Config.GamesSoundVolume);
        AssertPips(window, 1);
        Assert.False(window.SoundDown.IsEnabled);
        Assert.True(window.SoundUp.IsEnabled);

        for (var i = 0; i < 4; i++) Click(window.SoundUp);
        Assert.Equal(5, bench.Config.GamesSoundVolume);
        AssertPips(window, 5);
        Assert.False(window.SoundUp.IsEnabled);
        Assert.True(window.SoundDown.IsEnabled);

        Assert.Equal(new[] { 2, 1, 2, 3, 4, 5 }.Select(GamesSound.GainOf), bench.Speaker.Played.Select(p => p.Gain));
    });

    /// <summary>Muted, `−` or `+` unmutes (4.3), and previews at the new step.</summary>
    [Fact]
    public void AStepWhileMutedUnmutes() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1));
        bench.Config.GamesSoundMuted = true;
        var window = bench.Window();

        GamesBench.Click(window.SoundUp);

        Assert.Equal((4, false), (bench.Config.GamesSoundVolume, bench.Config.GamesSoundMuted));
        Assert.Equal(GamesText.SoundOn, window.SoundToggle.Content);
        AssertPips(window, 4);
        Assert.Equal(GamesSound.GainOf(4), Assert.Single(bench.Speaker.Played).Gain);
    });

    /// <summary>A hand-edited step out of range shows as the nearest, and is not written back by opening the window.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 5)]
    public void AStepOutOfRangeShowsAsTheNearest(int stored, int shown) => Run(() =>
    {
        using var bench = new GamesBench(Fakes(1));
        bench.Config.GamesSoundVolume = stored;
        var window = bench.Window();

        AssertPips(window, shown);
        Assert.Equal(shown > 1, window.SoundDown.IsEnabled);
        Assert.Equal(shown < 5, window.SoundUp.IsEnabled);
        Assert.Equal(stored, bench.Config.GamesSoundVolume);
    });

    // ---- The stage: a game is heard only on screen and not covered (A79) ------------------------

    private static GameSound Cue(string name) => new(name, new short[] { 1, 2, 3 });

    /// <summary>
    /// A game that plays from its view: its sound reaches the speaker only
    /// while it is on screen and not covered, through the window's every
    /// change: the grid, the game shown, the warning, "Keep playing", back to
    /// the grid, the window closed. Every change that leaves it unheard stops
    /// what plays.
    /// </summary>
    [Fact]
    public void AGameIsHeardOnlyWhileOnScreenAndNotCovered() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        var heard = 0;
        void Plays(bool expected, string cue)
        {
            game.Host!.Sound.Play(Cue(cue));
            if (expected) heard++;
            Assert.True(heard == bench.Speaker.Played.Count, $"{cue}: {(expected ? "not heard" : "heard")}");
        }

        Plays(false, "on the grid");

        GamesBench.ClickCard(GamesBench.Tile(window, 0).Card);
        var view = game.Views[^1];
        view.Play(Cue("shown"));
        heard++;
        Assert.Equal(heard, bench.Speaker.Played.Count);

        var stops = bench.Speaker.Stops;
        window.ShowHealthWarning(60);
        Assert.Equal(stops + 1, bench.Speaker.Stops);
        Plays(false, "under the warning");

        GamesBench.Click(window.WarningButton); // "Keep playing"
        Plays(true, "kept playing");

        stops = bench.Speaker.Stops;
        GamesBench.Click(window.BackButton);
        Assert.Equal(stops + 1, bench.Speaker.Stops);
        Plays(false, "back on the grid");

        window.ShowGame(bench.Host.Games[0]);
        Plays(true, "shown again");
        stops = bench.Speaker.Stops;
        window.Close();
        Assert.Equal(stops + 1, bench.Speaker.Stops);
        Plays(false, "after the window closed");
    });

    /// <summary>A window opened on a game under a warning already up: the game is not heard until "Keep playing".</summary>
    [Fact]
    public void AGameShownUnderTheWarningIsNotHeard() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        window.ShowHealthWarning(30);
        window.ShowGame(bench.Host.Games[0]);

        game.Views[^1].Play(Cue("paused"));
        Assert.Empty(bench.Speaker.Played);

        GamesBench.Click(window.WarningButton);
        game.Views[^1].Play(Cue("resumed"));
        Assert.Single(bench.Speaker.Played);
    });

    /// <summary>The game on screen faulting (its own handler reported it): the window's close goes through the grid, which stops it and hears it no more.</summary>
    [Fact]
    public void AGameThatFaultsOnScreenIsHeardNoMore() => Run(() =>
    {
        var game = new FakeGame();
        using var bench = new GamesBench(game);
        var window = bench.Window();
        window.ShowGame(bench.Host.Games[0]);
        var stops = bench.Speaker.Stops;

        game.Host!.Fault(new InvalidOperationException("made up"));

        Assert.Null(window.OnScreen);
        Assert.Equal(stops + 1, bench.Speaker.Stops);
        game.Views[^1].Play(Cue("after its fault"));
        Assert.Empty(bench.Speaker.Played);
    });

    /// <summary>A game that throws in its view's Root never reaches the screen, and is never heard: the window stays on the grid, which it tells the gate.</summary>
    [Fact]
    public void AGameThatThrowsInRootIsNeverHeard() => Run(() =>
    {
        var game = new ThrowsInRoot();
        using var bench = new GamesBench(new[] { new GameEntry(game.Id, () => game) });
        var window = bench.Window();
        var stops = bench.Speaker.Stops;

        GamesBench.ClickCard(GamesBench.Tile(window, 0).Card);

        Assert.Null(window.OnScreen);
        Assert.Equal(GameCondition.Faulted, bench.Host.Games[0].Condition);
        Assert.Equal(stops + 1, bench.Speaker.Stops); // the grid again, told
        game.Host!.Sound.Play(Cue("from a game that threw"));
        Assert.Empty(bench.Speaker.Played);
    });

    /// <summary>The preview plays on the grid too: the volume is set before playing, and the click is on the open window.</summary>
    [Fact]
    public void ThePreviewPlaysOnTheGrid() => Run(() =>
    {
        using var bench = new GamesBench(Fakes(2));
        var window = bench.Window();

        GamesBench.Click(window.SoundUp);

        Assert.Null(window.OnScreen);
        Assert.Single(bench.Speaker.Played);
    });

    /// <summary>A FakeGame in all but its view, whose Root throws: a broken view (A10).</summary>
    private sealed class ThrowsInRoot : IGame
    {
        private readonly FakeGame _inner = new("throws-in-root");

        public string Id => _inner.Id;
        public string Title => _inner.Title;
        public string Pitch => _inner.Pitch;
        public IGameHost? Host => _inner.Host;
        public AttachResult Attach(IGameHost host) => _inner.Attach(host);
        public void OnReading(DinoReading reading) => _inner.OnReading(reading);
        public GameTile Tile() => _inner.Tile();
        public FrameworkElement CreateTileArt() => _inner.CreateTileArt();
        public IGameView CreateView() => new BrokenView();
        public void Reset() => _inner.Reset();

        private sealed class BrokenView : IGameView
        {
            public FrameworkElement Root => throw new InvalidOperationException("made up: a view with no root");
            public IReadOnlyList<string> LayoutStates { get; } = new[] { "only" };
            public void Pause() { }
            public void Resume() { }
            public void ShowLayoutState(string state) { }
            public void Dispose() { }
        }
    }

    // ---- Nothing runs once a view is gone (A19) ------------------------------------------------

    /// <summary>
    /// Every listed game: a window on it, each layout state shown, the window
    /// closed; then no engine loop holds a timer. Empty until a game is
    /// listed: it then runs by itself.
    /// </summary>
    [Fact]
    public void NothingRunsOnceAViewIsGone() => Run(() =>
    {
        foreach (var entry in GameCatalog.All)
        {
            using var bench = new GamesBench(new[] { entry });
            var window = bench.Window();
            window.ShowGame(bench.Host.Games[0]);
            var view = window.ViewOnScreen ?? throw new InvalidOperationException($"{entry.Id} shows no view");
            foreach (var state in view.LayoutStates) view.ShowLayoutState(state);
            window.Close();
            Assert.Equal(0, PandoraOverlay.Games.Engine.GameLoop.LiveTimers);
        }
    });

    /// <summary>
    /// Every listed game in two windows in a row, grid and game and grid
    /// again: it stays Ready after every step, and the last grid shows its tile
    /// with its art (a game may hand back a new picture or one it keeps; a view
    /// is always new). Empty until a game is listed: it then runs by itself.
    /// </summary>
    [Fact]
    public void EveryListedGameSurvivesTwoWindowsInARow() => Run(() =>
    {
        foreach (var entry in GameCatalog.All)
        {
            using var bench = new GamesBench(new[] { entry });
            var game = bench.Host.Games[0];
            void Ready(string step) => Assert.True(game.Condition == GameCondition.Ready, $"{entry.Id} is {game.Condition} after {step}");

            var first = bench.Window();
            first.ShowGame(game);
            Ready("the first ShowGame");
            Assert.Same(game, first.OnScreen);
            first.ShowGrid();
            Ready("ShowGrid");
            first.ShowGame(game);
            Ready("the second ShowGame");
            first.Close();
            Ready("Close");

            var second = bench.Window();
            Assert.Same(game, second.OnScreen); // reopened where it was, by GamesView
            second.ShowGrid();
            Ready("the second window's ShowGrid");
            Assert.NotNull(GamesBench.Tile(second, 0).Well.Child as FrameworkElement);
            Assert.IsNotType<TextBlock>(GamesBench.Tile(second, 0).Well.Child); // its art, not its title
        }
    });
}
