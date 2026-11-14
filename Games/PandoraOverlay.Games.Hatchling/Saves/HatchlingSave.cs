using System.Globalization;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Hatchling's save (A9): its version, its schema, the copy of the game's state into
/// the save's plain shape (<see cref="SaveData"/>) and back, and the game's own check
/// of a save just read. Losing a player's progress is the worst bug this feature can
/// have, so this class has one job and keeps it narrow: a state saved here reads back
/// exactly, field for field, generators included, and a save no run of the game
/// could have written is refused, never played.
///
/// <para><b>Text only.</b> Nothing here touches a file: the host owns the disk, the
/// envelope, the backups and the held saves (A7). <see cref="HatchlingGame"/> hands
/// <see cref="Schema"/>'s text to the host's storage and a read's text back to it.</para>
///
/// <para><b>Version 1 until the owner releases</b> (A9). A shape changed during
/// development is simply Corrupt (held, then set aside on the player's click); after a
/// release a change of meaning needs a new version and an upgrade step, and every
/// shipped version keeps its fixtures in the tests for ever.</para>
///
/// <para><b>What is refused</b> (<see cref="Valid"/>): what no run of the game could
/// have written, and anything the rules would throw on: a list of the wrong length, a
/// cell off the board, an id the tables do not know (but an achievement's, which is
/// kept), a health outside 0 and 1, food or water outside 0 and 100, growth outside 0
/// and 100, a rank above its trait's, a danger outside 1 and 5, two creatures with one
/// id or one at or above the next id, a predator's number its species never reached or
/// one two living ones share (H38), an offered instinct already owned. A refused save
/// is the schema's Corrupt: the game answers SaveUnreadable and the host keeps the file
/// untouched (A7). <see cref="To"/> never throws on a save this check passes.</para>
/// </summary>
internal static class HatchlingSave
{
    /// <summary>The save's version: 1, until the owner releases (A9).</summary>
    public const int Version = 1;

    /// <summary>
    /// The save as versioned JSON (A9): version 1, no upgrade step (there is no older
    /// version), and <see cref="Valid"/> as the game's own check of every state read.
    /// </summary>
    public static readonly SaveSchema<SaveData> Schema = new(Version, Valid);

    // ===== The state into the save ==========================================================

