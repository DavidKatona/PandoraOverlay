namespace PandoraOverlay.Games.Hatchling;

/// <summary>A cell's ground.</summary>
internal enum Terrain { Grass, Forest, Water, Rock }

/// <summary>How you move: chosen once and kept, a choice that takes no turn.</summary>
internal enum Pace { Walk, Sneak, Sprint }

/// <summary>The kind of your last move: Braced asks whether you moved, Freeze whether you rested.</summary>
internal enum MoveKind { None, Walk, Sneak, Sprint, Eat, Drink, Rest, Attack }

/// <summary>What a creature on the island is.</summary>
internal enum CreatureKind { Predator, SmallGame }

/// <summary>How a run ended. Never "died": a run is caught, runs out, is left, or is won.</summary>
internal enum EndReason { Caught, Hunger, Thirst, Left, Outlasted, DefeatedApex }

/// <summary>
/// What a run is started with: the species, the danger, and the lineage's
/// ranks and the species' stars AS THEY ARE at the start (H19). The run keeps
/// this snapshot, so a trait bought during a run counts from the next one.
/// </summary>
internal sealed record RunSetup(string Species, int Danger, IReadOnlyDictionary<Trait, int> Ranks, int Stars);

/// <summary>A creature on the island: a predator or small game.</summary>
internal sealed class Creature
{
    /// <summary>From 1, in the order they arrived: the order predators act in (H1), and how events name them.</summary>
    public int Id { get; set; }
    public CreatureKind Kind { get; set; }
    /// <summary>A predator's species; for small game it means nothing and stays the default.</summary>
    public PredatorType Type { get; set; }
    /// <summary>A predator's number among its own species in this run, from 1 as they arrive: "Carnotaurus 2" for its whole life (H7).</summary>
    public int Ordinal { get; set; }
    /// <summary>Its cell.</summary>
    public int At { get; set; }
    /// <summary>Health in points (the threat already applied); at 0 or less it is defeated.</summary>
    public int Health { get; set; }
    /// <summary>0 unaware; 1 to 3 searching (it heard you, one less a turn); 4 hunting (it sees you, or is the apex).</summary>
    public int Alert { get; set; }
    /// <summary>The cell it heads for while alerted.</summary>
    public int Target { get; set; }
    /// <summary>It never gives up: the apex.</summary>
    public bool Relentless { get; set; }
    /// <summary>It saw you (or is the apex) on its last action: what makes "spots you" news only once.</summary>
    public bool SawYou { get; set; }
    /// <summary>It skips its next action (Headbutt).</summary>
    public bool Dazed { get; set; }
    /// <summary>The turn from which it can be dazed again (Headbutt's 3 turns).</summary>
    public int DazeFreeAt { get; set; }
    /// <summary>Its next actions, this many, take one step less (Venom).</summary>
    public int Slowed { get; set; }
    /// <summary>Its next actions, this many, lose <see cref="BleedDamage"/> each (Bleeder).</summary>
    public int Bleed { get; set; }
    /// <summary>What each bleeding action takes.</summary>
    public int BleedDamage { get; set; }
    /// <summary>You have hit it before: Charge doubles only a first hit.</summary>
    public bool HitByYou { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public Creature Clone() => (Creature)MemberwiseClone();   // every field is a value
}

/// <summary>Your dino in the run.</summary>
internal sealed class You
{
    /// <summary>Your cell.</summary>
    public int At { get; set; }
    /// <summary>Growth, a percent, 0 to 100.</summary>
    public double Growth { get; set; }
    /// <summary>Growth when the run began: a run pays only for growth gained in it (H8).</summary>
    public double Start { get; set; }
    /// <summary>The most growth reached in the run.</summary>
    public double Best { get; set; }
    /// <summary>A fraction of the maximum, 0 to 1, so Hardy raises the maximum and keeps the fraction.</summary>
    public double Health { get; set; }
    /// <summary>0 to 100.</summary>
    public double Food { get; set; }
    /// <summary>0 to 100.</summary>
    public double Water { get; set; }
    /// <summary>Second wind is still to be spent this run.</summary>
    public bool Wind { get; set; }
    /// <summary>The kind of your last move.</summary>
    public MoveKind LastMove { get; set; }
    /// <summary>The creature your Latch streak is on; 0 for none.</summary>
    public int LatchOn { get; set; }
    /// <summary>Hits in a row already landed on <see cref="LatchOn"/>, turn after turn.</summary>
    public int LatchStreak { get; set; }
    /// <summary>A bite has landed on you this run (the "unseen" achievement asks).</summary>
    public bool Bitten { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public You Clone() => (You)MemberwiseClone();   // every field is a value
}

/// <summary>What a line of a run's pay is for.</summary>
internal enum PayKind { Grew, Reached, Defeated, Outlasted, DefeatedApex, Danger, Legacy }

/// <summary>One line of a run's pay.</summary>
/// <param name="Value">What the line counts: the percent grown, the stage reached, the predators defeated, the danger, the percent of Legacy; 0 for a win's line.</param>
/// <param name="Amount">What it paid.</param>
internal sealed record PayLine(PayKind Kind, int Value, int Amount);

/// <summary>How a run ended and what it paid: kept with the run until the player leaves its summary.</summary>
internal sealed class RunOver
{
    public bool Won { get; set; }
    public EndReason Reason { get; set; }
    /// <summary>The predator that caught you (its species and ordinal); meaningless for any other reason.</summary>
    public PredatorType ByType { get; set; }
    public int ByOrdinal { get; set; }
    /// <summary>Your stage at the end, 0 Hatchling to 4 Elder.</summary>
    public int Stage { get; set; }
    /// <summary>The pay lines, in the order RunPay lists them.</summary>
    public List<PayLine> Lines { get; set; } = new();
    /// <summary>The sum of the lines.</summary>
    public int Total { get; set; }
    /// <summary>The ids of the achievements the run earned, in the order granted.</summary>
    public List<string> Earned { get; set; } = new();
    /// <summary>The danger the win unlocked; 0 for none.</summary>
    public int DangerUnlocked { get; set; }
    /// <summary>The pay is banked and the records follow: settled in the same step that ends the run.</summary>
    public bool Settled { get; set; }

