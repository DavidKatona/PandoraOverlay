using System.Windows;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

// The engine's playback tests have a Scene of their own in this namespace: the board's is BoardScene here.
using BoardScene = PandoraOverlay.Games.Hatchling.Scene;

/// <summary>
/// What every view test of Hatchling starts with: a game attached to a save in memory
/// (the tests' host, A41's rethrowing Fault), its view made by <c>CreateView</c>, never
/// shown; the loop driven by the manual clock one engine tick at a time; a state with a
/// run under way. The app's theme is not here: keys resolve to nothing, which is fine
/// for behaviour (task H16 lays the view out in every theme).
/// </summary>
internal static class ViewHarness
{
    /// <summary>One engine tick on the manual clock, a hair over a 30th of a second, so every look of a Full loop moves exactly one tick.</summary>
    public static readonly TimeSpan OneTick = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / EngineTime.TicksPerSecond + 1);

    /// <summary>A game attached to <paramref name="saved"/>, Ready.</summary>
    public static (HatchlingGame Game, FakeHost Host) Attached(GameState saved, bool expectFaults = false)
    {
        var host = new FakeHost { ExpectFaults = expectFaults };
        host.Memory.Stored = new SaveRead(SaveReadState.Ok, HatchlingSave.Version, SaveFiles.PayloadOf(saved));
        var game = new HatchlingGame();
        Assert.Equal(AttachResult.Ready, game.Attach(host));
        return (game, host);
    }

    /// <summary>The game's own view.</summary>
    public static HatchlingView ViewOf(HatchlingGame game) => (HatchlingView)game.CreateView();

    /// <summary>A save with <paramref name="species"/> unlocked and chosen at <paramref name="danger"/>, the lineage given, and <paramref name="seed"/> as the next run's.</summary>
    public static GameState Ready(string species = "Dryosaurus", int danger = 1, bool whole = false, uint seed = 7, int runs = 0)
    {
        var state = GameState.Fresh(SaveSamples.Moment);
        if (whole)
            foreach (var trait in Lineage.All) state.Ranks[trait.Trait] = trait.MaxRank;
        state.MaxDanger = Math.Max(state.MaxDanger, danger);
        if (!Unlocks.IsUnlocked(state, species)) Unlocks.Grant(state, species);
        state = Camp.Select(state, species);
        state = Camp.SetDanger(state, danger) ?? throw new InvalidOperationException("the danger was not unlocked");
        state.NextSeed = seed;
        state.Totals.Runs = runs;
        return state;
    }

    /// <summary>A save with a run started on it (H19), its offers at the start picked so the run awaits a move.</summary>
    public static GameState Running(string species = "Dryosaurus", int danger = 1, bool whole = false, uint seed = 7, int runs = 0)
    {
        var state = HatchlingGame.Started(Ready(species, danger, whole, seed, runs)) ?? throw new InvalidOperationException("no run");
        while (state.Run!.Offer is not null)
            state = HatchlingGame.Advance(state, new PlayerAction.Pick(0))!.Value.State;
        return state;
    }

    /// <summary>One engine tick: the clock moved, the loop's timer handler called.</summary>
    public static void Tick(HatchlingView view, FakeHost host)
    {
        host.ManualClock.Advance(OneTick);
        view.TheLoop.Tick();
    }

    /// <summary>Ticks until the playback has drained; the ticks it took. Fails after <paramref name="most"/>.</summary>
    public static int PlayOut(HatchlingView view, FakeHost host, int most = 5000)
    {
        var ticks = 0;
        while (view.RunPage.Playing)
        {
            Assert.True(ticks < most, "the playback did not drain");
            Tick(view, host);
            ticks++;
        }
        return ticks;
    }

    /// <summary>A press of the left button on <paramref name="cell"/>'s centre, as the surface reports it.</summary>
    public static PixelPointer On(int cell, PointerButton button = PointerButton.Left)
    {
        var (x, y) = BoardLayout.Centre(cell);
        return new PixelPointer(x, y, button);
    }

    /// <summary>A scene as text, sprite by sprite in creature order (the drawing order's ties left out), the ambient frame left out: equal texts are equal pictures.</summary>
    public static string Key(BoardScene scene, bool flips = true, bool youMark = true) => string.Join("\n",
        string.Join(";", scene.Sprites.OrderBy(s => s.Kind).ThenBy(s => s.Creature).Select(s => SpriteKey(s, flips, youMark))),
        string.Join(";", scene.Numbers.Select(n => $"{n.Value},{n.Good},{n.Sign},{n.X},{n.Y},{n.Opacity}")),
        string.Join(",", scene.Seen.Select(b => b ? 1 : 0)), string.Join(",", scene.InSight.Select(b => b ? 1 : 0)),
        string.Join(",", scene.Plants.Select(b => b ? 1 : 0)), string.Join(",", scene.Meat),
        string.Join(",", scene.MoveCells), string.Join(",", scene.AttackCells), scene.HoverCell, youMark && scene.ShowYourMark);

    public static string SpriteKey(SceneSprite s, bool flips = true, bool youMark = true) =>
        $"{s.Creature},{s.Kind},{s.Subject},{s.Action},{s.Tick},{s.X},{s.Y},{(flips && s.Flip)},{s.Opacity},{s.Ring},{(youMark ? s.Marks : s.Marks & ~Marks.You)},{s.Ordinal},{s.Row}";

    /// <summary>The run's turn list as its rows' words: name and what it did.</summary>
    public static IReadOnlyList<(string Name, string Text)> Words(IReadOnlyList<TurnRow> rows) =>
        rows.Select(r => (HatchlingText.RowName(r), HatchlingText.RowText(r))).ToList();
}

