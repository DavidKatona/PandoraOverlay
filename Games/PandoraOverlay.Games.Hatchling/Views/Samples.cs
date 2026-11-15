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
/// <param name="Reading">The real dino's last reading the Journal shows (H71), the sample's own: null is "no reading yet".</param>
/// <param name="Armed">Camp's unlock is shown armed (its first click's look), as <c>camp-locked</c> has it.</param>
internal sealed record Sample(GameState State, TurnResult? Turn = null, int Beat = 0, int Tick = 0, DinoReading? Reading = null, bool Armed = false);

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

    /// <summary>The sample of a layout state; a name that is not one throws.</summary>
    public static Sample For(string state) => state switch
    {
        "camp-first" => CampFirst(),
        "camp-locked" => CampLocked(),
        "camp-everything" => CampEverything(),
        "run-first" => First(),
        "run-mid" => new Sample(Mid().State),
        "run-playback" => Playback(),
        "run-apex" => Apex(),
        "run-offer" => Offer(),
        "run-lost" => Lost(),
        "run-won" => Won(),
        "lineage-fresh" => LineageFresh(),
        "lineage-mid" => LineageMid(),
        "lineage-full" => LineageFull(),
        "journal-fresh" => JournalFresh(),
        "journal-busy" => JournalBusy(),
        "records-fresh" => RecordsFresh(),
        "records-full" => RecordsFull(),
        _ when HatchlingText.Guide.Any(s => HatchlingView.GuidePrefix + s.Id == state) => Guide(),
        _ => throw new ArgumentException($"No sample for '{state}'.", nameof(state)),
    };

    // ---- the pages between runs: Camp, the Lineage, the Journal, Records --------------------------

    /// <summary>The species the samples' lists add beside <see cref="Species"/>: long names, in the table's order.</summary>
    private static readonly string[] Others = { "Hypsilophodon", "Tenontosaurus", "Diabloceratops", "Herrerasaurus", "Dilophosaurus" };

    /// <summary>A seen growth with every star (300% and over), its whole percent three digits wide.</summary>
    private const double AllStarsGrown = 345.6;

    /// <summary>A fresh save, as a player first meets it (H25): two unlocked, Dryosaurus chosen, no run finished, so the first minute's note.</summary>
    private static Sample CampFirst() => new(GameState.Fresh(Moment));

    /// <summary>
    /// The sample species chosen and locked, the wallet enough for the unlock at its
    /// highest price (1,500, after eleven bought), the unlock armed; thirteen species
    /// unlocked with stars, wins and freshness of every kind on their cards; danger 4
    /// of 4 chosen, so the fifth radio is disabled and the danger's pay has two decimals.
    /// </summary>
    private static Sample CampLocked()
    {
        var state = GameState.Fresh(Moment);
        state.Wallet = new Wallet(Balance, Balance);
        state.Bought = 11;
        var locked = new HashSet<string>(StringComparer.Ordinal)
            { Species, "Stegosaurus", "Pteranodon", "Carnotaurus", "Deinosuchus", "Tyrannosaurus", "Beipiaosaurus", "Gallimimus" };
        state.Unlocked = SpeciesTable.All.Select(s => s.Name).Where(n => !locked.Contains(n)).ToList();
        var i = 0;
        foreach (var name in state.Unlocked)
        {
            var record = state.RecordToChange(name);
            record.Grown = i % 4 * 100 + 12.5;          // 12%, 112%, 212%, 312%: no star to three
            record.Fresh = Tuning.FreshFloor + i % 5 * 0.5;   // x1.0 to x3.0
            record.WonAt = i % 6;                       // no win to danger 5
            record.Runs = 10 + i;
            i++;
        }
        state.Selected = Species;
        state.MaxDanger = Dangers.Highest - 1;
        state.Danger = Dangers.Highest - 1;
        state.Totals.Runs = 1234;
        return new Sample(state, Armed: true);
    }

    /// <summary>
    /// All 21 unlocked, each with three stars and won at danger 5, the wallet in seven
    /// figures, danger 5 chosen, the Beipiaosaurus chosen: its kind line ("Forager ·
    /// eats plants, meat and fish") is the widest of the 21, so the window's walk holds
    /// it across the panel in every theme (H83).
    /// </summary>
    private static Sample CampEverything()
    {
        var state = Game(runs: 12_345, balance: Balance);
        var i = 0;
        foreach (var species in SpeciesTable.All)
        {
            var record = state.RecordToChange(species.Name);
            record.Grown = AllStarsGrown;
            record.Fresh = Tuning.FreshFloor + i % 5 * 0.5;
            record.WonAt = Dangers.Highest;
            record.Runs = 100 + i;
            record.BestGrowth = Tuning.GrowthMost;
            i++;
        }
        state.Selected = "Beipiaosaurus";
        state.MaxDanger = Dangers.Highest;
        state.Danger = Dangers.Highest;
        return new Sample(state);
    }

    /// <summary>Nothing bought, the wallet empty: every first tier priced in Off, every other one needing the one above.</summary>
    private static Sample LineageFresh() => new(GameState.Fresh(Moment));

    /// <summary>
    /// Some bought (Keen nose, Fast metabolism and Sharp instincts complete), some the
    /// wallet can pay (Deep stomach, Thick hide, Quick reflexes, Strong start), some it
    /// can't (Dry spells, Legacy, Second thoughts), and some needing the one above.
    /// </summary>
    private static Sample LineageMid()
    {
        var state = GameState.Fresh(Moment);
        state.Wallet = new Wallet(750, 9_870);
        state.Ranks[Trait.DeepStomach] = 3;
        state.Ranks[Trait.ThickHide] = 1;
        state.Ranks[Trait.KeenNose] = Lineage.Of(Trait.KeenNose).MaxRank;
        state.Ranks[Trait.FastMetabolism] = Lineage.Of(Trait.FastMetabolism).MaxRank;
        state.Ranks[Trait.StrongStart] = 1;
        state.Ranks[Trait.SharpInstincts] = Lineage.Of(Trait.SharpInstincts).MaxRank;
        state.Totals.RanksBought = state.Ranks.Values.Sum();
        return new Sample(state);
    }

    /// <summary>Every trait complete, the wallet in seven figures.</summary>
    private static Sample LineageFull()
    {
        var state = Game(runs: 12_345, balance: Balance);
        foreach (var trait in Lineage.All) state.Ranks[trait.Trait] = trait.MaxRank;
        state.Totals.RanksBought = Lineage.All.Sum(t => t.MaxRank);
        return new Sample(state);
    }

    /// <summary>The journal's three entries at 0, at their widest words; nothing lately; no reading yet; the wallet empty, so no Swap.</summary>
    private static Sample JournalFresh()
    {
        var state = GameState.Fresh(Moment);
        state.Journal.Open = Entries();
        return new Sample(state);
    }

    /// <summary>
    /// The entries in progress, the first for the species the real dino is (in game,
    /// fully grown: "100%"); five species grown, so five rows of pay with the real
    /// dino's first; six lines lately, one of each kind and the widest of each.
    /// </summary>
    private static Sample JournalBusy()
    {
        var state = Game(runs: 12_345, balance: Balance);
        state.Journal.Open = Entries();
        state.Journal.Open[0].Seen = 32.4;
        state.Journal.Open[1].Seen = 74.9;
        state.Journal.Open[2].Seen = 0.6;
        state.Journal.Done = 1_234;
        var grown = new[] { AllStarsGrown, 299.9, 150.0, 123.4, 88.8, 50.0 };
        var fresh = new[] { Tuning.FreshFloor, 1.2, 2.4, 2.95, Tuning.FreshMax, Tuning.FreshMax };
        var names = new[] { Species }.Concat(Others).ToArray();
        for (var i = 0; i < names.Length; i++)
        {
            var record = state.RecordToChange(names[i]);
            record.Grown = grown[i];
            record.Fresh = fresh[i];
        }
        state.Lately = new List<LatelyLine>
        {
            new(LatelyKind.Grew, Species, 13.2, 2.4, 127, 0),
            new(LatelyKind.Journal, Species, 0, 0, Tuning.JournalReward, (int)Tuning.JournalTarget),
            new(LatelyKind.Star, Species, 0, 0, 0, 0),
            new(LatelyKind.Unlocked, Species, 0, 0, 0, 0),
            new(LatelyKind.Grew, Others[2], 99.9, Tuning.FreshMax, 1_199, 0),
            new(LatelyKind.Star, Others[0], 0, 0, 0, 0),
        };
        return new Sample(state, Reading: new DinoReading(true, Species, null, 1.0));
    }

    /// <summary>Nothing earned, nothing played, the sample species chosen (locked).</summary>
    private static Sample RecordsFresh()
    {
        var state = GameState.Fresh(Moment);
        state.Selected = Species;
        return new Sample(state);
    }

    /// <summary>
    /// The Guide's sections (H94): its words are the same for every save, so the sample
    /// only gives the game's bar its widest: no run (the first tab reads Camp, the longer
    /// word) and the wallet in seven figures beside the five tabs.
    /// </summary>
    private static Sample Guide()
    {
        var state = GameState.Fresh(Moment);
        state.Wallet = new Wallet(Balance, Balance);
        return new Sample(state);
    }

    /// <summary>All 24 earned and every total large: 21 of 21 unlocked, 63 of 63 stars, danger 5 won, the chosen species' line at its longest.</summary>
    private static Sample RecordsFull()
    {
        var state = Game(runs: 12_345, balance: Balance);
        state.Wallet = new Wallet(Balance, 12_345_678);
        state.Achievements = new Achievements(AchievementList.All, AchievementList.All.Select(a => a.Id));
        state.Totals.Wins = 9_876;
        state.Totals.Kills = 123_456;
        state.Journal.Done = 1_234;
        foreach (var species in SpeciesTable.All)
        {
            var record = state.RecordToChange(species.Name);
            record.Grown = AllStarsGrown;
            record.WonAt = Dangers.Highest;
            record.BestGrowth = Tuning.GrowthMost;
            record.Runs = 100;
        }
        state.RecordToChange(Species).Runs = 1_234;
        return new Sample(state);
    }

    /// <summary>The journal's entries at their widest: the sample species first, then two more long names, none a big build's (75%, 400).</summary>
    private static List<JournalEntry> Entries() =>
        new[] { Species, Others[2], Others[0] }
            .Select(n => SpeciesTable.TryGet(n, out var info) ? Journal.EntryFor(info) : throw new InvalidOperationException("a sample's species is the table's"))
            .ToList();

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
        return new Sample(state, turn, bite, BeatStep.WindUpTicks + 2 * BeatTicks.Stretch);
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
