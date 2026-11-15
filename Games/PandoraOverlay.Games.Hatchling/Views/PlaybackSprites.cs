using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The sprites a playback needs that the scene at the turn's start does not hold: a
/// creature that walks into what you know, one that arrives, the apex. Made by the
/// same rule as <c>Scene.Of</c>'s own (its ring by what it is, H22; its marks by its
/// alert and afflictions, H52): a test holds this rule equal to the scene's for every
/// creature of many states, so the two cannot drift apart.
/// </summary>
internal static class PlaybackSprites
{
    /// <summary>A sprite for <paramref name="creature"/> with its feet on <paramref name="cell"/>, standing, facing right, fully there.</summary>
    public static SceneSprite Of(Creature creature, int cell)
    {
        ArgumentNullException.ThrowIfNull(creature);
        var sprite = creature.Kind == CreatureKind.SmallGame
            ? new SceneSprite { Creature = creature.Id, Kind = SpriteKind.SmallGame, Subject = ArtNames.SmallGame, Ring = Ring.None }
            : new SceneSprite
            {
                Creature = creature.Id,
                Kind = SpriteKind.Predator,
                Subject = ArtNames.PredatorOf(creature.Type),
                Ring = RingOf(creature),
                Marks = MarksOf(creature),
                Ordinal = creature.Ordinal,
            };
        Place(sprite, cell);
        return sprite;
    }

    /// <summary>A creature's ring at rest: the apex's heavier red, any other predator's red, none for small game (H22).</summary>
    public static Ring RingOf(Creature creature) =>
        creature.Kind == CreatureKind.SmallGame ? Ring.None
        : creature.Type == Tuning.Apex ? Ring.Apex
        : Ring.Hostile;

    /// <summary>A predator's marks: hunting at alert 4 or for the apex, searching at 1 to 3; its afflictions (H52). None for small game.</summary>
    public static Marks MarksOf(Creature creature)
    {
        if (creature.Kind == CreatureKind.SmallGame) return Marks.None;
        var marks = Marks.None;
        if (creature.Alert >= Tuning.AlertSeen || creature.Type == Tuning.Apex) marks |= Marks.Hunt;
        else if (creature.Alert > 0) marks |= Marks.Search;
        if (creature.Dazed) marks |= Marks.Dazed;
        if (creature.Slowed > 0) marks |= Marks.Slow;
        if (creature.Bleed > 0) marks |= Marks.Bleed;
        return marks;
    }

    /// <summary>Feet on the cell's feet point, and the row it stands in (the drawing order).</summary>
    public static void Place(SceneSprite sprite, int cell)
    {
        (sprite.X, sprite.Y) = BoardLayout.Feet(cell);
        sprite.Row = Island.Grid.Row(cell);
    }
}
