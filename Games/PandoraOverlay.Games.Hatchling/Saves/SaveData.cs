namespace PandoraOverlay.Games.Hatchling;

// The save's shape, version 1 (A9): what Hatchling's payload text is, property by
// property. It exists apart from the game's own classes (GameState, RunState and
// the rest) for one reason: those may be renamed, split or retyped as the game
// grows, and a save must mean the same thing for ever. So the game's classes are
// copied into this plain shape and back (HatchlingSave.From and To), and only this
// shape is ever turned into text.
//
// THE NAMES BELOW ARE THE SAVE'S FORMAT FOR EVER. A property's name is a key in
// every player's save: renaming one, changing its type or its unit, or splitting
// it, strands every save that holds it unless the version rises and an upgrade
// step carries the old key over (A9). A property ADDED with a sound default needs
// no step: the default below is what an older save reads as. The whole format is
// pinned by a test (the saves' tests' "the format is pinned"), so a rename fails
// there before it strands anyone.
//
// Two rules of the engine's save schema, kept by every class here (A51):
// - every saved property has a public setter, so the reader can set it back: a
//   private setter is written and never read, and the value is silently lost at
//   the next launch;
// - only simple types: numbers, strings, booleans, lists, dictionaries with string
//   keys, and the classes below. A generator is its state, a uint, never an Rng
//   (the reader can't build one); a set is a list; an enum is its name; a moment
//   is ISO text in UTC.
//
// How things are written, each chosen once:
// - traits and instincts by their ids (Lineage, Instincts: "stomach", "eyes"),
//   which are stable whatever the C# names become;
// - species as the table spells them (H36); a predator by its species' name
//   (Predators.NameOf), which is also what RunState.Arrived is keyed by (H38);
// - every other enum by its C# member's name ("Sneak", "Caught", "Hit"), which a
//   test pins: a member renamed is a save stranded;
// - a run's ground as 121 letters (g grass, f forest, w water, r rock), its seen
//   cells and its plants as 121 letters of 0 and 1, its meat as 121 numbers;
// - the wallet as two numbers, Balance and Earned, never the currency's word in a
//   name (A24: the word lives in one source file, and a save outlives a rename);
// - achievements by their ids, an id this build does not know kept as it is (the
//   engine's rule: a later version may bring it back).
//
// Nothing here is ever shown: these classes are a format, not a model. The game
// never reads them but in HatchlingSave.To.

/// <summary>
/// Hatchling's save, version 1: everything the game keeps between runs and the
/// run itself while one exists (G14: reopening shows where you left off, mid-run
/// included). The plain copy of <see cref="GameState"/>; see the file's notes for
/// the rules of its shape.
/// </summary>
internal sealed class SaveData
{
    /// <summary>The wallet's balance (G3: the game's one wallet).</summary>
    public long Balance { get; set; }

    /// <summary>The wallet's lifetime earnings.</summary>
    public long Earned { get; set; }

    /// <summary>The fraction of one unit of pay server play still owes, 0 to under 1.</summary>
    public double Carry { get; set; }

    /// <summary>The lineage: a trait's id to its rank (H11); a trait not here is at rank 0.</summary>
    public Dictionary<string, int> Ranks { get; set; } = new();

    /// <summary>The species unlocked, in the order they were.</summary>
    public List<string> Unlocked { get; set; } = new();

    /// <summary>How many of them were bought (H10).</summary>
    public int Bought { get; set; }

    /// <summary>The species chosen on Camp.</summary>
    public string Selected { get; set; } = "";

    /// <summary>The danger chosen for the next run, 1 to 5.</summary>
    public int Danger { get; set; } = Dangers.Lowest;

    /// <summary>The highest danger unlocked, 1 to 5 (H15).</summary>
    public int MaxDanger { get; set; } = Dangers.Lowest;

    /// <summary>The playback plays twice as quick (H5).</summary>
    public bool Fast { get; set; }

    /// <summary>The records by species, keyed as the table spells them (H36).</summary>
    public Dictionary<string, SavedSpecies> Species { get; set; } = new();

    /// <summary>The moment freshness was last brought up to date (H18), ISO 8601 in UTC: "2026-11-14T10:00:00.0000000Z".</summary>
    public string FreshAsOf { get; set; } = "";

    /// <summary>The field journal (H12).</summary>
    public SavedJournal Journal { get; set; } = new();

    /// <summary>The achievements' ids, in the order earned (G22), unknown ones kept.</summary>
    public List<string> Achievements { get; set; } = new();

    /// <summary>Counts over every run.</summary>
    public SavedTotals Totals { get; set; } = new();

    /// <summary>The next run's seed (H25).</summary>
    public uint NextSeed { get; set; }

