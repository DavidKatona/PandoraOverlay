using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What every test of your move calls: <see cref="TurnRules.Apply"/> and
/// <see cref="TurnRules.YourMove"/> with the run given held unchanged (A20) in
/// every call, and a hand-built board. The moves' tests look at your move through
/// <see cref="Move"/>, apart from the world's turn after it, so they hold whatever
/// the world does (task H04); only the turn counter is asked of the world.
/// </summary>
internal static class Turns
{
    /// <summary>The middle of the board: row 5, column 5. Its row has five cells either side.</summary>
    public const int Middle = 5 * Tuning.Columns + 5;

    /// <summary>The cell <paramref name="steps"/> to the right of <see cref="Middle"/> in its row (negative: to the left); its distance is the steps.</summary>
    public static int Right(int steps) => Middle + steps;

    /// <summary>
    /// A run of <paramref name="species"/> (from <see cref="Runs.Fresh(string, uint, int)"/>
    /// and <see cref="Runs.Clear"/>) on a board of <paramref name="ground"/> only: no
    /// creature, no plant, no carcass, you in the middle at <paramref name="growth"/>,
    /// no offer open.
    /// </summary>
    public static RunState Board(string species, Terrain ground = Terrain.Grass, double growth = 0)
    {
        var run = Runs.Fresh(species, 1).Clear().Paved(ground).At(Middle);
        Array.Fill(run.Plants, false);
        Array.Fill(run.Meat, 0);
        run.Offer = null;
        run.PicksOwed = 0;
        run.You.Growth = growth;
        run.You.Start = growth;
        run.You.Best = growth;
        return run;
    }

    /// <summary>A copy of <paramref name="run"/> that has picked <paramref name="instincts"/>.</summary>
    public static RunState Picked(this RunState run, params Instinct[] instincts)
    {
        var copy = run.Clone();
        copy.Instincts.AddRange(instincts);
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> at <paramref name="pace"/>.</summary>
    public static RunState Paced(this RunState run, Pace pace)
    {
        var copy = run.Clone();
        copy.Pace = pace;
        return copy;
    }

    /// <summary>A copy of <paramref name="run"/> with cell <paramref name="cell"/> made <paramref name="ground"/>.</summary>
    public static RunState Ground(this RunState run, int cell, Terrain ground)
    {
        var copy = run.Clone();
        copy.Tiles[cell] = ground;
        return copy;
    }

    /// <summary><see cref="TurnRules.Apply"/>, holding A20: the run given is unchanged, and an answer's Before is it and its After a new one.</summary>
    public static TurnResult? Apply(RunState run, PlayerAction action)
    {
        var before = Runs.Fingerprint(run);
        var result = TurnRules.Apply(run, action);
        Assert.Equal(before, Runs.Fingerprint(run));
        if (result is not null)
        {
            Assert.Same(run, result.Before);
            Assert.NotSame(run, result.After);
        }
        return result;
    }

    /// <summary><see cref="Apply"/> that must be allowed.</summary>
    public static TurnResult Must(RunState run, PlayerAction action) =>
        Apply(run, action) ?? throw new Xunit.Sdk.XunitException($"{action} was refused.");

    /// <summary><see cref="Apply"/> that must be refused.</summary>
    public static void Refused(RunState run, PlayerAction action) => Assert.Null(Apply(run, action));

    /// <summary>
    /// Your move alone, on a copy of <paramref name="run"/> (<see cref="TurnRules.YourMove"/>
    /// with the run's own generator): the copy after the move, the move's events, what
    /// it left for the world, and the generator after it. It must be allowed; the run
    /// given is unchanged.
    /// </summary>
    public static (RunState After, List<TurnEvent> Events, TurnRules.Done Done, Rng Rng) Move(RunState run, PlayerAction action)
    {
        var before = Runs.Fingerprint(run);
        var copy = run.Clone();
        var events = new List<TurnEvent>();
        var rng = new Rng(copy.Rng);
        var done = TurnRules.YourMove(copy, Stats.Of(copy), rng, action, events);
        Assert.Equal(before, Runs.Fingerprint(run));
        if (done is null) throw new Xunit.Sdk.XunitException($"{action} was refused.");
        return (copy, events, done.Value, rng);
    }

    /// <summary>Your move alone, refused: null, no event, and the copy it was tried on untouched (as the run given).</summary>
    public static void MoveRefused(RunState run, PlayerAction action)
    {
        var before = Runs.Fingerprint(run);
        var copy = run.Clone();
        var events = new List<TurnEvent>();
        Assert.Null(TurnRules.YourMove(copy, Stats.Of(copy), new Rng(copy.Rng), action, events));
        Assert.Empty(events);
        Assert.Equal(before, Runs.Fingerprint(copy));
        Assert.Equal(before, Runs.Fingerprint(run));
        // And Apply refuses it too: one rule, one answer.
        Refused(run, action);
    }

    /// <summary>An event as one text, its lists spelled out, so two runs' events can be compared by value.</summary>
    public static string Describe(TurnEvent e) => e switch
    {
        Walked w => $"Walked({w.Pace},{w.From},[{string.Join(",", w.Path)}],{w.Waded},{w.Noise})",
        Stepped s => $"Stepped({s.Creature},{s.From},[{string.Join(",", s.Path)}],{s.Gait})",
        PlantsGrew p => $"PlantsGrew([{string.Join(",", p.Cells)}])",
        InstinctsOffered o => $"InstinctsOffered([{string.Join(",", o.Choices)}])",
        _ => e.ToString(),
    };
}

/// <summary>
/// <see cref="TurnRules.Apply"/>: what is allowed when, what passes a turn, the order
/// of a turn's halves, the generator written back, and the same run twice.
/// </summary>
public class TurnRulesTests
{
    private static readonly PlayerAction[] Everything =
    {
        new PlayerAction.Move(Turns.Right(1)), new PlayerAction.Eat(), new PlayerAction.Drink(), new PlayerAction.Rest(),
        new PlayerAction.Attack(1), new PlayerAction.SetPace(Pace.Sneak), new PlayerAction.Pick(0),
        new PlayerAction.Redraw(), new PlayerAction.Leave(),
    };

