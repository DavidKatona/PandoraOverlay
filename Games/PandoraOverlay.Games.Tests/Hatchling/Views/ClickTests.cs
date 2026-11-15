using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The left button on an element of a view as WPF raises it, short of the capture: the
/// press (its preview tunnelling down from the view's root, then the press bubbling back
/// up) and the release (the same), each re-raised by WPF as its left-button event on
/// every element on the way. A view never shown can't hold the capture a button needs
/// to raise its own Click on the release (the clicks planner's probe), so a button's
/// click is raised for it inside its own release, its toggle first, as
/// <c>ButtonBase.OnClick</c> does in a real one. A real left click is the owner's.
/// </summary>
internal static class Hand
{
    private static MouseButtonEventArgs Left(RoutedEvent routed) =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routed };

    /// <summary>The left button goes down on <paramref name="on"/>.</summary>
    public static void Down(UIElement on)
    {
        on.RaiseEvent(Left(Mouse.PreviewMouseDownEvent));
        on.RaiseEvent(Left(Mouse.MouseDownEvent));
    }

    /// <summary>The left button comes up on <paramref name="on"/>, <paramref name="inside"/> raised between its preview and its bubble.</summary>
    public static void Up(UIElement on, Action? inside = null)
    {
        on.RaiseEvent(Left(Mouse.PreviewMouseUpEvent));
        inside?.Invoke();
        on.RaiseEvent(Left(Mouse.MouseUpEvent));
    }

    /// <summary>
    /// A left click on a button: down, then up, its toggle and its Click raised from its
    /// own MouseLeftButtonUp, where a button that holds the capture raises them, so the
    /// release then bubbles on to the view's root along the route WPF built for it
    /// before any handler ran. That order matters: a game's handler may take the
    /// button's page out of the view ("Back to camp" shows Camp), and the release still
    /// reaches the root, as a real one does.
    /// </summary>
    public static void Click(ButtonBase control)
    {
        Down(control);
        MouseButtonEventHandler clicks = (_, _) =>
        {
            switch (control)
            {
                case RadioButton radio:
                    radio.IsChecked = true;
                    break;
                case ToggleButton toggle:
                    toggle.IsChecked = toggle.IsChecked != true;
                    break;
            }
            control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        };
        // handledEventsToo: the button's own OnMouseLeftButtonUp has marked the release handled.
        control.AddHandler(UIElement.MouseLeftButtonUpEvent, clicks, handledEventsToo: true);
        try
        {
            Up(control);
        }
        finally
        {
            control.RemoveHandler(UIElement.MouseLeftButtonUpEvent, clicks);
        }
    }

    /// <summary>A left click on a card: down and up on it (its own handlers make it a click).</summary>
    public static void Click(UIElement card)
    {
        Down(card);
        Up(card);
    }
}

/// <summary>
/// Hatchling's clicks (G27, A105, H102, H103), <c>PLAN-CLICKS.md</c> 1.6's table row by
/// row, on the game's real view with the tests' host: the root taken as the host takes
/// it (<see cref="IGameView.Root"/>, where the engine wires its rule), each control
/// clicked by the left button as <see cref="Hand"/> raises it, and what reached the host
/// read off its <see cref="FakeSound"/>: the click, or none, and the cue, as before
/// (<c>PageSoundTests</c> and <c>PlaybackSoundTests</c> hold the cues themselves,
/// unchanged). Seeded so that every row is reached, a refusal of each kind included:
/// Camp's start, the offer's card and Redraw are refused only on a layout state's
/// sample (their controls are disabled whenever the game would refuse them), a trait
/// card for real.
/// </summary>
[Collection(LoopTimers.Name)]
public class ClickTests
{
    /// <summary>The game's own view, its root taken as the host takes it: the click rule wired.</summary>
    private static HatchlingView Wired(HatchlingGame game)
    {
        var view = ViewHarness.ViewOf(game);
        _ = ((IGameView)view).Root;
        return view;
    }