    /// <summary>A copy that shares nothing with this one.</summary>
    public RunOver Clone()
    {
        var copy = (RunOver)MemberwiseClone();
        copy.Lines = new List<PayLine>(Lines);     // the lines themselves are immutable records
        copy.Earned = new List<string>(Earned);
        return copy;
    }
}

/// <summary>
/// Everything a run is, all of it saved, so a run reopened
/// is exactly where it was (G14). A plain mutable class with no behaviour: a rule
/// never changes the state it was given, it changes a <see cref="Clone"/> and
/// returns that.
/// </summary>
internal sealed class RunState
{
    /// <summary>The species played, as <see cref="SpeciesTable"/> spells it.</summary>
    public string Species { get; set; } = "";
    /// <summary>1 to 5.</summary>
    public int Danger { get; set; } = Dangers.Lowest;
    /// <summary>The lineage when the run began (H19): trait to rank, a missing trait at 0.</summary>
    public Dictionary<Trait, int> Ranks { get; set; } = new();
    /// <summary>The species' stars when the run began (H19).</summary>
    public int Stars { get; set; }
    /// <summary>The seed the run began with, for the log and for bug reports.</summary>
    public uint Seed { get; set; }
    /// <summary>The run's generator's state now (the engine's <c>Rng.State</c>): every roll of the run comes from it.</summary>
    public uint Rng { get; set; }
    /// <summary>Turns passed.</summary>
    public int Turn { get; set; }
    /// <summary>The island's ground, cell by cell.</summary>
    public Terrain[] Tiles { get; set; } = new Terrain[Tuning.Cells];
    /// <summary>The cells that have been in sight at any time this run.</summary>
    public bool[] Seen { get; set; } = new bool[Tuning.Cells];
    /// <summary>The cells with a plant on them.</summary>
    public bool[] Plants { get; set; } = new bool[Tuning.Cells];
    /// <summary>Meals of carcass on each cell.</summary>
    public int[] Meat { get; set; } = new int[Tuning.Cells];
    /// <summary>Every creature alive on the island, in id order.</summary>
    public List<Creature> Creatures { get; set; } = new();
    /// <summary>The next creature's id.</summary>
    public int NextId { get; set; } = 1;
    /// <summary>The pace chosen.</summary>
    public Pace Pace { get; set; }
    /// <summary>Your dino.</summary>
    public You You { get; set; } = new();
    /// <summary>The instincts picked, in order.</summary>
    public List<Instinct> Instincts { get; set; } = new();
    /// <summary>The instincts offered now, or null when no offer is open.</summary>
    public List<Instinct>? Offer { get; set; }
    /// <summary>Offers still owed, the open one included.</summary>
    public int PicksOwed { get; set; }
    /// <summary>The paid redraws so far this run: the next costs twice the last.</summary>
    public int Redraws { get; set; }
    /// <summary>Second thoughts' free redraw is spent.</summary>
    public bool FreeRedrawUsed { get; set; }
    /// <summary>The turn the apex came, or <see cref="Tuning.ApexNotYet"/>.</summary>
    public int ApexAt { get; set; } = Tuning.ApexNotYet;
    /// <summary>Predators defeated, the apex not counted.</summary>
    public int Kills { get; set; }
    /// <summary>Null while the run goes on; how it ended once it has.</summary>
    public RunOver? Over { get; set; }
    /// <summary>The turn list's rows as last shown, as codes, so a reopened run shows the same list (H28).</summary>
    public List<TurnRow> LastTurn { get; set; } = new();

