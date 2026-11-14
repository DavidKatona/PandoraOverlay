using System.Windows;
using System.Windows.Controls;
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