    // ---- A run that is over ------------------------------------------------------------

    [Fact]
    public void ARunThatIsOverAllowsNothing()
    {
        var run = Turns.Board("Herrerasaurus").Put(PredatorType.Troodon, Turns.Right(1));
        run.Plants[Turns.Middle] = true;
        run.Over = new RunOver { Won = false, Reason = EndReason.Caught };
        foreach (var action in Everything)
            Turns.Refused(run, action);
    }

    [Fact]
    public void ARunLeftIsOverAfterwards()
    {
        var left = Turns.Must(Turns.Board("Dryosaurus"), new PlayerAction.Leave()).After;
        foreach (var action in Everything)
            Turns.Refused(left, action);
    }

    // ---- Leave (H20) -----------------------------------------------------------------

    [Fact]
    public void LeavingEndsTheRunAsLeftWithNoWorldsTurn()
    {
        var run = Turns.Board("Dryosaurus", growth: 30);
        var result = Turns.Must(run, new PlayerAction.Leave());
        var ended = Assert.Single(result.Events);
        Assert.Equal(new RunEnded(false, EndReason.Left, 0), ended);
        var over = Assert.IsType<RunOver>(result.After.Over);
        Assert.False(over.Won);
        Assert.Equal(EndReason.Left, over.Reason);
        Assert.Equal(Stages.Juvenile, over.Stage);
        Assert.Equal(run.Turn, result.After.Turn);
        Assert.Equal(run.Rng, result.After.Rng);   // nothing was rolled
    }

    [Fact]
    public void ARunCanBeLeftAtTurnZero()
    {
        var run = Runs.Fresh("Herrerasaurus", 7);
        Assert.Equal(0, run.Turn);
        Assert.NotNull(Turns.Must(run, new PlayerAction.Leave()).After.Over);
    }

