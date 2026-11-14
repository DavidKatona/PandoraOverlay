namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Cover, hiding, wading, sight and what the player knows. Read-only questions on a
/// run, but <see cref="Reveal"/>, which marks what is in sight as seen.
///
/// <para><see cref="Knows"/> is THE answer to "may the player see this creature":
/// the board, the turn's playback, the context line and the bot all ask it and
/// nothing else, so no screen can ever show, or a bot ever act on, a creature the
/// player could not know about (the turns' clarity, G9, rests on it: what is shown
/// is what you could know).</para>
///
/// <para><see cref="ShowsFood"/> is to food what <see cref="Knows"/> is to creatures
/// (H39): the painter, the context line and the bot ask it, and nothing shows a plant
/// or a carcass it does not answer.</para>
/// </summary>
internal static class Sight
{
    /// <summary>
    /// You stand in cover: forest for a build that hides in it, water for one that
    /// swims. Cover hides you from eyes (not ears) and is where an ambush comes from.
    /// </summary>
    public static bool InCover(RunState run, Stats stats)
    {
        var ground = run.Tiles[run.You.At];
        return (ground == Terrain.Forest && stats.Hides) || (ground == Terrain.Water && stats.Swims);
    }

    /// <summary>
    /// Predators can't see you: in cover, or, for Freeze (H13), on any ground when
    /// your last action was a Rest. Freeze holds until your next action, which is
    /// what <see cref="You.LastMove"/> says: any other move replaces the Rest.
    /// </summary>
    public static bool Hidden(RunState run, Stats stats) =>
        InCover(run, stats) || (stats.Species.Perk == Perk.Freeze && run.You.LastMove == MoveKind.Rest);

    /// <summary>You stand in water you wade through (it costs noise and food to enter).</summary>
    public static bool Wading(RunState run, Stats stats) => WouldWade(run, stats, run.You.At);

    /// <summary>
    /// <paramref name="cell"/> is water you would wade in: for every build but those
    /// that swim or fly, and never for a Wader (H13), which wades without cost.
    /// </summary>
    public static bool WouldWade(RunState run, Stats stats, int cell) =>
        run.Tiles[cell] == Terrain.Water && !stats.Swims && !stats.Flies && stats.Species.Perk != Perk.Wader;

    /// <summary><paramref name="cell"/> is at most <see cref="Stats.Sight"/> steps from you.</summary>
    public static bool InSight(RunState run, Stats stats, int cell) =>
        Island.Grid.Distance(run.You.At, cell) <= stats.Sight;

    /// <summary>
    /// The player may know about <paramref name="creature"/>: it is in sight; or it is
    /// a predator within your <see cref="Stats.Sense"/> (Wary, which senses predators
    /// two cells beyond sight, never small game); or it is the apex, which is always
    /// known once it has come (H6: a hunter nobody can see coming for you would teach
    /// nothing). The apex is told by its species, which only the apex has.
    /// </summary>
    public static bool Knows(RunState run, Stats stats, Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (InSight(run, stats, creature.At)) return true;
        if (creature.Kind != CreatureKind.Predator) return false;
        return creature.Type == Tuning.Apex
            || Island.Grid.Distance(run.You.At, creature.At) <= stats.Sense;
    }

    /// <summary>
    /// The ONE answer to "may the player see the food of <paramref name="kind"/> on
    /// <paramref name="cell"/>" (H39), asked by the painter, the context line and the
    /// bot, as <see cref="Knows"/> is for creatures.
    /// <list type="bullet">
    /// <item>Plants: a plant is on the cell, and the cell is seen, or the run has Good
    /// nose and you eat plants.</item>
    /// <item>Meat: a carcass is on the cell, and the cell is seen, or your perk is
    /// Scavenger, or the run has Good nose and you eat meat.</item>
    /// <item>Fish: never. Fish is the water itself, a ground, and the fog covers
    /// ground whatever your nose; the board has no picture for it.</item>
    /// </list>
    /// On a seen cell the food shows AS IT IS NOW, in sight or not: the run keeps no
    /// memory of what a cell held, and what changes out of sight is small and true (a
    /// plant grown there, the meat of a predator that bled out). "You eat" is
    /// <see cref="Stats.Eats"/>, so a Wader with Good nose is shown plants and carcasses.
    /// Small game is a creature, not food: <see cref="Knows"/> answers for it.
    /// </summary>
    public static bool ShowsFood(RunState run, Stats stats, int cell, Food kind)
    {
        var nose = run.Has(Instinct.Nose);
        return kind switch
        {
            Food.Plants => run.Plants[cell]
                && (run.Seen[cell] || (nose && stats.Eats.HasFlag(Diet.Plants))),
            Food.Meat => run.Meat[cell] > 0
                && (run.Seen[cell] || stats.Species.Perk == Perk.Scavenger || (nose && stats.Eats.HasFlag(Diet.Meat))),
            _ => false,
        };
    }

    /// <summary>
    /// Every cell in sight becomes seen (<see cref="RunState.Seen"/>): the run's map
    /// of the island remembers what you have looked at. It changes the run it is
    /// given: it is called on a rule's own copy, at the start of a run and after a move.
    /// </summary>
    public static void Reveal(RunState run, Stats stats)
    {
        foreach (var cell in Island.Grid.Within(run.You.At, stats.Sight))
            run.Seen[cell] = true;
    }
}
