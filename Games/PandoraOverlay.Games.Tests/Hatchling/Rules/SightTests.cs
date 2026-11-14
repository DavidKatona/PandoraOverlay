using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Cover, hiding, wading, sight and what the player may know (<see cref="Sight"/>),
/// on hand-built boards: one ground everywhere, no creature, you in the middle.
/// </summary>
public class SightTests
{
    /// <summary>The middle of the board: row 5, column 5.</summary>
    private const int Middle = 5 * Tuning.Columns + 5;

    /// <summary>A run of <paramref name="species"/> on a board of <paramref name="ground"/> only, no creature, you in the middle.</summary>
    private static RunState Board(string species, Terrain ground = Terrain.Grass) =>
        Runs.Fresh(species, 1).Clear().Paved(ground).At(Middle);

    /// <summary>A cell <paramref name="steps"/> to your right in your row (the middle's row has room for 5).</summary>
    private static int Right(int steps) => Middle + steps;

    // ---- Cover ---------------------------------------------------------------------

    [Theory]
    [InlineData("Herrerasaurus", Terrain.Forest, true)]    // a Hunter hides in forest
    [InlineData("Dryosaurus", Terrain.Forest, true)]       // so does a Runner
    [InlineData("Tyrannosaurus", Terrain.Forest, false)]   // an Apex does not
    [InlineData("Triceratops", Terrain.Forest, false)]     // nor a Tank
    [InlineData("Deinosuchus", Terrain.Forest, false)]     // a Swimmer's cover is water
    [InlineData("Deinosuchus", Terrain.Water, true)]
    [InlineData("Herrerasaurus", Terrain.Water, false)]
    [InlineData("Pteranodon", Terrain.Water, false)]       // flying over water is no cover
    [InlineData("Herrerasaurus", Terrain.Grass, false)]
    [InlineData("Deinosuchus", Terrain.Rock, false)]
    public void CoverIsForestForAHiderAndWaterForASwimmer(string species, object ground, bool cover)
    {
        var run = Board(species, (Terrain)ground);
        var stats = Stats.Of(run);
        Assert.Equal(cover, Sight.InCover(run, stats));
        Assert.Equal(cover, Sight.Hidden(run, stats));
    }

    // ---- Freeze (H13) ---------------------------------------------------------------

    [Theory]
    [InlineData(MoveKind.Rest, true)]
    [InlineData(MoveKind.None, false)]
    [InlineData(MoveKind.Walk, false)]
    [InlineData(MoveKind.Sneak, false)]
    [InlineData(MoveKind.Eat, false)]
    [InlineData(MoveKind.Drink, false)]
    [InlineData(MoveKind.Attack, false)]
    public void FreezeHidesYouOnOpenGroundOnlyAfterARest(object last, bool hidden)
    {
        var run = Board("Hypsilophodon");
        run.You.LastMove = (MoveKind)last;
        var stats = Stats.Of(run);
        Assert.False(Sight.InCover(run, stats));   // Freeze is hiding, not cover
        Assert.Equal(hidden, Sight.Hidden(run, stats));
    }

    [Fact]
    public void ARestHidesNoOneWithoutFreeze()
    {
        var run = Board("Dryosaurus");
        run.You.LastMove = MoveKind.Rest;
        Assert.False(Sight.Hidden(run, Stats.Of(run)));
    }

    // ---- Wading ---------------------------------------------------------------------

    [Theory]
    [InlineData("Herrerasaurus", true)]
    [InlineData("Tyrannosaurus", true)]
    [InlineData("Gallimimus", true)]        // a Forager wades...
    [InlineData("Beipiaosaurus", false)]    // ...but the Wader does not (H13)
    [InlineData("Deinosuchus", false)]      // a Swimmer swims
    [InlineData("Pteranodon", false)]       // a Flyer flies
    public void WadingIsWaterForEveryoneButSwimmersFlyersAndTheWader(string species, bool wades)
    {
        var run = Board(species, Terrain.Water);
        var stats = Stats.Of(run);
        Assert.Equal(wades, Sight.Wading(run, stats));
        Assert.Equal(wades, Sight.WouldWade(run, stats, Right(1)));
    }

    [Fact]
    public void NoOneWadesOnLand()
    {
        var run = Board("Herrerasaurus");
        var stats = Stats.Of(run);
        Assert.False(Sight.Wading(run, stats));
        run.Tiles[Right(1)] = Terrain.Water;
        Assert.True(Sight.WouldWade(run, stats, Right(1)));
        Assert.False(Sight.WouldWade(run, stats, Right(2)));
    }