    // ---- The pace --------------------------------------------------------------------

    [Theory]
    [InlineData(Pace.Walk)]
    [InlineData(Pace.Sneak)]
    [InlineData(Pace.Sprint)]
    public void SettingThePaceTakesNoTurnAndSaysNothing(object pace)
    {
        var run = Turns.Board("Dryosaurus");
        var result = Turns.Must(run, new PlayerAction.SetPace((Pace)pace));
        Assert.Empty(result.Events);
        Assert.Equal((Pace)pace, result.After.Pace);
        Assert.Equal(run.Turn, result.After.Turn);
        Assert.Equal(run.Rng, result.After.Rng);
        Assert.Equal(run.You.LastMove, result.After.You.LastMove);   // not a move
    }

    [Fact]
    public void TheNextMoveIsAtThePaceSet()
    {
        var run = Turns.Board("Dryosaurus");
        var sneaking = Turns.Must(run, new PlayerAction.SetPace(Pace.Sneak)).After;
        var walked = Assert.IsType<Walked>(Turns.Must(sneaking, new PlayerAction.Move(Turns.Right(1))).Events[0]);
        Assert.Equal(Pace.Sneak, walked.Pace);
        Assert.Equal(Pace.Sneak, Turns.Must(sneaking, new PlayerAction.Rest()).After.Pace);   // it stays as chosen
    }

    // ---- An offer open (H14) ---------------------------------------------------------

    /// <summary>A run that starts with an offer open (Born ready) and, with Strong start 1 (10%), no second pick owed.</summary>
    private static RunState Offered(int strongStart = 0)
    {
        var ranks = strongStart > 0
            ? Runs.Ranks((Trait.BornReady, 1), (Trait.StrongStart, strongStart))
            : Runs.Ranks((Trait.BornReady, 1));
        var run = Runs.Fresh("Dryosaurus", 3, ranks, stars: 0);
        Assert.NotNull(run.Offer);
        return run;
    }

    [Fact]
    public void WhileAnOfferIsOpenNoMoveAndNoPaceIsAllowed()
    {
        var run = Offered();
        var stats = Stats.Of(run);
        foreach (var to in Moves.Targets(run, stats))
            Turns.Refused(run, new PlayerAction.Move(to));
        Turns.Refused(run, new PlayerAction.Eat());
        Turns.Refused(run, new PlayerAction.Drink());
        Turns.Refused(run, new PlayerAction.Rest());
        foreach (var creature in run.Creatures)
            Turns.Refused(run, new PlayerAction.Attack(creature.Id));
        Turns.Refused(run, new PlayerAction.SetPace(Pace.Sprint));
    }

    [Fact]
    public void APickIsTakenWithNoTurnAndNoEventWhenNoOtherIsOwed()
    {
        var run = Offered();
        Assert.Equal(1, run.PicksOwed);
        var result = Turns.Must(run, new PlayerAction.Pick(1));
        Assert.Empty(result.Events);
        Assert.Equal(new[] { run.Offer![1] }, result.After.Instincts);
        Assert.Null(result.After.Offer);
        Assert.Equal(run.Turn, result.After.Turn);
        // And with the offer answered, a move is allowed again.
        Assert.NotNull(Turns.Apply(result.After, new PlayerAction.Rest()));
    }

    [Fact]
    public void APickThatOpensTheNextOfferSaysSo()
    {
        var run = Offered(strongStart: 3);   // 30%: Juvenile's pick owed, and Born ready's
        Assert.Equal(2, run.PicksOwed);
        var result = Turns.Must(run, new PlayerAction.Pick(0));
        var offered = Assert.IsType<InstinctsOffered>(Assert.Single(result.Events));
        Assert.NotNull(result.After.Offer);
        Assert.Equal(result.After.Offer, offered.Choices);
        Assert.NotSame(result.After.Offer, offered.Choices);   // the event never shares the state's list
        Assert.Equal(run.Turn, result.After.Turn);
    }