    private static HatchlingViewRoot Bar(HatchlingView view) => (HatchlingViewRoot)view.Root;

    /// <summary>A Camp with something to spend and a few runs behind it.</summary>
    private static GameState Rich(long balance = 5_000, int runs = 3)
    {
        var state = ViewHarness.Ready(runs: runs);
        state.Wallet = new Wallet(balance, balance);
        return state;
    }

    /// <summary>A run just started with the whole lineage: Born ready's offer at the start open.</summary>
    private static GameState Offered(long balance)
    {
        var state = HatchlingGame.Started(ViewHarness.Ready(whole: true))!;
        Assert.NotNull(state.Run!.Offer);
        state.Wallet = new Wallet(balance, balance);
        return state;
    }

    /// <summary>A run with one turn played: leaving it earns "first" (H47 pays nothing for a run left before its first turn).</summary>
    private static GameState Played() =>
        HatchlingGame.Advance(ViewHarness.Running(), new PlayerAction.Rest())!.Value.State;

    // ===== Camp ================================================================================

    [Fact]
    public void StartARunHatchesAndARefusedStartPresses() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = Wired(game);
        Hand.Click(PageHarness.Shown<CampPage>(view).ActionButton);
        Assert.NotNull(game.State.Run);
        Assert.Equal(new[] { "hatch" }, host.Sound.Played);
        Assert.Empty(host.Sound.Clicks);   // the cue is the click's sound

