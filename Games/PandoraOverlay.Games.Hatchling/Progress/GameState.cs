using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

// What outlives a run: the lineage, the species unlocked, Camp's choices, what
// each species has been grown and played, the journal, the achievements, the
// totals, the next seed, and the run itself while one exists. All of it is in
// the save (the save's own copy in simple types is Saves/, task H08).
//
// The rules that change it are pure in the game's sense (A20): each takes a
// state, changes a Clone and returns that, and the state it was given reads as
// before. That is why every class here has a deep Clone, and why a test changes
// every member of a copy and reads the original back.

/// <summary>
/// What the game keeps about one species. A species with no record is grown 0
/// and as fresh as can be (<see cref="Tuning.FreshMax"/>): <see cref="GameState.Record"/>
/// answers such a default record without storing it, so a species nobody has
/// touched costs nothing in the save.
/// </summary>
internal sealed class SpeciesRecord
{
    /// <summary>Percent of growth SEEN on the server (A6), over every life: what the stars and the journal's lean read.</summary>
    public double Grown { get; set; }

    /// <summary>The server pay's multiplier for this species, <see cref="Tuning.FreshFloor"/> to <see cref="Tuning.FreshMax"/> (H18); brought up to date by the calendar, never shown as a wait.</summary>
    public double Fresh { get; set; } = Tuning.FreshMax;

    /// <summary>Times a seen step crossed Adult on the server (the "diverse" achievement counts species with one or more).</summary>
    public int Adults { get; set; }

    /// <summary>Runs played with it and settled.</summary>
    public int Runs { get; set; }

    /// <summary>The most growth reached in one of its runs, a percent, 0 to 100.</summary>
    public double BestGrowth { get; set; }

    /// <summary>The highest danger won at with it; 0 for none (H15: a species' card shows it).</summary>
    public int WonAt { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public SpeciesRecord Clone() => (SpeciesRecord)MemberwiseClone();   // every member is a value
}

/// <summary>
/// One open entry of the field journal (H12): "grow a Maiasaura by 75%", counted in
/// seen growth of that species from the moment it was drawn, over any number of
/// lives. Drawn and counted by the journal's own rules (task H06).
/// </summary>
internal sealed class JournalEntry
{
    /// <summary>The species asked for, as <see cref="SpeciesTable"/> spells it (H36).</summary>
    public string Species { get; set; } = "";

    /// <summary>Percent of seen growth asked for (<see cref="Tuning.JournalTarget"/>, or <see cref="Tuning.JournalBigTarget"/> for a big build).</summary>
    public double Target { get; set; }

    /// <summary>What finishing it pays into the game's wallet.</summary>
    public int Reward { get; set; }

    /// <summary>Percent of seen growth counted so far; the entry is done at <see cref="Target"/>.</summary>
    public double Seen { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public JournalEntry Clone() => (JournalEntry)MemberwiseClone();   // every member is a value
}

/// <summary>The field journal (H12): the open entries, how many were finished, and its own generator.</summary>
internal sealed class JournalState
{
    /// <summary>The open entries, <see cref="Tuning.JournalEntries"/> once the journal has been filled.</summary>
    public List<JournalEntry> Open { get; set; } = new();

    /// <summary>Entries finished, in all (the "researcher" and "naturalist" achievements count them).</summary>
    public int Done { get; set; }

    /// <summary>
    /// The journal's generator's state (the engine's <c>Rng.State</c>): its draws
    /// are its own stream, so a run's rolls never move them and they never move a
    /// run's.
    /// </summary>
    public uint Rng { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public JournalState Clone()
    {
        var copy = (JournalState)MemberwiseClone();
        copy.Open = Open.Select(e => e.Clone()).ToList();
        return copy;
    }
}

/// <summary>Counts over every run, for Records and for the achievements that count across runs.</summary>
internal sealed class Totals
{
    /// <summary>Runs settled.</summary>
    public int Runs { get; set; }

    /// <summary>Runs won (outlasted or defeated the apex).</summary>
    public int Wins { get; set; }