    [Fact]
    public void ARedrawTakesNoTurnAndSaysNothing()
    {
        var run = Offered();
        var result = Turns.Must(run, new PlayerAction.Redraw());
        Assert.Empty(result.Events);
        Assert.NotNull(result.After.Offer);
        Assert.Equal(run.Turn, result.After.Turn);
    }

    [Fact]
    public void APickOutsideTheOfferIsRefused()
    {
        var run = Offered();
        Turns.Refused(run, new PlayerAction.Pick(-1));
        Turns.Refused(run, new PlayerAction.Pick(run.Offer!.Count));
    }

    [Fact]
    public void WhileAnOfferIsOpenTheRunCanBeLeft()
    {
        var result = Turns.Must(Offered(), new PlayerAction.Leave());
        Assert.Equal(new RunEnded(false, EndReason.Left, 0), Assert.Single(result.Events));
    }

    [Fact]
    public void WithNoOfferOpenAPickOrARedrawIsRefused()
    {
        var run = Turns.Board("Dryosaurus");
        Turns.Refused(run, new PlayerAction.Pick(0));
        Turns.Refused(run, new PlayerAction.Redraw());
    }

    // ---- A turn ----------------------------------------------------------------------

    [Fact]
    public void EveryMoveTakesOneTurnAndBeginsTheTurnsEvents()
    {
        var run = Turns.Board("Herrerasaurus").Ground(Turns.Right(-1), Terrain.Water)
            .Put(PredatorType.Allosaurus, Turns.Right(1));
        run.Meat[Turns.Middle] = 1;
        run.You.Food = 50;
        run.You.Water = 50;
        var cases = new (PlayerAction Action, Type First)[]
        {
            (new PlayerAction.Move(Island.Grid.Neighbours(Turns.Middle)[0]), typeof(Walked)),
            (new PlayerAction.Eat(), typeof(Ate)),
            (new PlayerAction.Drink(), typeof(Drank)),
            (new PlayerAction.Rest(), typeof(Rested)),
            (new PlayerAction.Attack(run.Creatures[0].Id), typeof(Struck)),
        };
        foreach (var (action, first) in cases)
        {
            var result = Turns.Must(run, action);
            Assert.Equal(run.Turn + 1, result.After.Turn);
            Assert.IsType(first, result.Events[0]);
        }
    }

    [Theory]
    [InlineData(Pace.Walk, MoveKind.Walk)]
    [InlineData(Pace.Sneak, MoveKind.Sneak)]
    [InlineData(Pace.Sprint, MoveKind.Sprint)]
    public void AMoveIsRememberedByItsPace(object pace, object kind)
    {
        var run = Turns.Board("Dryosaurus").Paced((Pace)pace);
        Assert.Equal((MoveKind)kind, Turns.Move(run, new PlayerAction.Move(Turns.Right(1))).After.You.LastMove);
    }

    [Fact]
    public void EveryOtherMoveIsRememberedByItsKind()
    {
        var run = Turns.Board("Herrerasaurus").Ground(Turns.Right(-1), Terrain.Water)
            .Put(PredatorType.Allosaurus, Turns.Right(1));
        run.Meat[Turns.Middle] = 1;
        run.You.Food = 50;
        run.You.Water = 50;
        Assert.Equal(MoveKind.Eat, Turns.Move(run, new PlayerAction.Eat()).After.You.LastMove);
        Assert.Equal(MoveKind.Drink, Turns.Move(run, new PlayerAction.Drink()).After.You.LastMove);
        Assert.Equal(MoveKind.Rest, Turns.Move(run, new PlayerAction.Rest()).After.You.LastMove);
        Assert.Equal(MoveKind.Attack, Turns.Move(run, new PlayerAction.Attack(run.Creatures[0].Id)).After.You.LastMove);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]   // Keen eyes
    public void AfterAMoveWhatIsInSightIsSeen(bool keenEyes, int sight)
    {
        var run = Turns.Board("Dryosaurus");
        if (keenEyes) run = run.Picked(Instinct.Eyes);
        Array.Fill(run.Seen, false);
        var to = Turns.Right(1);
        var after = Turns.Must(run, new PlayerAction.Move(to)).After;
        Assert.Equal(sight, Stats.Of(after).Sight);
        foreach (var cell in Island.Grid.Within(to, sight))
            Assert.True(after.Seen[cell]);
    }

