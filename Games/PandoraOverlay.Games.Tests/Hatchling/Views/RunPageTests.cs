using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

// The engine's playback tests have a Scene of their own in this namespace: the board's is BoardScene here.
using BoardScene = PandoraOverlay.Games.Hatchling.Scene;

/// <summary>
/// What a click on the Run page does (H27, H5; H5, H20, H49), by calling
/// the handlers the events call: a lit cell moves, a creature beside you is attacked,
/// anything else is nothing; during a playback every press only skips; a pace is kept;
/// Leave is armed; an open offer and an ended run take no move; the moves' lines say
/// what they would add now; the loop's rate in every state (A19).
/// </summary>
[Collection(LoopTimers.Name)]
public class RunPageTests
{
    private static int Writes(FakeHost host) => host.Memory.Writes.Count;

    [Fact]
    public void AClickOnALitCellMovesThere() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var run = game.State.Run!;
        var target = Moves.Targets(run, Stats.Of(run))[0];
        Assert.Contains(target, view.RunPage.CurrentScene.MoveCells);
        var writes = Writes(host);
        view.RunPage.BoardPressed(ViewHarness.On(target));
        Assert.Equal(writes + 1, Writes(host));
        Assert.Equal(1, game.State.Run!.Turn);
        Assert.Equal(target, game.State.Run.You.At);
        Assert.True(view.RunPage.Playing);
        Assert.Equal(LoopRate.Full, view.TheLoop.Rate);
        Assert.False(view.RunPage.CurrentScene.ShowYourMark);   // mark/you hidden from the first beat (H52)
        ViewHarness.PlayOut(view, host);
        Assert.Equal(LoopRate.Ambient, view.TheLoop.Rate);
        Assert.True(view.RunPage.CurrentScene.ShowYourMark);
    });

    [Fact]
    public void AClickOnACellThatIsNotOneDoesNothingAndTheRightButtonNothingAtAll() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var run = game.State.Run!;
        var stats = Stats.Of(run);
        var targets = Moves.Targets(run, stats);
        var nothing = Enumerable.Range(0, run.Tiles.Length)
            .First(c => c != run.You.At && !targets.Contains(c) && Island.CreatureAt(run, c) is null);
        var writes = Writes(host);
        view.RunPage.BoardPressed(ViewHarness.On(nothing));
        view.RunPage.BoardPressed(ViewHarness.On(run.You.At));
        view.RunPage.BoardPressed(ViewHarness.On(targets[0], PointerButton.Right));
        view.RunPage.BoardPressed(new PixelPointer(0, 0, PointerButton.Left));   // the board's corner: no cell
        Assert.Equal(writes, Writes(host));
        Assert.Equal(0, game.State.Run!.Turn);
        Assert.False(view.RunPage.Playing);
    });

    [Fact]
    public void AClickOnACreatureBesideYouAttacksIt() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        var run = saved.Run!;
        var beside = Island.Grid.Neighbours(run.You.At).First(c => run.Tiles[c] != Terrain.Rock && Island.CreatureAt(run, c) is null);
        saved.Run = run.Clear().Put(PredatorType.Troodon, beside, alert: 0);
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        Assert.Contains(beside, view.RunPage.CurrentScene.AttackCells);
        view.RunPage.BoardPressed(ViewHarness.On(beside));
        Assert.Equal(1, game.State.Run!.Turn);
        Assert.Contains(game.State.Run.LastTurn[0].First, new[] { RowCode.Hit, RowCode.Defeated });
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void DuringAPlaybackEveryPressOnlySkips() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var presses = new (string What, Action Press)[]
        {
            ("the board on a lit cell", () => page.BoardPressed(ViewHarness.On(Moves.Targets(game.State.Run!, Stats.Of(game.State.Run!))[0]))),
            ("a pace", () => page.PaceClicked(Pace.Sprint)),
            ("Eat", page.EatClicked),
            ("Drink", page.DrinkClicked),
            ("Rest", page.RestClicked),
            ("Leave", page.LeaveClicked),
            ("a tab", () => view.TabClicked(PageKind.Records)),
        };
        foreach (var (what, press) in presses)
        {
            page.RestClicked();
            Assert.True(page.Playing);
            ViewHarness.Tick(view, host);
            var writes = Writes(host);
            var turn = game.State.Run!.Turn;
            var pace = game.State.Run.Pace;
            press();
            Assert.False(page.Playing);
            Assert.True(writes == Writes(host), $"{what} during a playback wrote something");
            Assert.Equal(turn, game.State.Run!.Turn);
            Assert.Equal(pace, game.State.Run.Pace);
            Assert.False(page.LeaveArmed);
            Assert.Equal(PageKind.Run, view.Shown);
        }
    });

    [Fact]
    public void APaceIsKeptAndTheBoardMarksItsCells() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var writes = Writes(host);
        view.RunPage.PaceClicked(Pace.Sprint);
        Assert.Equal(Pace.Sprint, game.State.Run!.Pace);
        Assert.Equal(writes + 1, Writes(host));
        Assert.False(view.RunPage.Playing);   // a pace takes no turn and plays nothing
        var run = game.State.Run;
        Assert.Equal(Moves.Targets(run, Stats.Of(run)), view.RunPage.CurrentScene.MoveCells);
        // The same pace again is nothing.
        view.RunPage.PaceClicked(Pace.Sprint);
        Assert.Equal(writes + 1, Writes(host));
        using var again = ViewHarness.ViewOf(game);
        Assert.Equal(Pace.Sprint, game.State.Run.Pace);
    });

    [Fact]
    public void LeaveTakesTwoClicksAndDisarmsWhenTheMouseLeavesOrAnotherClickComes() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var writes = Writes(host);
        page.LeaveClicked();
        Assert.True(page.LeaveArmed);
        Assert.Equal(writes, Writes(host));
        page.LeaveLeft();
        Assert.False(page.LeaveArmed);
        page.LeaveClicked();
        page.BoardPressed(ViewHarness.On(game.State.Run!.You.At, PointerButton.Right));
        Assert.False(page.LeaveArmed);
        Assert.Equal(writes, Writes(host));

        page.LeaveClicked();
        page.LeaveClicked();
        var over = game.State.Run!.Over;
        Assert.NotNull(over);
        Assert.Equal(EndReason.Left, over!.Reason);
        Assert.False(page.Playing);   // a run left has no beat: the summary at once (H20)
        Assert.True(page.SummaryShown.Open);
        Assert.Equal(HatchlingText.OverTitle(over), page.BannerShown);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
    });

    [Fact]
    public void WithAnOfferOpenTheBoardTakesNoMoveAndACardPicks() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        saved.Run!.Offer = new List<Instinct> { Instinct.Eyes, Instinct.Feet, Instinct.Quick };
        saved.Run.PicksOwed = 1;
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        Assert.True(page.OfferShown.Open);
        Assert.Equal(3, page.OfferShown.Cards.Count);
        Assert.Equal(HatchlingText.ChooseInstinct, page.BannerShown);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.Empty(page.CurrentScene.MoveCells);
        var run = game.State.Run!;
        var writes = Writes(host);
        var anywhere = Island.Grid.Neighbours(run.You.At).First(c => run.Tiles[c] != Terrain.Rock && Island.CreatureAt(run, c) is null);
        page.BoardPressed(ViewHarness.On(anywhere));
        page.RestClicked();
        page.LeaveClicked();
        Assert.Equal(writes, Writes(host));
        Assert.False(page.LeaveArmed);

        page.PickClicked(1);
        Assert.Contains(Instinct.Feet, game.State.Run!.Instincts);
        Assert.False(page.OfferShown.Open);
        Assert.Equal(LoopRate.Ambient, view.TheLoop.Rate);
        Assert.Equal(HatchlingText.YourTurn, page.BannerShown);
    });

    [Fact]
    public void ARedrawIsPricedAndDisabledWhenTheWalletIsShort() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        saved.Run!.Offer = new List<Instinct> { Instinct.Eyes, Instinct.Feet, Instinct.Quick };
        saved.Run.PicksOwed = 1;
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var offer = view.RunPage.OfferShown;
        Assert.Equal(HatchlingText.Redraw(Offers.RedrawPrice(game.State.Run!)), offer.Redraw);
        Assert.False(offer.CanRedraw);

        var rich = ViewHarness.Running();
        rich.Run!.Offer = new List<Instinct> { Instinct.Eyes, Instinct.Feet, Instinct.Quick };
        rich.Run.PicksOwed = 1;
        rich.Wallet = new Wallet(500, 500);
        var (paying, _) = ViewHarness.Attached(rich);
        using var view2 = ViewHarness.ViewOf(paying);
        Assert.True(view2.RunPage.OfferShown.CanRedraw);
        view2.RunPage.RedrawClicked();
        Assert.Equal(500 - Tuning.RedrawPrice, paying.State.Wallet.Balance);
        Assert.Equal(HatchlingText.Money(500 - Tuning.RedrawPrice), view2.WalletShown);
        Assert.True(view2.RunPage.OfferShown.Open);
    });

    [Fact]
    public void WithTheRunOverNothingActsAndBackToCampShowsCamp() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        page.LeaveClicked();
        page.LeaveClicked();
        var writes = Writes(host);
        var run = game.State.Run!;
        page.BoardPressed(ViewHarness.On(Island.Grid.Neighbours(run.You.At).First()));
        page.RestClicked();
        page.EatClicked();
        page.PaceClicked(Pace.Sneak);
        page.LeaveClicked();
        page.PickClicked(0);
        page.RedrawClicked();
        Assert.Equal(writes, Writes(host));
        Assert.Empty(page.CurrentScene.MoveCells);
        Assert.False(page.CurrentScene.ShowYourMark);
        Assert.False(page.MovesShown.CanEat);   // the rules take no move: the panel says so
        Assert.False(page.MovesShown.CanDrink);

        page.BackClicked();
        Assert.Null(game.State.Run);
        Assert.Equal(PageKind.Camp, view.Shown);
        Assert.Equal((HatchlingText.TabCamp, PageKind.Camp), view.TabsShown);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
    });

    [Fact]
    public void TheMovesSayWhatTheyWouldAddNow() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        var run = saved.Run!;
        run.Plants[run.You.At] = true;
        run.You.Food = 90;
        run.You.Health = 1;
        var (game, _) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var shown = view.RunPage.MovesShown;
        var now = game.State.Run!;
        var stats = Stats.Of(now);
        Assert.Equal("+10 food", shown.Eat);   // food 90 and a plant: what goes into the bar (H49)
        Assert.Equal(HatchlingText.EatGain(Moves.EatGain(now, stats)), shown.Eat);
        Assert.True(shown.CanEat);
        Assert.Equal("+0% health", shown.RestLine);   // full health
        Assert.Equal(Moves.WaterNear(now) ? HatchlingText.DrinkGain(Moves.DrinkGain(now)) : HatchlingText.DrinkNothing, shown.Drink);

        var bare = ViewHarness.Running();
        bare.Run!.Plants[bare.Run.You.At] = false;
        bare.Run.Meat[bare.Run.You.At] = 0;
        var (bareGame, _) = ViewHarness.Attached(bare);
        using var bareView = ViewHarness.ViewOf(bareGame);
        if (bareGame.State.Run!.Tiles[bareGame.State.Run.You.At] != Terrain.Water)
        {
            Assert.Equal(HatchlingText.EatNothing, bareView.RunPage.MovesShown.Eat);
            Assert.False(bareView.RunPage.MovesShown.CanEat);
        }
    });

    [Fact]
    public void TheContextLineSaysWhatIsUnderTheMouseAndElseAHint() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running(runs: 0));
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var run = game.State.Run!;
        var stats = Stats.Of(run);
        Assert.Equal(Hints.Text(Hints.For(run, stats.Hides, stats.Eats.HasFlag(Diet.Plants), (int)Tuning.Round(run.You.Health * 100), RunPage.SightKnows)), page.ContextShown);
        var cell = Moves.Targets(run, stats)[0];
        page.BoardMoved(ViewHarness.On(cell, PointerButton.None));
        Assert.Equal(Describe.Cell(run, cell, RunPage.Facts(run, stats, cell)), page.ContextShown);
        Assert.StartsWith("Walk here", page.ContextShown);
        Assert.Equal(cell, page.CurrentScene.HoverCell);
        page.BoardLeft();
        Assert.Equal(-1, page.CurrentScene.HoverCell);

        // Past the first three runs no hint shows: nothing under the mouse, nothing said.
        var (later, _) = ViewHarness.Attached(ViewHarness.Running(runs: Tuning.HintRuns));
        using var laterView = ViewHarness.ViewOf(later);
        Assert.Equal("", laterView.RunPage.ContextShown);
    });

    [Fact]
    public void FastIsKeptAndPlaysAtOnceAtTwiceTheSpeed() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = ViewHarness.ViewOf(game);
        view.RunPage.RestClicked();
        view.RunPage.FastClicked(true);   // during the playback too, at once
        Assert.True(view.RunPage.Playing);
        Assert.True(game.State.Fast);
        Assert.Equal(2, view.ThePlayback.Speed);
        ViewHarness.PlayOut(view, host);
        view.RunPage.FastClicked(false);
        Assert.False(game.State.Fast);
        Assert.Equal(1, view.ThePlayback.Speed);
    });

    [Fact]
    public void ThePanelFollowsTheBeatsAndRestsOnTheState() => Sta.Run(() =>
    {
        var saved = ViewHarness.Running();
        saved.Run!.You.Health = 0.5;
        var (game, host) = ViewHarness.Attached(saved);
        using var view = ViewHarness.ViewOf(game);
        var page = view.RunPage;
        var before = page.PanelShown;
        page.RestClicked();
        ViewHarness.Tick(view, host);
        Assert.Equal(before.Health, page.PanelShown.Health);   // the rest's beat has only begun
        ViewHarness.PlayOut(view, host);
        var run = game.State.Run!;
        var max = Stats.Of(run).MaxHealth;
        Assert.Equal(HatchlingText.HealthValue((int)Tuning.Round(run.You.Health * max), max), page.PanelShown.Health);
        Assert.Equal(HatchlingText.Count((long)Tuning.Round(run.You.Food)), page.PanelShown.Food);
        Assert.Equal(HatchlingText.Count((long)Tuning.Round(run.You.Water)), page.PanelShown.Water);
        Assert.Equal(HatchlingText.StageLine(Stages.Of(run.You.Growth), (int)Math.Floor(run.You.Growth + RunPay.GrowthSlack)), page.PanelShown.Stage);
        Assert.Equal(HatchlingText.TurnNumber(1), HatchlingText.TurnNumber(run.Turn));
    });
}
