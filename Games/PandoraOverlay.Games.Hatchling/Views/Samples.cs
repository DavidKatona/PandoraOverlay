using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// A layout state's content (A26): a sample state, and for
/// <c>run-playback</c> the turn it is frozen in, the beat and the tick.
/// </summary>
/// <param name="State">The game's state as the sample has it; handed to the same code the real game's state is.</param>
/// <param name="Turn">A turn played back and frozen, or null for a still state at rest.</param>
/// <param name="Beat">The beat of <paramref name="Turn"/> it stands in.</param>
/// <param name="Tick">The tick of that beat.</param>
internal sealed record Sample(GameState State, TurnResult? Turn = null, int Beat = 0, int Tick = 0);

/// <summary>
/// The layout states' samples, built in code and never read from a save
/// (A26): the window's layout tests lay each one out in every theme at its fit
/// factor, and the render tool draws them, so they carry the longest names and the
/// largest numbers the screens must hold (the longest species, two-digit predator
/// numbers, a seven-figure wallet, eleven rows). Each is a <see cref="GameState"/> made
/// with the rules' own functions (an island from <see cref="Island.NewRun"/>, rows
/// from <see cref="TurnScripts"/>), so the page draws them with exactly the code it
/// draws the game with. Deterministic: the same picture every time.
/// </summary>
internal static class Samples
{
    /// <summary>The species of every sample: the longest name in the table.</summary>
    internal const string Species = "Pachycephalosaurus";

    /// <summary>The island the samples stand on.</summary>
    private const uint Seed = 20261115;

    /// <summary>A fixed calendar moment: a sample never reads the clock.</summary>
    private static readonly DateTime Moment = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The wallet of every sample but the first run's: seven figures (A26).</summary>
    internal const long Balance = 1_234_567;

    /// <summary>The sample of a Run page's layout state; anything else throws (Camp's and the other pages' are task H15's).</summary>
    public static Sample For(string state) => state switch
    {
        "run-first" => First(),
        "run-mid" => new Sample(Mid().State),
        "run-playback" => Playback(),
        "run-apex" => Apex(),
        "run-offer" => Offer(),
        "run-lost" => Lost(),
        "run-won" => Won(),
        _ => throw new ArgumentException($"No sample for '{state}'.", nameof(state)),
    };

    // ---- the states ------------------------------------------------------------------------

    /// <summary>Turn 0 of a first run: the turn list at its start, the turn-0 hint (no run finished yet).</summary>
    private static Sample First()
    {
        var state = Game(runs: 0, balance: 0);
        var run = Island.NewRun(new RunSetup(Species, Dangers.Lowest, new Dictionary<Trait, int>(), 0), Seed);
        run.LastTurn = new List<TurnRow>(TurnScripts.RowsAtStart(run, RunPage.SightKnows));
        state.Run = run;
        return new Sample(state);
    }

    /// <summary>
    /// A Subadult in the forest (hidden), three predators known, two instincts, the
    /// turn list at rest after the turn <see cref="Playback"/> plays: the
    /// Carnotaurus 2 spotted you and bit you for 12.
    /// </summary>
    private static (GameState State, TurnResult Turn) Mid()
    {
        var before = Ground(growth: 62.4, danger: 3);
        before.Instincts = new List<Instinct> { Instinct.Eyes, Instinct.Ambush };
        before.Turn = 47;
        var near = Near(before);
        var troodon = Put(before, PredatorType.Troodon, near[4], alert: 0);
        before.Arrived[PredatorType.Carnotaurus] = 1;   // the first Carnotaurus came and went: this is the second
        var carno = Put(before, PredatorType.Carnotaurus, near[0], alert: 2);
        var herrera = Put(before, PredatorType.Herrerasaurus, near[7], alert: 1);

        var herreraTo = Island.Grid.Neighbours(herrera.At).First(c => Island.IsLand(before, c) && Island.CreatureAt(before, c) is null && c != before.You.At);
        var after = before.Clone();
        after.Turn++;
        after.You.LastMove = MoveKind.Rest;
        after.You.Health = 0.71;
        after.You.Growth = before.You.Growth + 1.4;
        after.You.Best = after.You.Growth;
        after.You.Food = 58;
        after.You.Water = 41;
        after.You.Bitten = true;
        after.Creatures.Single(c => c.Id == carno.Id).Alert = Tuning.AlertSeen;
        var movedHerrera = after.Creatures.Single(c => c.Id == herrera.Id);
        movedHerrera.At = herreraTo;

        var events = new TurnEvent[]
        {
            new Rested(8, Tuning.RestNoise),
            new Stayed(troodon.Id, Idle.Waits),
            new Noticed(carno.Id, Notice.Spotted),
            new Stayed(carno.Id, Idle.Waits),
            new Bit(carno.Id, 12, false, false),
            new Stepped(herrera.Id, herrera.At, new[] { herreraTo }, Gait.Search),
            new TimePassed(0, 0, 0),
            new Grew(before.You.Growth, after.You.Growth),
        };
        var turn = new TurnResult(before, after, events);
        after.LastTurn = new List<TurnRow>(TurnScripts.Build(turn, RunPage.SightKnows).Rows);
        var state = Game(runs: 12, balance: Balance);
        state.Run = after;
        return (state, turn);
    }

