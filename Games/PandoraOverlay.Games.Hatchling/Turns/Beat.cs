namespace PandoraOverlay.Games.Hatchling;

// A beat is one moment of a turn's playback: what the board shows, for how many
// ticks, under which banner, and which row of the turn list it belongs to. It is
// DATA: no WPF type, no engine step. The view turns each beat into one step of its
// own and enqueues it by itself, never inside a Sequence or a Together (A35): the
// engine's 300-tick limit is per queued step, and a step that finishes at once
// still takes a tick, so a beat built of two engine steps would run a tick long.

/// <summary>What a beat shows. One kind for each row of the beat table (G9), plus the Gap between two actors.</summary>
internal enum BeatKind
{
    /// <summary>You walk, sneak or sprint, cell by cell (one kind: the pace is in the banner and the ticks).</summary>
    YouWalk,
    YouEat,
    YouDrink,
    YouRest,
    /// <summary>Your hit on the creature you attacked.</summary>
    YouStrike,
    /// <summary>Thagomizer's sweep: one more creature beside you, struck by the same blow.</summary>
    AlsoStruck,
    /// <summary>Nothing moves: the pause that tells one actor's action from the next (G9).</summary>
    Gap,
    /// <summary>A known predator's action begins: its ring lights and its row takes the marker.</summary>
    Focus,
    /// <summary>It spots you, hears you, or loses your trail.</summary>
    Notices,
    /// <summary>It hunts, searches or wanders, cell by cell.</summary>
    Steps,
    /// <summary>It waits, is dazed, or is held by venom.</summary>
    Stays,
    Bites,
    /// <summary>A bite you dodged.</summary>
    Dodged,
    /// <summary>Your spikes hurt it, or it bleeds.</summary>
    IsHurt,
    SecondWind,
    /// <summary>Every known small game that moved, together (H4).</summary>
    SmallGame,
    /// <summary>A predator arrives where you can see it.</summary>
    Arrives,
    /// <summary>Everything you cannot see, as one line that never says which, how many or where (H3).</summary>
    Unseen,
    TimePasses,
    /// <summary>Hunger or thirst took health this turn.</summary>
    TimeHurts,
    /// <summary>You grew into a new stage.</summary>
    Stage,
    /// <summary>The apex has come (H6).</summary>
    Apex,
    /// <summary>The run ended: then the summary.</summary>
    End,
}

/// <summary>Whose beat it is: what the view lights and animates.</summary>
internal enum BeatActor
{
    /// <summary>Your dino.</summary>
    You,
    /// <summary>One predator, named by <see cref="Beat.Creature"/>.</summary>
    Creature,
    /// <summary>The known small game, together.</summary>
    SmallGame,
    /// <summary>What is out of sight: nothing on the board moves.</summary>
    Unseen,
    /// <summary>Time, and the gaps: the board's numbers, nobody's sprite.</summary>
    World,
}

/// <summary>
/// One beat of a turn's playback.
/// </summary>
/// <param name="Kind">What it shows.</param>
/// <param name="Actor">Whose it is.</param>
/// <param name="Creature">The creature it concerns (an id): the predator acting, the one you strike, the one arriving; 0 for none.</param>
/// <param name="Ticks">How long it lasts at speed 1, in the engine's ticks (30 a second): 1 at least, <see cref="TurnScript.MaxBeatTicks"/> at most.</param>
/// <param name="Banner">What the banner reads while it plays. Never empty: a Gap carries the banner of the beat before it, since the banner stays as it was (G9: it is always plain whose turn it is).</param>
/// <param name="Events">The turn's events this beat shows, in their order (an affliction rides on the hit that caused it). Empty for a Focus, a Gap and the Unseen beat: the last must say nothing of what it hides (H3).</param>
/// <param name="RowIndex">The index in <see cref="TurnScript.Rows"/> of the row this beat belongs to: the view puts the marker there when it begins. -1 for none (a Gap, time, the end).</param>
internal sealed record Beat(BeatKind Kind, BeatActor Actor, int Creature, int Ticks, string Banner,
    IReadOnlyList<TurnEvent> Events, int RowIndex)
{
    // A record compares a list by reference, and two scripts built from the same
    // turn must be equal (H2: the script is a pure function of the turn), so the
    // events are compared one by one. They are the turn's own event objects, so
    // equal events are usually the same objects.
    public bool Equals(Beat? other) =>
        other is not null
        && Kind == other.Kind && Actor == other.Actor && Creature == other.Creature && Ticks == other.Ticks
        && string.Equals(Banner, other.Banner, StringComparison.Ordinal) && RowIndex == other.RowIndex
        && Events.SequenceEqual(other.Events);

    public override int GetHashCode() => HashCode.Combine(Kind, Actor, Creature, Ticks, Banner, RowIndex, Events.Count);
}

