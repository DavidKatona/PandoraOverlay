using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What <see cref="Describe.Cell"/> needs to know about a cell that the run alone
/// does not say: the answers of your numbers (<c>Stats</c>), of what you know
/// (<c>Sight</c>) and of your moves. The view fills it (task H14), so the line
/// under the mouse says exactly what the board and the moves would do.
/// </summary>
/// <param name="MoveTarget">A click here would move you (walk, sneak or sprint by the run's pace).</param>
/// <param name="Noise">That move's noise, every modifier applied.</param>
/// <param name="Creature">The creature on the cell that the player knows (Sight.Knows), or null: one the player does not know is never named (H3).</param>
/// <param name="CanAttack">A click here would attack that creature.</param>
/// <param name="Eats">Your diet.</param>
/// <param name="Hides">Your build hides in forest.</param>
/// <param name="Flies">Your build flies: rock is open to you.</param>
/// <param name="Wades">You would wade on this cell (it is water, and you neither swim nor fly nor are a Wader).</param>
/// <param name="PlantGain">A plant's food for you, rounded, with your meal's factors.</param>
/// <param name="MeatGain">A meal of carcass, the same.</param>
/// <param name="FishGain">Fishing, the same.</param>
/// <param name="FoodThroughFog">The board shows this cell's food although the cell was never seen (Good nose, Scavenger): the line names it as the board shows it.</param>
internal sealed record CellFacts(bool MoveTarget, int Noise, Creature? Creature, bool CanAttack,
    Diet Eats, bool Hides, bool Flies, bool Wades, int PlantGain, int MeatGain, int FishGain, bool FoodThroughFog);

/// <summary>
/// The context line for the cell under the mouse (H23): what is there, always, and
/// what you could do with it. Pure: every fact is a parameter or in the run, and
/// every word is <see cref="HatchlingText"/>'s.
/// </summary>
internal static class Describe
{
    /// <summary>
    /// The island's grid, for distances. (Task H02 makes the game's own,
    /// <c>Island.Grid</c>; this one is the same 11 x 11 and goes when the two meet,
    /// so this task stands on H01 alone.)
    /// </summary>
    internal static readonly HexGrid Board = new(Tuning.Columns, Tuning.Rows);

    /// <summary>
    /// The line for <paramref name="cell"/>: the move's prefix when a click there
    /// moves you ("Walk here · noise 3 · "), then the first that applies: a known
    /// predator, known small game, you, a plant, a carcass, the ground; "Not seen
    /// yet" for a cell never seen. The ground of a cell never seen is never told:
    /// the board draws it as fog.
    /// </summary>
    public static string Cell(RunState run, int cell, CellFacts facts)
    {
        var prefix = !facts.MoveTarget ? "" : run.Pace switch
        {
            Pace.Sneak => HatchlingText.MoveSneak(facts.Noise),
            Pace.Sprint => HatchlingText.MoveSprint(facts.Noise),
            _ => HatchlingText.MoveWalk(facts.Noise),
        };
        return prefix + What(run, cell, facts);
    }

    private static string What(RunState run, int cell, CellFacts facts)
    {
        var attack = facts.CanAttack ? HatchlingText.ClickAttack : "";
        var who = facts.Creature;
        if (who is { Kind: CreatureKind.Predator })
            return HatchlingText.SeePredator(who.Type, who.Ordinal, State(who), Board.Distance(run.You.At, cell)) + attack;
        if (who is { Kind: CreatureKind.SmallGame })
            return HatchlingText.SeeGame + attack;
        if (cell == run.You.At)
            return HatchlingText.SeeYou(StageOf(run.You.Growth), run.Species);

        var seen = run.Seen[cell];
        if (run.Plants[cell] && (seen || facts.FoodThroughFog))
            return HatchlingText.SeePlants + (facts.Eats.HasFlag(Diet.Plants) ? HatchlingText.SeePlantsEat(facts.PlantGain) : "");
        if (run.Meat[cell] > 0 && (seen || facts.FoodThroughFog))
            return HatchlingText.SeeCarcass(run.Meat[cell]) + (facts.Eats.HasFlag(Diet.Meat) ? HatchlingText.SeeCarcassEat(facts.MeatGain) : "");
        if (!seen) return HatchlingText.SeeUnseen;

        return run.Tiles[cell] switch
        {
            Terrain.Forest => HatchlingText.SeeForest + (facts.Hides ? HatchlingText.SeeForestHides : ""),
            Terrain.Water => HatchlingText.SeeWater
                + (facts.Wades ? HatchlingText.SeeWaterWade : "")
                + (facts.Eats.HasFlag(Diet.Fish) ? HatchlingText.SeeWaterFish(facts.FishGain) : ""),
            Terrain.Rock => facts.Flies ? HatchlingText.SeeRockFly : HatchlingText.SeeRock,
            _ => HatchlingText.SeeGrass,
        };
    }

    /// <summary>A known predator's state: the apex "has your scent", alert 4 "hunting you", 1 to 3 "heard you", else "unaware".</summary>
    private static string State(Creature who) =>
        who.Type == Tuning.Apex ? HatchlingText.StateScent
        : who.Alert >= Tuning.AlertSeen ? HatchlingText.StateHunting
        : who.Alert > 0 ? HatchlingText.StateHeard
        : HatchlingText.StateUnaware;

    /// <summary>
    /// The stage of a growth, 0 Hatchling to 4 Elder: the number of lines it is on
    /// or past. (Task H02's <c>Stages.Of</c> is the game's own; this goes when the two meet.)
    /// </summary>
    private static int StageOf(double growth) => Tuning.StageLines.Count(line => growth >= line);
}