    /// <summary>Predators defeated over all runs, the apex not counted (as a run's own count).</summary>
    public int Kills { get; set; }

    /// <summary>The builds a run has been won with ("shapes": all nine).</summary>
    public HashSet<Build> BuildsWon { get; set; } = new();

    /// <summary>The species a run has been won with, as the table spells them (H36; "all": all 21).</summary>
    public HashSet<string> SpeciesWon { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Ranks of traits bought, in all ("roots": ten).</summary>
    public int RanksBought { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public Totals Clone()
    {
        var copy = (Totals)MemberwiseClone();
        copy.BuildsWon = new HashSet<Build>(BuildsWon);
        copy.SpeciesWon = new HashSet<string>(SpeciesWon, StringComparer.Ordinal);
        return copy;
    }
}

/// <summary>What a line of the Journal page's "on the server lately" list says.</summary>
internal enum LatelyKind
{
    /// <summary>A species grew: one line that grows while it is the newest.</summary>
    Grew,
    /// <summary>A species earned a star.</summary>
    Star,
    /// <summary>A species was unlocked by growing it to Adult.</summary>
    Unlocked,
    /// <summary>A journal entry was finished.</summary>
    Journal,
}

/// <summary>
/// One line of what server play wrote lately, kept as codes and numbers, never as
/// text: <see cref="HatchlingText"/> turns it into words (A24). Written by server
/// play (task H06); the members a kind does not use stay 0.
/// </summary>
/// <param name="Species">As the table spells it (H36).</param>
/// <param name="Percent">A growth line's percent so far.</param>
/// <param name="Fresh">A growth line's multiplier at its first step.</param>
/// <param name="Amount">What the line paid so far.</param>
/// <param name="Target">A journal line's target, in percent.</param>
internal sealed record LatelyLine(LatelyKind Kind, string Species, double Percent, double Fresh, int Amount, int Target);

/// <summary>
/// Everything Hatchling keeps between runs, and the run while one exists: the
/// plan's table of what is kept, one member each. A plain mutable class: the rules
/// in <c>Progress/</c> change a <see cref="Clone"/> and return it.
///
/// <para><b>One wallet (G3).</b> <see cref="Wallet"/> is this game's only one, and
/// nothing else in the game holds one: everything the game pays goes into it and
/// everything it sells comes out of it, and no function here takes a second (a
/// test holds both by reflection). The engine's wallet has no way to move its
/// content anywhere else.</para>
/// </summary>
internal sealed class GameState
{
    /// <summary>The game's one wallet (G3): its balance and its lifetime earnings.</summary>
    public Wallet Wallet { get; set; } = new();

    /// <summary>The fraction of one unit of pay that server play still owes, 0 to under 1 (task H06).</summary>
    public double Carry { get; set; }

    /// <summary>The lineage: trait to rank, a trait missing at 0 (H11).</summary>
    public Dictionary<Trait, int> Ranks { get; set; } = new();

    /// <summary>The species unlocked, in the order they were, as the table spells them (H36).</summary>
    public List<string> Unlocked { get; set; } = new();

    /// <summary>How many of <see cref="Unlocked"/> were bought, which sets the next one's price (H10).</summary>
    public int Bought { get; set; }

    /// <summary>The species chosen on Camp, as the table spells it: any of the 21, a locked one too.</summary>
    public string Selected { get; set; } = "";

    /// <summary>The danger chosen for the next run, 1 to <see cref="MaxDanger"/>.</summary>
    public int Danger { get; set; } = Dangers.Lowest;

    /// <summary>The highest danger unlocked, 1 to 5 (H15): a win at a danger unlocks the next.</summary>
    public int MaxDanger { get; set; } = Dangers.Lowest;

    /// <summary>The playback's pace: Fast plays twice as quick (H5).</summary>
    public bool Fast { get; set; }

    /// <summary>The records by species, keyed as the table spells them (H36); a species with none is grown 0 and fresh.</summary>
    public Dictionary<string, SpeciesRecord> Species { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The moment (UTC) freshness was last brought up to date (H18): set back by nothing, so a clock turned back gains nothing.</summary>
    public DateTime FreshAsOf { get; set; }

    /// <summary>The field journal (H12).</summary>
    public JournalState Journal { get; set; } = new();

    /// <summary>The engine's achievements over <see cref="AchievementList.All"/> (G22): the ids earned, in order.</summary>
    public Achievements Achievements { get; set; } = new(AchievementList.All);

    /// <summary>Counts over every run.</summary>
    public Totals Totals { get; set; } = new();

    /// <summary>The next run's seed: moved by <see cref="Tuning.SeedStep"/> after each settled run.</summary>
    public uint NextSeed { get; set; }

    /// <summary>The last lines server play wrote, newest first (task H06).</summary>
    public List<LatelyLine> Lately { get; set; } = new();

    /// <summary>The run, under way or ended and not yet left; null on Camp (H20).</summary>
    public RunState? Run { get; set; }

    /// <summary>
    /// A deep copy: no list, dictionary, set or object is shared with this one,
    /// the wallet and the achievements included (each rebuilt from its numbers and
    /// its list), so a rule can change the copy freely. The records inside lists
    /// (<see cref="LatelyLine"/>) and the achievement definitions are immutable and
    /// are shared.
    /// </summary>
    public GameState Clone()
    {
        var copy = (GameState)MemberwiseClone();
        copy.Wallet = new Wallet(Wallet.Balance, Wallet.Earned);
        copy.Ranks = new Dictionary<Trait, int>(Ranks);
        copy.Unlocked = new List<string>(Unlocked);
        copy.Species = Species.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal);
        copy.Journal = Journal.Clone();
        copy.Achievements = new Achievements(AchievementList.All, Achievements.Unlocked);
        copy.Totals = Totals.Clone();
        copy.Lately = new List<LatelyLine>(Lately);
        copy.Run = Run?.Clone();
        return copy;
    }

    /// <summary>
    /// A fresh save (H25). Its first seeds come from the calendar, so two players
    /// do not get the same islands in the same order; the tests' clock is fixed, so
    /// theirs are the same every time. <c>seed0</c> is the low 32 bits of
    /// <paramref name="utcNow"/>'s ticks xor its high 32 bits; the journal's
    /// generator starts at <c>Rng.Mix(seed0, 1)</c> and the first run's seed is
    /// <c>Rng.Mix(seed0, 2)</c>, two streams that never meet. The two starters are
    /// unlocked, the first of them chosen, danger 1 of 1, and freshness up to date
    /// at <paramref name="utcNow"/>. The moment is handed in: the game reads no
    /// clock of its own (A29).
    /// </summary>
    public static GameState Fresh(DateTime utcNow)
    {
        var seed0 = Seed0(utcNow);
        var state = new GameState
        {
            Unlocked = new List<string>(SpeciesTable.Starters),
            Selected = SpeciesTable.Starters[0],
            Danger = Dangers.Lowest,
            MaxDanger = Dangers.Lowest,
            FreshAsOf = utcNow,
            Journal = new JournalState { Rng = Engine.Rng.Mix(seed0, JournalSalt) },
            NextSeed = Engine.Rng.Mix(seed0, RunSalt),
        };
        // The journal's three entries are drawn here by server play's own rule
        // (task H06), from the generator just set. Inside this class the bare name
        // Journal is the property, so the call is written Hatchling.Journal.Fill(state).
        Hatchling.Journal.Fill(state);
        return state;
    }

    /// <summary>The salt of the journal's stream from a fresh save's seed (H25).</summary>
    internal const uint JournalSalt = 1;

    /// <summary>The salt of the first run's seed from a fresh save's seed (H25).</summary>
    internal const uint RunSalt = 2;

    /// <summary>A fresh save's seed: the low 32 bits of the moment's ticks xor its high 32 bits (H25).</summary>
    internal static uint Seed0(DateTime utcNow)
    {
        unchecked
        {
            var ticks = (ulong)utcNow.Ticks;
            return (uint)ticks ^ (uint)(ticks >> 32);
        }
    }

    /// <summary>
    /// The record of <paramref name="species"/>, or, when it has none, a new
    /// default one (grown 0, fresh <see cref="Tuning.FreshMax"/>) that is NOT
    /// stored: a rule that wants to change a record stores it itself. The name is
    /// found ignoring case and read in the table's spelling (H36).
    /// </summary>
    public SpeciesRecord Record(string species) =>
        Species.TryGetValue(KeyOf(species), out var record) ? record : new SpeciesRecord();

    /// <summary>
    /// The stored record of <paramref name="species"/>, made and stored when it has
    /// none: for a rule changing a clone. Keyed as the table spells the species
    /// (H36); a name the table does not know is kept as given.
    /// </summary>
    internal SpeciesRecord RecordToChange(string species)
    {
        var key = KeyOf(species);
        if (!Species.TryGetValue(key, out var record))
        {
            record = new SpeciesRecord();
            Species[key] = record;
        }
        return record;
    }

    /// <summary>The trait's rank in the lineage; 0 when it has none.</summary>
    public int Rank(Trait trait) => Ranks.TryGetValue(trait, out var rank) ? rank : 0;

    /// <summary>
    /// The species' stars: one for each line of <see cref="Tuning.StarsAt"/> its seen
    /// growth has reached, 0 to 3. A star is power in runs (5% health and attack),
    /// and it comes only from growing the species on the server (G6), never from an
    /// achievement (G22).
    /// </summary>
    public int Stars(string species)
    {
        var grown = Record(species).Grown;
        var stars = 0;
        foreach (var line in Tuning.StarsAt)
            if (grown + StarSlack >= line) stars++;
        return stars;
    }

    /// <summary>
    /// What growth summed from many small seen steps may fall short of a star's line
    /// by and still reach it: a thousand steps of 0.1% are 99.99999999999 in floating
    /// point, not 100. Far below any step the server can show.
    /// </summary>
    internal const double StarSlack = 1e-9;

    /// <summary>
    /// Nothing has been played, earned, bought or grown: what a fresh save is, and
    /// what the tile's "anything to reset" is the reverse of. Camp's choices (the
    /// species, the danger, the pace) and the journal's open entries are not
    /// progress, so they do not count; a run under way does.
    /// </summary>
    public bool IsFresh =>
        Run is null
        && Wallet.Balance == 0 && Wallet.Earned == 0 && Carry == 0
        && Bought == 0 && Ranks.Values.All(r => r == 0)
        && Unlocked.Count == SpeciesTable.Starters.Count && SpeciesTable.Starters.All(Unlocked.Contains)
        && MaxDanger == Dangers.Lowest
        && Species.Values.All(r => r.Grown == 0 && r.Adults == 0 && r.Runs == 0)
        && Journal.Done == 0 && Journal.Open.All(e => e.Seen == 0)
        && Achievements.Unlocked.Count == 0
        && Totals.Runs == 0 && Totals.RanksBought == 0
        && Lately.Count == 0;

    /// <summary>
    /// What a run of <paramref name="species"/> at <paramref name="danger"/> begins
    /// with: the lineage's ranks and the species' stars AS THEY ARE NOW, copied, so
    /// a trait bought or a star earned during the run counts from the next one (H19).
    /// The species is written as the table spells it (H36). An unknown species or a
    /// danger outside 1 to 5 throws: Camp lets neither through, so either is a
    /// defect.
    /// </summary>
    public RunSetup SetupFor(string species, int danger)
    {
        if (!SpeciesTable.TryGet(species, out var info))
            throw new ArgumentException($"No species is named \"{species}\".", nameof(species));
        if (danger < Dangers.Lowest || danger > Dangers.Highest)
            throw new ArgumentOutOfRangeException(nameof(danger), danger, "not a danger");
        var ranks = Ranks.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
        return new RunSetup(info.Name, danger, ranks, Stars(info.Name));
    }

    /// <summary>The key a species' record is kept under: the table's spelling, or the name as given when the table has none.</summary>
    private static string KeyOf(string species) =>
        SpeciesTable.TryGet(species, out var info) ? info.Name : species;
}