        var (fresh, freshHost) = ViewHarness.Attached(Rich());
        using var freshView = Wired(fresh);
        freshView.ShowLayoutState("camp-first");   // a sample: the start is refused
        var camp = PageHarness.Shown<CampPage>(freshView);
        Assert.Equal((HatchlingText.StartRun, true), (camp.ActionShown.Button, camp.ActionShown.Enabled));
        Hand.Click(camp.ActionButton);
        Assert.Null(fresh.State.Run);
        Assert.Empty(freshHost.Sound.Played);
        Assert.Equal(new[] { GameClick.Press }, freshHost.Sound.Clicks);   // H103: the press registered
    });

    [Fact]
    public void TheUnlocksFirstClickArmsAndItsSecondUnlocksWithNoClick() => Sta.Run(() =>
    {
        var saved = Rich(balance: 1_000);
        saved.Bought = 2;
        saved.Selected = "Maiasaura";
        var (game, host) = ViewHarness.Attached(saved);
        using var view = Wired(game);
        var camp = PageHarness.Shown<CampPage>(view);

        Hand.Click(camp.ActionButton);
        Assert.True(camp.UnlockArmed);
        Assert.Empty(host.Sound.Played);
        Assert.Equal(new[] { GameClick.Arm }, host.Sound.Clicks);

        Hand.Click(camp.ActionButton);
        Assert.True(Unlocks.IsUnlocked(game.State, "Maiasaura"));
        Assert.Equal(new[] { "unlock" }, host.Sound.Played);
        Assert.Equal(new[] { GameClick.Arm }, host.Sound.Clicks);
    });

    [Fact]
    public void ASpeciesCardAndADangerSelect() => Sta.Run(() =>
    {
        var saved = Rich();
        saved.MaxDanger = 3;
        var (game, host) = ViewHarness.Attached(saved);
        using var view = Wired(game);
        var camp = PageHarness.Shown<CampPage>(view);
        var cards = camp.Cards.Children.OfType<Border>().ToList();
        var other = camp.CardsShown.Select((c, i) => (c.Species, Index: i)).First(c => c.Species != game.State.Selected);

        Hand.Click(cards[other.Index]);
        Assert.Equal(other.Species, game.State.Selected);
        Hand.Click(cards[other.Index]);   // the chosen one's card: nothing to change, still a click
        Hand.Click((RadioButton)camp.DangerRadios.Children[1]);
        Assert.Equal(2, game.State.Danger);

        Assert.Empty(host.Sound.Played);
        Assert.Equal(new[] { GameClick.Select, GameClick.Select, GameClick.Select }, host.Sound.Clicks);
    });

    [Fact]
    public void ACardReleasedAwayFromItsPressIsNoClick() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = Wired(game);
        var cards = PageHarness.Shown<CampPage>(view).Cards.Children.OfType<Border>().ToList();
        var selected = game.State.Selected;

        Hand.Down(cards[3]);
        Hand.Up(cards[4]);   // pressed on one card, released on another (H79): no choice, no click

        Assert.Equal(selected, game.State.Selected);
        Assert.Empty(host.Sound.Clicks);
    });

    // ===== The offer ===========================================================================

    [Fact]
    public void AnOffersCardChoosesWithNoClickAndARefusedOneSelects() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Offered(1_000));
        using var view = Wired(game);
        var instincts = game.State.Run!.Instincts.Count;
        Hand.Click(view.RunPage.OfferCards.Children.OfType<Border>().First());
        Assert.Equal(instincts + 1, game.State.Run!.Instincts.Count);   // picked (the next offer may open: Born ready owes more)
        Assert.Equal(new[] { "choose" }, host.Sound.Played);
        Assert.Empty(host.Sound.Clicks);

        var (sample, sampleHost) = ViewHarness.Attached(Rich());
        using var sampleView = Wired(sample);
        sampleView.ShowLayoutState("run-offer");   // a sample's offer: the pick is refused
        Hand.Click(sampleView.RunPage.OfferCards.Children.OfType<Border>().First());
        Assert.Empty(sampleHost.Sound.Played);
        Assert.Equal(new[] { GameClick.Select }, sampleHost.Sound.Clicks);
    });

    [Fact]
    public void RedrawShufflesWithNoClickAndARefusedOnePresses() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Offered(1_000));
        using var view = Wired(game);
        Hand.Click(view.RunPage.RedrawButton);
        Assert.Equal(new[] { "shuffle" }, host.Sound.Played);
        Assert.Empty(host.Sound.Clicks);

        var (sample, sampleHost) = ViewHarness.Attached(Rich());
        using var sampleView = Wired(sample);
        sampleView.ShowLayoutState("run-offer");
        Assert.True(sampleView.RunPage.OfferShown.CanRedraw);
        Hand.Click(sampleView.RunPage.RedrawButton);
        Assert.Empty(sampleHost.Sound.Played);
        Assert.Equal(new[] { GameClick.Press }, sampleHost.Sound.Clicks);
    });

    // ===== Lineage ===============================================================================

    private static Trait Buyable => Lineage.All.First(t => Lineage.Above(t.Trait) is null).Trait;

    private static Border CardOf(LineagePage page, Trait trait) =>
        page.Branches.Children.OfType<Border>().ElementAt(Lineage.All.ToList().FindIndex(t => t.Trait == trait));

    [Fact]
    public void ATraitCardChoosesWithNoClickAndOneThatBuysNothingSelects() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich(balance: 100_000));
        using var view = Wired(game);
        var page = PageHarness.Open<LineagePage>(view, PageKind.Lineage);
        Hand.Click(CardOf(page, Buyable));
        Assert.Equal(1, game.State.Rank(Buyable));
        Assert.Equal(new[] { "choose" }, host.Sound.Played);
        Assert.Empty(host.Sound.Clicks);

        var (poor, poorHost) = ViewHarness.Attached(Rich(balance: 0));
        using var poorView = Wired(poor);
        var poorPage = PageHarness.Open<LineagePage>(poorView, PageKind.Lineage);
        Hand.Click(CardOf(poorPage, Buyable));   // short of the wallet: refused for real
        Assert.Equal(0, poor.State.Rank(Buyable));
        Assert.Empty(poorHost.Sound.Played);
        Assert.Equal(new[] { GameClick.Select }, poorHost.Sound.Clicks);
    });

    // ===== Journal ===============================================================================

    [Fact]
    public void TheSwapsFirstClickArmsAndItsSecondShufflesWithNoClick() => Sta.Run(() =>
    {
        var saved = ViewHarness.Ready(runs: 2);
        saved.Wallet = new Wallet(120, 120);
        saved.Journal.Open = new[] { "Maiasaura", "Triceratops", "Pteranodon" }
            .Select(n => SpeciesTable.TryGet(n, out var info) ? Journal.EntryFor(info) : throw new ArgumentException(n)).ToList();
        var (game, host) = ViewHarness.Attached(saved);
        using var view = Wired(game);
        var page = PageHarness.Open<JournalPage>(view, PageKind.Journal);
        var swap = PageHarness.Walk(page.Entry1).OfType<Button>().Single();

        Hand.Click(swap);
        Assert.Equal(1, page.ArmedEntry);
        Assert.Empty(host.Sound.Played);
        Assert.Equal(new[] { GameClick.Arm }, host.Sound.Clicks);

        Hand.Click(swap);
        Assert.NotEqual("Triceratops", game.State.Journal.Open[1].Species);
        Assert.Equal(new[] { "shuffle" }, host.Sound.Played);
        Assert.Equal(new[] { GameClick.Arm }, host.Sound.Clicks);
    });

    // ===== The bar and the Guide =================================================================

    [Fact]
    public void TheTabsAndTheGuidesSectionsSelect() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(Rich());
        using var view = Wired(game);
        var bar = Bar(view);

        Hand.Click(bar.LineageTab);
        Assert.Equal(PageKind.Lineage, view.Shown);
        Hand.Click(bar.RecordsTab);
        Hand.Click(bar.GuideTab);
        Hand.Click(view.GuidePage.Buttons[2]);
        Assert.Equal(2, view.GuidePage.ListShown.Checked);
        Hand.Click(bar.FirstTab);
        Assert.Equal(PageKind.Camp, view.Shown);

        Assert.Empty(host.Sound.Played);
        Assert.Equal(Enumerable.Repeat(GameClick.Select, 5), host.Sound.Clicks);
    });

    // ===== The run's own controls ===============================================================

    [Fact]
    public void APaceSelectsAndFastTogglesAtRest() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = Wired(game);
        var page = view.RunPage;

        Hand.Click(page.SneakPace);
        Assert.Equal(Pace.Sneak, game.State.Run!.Pace);
        Hand.Click(page.FastBox);
        Assert.True(game.State.Fast);
        Hand.Click(page.FastBox);
        Assert.False(game.State.Fast);

        Assert.Empty(host.Sound.Played);
        Assert.Equal(new[] { GameClick.Select, GameClick.ToggleOn, GameClick.ToggleOff }, host.Sound.Clicks);
    });

    [Fact]
    public void EatDrinkAndRestHaveTheirTurnForTheirSound() => Sta.Run(() =>
    {
        foreach (var move in new[] { "eat", "drink", "rest" })
        {
            var saved = ViewHarness.Running();
            var run = saved.Run!;
            run.Plants[run.You.At] = true;
            run.You.Food = 90;
            run.Tiles[Island.Grid.Neighbours(run.You.At).First()] = Terrain.Water;
            run.You.Water = 10;
            var (game, host) = ViewHarness.Attached(saved);
            using var view = Wired(game);
            var page = view.RunPage;
            var button = move switch { "eat" => page.EatButton, "drink" => page.DrinkButton, _ => page.RestButton };
            Assert.True(button.IsEnabled, move);

            Hand.Click(button);

            Assert.True(page.Playing, move);   // the click began a turn: its beats are its sound
            Assert.Empty(host.Sound.Clicks);
            ViewHarness.PlayOut(view, host);
            Assert.Empty(host.Sound.Clicks);   // and the beats' cues never bring one
        }
    });

    [Fact]
    public void NothingClicksDuringAPlayback() => Sta.Run(() =>
    {
        // Every control of the Run page and the bar, pressed while a turn plays: a skip
        // (H5), whose one sound is H98's or none, never a click; Fast changes the pace
        // of the turn still playing, and is silent too.
        var controls = new Func<HatchlingView, ButtonBase>[]
        {
            v => v.RunPage.RestButton, v => v.RunPage.WalkPace, v => v.RunPage.SprintPace, v => v.RunPage.FastBox,
            v => v.RunPage.LeaveButton, v => Bar(v).LineageTab, v => Bar(v).GuideTab, v => Bar(v).FirstTab,
        };
        foreach (var control in controls)
        {
            var (game, host) = ViewHarness.Attached(ViewHarness.Running());
            using var view = Wired(game);
            Hand.Click(view.RunPage.RestButton);
            Assert.True(view.RunPage.Playing);
            var played = host.Sound.Played.Count;

            Hand.Click(control(view));

            Assert.Empty(host.Sound.Clicks);
            Assert.True(host.Sound.Played.Count - played <= 1, "a skip plays at most its one cue (H98)");
            Assert.False(view.RunPage.LeaveArmed);   // Leave's press during a playback only skipped
        }
    });

    [Fact]
    public void LeaveArmsThenPressesOrRewards() => Sta.Run(() =>
    {
        // Left before its first turn: the run earns nothing, so the second click is a press.
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = Wired(game);
        var page = view.RunPage;
        Hand.Click(page.LeaveButton);
        Assert.True(page.LeaveArmed);
        Hand.Click(page.LeaveButton);
        Assert.NotNull(game.State.Run!.Over);
        Assert.Empty(host.Sound.Played);
        Assert.Equal(new[] { GameClick.Arm, GameClick.Press }, host.Sound.Clicks);

        // With a turn played: its summary earns "first", and Reward is the second click's sound.
        var (played, playedHost) = ViewHarness.Attached(Played());
        using var playedView = Wired(played);
        Hand.Click(playedView.RunPage.LeaveButton);
        Hand.Click(playedView.RunPage.LeaveButton);
        Assert.NotNull(played.State.Run!.Over);
        Assert.Equal(new[] { "reward" }, playedHost.Sound.Played);
        Assert.Equal(new[] { GameClick.Arm }, playedHost.Sound.Clicks);
    });

    [Fact]
    public void BackToCampIsMarkedBack() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = Wired(game);
        var page = view.RunPage;
        Assert.Equal(GameClickMark.Back, GameClicks.GetCue(page.BackButton));
        page.LeaveClicked();
        page.LeaveClicked();   // in code: no click (no left release around it)
        Assert.Empty(host.Sound.Clicks);

        Hand.Click(page.BackButton);

        Assert.Null(game.State.Run);
        Assert.Equal(PageKind.Camp, view.Shown);
        Assert.Empty(host.Sound.Played);
        Assert.Equal(new[] { GameClick.Back }, host.Sound.Clicks);
    });

    [Fact]
    public void TheBoardNeverClicks() => Sta.Run(() =>
    {
        var (game, host) = ViewHarness.Attached(ViewHarness.Running());
        using var view = Wired(game);
        var page = view.RunPage;
        var run = game.State.Run!;

        // A press on the board takes a move (the surface reports its press); its release is no click.
        page.BoardPressed(ViewHarness.On(Moves.Targets(run, Stats.Of(run))[0]));
        Hand.Up(page.Board);
        Assert.True(page.Playing);
        // A press during the playback skips it; its release is no click either.
        page.BoardPressed(ViewHarness.On(game.State.Run!.You.At));
        Hand.Up(page.Board);
        Assert.False(page.Playing);
        // And at rest on a cell that takes nothing.
        page.BoardPressed(ViewHarness.On(game.State.Run!.You.At));
        Hand.Up(page.Board);

        Assert.Empty(host.Sound.Clicks);
    });
}
