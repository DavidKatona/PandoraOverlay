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
    public void YouWalkFiveTicksACell()
    {
        var island = Island();
        var beat = One(Build(island, YouAt(island, Row5(1)), Walk(Here, Row5(1)), Time()), BeatKind.YouWalk);
        Is(beat, BeatActor.You, 5, "You walk");
    }

    [Fact]
    public void YouSneakSixTicksACell()
    {
        var island = Island();
        var beat = One(Build(island, YouAt(island, Row5(1)), Sneak(Here, Row5(1)), Time()), BeatKind.YouWalk);
        Is(beat, BeatActor.You, 6, "You sneak");
    }

    [Fact]
    public void YouSprintThreeTicksACell()
    {
        var island = Island();
        var two = One(Build(island, YouAt(island, Row5(0)), Sprint(Here, Row5(1), Row5(0)), Time()), BeatKind.YouWalk);
        Is(two, BeatActor.You, 6, "You sprint");

        // A sprint of five cells is 15 (plan 3.1).
        var five = Grid.Index(2, 0);
        var path = new[] { Grid.Index(2, 4), Grid.Index(2, 3), Grid.Index(2, 2), Grid.Index(2, 1), five };
        Is(One(Build(island, YouAt(island, five), Sprint(Here, path), Time()), BeatKind.YouWalk), BeatActor.You, 15, "You sprint");
    }

    [Fact]
    public void YouEatDrinkAndRestEightTicksEach()
    {
        var island = Island();
        Is(One(Build(island, island, new Ate(Food.Plants, 30, 1), Time()), BeatKind.YouEat), BeatActor.You, 8, "You eat");
        Is(One(Build(island, island, new Drank(40, 1), Time()), BeatKind.YouDrink), BeatActor.You, 8, "You drink");
        Is(One(Build(island, island, Rest(), Time()), BeatKind.YouRest), BeatActor.You, 8, "You rest");
    }

    [Fact]
    public void YourStrikeIsNineTicksAndAHitThatDefeatsFadesSixMore()
    {
        var island = Island();
        var hit = One(Build(island, island, new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4), Time()), BeatKind.YouStrike);
        Is(hit, BeatActor.You, 9, "You hit the Troodon: 7");
        Assert.Equal(TroodonId, hit.Creature);

        var defeated = One(Build(island, Without(island, TroodonId), new Struck(TroodonId, Row5(3), 20, false, false, false, true, 4), Time()), BeatKind.YouStrike);
        Is(defeated, BeatActor.You, 15, "You defeated the Troodon");
    }

    [Fact]
    public void YourStrikeOnSmallGameSaysSmallGame()
    {
        var island = Run(Here, Game(GameId, Row5(3)));
        Is(One(Build(island, island, new Struck(GameId, Row5(3), 3, false, false, false, false, 4), Time()), BeatKind.YouStrike),
            BeatActor.You, 9, "You hit small game: 3");
        Is(One(Build(island, Without(island, GameId), new Struck(GameId, Row5(3), 9, false, false, false, true, 4), Time()), BeatKind.YouStrike),
            BeatActor.You, 15, "You catch small game");
    }

    [Fact]
    public void EachSweptOneIsSixTicks()
    {
        // Thagomizer: the Troodon beside you, then Carnotaurus 2 moved beside you too.
        var island = With(Island(), CarnoId, Grid.Index(2, 4));
        var script = Build(island, island,
            new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4),
            new Struck(CarnoId, Grid.Index(2, 4), 5, false, false, true, false, 4),
            Time());
        Is(One(script, BeatKind.AlsoStruck), BeatActor.You, 6, "and the Carnotaurus 2: 5");
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
    public void AGapIsThreeTicksAndLeavesTheBannerAsItWas()
    {
        var script = Rested();
        var gap = script.Beats[1];
        Assert.Equal(BeatKind.Gap, gap.Kind);
        Is(gap, BeatActor.World, 3, "You rest");
        Assert.Equal(-1, gap.RowIndex);
        Assert.Empty(gap.Events);
    }

    [Fact]
    public void AFocusIsThreeTicksAndAlreadyNamesWhatItDoes()
    {
        var script = Rested(new Noticed(CarnoId, Notice.Spotted), Step(CarnoId, Gait.Hunt, Row5(4), Row5(3)));
        var focus = One(script, BeatKind.Focus);
        Is(focus, BeatActor.Creature, 3, "Carnotaurus 2 spots you");
        Assert.Equal(CarnoId, focus.Creature);
        Assert.Empty(focus.Events);
    }

    // ---- a predator's beats -------------------------------------------------------------

    [Theory]
    // The game's enums are internal, so a theory takes them as their numbers.
    [InlineData((int)Notice.Spotted, "Carnotaurus 2 spots you")]
    [InlineData((int)Notice.Heard, "Carnotaurus 2 hears you")]
    [InlineData((int)Notice.LostTrail, "Carnotaurus 2 loses your trail")]
    public void ItNoticesInFiveTicks(int kind, string banner)
    {
        var script = Rested(new Noticed(CarnoId, (Notice)kind), new Stayed(CarnoId, Idle.Waits));
        Is(One(script, BeatKind.Notices), BeatActor.Creature, 5, banner);
    }

    [Theory]
    [InlineData((int)Gait.Hunt, "Carnotaurus 2 hunts you")]
    [InlineData((int)Gait.Search, "Carnotaurus 2 searches")]
    [InlineData((int)Gait.Wander, "Carnotaurus 2 wanders")]
    public void ItStepsInFourTicksACell(int gaitNumber, string banner)
    {
        var gait = (Gait)gaitNumber;
        Is(One(Rested(Step(CarnoId, gait, Row5(4), Row5(5))), BeatKind.Steps), BeatActor.Creature, 4, banner);
        Is(One(Rested(Step(CarnoId, gait, Row5(4), Row5(5), Row5(6))), BeatKind.Steps), BeatActor.Creature, 8, banner);
    }

    [Theory]
    [InlineData((int)Idle.Waits, 2, "Carnotaurus 2 waits")]
    [InlineData((int)Idle.Dazed, 5, "Carnotaurus 2 is dazed")]
    [InlineData((int)Idle.Held, 5, "Carnotaurus 2 is held by venom")]
    public void ItStays(int why, int ticks, string banner)
    {
        Is(One(Rested(new Stayed(CarnoId, (Idle)why)), BeatKind.Stays), BeatActor.Creature, ticks, banner);
    }

    [Fact]
    public void ItBitesInTenTicksAndADodgeIsEight()
    {
        Is(One(Rested(new Stayed(TroodonId, Idle.Waits), new Bit(TroodonId, 12, false, false)), BeatKind.Bites),
            BeatActor.Creature, 10, "Troodon bites you: 12");
        Is(One(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 0, true, false)), BeatKind.Dodged),
            BeatActor.Creature, 8, "You dodge the Carnotaurus 2");
    }

    [Fact]
    public void ItIsHurtInSixTicks()
    {
        Is(One(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 12, false, false), new Hurt(CarnoId, Row5(4), 4, HurtBy.Spikes, false)), BeatKind.IsHurt),
            BeatActor.Creature, 6, "Your spikes hurt the Carnotaurus 2: 4");
        Is(One(Rested(new Stayed(CarnoId, Idle.Waits), new Hurt(CarnoId, Row5(4), 5, HurtBy.Bleed, false)), BeatKind.IsHurt),
            BeatActor.Creature, 6, "Carnotaurus 2 bleeds: 5");

        var island = Island();
        var bled = Build(island, Without(island, CarnoId), Rest(),
            new Stayed(CarnoId, Idle.Waits), new Hurt(CarnoId, Row5(4), 5, HurtBy.Bleed, true), Time());
        Is(One(bled, BeatKind.IsHurt), BeatActor.Creature, 6, "Carnotaurus 2 bleeds: 5 and is defeated");
        var spiked = Build(island, Without(island, CarnoId), Rest(),
            new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 12, false, false), new Hurt(CarnoId, Row5(4), 4, HurtBy.Spikes, true), Time());
        Is(One(spiked, BeatKind.IsHurt), BeatActor.Creature, 6, "Your spikes hurt the Carnotaurus 2: 4 and is defeated");
    }

    [Fact]
    public void SecondWindIsTwelveTicks()
    {
        var script = Rested(new Stayed(TroodonId, Idle.Waits), new Bit(TroodonId, 40, false, false), new SecondWind());
        Is(One(script, BeatKind.SecondWind), BeatActor.You, 12, "Second wind: you are still standing");
    }

    // ---- small game, out of sight, arrivals ---------------------------------------------

    [Fact]
    public void SmallGameMovesTogetherInFiveTicks()
    {
        var island = Run(Here, Game(5, Row5(4)), Game(6, Row5(5)));
        var moved = Build(island, With(With(island, 5, Row5(3)), 6, Row5(4)), Rest(),
            Step(5, Gait.Roam, Row5(4), Row5(3)), Step(6, Gait.Roam, Row5(5), Row5(4)), Time());
        var beat = One(moved, BeatKind.SmallGame);
        Is(beat, BeatActor.SmallGame, 5, "Small game moves");
        Assert.Equal(2, beat.Events.Count);

        var fled = Build(island, With(With(island, 5, Row5(5)), 6, Row5(4)), Rest(),
            Step(5, Gait.Flee, Row5(4), Row5(5)), Step(6, Gait.Roam, Row5(5), Row5(4)), Time());
        Is(One(fled, BeatKind.SmallGame), BeatActor.SmallGame, 5, "Small game scatters");
    }

    [Fact]
    public void ItArrivesInSixTicks()
    {
        var island = Island();
        var after = island.Clone();
        after.Creatures.Add(Predator(7, PredatorType.Carnotaurus, Row5(5), ordinal: 3));
        var beat = One(Build(island, after, Rest(), Time(), new Arrived(7, Row5(5), false)), BeatKind.Arrives);
        Is(beat, BeatActor.Creature, 6, "Carnotaurus 3 arrives");
        Assert.Equal(7, beat.Creature);
    }

    [Fact]
    public void OutOfSightIsThreeTicksQuietAndEightWhenSomethingHeardYou()
    {
        Is(One(Rested(), BeatKind.Unseen), BeatActor.Unseen, 3, "Out of sight: quiet");
        var heard = Rested(new Noticed(AlloId, Notice.Heard), Step(AlloId, Gait.Search, Row5(10), Row5(9)));
        Is(One(heard, BeatKind.Unseen), BeatActor.Unseen, 8, "Something heard you");
    }

    // ---- time and the end ---------------------------------------------------------------

    [Fact]
    public void TimePassesInFourTicksAndCarriesTheGrowth()
    {
        var island = Island();
        var time = Time();
        var grew = new Grew(10, 11.4);
        var beat = One(Build(island, island, Rest(), time, grew), BeatKind.TimePasses);
        Is(beat, BeatActor.World, 4, "Time passes");
        Assert.Equal(new TurnEvent[] { time, grew }, beat.Events);
        Assert.DoesNotContain(BeatKind.TimeHurts, Build(island, island, Rest(), time, grew).Beats.Select(b => b.Kind));
    }

    [Theory]
    [InlineData(5, 0, "You are starving")]
    [InlineData(0, 5, "You are parched")]
    [InlineData(5, 5, "You are starving and parched")]
    public void TimeHurtsInEightTicks(int hunger, int thirst, string banner)
    {
        var island = Island();
        var script = Build(island, island, Rest(), new TimePassed(hunger, thirst, 0));
        var hurts = One(script, BeatKind.TimeHurts);
        Is(hurts, BeatActor.You, 8, banner);
        // Right after Time passes.
        Assert.Equal(BeatKind.TimePasses, script.Beats[script.Beats.ToList().IndexOf(hurts) - 1].Kind);
    }

    [Fact]
    public void AStageIsTwentyTicks()
    {
        var island = Island();
        var script = Build(island, island, Rest(), Time(), new Grew(24, 25.4), new StageReached(1));
        Is(One(script, BeatKind.Stage), BeatActor.You, 20, "You grew to Juvenile");
    }

    [Fact]
    public void TheApexIsSixteenTicks()
    {
        var island = Island();
        var after = island.Clone();
        after.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4));
        var beat = One(Build(island, after, Rest(), Time(), new ApexArrived(9, Grid.Index(10, 0))), BeatKind.Apex);
        Is(beat, BeatActor.Creature, 16, "A Tyrannosaurus has your scent");
        Assert.Equal(9, beat.Creature);
    }

    [Fact]
    public void TheEndIsTwelveTicksWithItsReason()
    {
        var island = Island();
        var caught = Build(island, island, Rest(), new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 99, false, false),
            new RunEnded(false, EndReason.Caught, CarnoId));
        Is(One(caught, BeatKind.End), BeatActor.World, 12, "The Carnotaurus 2 caught you");

        Is(One(Build(island, island, Rest(), new TimePassed(5, 0, 0), new RunEnded(false, EndReason.Hunger, 0)), BeatKind.End),
            BeatActor.World, 12, "You ran out of food");
        Is(One(Build(island, island, Rest(), new TimePassed(0, 5, 0), new RunEnded(false, EndReason.Thirst, 0)), BeatKind.End),
            BeatActor.World, 12, "You ran out of water");
        Is(One(Build(island, island, Rest(), Time(), new RunEnded(true, EndReason.Outlasted, 0)), BeatKind.End),
            BeatActor.World, 12, "You outlasted the apex");

        var apex = Run(Here, Predator(9, PredatorType.Tyrannosaurus, Row5(3), alert: 4));
        var won = Build(apex, Without(apex, 9), new Struck(9, Row5(3), 30, false, false, false, true, 4), new RunEnded(true, EndReason.DefeatedApex, 0));
        Is(One(won, BeatKind.End), BeatActor.World, 12, "You defeated the apex");
        Is(One(won, BeatKind.YouStrike), BeatActor.You, 15, "You defeated the Tyrannosaurus");
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
