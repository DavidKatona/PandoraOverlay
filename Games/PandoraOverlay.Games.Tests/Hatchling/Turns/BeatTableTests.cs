using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// One script for each row of the beat table (G9): the banner, the ticks and the
/// actor, word for word and number for number. Every turn here is written by hand.
/// </summary>
public class BeatTableTests
{
    // You on row 5 at column 2; a Troodon beside you, "Carnotaurus 2" two cells
    // away, small game three away: all known. An Allosaurus eight away: not known.
    private const int TroodonId = 1, CarnoId = 2, AlloId = 4, GameId = 5;
    private static readonly int Here = Row5(2);

    private static RunState Island() => Run(Here,
        Predator(TroodonId, PredatorType.Troodon, Row5(3)),
        Predator(CarnoId, PredatorType.Carnotaurus, Row5(4), ordinal: 2),
        Predator(AlloId, PredatorType.Allosaurus, Row5(10)),
        Game(GameId, Row5(5)));

    /// <summary>A turn in which you rest, then <paramref name="middle"/>, then time passes; nothing moves.</summary>
    private static TurnScript Rested(params TurnEvent[] middle)
    {
        var island = Island();
        return Build(island, island, new TurnEvent[] { Rest() }.Concat(middle).Append(Time()).ToArray());
    }

    private static void Is(Beat beat, BeatActor actor, int ticks, string banner)
    {
        Assert.Equal(actor, beat.Actor);
        Assert.Equal(ticks, beat.Ticks);
        Assert.Equal(banner, beat.Banner);
    }

    // ---- your beats -------------------------------------------------------------------

    [Fact]
    public void YouWalkTenTicksACell()
    {
        var island = Island();
        var beat = One(Build(island, YouAt(island, Row5(1)), Walk(Here, Row5(1)), Time()), BeatKind.YouWalk);
        Is(beat, BeatActor.You, 10, "You walk");
    }

    [Fact]
    public void YouSneakTwelveTicksACell()
    {
        var island = Island();
        var beat = One(Build(island, YouAt(island, Row5(1)), Sneak(Here, Row5(1)), Time()), BeatKind.YouWalk);
        Is(beat, BeatActor.You, 12, "You sneak");
    }

    [Fact]
    public void YouSprintSixTicksACell()
    {
        var island = Island();
        var two = One(Build(island, YouAt(island, Row5(0)), Sprint(Here, Row5(1), Row5(0)), Time()), BeatKind.YouWalk);
        Is(two, BeatActor.You, 12, "You sprint");

        // A sprint of five cells is 30 (plan 3.1).
        var five = Grid.Index(2, 0);
        var path = new[] { Grid.Index(2, 4), Grid.Index(2, 3), Grid.Index(2, 2), Grid.Index(2, 1), five };
        Is(One(Build(island, YouAt(island, five), Sprint(Here, path), Time()), BeatKind.YouWalk), BeatActor.You, 30, "You sprint");
    }

    [Fact]
    public void YouEatDrinkAndRestSixteenTicksEach()
    {
        var island = Island();
        Is(One(Build(island, island, new Ate(Food.Plants, 30, 1), Time()), BeatKind.YouEat), BeatActor.You, 16, "You eat");
        Is(One(Build(island, island, new Drank(40, 1), Time()), BeatKind.YouDrink), BeatActor.You, 16, "You drink");
        Is(One(Build(island, island, Rest(), Time()), BeatKind.YouRest), BeatActor.You, 16, "You rest");
    }

    [Fact]
    public void YourStrikeIsEighteenTicksAndAHitThatDefeatsFadesTwelveMore()
    {
        var island = Island();
        var hit = One(Build(island, island, new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4), Time()), BeatKind.YouStrike);
        Is(hit, BeatActor.You, 18, "You hit the Troodon: 7");
        Assert.Equal(TroodonId, hit.Creature);

