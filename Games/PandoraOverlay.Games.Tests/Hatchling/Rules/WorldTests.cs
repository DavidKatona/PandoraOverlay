using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The world's turn (<see cref="World.Turn"/>), step by step on hand-built boards: a
/// predator's mind (it sees you, hears you, loses your trail), its steps and its
/// wander, its bite, a daze and a bleed; small game fleeing and roaming; time
/// (the drains, hunger and thirst, healing, growth, the end by either); the offers
/// a line owes and "unseen" at Adult (H46); the arrivals at each danger, the apex
/// and its wait. Where a roll decides, the generator is set to a seed whose rolls
/// are known, and the number of rolls taken is held too: a roll taken where the
/// design takes none would shift every run after it.
/// </summary>
public class WorldTests
{
    private const int Middle = Turns.Middle;
    private static int Right(int steps) => Turns.Right(steps);

    // ---- Helpers -----------------------------------------------------------------------

    /// <summary>The world's turn alone, on a copy of <paramref name="run"/>, with its own generator: the run given is unchanged.</summary>
    private static (RunState After, List<TurnEvent> Events, Rng Rng) Turn(RunState run, int noise = 0, double drain = 1)
    {
        var before = Runs.Fingerprint(run);
        var copy = run.Clone();
        var rng = new Rng(copy.Rng);
        var events = new List<TurnEvent>();
        World.Turn(copy, rng, noise, drain, events);
        copy.Rng = rng.State;
        Assert.Equal(before, Runs.Fingerprint(run));
        return (copy, events, rng);
    }

    /// <summary>A copy of <paramref name="run"/> whose generator's next rolls pass <paramref name="rolls"/>, in order.</summary>
    private static RunState Rolling(RunState run, params Func<double, bool>[] rolls) => BiteTests.Rolling(run, rolls);

    /// <summary>The state of <paramref name="run"/>'s generator after <paramref name="rolls"/> rolls.</summary>
    private static uint After(RunState run, int rolls)
    {
        var rng = new Rng(run.Rng);
        for (var i = 0; i < rolls; i++) rng.NextDouble();
        return rng.State;
    }

    /// <summary>The <paramref name="skip"/>+1-th roll of <paramref name="run"/>'s generator.</summary>
    private static double Roll(RunState run, int skip = 0)
    {
        var rng = new Rng(run.Rng);
        for (var i = 0; i < skip; i++) rng.NextDouble();
        return rng.NextDouble();
    }

    /// <summary>A copy of <paramref name="run"/> where only the middle row is open (every other cell rock): a walk along it has one way.</summary>
    private static RunState Corridor(RunState run)
    {
        var copy = run.Clone();
        for (var cell = 0; cell < Tuning.Cells; cell++)
            if (cell / Tuning.Columns != Middle / Tuning.Columns) copy.Tiles[cell] = Terrain.Rock;
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> with food and water at <paramref name="food"/> and <paramref name="water"/>.</summary>
    private static RunState Fed(RunState run, double food, double water)
    {
        var copy = run.Clone();
        copy.You.Food = food;
        copy.You.Water = water;
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> at turn <paramref name="turn"/>.</summary>
    private static RunState AtTurn(RunState run, int turn)
    {
        var copy = run.Clone();
        copy.Turn = turn;
        return copy;
    }

    /// <summary>A board where nothing grows, heals or hurts in a turn at drain 0: food and water at 20.</summary>
    private static RunState Still(string species, double growth = 0, Terrain ground = Terrain.Grass) =>
        Fed(Turns.Board(species, ground, growth), 20, 20);

    /// <summary>The events of creature <paramref name="id"/>'s action, in order.</summary>
    private static List<TurnEvent> Of(IEnumerable<TurnEvent> events, int id) => events.Where(e => e switch
    {
        Noticed n => n.Creature == id,
        Stepped s => s.Creature == id,
        Stayed s => s.Creature == id,
        Bit b => b.Creature == id,
        Hurt h => h.Creature == id,
        _ => false,
    }).ToList();

    /// <summary>The cells of the top row: five or more from the middle, where a creature put is far from you.</summary>
    private static int Far(int k) => k;

    // ---- B2, B3: sight and hearing -----------------------------------------------------

    [Fact]
    public void APredatorSeesYouWithinItsSightAndHunts()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(3));   // sight 3
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        var mine = Of(events, id);
        Assert.Equal(new Noticed(id, Notice.Spotted), mine[0]);
        Assert.Equal(Gait.Hunt, Assert.IsType<Stepped>(mine[1]).Gait);
        var it = after.Creatures.Single(c => c.Id == id);
        Assert.Equal(Tuning.AlertSeen, it.Alert);
        Assert.Equal(Middle, it.Target);
        Assert.True(it.SawYou);
    }