    /// <summary>The last lines server play wrote, newest first.</summary>
    public List<SavedLine> Lately { get; set; } = new();

    /// <summary>The run, under way or ended and not yet left (H20); null on Camp.</summary>
    public SavedRun? Run { get; set; }
}

/// <summary>What the game keeps about one species (<see cref="SpeciesRecord"/>).</summary>
internal sealed class SavedSpecies
{
    /// <summary>Percent of seen growth over every life.</summary>
    public double Grown { get; set; }

    /// <summary>The server pay's multiplier, 1.0 to 3.0; a species with no record is as fresh as can be.</summary>
    public double Fresh { get; set; } = Tuning.FreshMax;

    /// <summary>Times a seen step crossed Adult.</summary>
    public int Adults { get; set; }

    /// <summary>Runs played with it and settled.</summary>
    public int Runs { get; set; }

    /// <summary>The most growth one of its runs reached, a percent.</summary>
    public double BestGrowth { get; set; }

    /// <summary>The highest danger won at; 0 for none.</summary>
    public int WonAt { get; set; }
}

/// <summary>The field journal (<see cref="JournalState"/>).</summary>
internal sealed class SavedJournal
{
    /// <summary>The open entries, three once filled.</summary>
    public List<SavedEntry> Open { get; set; } = new();

    /// <summary>Entries finished, in all.</summary>
    public int Done { get; set; }

    /// <summary>The journal's own generator's state (the engine's Rng.State).</summary>
    public uint Rng { get; set; }
}

/// <summary>One open entry of the journal (<see cref="JournalEntry"/>).</summary>
internal sealed class SavedEntry
{
    /// <summary>The species asked for.</summary>
    public string Species { get; set; } = "";

    /// <summary>Percent of seen growth asked for.</summary>
    public double Target { get; set; }

    /// <summary>What finishing it pays.</summary>
    public int Reward { get; set; }

    /// <summary>Percent of seen growth counted so far.</summary>
    public double Seen { get; set; }
}

/// <summary>Counts over every run (<see cref="Hatchling.Totals"/>).</summary>
internal sealed class SavedTotals
{
    public int Runs { get; set; }
    public int Wins { get; set; }
    public int Kills { get; set; }

    /// <summary>The builds a run has been won with, by name, in the enum's order (a set, written as a list).</summary>
    public List<string> BuildsWon { get; set; } = new();

    /// <summary>The species a run has been won with, in ordinal order (a set, written as a list).</summary>
    public List<string> SpeciesWon { get; set; } = new();

    public int RanksBought { get; set; }
}

/// <summary>One line of what server play wrote lately (<see cref="LatelyLine"/>).</summary>
internal sealed class SavedLine
{
    /// <summary>The kind of line, by name: Grew, Star, Unlocked, Journal.</summary>
    public string Kind { get; set; } = "";

    public string Species { get; set; } = "";
    public double Percent { get; set; }
    public double Fresh { get; set; }
    public int Amount { get; set; }
    public int Target { get; set; }
}

/// <summary>A run: everything <see cref="RunState"/> holds (G14, H19, H28).</summary>
internal sealed class SavedRun
{
    public string Species { get; set; } = "";
    public int Danger { get; set; } = Dangers.Lowest;

    /// <summary>The lineage as the run began (H19): a trait's id to its rank.</summary>
    public Dictionary<string, int> Ranks { get; set; } = new();

    /// <summary>The species' stars as the run began (H19).</summary>
    public int Stars { get; set; }

    /// <summary>The seed the run began with.</summary>
    public uint Seed { get; set; }

    /// <summary>The run's generator's state now: a restored run rolls on exactly as it would have.</summary>
    public uint Rng { get; set; }

    public int Turn { get; set; }

    /// <summary>The island's ground, cell by cell: 121 letters of g, f, w and r.</summary>
    public string Tiles { get; set; } = "";

    /// <summary>The cells seen at any time this run: 121 letters of 0 and 1.</summary>
    public string Seen { get; set; } = "";

    /// <summary>The cells with a plant on them: 121 letters of 0 and 1.</summary>
    public string Plants { get; set; } = "";

    /// <summary>Meals of carcass on each cell: 121 numbers.</summary>
    public List<int> Meat { get; set; } = new();

    /// <summary>Every creature on the island, in id order.</summary>
    public List<SavedCreature> Creatures { get; set; } = new();

    public int NextId { get; set; } = 1;

    /// <summary>A predator species' name to how many of it have arrived in this run, the defeated included (H38).</summary>
    public Dictionary<string, int> Arrived { get; set; } = new();

    /// <summary>The pace, by name: Walk, Sneak, Sprint.</summary>
    public string Pace { get; set; } = "";