/// <summary>
/// Hatchling's view as a whole (G13, A26; G14; A10, A11, A19): its frame,
/// its tabs and wallet, its lifecycle, its layout states, and a handler that throws.
/// </summary>
[Collection(LoopTimers.Name)]
public class HatchlingViewTests
{
    [Fact]
    public void TheRootMeasuresTheGamesArea() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var root = view.Root;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        Assert.Equal(GameFrame.ViewWidth, root.ActualWidth);
        Assert.Equal(GameFrame.ViewHeight, root.ActualHeight);
        Assert.Equal(new Size(GameFrame.ViewWidth, GameFrame.ViewHeight), root.DesiredSize);
    });

    [Fact]
    public void TheFirstTabIsTheRunWithARunAndCampWithout() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using (var view = ViewHarness.ViewOf(game))
        {
            Assert.Equal(PageKind.Run, view.Shown);
            Assert.Equal((HatchlingText.TabRun, PageKind.Run), view.TabsShown);
        }

        var (camp, _) = ViewHarness.Attached(ViewHarness.Ready());
        using var atCamp = ViewHarness.ViewOf(camp);
        Assert.Equal(PageKind.Camp, atCamp.Shown);
        Assert.Equal((HatchlingText.TabCamp, PageKind.Camp), atCamp.TabsShown);
        // Run asked for with no run is Camp (H20), and the other tabs show their pages.
        atCamp.TabClicked(PageKind.Run);
        Assert.Equal(PageKind.Camp, atCamp.Shown);
        atCamp.TabClicked(PageKind.Records);
        Assert.Equal(PageKind.Records, atCamp.Shown);
        Assert.Equal((HatchlingText.TabCamp, PageKind.Records), atCamp.TabsShown);
        // A click on the checked tab keeps it checked.
        atCamp.TabClicked(PageKind.Records);
        Assert.Equal(PageKind.Records, atCamp.TabsShown.Checked);
    });

    [Fact]
    public void TheWalletFollowsTheState() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        saved.Wallet = new Wallet(1240, 1240);
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        Assert.Equal(HatchlingText.Money(1240), view.WalletShown);

        // Leaving the run banks what it reached: the wallet shows it at once.
        view.RunPage.LeaveClicked();
        view.RunPage.LeaveClicked();
        Assert.NotNull(game.State.Run!.Over);
        Assert.Equal(HatchlingText.Money(game.State.Wallet.Balance), view.WalletShown);

        // A change from outside the view (a Reset, as the window's) refreshes it too.
        game.Reset();
        Assert.Equal(HatchlingText.Money(0), view.WalletShown);
        Assert.Equal(PageKind.Camp, view.Shown);
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void DisposeTwiceStopsEverythingAndHearsTheGameNoMore() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        var view = ViewHarness.ViewOf(game);
        Assert.Equal(LoopRate.Ambient, view.TheLoop.Rate);
        Assert.Equal(1, GameLoop.LiveTimers);
        view.Dispose();
        view.Dispose();
        Assert.Equal(0, GameLoop.LiveTimers);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        var wallet = view.WalletShown;
        var shown = view.Shown;
        game.Reset();   // raises Changed: a disposed view does not hear it
        Assert.Equal(wallet, view.WalletShown);
        Assert.Equal(shown, view.Shown);
    });

    [Fact]
    public void CreateViewNeedsAnAttachedGame()
    {
        Assert.Throws<InvalidOperationException>(() => new HatchlingGame().CreateView());
    }

    [Fact]
    public void TheLayoutStatesAreSeventeenAndTheRunPagesShowStill() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        Assert.Equal(17, view.LayoutStates.Count);
        Assert.Equal(17, view.LayoutStates.Distinct().Count());
        var writes = host.Memory.Writes.Count;
        var runStates = view.LayoutStates.Where(s => s.StartsWith("run-", StringComparison.Ordinal)).ToList();
        Assert.Equal(7, runStates.Count);

        // Any order, any number of times, on a view never shown.
        foreach (var state in runStates.Concat(Enumerable.Reverse(runStates)).Concat(runStates))
        {
            view.ShowLayoutState(state);
            Assert.Equal(PageKind.Run, view.Shown);
            Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
            Assert.True(view.ThePlayback.Idle);
            var root = view.Root;
            root.Measure(new Size(GameFrame.ViewWidth, GameFrame.ViewHeight));
            root.Arrange(new Rect(0, 0, GameFrame.ViewWidth, GameFrame.ViewHeight));
            Assert.Equal(GameFrame.ViewWidth, root.ActualWidth);
            Assert.Equal(GameFrame.ViewHeight, root.ActualHeight);
        }
        Assert.Equal(0, GameLoop.LiveTimers);
        Assert.Equal(writes, host.Memory.Writes.Count);   // no storage written

        foreach (var other in view.LayoutStates.Except(runStates))
            Assert.Throws<NotSupportedException>(() => view.ShowLayoutState(other));
        Assert.Throws<ArgumentException>(() => view.ShowLayoutState("run-nowhere"));
    });

    [Fact]
    public void TheLayoutStatesShowWhatThePlanNames() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;

        view.ShowLayoutState("run-first");
        Assert.Equal(HatchlingText.YourTurn, page.BannerShown);
        Assert.Equal(HatchlingText.HintFirst, page.ContextShown);
        Assert.Equal((HatchlingText.RowMarker, HatchlingText.RowYou, HatchlingText.DidHatch), page.RowsShown[0]);

        view.ShowLayoutState("run-playback");
        Assert.Equal("Carnotaurus 2 bites you: 12", page.BannerShown);
        var rows = page.RowsShown;
        Assert.True(rows.Count == 5, string.Join(" | ", rows));
        Assert.Equal("rested · +8%", rows[0].Text);
        Assert.Equal("waited", rows.Single(r => r.Name == "Troodon").Text);
        Assert.Equal((HatchlingText.RowMarker, "Carnotaurus 2", ""), rows.Single(r => r.Name == "Carnotaurus 2"));
        Assert.Equal("", rows.Single(r => r.Name == "Herrerasaurus").Text);
        Assert.Equal(HatchlingText.Skip, page.ContextShown);

        view.ShowLayoutState("run-mid");
        Assert.Equal("spotted you · bit you: 12", page.RowsShown.Single(r => r.Name == "Carnotaurus 2").Text);
        Assert.Equal(HatchlingText.StatusForest, RunPage.Status(view.State.Run!).Text);
        Assert.Equal("Instincts: Keen eyes, Ambusher", HatchlingText.InstinctList(view.State.Run!.Instincts));

        view.ShowLayoutState("run-apex");
        Assert.Equal(TurnScript.MaxRows, page.RowsShown.Count);
        Assert.Equal(HatchlingText.StatusApex(21), RunPage.Status(view.State.Run!).Text);

        view.ShowLayoutState("run-offer");
        var offer = page.OfferShown;
        Assert.True(offer.Open);
        Assert.Equal(4, offer.Cards.Count);
        Assert.Equal(HatchlingText.Redraw(100), offer.Redraw);
        Assert.Equal(HatchlingText.ChooseInstinct, page.BannerShown);

        view.ShowLayoutState("run-lost");
        Assert.True(page.SummaryShown.Open);
        Assert.Equal(HatchlingText.OverLost, page.SummaryShown.Title);

        view.ShowLayoutState("run-won");
        var won = page.SummaryShown;
        Assert.Equal(HatchlingText.OverWonDefeat, won.Title);
        Assert.Equal(9 + 3 + 1, won.Lines.Count);
        Assert.Equal(HatchlingText.PayMore(2), won.Lines[^1]);
        Assert.Equal(HatchlingText.Money(Samples.Balance), view.WalletShown);
    });

    [Fact]
    public void ReopeningShowsTheSameRowsAndTheFinalScene() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using (var view = ViewHarness.ViewOf(game))
        {
            view.RunPage.RestClicked();
            Assert.True(view.RunPage.Playing);
            // Closed in the middle of the playback: nothing was pending (A20).
            ViewHarness.Tick(view, host);
        }
        Assert.Equal(0, GameLoop.LiveTimers);
        using var again = ViewHarness.ViewOf(game);
        var run = game.State.Run!;
        Assert.Equal(1, run.Turn);
        Assert.Equal(ViewHarness.Words(run.LastTurn), again.RunPage.RowsShown.Select(r => (r.Name, r.Text)).ToList());
        Assert.Equal(ViewHarness.Key(BoardScene.Of(run, Stats.Of(run), yourTurn: true, -1)), ViewHarness.Key(again.RunPage.CurrentScene));
        Assert.Equal(HatchlingText.YourTurn, again.RunPage.BannerShown);
    });

    [Fact]
    public void AHandlerThatThrowsReachesTheHostsFaultAndNotTheTest() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running(), expectFaults: true);
        using var view = ViewHarness.ViewOf(game);
        var boom = new InvalidOperationException("a test's fault");
        view.RunPage.BeforeAct = () => throw boom;
        var writes = host.Memory.Writes.Count;
        view.Guarded(view.RunPage.RestClicked);
        Assert.Same(boom, Assert.Single(host.Faults));
        Assert.Equal(writes, host.Memory.Writes.Count);
    });

    [Fact]
    public void PauseStopsThePlaybackAndTheInputAndResumeGoesOn() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        view.Guarded(view.RunPage.RestClicked);
        ViewHarness.Tick(view, host);
        var at = ViewHarness.Key(view.RunPage.CurrentScene);
        var banner = view.RunPage.BannerShown;
        view.Pause();
        for (var i = 0; i < 20; i++) ViewHarness.Tick(view, host);
        Assert.True(view.RunPage.Playing);
        Assert.Equal(at, ViewHarness.Key(view.RunPage.CurrentScene));
        Assert.Equal(banner, view.RunPage.BannerShown);
        // A press while paused is not taken: not even as a skip.
        view.Guarded(() => view.RunPage.BoardPressed(new PixelPointer(1, 1, PointerButton.Left)));
        Assert.True(view.RunPage.Playing);
        view.Resume();
        ViewHarness.PlayOut(view, host);
        Assert.False(view.RunPage.Playing);
        Assert.Equal(LoopRate.Ambient, view.TheLoop.Rate);
    });

    [Fact]
    public void APressOnATabDuringAPlaybackOnlySkips() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        view.Guarded(view.RunPage.RestClicked);
        Assert.True(view.RunPage.Playing);
        view.TabClicked(PageKind.Lineage);
        Assert.False(view.RunPage.Playing);
        Assert.Equal(PageKind.Run, view.Shown);
        Assert.Equal(PageKind.Run, view.TabsShown.Checked);
        // At rest the tab is taken, and the loop is off on another page.
        view.TabClicked(PageKind.Lineage);
        Assert.Equal(PageKind.Lineage, view.Shown);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        view.TabClicked(PageKind.Run);
        Assert.Equal(LoopRate.Ambient, view.TheLoop.Rate);
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void QuartersAreTheAppsLines()
    {
        Assert.Equal(Quarter.Above75, Quarters.Of(1));
        Assert.Equal(Quarter.Above75, Quarters.Of(0.75));
        Assert.Equal(Quarter.From50To75, Quarters.Of(0.7499));
        Assert.Equal(Quarter.From50To75, Quarters.Of(0.5));
        Assert.Equal(Quarter.From25To50, Quarters.Of(0.4999));
        Assert.Equal(Quarter.From25To50, Quarters.Of(0.25));
        Assert.Equal(Quarter.Below25, Quarters.Of(0.2499));
        Assert.Equal(Quarter.Below25, Quarters.Of(0));
        Assert.Equal(Quarter.Above75, Quarters.Of(double.NaN));
        Assert.Equal("Theme.Bars.Health.Below25.Fill", Quarters.HealthKey(Quarter.Below25));
        Assert.Equal("Theme.Bars.Hunger.From50To75.Fill", Quarters.FoodKey(Quarter.From50To75));
        Assert.Equal("Theme.Bars.Thirst.Above75.Fill", Quarters.WaterKey(Quarter.Above75));
    }
}