    [Fact]
    public void APredatorDoesNotSeeYouBeyondItsSight()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(4));
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        Assert.DoesNotContain(Of(events, id), e => e is Noticed);
        var it = after.Creatures.Single(c => c.Id == id);
        Assert.Equal(0, it.Alert);
        Assert.False(it.SawYou);
    }

    [Fact]
    public void InCoverYouAreNotSeenButBesideYouAlwaysAre()
    {
        var hidden = Turns.Board("Dryosaurus", Terrain.Forest).Put(PredatorType.Allosaurus, Right(3));
        var far = hidden.Creatures[^1].Id;
        Assert.DoesNotContain(Of(Turn(hidden).Events, far), e => e is Noticed);

        var beside = Turns.Board("Dryosaurus", Terrain.Forest).Put(PredatorType.Troodon, Right(1));
        var near = beside.Creatures[^1].Id;
        var mine = Of(Turn(beside).Events, near);
        Assert.Equal(new Noticed(near, Notice.Spotted), mine[0]);
        Assert.Contains(mine, e => e is Bit);
    }

    [Fact]
    public void FreezeAfterARestHidesYouOnOpenGround()
    {
        var frozen = Turns.Board("Hypsilophodon").Put(PredatorType.Allosaurus, Right(3));
        var id = frozen.Creatures[^1].Id;
        Assert.DoesNotContain(Of(Turns.Must(frozen, new PlayerAction.Rest()).Events, id), e => e is Noticed);

        // The same rest without Freeze is seen.
        var plain = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(3));
        Assert.Contains(new Noticed(plain.Creatures[^1].Id, Notice.Spotted), Turns.Must(plain, new PlayerAction.Rest()).Events);

        // And Freeze holds only after a Rest: after any other move it is seen.
        var standing = Turns.Board("Hypsilophodon").Put(PredatorType.Allosaurus, Right(3));
        standing.You.LastMove = MoveKind.Eat;
        Assert.Contains(new Noticed(id, Notice.Spotted), Turn(standing).Events);
    }

    [Fact]
    public void SpottedIsNewsOnlyWhenItDidNotSeeYouLastAction()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(3));
        run.Creatures[^1].SawYou = true;
        run.Creatures[^1].Alert = Tuning.AlertSeen;
        var id = run.Creatures[^1].Id;
        var mine = Of(Turn(run).Events, id);
        Assert.DoesNotContain(mine, e => e is Noticed);
        Assert.Equal(Gait.Hunt, Assert.IsType<Stepped>(mine[0]).Gait);
    }

    [Theory]
    [InlineData(4, 4, true)]
    [InlineData(4, 3, false)]
    [InlineData(2, 2, true)]
    [InlineData(5, 0, false)]
    public void APredatorHearsYouAtTheNoisesDistanceAndNotBeyond(int distance, int noise, bool heard)
    {
        // In cover, so it cannot see you: only its ears decide.
        var run = Turns.Board("Dryosaurus", Terrain.Forest).Put(PredatorType.Allosaurus, Right(distance));
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run, noise: noise);
        var it = after.Creatures.Single(c => c.Id == id);
        if (heard)
        {
            Assert.Equal(new Noticed(id, Notice.Heard), Of(events, id)[0]);
            Assert.Equal(Middle, it.Target);
            // Alerted to 3, then a point lost after its step unless it now stands beside you.
            Assert.Equal(Island.Grid.Distance(it.At, Middle) == 1 ? Tuning.AlertHeard : Tuning.AlertHeard - 1, it.Alert);
        }
        else
        {
            Assert.DoesNotContain(Of(events, id), e => e is Noticed);
            Assert.Equal(0, it.Alert);
        }
    }

    [Fact]
    public void HeardIsNewsOnlyWhenItsAlertWas0()
    {
        var run = Corridor(Turns.Board("Dryosaurus", Terrain.Forest)).Put(PredatorType.Allosaurus, Right(5), alert: 2);
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run, noise: 5);
        var mine = Of(events, id);
        Assert.DoesNotContain(mine, e => e is Noticed);
        Assert.Equal(new Stepped(id, Right(5), new[] { Right(4) }, Gait.Search), mine[0], new SteppedEq());
        Assert.Equal(Tuning.AlertHeard - 1, after.Creatures.Single(c => c.Id == id).Alert);   // set to 3, then fell
    }

    [Fact]
    public void YourMovesNoiseReachesTheWorld()
    {
        // A Tenontosaurus walks a cell (noise 3) in the forest: an Allosaurus three from where it lands hears it, one four away does not.
        var run = Turns.Board("Tenontosaurus", Terrain.Forest)
            .Put(PredatorType.Allosaurus, Right(4)).Put(PredatorType.Allosaurus, Right(-4));
        var near = run.Creatures[^2].Id;
        var far = run.Creatures[^1].Id;
        var result = Turns.Must(run, new PlayerAction.Move(Right(1)));
        Assert.Equal(3, Assert.IsType<Walked>(result.Events[0]).Noise);
        Assert.Contains(new Noticed(near, Notice.Heard), result.Events);
        Assert.DoesNotContain(new Noticed(far, Notice.Heard), result.Events);
    }

    // ---- B4: the steps ------------------------------------------------------------------

    [Fact]
    public void AFastPredatorTakesTwoStepsTowardYou()
    {
        var run = Corridor(Turns.Board("Dryosaurus")).Put(PredatorType.Troodon, Right(5), alert: 3);
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        Assert.Equal(new Stepped(id, Right(5), new[] { Right(4), Right(3) }, Gait.Search), Of(events, id)[0], new SteppedEq());
        Assert.Equal(Right(3), after.Creatures.Single(c => c.Id == id).At);
    }

    [Fact]
    public void AStepOntoWaterEndsTheWalk()
    {
        var run = Corridor(Turns.Board("Dryosaurus")).Ground(Right(4), Terrain.Water).Put(PredatorType.Troodon, Right(5), alert: 3);
        var id = run.Creatures[^1].Id;
        Assert.Equal(new Stepped(id, Right(5), new[] { Right(4) }, Gait.Search), Of(Turn(run).Events, id)[0], new SteppedEq());
    }

    [Fact]
    public void AWalkStopsBesideYouAndNeverOnYou()
    {
        var run = Corridor(Turns.Board("Dryosaurus")).Put(PredatorType.Troodon, Right(2), alert: 3);
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        var mine = Of(events, id);
        Assert.Equal(new Noticed(id, Notice.Spotted), mine[0]);   // sight 2: it saw you
        Assert.Equal(new Stepped(id, Right(2), new[] { Right(1) }, Gait.Hunt), mine[1], new SteppedEq());
        Assert.Equal(Right(1), after.Creatures.Single(c => c.Id == id).At);
    }

    [Fact]
    public void ANoWayIsAWait()
    {
        // Rock between: it is alerted and does not move.
        var run = Corridor(Turns.Board("Dryosaurus")).Ground(Right(4), Terrain.Rock).Put(PredatorType.Troodon, Right(5), alert: 3);
        var id = run.Creatures[^1].Id;
        Assert.Equal(new Stayed(id, Idle.Waits), Of(Turn(run).Events, id)[0]);
    }

    [Fact]
    public void ASlowedPredatorTakesAStepLessAndTheSlowWearsOff()
    {
        var fast = Corridor(Turns.Board("Dryosaurus")).Put(PredatorType.Troodon, Right(5), alert: 3);
        fast.Creatures[^1].Slowed = 2;
        var id = fast.Creatures[^1].Id;
        var (after, events, _) = Turn(fast);
        Assert.Equal(new Stepped(id, Right(5), new[] { Right(4) }, Gait.Search), Of(events, id)[0], new SteppedEq());
        Assert.Equal(1, after.Creatures.Single(c => c.Id == id).Slowed);

        var slow = Corridor(Turns.Board("Dryosaurus")).Put(PredatorType.Allosaurus, Right(5), alert: 3);
        slow.Creatures[^1].Slowed = 1;
        var held = slow.Creatures[^1].Id;
        var (afterSlow, slowEvents, _) = Turn(slow);
        Assert.Equal(new Stayed(held, Idle.Held), Of(slowEvents, held)[0]);
        Assert.Equal(0, afterSlow.Creatures.Single(c => c.Id == held).Slowed);
        Assert.Equal(Right(5), afterSlow.Creatures.Single(c => c.Id == held).At);
    }

    [Fact]
    public void ASearcherLosesAPointAnActionAndAtZeroYourTrail()
    {
        var run = Corridor(Turns.Board("Dryosaurus")).Put(PredatorType.Allosaurus, Right(5), alert: 1);
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        var mine = Of(events, id);
        Assert.Equal(2, mine.Count);
        Assert.IsType<Stepped>(mine[0]);
        Assert.Equal(new Noticed(id, Notice.LostTrail), mine[1]);   // after the steps
        Assert.Equal(0, after.Creatures.Single(c => c.Id == id).Alert);
    }

    [Fact]
    public void ASearcherThatStepsBesideYouKeepsItsAlertAndBites()
    {
        // H43: at alert 1, hidden from it, it steps beside you: no fall, no lost trail, a bite.
        var run = Corridor(Turns.Board("Dryosaurus", Terrain.Forest)).Put(PredatorType.Allosaurus, Right(2), alert: 1);
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        var mine = Of(events, id);
        Assert.Equal(new Stepped(id, Right(2), new[] { Right(1) }, Gait.Search), mine[0], new SteppedEq());
        Assert.IsType<Bit>(mine[1]);
        Assert.DoesNotContain(mine, e => e is Noticed);
        Assert.Equal(1, after.Creatures.Single(c => c.Id == id).Alert);
    }

    [Fact]
    public void ASearcherHeadsForTheCellItHeardYouOn()
    {
        // Its target is a cell you have left: it walks there, not to you.
        var run = Corridor(Turns.Board("Dryosaurus", Terrain.Forest)).Put(PredatorType.Troodon, Right(-5), alert: 3);
        run.Creatures[^1].Target = Right(-3);
        var id = run.Creatures[^1].Id;
        Assert.Equal(new Stepped(id, Right(-5), new[] { Right(-4), Right(-3) }, Gait.Search), Of(Turn(run).Events, id)[0], new SteppedEq());

        var there = Corridor(Turns.Board("Dryosaurus", Terrain.Forest)).Put(PredatorType.Troodon, Right(-3), alert: 3);
        there.Creatures[^1].Target = Right(-3);
        Assert.Equal(new Stayed(there.Creatures[^1].Id, Idle.Waits), Of(Turn(there).Events, there.Creatures[^1].Id)[0]);
    }

    [Fact]
    public void TheApexHuntsFromAnywhereAndNeverLosesYou()
    {
        var run = Corridor(Turns.Board("Dryosaurus", Terrain.Forest)).Put(PredatorType.Tyrannosaurus, Right(5));
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        var mine = Of(events, id);
        Assert.Equal(new Stepped(id, Right(5), new[] { Right(4) }, Gait.Hunt), mine[0], new SteppedEq());
        Assert.Single(mine);   // nothing noticed: it did not see you, and it never loses you
        var it = after.Creatures.Single(c => c.Id == id);
        Assert.Equal(Tuning.AlertSeen, it.Alert);
        Assert.True(it.SawYou);
    }

    // ---- B5: the wander -----------------------------------------------------------------

    [Fact]
    public void AnUnawarePredatorWandersOnARollUnderTheWander()
    {
        var start = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(4));
        var run = Rolling(start, r => r < Tuning.Wander, r => true);
        var id = run.Creatures[^1].Id;
        var near = Island.Grid.Neighbours(Right(4)).ToList();   // all grass, all empty
        var (after, events, rng) = Turn(run);
        var expected = near[(int)(Roll(run, 1) * near.Count)];
        Assert.Equal(new Stepped(id, Right(4), new[] { expected }, Gait.Wander), Of(events, id)[0], new SteppedEq());
        Assert.Equal(expected, after.Creatures.Single(c => c.Id == id).At);
        Assert.Equal(After(run, 2), rng.State);
    }

    [Fact]
    public void AnUnawarePredatorWaitsOnARollAtTheWanderOrAbove()
    {
        var run = Rolling(Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(4)), r => r >= Tuning.Wander);
        var id = run.Creatures[^1].Id;
        var (_, events, rng) = Turn(run);
        Assert.Equal(new TurnEvent[] { new Stayed(id, Idle.Waits) }, Of(events, id));
        Assert.Equal(After(run, 1), rng.State);   // no pick
    }

    [Fact]
    public void ABoxedInPredatorWaitsAndTakesNoPick()
    {
        var start = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(4));
        foreach (var cell in Island.Grid.Neighbours(Right(4))) start.Tiles[cell] = Terrain.Rock;
        var run = Rolling(start, r => r < Tuning.Wander);
        var id = run.Creatures[^1].Id;
        var (_, events, rng) = Turn(run);
        Assert.Equal(new TurnEvent[] { new Stayed(id, Idle.Waits) }, Of(events, id));
        Assert.Equal(After(run, 1), rng.State);
    }

    // ---- B6, B7, B1: the bite, the bleed, the daze ----------------------------------------

    [Fact]
    public void APredatorThatEndsBesideYouBites()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Troodon, Right(2));   // sight 2
        var id = run.Creatures[^1].Id;
        var (after, events, _) = Turn(run);
        var mine = Of(events, id);
        Assert.Equal(new TurnEvent[] { new Noticed(id, Notice.Spotted) }, mine.Take(1));
        Assert.IsType<Stepped>(mine[1]);
        var bit = Assert.IsType<Bit>(mine[2]);
        Assert.Equal(3, mine.Count);
        if (!bit.Dodged) Assert.True(after.You.Bitten);
    }

    [Fact]
    public void ADazedPredatorDoesNothingButBleed()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Troodon, Right(1));
        var it = run.Creatures[^1];
        it.Dazed = true;
        it.Slowed = 1;
        it.Bleed = 2;
        it.BleedDamage = 3;
        var (after, events, rng) = Turn(run);
        Assert.Equal(new TurnEvent[] { new Stayed(it.Id, Idle.Dazed), new Hurt(it.Id, Right(1), 3, HurtBy.Bleed, false) }, Of(events, it.Id));
        var then = after.Creatures.Single(c => c.Id == it.Id);
        Assert.False(then.Dazed);
        Assert.Equal(1, then.Slowed);       // it did not act: the slow waits for its next action
        Assert.Equal(1, then.Bleed);
        Assert.Equal(it.Health - 3, then.Health);
        Assert.False(then.SawYou);          // it did not look
        Assert.Equal(run.Rng, rng.State);   // no roll: no notice, no step, no bite
        Assert.Equal(1.0, after.You.Health, 10);
    }

    [Fact]
    public void BleedingToZeroDefeatsAPredator()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Allosaurus, Right(5));
        var it = run.Creatures[^1];
        it.Bleed = 1;
        it.BleedDamage = it.Health;
        var (after, events, _) = Turn(run);
        var hurt = Assert.IsType<Hurt>(Of(events, it.Id)[^1]);
        Assert.True(hurt.Defeated);
        Assert.Equal(HurtBy.Bleed, hurt.By);
        Assert.DoesNotContain(after.Creatures, c => c.Id == it.Id);
        Assert.Equal(run.Kills + 1, after.Kills);
        Assert.Equal(Predators.Of(PredatorType.Allosaurus).Meat, after.Meat[hurt.At]);
        Assert.Null(after.Over);
    }

    [Fact]
    public void TheApexBledOutWinsTheRun()
    {
        var run = Turns.Board("Allosaurus", growth: 100).Put(PredatorType.Tyrannosaurus, Right(5));
        run.ApexAt = 0;
        var it = run.Creatures[^1];
        it.Bleed = 1;
        it.BleedDamage = it.Health;
        var (after, events, _) = Turn(run);
        Assert.Equal(new RunEnded(true, EndReason.DefeatedApex, 0), events[^1]);
        Assert.DoesNotContain(events, e => e is TimePassed);   // the world's turn stopped there
        Assert.Equal(run.Kills, after.Kills);
        Assert.True(after.Over!.Won);
    }

    [Fact]
    public void APredatorDefeatedBySpikesDoesNotBleedAfter()
    {
        var run = Rolling(Turns.Board("Kentrosaurus", growth: 75).Put(PredatorType.Troodon, Right(1)), r => true, r => true);
        var it = run.Creatures[^1];
        it.Health = 1;
        it.Bleed = 2;
        it.BleedDamage = 1;
        var mine = Of(Turn(run).Events, it.Id);
        Assert.Equal(HurtBy.Spikes, Assert.IsType<Hurt>(mine[^1]).By);
        Assert.True(((Hurt)mine[^1]).Defeated);
        Assert.DoesNotContain(mine, e => e is Hurt { By: HurtBy.Bleed });
    }

    [Fact]
    public void ABiteThatCatchesYouStopsTheWorld()
    {
        var run = Turns.Board("Dryosaurus").Put(PredatorType.Carnotaurus, Right(1)).Put(PredatorType.Troodon, Right(-1)).PutSmallGame(Right(4));
        run.You.Health = 0.001;
        var catcher = run.Creatures[0].Id;
        var second = run.Creatures[1].Id;
        var (after, events, _) = Turn(run);
        Assert.Equal(new RunEnded(false, EndReason.Caught, catcher), events[^1]);
        Assert.Empty(Of(events, second));   // it never acted
        Assert.DoesNotContain(events, e => e is TimePassed);
        Assert.Equal(EndReason.Caught, after.Over!.Reason);
    }

    [Fact]
    public void PredatorsActInIdOrderOneStepOrStayEach()
    {
        var run = Turns.Board("Dryosaurus")
            .Put(PredatorType.Allosaurus, Right(5)).PutSmallGame(Right(-4)).Put(PredatorType.Troodon, Right(-3), alert: 2)
            .Put(PredatorType.Herrerasaurus, Far(2));
        var predators = run.Creatures.Where(c => c.Kind == CreatureKind.Predator).Select(c => c.Id).ToList();
        var (_, events, _) = Turn(run);
        var actors = events.Select(e => e switch { Stepped s => s.Creature, Stayed s => s.Creature, _ => 0 })
            .Where(id => predators.Contains(id)).ToList();
        Assert.Equal(predators, actors);
        var lastOfEach = predators.Select(id => events.FindLastIndex(e => Of(new[] { e }, id).Count > 0)).ToList();
        var firstOfEach = predators.Select(id => events.FindIndex(e => Of(new[] { e }, id).Count > 0)).ToList();
        for (var k = 1; k < predators.Count; k++) Assert.True(firstOfEach[k] > lastOfEach[k - 1]);
    }

    // ---- C: small game ------------------------------------------------------------------

    [Fact]
    public void SmallGameWithinTwoFleesToTheFurthestNeighbour()
    {
        var run = Rolling(Turns.Board("Dryosaurus").PutSmallGame(Right(2)), r => r < Tuning.Flee);
        var id = run.Creatures[^1].Id;
        var near = Island.Grid.Neighbours(Right(2)).Where(c => c != Middle).ToList();
        var furthest = near.First(c => Island.Grid.Distance(c, Middle) == near.Max(n => Island.Grid.Distance(n, Middle)));
        var (after, events, rng) = Turn(run);
        Assert.Equal(new Stepped(id, Right(2), new[] { furthest }, Gait.Flee), events.OfType<Stepped>().Single(), new SteppedEq());
        Assert.Equal(furthest, after.Creatures.Single(c => c.Id == id).At);
        Assert.Equal(After(run, 1), rng.State);   // fled: no roam roll
    }

    [Fact]
    public void SmallGameThatDidNotFleeMayRoam()
    {
        var run = Rolling(Turns.Board("Dryosaurus").PutSmallGame(Right(2)), r => r >= Tuning.Flee, r => r < Tuning.Roam, r => true);
        var id = run.Creatures[^1].Id;
        var near = Island.Grid.Neighbours(Right(2)).Where(c => c != Middle).ToList();
        var (_, events, rng) = Turn(run);
        var expected = near[(int)(Roll(run, 2) * near.Count)];
        Assert.Equal(new Stepped(id, Right(2), new[] { expected }, Gait.Roam), events.OfType<Stepped>().Single(), new SteppedEq());
        Assert.Equal(After(run, 3), rng.State);
    }

    [Fact]
    public void SmallGameFurtherThanTwoOnlyRoams()
    {
        var roams = Rolling(Turns.Board("Dryosaurus").PutSmallGame(Right(4)), r => r < Tuning.Roam, r => true);
        var (_, events, rng) = Turn(roams);
        Assert.Equal(Gait.Roam, events.OfType<Stepped>().Single().Gait);
        Assert.Equal(After(roams, 2), rng.State);

        var stays = Rolling(Turns.Board("Dryosaurus").PutSmallGame(Right(4)), r => r >= Tuning.Roam);
        var (_, none, rng2) = Turn(stays);
        Assert.Empty(none.OfType<Stepped>());
        Assert.Equal(After(stays, 1), rng2.State);
    }

    [Fact]
    public void SmallGameBoxedInStaysAndRollsNothing()
    {
        var run = Turns.Board("Dryosaurus").PutSmallGame(Right(2));
        foreach (var cell in Island.Grid.Neighbours(Right(2))) if (cell != Middle) run.Tiles[cell] = Terrain.Water;
        var (_, events, rng) = Turn(run);
        Assert.Empty(events.OfType<Stepped>());
        Assert.Equal(run.Rng, rng.State);
    }

    // ---- D: time ------------------------------------------------------------------------

    [Theory]
    [InlineData("Dryosaurus", 1.0, 0, 0)]
    [InlineData("Dryosaurus", 1.5, 0, 0)]
    [InlineData("Tyrannosaurus", 1.0, 0, 0)]
    [InlineData("Tyrannosaurus", 2.25, 3, 2)]
    [InlineData("Maiasaura", 1.5, 5, 3)]
    public void FoodAndWaterDrainAtTheMovesFactor(string species, double drain, int deepStomach, int drySpells)
    {
        var run = Turns.Board(species).With(Runs.Ranks((Trait.DeepStomach, deepStomach), (Trait.DrySpells, drySpells)), 0);
        var stats = Stats.Of(run);
        var (after, _, _) = Turn(run, drain: drain);
        Assert.Equal(80 - Tuning.FoodPerTurn * Builds.Of(stats.Build.Build).Hunger * (1 - 0.08 * deepStomach) * drain, after.You.Food, 10);
        Assert.Equal(80 - Tuning.WaterPerTurn * (1 - 0.10 * drySpells) * drain, after.You.Water, 10);
    }

    [Fact]
    public void ASneaksDrainReachesTheWorld()
    {
        var run = Turns.Board("Dryosaurus").Paced(Pace.Sneak);
        var after = Turns.Must(run, new PlayerAction.Move(Right(1))).After;
        Assert.Equal(80 - Tuning.FoodPerTurn * Tuning.SneakDrain, after.You.Food, 10);
        Assert.Equal(80 - Tuning.WaterPerTurn * Tuning.SneakDrain, after.You.Water, 10);
    }

    [Fact]
    public void FoodAndWaterNeverGoBelowZero()
    {
        var (after, _, _) = Turn(Fed(Turns.Board("Dryosaurus"), 0.5, 1), drain: 3);
        Assert.Equal(0, after.You.Food);
        Assert.Equal(0, after.You.Water);
    }

    [Theory]
    [InlineData(1, 80, 5, 0)]
    [InlineData(80, 1, 0, 5)]
    [InlineData(1, 1, 5, 5)]
    public void AnEmptyFoodOrWaterCostsHealth(double food, double water, int hunger, int thirst)
    {
        var (after, events, _) = Turn(Fed(Turns.Board("Dryosaurus"), food, water));
        Assert.Equal(new TimePassed(hunger, thirst, 0), events.OfType<TimePassed>().Single());
        Assert.Equal(1 - (hunger + thirst) / 100.0, after.You.Health, 10);
    }

    [Fact]
    public void FedAndWateredAboveHalfYouHeal()
    {
        var hurt = Turns.Board("Dryosaurus");
        hurt.You.Health = 0.5;
        var (after, events, _) = Turn(hurt);
        Assert.Equal(new TimePassed(0, 0, 2), events.OfType<TimePassed>().Single());
        Assert.Equal(0.52, after.You.Health, 10);

        // Held at full: what went into the bar.
        hurt.You.Health = 0.99;
        var (full, more, _) = Turn(hurt);
        Assert.Equal(1.0, full.You.Health);
        Assert.Equal(new TimePassed(0, 0, 1), more.OfType<TimePassed>().Single());

        var whole = Turns.Board("Dryosaurus");
        Assert.Equal(new TimePassed(0, 0, 0), Turn(whole).Events.OfType<TimePassed>().Single());
    }

    [Fact]
    public void AtHalfFoodOrWaterThereIsNoHealing()
    {
        foreach (var (food, water) in new[] { (50.0, 80.0), (80.0, 50.0) })
        {
            var run = Fed(Turns.Board("Dryosaurus"), food, water);
            run.You.Health = 0.5;
            var (after, events, _) = Turn(run, drain: 0);
            Assert.Equal(new TimePassed(0, 0, 0), events.OfType<TimePassed>().Single());
            Assert.Equal(0.5, after.You.Health);
        }
    }

    [Fact]
    public void ExactlyOneTimePassedEvenWhenNothingChanged()
    {
        var (_, events, _) = Turn(Still("Dryosaurus"), drain: 0);
        Assert.Equal(new TurnEvent[] { new TimePassed(0, 0, 0) }, events);
    }

    [Theory]
    [InlineData(0, 0, 26, 26, true)]
    [InlineData(0, 0, 25, 80, false)]
    [InlineData(0, 0, 80, 25, false)]
    [InlineData(10, 2, 80, 80, true)]
    [InlineData(60, 5, 80, 80, true)]
    public void YouGrowFedAndWateredAboveAQuarterAtYourRate(double growth, int metabolism, double food, double water, bool grows)
    {
        var run = Fed(Turns.Board("Dryosaurus", growth: growth), food, water).With(Runs.Ranks((Trait.FastMetabolism, metabolism)), 0);
        var (after, events, _) = Turn(run, drain: 0);
        if (grows)
        {
            var to = growth + Tuning.GrowthPerTurn * (1 + Tuning.FastMetabolismStep * metabolism);
            Assert.Equal(to, after.You.Growth, 10);
            Assert.Equal(to, after.You.Best, 10);
            var grew = events.OfType<Grew>().Single();
            Assert.Equal(growth, grew.From);
            Assert.Equal(to, grew.To, 10);
            Assert.IsType<TimePassed>(events[events.IndexOf(grew) - 1]);   // the Time passes beat hangs on it
        }
        else
        {
            Assert.Equal(growth, after.You.Growth);
            Assert.Empty(events.OfType<Grew>());
        }
    }

    [Fact]
    public void GrowthStopsAtFull()
    {
        var run = Turns.Board("Dryosaurus", growth: 99.5);
        run.ApexAt = 0;   // no apex in this test
        var (after, events, _) = Turn(run);
        Assert.Equal(100, after.You.Growth);
        Assert.Equal(new Grew(99.5, 100), events.OfType<Grew>().Single());

        var (still, more, _) = Turn(after);
        Assert.Equal(100, still.You.Growth);
        Assert.Empty(more.OfType<Grew>());
    }

    [Theory]
    [InlineData(0.5, 80, (int)EndReason.Hunger)]
    [InlineData(80, 0.5, (int)EndReason.Thirst)]
    [InlineData(0.5, 0.5, (int)EndReason.Hunger)]
    public void HungerOrThirstAtZeroHealthEndsTheRun(double food, double water, int why)
    {
        var reason = (EndReason)why;
        var run = Fed(Turns.Board("Dryosaurus", growth: 30), food, water);
        run.You.Health = 0.04;
        var (after, events, _) = Turn(run);
        Assert.IsType<TimePassed>(events[^2]);
        Assert.Equal(new RunEnded(false, reason, 0), events[^1]);
        Assert.Equal(0, after.You.Health);
        Assert.Equal(reason, after.Over!.Reason);
        Assert.Empty(events.OfType<Grew>());
    }

    [Fact]
    public void SecondWindSavesYouFromHungerToo()
    {
        var run = Fed(Turns.Board("Dryosaurus"), 0.5, 80);
        run.You.Health = 0.04;
        run.You.Wind = true;
        var (after, events, _) = Turn(run);
        var time = events.FindIndex(e => e is TimePassed);
        Assert.IsType<SecondWind>(events[time + 1]);
        Assert.Equal(Tuning.SecondWindHealth, after.You.Health);
        Assert.False(after.You.Wind);
        Assert.Null(after.Over);
    }

    // ---- D5 and F: the offers a line owes, and "unseen" -----------------------------------

    [Theory]
    [InlineData(24.0, Stages.Juvenile)]
    [InlineData(49.0, Stages.Subadult)]
    [InlineData(74.0, Stages.Adult)]
    public void EachOfTheFirstThreeLinesOwesAPick(double growth, int stage)
    {
        var (after, events, _) = Turn(Turns.Board("Dryosaurus", growth: growth));
        Assert.Equal(new StageReached(stage), events.OfType<StageReached>().Single());
        Assert.Equal(1, after.PicksOwed);
        Assert.NotNull(after.Offer);
        var offered = Assert.IsType<InstinctsOffered>(events[^1]);
        Assert.Equal(after.Offer, offered.Choices);
        Assert.NotSame(after.Offer, offered.Choices);
        Assert.True(events.IndexOf(events.OfType<Grew>().Single()) < events.IndexOf(events.OfType<StageReached>().Single()));
    }

    [Fact]
    public void ElderOwesAPickOnlyWithLateBloomer()
    {
        var plain = Turns.Board("Dryosaurus", growth: 99);
        var (after, events, _) = Turn(plain);
        Assert.Equal(new StageReached(Stages.Elder), events.OfType<StageReached>().Single());
        Assert.Equal(0, after.PicksOwed);
        Assert.Null(after.Offer);
        Assert.Empty(events.OfType<InstinctsOffered>());

        var bloomer = plain.With(Runs.Ranks((Trait.LateBloomer, 1)), 0);
        var (later, more, _) = Turn(bloomer);
        Assert.Equal(1, later.PicksOwed);
        Assert.NotNull(later.Offer);
        Assert.IsType<InstinctsOffered>(more[^1]);
    }

    [Fact]
    public void AdultCrossedUnbittenIsRemembered()
    {
        var (after, _, _) = Turn(Turns.Board("Dryosaurus", growth: 74));
        Assert.True(after.You.AdultUnbitten);
    }

    [Fact]
    public void AdultCrossedAfterABiteIsNot()
    {
        var run = Turns.Board("Dryosaurus", growth: 74);
        run.You.Bitten = true;
        Assert.False(Turn(run).After.You.AdultUnbitten);
    }

    [Fact]
    public void ABiteAfterAdultLeavesUnbittenSet()
    {
        var adult = Turn(Turns.Board("Dryosaurus", growth: 74)).After;
        Assert.True(adult.You.AdultUnbitten);
        var bitten = Rolling(adult.Put(PredatorType.Troodon, Right(1)), r => true, r => true);
        var (after, events, _) = Turn(bitten);
        Assert.Contains(events, e => e is Bit { Dodged: false });
        Assert.True(after.You.Bitten);
        Assert.True(after.You.AdultUnbitten);
    }

    [Fact]
    public void ARunThatBeganPastAdultNeverSetsIt()
    {
        var run = Turns.Board("Dryosaurus", growth: 80);
        for (var k = 0; k < 10; k++)
        {
            run = Turn(run).After;
            run.ApexAt = 0;
            Assert.False(run.You.AdultUnbitten);
        }
    }

    // ---- E: arrivals --------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void APredatorArrivesOnTheDangersBeat(int danger)
    {
        var every = Dangers.Of(danger).Every;
        var on = AtTurn(Still("Dryosaurus"), every - 1);
        on.Danger = danger;
        var (after, events, _) = Turn(on, drain: 0);
        var arrived = events.OfType<Arrived>().Single(a => after.Creatures.Single(c => c.Id == a.Creature).Kind == CreatureKind.Predator);
        Assert.Equal(every, after.Turn);
        var it = after.Creatures.Single(c => c.Id == arrived.Creature);
        Assert.Equal(arrived.At, it.At);
        Assert.True(Island.Grid.Distance(it.At, Middle) >= Tuning.PredatorDistance);

        // The turn after the beat (and the one before it) brings none.
        foreach (var turn in new[] { every, every - 2 })
        {
            var off = AtTurn(Still("Dryosaurus"), turn);
            off.Danger = danger;
            var (offAfter, _, _) = Turn(off, drain: 0);
            Assert.Equal(off.Creatures.Count(c => c.Kind == CreatureKind.Predator),
                offAfter.Creatures.Count(c => c.Kind == CreatureKind.Predator));
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(1, 4)]
    [InlineData(3, 1)]
    [InlineData(5, 0)]
    [InlineData(5, 3)]
    [InlineData(5, 4)]
    public void PredatorsArriveOnlyWhileFewerThanTheDangersMost(int danger, int stage)
    {
        var most = Dangers.Of(danger).Most(stage);
        var atDanger = Still("Dryosaurus");
        atDanger.Danger = danger;
        Assert.Equal(most, World.MostPredators(atDanger, stage));
        RunState Board(int predators)
        {
            var run = AtTurn(Still("Dryosaurus", growth: stage == 0 ? 0 : Tuning.StageLines[stage - 1]), Dangers.Of(danger).Every - 1);
            run.Danger = danger;
            run.ApexAt = 0;   // no apex comes in this test
            for (var k = 0; k < predators; k++) run = run.Put(PredatorType.Troodon, Far(k));
            // The apex is not counted.
            return run.Put(PredatorType.Tyrannosaurus, Far(10));
        }
        static int Predators(RunState run) => run.Creatures.Count(c => c.Kind == CreatureKind.Predator);

        var full = Board(most);
        Assert.Equal(Predators(full), Predators(Turn(full, drain: 0).After));
        var room = Board(most - 1);
        Assert.Equal(Predators(room) + 1, Predators(Turn(room, drain: 0).After));
    }

    [Theory]
    [InlineData("Dryosaurus")]
    [InlineData("Tenontosaurus")]
    [InlineData("Triceratops")]
    public void AnArrivalIsPickedFromThePoolOfYourSizeAndStage(string species)
    {
        var size = Builds.Of(SpeciesTable.All.Single(s => s.Name == species).Build).Size;
        for (var stage = Stages.Hatchling; stage <= Stages.Elder; stage++)
        {
            var pool = Predators.Pool(size, stage);
            var seen = new HashSet<PredatorType>();
            for (uint seed = 1; seed <= 200; seed++)
            {
                var run = AtTurn(Still(species, growth: stage == 0 ? 0 : Tuning.StageLines[stage - 1]), 6);
                run.ApexAt = 0;
                run.Rng = seed;
                var (after, events, _) = Turn(run, drain: 0);
                var arrived = events.OfType<Arrived>().Single();
                var type = after.Creatures.Single(c => c.Id == arrived.Creature).Type;
                // Nothing rolled before it: the type is the first roll's pick, the alert the second roll.
                Assert.Equal(pool[new Rng(seed).Next(pool.Count)], type);
                Assert.Equal(Roll(run, 1) < Tuning.ArrivesAlerted, arrived.Alerted);
                seen.Add(type);
            }
            Assert.Equal(pool.ToHashSet(), seen);
        }
    }

    [Fact]
    public void FortyPercentOfArrivalsComeAlerted()
    {
        var alerted = 0;
        for (uint seed = 1; seed <= 2000; seed++)
        {
            var run = AtTurn(Still("Dryosaurus"), 6);
            run.Rng = seed;
            var (after, events, _) = Turn(run, drain: 0);
            var arrived = events.OfType<Arrived>().Single();
            var it = after.Creatures.Single(c => c.Id == arrived.Creature);
            if (arrived.Alerted)
            {
                alerted++;
                Assert.Equal(Tuning.AlertHeard, it.Alert);
                Assert.Equal(Middle, it.Target);
            }
            else
            {
                Assert.Equal(0, it.Alert);
                Assert.Equal(Tuning.NoTarget, it.Target);
            }
        }
        Assert.InRange(alerted, 760, 840);   // 38% to 42% of 2,000
    }

    [Fact]
    public void ArrivalsAreNumberedByTheirSpecies()
    {
        // A hatchling's small pool holds only Troodons. The board's own first Troodon
        // was cleared away and one more put: the count holds both (H38), so the
        // arrival is the third, whoever still lives.
        var run = AtTurn(Still("Dryosaurus"), 6).Put(PredatorType.Troodon, Far(0));
        Assert.Equal(2, run.Arrived[PredatorType.Troodon]);
        var (after, events, _) = Turn(run, drain: 0);
        var arrived = after.Creatures.Single(c => c.Id == events.OfType<Arrived>().Single().Creature);
        Assert.Equal(PredatorType.Troodon, arrived.Type);
        Assert.Equal(3, arrived.Ordinal);
        Assert.Equal(3, after.Arrived[PredatorType.Troodon]);
    }

    [Fact]
    public void SmallGameArrivesUpToFive()
    {
        RunState WithGame(int count)
        {
            var run = AtTurn(Still("Dryosaurus"), Tuning.SmallGameEvery - 1);
            for (var k = 0; k < count; k++) run = run.PutSmallGame(Far(k));
            return run;
        }
        var (after, events, _) = Turn(WithGame(4), drain: 0);
        var arrived = events.OfType<Arrived>().Single();
        Assert.False(arrived.Alerted);
        var it = after.Creatures.Single(c => c.Id == arrived.Creature);
        Assert.Equal(CreatureKind.SmallGame, it.Kind);
        Assert.True(Island.Grid.Distance(it.At, Middle) >= Tuning.SmallGameDistance);
        Assert.Equal(Tuning.SmallGameMost, after.Creatures.Count(c => c.Kind == CreatureKind.SmallGame));

        Assert.Empty(Turn(WithGame(5), drain: 0).Events.OfType<Arrived>());
    }

    [Fact]
    public void PlantsGrowTwoAtATimeOutOfYourSight()
    {
        for (uint seed = 1; seed <= 100; seed++)
        {
            var run = AtTurn(Still("Dryosaurus"), Tuning.PlantsEvery - 1);
            run.Rng = seed;
            var sight = Stats.Of(run).Sight;
            var (after, events, _) = Turn(run, drain: 0);
            var grew = events.OfType<PlantsGrew>().Single();
            Assert.Equal(Tuning.PlantsAtATime, grew.Cells.Count);
            Assert.Equal(grew.Cells.Count, grew.Cells.Distinct().Count());
            foreach (var cell in grew.Cells)
            {
                Assert.True(after.Plants[cell]);
                Assert.True(Island.Grid.Distance(cell, Middle) > sight);
                Assert.True(Island.IsLand(after, cell));
            }
            Assert.Equal(Tuning.PlantsAtATime, after.Plants.Count(p => p));
        }
    }

    [Fact]
    public void TheApexComesOnceAtFullGrowth()
    {
        var run = AtTurn(Still("Dryosaurus", growth: 100), 3);
        var (after, events, _) = Turn(run, drain: 0);
        var came = events.OfType<ApexArrived>().Single();
        Assert.DoesNotContain(events, e => e is Arrived a && a.Creature == came.Creature);   // ApexArrived alone
        var apex = after.Creatures.Single(c => c.Id == came.Creature);
        Assert.Equal(Tuning.Apex, apex.Type);
        Assert.True(apex.Relentless);
        Assert.Equal(came.At, apex.At);
        Assert.Equal(1, apex.Ordinal);
        Assert.Equal(Middle, apex.Target);
        Assert.Equal((int)Tuning.Round(Predators.Of(Tuning.Apex).Health * Island.Threat(run)), apex.Health);
        Assert.True(Island.Grid.Distance(apex.At, Middle) >= Tuning.PredatorDistance);
        Assert.Equal(4, after.ApexAt);
        Assert.Empty(events.OfType<ApexWaits>());   // not on the turn it came

        var (later, more, _) = Turn(after, drain: 0);
        Assert.Empty(more.OfType<ApexArrived>());
        Assert.Single(later.Creatures, c => c.Type == Tuning.Apex);
        Assert.Equal(new ApexWaits(World.OutlastTurns(later) - 1), more.OfType<ApexWaits>().Single());
    }

    [Fact]
    public void TheApexComesOnlyAtFullGrowth()
    {
        var (_, events, _) = Turn(Turns.Board("Dryosaurus", growth: 98));
        Assert.Empty(events.OfType<ApexArrived>());
    }

    [Theory]
    [InlineData(1, false, 15)]
    [InlineData(1, true, 10)]
    [InlineData(3, false, 17)]
    [InlineData(5, false, 21)]
    [InlineData(5, true, 16)]
    public void TheApexIsOutlastedAfterTheDangersTurns(int danger, bool patience, int turns)
    {
        RunState Waiting(int waited)
        {
            var run = AtTurn(Corridor(Still("Dryosaurus", growth: 100)), waited - 1).Put(PredatorType.Tyrannosaurus, Right(5));
            run.Danger = danger;
            run.ApexAt = 0;
            return patience ? run.With(Runs.Ranks((Trait.EldersPatience, 1)), 0) : run;
        }
        Assert.Equal(turns, World.OutlastTurns(Waiting(1)));

        var (going, events, _) = Turn(Waiting(turns - 1), drain: 0);
        Assert.Null(going.Over);
        Assert.Equal(new ApexWaits(1), events.OfType<ApexWaits>().Single());

        var (won, ended, _) = Turn(Waiting(turns), drain: 0);
        Assert.Equal(new RunEnded(true, EndReason.Outlasted, 0), ended[^1]);
        Assert.Empty(ended.OfType<ApexWaits>());
        Assert.True(won.Over!.Won);
        Assert.Equal(EndReason.Outlasted, won.Over.Reason);
    }

    // ---- The end ------------------------------------------------------------------------

    [Fact]
    public void EndFillsTheRunsOverAndAddsRunEnded()
    {
        var run = Turns.Board("Dryosaurus", growth: 55);
        run.You.Start = 0;
        run.Kills = 1;
        var events = new List<TurnEvent>();
        World.End(run, won: false, EndReason.Left, by: null, events);
        Assert.Equal(new TurnEvent[] { new RunEnded(false, EndReason.Left, 0) }, events);
        Assert.Equal(EndReason.Left, run.Over!.Reason);
        Assert.Equal(Stages.Subadult, run.Over.Stage);
        Assert.Equal(45, run.Over.Total);
        Assert.False(run.Over.Settled);
    }

    /// <summary>Two <see cref="Stepped"/> are equal when their paths hold the same cells (the record compares the lists by reference).</summary>
    private sealed class SteppedEq : IEqualityComparer<TurnEvent>
    {
        public bool Equals(TurnEvent? x, TurnEvent? y) => x is not null && y is not null && Turns.Describe(x) == Turns.Describe(y);
        public int GetHashCode(TurnEvent e) => Turns.Describe(e).GetHashCode();
    }
}