    /// <summary>The same turn in the middle of the Carnotaurus 2's bite: its banner, the rows that acted written, the one acting marked, the one still to act empty.</summary>
    private static Sample Playback()
    {
        var (state, turn) = Mid();
        var script = TurnScripts.Build(turn, RunPage.SightKnows);
        var bite = script.Beats.ToList().FindIndex(b => b.Kind == BeatKind.Bites);
        return new Sample(state, turn, bite, BeatStep.StrikeTicks + 2);
    }

    /// <summary>An Elder at danger 5 with the apex and seven predators known: eleven rows, the apex's count in the status line.</summary>
    private static Sample Apex()
    {
        var run = Ground(growth: Tuning.GrowthMost, danger: Dangers.Highest);
        run.Instincts = new List<Instinct> { Instinct.Fierce, Instinct.Hardy, Instinct.Quick };
        run.Turn = 312;
        var near = Near(run);
        var apex = Put(run, Tuning.Apex, near[0], alert: Tuning.AlertSeen);
        apex.Relentless = true;
        run.ApexAt = run.Turn - (World.OutlastTurns(run) - 21);
        var types = new[] { PredatorType.Allosaurus, PredatorType.Carnotaurus, PredatorType.Herrerasaurus, PredatorType.Troodon };
        foreach (var t in types) run.Arrived[t] = 9;   // two-digit numbers on the board and in the list (H38)
        var predators = new List<Creature> { apex };
        for (var i = 0; i < 7; i++)
            predators.Add(Put(run, types[i % types.Length], near[i + 1], alert: i % 3 == 0 ? Tuning.AlertSeen : i % 3 == 1 ? 2 : 0));
        PutGame(run, near[9]);

        var rows = new List<TurnRow>
        {
            new(RowActor.You, default, 0, 0, RowCode.Defeated, RowCode.None, 41, 0, default, PredatorType.Carnotaurus, 12, false),
        };
        foreach (var p in predators)
            rows.Add(new TurnRow(RowActor.Predator, p.Type, p.Ordinal, p.Alert, RowCode.Spotted, RowCode.Bit, 12, 0, default, default, 0, false));
        rows.Add(new TurnRow(RowActor.SmallGame, default, 0, 0, RowCode.Scattered, RowCode.None, 0, 0, default, default, 0, false));
        rows.Add(new TurnRow(RowActor.Unseen, default, 0, 0, RowCode.HeardYou, RowCode.None, 0, 0, default, default, 0, false));
        run.LastTurn = rows;

        var state = Game(runs: 12, balance: Balance);
        state.Run = run;
        return new Sample(state);
    }

    /// <summary>An offer of four instincts (Sharp instincts), the redraw priced.</summary>
    private static Sample Offer()
    {
        var (state, _) = Mid();
        var run = state.Run!;
        run.Ranks[Trait.SharpInstincts] = 1;
        run.Offer = new List<Instinct> { Instinct.Appetite, Instinct.Stride, Instinct.Healer, Instinct.Nose };
        run.PicksOwed = 1;
        run.Redraws = 2;
        return new Sample(state);
    }

    /// <summary>The summary of a short lost run: caught early, two pay lines and one achievement.</summary>
    private static Sample Lost()
    {
        var (state, _) = Mid();
        var run = state.Run!;
        run.You.Growth = 27.4;
        run.You.Health = 0;
        run.Over = new RunOver
        {
            Won = false,
            Reason = EndReason.Caught,
            ByType = PredatorType.Carnotaurus,
            ByOrdinal = 2,
            Stage = Stages.Juvenile,
            Lines = new List<PayLine> { new(PayKind.Grew, 27, 14), new(PayKind.Reached, Stages.Juvenile, 8) },
            Earned = new List<string> { AchievementList.First },
            Settled = true,
        };
        run.Over.Total = RunPay.Total(run.Over.Lines);
        return new Sample(state);
    }