    /// <summary>
    /// <paramref name="state"/> as the save's plain shape: every member copied, nothing
    /// shared with the state. Lists keep their order; what the state keeps as a set or
    /// a dictionary is written in a fixed order (the table's, the enum's or ordinal),
    /// so one state always gives one text. A value no table names (an enum member that
    /// is not one) throws: a defect of the game, found here, before it could be written.
    /// </summary>
    public static SaveData From(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new SaveData
        {
            Balance = state.Wallet.Balance,
            Earned = state.Wallet.Earned,
            Carry = state.Carry,
            Ranks = RanksFrom(state.Ranks),
            Unlocked = new List<string>(state.Unlocked),
            Bought = state.Bought,
            Selected = state.Selected,
            Danger = state.Danger,
            MaxDanger = state.MaxDanger,
            Fast = state.Fast,
            Species = state.Species
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => SpeciesFrom(p.Value), StringComparer.Ordinal),
            FreshAsOf = MomentText(state.FreshAsOf),
            Journal = new SavedJournal
            {
                Open = state.Journal.Open.Select(EntryFrom).ToList(),
                Done = state.Journal.Done,
                Rng = state.Journal.Rng,
            },
            Achievements = new List<string>(state.Achievements.Unlocked),
            Totals = new SavedTotals
            {
                Runs = state.Totals.Runs,
                Wins = state.Totals.Wins,
                Kills = state.Totals.Kills,
                BuildsWon = state.Totals.BuildsWon.OrderBy(b => b).Select(b => Name(b)).ToList(),
                SpeciesWon = state.Totals.SpeciesWon.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                RanksBought = state.Totals.RanksBought,
            },
            NextSeed = state.NextSeed,
            Lately = state.Lately.Select(LineFrom).ToList(),
            Run = state.Run is null ? null : RunFrom(state.Run),
        };
    }

    private static Dictionary<string, int> RanksFrom(Dictionary<Trait, int> ranks) =>
        ranks.OrderBy(p => p.Key).ToDictionary(p => Lineage.Of(p.Key).Id, p => p.Value, StringComparer.Ordinal);

    private static SavedSpecies SpeciesFrom(SpeciesRecord r) => new()
    {
        Grown = r.Grown,
        Fresh = r.Fresh,
        Adults = r.Adults,
        Runs = r.Runs,
        BestGrowth = r.BestGrowth,
        WonAt = r.WonAt,
    };

    private static SavedEntry EntryFrom(JournalEntry e) => new()
    {
        Species = e.Species,
        Target = e.Target,
        Reward = e.Reward,
        Seen = e.Seen,
    };

    private static SavedLine LineFrom(LatelyLine line) => new()
    {
        Kind = Name(line.Kind),
        Species = line.Species,
        Percent = line.Percent,
        Fresh = line.Fresh,
        Amount = line.Amount,
        Target = line.Target,
    };

    private static SavedRun RunFrom(RunState run) => new()
    {
        Species = run.Species,
        Danger = run.Danger,
        Ranks = RanksFrom(run.Ranks),
        Stars = run.Stars,
        Seed = run.Seed,
        Rng = run.Rng,
        Turn = run.Turn,
        Tiles = new string(run.Tiles.Select(LetterOf).ToArray()),
        Seen = Flags(run.Seen),
        Plants = Flags(run.Plants),
        Meat = new List<int>(run.Meat),
        Creatures = run.Creatures.Select(CreatureFrom).ToList(),
        NextId = run.NextId,
        Arrived = run.Arrived.OrderBy(p => p.Key)
            .ToDictionary(p => Predators.NameOf(p.Key), p => p.Value, StringComparer.Ordinal),
        Pace = Name(run.Pace),
        You = YouFrom(run.You),
        Instincts = run.Instincts.Select(Hatchling.Instincts.IdOf).ToList(),
        Offer = run.Offer?.Select(Hatchling.Instincts.IdOf).ToList(),
        PicksOwed = run.PicksOwed,
        Redraws = run.Redraws,
        FreeRedrawUsed = run.FreeRedrawUsed,
        ApexAt = run.ApexAt,
        Kills = run.Kills,
        Over = run.Over is null ? null : OverFrom(run.Over),
        LastTurn = run.LastTurn.Select(RowFrom).ToList(),
    };

    private static SavedCreature CreatureFrom(Creature c) => new()
    {
        Id = c.Id,
        Kind = Name(c.Kind),
        Type = Predators.NameOf(c.Type),
        Ordinal = c.Ordinal,
        At = c.At,
        Health = c.Health,
        Alert = c.Alert,
        Target = c.Target,
        Relentless = c.Relentless,
        SawYou = c.SawYou,
        Dazed = c.Dazed,
        DazeFreeAt = c.DazeFreeAt,
        Slowed = c.Slowed,
        Bleed = c.Bleed,
        BleedDamage = c.BleedDamage,
        HitByYou = c.HitByYou,
    };

    private static SavedYou YouFrom(You you) => new()
    {
        At = you.At,
        Growth = you.Growth,
        Start = you.Start,
        Best = you.Best,
        Health = you.Health,
        Food = you.Food,
        Water = you.Water,
        Wind = you.Wind,
        LastMove = Name(you.LastMove),
        LatchOn = you.LatchOn,
        LatchStreak = you.LatchStreak,
        Bitten = you.Bitten,
        AdultUnbitten = you.AdultUnbitten,
    };

    private static SavedOver OverFrom(RunOver over) => new()
    {
        Won = over.Won,
        Reason = Name(over.Reason),
        ByType = Predators.NameOf(over.ByType),
        ByOrdinal = over.ByOrdinal,
        Stage = over.Stage,
        Lines = over.Lines.Select(l => new SavedPay { Kind = Name(l.Kind), Value = l.Value, Amount = l.Amount }).ToList(),
        Total = over.Total,
        Earned = new List<string>(over.Earned),
        DangerUnlocked = over.DangerUnlocked,
        Settled = over.Settled,
    };

    private static SavedRow RowFrom(TurnRow row) => new()
    {
        Actor = Name(row.Actor),
        Type = Predators.NameOf(row.Type),
        Ordinal = row.Ordinal,
        Alert = row.Alert,
        First = Name(row.First),
        Second = Name(row.Second),
        A = row.A,
        B = row.B,
        Food = Name(row.Food),
        TargetType = Predators.NameOf(row.TargetType),
        TargetOrdinal = row.TargetOrdinal,
        TargetIsSmallGame = row.TargetIsSmallGame,
    };

    // ===== The save into the state ==========================================================

    /// <summary>
    /// The game's state from a save <see cref="Valid"/> passed: every member copied
    /// back, nothing shared with <paramref name="data"/>. It never throws on such a
    /// save; on one the check refuses it may (a defect of the caller: the schema
    /// never hands one over). The journal is NOT topped up here: a journal with fewer
    /// than three open entries is filled by the game at Attach, which draws from the
    /// journal's own generator (task H06's rule).
    /// </summary>
    public static GameState To(SaveData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new GameState
        {
            Wallet = new Wallet(data.Balance, data.Earned),
            Carry = data.Carry,
            Ranks = RanksTo(data.Ranks),
            Unlocked = new List<string>(data.Unlocked),
            Bought = data.Bought,
            Selected = data.Selected,
            Danger = data.Danger,
            MaxDanger = data.MaxDanger,
            Fast = data.Fast,
            Species = data.Species.ToDictionary(p => p.Key, p => SpeciesTo(p.Value), StringComparer.Ordinal),
            FreshAsOf = Moment(data.FreshAsOf) ?? throw new FormatException("The save's moment is not one in UTC."),
            Journal = new JournalState
            {
                Open = data.Journal.Open.Select(EntryTo).ToList(),
                Done = data.Journal.Done,
                Rng = data.Journal.Rng,
            },
            Achievements = new Achievements(AchievementList.All, data.Achievements),
            Totals = new Totals
            {
                Runs = data.Totals.Runs,
                Wins = data.Totals.Wins,
                Kills = data.Totals.Kills,
                BuildsWon = new HashSet<Build>(data.Totals.BuildsWon.Select(Parse<Build>)),
                SpeciesWon = new HashSet<string>(data.Totals.SpeciesWon, StringComparer.Ordinal),
                RanksBought = data.Totals.RanksBought,
            },
            NextSeed = data.NextSeed,
            Lately = data.Lately.Select(LineTo).ToList(),
            Run = data.Run is null ? null : RunTo(data.Run),
        };
    }

    private static Dictionary<Trait, int> RanksTo(Dictionary<string, int> ranks) =>
        ranks.ToDictionary(p => TraitOf(p.Key), p => p.Value);

    private static SpeciesRecord SpeciesTo(SavedSpecies r) => new()
    {
        Grown = r.Grown,
        Fresh = r.Fresh,
        Adults = r.Adults,
        Runs = r.Runs,
        BestGrowth = r.BestGrowth,
        WonAt = r.WonAt,
    };

    private static JournalEntry EntryTo(SavedEntry e) => new()
    {
        Species = e.Species,
        Target = e.Target,
        Reward = e.Reward,
        Seen = e.Seen,
    };

    private static LatelyLine LineTo(SavedLine l) =>
        new(Parse<LatelyKind>(l.Kind), l.Species, l.Percent, l.Fresh, l.Amount, l.Target);

    private static RunState RunTo(SavedRun r) => new()
    {
        Species = r.Species,
        Danger = r.Danger,
        Ranks = RanksTo(r.Ranks),
        Stars = r.Stars,
        Seed = r.Seed,
        Rng = r.Rng,
        Turn = r.Turn,
        Tiles = r.Tiles.Select(TerrainOf).ToArray(),
        Seen = r.Seen.Select(c => c == '1').ToArray(),
        Plants = r.Plants.Select(c => c == '1').ToArray(),
        Meat = r.Meat.ToArray(),
        Creatures = r.Creatures.Select(CreatureTo).ToList(),
        NextId = r.NextId,
        Arrived = r.Arrived.ToDictionary(p => PredatorOf(p.Key), p => p.Value),
        Pace = Parse<Pace>(r.Pace),
        You = YouTo(r.You),
        Instincts = r.Instincts.Select(InstinctOf).ToList(),
        Offer = r.Offer?.Select(InstinctOf).ToList(),
        PicksOwed = r.PicksOwed,
        Redraws = r.Redraws,
        FreeRedrawUsed = r.FreeRedrawUsed,
        ApexAt = r.ApexAt,
        Kills = r.Kills,
        Over = r.Over is null ? null : OverTo(r.Over),
        LastTurn = r.LastTurn.Select(RowTo).ToList(),
    };

    private static Creature CreatureTo(SavedCreature c) => new()
    {
        Id = c.Id,
        Kind = Parse<CreatureKind>(c.Kind),
        Type = PredatorOf(c.Type),
        Ordinal = c.Ordinal,
        At = c.At,
        Health = c.Health,
        Alert = c.Alert,
        Target = c.Target,
        Relentless = c.Relentless,
        SawYou = c.SawYou,
        Dazed = c.Dazed,
        DazeFreeAt = c.DazeFreeAt,
        Slowed = c.Slowed,
        Bleed = c.Bleed,
        BleedDamage = c.BleedDamage,
        HitByYou = c.HitByYou,
    };

    private static You YouTo(SavedYou y) => new()
    {
        At = y.At,
        Growth = y.Growth,
        Start = y.Start,
        Best = y.Best,
        Health = y.Health,
        Food = y.Food,
        Water = y.Water,
        Wind = y.Wind,
        LastMove = Parse<MoveKind>(y.LastMove),
        LatchOn = y.LatchOn,
        LatchStreak = y.LatchStreak,
        Bitten = y.Bitten,
        AdultUnbitten = y.AdultUnbitten,
    };

    private static RunOver OverTo(SavedOver o) => new()
    {
        Won = o.Won,
        Reason = Parse<EndReason>(o.Reason),
        ByType = PredatorOf(o.ByType),
        ByOrdinal = o.ByOrdinal,
        Stage = o.Stage,
        Lines = o.Lines.Select(l => new PayLine(Parse<PayKind>(l.Kind), l.Value, l.Amount)).ToList(),
        Total = o.Total,
        Earned = new List<string>(o.Earned),
        DangerUnlocked = o.DangerUnlocked,
        Settled = o.Settled,
    };

    private static TurnRow RowTo(SavedRow r) =>
        new(Parse<RowActor>(r.Actor), PredatorOf(r.Type), r.Ordinal, r.Alert, Parse<RowCode>(r.First), Parse<RowCode>(r.Second),
            r.A, r.B, Parse<Food>(r.Food), PredatorOf(r.TargetType), r.TargetOrdinal, r.TargetIsSmallGame);

    // ===== The game's own check =============================================================

    /// <summary>
    /// The save can be played: nothing in it is what no run of the game could have
    /// written, and nothing in it would make <see cref="To"/> or the rules throw (see
    /// the class's summary for the list). Never throws, whatever it is given: the
    /// schema calls it on whatever a file held. The schema answers Corrupt when it is
    /// false (A9), and so does the game, which saves a state only after this check
    /// passes it too (<see cref="HatchlingGame"/>): a save that could not be read back
    /// is never written.
    /// </summary>
    public static bool Valid(SaveData data) => Problem(data) is null;

    /// <summary>
    /// What is wrong with <paramref name="data"/>, in a few words, or null when
    /// nothing is: <see cref="Valid"/>'s reason, for the game's defect message and
    /// the tests'. The words name a field and a rule, never a value: they could
    /// otherwise carry text from a damaged file into a message.
    /// </summary>
    internal static string? Problem(SaveData? data)
    {
        if (data is null) return "there is no save";
        if (data.Balance < 0 || data.Earned < 0) return "the wallet is below zero";
        if (!(data.Carry >= 0 && data.Carry < 1)) return "the carry is not from 0 to under 1";
        if (RanksProblem(data.Ranks) is { } ranks) return ranks;
        if (data.Unlocked is null || !data.Unlocked.All(IsSpecies)) return "an unlocked species is not in the table";
        if (!Distinct(data.Unlocked)) return "a species is unlocked twice";
        if (data.Bought < 0 || data.Bought > SpeciesTable.All.Count) return "the species bought are not from 0 to 21";
        if (!IsSpecies(data.Selected)) return "the chosen species is not in the table";
        if (!IsDanger(data.Danger) || !IsDanger(data.MaxDanger)) return "a danger is not from 1 to 5";
        if (SpeciesProblem(data.Species) is { } species) return species;
        if (Moment(data.FreshAsOf) is null) return "the freshness's moment is not one in UTC";
        if (JournalProblem(data.Journal) is { } journal) return journal;
        if (data.Achievements is null) return "the achievements are missing";
        if (TotalsProblem(data.Totals) is { } totals) return totals;
        if (LatelyProblem(data.Lately) is { } lately) return lately;
        if (data.Run is not null && RunProblem(data.Run) is { } run) return run;
        return null;
    }

    private static string? RanksProblem(Dictionary<string, int>? ranks)
    {
        if (ranks is null) return "the ranks are missing";
        foreach (var (id, rank) in ranks)
        {
            if (!Lineage.TryParse(id, out var trait)) return "a trait's id is not in the lineage";
            if (rank < 0 || rank > Lineage.Of(trait).MaxRank) return "a rank is above its trait's, or below 0";
        }
        return null;
    }

    private static string? SpeciesProblem(Dictionary<string, SavedSpecies>? records)
    {
        if (records is null) return "the species' records are missing";
        foreach (var (name, r) in records)
        {
            if (!IsSpecies(name)) return "a record's species is not in the table";
            if (r is null) return "a species' record is missing";
            if (!(r.Grown >= 0)) return "a species' growth is below 0";
            if (!(r.Fresh >= Tuning.FreshFloor && r.Fresh <= Tuning.FreshMax)) return "a freshness is not from 1.0 to 3.0";
            if (r.Adults < 0 || r.Runs < 0) return "a species' count is below 0";
            if (!IsGrowth(r.BestGrowth)) return "a species' best growth is not from 0 to 100";
            if (r.WonAt < 0 || r.WonAt > Dangers.Highest) return "a species' won-at danger is not from 0 to 5";
        }
        return null;
    }

    private static string? JournalProblem(SavedJournal? journal)
    {
        if (journal?.Open is null) return "the journal is missing";
        if (journal.Open.Count > Tuning.JournalEntries) return "the journal holds more than three entries";
        foreach (var e in journal.Open)
        {
            if (e is null || !IsSpecies(e.Species)) return "a journal entry's species is not in the table";
            if (!(e.Target > 0) || e.Reward < 0 || !(e.Seen >= 0)) return "a journal entry's numbers are out of range";
        }
        if (journal.Done < 0) return "the journal's count is below 0";
        return null;
    }

    private static string? TotalsProblem(SavedTotals? totals)
    {
        if (totals?.BuildsWon is null || totals.SpeciesWon is null) return "the totals are missing";
        if (totals.Runs < 0 || totals.Wins < 0 || totals.Kills < 0 || totals.RanksBought < 0) return "a total is below 0";
        if (!totals.BuildsWon.All(IsName<Build>) || !Distinct(totals.BuildsWon)) return "a build won with is unknown or twice";
        if (!totals.SpeciesWon.All(IsSpecies) || !Distinct(totals.SpeciesWon)) return "a species won with is unknown or twice";
        return null;
    }

    private static string? LatelyProblem(List<SavedLine>? lately)
    {
        if (lately is null) return "the lines of lately are missing";
        if (lately.Count > ServerPlay.LatelyMost) return "lately holds more than six lines";
        foreach (var line in lately)
            if (line is null || !IsName<LatelyKind>(line.Kind) || !IsSpecies(line.Species))
                return "a line of lately is not one the game writes";
        return null;
    }

    private static string? RunProblem(SavedRun run)
    {
        if (!IsSpecies(run.Species)) return "the run's species is not in the table";
        if (!IsDanger(run.Danger)) return "the run's danger is not from 1 to 5";
        if (RanksProblem(run.Ranks) is { } ranks) return "the run's lineage: " + ranks;
        if (run.Stars < 0 || run.Stars > Tuning.StarsAt.Count) return "the run's stars are not from 0 to 3";
        if (run.Turn < 0) return "the run's turn is below 0";
        if (!Board(run.Tiles, "gfwr")) return "the run's ground is not 121 letters of g, f, w and r";
        if (!Board(run.Seen, "01") || !Board(run.Plants, "01")) return "the run's seen cells or plants are not 121 letters of 0 and 1";
        if (run.Meat is null || run.Meat.Count != Tuning.Cells || run.Meat.Any(m => m < 0)) return "the run's meat is not 121 counts";
        if (run.NextId < 1) return "the run's next id is below 1";
        if (run.Arrived is null) return "the run's arrivals are missing";
        foreach (var (type, count) in run.Arrived)
            if (!IsPredator(type) || count < 0) return "an arrival's predator is unknown, or its count below 0";
        if (CreaturesProblem(run) is { } creatures) return creatures;
        if (!IsName<Pace>(run.Pace)) return "the run's pace is not one";
        if (YouProblem(run.You) is { } you) return you;
        if (run.Instincts is null || !run.Instincts.All(IsInstinct) || !Distinct(run.Instincts))
            return "an instinct picked is unknown or twice";
        if (run.Offer is not null)
        {
            var size = run.Ranks.TryGetValue(Lineage.Of(Trait.SharpInstincts).Id, out var sharp) && sharp > 0
                ? Tuning.SharpOfferSize
                : Tuning.OfferSize;
            if (run.Offer.Count == 0 || run.Offer.Count > size) return "the offer is empty or holds more than it can";
            if (!run.Offer.All(IsInstinct) || !Distinct(run.Offer)) return "an instinct offered is unknown or twice";
            if (run.Offer.Any(run.Instincts.Contains)) return "an instinct offered is owned already";
        }
        if (run.PicksOwed < 0 || run.Redraws < 0 || run.Kills < 0) return "a count of the run is below 0";
        if (run.ApexAt < Tuning.ApexNotYet) return "the apex's turn is below -1";
        if (run.Over is not null && OverProblem(run.Over) is { } over) return over;
        if (run.LastTurn is null || run.LastTurn.Count > TurnScript.MaxRows) return "the turn list is missing or holds more than eleven rows";
        foreach (var row in run.LastTurn)
        {
            if (row is null || !IsName<RowActor>(row.Actor) || !IsPredator(row.Type) || !IsName<RowCode>(row.First)
                || !IsName<RowCode>(row.Second) || !IsName<Food>(row.Food) || !IsPredator(row.TargetType))
                return "a row of the turn list is not one the game writes";
        }
        return null;
    }

    /// <summary>
    /// The creatures: each a kind and a species the tables know, on the board, with an
    /// alert of 0 to 4 and a target that is a cell or none; ids from 1, unique and below
    /// the next id; and a predator's number (H38) one its species has reached in this
    /// run (from 1 to its <c>Arrived</c>), held by no other living one of its species.
    /// </summary>
    private static string? CreaturesProblem(SavedRun run)
    {
        if (run.Creatures is null) return "the creatures are missing";
        var ids = new HashSet<int>();
        var numbers = new HashSet<(string, int)>();
        foreach (var c in run.Creatures)
        {
            if (c is null) return "a creature is missing";
            if (c.Id < 1 || c.Id >= run.NextId) return "a creature's id is not from 1 to under the next id";
            if (!ids.Add(c.Id)) return "two creatures share an id";
            if (!IsName<CreatureKind>(c.Kind) || !IsPredator(c.Type)) return "a creature's kind or species is unknown";
            if (!IsCell(c.At)) return "a creature is off the board";
            if (c.Alert < 0 || c.Alert > Tuning.AlertSeen) return "a creature's alert is not from 0 to 4";
            if (c.Target != Tuning.NoTarget && !IsCell(c.Target)) return "a creature's target is off the board";
            if (c.Kind != nameof(CreatureKind.Predator)) continue;
            var arrived = run.Arrived.TryGetValue(c.Type, out var n) ? n : 0;
            if (c.Ordinal < 1 || c.Ordinal > arrived) return "a predator's number is one its species never reached";
            if (!numbers.Add((c.Type, c.Ordinal))) return "two living predators of one species share a number";
        }
        return null;
    }

    private static string? YouProblem(SavedYou? you)
    {
        if (you is null) return "your dino is missing";
        if (!IsCell(you.At)) return "your dino is off the board";
        if (!IsGrowth(you.Growth) || !IsGrowth(you.Start) || !IsGrowth(you.Best)) return "your growth is not from 0 to 100";
        if (!(you.Health >= 0 && you.Health <= 1)) return "your health is not from 0 to 1";
        if (!(you.Food >= 0 && you.Food <= Tuning.FoodMost) || !(you.Water >= 0 && you.Water <= Tuning.WaterMost))
            return "your food or water is not from 0 to 100";
        if (!IsName<MoveKind>(you.LastMove)) return "your last move is not one";
        if (you.LatchOn < 0 || you.LatchStreak < 0) return "your latch is below 0";
        return null;
    }

    private static string? OverProblem(SavedOver over)
    {
        if (!IsName<EndReason>(over.Reason) || !IsPredator(over.ByType)) return "the run's end is not one the game writes";
        if (over.Stage < Stages.Hatchling || over.Stage > Stages.Elder) return "the run's last stage is not from 0 to 4";
        if (over.Lines is null || over.Lines.Any(l => l is null || !IsName<PayKind>(l.Kind))) return "a line of the run's pay is not one";
        if (over.Total < 0) return "the run's pay is below 0";
        if (over.Earned is null) return "the run's achievements are missing";
        if (over.DangerUnlocked < 0 || over.DangerUnlocked > Dangers.Highest) return "the danger the run unlocked is not from 0 to 5";
        return null;
    }

    // ===== Words of the format ==============================================================

    /// <summary>
    /// A moment as the save writes it: ISO 8601, to the tick, in UTC ("O", with its
    /// Z). The game's moments are the host's calendar, UTC by the Contract
    /// (IGameClock.UtcNow); one of another kind is written as UTC all the same and
    /// never converted, since a conversion would read the PC's time zone.
    /// </summary>
    internal static string MomentText(DateTime moment) =>
        DateTime.SpecifyKind(moment, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>The moment <paramref name="text"/> holds, exactly as <see cref="MomentText"/> writes one in UTC; null for anything else.</summary>
    internal static DateTime? Moment(string? text) =>
        text is not null
        && DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
        && moment.Kind == DateTimeKind.Utc
            ? moment
            : null;

    /// <summary>The letter a cell's ground is written as.</summary>
    private static char LetterOf(Terrain ground) => ground switch
    {
        Terrain.Grass => 'g',
        Terrain.Forest => 'f',
        Terrain.Water => 'w',
        Terrain.Rock => 'r',
        _ => throw new ArgumentOutOfRangeException(nameof(ground), ground, "not a ground"),
    };

    private static Terrain TerrainOf(char letter) => letter switch
    {
        'g' => Terrain.Grass,
        'f' => Terrain.Forest,
        'w' => Terrain.Water,
        'r' => Terrain.Rock,
        _ => throw new FormatException("not a ground's letter"),
    };

    private static string Flags(bool[] cells) => new(cells.Select(on => on ? '1' : '0').ToArray());

    /// <summary>A board's worth of letters, each one of <paramref name="alphabet"/>.</summary>
    private static bool Board(string? letters, string alphabet) =>
        letters is not null && letters.Length == Tuning.Cells && letters.All(alphabet.Contains);

    /// <summary>
    /// The name an enum value is saved as: its member's name, which a test pins. A
    /// value that is no member throws (a defect, found before it is written).
    /// </summary>
    private static string Name<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value.ToString() : throw new ArgumentOutOfRangeException(nameof(value), value, $"not a {typeof(T).Name}");

    private static bool IsName<T>(string? name) where T : struct, Enum => name is not null && Names<T>.ByName.ContainsKey(name);

    private static T Parse<T>(string name) where T : struct, Enum =>
        Names<T>.ByName.TryGetValue(name, out var value) ? value : throw new FormatException($"not a {typeof(T).Name}");

    /// <summary>
    /// An enum's members by their exact names. Exact on purpose: <c>Enum.TryParse</c>
    /// would also take "1", " Walk" or "walk", none of which the game ever writes.
    /// </summary>
    private static class Names<T> where T : struct, Enum
    {
        public static readonly Dictionary<string, T> ByName =
            Enum.GetValues<T>().ToDictionary(v => v.ToString(), v => v, StringComparer.Ordinal);
    }

    /// <summary>A species exactly as the table spells it (H36): the records, the lists and the run are keyed so.</summary>
    private static bool IsSpecies(string? name) =>
        SpeciesTable.TryGet(name, out var species) && string.Equals(species.Name, name, StringComparison.Ordinal);

    private static bool IsPredator(string? name) => name is not null && PredatorsByName.ContainsKey(name);

    private static PredatorType PredatorOf(string name) =>
        PredatorsByName.TryGetValue(name, out var type) ? type : throw new FormatException("not a predator");

    /// <summary>The predators by their species' names, as the save writes them (<see cref="Predators.NameOf"/>).</summary>
    private static readonly Dictionary<string, PredatorType> PredatorsByName =
        Enum.GetValues<PredatorType>().ToDictionary(Predators.NameOf, t => t, StringComparer.Ordinal);

    private static bool IsInstinct(string? id) => Hatchling.Instincts.TryParse(id, out _);

    private static Instinct InstinctOf(string id) =>
        Hatchling.Instincts.TryParse(id, out var instinct) ? instinct : throw new FormatException("not an instinct");

    private static Trait TraitOf(string id) =>
        Lineage.TryParse(id, out var trait) ? trait : throw new FormatException("not a trait");

    private static bool IsDanger(int danger) => danger >= Dangers.Lowest && danger <= Dangers.Highest;

    private static bool IsCell(int cell) => cell >= 0 && cell < Tuning.Cells;

    /// <summary>A growth, 0 to <see cref="Tuning.GrowthMost"/> (100): a run's, and a species' best.</summary>
    private static bool IsGrowth(double value) => value >= 0 && value <= Tuning.GrowthMost;

    private static bool Distinct(List<string> items) => items.Distinct(StringComparer.Ordinal).Count() == items.Count;
}