    /// <summary>
    /// A deep copy: no array, list, dictionary or object is shared with this one,
    /// so a rule can change the copy freely. The records inside lists
    /// (<see cref="TurnRow"/>, <see cref="PayLine"/>) are immutable and are shared.
    /// </summary>
    public RunState Clone()
    {
        var copy = (RunState)MemberwiseClone();
        copy.Ranks = new Dictionary<Trait, int>(Ranks);
        copy.Tiles = (Terrain[])Tiles.Clone();
        copy.Seen = (bool[])Seen.Clone();
        copy.Plants = (bool[])Plants.Clone();
        copy.Meat = (int[])Meat.Clone();
        copy.Creatures = Creatures.Select(c => c.Clone()).ToList();
        copy.You = You.Clone();
        copy.Instincts = new List<Instinct>(Instincts);
        copy.Offer = Offer is null ? null : new List<Instinct>(Offer);
        copy.Over = Over?.Clone();
        copy.LastTurn = new List<TurnRow>(LastTurn);
        return copy;
    }

    /// <summary>The trait's rank in the run's snapshot of the lineage; 0 when absent.</summary>
    public int Rank(Trait trait) => Ranks.TryGetValue(trait, out var rank) ? rank : 0;

    /// <summary>The run has picked <paramref name="instinct"/>.</summary>
    public bool Has(Instinct instinct) => Instincts.Contains(instinct);
}

/// <summary>Who a row of the turn list is.</summary>
internal enum RowActor { You, Predator, SmallGame, Unseen }

/// <summary>
/// What a row of the turn list says, as codes, never as text (H28): the save keeps
/// codes, and <see cref="HatchlingText.RowText"/> turns them into words, so no
/// other file builds a sentence.
/// </summary>
internal enum RowCode
{
    None,
    // you
    Hatched, Walked, Sneaked, Sprinted, Ate, Drank, Rested, Hit, Defeated,
    // a predator: what it noticed...
    Spotted, Heard, Lost,
    // ...and what it did
    Bit, Missed, Dazed, Held, Hunting, Searching, Wandered, Waited, Arrived, WasDefeated,
    // small game
    Scattered, Moved, Still,
    // out of sight
    Quiet, HeardYou,
}

/// <summary>
/// One row of the turn list as last shown: the turn script fills them, the save
/// keeps them, the Run page shows them.
/// </summary>
/// <param name="Type">A predator row's species (meaningless for the others).</param>
/// <param name="Ordinal">A predator row's number among its species (H7).</param>
/// <param name="Alert">A predator's alert at the end of the turn: the list colours its name by it.</param>
/// <param name="First">The first thing said: your move, what a predator noticed, small game's or out of sight's word. <see cref="RowCode.None"/> for nothing.</param>
/// <param name="Second">A predator's action, after what it noticed; else <see cref="RowCode.None"/>.</param>
/// <param name="A">The first number the codes need: a walk's or a sneak's noise, a sprint's cells, a meal's or a drink's gain, a rest's percent, a hit's or a bite's damage.</param>
/// <param name="B">The second: a sprint's noise.</param>
/// <param name="Food">What a meal was.</param>
/// <param name="TargetType">Your hit's or your defeat's target, when it was a predator: its species...</param>
/// <param name="TargetOrdinal">...and its number.</param>
/// <param name="TargetIsSmallGame">Your hit or your catch was on small game.</param>
internal sealed record TurnRow(RowActor Actor, PredatorType Type, int Ordinal, int Alert, RowCode First, RowCode Second,
    int A, int B, Food Food, PredatorType TargetType, int TargetOrdinal, bool TargetIsSmallGame);