    public SavedYou You { get; set; } = new();

    /// <summary>The instincts picked, by id, in order.</summary>
    public List<string> Instincts { get; set; } = new();

    /// <summary>The instincts offered now, by id; null when no offer is open.</summary>
    public List<string>? Offer { get; set; }

    public int PicksOwed { get; set; }
    public int Redraws { get; set; }
    public bool FreeRedrawUsed { get; set; }

    /// <summary>The turn the apex came, or -1.</summary>
    public int ApexAt { get; set; } = Tuning.ApexNotYet;

    public int Kills { get; set; }

    /// <summary>How the run ended; null while it goes on.</summary>
    public SavedOver? Over { get; set; }

    /// <summary>The turn list's rows as last shown, as codes (H28).</summary>
    public List<SavedRow> LastTurn { get; set; } = new();
}

/// <summary>A creature on the island (<see cref="Creature"/>).</summary>
internal sealed class SavedCreature
{
    public int Id { get; set; }

    /// <summary>By name: Predator, SmallGame.</summary>
    public string Kind { get; set; } = "";

    /// <summary>A predator's species' name (small game keeps the default's, meaning nothing).</summary>
    public string Type { get; set; } = "";

    public int Ordinal { get; set; }
    public int At { get; set; }
    public int Health { get; set; }
    public int Alert { get; set; }

    /// <summary>The cell it heads for, or -1 for none (never 0, a real cell).</summary>
    public int Target { get; set; } = Tuning.NoTarget;

    public bool Relentless { get; set; }
    public bool SawYou { get; set; }
    public bool Dazed { get; set; }
    public int DazeFreeAt { get; set; }
    public int Slowed { get; set; }
    public int Bleed { get; set; }
    public int BleedDamage { get; set; }
    public bool HitByYou { get; set; }
}

/// <summary>Your dino in the run (<see cref="Hatchling.You"/>), the "unseen" crossing included (H46).</summary>
internal sealed class SavedYou
{
    public int At { get; set; }
    public double Growth { get; set; }
    public double Start { get; set; }
    public double Best { get; set; }
    public double Health { get; set; }
    public double Food { get; set; }
    public double Water { get; set; }
    public bool Wind { get; set; }

    /// <summary>The kind of your last move, by name.</summary>
    public string LastMove { get; set; } = "";

    public int LatchOn { get; set; }
    public int LatchStreak { get; set; }
    public bool Bitten { get; set; }
    public bool AdultUnbitten { get; set; }
}

/// <summary>How a run ended and what it paid (<see cref="RunOver"/>).</summary>
internal sealed class SavedOver
{
    public bool Won { get; set; }

    /// <summary>By name: Caught, Hunger, Thirst, Left, Outlasted, DefeatedApex.</summary>
    public string Reason { get; set; } = "";

    /// <summary>The predator that caught you, by its species' name; meaningless for any other end.</summary>
    public string ByType { get; set; } = "";

    public int ByOrdinal { get; set; }
    public int Stage { get; set; }
    public List<SavedPay> Lines { get; set; } = new();
    public int Total { get; set; }

    /// <summary>The ids of the achievements the run earned, in the order granted.</summary>
    public List<string> Earned { get; set; } = new();

    public int DangerUnlocked { get; set; }
    public bool Settled { get; set; }
}

/// <summary>One line of a run's pay (<see cref="PayLine"/>).</summary>
internal sealed class SavedPay
{
    /// <summary>By name: Grew, Reached, Defeated, Outlasted, DefeatedApex, Danger, Legacy.</summary>
    public string Kind { get; set; } = "";

    public int Value { get; set; }
    public int Amount { get; set; }
}

/// <summary>One row of the turn list as last shown (<see cref="TurnRow"/>), as codes (H28).</summary>
internal sealed class SavedRow
{
    /// <summary>By name: You, Predator, SmallGame, Unseen.</summary>
    public string Actor { get; set; } = "";

    /// <summary>A predator row's species' name.</summary>
    public string Type { get; set; } = "";

    public int Ordinal { get; set; }
    public int Alert { get; set; }

    /// <summary>The row's codes, by name (<see cref="RowCode"/>).</summary>
    public string First { get; set; } = "";

    public string Second { get; set; } = "";
    public int A { get; set; }
    public int B { get; set; }

    /// <summary>What a meal was, by name.</summary>
    public string Food { get; set; } = "";

    /// <summary>Your hit's target, when a predator: its species' name...</summary>
    public string TargetType { get; set; } = "";

    /// <summary>...and its number.</summary>
    public int TargetOrdinal { get; set; }

    public bool TargetIsSmallGame { get; set; }
}