    // ---- Sight and what is known ------------------------------------------------------

    [Fact]
    public void InSightIsWithinYourSightCountedInSteps()
    {
        var run = Board("Herrerasaurus");   // sight 3
        var stats = Stats.Of(run);
        Assert.True(Sight.InSight(run, stats, Middle));
        Assert.True(Sight.InSight(run, stats, Right(3)));
        Assert.False(Sight.InSight(run, stats, Right(4)));
    }

    [Fact]
    public void ACreatureInSightIsKnownAndOneBeyondItIsNot()
    {
        var run = Board("Herrerasaurus").Put(PredatorType.Carnotaurus, Right(3)).Put(PredatorType.Troodon, Right(4))
            .PutSmallGame(Right(2)).PutSmallGame(Right(5));
        var stats = Stats.Of(run);
        var known = run.Creatures.Select(c => Sight.Knows(run, stats, c)).ToList();
        Assert.Equal(new[] { true, false, true, false }, known);
    }

    /// <summary>Wary senses predators two cells beyond sight, never small game, and never further.</summary>
    [Fact]
    public void WarySensesPredatorsTwoBeyondSightButNeverSmallGame()
    {
        var run = Board("Herrerasaurus").Put(PredatorType.Carnotaurus, Right(5)).PutSmallGame(Right(4));
        run.Instincts.Add(Instinct.Wary);
        var stats = Stats.Of(run);   // sight 3, sense 5
        Assert.True(Sight.Knows(run, stats, run.Creatures[0]));
        Assert.False(Sight.Knows(run, stats, run.Creatures[1]));

        // One step further than the sense is not known.
        var far = Board("Herrerasaurus").At(Middle - 1).Put(PredatorType.Carnotaurus, Right(5));
        far.Instincts.Add(Instinct.Wary);
        Assert.False(Sight.Knows(far, Stats.Of(far), far.Creatures[0]));
    }

    [Fact]
    public void WithoutWaryAPredatorBeyondSightIsUnknown()
    {
        var run = Board("Herrerasaurus").Put(PredatorType.Carnotaurus, Right(4));
        Assert.False(Sight.Knows(run, Stats.Of(run), run.Creatures[0]));
    }

    /// <summary>H6: the apex is known wherever it is, in sight or not.</summary>
    [Fact]
    public void TheApexIsAlwaysKnown()
    {
        var run = Board("Herrerasaurus").At(0).Put(PredatorType.Tyrannosaurus, Tuning.Cells - 1);
        var stats = Stats.Of(run);
        Assert.False(Sight.InSight(run, stats, Tuning.Cells - 1));
        Assert.True(Sight.Knows(run, stats, run.Creatures[0]));
    }

    [Fact]
    public void KeenEyesWidensWhatIsKnown()
    {
        var run = Board("Herrerasaurus").Put(PredatorType.Carnotaurus, Right(4));
        run.Instincts.Add(Instinct.Eyes);
        Assert.True(Sight.Knows(run, Stats.Of(run), run.Creatures[0]));
    }

    // ---- Reveal ---------------------------------------------------------------------

    [Fact]
    public void RevealMarksEveryCellInSightAsSeenAndKeepsWhatWasSeen()
    {
        var run = Board("Herrerasaurus");
        Array.Fill(run.Seen, false);
        run.Seen[0] = true;   // seen before: stays seen
        var stats = Stats.Of(run);
        Sight.Reveal(run, stats);
        for (var cell = 0; cell < Tuning.Cells; cell++)
            Assert.Equal(cell == 0 || Island.Grid.Distance(Middle, cell) <= stats.Sight, run.Seen[cell]);
        Assert.Equal(1 + 37, run.Seen.Count(s => s));   // a radius of 3 holds 37 cells, all on the board here
    }

    [Fact]
    public void TheAnswersChangeNothing()
    {
        var run = Board("Hypsilophodon", Terrain.Water).Put(PredatorType.Carnotaurus, Right(2));
        var before = Runs.Fingerprint(run);
        var stats = Stats.Of(run);
        Sight.InCover(run, stats);
        Sight.Hidden(run, stats);
        Sight.Wading(run, stats);
        Sight.WouldWade(run, stats, Right(1));
        Sight.InSight(run, stats, Right(1));
        Sight.Knows(run, stats, run.Creatures[0]);
        Assert.Equal(before, Runs.Fingerprint(run));
    }
}