        var defeated = One(Build(island, Without(island, TroodonId), new Struck(TroodonId, Row5(3), 20, false, false, false, true, 4), Time()), BeatKind.YouStrike);
        Is(defeated, BeatActor.You, 30, "You defeated the Troodon");
    }

    [Fact]
    public void YourStrikeOnSmallGameSaysSmallGame()
    {
        var island = Run(Here, Game(GameId, Row5(3)));
        Is(One(Build(island, island, new Struck(GameId, Row5(3), 3, false, false, false, false, 4), Time()), BeatKind.YouStrike),
            BeatActor.You, 18, "You hit small game: 3");
        Is(One(Build(island, Without(island, GameId), new Struck(GameId, Row5(3), 9, false, false, false, true, 4), Time()), BeatKind.YouStrike),
            BeatActor.You, 30, "You catch small game");
    }

    [Fact]
    public void EachSweptOneIsTwelveTicks()
    {
        // Thagomizer: the Troodon beside you, then Carnotaurus 2 moved beside you too.
        var island = With(Island(), CarnoId, Grid.Index(2, 4));
        var script = Build(island, island,
            new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4),
            new Struck(CarnoId, Grid.Index(2, 4), 5, false, false, true, false, 4),
            Time());
        Is(One(script, BeatKind.AlsoStruck), BeatActor.You, 12, "and the Carnotaurus 2: 5");
        Assert.Equal(CarnoId, One(script, BeatKind.AlsoStruck).Creature);
    }

    [Fact]
    public void AnAfflictionMakesNoBeatAndRidesOnTheHit()
    {
        var island = Island();
        var hit = new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4);
        var dazed = new Afflicted(TroodonId, Affliction.Dazed);
        var script = Build(island, island, hit, dazed, Time());
        Assert.Equal(new TurnEvent[] { hit, dazed }, One(script, BeatKind.YouStrike).Events);
        Assert.DoesNotContain(script.Beats, b => b.Kind != BeatKind.YouStrike && b.Events.Contains(dazed));
    }

    // ---- between actors -----------------------------------------------------------------

    [Fact]
    public void AGapIsFifteenTicksAndLeavesTheBannerAsItWas()
    {
        var script = Rested();
        var gap = script.Beats[1];
        Assert.Equal(BeatKind.Gap, gap.Kind);
        Is(gap, BeatActor.World, 15, "You rest");
        Assert.Equal(-1, gap.RowIndex);
        Assert.Empty(gap.Events);
    }

    [Fact]
    public void AFocusIsFifteenTicksAndAlreadyNamesWhatItDoes()
    {
        var script = Rested(new Noticed(CarnoId, Notice.Spotted), Step(CarnoId, Gait.Hunt, Row5(4), Row5(3)));
        var focus = One(script, BeatKind.Focus);
        Is(focus, BeatActor.Creature, 15, "Carnotaurus 2 spots you");
        Assert.Equal(CarnoId, focus.Creature);
        Assert.Empty(focus.Events);
    }

    // ---- the enemies' time (H93) --------------------------------------------------------

    /// <summary>The owner's ask (H93, Nov 15 2026): each hunter singled out before it acts, and two actors plainly apart.</summary>
    private const int OwnersLeast = 12;

    [Fact]
    public void AFocusAndAGapAreEachAtLeastTwelveTicks()
    {
        Assert.InRange(BeatTicks.Focus, OwnersLeast, TurnScript.MaxBeatTicks);
        Assert.InRange(BeatTicks.Gap, OwnersLeast, TurnScript.MaxBeatTicks);
        // And so in a script, where every beat is clamped to the most.
        var script = TwoHunters();
        Assert.Equal(2, script.Beats.Count(b => b.Kind == BeatKind.Focus));
        Assert.Equal(3, script.Beats.Count(b => b.Kind == BeatKind.Gap));
        Assert.All(script.Beats.Where(b => b.Kind is BeatKind.Focus or BeatKind.Gap),
            b => Assert.InRange(b.Ticks, OwnersLeast, TurnScript.MaxBeatTicks));
    }

    [Fact]
    public void AQuietTurnIsAboutASecondAndOneWithTwoHuntersAboutSix()
    {
        // A walk with nothing known: your beat, a Gap, Out of sight, Time passes.
        var empty = Run(Here);
        var quiet = Build(empty, YouAt(empty, Row5(1)), Walk(Here, Row5(1)), Time());
        Assert.Equal(new[] { BeatKind.YouWalk, BeatKind.Gap, BeatKind.Unseen, BeatKind.TimePasses }, quiet.Beats.Select(b => b.Kind));
        Assert.Equal(39, quiet.TotalTicks);              // 1.3 s at Normal, 0.65 s at Fast
        Assert.Equal(195, TwoHunters().TotalTicks);      // 6.5 s at Normal, 3.25 s at Fast
    }

    /// <summary>You walk a cell; a Troodon and Carnotaurus 2 each spot you, step two cells and bite; time passes.</summary>
    private static TurnScript TwoHunters()
    {
        var to = Row5(3);
        int troodonFrom = Row5(6), carnoFrom = Grid.Index(3, 8);
        var island = Run(Here,
            Predator(1, PredatorType.Troodon, troodonFrom),
            Predator(2, PredatorType.Carnotaurus, carnoFrom, ordinal: 2));
        var after = With(With(YouAt(island, to), 1, Row5(4), alert: 4), 2, Grid.Index(3, 6), alert: 4);
        return Build(island, after, Walk(Here, to),
            new Noticed(1, Notice.Spotted), Step(1, Gait.Hunt, troodonFrom, Row5(5), Row5(4)), new Bit(1, 9, false, false),
            new Noticed(2, Notice.Spotted), Step(2, Gait.Hunt, carnoFrom, Grid.Index(3, 7), Grid.Index(3, 6)), new Bit(2, 9, false, false),
            Time());
    }

    // ---- a predator's beats -------------------------------------------------------------

    [Theory]
    // The game's enums are internal, so a theory takes them as their numbers.
    [InlineData((int)Notice.Spotted, "Carnotaurus 2 spots you")]
    [InlineData((int)Notice.Heard, "Carnotaurus 2 hears you")]
    [InlineData((int)Notice.LostTrail, "Carnotaurus 2 loses your trail")]
    public void ItNoticesInTwelveTicks(int kind, string banner)
    {
        var script = Rested(new Noticed(CarnoId, (Notice)kind), new Stayed(CarnoId, Idle.Waits));
        Is(One(script, BeatKind.Notices), BeatActor.Creature, 12, banner);
    }

    [Theory]
    [InlineData((int)Gait.Hunt, "Carnotaurus 2 hunts you")]
    [InlineData((int)Gait.Search, "Carnotaurus 2 searches")]
    [InlineData((int)Gait.Wander, "Carnotaurus 2 wanders")]
    public void ItStepsInEightTicksACell(int gaitNumber, string banner)
    {
        var gait = (Gait)gaitNumber;
        Is(One(Rested(Step(CarnoId, gait, Row5(4), Row5(5))), BeatKind.Steps), BeatActor.Creature, 8, banner);
        Is(One(Rested(Step(CarnoId, gait, Row5(4), Row5(5), Row5(6))), BeatKind.Steps), BeatActor.Creature, 16, banner);
    }

    [Theory]
    [InlineData((int)Idle.Waits, 4, "Carnotaurus 2 waits")]
    [InlineData((int)Idle.Dazed, 10, "Carnotaurus 2 is dazed")]
    [InlineData((int)Idle.Held, 10, "Carnotaurus 2 is held by venom")]
    public void ItStays(int why, int ticks, string banner)
    {
        Is(One(Rested(new Stayed(CarnoId, (Idle)why)), BeatKind.Stays), BeatActor.Creature, ticks, banner);
    }

    [Fact]
    public void ItBitesInTwentyTicksAndADodgeIsSixteen()
    {
        Is(One(Rested(new Stayed(TroodonId, Idle.Waits), new Bit(TroodonId, 12, false, false)), BeatKind.Bites),
            BeatActor.Creature, 20, "Troodon bites you: 12");
        Is(One(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 0, true, false)), BeatKind.Dodged),
            BeatActor.Creature, 16, "You dodge the Carnotaurus 2");
    }

    [Fact]
    public void ItIsHurtInTwelveTicks()
    {
        Is(One(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 12, false, false), new Hurt(CarnoId, Row5(4), 4, HurtBy.Spikes, false)), BeatKind.IsHurt),
            BeatActor.Creature, 12, "Your spikes hurt the Carnotaurus 2: 4");
        Is(One(Rested(new Stayed(CarnoId, Idle.Waits), new Hurt(CarnoId, Row5(4), 5, HurtBy.Bleed, false)), BeatKind.IsHurt),
            BeatActor.Creature, 12, "Carnotaurus 2 bleeds: 5");

        var island = Island();
        var bled = Build(island, Without(island, CarnoId), Rest(),
            new Stayed(CarnoId, Idle.Waits), new Hurt(CarnoId, Row5(4), 5, HurtBy.Bleed, true), Time());
        Is(One(bled, BeatKind.IsHurt), BeatActor.Creature, 12, "Carnotaurus 2 bleeds: 5 and is defeated");
        var spiked = Build(island, Without(island, CarnoId), Rest(),
            new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 12, false, false), new Hurt(CarnoId, Row5(4), 4, HurtBy.Spikes, true), Time());
        Is(One(spiked, BeatKind.IsHurt), BeatActor.Creature, 12, "Your spikes hurt the Carnotaurus 2: 4 and is defeated");
    }

    [Fact]
    public void SecondWindIsTwentyFourTicks()
    {
        var script = Rested(new Stayed(TroodonId, Idle.Waits), new Bit(TroodonId, 40, false, false), new SecondWind());
        Is(One(script, BeatKind.SecondWind), BeatActor.You, 24, "Second wind: you are still standing");
    }

    // ---- small game, out of sight, arrivals ---------------------------------------------

    [Fact]
    public void SmallGameMovesTogetherInTenTicks()
    {
        var island = Run(Here, Game(5, Row5(4)), Game(6, Row5(5)));
        var moved = Build(island, With(With(island, 5, Row5(3)), 6, Row5(4)), Rest(),
            Step(5, Gait.Roam, Row5(4), Row5(3)), Step(6, Gait.Roam, Row5(5), Row5(4)), Time());
        var beat = One(moved, BeatKind.SmallGame);
        Is(beat, BeatActor.SmallGame, 10, "Small game moves");
        Assert.Equal(2, beat.Events.Count);

        var fled = Build(island, With(With(island, 5, Row5(5)), 6, Row5(4)), Rest(),
            Step(5, Gait.Flee, Row5(4), Row5(5)), Step(6, Gait.Roam, Row5(5), Row5(4)), Time());
        Is(One(fled, BeatKind.SmallGame), BeatActor.SmallGame, 10, "Small game scatters");
    }

    [Fact]
    public void ItArrivesInTwelveTicks()
    {
        var island = Island();
        var after = island.Clone();
        after.Creatures.Add(Predator(7, PredatorType.Carnotaurus, Row5(5), ordinal: 3));
        var beat = One(Build(island, after, Rest(), Time(), new Arrived(7, Row5(5), false)), BeatKind.Arrives);
        Is(beat, BeatActor.Creature, 12, "Carnotaurus 3 arrives");
        Assert.Equal(7, beat.Creature);
    }

    [Fact]
    public void OutOfSightIsSixTicksQuietAndSixteenWhenSomethingHeardYou()
    {
        Is(One(Rested(), BeatKind.Unseen), BeatActor.Unseen, 6, "Out of sight: quiet");
        var heard = Rested(new Noticed(AlloId, Notice.Heard), Step(AlloId, Gait.Search, Row5(10), Row5(9)));
        Is(One(heard, BeatKind.Unseen), BeatActor.Unseen, 16, "Something heard you");
    }

    // ---- time and the end ---------------------------------------------------------------

    [Fact]
    public void TimePassesInEightTicksAndCarriesTheGrowth()
    {
        var island = Island();
        var time = Time();
        var grew = new Grew(10, 11.4);
        var beat = One(Build(island, island, Rest(), time, grew), BeatKind.TimePasses);
        Is(beat, BeatActor.World, 8, "Time passes");
        Assert.Equal(new TurnEvent[] { time, grew }, beat.Events);
        Assert.DoesNotContain(BeatKind.TimeHurts, Build(island, island, Rest(), time, grew).Beats.Select(b => b.Kind));
    }

    [Theory]
    [InlineData(5, 0, "You are starving")]
    [InlineData(0, 5, "You are parched")]
    [InlineData(5, 5, "You are starving and parched")]
    public void TimeHurtsInSixteenTicks(int hunger, int thirst, string banner)
    {
        var island = Island();
        var script = Build(island, island, Rest(), new TimePassed(hunger, thirst, 0));
        var hurts = One(script, BeatKind.TimeHurts);
        Is(hurts, BeatActor.You, 16, banner);
        // Right after Time passes.
        Assert.Equal(BeatKind.TimePasses, script.Beats[script.Beats.ToList().IndexOf(hurts) - 1].Kind);
    }

    [Fact]
    public void AStageIsFortyTicks()
    {
        var island = Island();
        var script = Build(island, island, Rest(), Time(), new Grew(24, 25.4), new StageReached(1));
        Is(One(script, BeatKind.Stage), BeatActor.You, 40, "You grew to Juvenile");
    }

    [Fact]
    public void TheApexIsThirtyTwoTicks()
    {
        var island = Island();
        var after = island.Clone();
        after.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4));
        var beat = One(Build(island, after, Rest(), Time(), new ApexArrived(9, Grid.Index(10, 0))), BeatKind.Apex);
        Is(beat, BeatActor.Creature, 32, "A Tyrannosaurus has your scent");
        Assert.Equal(9, beat.Creature);
    }

    [Fact]
    public void TheEndIsTwentyFourTicksWithItsReason()
    {
        var island = Island();
        var caught = Build(island, island, Rest(), new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 99, false, false),
            new RunEnded(false, EndReason.Caught, CarnoId));
        Is(One(caught, BeatKind.End), BeatActor.World, 24, "The Carnotaurus 2 caught you");

        Is(One(Build(island, island, Rest(), new TimePassed(5, 0, 0), new RunEnded(false, EndReason.Hunger, 0)), BeatKind.End),
            BeatActor.World, 24, "You ran out of food");
        Is(One(Build(island, island, Rest(), new TimePassed(0, 5, 0), new RunEnded(false, EndReason.Thirst, 0)), BeatKind.End),
            BeatActor.World, 24, "You ran out of water");
        Is(One(Build(island, island, Rest(), Time(), new RunEnded(true, EndReason.Outlasted, 0)), BeatKind.End),
            BeatActor.World, 24, "You outlasted the apex");

        var apex = Run(Here, Predator(9, PredatorType.Tyrannosaurus, Row5(3), alert: 4));
        var won = Build(apex, Without(apex, 9), new Struck(9, Row5(3), 30, false, false, false, true, 4), new RunEnded(true, EndReason.DefeatedApex, 0));
        Is(One(won, BeatKind.End), BeatActor.World, 24, "You defeated the apex");
        Is(One(won, BeatKind.YouStrike), BeatActor.You, 30, "You defeated the Tyrannosaurus");
    }

    [Fact]
    public void ARunLeftHasNoBeatAndKeepsTheListAsLastShown()
    {
        var island = Island();
        var shown = TurnScripts.RowsAtStart(island, Sees);
        island.LastTurn = shown.ToList();
        var script = Build(island, island, new RunEnded(false, EndReason.Left, 0));
        Assert.Empty(script.Beats);
        Assert.Equal(0, script.TotalTicks);
        Assert.Equal(shown, script.Rows);
    }

    [Fact]
    public void AChoiceThatTakesNoTurnPlaysNothing()
    {
        var island = Island();
        var script = Build(island, island, new InstinctsOffered(new[] { Instinct.Eyes, Instinct.Feet, Instinct.Quick }));
        Assert.Empty(script.Beats);
        Assert.Empty(Build(island, island).Beats);
    }
}