    /// <summary>The summary at its tallest: nine pay lines, three achievements and "and 2 more", "Danger 5 unlocked".</summary>
    private static Sample Won()
    {
        var sample = Apex();
        var run = sample.State.Run!;
        run.Danger = Dangers.Highest - 1;
        run.Over = new RunOver
        {
            Won = true,
            Reason = EndReason.DefeatedApex,
            Stage = Stages.Elder,
            Lines = new List<PayLine>
            {
                new(PayKind.Grew, 100, 88),
                new(PayKind.Reached, Stages.Juvenile, 9),
                new(PayKind.Reached, Stages.Subadult, 18),
                new(PayKind.Reached, Stages.Adult, 35),
                new(PayKind.Reached, Stages.Elder, 70),
                new(PayKind.Defeated, 23, 120),
                new(PayKind.DefeatedApex, 0, 105),
                new(PayKind.Danger, Dangers.Highest - 1, 1_212),
                new(PayKind.Legacy, 125, 2_071),
            },
            Earned = new List<string> { AchievementList.Elder, AchievementList.Win, AchievementList.Slayer, AchievementList.Underdog, AchievementList.Heavy },
            DangerUnlocked = Dangers.Highest,
            Settled = true,
        };
        run.Over.Total = RunPay.Total(run.Over.Lines);
        return sample;
    }

    // ---- the pieces --------------------------------------------------------------------------

    /// <summary>A save with every species unlocked, the sample's species chosen, so many runs finished and so much in the wallet.</summary>
    private static GameState Game(int runs, long balance)
    {
        var state = GameState.Fresh(Moment);
        state.Wallet = new Wallet(balance, balance);
        state.Unlocked = SpeciesTable.All.Select(s => s.Name).ToList();
        state.Selected = Species;
        state.Totals.Runs = runs;
        return state;
    }

    /// <summary>
    /// A run of the sample species on the sample island at <paramref name="growth"/>,
    /// you in the forest nearest the island's middle (hidden: a Grazer hides there),
    /// the whole island seen, no creature yet.
    /// </summary>
    private static RunState Ground(double growth, int danger)
    {
        var run = Island.NewRun(new RunSetup(Species, danger, new Dictionary<Trait, int>(), Tuning.StarsAt.Count), Seed);
        run.Creatures.Clear();
        run.Arrived.Clear();   // the start's Troodon is gone with the rest: numbers count the sample's own arrivals
        Array.Fill(run.Seen, true);
        var middle = Island.Grid.Index(Island.Grid.Columns / 2, Island.Grid.Rows / 2);
        var forest = Enumerable.Range(0, run.Tiles.Length)
            .Where(c => run.Tiles[c] == Terrain.Forest && Island.Grid.Neighbours(c).Count() == 6)
            .OrderBy(c => Island.Grid.Distance(c, middle)).ThenBy(c => c)
            .First();
        run.You.At = forest;
        run.You.Growth = growth;
        run.You.Start = 0;
        run.You.Best = growth;
        run.You.Health = 0.83;
        run.You.Food = 64;
        run.You.Water = 47;
        run.You.LastMove = MoveKind.Rest;
        return run;
    }

    /// <summary>The land cells within your sight, nearest first: where the samples' creatures stand, so the player knows each (Sight.Knows).</summary>
    private static List<int> Near(RunState run)
    {
        var sight = Stats.Of(run).Sight;
        return Island.Grid.Within(run.You.At, sight)
            .Where(c => c != run.You.At && run.Tiles[c] != Terrain.Rock)
            .OrderBy(c => Island.Grid.Distance(c, run.You.At)).ThenBy(c => c)
            .ToList();
    }

    /// <summary>A predator of <paramref name="type"/> on <paramref name="cell"/>: the next id, the next number of its species (H38), the run's threat on its health.</summary>
    private static Creature Put(RunState run, PredatorType type, int cell, int alert)
    {
        var creature = new Creature
        {
            Id = run.NextId++,
            Kind = CreatureKind.Predator,
            Type = type,
            Ordinal = Island.TakeOrdinal(run, type),
            At = cell,
            Health = (int)Tuning.Round(Predators.Of(type).Health * Island.Threat(run)),
            Alert = alert,
            Target = alert > 0 ? run.You.At : Tuning.NoTarget,
            SawYou = alert >= Tuning.AlertSeen,
        };
        run.Creatures.Add(creature);
        return creature;
    }

    private static void PutGame(RunState run, int cell) => run.Creatures.Add(new Creature
    {
        Id = run.NextId++,
        Kind = CreatureKind.SmallGame,
        At = cell,
        Health = Tuning.SmallGameHealth,
    });
}
