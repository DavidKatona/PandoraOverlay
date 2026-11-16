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
///
/// <para><b>The enemies' time</b> (H93, the owner's call of Nov 15 2026, amending H5's
/// paces: "emphasise the enemies"). The first table was brisk, and the owner found it
/// too quick to follow the hunters. Every beat is now <see cref="Stretch"/> times its
/// first length, so a beat keeps its inner proportions (a strike's wind-up, then its
/// blow; a bite's frames, then your hurt), and the view slows every frame it shows
/// inside a beat by the same factor (<c>BeatStep.ArtTick</c>); but a predator's
/// <see cref="Focus"/> and the <see cref="Gap"/> between two actors are half a second
/// each, and <see cref="Notices"/> long enough to read, so each hunter is singled out
/// before it acts and two actors are plainly apart. A quiet turn (a walk, a gap, out
/// of sight, time) is 39 ticks, 1.3 s at Normal; a turn with two hunters that both
/// spot you, step twice and bite, 195, 6.5 s. Fast, speed 2 over this table, is
/// about the first table's Normal, with the focus and the gap at a quarter second.</para>
/// </summary>
internal static class BeatTicks
{
    /// <summary>
    /// How many times its first length every beat is but the Focus, the Gap and a
    /// notice (H93): 2. The art's frames were timed for the first table (a walk's
    /// cycle of 12 ticks, a strike's 3 and 6), so the view asks them with a beat's tick
    /// divided by this (<c>BeatStep.ArtTick</c>), and its own frame tables (the lean,
    /// the hop, the blinks, the flashes) the same way: at Normal they show for twice
    /// their ticks, at Fast as the first table drew them. Never regenerate the art for it.
    /// </summary>
    public const int Stretch = 2;

    /// <summary>A walk, a cell: 10.</summary>
    public const int WalkPerCell = 5 * Stretch;
    /// <summary>A sneak, a cell: slower, 12.</summary>
    public const int SneakPerCell = 6 * Stretch;
    /// <summary>A sprint, a cell: fast, 6. Five cells are 30.</summary>
    public const int SprintPerCell = 3 * Stretch;
    /// <summary>Eating: the eat frames, the food going, the gain rising: 16.</summary>
    public const int Eat = 8 * Stretch;
    /// <summary>Drinking: 16.</summary>
    public const int Drink = 8 * Stretch;
    /// <summary>Resting: 16.</summary>
    public const int Rest = 8 * Stretch;
    /// <summary>Your hit, 18: your strike frames toward it (6), then its hurt frame and the number (12).</summary>
    public const int Strike = 9 * Stretch;
    /// <summary>A hit that defeats: it fades over this many more ticks (12) and its meat appears.</summary>
    public const int StrikeDefeatFade = 6 * Stretch;
    /// <summary>Each creature Thagomizer's sweep also struck: its hurt frame and its number, 12.</summary>
    public const int AlsoStruck = 6 * Stretch;
    /// <summary>Between two actors, nothing moving (G9): half a second, so two actors are plainly apart (H93).</summary>
    public const int Gap = 15;
    /// <summary>A known predator's action begins, its ring lit and its row marked: half a second, so it is singled out before it acts (H93).</summary>
    public const int Focus = 15;
    /// <summary>It spots you, hears you, loses your trail: the mark above it changes, 12, long enough to read (H93).</summary>
    public const int Notices = 12;
    /// <summary>A predator's step, a cell: 8.</summary>
    public const int StepPerCell = 4 * Stretch;
    /// <summary>It waits: short, nothing happens, 4.</summary>
    public const int Waits = 2 * Stretch;
    /// <summary>It is dazed, or held by venom: long enough to read why it did not move, 10.</summary>
    public const int Stuck = 5 * Stretch;
    /// <summary>A bite, 20: its strike frames (6), your hurt frame and the number (14).</summary>
    public const int Bite = 10 * Stretch;
    /// <summary>A bite you dodged, 16: its strike frames, and your dino hops aside and back.</summary>
    public const int Dodge = 8 * Stretch;
    /// <summary>Your spikes hurt it, or it bleeds: 12.</summary>
    public const int Hurt = 6 * Stretch;
    /// <summary>Second wind: your ring flashes three times, 24.</summary>
    public const int SecondWind = 12 * Stretch;
    /// <summary>The small game that moved, all together: 10.</summary>
    public const int SmallGame = 5 * Stretch;
    /// <summary>A predator arrives where you can see it: it fades in, 12.</summary>
    public const int Arrives = 6 * Stretch;
    /// <summary>Out of sight, when nothing out there heard you: 6.</summary>
    public const int UnseenQuiet = 3 * Stretch;
    /// <summary>Out of sight, when something heard you: long enough to be read, 16.</summary>
    public const int UnseenHeard = 8 * Stretch;
    /// <summary>Time passes: the bars and the growth slide to their new values, 8.</summary>
    public const int TimePasses = 4 * Stretch;
    /// <summary>Hunger or thirst took health: your hurt frame and the number, 16.</summary>
    public const int TimeHurts = 8 * Stretch;
    /// <summary>A new stage: your dino blinks three times and is its new size, 40.</summary>
    public const int Stage = 20 * Stretch;
    /// <summary>The apex has your scent: it fades in, 32.</summary>
    public const int Apex = 16 * Stretch;
    /// <summary>The run ended, 24: then the summary.</summary>
    public const int End = 12 * Stretch;
}