    [Fact]
    public void AHitsRollIsWrittenBackIntoTheRun()
    {
        var run = Turns.Board("Carnotaurus", growth: 75).Put(PredatorType.Allosaurus, Turns.Right(1));
        var id = run.Creatures[0].Id;
        // The move alone takes exactly one roll, the swing.
        var expected = new Rng(run.Rng);
        expected.NextDouble();
        Assert.Equal(expected.State, Turns.Move(run, new PlayerAction.Attack(id)).Rng.State);
        // And the turn writes its generator back: it went on from there.
        Assert.NotEqual(run.Rng, Turns.Must(run, new PlayerAction.Attack(id)).After.Rng);
    }

    [Fact]
    public void ARefusedMoveIsNull()
    {
        var run = Turns.Board("Dryosaurus");
        Turns.Refused(run, new PlayerAction.Move(Turns.Middle));        // your own cell
        Turns.Refused(run, new PlayerAction.Move(Turns.Right(3)));      // too far for a walk
        Turns.Refused(run, new PlayerAction.Move(-1));                  // not a cell
        Turns.Refused(run, new PlayerAction.Move(Tuning.Cells));
        Turns.Refused(run, new PlayerAction.Attack(99));                // no such creature
    }

    // ---- The same run twice ------------------------------------------------------------

    [Fact]
    public void TheSameSeedAndTheSameMovesGiveTheSameStatesAndEvents()
    {
        foreach (var species in new[] { "Dryosaurus", "Stegosaurus", "Dilophosaurus", "Deinosuchus", "Pteranodon" })
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                var (statesA, eventsA) = Play(species, seed);
                var (statesB, eventsB) = Play(species, seed);
                Assert.Equal(statesA, statesB);
                Assert.Equal(eventsA, eventsB);
                Assert.True(statesA.Count > 1);
            }
        }
    }

    /// <summary>
    /// Up to 40 turns of <paramref name="species"/> on <paramref name="seed"/>'s island,
    /// each action picked among the legal ones by a generator of its own (picks are
    /// taken first, the pace changed now and then): every state's fingerprint and
    /// every event, as text.
    /// </summary>
    private static (List<string> States, List<string> Events) Play(string species, uint seed)
    {
        var run = Runs.Fresh(species, seed);
        var chooser = new Rng(seed * 7919u);
        var states = new List<string> { Runs.Fingerprint(run) };
        var events = new List<string>();
        for (var turn = 0; turn < 40 && run.Over is null; turn++)
        {
            var stats = Stats.Of(run);
            var actions = new List<PlayerAction>();
            if (run.Offer is not null)
            {
                actions.Add(new PlayerAction.Pick(chooser.Next(run.Offer.Count)));
            }
            else
            {
                actions.AddRange(Moves.Targets(run, stats).Select(to => (PlayerAction)new PlayerAction.Move(to)));
                actions.AddRange(Moves.Attackable(run, stats).Select(c => (PlayerAction)new PlayerAction.Attack(c.Id)));
                if (Moves.FoodHere(run, stats) is not null && run.You.Food < Tuning.FoodMost) actions.Add(new PlayerAction.Eat());
                if (Moves.WaterNear(run) && run.You.Water < Tuning.WaterMost) actions.Add(new PlayerAction.Drink());
                actions.Add(new PlayerAction.Rest());
                actions.Add(new PlayerAction.SetPace((Pace)chooser.Next(3)));
            }
            var result = Turns.Must(run, actions[chooser.Next(actions.Count)]);
            run = result.After;
            states.Add(Runs.Fingerprint(run));
            events.AddRange(result.Events.Select(Turns.Describe));
            events.Add("--");
        }
        return (states, events);
    }
}
