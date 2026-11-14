using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Invariants over whole turns (<see cref="TurnRules.Apply"/> with the real world's
/// turn): 200 seeded runs, every species in turn, played by random legal moves to
/// their end. After every turn: no two creatures on a cell, none on yours, none on
/// rock; health within 0 and 1 while the run goes on; food and water within 0 and
/// 100; growth never falls; every predator that acted has exactly one
/// <see cref="Stepped"/> or <see cref="Stayed"/>; the events' paths, replayed over
/// the state before, give the cells of the state after; the events come in the
/// turn's fixed order (H1: you, each predator by id, small game, then time and
/// arrivals), each predator's in the order things happen. And the same seed and
/// moves twice give equal states and events.
/// </summary>
public class TurnInvariantsTests
{
    /// <summary>More turns than any run lasts: a run still going after them fails the test.</summary>
    private const int MostTurns = 2000;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public TurnInvariantsTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void TwoHundredRunsKeepEveryInvariantToTheirEnd()
    {
        var ended = new Dictionary<EndReason, int>();
        var turns = 0;
        var apexes = 0;
        for (uint seed = 1; seed <= 200; seed++)
        {
            var species = SpeciesTable.All[(int)(seed % (uint)SpeciesTable.All.Count)].Name;
            var danger = 1 + (int)(seed % 5);
            var run = Play(species, seed, danger, check: true).Last;
            Assert.True(run.Over is not null, $"{species} on seed {seed} did not end in {MostTurns} turns");
            ended[run.Over!.Reason] = ended.GetValueOrDefault(run.Over.Reason) + 1;
            turns += run.Turn;
            if (run.ApexAt != Tuning.ApexNotYet) apexes++;
        }
        _output.WriteLine($"{turns} turns in all; the apex came in {apexes} runs; ends: "
            + string.Join(", ", ended.OrderBy(e => e.Key).Select(e => $"{e.Key} {e.Value}")));
        // The random player is mostly caught; it never leaves.
        Assert.Contains(EndReason.Caught, ended.Keys);
        Assert.DoesNotContain(EndReason.Left, ended.Keys);
    }

    [Fact]
    public void TwoHundredRunsNearElderKeepThemThroughTheApex()
    {
        // A random player is caught long before Elder: these begin at 96% with
        // Second wind, so the apex's arrival, its wait, outlasting it and the wind
        // are held to the same invariants.
        var ended = new Dictionary<EndReason, int>();
        var apexes = 0;
        var winds = 0;
        for (uint seed = 1; seed <= 200; seed++)
        {
            var species = SpeciesTable.All[(int)(seed % (uint)SpeciesTable.All.Count)].Name;
            var (_, events, run) = Play(species, seed, 1 + (int)(seed % 5), check: true, growth: 96, wind: true);
            Assert.True(run.Over is not null, $"{species} on seed {seed} did not end in {MostTurns} turns");
            ended[run.Over!.Reason] = ended.GetValueOrDefault(run.Over.Reason) + 1;
            if (run.ApexAt != Tuning.ApexNotYet) apexes++;
            winds += events.Count(e => e.StartsWith("SecondWind", StringComparison.Ordinal));
        }
        _output.WriteLine($"the apex came in {apexes} runs; Second wind {winds} times; ends: "
            + string.Join(", ", ended.OrderBy(e => e.Key).Select(e => $"{e.Key} {e.Value}")));
        Assert.True(apexes > 100);
        Assert.Contains(EndReason.Outlasted, ended.Keys);
        Assert.True(winds > 0);
    }

    [Fact]
    public void TheSameSeedAndMovesTwiceGiveTheSameRun()
    {
        for (uint seed = 1; seed <= 40; seed++)
        {
            var species = SpeciesTable.All[(int)(seed % (uint)SpeciesTable.All.Count)].Name;
            var a = Play(species, seed, 1 + (int)(seed % 5), check: false);
            var b = Play(species, seed, 1 + (int)(seed % 5), check: false);
            Assert.Equal(a.States, b.States);
            Assert.Equal(a.Events, b.Events);
        }
    }

    /// <summary>
    /// A run of <paramref name="species"/> on <paramref name="seed"/>'s island at
    /// <paramref name="danger"/>, each action picked among the legal ones (never Leave)
    /// by a generator of its own, to its end; every state's fingerprint and every
    /// event as text. With <paramref name="check"/>, every turn is held to the invariants.
    /// </summary>
    private static (List<string> States, List<string> Events, RunState Last) Play(string species, uint seed, int danger, bool check,
        double growth = 0, bool wind = false)
    {
        var run = Runs.Fresh(species, seed, danger);
        if (growth > 0 || wind)
        {
            // Begun further on: what a lineage's start would give, set by hand.
            run = run.Clone();
            run.You.Growth = run.You.Start = run.You.Best = growth;
            run.You.Wind = wind;
        }
        var chooser = new Rng(seed * 7919u + 13);
        var states = new List<string> { Runs.Fingerprint(run) };
        var events = new List<string>();
        for (var turn = 0; turn < MostTurns && run.Over is null; turn++)
        {
            var result = Turns.Must(run, Choose(run, chooser));
            if (check) Hold(result);
            run = result.After;
            states.Add(Runs.Fingerprint(run));
            events.AddRange(result.Events.Select(Turns.Describe));
            events.Add("--");
        }
        return (states, events, run);
    }

    /// <summary>A legal action, at random: an offer's pick while one is open; else a move, a hit, eating, drinking, resting, now and then a pace.</summary>
    private static PlayerAction Choose(RunState run, Rng chooser)
    {
        if (run.Offer is not null) return new PlayerAction.Pick(chooser.Next(run.Offer.Count));
        var stats = Stats.Of(run);
        var actions = new List<PlayerAction>();
        actions.AddRange(Moves.Targets(run, stats).Select(to => (PlayerAction)new PlayerAction.Move(to)));
        actions.AddRange(Moves.Attackable(run, stats).Select(c => (PlayerAction)new PlayerAction.Attack(c.Id)));
        // A player eats and drinks when it can more often than at random: the runs then
        // live long enough to grow, meet the apex and its wait.
        if (Moves.FoodHere(run, stats) is not null && run.You.Food < Tuning.FoodMost)
            for (var k = 0; k < 4; k++) actions.Add(new PlayerAction.Eat());
        if (Moves.WaterNear(run) && run.You.Water < Tuning.WaterMost)
            for (var k = 0; k < 4; k++) actions.Add(new PlayerAction.Drink());
        actions.Add(new PlayerAction.Rest());
        if (chooser.Chance(0.1)) return new PlayerAction.SetPace((Pace)chooser.Next(3));
        return actions[chooser.Next(actions.Count)];
    }

    // ---- The invariants of one turn -------------------------------------------------------

    private static void Hold(TurnResult turn)
    {
        var before = turn.Before;
        var after = turn.After;
        var events = turn.Events;

        // The board.
        var cells = after.Creatures.Select(c => c.At).ToList();
        Assert.Equal(cells.Count, cells.Distinct().Count());
        Assert.DoesNotContain(after.You.At, cells);
        Assert.All(after.Creatures, c => Assert.NotEqual(Terrain.Rock, after.Tiles[c.At]));

        // Your numbers.
        if (after.Over is null) Assert.InRange(after.You.Health, 0, 1);
        Assert.InRange(after.You.Food, 0, Tuning.FoodMost);
        Assert.InRange(after.You.Water, 0, Tuning.WaterMost);
        Assert.True(after.You.Growth >= before.You.Growth);
        Assert.True(after.You.Best >= after.You.Growth);

        Replay(turn);
        Order(turn);

        // Every predator that acted did one thing; those that did not act come after
        // the one the run ended in (the world stopped), or the world's turn never came.
        var acted = events.Select(e => e switch { Stepped s => s.Creature, Stayed s => s.Creature, _ => 0 })
            .Where(id => before.Creatures.Any(c => c.Id == id && c.Kind == CreatureKind.Predator)).ToList();
        Assert.Equal(acted.Count, acted.Distinct().Count());
        var reachedTime = events.Any(e => e is TimePassed);
        var struckDown = events.OfType<Struck>().Where(s => s.Defeated).Select(s => s.Creature).ToHashSet();
        var worldCame = events.Any(e => e is Noticed or Stepped or Stayed or Bit or Hurt or TimePassed);
        foreach (var c in before.Creatures.Where(c => c.Kind == CreatureKind.Predator && !struckDown.Contains(c.Id)))
        {
            if (reachedTime) Assert.Contains(c.Id, acted);
            else if (acted.Count > 0 && c.Id < acted.Max()) Assert.Contains(c.Id, acted);
        }
        if (!worldCame) Assert.Empty(acted);

        // At most one bite a predator a turn, and only from one that acted.
        var biters = events.OfType<Bit>().Select(b => b.Creature).ToList();
        Assert.Equal(biters.Count, biters.Distinct().Count());
        Assert.All(biters, id => Assert.Contains(id, acted));

        // One end at most, and it is the last event.
        var ends = events.Count(e => e is RunEnded);
        Assert.True(ends <= 1);
        if (ends == 1) Assert.IsType<RunEnded>(events[^1]);
        Assert.Equal(ends == 1, after.Over is not null);
    }

    /// <summary>Replays the events' paths, arrivals and defeats over the state before: the cells must be the state after's.</summary>
    private static void Replay(TurnResult turn)
    {
        var you = turn.Before.You.At;
        var at = turn.Before.Creatures.ToDictionary(c => c.Id, c => c.At);
        foreach (var e in turn.Events)
        {
            switch (e)
            {
                case Walked w:
                    Assert.Equal(you, w.From);
                    if (w.Path.Count > 0) you = w.Path[^1];
                    break;
                case Stepped s:
                    Assert.Equal(at[s.Creature], s.From);
                    Assert.NotEmpty(s.Path);
                    Assert.True(s.Path.Count <= Tuning.FastSteps);
                    var from = s.From;
                    foreach (var cell in s.Path)
                    {
                        Assert.Equal(1, Island.Grid.Distance(from, cell));
                        from = cell;
                    }
                    at[s.Creature] = s.Path[^1];
                    break;
                case Struck { Defeated: true } s:
                    Assert.True(at.Remove(s.Creature));
                    break;
                case Hurt { Defeated: true } h:
                    Assert.Equal(at[h.Creature], h.At);
                    Assert.True(at.Remove(h.Creature));
                    break;
                case Arrived a:
                    at.Add(a.Creature, a.At);
                    break;
                case ApexArrived x:
                    at.Add(x.Creature, x.At);
                    break;
            }
        }
        Assert.Equal(turn.After.You.At, you);
        Assert.Equal(turn.After.Creatures.OrderBy(c => c.Id).Select(c => (c.Id, c.At)), at.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)));
    }

    /// <summary>
    /// The turn's order (H1): your move; each predator in id order, its events in a
    /// block in the order things happen; small game in id order; then time and the
    /// arrivals in the design's order; the end last.
    /// </summary>
    private static void Order(TurnResult turn)
    {
        var kinds = turn.Before.Creatures.ToDictionary(c => c.Id, c => c.Kind);
        var phase = 0;          // 0 you, 1 predators, 2 small game, 3 the world
        var lastPredator = 0;
        var lastRank = -1;      // within the current predator's block
        var lastGame = 0;
        var worldRank = -1;
        var lastStage = 0;
        foreach (var e in turn.Events)
        {
            if (e is RunEnded) continue;   // held as last elsewhere
            var (p, id, rank) = Classify(e, kinds, phase);
            Assert.True(p >= phase, $"{Turns.Describe(e)} after phase {phase}");
            if (p > phase) { phase = p; lastRank = -1; }
            if (e is SecondWind && p == 1)
            {
                // Inside the biter's action, right after its bite.
                Assert.Equal(3, lastRank);
                lastRank = rank;
                continue;
            }
            switch (p)
            {
                case 1:
                    if (id != lastPredator)
                    {
                        Assert.True(id > lastPredator, $"predator {id} after {lastPredator}");
                        lastPredator = id;
                        lastRank = -1;
                    }
                    Assert.True(rank > lastRank, $"{Turns.Describe(e)} out of its action's order");
                    lastRank = rank;
                    break;
                case 2:
                    Assert.True(id > lastGame);
                    lastGame = id;
                    break;
                case 3:
                    Assert.True(rank >= worldRank, $"{Turns.Describe(e)} out of the world's order");
                    if (e is StageReached st)
                    {
                        Assert.True(st.Stage > lastStage);
                        lastStage = st.Stage;
                    }
                    else Assert.True(rank > worldRank || e is Arrived, $"{Turns.Describe(e)} twice");
                    worldRank = rank;
                    break;
            }
        }
    }

    /// <summary>An event's phase, its creature (for an action), and its rank in its phase.</summary>
    private static (int Phase, int Id, int Rank) Classify(TurnEvent e, Dictionary<int, CreatureKind> kinds, int phase) => e switch
    {
        Walked or Ate or Drank or Rested or Struck or Afflicted => (0, 0, 0),
        Noticed { Kind: Notice.Spotted or Notice.Heard } n => (1, n.Creature, 0),
        Stepped s when kinds.GetValueOrDefault(s.Creature) == CreatureKind.SmallGame => (2, s.Creature, 0),
        Stepped s => (1, s.Creature, 1),
        Stayed s => (1, s.Creature, 1),
        Noticed n => (1, n.Creature, 2),
        Bit b => (1, b.Creature, 3),
        SecondWind when phase <= 1 => (1, -1, 4),   // inside the biter's action: id -1 is fixed below
        Hurt { By: HurtBy.Spikes } h => (1, h.Creature, 5),
        Hurt h => (1, h.Creature, 6),
        TimePassed => (3, 0, 0),
        SecondWind => (3, 0, 1),
        Grew => (3, 0, 2),
        StageReached => (3, 0, 3),
        Arrived => (3, 0, 4),
        PlantsGrew => (3, 0, 5),
        ApexArrived => (3, 0, 6),
        ApexWaits => (3, 0, 7),
        InstinctsOffered => (3, 0, 8),
        _ => throw new Xunit.Sdk.XunitException($"an event the order does not know: {e}"),
    };
}
