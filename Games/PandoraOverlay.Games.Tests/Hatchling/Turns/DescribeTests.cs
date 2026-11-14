using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The context line for the cell under the mouse (H23): each row of its table,
/// with and without the move's prefix, singular and plural.
/// </summary>
public class DescribeTests
{
    private static readonly int Here = Row5(2);

    /// <summary>Nothing to move to, nobody there, no food shown, a plant-eater that hides, the plan's plain gains.</summary>
    private static CellFacts Plain(Diet eats = Diet.Plants) =>
        new(false, 0, null, false, eats, true, false, false, 30, 45, 25, false, false);

    private static RunState Island()
    {
        var run = Run(Here);
        run.Species = "Dryosaurus";
        run.You.Growth = 30;
        return run;
    }

    // ---- creatures ----------------------------------------------------------------------

    [Fact]
    public void AKnownPredatorIsNamedWithItsStateAndDistance()
    {
        var run = Island();
        var carno = Predator(1, PredatorType.Carnotaurus, Row5(5), ordinal: 2, alert: 4);
        Assert.Equal("Carnotaurus 2 · hunting you · 3 hexes", Describe.Cell(run, Row5(5), Plain() with { Creature = carno }));

        var beside = Predator(1, PredatorType.Carnotaurus, Row5(3), ordinal: 2, alert: 2);
        Assert.Equal("Carnotaurus 2 · heard you · 1 hex · click to attack",
            Describe.Cell(run, Row5(3), Plain() with { Creature = beside, CanAttack = true }));

        var calm = Predator(1, PredatorType.Troodon, Row5(4), alert: 0);
        Assert.Equal("Troodon · unaware · 2 hexes", Describe.Cell(run, Row5(4), Plain() with { Creature = calm }));

        var apex = Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4);
        Assert.StartsWith("Tyrannosaurus · has your scent · ", Describe.Cell(run, Grid.Index(10, 0), Plain() with { Creature = apex }));
    }

    [Fact]
    public void KnownSmallGame()
    {
        var run = Island();
        var game = Game(5, Row5(3));
        Assert.Equal("Small game", Describe.Cell(run, Row5(3), Plain() with { Creature = game }));
        Assert.Equal("Small game · click to attack", Describe.Cell(run, Row5(3), Plain() with { Creature = game, CanAttack = true }));
    }

    [Fact]
    public void YourOwnCell()
    {
        var run = Island();
        Assert.Equal("You · Juvenile Dryosaurus", Describe.Cell(run, Here, Plain()));
        run.You.Growth = 100;
        Assert.Equal("You · Elder Dryosaurus", Describe.Cell(run, Here, Plain()));
        run.You.Growth = 24.9;
        Assert.Equal("You · Hatchling Dryosaurus", Describe.Cell(run, Here, Plain()));
    }

    // ---- food ---------------------------------------------------------------------------

    [Fact]
    public void APlantTheBoardShows()
    {
        var run = Island();
        var cell = Row5(4);
        run.Plants[cell] = true;
        var shows = Plain() with { PlantShows = true };
        Assert.Equal("Plants · stand here and Eat: +30 food", Describe.Cell(run, cell, shows));
        Assert.Equal("Plants", Describe.Cell(run, cell, shows with { Eats = Diet.Meat }));
        Assert.Equal("Plants · stand here and Eat: +45 food", Describe.Cell(run, cell, shows with { PlantGain = 45 }));
        // Not shown (the view's fact): the line does not name it, seen cell or not.
        Assert.Equal("Grass", Describe.Cell(run, cell, Plain()));

        // A cell never seen whose plant the board shows (Good nose): named.
        run.Seen[cell] = false;
        Assert.Equal("Plants · stand here and Eat: +30 food", Describe.Cell(run, cell, shows));
        Assert.Equal("Not seen yet", Describe.Cell(run, cell, Plain()));
    }

    [Fact]
    public void ACarcassTheBoardShowsSingularAndPlural()
    {
        var run = Island();
        var cell = Row5(4);
        run.Meat[cell] = 2;
        var shows = Plain() with { CarcassShows = true };
        Assert.Equal("Carcass · 2 meals", Describe.Cell(run, cell, shows));
        run.Meat[cell] = 1;
        Assert.Equal("Carcass · 1 meal · stand here and Eat: +45 food", Describe.Cell(run, cell, shows with { Eats = Diet.Meat }));
        run.Seen[cell] = false;
        Assert.Equal("Carcass · 1 meal · stand here and Eat: +45 food", Describe.Cell(run, cell, shows with { Eats = Diet.Meat }));
        Assert.Equal("Not seen yet", Describe.Cell(run, cell, Plain(Diet.Meat)));
    }

    [Fact]
    public void AFoggedCellNamesOnlyTheFoodTheBoardShows()
    {
        // A plant and a carcass in the fog; a Scavenger is shown the carcass only.
        var run = Island();
        var cell = Row5(4);
        run.Plants[cell] = true;
        run.Meat[cell] = 2;
        run.Seen[cell] = false;
        Assert.Equal("Carcass · 2 meals · stand here and Eat: +45 food",
            Describe.Cell(run, cell, Plain(Diet.Meat) with { CarcassShows = true }));
        Assert.Equal("Not seen yet", Describe.Cell(run, cell, Plain(Diet.Meat)));
    }

    [Fact]
    public void TheFoodFactsAreSightsShowsFood()
    {
        // What the view hands in, with the real Sight.ShowsFood: a Ceratosaurus
        // (Scavenger) is shown a fogged carcass, and not the plant beside it in the fog.
        var run = Runs.Fresh("Ceratosaurus", 7).Clear();
        var cell = Enumerable.Range(0, Tuning.Cells).First(c => !run.Seen[c]);
        run.Plants[cell] = true;
        run.Meat[cell] = 1;
        var stats = Stats.Of(run);
        var facts = new CellFacts(false, 0, null, false, stats.Eats, stats.Hides, stats.Flies, false, 30, 45, 25,
            Sight.ShowsFood(run, stats, cell, Food.Plants), Sight.ShowsFood(run, stats, cell, Food.Meat));
        Assert.Equal("Carcass · 1 meal · stand here and Eat: +45 food", Describe.Cell(run, cell, facts));
    }

    // ---- the ground ---------------------------------------------------------------------

    [Fact]
    public void Forest()
    {
        var run = Island();
        run.Tiles[Row5(4)] = Terrain.Forest;
        Assert.Equal("Forest · hides you from their eyes", Describe.Cell(run, Row5(4), Plain()));
        Assert.Equal("Forest", Describe.Cell(run, Row5(4), Plain() with { Hides = false }));
    }

    [Fact]
    public void Water()
    {
        var run = Island();
        run.Tiles[Row5(4)] = Terrain.Water;
        Assert.Equal("Water · Drink on it or beside it", Describe.Cell(run, Row5(4), Plain()));
        Assert.Equal("Water · Drink on it or beside it · wading is slow and loud", Describe.Cell(run, Row5(4), Plain() with { Wades = true }));
        Assert.Equal("Water · Drink on it or beside it · Eat here: +25 food", Describe.Cell(run, Row5(4), Plain(Diet.Meat | Diet.Fish)));
    }

    [Fact]
    public void Rock()
    {
        var run = Island();
        run.Tiles[Row5(4)] = Terrain.Rock;
        Assert.Equal("Rock · no way through", Describe.Cell(run, Row5(4), Plain()));
        Assert.Equal("Rock · only you can cross it", Describe.Cell(run, Row5(4), Plain() with { Flies = true }));
    }

    [Fact]
    public void GrassAndNeverSeen()
    {
        var run = Island();
        Assert.Equal("Grass", Describe.Cell(run, Row5(4), Plain()));
        // The ground of a cell never seen is never told: the board draws it as fog.
        run.Tiles[Row5(4)] = Terrain.Forest;
        run.Seen[Row5(4)] = false;
        Assert.Equal("Not seen yet", Describe.Cell(run, Row5(4), Plain()));
    }

    // ---- the move's prefix --------------------------------------------------------------

    [Fact]
    public void AMoveTargetStartsWithTheMoveByThePace()
    {
        var run = Island();
        run.Tiles[Row5(3)] = Terrain.Forest;
        var move = Plain() with { MoveTarget = true, Noise = 3 };
        Assert.Equal("Walk here · noise 3 · Forest · hides you from their eyes", Describe.Cell(run, Row5(3), move));
        run.Pace = Pace.Sneak;
        Assert.Equal("Sneak here · noise 1 · Grass", Describe.Cell(run, Row5(1), move with { Noise = 1 }));
        run.Pace = Pace.Sprint;
        Assert.Equal("Sprint here · noise 5 · Not seen yet", Describe.Cell(Unseen(run, Row5(0)), Row5(0), move with { Noise = 5 }));
        // Not a target: no prefix.
        Assert.Equal("Grass", Describe.Cell(run, Row5(1), Plain()));
    }

    private static RunState Unseen(RunState run, int cell)
    {
        run.Seen[cell] = false;
        return run;
    }
}