/// <summary>
/// How long each beat lasts, in the engine's ticks at speed 1 (30 a second; Fast
/// plays at speed 2, H5): the beat table's numbers, each with its one home here.
/// Brisk on purpose: a turn with nothing known nearby is about half a second, a
/// turn with two hunters that both bite about two and a half.
/// </summary>
internal static class BeatTicks
{
    /// <summary>A walk, a cell.</summary>
    public const int WalkPerCell = 5;
    /// <summary>A sneak, a cell: slower.</summary>
    public const int SneakPerCell = 6;
    /// <summary>A sprint, a cell: fast. Five cells are 15.</summary>
    public const int SprintPerCell = 3;
    /// <summary>Eating: the eat frames, the food going, the gain rising.</summary>
    public const int Eat = 8;
    /// <summary>Drinking.</summary>
    public const int Drink = 8;
    /// <summary>Resting.</summary>
    public const int Rest = 8;
    /// <summary>Your hit: your strike frames toward it (3), then its hurt frame and the number (6).</summary>
    public const int Strike = 9;
    /// <summary>A hit that defeats: it fades over this many more ticks and its meat appears.</summary>
    public const int StrikeDefeatFade = 6;
    /// <summary>Each creature Thagomizer's sweep also struck: its hurt frame and its number.</summary>
    public const int AlsoStruck = 6;
    /// <summary>Between two actors, nothing moving (G9).</summary>
    public const int Gap = 3;
    /// <summary>A known predator's action begins.</summary>
    public const int Focus = 3;
    /// <summary>It spots you, hears you, loses your trail: the mark above it changes.</summary>
    public const int Notices = 5;
    /// <summary>A predator's step, a cell.</summary>
    public const int StepPerCell = 4;
    /// <summary>It waits: short, nothing happens.</summary>
    public const int Waits = 2;
    /// <summary>It is dazed, or held by venom: long enough to read why it did not move.</summary>
    public const int Stuck = 5;
    /// <summary>A bite: its strike frames (3), your hurt frame and the number (7).</summary>
    public const int Bite = 10;
    /// <summary>A bite you dodged: its strike frames, and your dino hops aside and back.</summary>
    public const int Dodge = 8;
    /// <summary>Your spikes hurt it, or it bleeds.</summary>
    public const int Hurt = 6;
    /// <summary>Second wind: your ring flashes three times.</summary>
    public const int SecondWind = 12;
    /// <summary>The small game that moved, all together.</summary>
    public const int SmallGame = 5;
    /// <summary>A predator arrives where you can see it: it fades in.</summary>
    public const int Arrives = 6;
    /// <summary>Out of sight, when nothing out there heard you.</summary>
    public const int UnseenQuiet = 3;
    /// <summary>Out of sight, when something heard you: long enough to be read.</summary>
    public const int UnseenHeard = 8;
    /// <summary>Time passes: the bars and the growth slide to their new values.</summary>
    public const int TimePasses = 4;
    /// <summary>Hunger or thirst took health: your hurt frame and the number.</summary>
    public const int TimeHurts = 8;
    /// <summary>A new stage: your dino blinks three times and is its new size.</summary>
    public const int Stage = 20;
    /// <summary>The apex has your scent: it fades in.</summary>
    public const int Apex = 16;
    /// <summary>The run ended: then the summary.</summary>
    public const int End = 12;
}
