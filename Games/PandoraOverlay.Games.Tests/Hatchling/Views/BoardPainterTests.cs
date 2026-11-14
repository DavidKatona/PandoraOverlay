using System.IO;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Games.Tests;

// The engine's playback tests have a Scene of their own in this namespace, which
// outranks any alias of that name: the board's is BoardScene here.
using BoardScene = PandoraOverlay.Games.Hatchling.Scene;

/// <summary>
/// The board's painter (<see cref="BoardPainter"/>) and the scene it paints
/// (<see cref="BoardScene"/>): golden frames with no art at all, so they never change
/// when art does (H30); what the scene may show (only what the player may know, H6,
/// H29, H39); the predators' numbers (H38); the layout; each stand-in in its colour
/// at its place; and a frame that allocates nothing.
///
/// <para>Every scene at rest is made by the scene's one way in (H53), so the cells
/// to move to and attack are the rules' own (<c>Moves.Targets</c>,
/// <c>Moves.Attackable</c>).</para>
/// </summary>
public class BoardPainterTests
{
    private readonly ITestOutputHelper _output;

    public BoardPainterTests(ITestOutputHelper output) => _output = output;

    /// <summary>The environment variable naming a folder for the real-art pictures (not golden: they change with the art). Unset, none is written.</summary>
    internal const string PreviewVariable = "GAMES_ART_PREVIEW";

    private static readonly ArtBook RealArt = ArtBook.Load(typeof(Island).Assembly);

    private static int Cell(int column, int row) => Island.Grid.Index(column, row);

    // ---- The scenes the golden frames are painted from ---------------------------------

    /// <summary>A fresh run of a Dryosaurus, your turn, nothing chosen yet.</summary>
    internal static BoardScene StartScene()
    {
        var run = Runs.Fresh("Dryosaurus", 7);
        return BoardScene.Of(run, Stats.Of(run), yourTurn: true, hoverCell: -1);
    }

    /// <summary>
    /// A Dryosaurus with Good nose, moved north from its start: the start's cells seen
    /// and dim, the new ones in sight, the rest never seen. A plant and a carcass on a
    /// dim cell, a plant in sight, and a plant on a cell never seen, which Good nose
    /// shows a plant eater (H39).
    /// </summary>
    internal static (RunState Run, BoardScene Scene) FogScene()
    {
        var run = Runs.Fresh("Dryosaurus", 7).Clear();
        run.Instincts.Add(Instinct.Nose);
        var start = run.You.At;
        var here = Cell(5, 3);
        run.Tiles[here] = Terrain.Grass;
        run.You.At = here;
        Sight.Reveal(run, Stats.Of(run));
        Array.Fill(run.Plants, false);
        Array.Fill(run.Meat, 0);

        var dim = start;                  // seen at the start, out of sight now
        var dimToo = Island.Grid.Neighbours(start)[0];
        var inSight = Cell(6, 4);
        var unseen = Cell(9, 6);          // never seen: off both views
        foreach (var cell in new[] { dim, dimToo, inSight, unseen }) run.Tiles[cell] = Terrain.Grass;
        run.Plants[dim] = true;
        run.Meat[dimToo] = 2;
        run.Plants[inSight] = true;
        run.Plants[unseen] = true;
        run.Meat[Cell(1, 6)] = 1;         // a carcass never seen: a plant eater's nose does not show it

        var stats = Stats.Of(run);
        Assert.True(run.Seen[dim] && !Sight.InSight(run, stats, dim));
        Assert.True(run.Seen[dimToo] && !Sight.InSight(run, stats, dimToo));
        Assert.True(Sight.InSight(run, stats, inSight));
        Assert.False(run.Seen[unseen]);
        Assert.False(run.Seen[Cell(1, 6)]);
        return (run, BoardScene.Of(run, stats, yourTurn: true, hoverCell: -1));
    }

    /// <summary>
    /// You in the middle; a Carnotaurus beside you hunting, a second two cells off
    /// searching, dazed and bleeding (it is "Carnotaurus 2"), the apex far beyond sight
    /// (shown, H6), small game in sight; the cells to move to, the one to attack, and
    /// the mouse on a move cell.
    /// </summary>
    internal static (RunState Run, BoardScene Scene) HuntedScene()
    {
        var middle = Cell(5, 5);
        var beside = Cell(6, 5);
        var twoOff = Cell(3, 5);
        var apexAt = Cell(5, 0);
        var game = Cell(6, 7);
        var run = Runs.Fresh("Dryosaurus", 7).Clear().At(middle);
        foreach (var cell in new[] { middle, beside, twoOff, game }) run.Tiles[cell] = Terrain.Grass;
        run.Tiles[apexAt] = Terrain.Grass;
        Sight.Reveal(run, Stats.Of(run));
        run = run.Put(PredatorType.Carnotaurus, beside, Tuning.AlertSeen)
                 .Put(PredatorType.Carnotaurus, twoOff, 2)
                 .Put(PredatorType.Tyrannosaurus, apexAt, Tuning.AlertSeen)
                 .PutSmallGame(game);
        var second = run.Creatures.Single(c => c.At == twoOff);
        second.Dazed = true;
        second.Bleed = 2;
        second.BleedDamage = 3;

        var stats = Stats.Of(run);
        Assert.Equal(2, second.Ordinal);
        Assert.False(Sight.InSight(run, stats, apexAt));
        Assert.True(Sight.InSight(run, stats, game));
        var hover = Moves.Targets(run, stats).First();
        return (run, BoardScene.Of(run, stats, yourTurn: true, hover));
    }

    private static PixelBuffer Painted(BoardScene scene, ArtBook art)
    {
        var buffer = new PixelBuffer(BoardLayout.Width, BoardLayout.Height);
        BoardPainter.Paint(buffer, scene, art);
        return buffer;
    }

    // ---- Golden frames (A47), with no art --------------------------------------------------

    [Fact]
    public void BoardStart() => FrameGolden.Check(Painted(StartScene(), ArtBook.Empty), "Hatchling/board-start");

    [Fact]
    public void BoardFog()
    {
        var (_, scene) = FogScene();
        Assert.Contains(true, scene.FoodShown);   // the plant through the fog is in it
        FrameGolden.Check(Painted(scene, ArtBook.Empty), "Hatchling/board-fog");
    }

    [Fact]
    public void BoardHunted()
    {
        var (_, scene) = HuntedScene();
        Assert.Equal(3, scene.Sprites.Count(s => s.Kind == SpriteKind.Predator));
        Assert.Single(scene.AttackCells);
        FrameGolden.Check(Painted(scene, ArtBook.Empty), "Hatchling/board-hunted");
    }

    // ---- With the real art: paints, differs, not golden ------------------------------------

    [Fact]
    public void TheRealArtPaintsTheSameScenesAndDiffersFromTheStandIns()
    {
        foreach (var atlas in new[] { "creatures", "terrain", "things" })
            Assert.Contains(atlas, RealArt.Atlases);
        _output.WriteLine("Atlases embedded: " + string.Join(", ", RealArt.Atlases));
        var scenes = new (string Name, BoardScene Scene)[]
        {
            ("board-start", StartScene()), ("board-fog", FogScene().Scene), ("board-hunted", HuntedScene().Scene),
        };
        var folder = Environment.GetEnvironmentVariable(PreviewVariable);
        foreach (var (name, scene) in scenes)
        {
            var real = Painted(scene, RealArt);
            var standIn = Painted(scene, ArtBook.Empty);
            Assert.NotEqual(standIn.Pixels, real.Pixels);
            // Your dino: the creatures atlas draws it where the stand-in drew nothing.
            var you = scene.Sprites.Single(s => s.Kind == SpriteKind.You);
            Assert.Contains(Differ(standIn, real), p => p.X >= you.X - 18 && p.X < you.X + 18 && p.Y > you.Y - 25 && p.Y <= you.Y);
            if (!string.IsNullOrEmpty(folder))
            {
                FrameGolden.Save(real, Path.Combine(folder, $"{name}-real.png"));
                _output.WriteLine($"Wrote {Path.Combine(folder, $"{name}-real.png")}");
            }
        }
    }

    // ---- Nothing leaks (H6) -------------------------------------------------------------

    [Fact]
    public void APredatorThePlayerDoesNotKnowIsInNoPixel()
    {
        var (run, _) = HuntedScene();
        var far = Cell(10, 9);
        run.Tiles[far] = Terrain.Grass;
        var with = run.Put(PredatorType.Allosaurus, far, Tuning.AlertSeen);
        var stranger = with.Creatures[^1];
        var stats = Stats.Of(with);
        Assert.False(Sight.Knows(with, stats, stranger));

        // It is in no list and no pixel.
        var withScene = BoardScene.Of(with, stats, true, -1);
        var withoutScene = BoardScene.Of(run, Stats.Of(run), true, -1);
        Assert.DoesNotContain(withScene.Sprites, s => s.Creature == stranger.Id);
        Assert.DoesNotContain(far, withScene.AttackCells);
        Assert.DoesNotContain(far, withScene.MoveCells);
        Assert.Equal(withoutScene.AttackCells, withScene.AttackCells);
        Assert.Equal(Painted(withoutScene, ArtBook.Empty).Pixels, Painted(withScene, ArtBook.Empty).Pixels);
    }

    [Fact]
    public void TheApexBeyondSightIsDrawn()
    {
        var (run, scene) = HuntedScene();
        var apex = run.Creatures.Single(c => c.Type == Tuning.Apex);
        var without = run.Clone();
        without.Creatures.RemoveAll(c => c.Id == apex.Id);
        var withoutScene = BoardScene.Of(without, Stats.Of(without), true, scene.HoverCell);

        var sprite = scene.Sprites.Single(s => s.Creature == apex.Id);
        Assert.Equal(Ring.Apex, sprite.Ring);
        Assert.Equal(Marks.Hunt, sprite.Marks);
        Assert.NotEqual(Painted(withoutScene, ArtBook.Empty).Pixels, Painted(scene, ArtBook.Empty).Pixels);
    }

    // ---- Food (H29, H39) ------------------------------------------------------------------

    [Theory]
    [InlineData("Dryosaurus", true)]     // Good nose, a plant eater: plants through the fog
    [InlineData("Ceratosaurus", false)]  // Scavenger: carcasses through the fog
    [InlineData("Dryosaurus", false)]    // plain: only seen cells
    public void TheScenesFoodIsExactlyWhatSightShows(string species, bool nose)
    {
        var run = Runs.Fresh(species, 11).Clear();
        if (nose) run.Instincts.Add(Instinct.Nose);
        run.You.At = Cell(5, 2);
        run.Tiles[run.You.At] = Terrain.Grass;
        Sight.Reveal(run, Stats.Of(run));
        // Food on cells seen and unseen alike, so the fog has something to hide or show.
        for (var cell = 0; cell < Tuning.Cells; cell += 3) run.Plants[cell] = true;
        for (var cell = 1; cell < Tuning.Cells; cell += 4) run.Meat[cell] = 1 + cell % 3;

        var stats = Stats.Of(run);
        var scene = BoardScene.Of(run, stats, true, -1);
        for (var cell = 0; cell < Tuning.Cells; cell++)
        {
            Assert.Equal(Sight.ShowsFood(run, stats, cell, Food.Plants), scene.Plants[cell]);
            var meat = Sight.ShowsFood(run, stats, cell, Food.Meat);
            Assert.Equal(meat ? run.Meat[cell] : 0, scene.Meat[cell]);
            Assert.Equal(!run.Seen[cell] && (scene.Plants[cell] || scene.Meat[cell] > 0), scene.FoodShown[cell]);
        }

        var throughFog = Enumerable.Range(0, Tuning.Cells).Where(c => scene.FoodShown[c]).ToList();
        if (nose) Assert.Contains(throughFog, c => scene.Plants[c]);
        else if (species == "Ceratosaurus") Assert.True(throughFog.Count > 0 && throughFog.All(c => scene.Meat[c] > 0 && !scene.Plants[c]));
        else Assert.Empty(throughFog);
    }

    // ---- The predators' numbers (H38) ---------------------------------------------------

    /// <summary>Ten near-white digits, each its own blue so a test can tell which was drawn.</summary>
    private static readonly ArtBook Digits = MadeAtlas.Book(("things", MadeAtlas.Of(
        Enumerable.Range(0, 10).Select(d => ($"ordinal/digit/{d}", MadeAtlas.Solid(5, 7, DigitColour(d)), 0, 0)).ToArray())));

    private static uint DigitColour(int digit) => PixelBuffer.Colour(255, 255, (byte)(200 + digit * 5));

    /// <summary>One predator with this number, alone on a fogged board.</summary>
    private static BoardScene OnePredator(int ordinal, out SceneSprite sprite)
    {
        var scene = Blank(seen: false);
        var (x, y) = BoardLayout.Feet(Cell(5, 5));
        sprite = new SceneSprite { Creature = 1, Kind = SpriteKind.Predator, Subject = "Carnotaurus", Ring = Ring.Hostile, Ordinal = ordinal, X = x, Y = y, Row = 5 };
        scene.Sprites.Add(sprite);
        return scene;
    }

    /// <summary>The pixels that differ between two frames.</summary>
    private static List<(int X, int Y)> Differ(PixelBuffer a, PixelBuffer b) =>
        Enumerable.Range(0, a.Pixels.Length).Where(i => a.Pixels[i] != b.Pixels[i]).Select(i => (i % a.Width, i / a.Width)).ToList();

    [Fact]
    public void ANumberTwoDrawsOneDigitAtItsPlace()
    {
        var two = Painted(OnePredator(2, out var sprite), Digits);
        var none = Painted(OnePredator(1, out _), Digits);
        var x = sprite.X + 14;
        var y = sprite.Y - 9 + 12;   // the cell's centre plus (14, 12)
        var differ = Differ(none, two);
        Assert.Equal(35, differ.Count);
        Assert.All(differ, p => Assert.InRange(p.X, x, x + 4));
        Assert.All(differ, p => Assert.InRange(p.Y, y, y + 6));
        Assert.Equal(DigitColour(2), two.Pixels[y * two.Width + x]);
    }

    [Fact]
    public void ATwelveDrawsTwoDigitsTheFirstSixToTheLeft()
    {
        var twelve = Painted(OnePredator(12, out var sprite), Digits);
        var none = Painted(OnePredator(1, out _), Digits);
        var x = sprite.X + 14;
        var y = sprite.Y + 3;
        Assert.Equal(70, Differ(none, twelve).Count);
        Assert.Equal(DigitColour(2), twelve.Pixels[y * twelve.Width + x]);
        Assert.Equal(DigitColour(1), twelve.Pixels[y * twelve.Width + x - 6]);
        Assert.Equal(DigitColour(9), Painted(OnePredator(99, out _), Digits).Pixels[y * twelve.Width + x - 6]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(250)]
    public void OneAndAboveNinetyNineDrawNoDigit(int ordinal)
    {
        var none = Painted(OnePredator(1, out _), Digits);
        Assert.Empty(Differ(none, Painted(OnePredator(ordinal, out _), Digits)));
    }

    [Fact]
    public void WithNoArtNoDigitIsDrawnAndNothingThrows()
    {
        var none = Painted(OnePredator(1, out _), ArtBook.Empty);
        Assert.Empty(Differ(none, Painted(OnePredator(12, out _), ArtBook.Empty)));
        Assert.Empty(Differ(none, Painted(OnePredator(2, out _), ArtBook.Empty)));
    }

    [Fact]
    public void ANumberWithADigitMissingIsNotDrawnAtAll()
    {
        var noOne = MadeAtlas.Book(("things", MadeAtlas.Of(
            Enumerable.Range(0, 10).Where(d => d != 1).Select(d => ($"ordinal/digit/{d}", MadeAtlas.Solid(5, 7, DigitColour(d)), 0, 0)).ToArray())));
        var none = Painted(OnePredator(1, out _), noOne);
        Assert.Empty(Differ(none, Painted(OnePredator(12, out _), noOne)));   // never a lone "2" for 12
        Assert.Equal(35, Differ(none, Painted(OnePredator(2, out _), noOne)).Count);
    }

    // ---- The layout -------------------------------------------------------------------

    [Fact]
    public void TheBoardFitsTheSurface()
    {
        Assert.Equal(506, BoardLayout.Hexes.Width);
        Assert.Equal(430, BoardLayout.Hexes.Height);
        Assert.True(BoardLayout.OriginX + BoardLayout.Hexes.Width <= BoardLayout.Width);
        Assert.True(BoardLayout.OriginY + BoardLayout.Hexes.Height <= BoardLayout.Height);
        Assert.Equal(0, BoardLayout.Width % 4);
        Assert.Equal(0, BoardLayout.Height % 4);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(10, 10)]
    [InlineData(5, 5)]
    [InlineData(0, 5)]
    [InlineData(10, 5)]
    public void APlaceOnAnyCellStaysOnTheSurface(int column, int row)
    {
        var cell = Cell(column, row);
        var centre = BoardLayout.Centre(cell);
        foreach (var (x, y) in new[] { BoardLayout.Feet(cell), BoardLayout.Item(cell), BoardLayout.Mark(cell) })
        {
            Assert.InRange(x, 0, BoardLayout.Width - 1);
            Assert.InRange(y, 0, BoardLayout.Height - 1);
        }
        Assert.Equal((centre.X, centre.Y + 9), BoardLayout.Feet(cell));
        Assert.Equal((centre.X, centre.Y + 8), BoardLayout.Item(cell));
        Assert.Equal((centre.X, centre.Y - 19), BoardLayout.Mark(cell));
        Assert.Equal(cell, BoardLayout.Hexes.CellAt(centre.X, centre.Y));
    }

    [Fact]
    public void TheCellMaskIsTheEnginesOutline()
    {
        var spans = BoardLayout.CellSpans;
        Assert.Equal(BoardLayout.CellWidth * BoardLayout.RowPitch, spans.Sum(s => s.Length));   // every cell owns w * p pixels
        foreach (var span in spans)
            for (var x = span.X; x < span.X + span.Length; x++)
                Assert.True(HexLayout.InCell(x, span.Y, 44, 50, 38));
    }

    // ---- The stand-ins (H30), a few pixels each ------------------------------------------

    /// <summary>A board of grass, every cell seen and in sight (or all unseen), nothing on it.</summary>
    private static BoardScene Blank(bool seen = true) => new()
    {
        Tiles = Enumerable.Repeat(Terrain.Grass, Tuning.Cells).ToArray(),
        Seen = Enumerable.Repeat(seen, Tuning.Cells).ToArray(),
        InSight = Enumerable.Repeat(seen, Tuning.Cells).ToArray(),
        Plants = new bool[Tuning.Cells],
        Meat = new int[Tuning.Cells],
        FoodShown = new bool[Tuning.Cells],
    };

    private static uint At(PixelBuffer buffer, int x, int y) => buffer.Pixels[y * buffer.Width + x];

    private static uint Over(uint top, uint under)
    {
        var one = new PixelBuffer(1, 1);
        one.Clear(under);
        one.Fill(0, 0, 1, 1, top);
        return one.Pixels[0];
    }

    [Fact]
    public void TheGroundAndTheTilesStandIns()
    {
        var scene = Blank();
        scene.Tiles[Cell(1, 1)] = Terrain.Forest;
        scene.Tiles[Cell(2, 1)] = Terrain.Water;
        scene.Tiles[Cell(3, 1)] = Terrain.Rock;
        scene.Seen[Cell(4, 1)] = false;
        scene.InSight[Cell(4, 1)] = false;
        scene.InSight[Cell(5, 1)] = false;   // seen, out of sight: dim
        var frame = Painted(scene, ArtBook.Empty);

        Assert.Equal(HatchlingPalette.Ground, At(frame, 0, 0));   // the notch at the board's corner
        Assert.Equal(HatchlingPalette.Ground, At(frame, BoardLayout.Width - 1, BoardLayout.Height - 1));
        uint CentreOf(int column, int row) { var (x, y) = BoardLayout.Centre(Cell(column, row)); return At(frame, x, y); }
        Assert.Equal(HatchlingPalette.Grass, CentreOf(0, 1));
        Assert.Equal(HatchlingPalette.Forest, CentreOf(1, 1));
        Assert.Equal(HatchlingPalette.Water, CentreOf(2, 1));
        Assert.Equal(HatchlingPalette.Rock, CentreOf(3, 1));
        Assert.Equal(HatchlingPalette.Unseen, CentreOf(4, 1));
        Assert.Equal(Over(HatchlingPalette.Dim, HatchlingPalette.Grass), CentreOf(5, 1));

        // The tile is exactly the cell's outline: its box's corner is not the cell's, it is the neighbour's or the ground.
        var (left, top) = BoardLayout.Hexes.TopLeft(Cell(1, 1));
        Assert.Equal(HatchlingPalette.Forest, At(frame, left, top + 25));
        Assert.NotEqual(HatchlingPalette.Forest, At(frame, left, top));
    }

    [Fact]
    public void TheCellMarksStandInsAreTheRim()
    {
        var scene = Blank();
        scene.MoveCells.Add(Cell(2, 2));
        scene.AttackCells.Add(Cell(4, 2));
        scene.HoverCell = Cell(6, 2);
        var frame = Painted(scene, ArtBook.Empty);
        foreach (var (column, colour) in new[] { (2, HatchlingPalette.MoveRim), (4, HatchlingPalette.AttackRim), (6, HatchlingPalette.HoverRim) })
        {
            var (left, top) = BoardLayout.Hexes.TopLeft(Cell(column, 2));
            Assert.Equal(colour, At(frame, left, top + 25));          // the rim's two pixels at the side
            Assert.Equal(colour, At(frame, left + 1, top + 25));
            Assert.Equal(HatchlingPalette.Grass, At(frame, left + 2, top + 25));
            Assert.Equal(colour, At(frame, left + 22, top + 2));      // and at the top's point
            Assert.Equal(HatchlingPalette.Grass, At(frame, left + 22, top + 25));
        }
    }

    [Fact]
    public void TheItemsCreaturesAndMarksStandIns()
    {
        var scene = Blank();
        scene.Plants[Cell(2, 4)] = true;
        scene.Meat[Cell(4, 4)] = 2;
        SceneSprite Sprite(SpriteKind kind, int column, Ring ring, Marks marks)
        {
            var (x, y) = BoardLayout.Feet(Cell(column, 6));
            var sprite = new SceneSprite { Kind = kind, Subject = "nothing drawn", Ring = ring, Marks = marks, X = x, Y = y, Row = 6 };
            scene.Sprites.Add(sprite);
            return sprite;
        }
        var game = Sprite(SpriteKind.SmallGame, 1, Ring.None, Marks.None);
        var hunter = Sprite(SpriteKind.Predator, 3, Ring.Hostile, Marks.Hunt | Marks.Bleed);
        var apex = Sprite(SpriteKind.Predator, 5, Ring.Apex, Marks.Hunt);
        var you = Sprite(SpriteKind.You, 7, Ring.You, Marks.You);
        var acting = Sprite(SpriteKind.Predator, 9, Ring.Acting, Marks.Search);
        scene.ShowYourMark = true;
        var frame = Painted(scene, ArtBook.Empty);

        // A plant and a carcass: 7 x 7 diamonds on the item's point.
        var (px, py) = BoardLayout.Item(Cell(2, 4));
        Assert.Equal(HatchlingPalette.Plant, At(frame, px, py));
        Assert.Equal(HatchlingPalette.Plant, At(frame, px, py - 3));
        Assert.Equal(HatchlingPalette.Grass, At(frame, px - 1, py - 3));
        var (mx, my) = BoardLayout.Item(Cell(4, 4));
        Assert.Equal(HatchlingPalette.Meat, At(frame, mx + 3, my));
        Assert.Equal(HatchlingPalette.Grass, At(frame, mx + 4, my));

        // Small game: a 5 x 5 square standing on its feet; no ring.
        Assert.Equal(HatchlingPalette.SmallGame, At(frame, game.X, game.Y));
        Assert.Equal(HatchlingPalette.SmallGame, At(frame, game.X - 2, game.Y - 4));
        Assert.Equal(HatchlingPalette.Grass, At(frame, game.X, game.Y + 1));
        Assert.Equal(HatchlingPalette.Grass, At(frame, game.X - 14, game.Y));

        // Rings: a 28 x 10 ellipse outline round the feet, in the ring's colour; the inside is the ground.
        foreach (var (sprite, colour) in new[] { (hunter, HatchlingPalette.HostileRing), (apex, HatchlingPalette.ApexRing),
                     (you, HatchlingPalette.YouRing), (acting, HatchlingPalette.ActingRing) })
        {
            Assert.Equal(colour, At(frame, sprite.X - 14, sprite.Y));
            Assert.Equal(colour, At(frame, sprite.X + 13, sprite.Y - 1));
            Assert.Equal(colour, At(frame, sprite.X, sprite.Y - 5));
            Assert.Equal(HatchlingPalette.Grass, At(frame, sprite.X, sprite.Y + 2));
        }

        // Marks above a head: 5 x 5 squares in the ring's colour, 19 above the cell's centre, the next 10 to the right.
        int MarkY(SceneSprite s) => s.Y - 9 - 19;
        Assert.Equal(HatchlingPalette.HostileRing, At(frame, hunter.X, MarkY(hunter)));
        Assert.Equal(HatchlingPalette.HostileRing, At(frame, hunter.X + 10 + 2, MarkY(hunter) + 2));   // its bleeding beside it
        Assert.Equal(HatchlingPalette.Grass, At(frame, hunter.X + 3, MarkY(hunter)));
        Assert.Equal(HatchlingPalette.ApexRing, At(frame, apex.X - 2, MarkY(apex) - 2));
        Assert.Equal(HatchlingPalette.Grass, At(frame, apex.X + 10, MarkY(apex)));
        Assert.Equal(HatchlingPalette.YouRing, At(frame, you.X, MarkY(you)));
        Assert.Equal(HatchlingPalette.ActingRing, At(frame, acting.X, MarkY(acting)));
    }

    [Fact]
    public void YourMarkNeedsTheScenesSwitch()
    {
        var scene = Blank();
        var (x, y) = BoardLayout.Feet(Cell(5, 5));
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.You, Subject = "x", Ring = Ring.You, Marks = Marks.You, X = x, Y = y, Row = 5 });
        scene.ShowYourMark = false;
        Assert.Equal(HatchlingPalette.Grass, At(Painted(scene, ArtBook.Empty), x, y - 28));
        scene.ShowYourMark = true;
        Assert.Equal(HatchlingPalette.YouRing, At(Painted(scene, ArtBook.Empty), x, y - 28));
    }

    [Fact]
    public void AFadingCreatureFadesItsStandIns()
    {
        var scene = Blank();
        var (x, y) = BoardLayout.Feet(Cell(5, 5));
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.Predator, Subject = "x", Ring = Ring.Hostile, Marks = Marks.Hunt, X = x, Y = y, Row = 5, Opacity = 96 });
        var frame = Painted(scene, ArtBook.Empty);
        Assert.Equal(Over(HatchlingPalette.Faded(HatchlingPalette.HostileRing, 96), HatchlingPalette.Grass), At(frame, x - 14, y));
        Assert.Equal(Over(HatchlingPalette.Faded(HatchlingPalette.HostileRing, 96), HatchlingPalette.Grass), At(frame, x, y - 28));
    }

    [Fact]
    public void FadedIsHowTheEngineFadesASprite()
    {
        // A one-pixel "sprite" of each colour drawn at an opacity is the colour Faded by it, over transparency.
        foreach (var colour in new[] { HatchlingPalette.HostileRing, HatchlingPalette.Dim, HatchlingPalette.Plant })
        foreach (var opacity in new byte[] { 0, 1, 96, 128, 254, 255 })
        {
            var sheet = new PixelBuffer(1, 1);
            sheet.Clear(colour);
            var target = new PixelBuffer(1, 1);
            target.Draw(sheet, new PixelRect(0, 0, 1, 1), 0, 0, opacity: opacity);
            Assert.Equal(target.Pixels[0], HatchlingPalette.Faded(colour, opacity));
        }
    }

    // ---- The scene at rest -----------------------------------------------------------------

    [Fact]
    public void ThePredatorsMarksComeFromTheirAlertAndAfflictions()
    {
        var (run, scene) = HuntedScene();
        SceneSprite Of(int cell) => scene.Sprites.Single(s => s.Creature == run.Creatures.Single(c => c.At == cell).Id);
        Assert.Equal(Marks.Hunt, Of(Cell(6, 5)).Marks);
        Assert.Equal(Marks.Search | Marks.Dazed | Marks.Bleed, Of(Cell(3, 5)).Marks);
        Assert.Equal(Ring.Hostile, Of(Cell(6, 5)).Ring);
        Assert.Equal(2, Of(Cell(3, 5)).Ordinal);
        Assert.Equal(Ring.None, Of(Cell(6, 7)).Ring);
        Assert.Equal("smallgame", Of(Cell(6, 7)).Subject);
        Assert.Equal("Carnotaurus", Of(Cell(6, 5)).Subject);

        var you = scene.Sprites.Single(s => s.Kind == SpriteKind.You);
        Assert.Equal("Dryosaurus-s", you.Subject);   // H21: a hatchling's size
        Assert.Equal(Marks.You, you.Marks);
        Assert.Equal(BoardLayout.Feet(run.You.At), (you.X, you.Y));
        Assert.All(scene.Sprites, s => Assert.Equal((ArtNames.Stand, 0, false, (byte)255), (s.Action, s.Tick, s.Flip, s.Opacity)));
    }

    [Fact]
    public void YourMarksAndSizeFollowYourState()
    {
        var run = Runs.Fresh("Hypsilophodon", 3).Clear();
        run.You.Growth = 100;
        run.You.LastMove = MoveKind.Rest;   // Freeze: hidden after a rest (H13)
        var stats = Stats.Of(run);
        var you = BoardScene.Of(run, stats, false, -1).Sprites.Single(s => s.Kind == SpriteKind.You);
        Assert.Equal("Hypsilophodon", you.Subject);
        Assert.Equal(Marks.Hidden | Marks.Elder, you.Marks);   // not your turn: no "you"
    }

    /// <summary>The scene's marks of a choice are exactly the rules' answers (H53).</summary>
    private static void AssertTheRulesCells(RunState run)
    {
        var stats = Stats.Of(run);
        var scene = BoardScene.Of(run, stats, true, -1);
        Assert.Equal(Moves.Targets(run, stats), scene.MoveCells);
        Assert.Equal(Moves.Attackable(run, stats).Select(c => c.At), scene.AttackCells);
    }

    [Fact]
    public void ChoosingMarksExactlyTheRulesCellsAtAWalk()
    {
        var (run, scene) = HuntedScene();
        AssertTheRulesCells(run);
        Assert.Equal(new[] { Cell(6, 5) }, scene.AttackCells);
    }

    [Fact]
    public void ChoosingMarksExactlyTheRulesCellsAtASprint()
    {
        var run = Runs.Fresh("Dryosaurus", 7);
        run.Pace = Pace.Sprint;
        var stats = Stats.Of(run);
        AssertTheRulesCells(run);
        Assert.True(BoardScene.Of(run, stats, true, -1).MoveCells.Count > Island.Grid.Neighbours(run.You.At).Count);
    }

    [Fact]
    public void ASpittersTargetTwoCellsOffIsMarked()
    {
        var middle = Cell(5, 5);
        var twoOff = Cell(7, 5);
        var run = Runs.Fresh("Dilophosaurus", 7).Clear().At(middle);
        run.Tiles[middle] = Terrain.Grass;
        run.Tiles[twoOff] = Terrain.Grass;
        Sight.Reveal(run, Stats.Of(run));
        run = run.Put(PredatorType.Carnotaurus, twoOff, Tuning.AlertSeen);
        Assert.Equal(2, Island.Grid.Distance(middle, twoOff));
        Assert.True(Sight.Knows(run, Stats.Of(run), run.Creatures[^1]));
        AssertTheRulesCells(run);
        Assert.Contains(twoOff, BoardScene.Of(run, Stats.Of(run), true, -1).AttackCells);   // Spit: "beside you" would not give it
    }

    [Fact]
    public void NoChoiceIsMarkedOffYourTurnWithAnOfferOrOnceTheRunIsOver()
    {
        var (run, _) = HuntedScene();
        var stats = Stats.Of(run);
        var hover = Moves.Targets(run, stats)[0];

        var choosing = BoardScene.Of(run, stats, true, hover);
        Assert.Equal(hover, choosing.HoverCell);
        Assert.True(choosing.ShowYourMark);
        Assert.Equal(-1, BoardScene.Of(run, stats, true, Tuning.Cells).HoverCell);   // off the board: none

        var offer = run.Clone();
        offer.Offer = new List<Instinct> { Instinct.Eyes, Instinct.Feet, Instinct.Stride };
        var over = run.Clone();
        over.Over = new RunOver { Reason = EndReason.Caught };
        foreach (var scene in new[]
                 {
                     BoardScene.Of(run, stats, false, hover),
                     BoardScene.Of(offer, stats, true, hover),
                     BoardScene.Of(over, stats, true, hover),
                 })
        {
            Assert.Empty(scene.MoveCells);
            Assert.Empty(scene.AttackCells);
            Assert.Equal(-1, scene.HoverCell);
        }

        // An open offer keeps mark/you: it is still you who chooses (H53).
        var offered = BoardScene.Of(offer, stats, true, hover);
        Assert.True(offered.ShowYourMark);
        Assert.True(offered.Sprites.Single(s => s.Kind == SpriteKind.You).Marks.HasFlag(Marks.You));

        // A run that is over never shows it, even on "your turn".
        var ended = BoardScene.Of(over, stats, true, hover);
        Assert.False(ended.ShowYourMark);
        Assert.False(ended.Sprites.Single(s => s.Kind == SpriteKind.You).Marks.HasFlag(Marks.You));

        Assert.False(BoardScene.Of(run, stats, false, hover).ShowYourMark);
    }

    // ---- Every mark above a head has a place of its own (H52) -------------------------------

    /// <summary>The pixels where two frames differ lie all inside the 5 x 5 square centred on (x, y).</summary>
    private static void DifferOnlyInTheSquareAt(PixelBuffer a, PixelBuffer b, int x, int y)
    {
        var differ = Differ(a, b);
        Assert.Equal(25, differ.Count);
        Assert.All(differ, p => Assert.True(Math.Abs(p.X - x) <= 2 && Math.Abs(p.Y - y) <= 2, $"({p.X}, {p.Y})"));
    }

    private static PixelBuffer OneSprite(SpriteKind kind, Ring ring, Marks marks, bool showYourMark, out int x, out int y)
    {
        var scene = Blank();
        (x, y) = BoardLayout.Feet(Cell(5, 5));
        scene.Sprites.Add(new SceneSprite { Kind = kind, Subject = "x", Ring = ring, Marks = marks, X = x, Y = y, Row = 5 });
        scene.ShowYourMark = showYourMark;
        y -= 28;   // the mark's point: the feet minus (0, 28), 19 above the cell's centre
        return Painted(scene, ArtBook.Empty);
    }

    [Fact]
    public void AnAfflictionStandsTenRightOfTheAlertsPlaceWithOrWithoutTheAlert()
    {
        var bleeding = OneSprite(SpriteKind.Predator, Ring.Hostile, Marks.Bleed, false, out var x, out var y);
        Assert.Equal(HatchlingPalette.HostileRing, At(bleeding, x + 10, y));
        Assert.Equal(HatchlingPalette.Grass, At(bleeding, x, y));   // the alert's place stays empty
        var hunting = OneSprite(SpriteKind.Predator, Ring.Hostile, Marks.Hunt | Marks.Bleed, false, out _, out _);
        DifferOnlyInTheSquareAt(bleeding, hunting, x, y);
    }

    [Fact]
    public void YourMarksStandOnThePointTenLeftAndTenRight()
    {
        var all = OneSprite(SpriteKind.You, Ring.You, Marks.You | Marks.Hidden | Marks.Elder, true, out var x, out var y);
        Assert.Equal(HatchlingPalette.YouRing, At(all, x, y));
        Assert.Equal(HatchlingPalette.YouRing, At(all, x - 10, y));   // hidden
        Assert.Equal(HatchlingPalette.YouRing, At(all, x + 10, y));   // Elder
        Assert.Equal(HatchlingPalette.Grass, At(all, x - 5, y));
        Assert.Equal(HatchlingPalette.Grass, At(all, x + 5, y));

        // mark/you switched off by the playback: nothing else moves.
        var noYou = OneSprite(SpriteKind.You, Ring.You, Marks.You | Marks.Hidden | Marks.Elder, false, out _, out _);
        DifferOnlyInTheSquareAt(all, noYou, x, y);
        Assert.Equal(HatchlingPalette.YouRing, At(noYou, x - 10, y));
        Assert.Equal(HatchlingPalette.YouRing, At(noYou, x + 10, y));
    }

    [Fact]
    public void SpritesAreDrawnTopRowFirstThenLeftToRightSmallGameUnderPredatorsUnderYou()
    {
        var scene = Blank();
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.You, Row = 3, X = 50 });
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.Predator, Row = 3, X = 50 });
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.SmallGame, Row = 3, X = 50 });
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.Predator, Row = 3, X = 20 });
        scene.Sprites.Add(new SceneSprite { Kind = SpriteKind.You, Row = 1, X = 400 });
        scene.Sort();
        Assert.Equal(new[] { (1, 400, SpriteKind.You), (3, 20, SpriteKind.Predator), (3, 50, SpriteKind.SmallGame),
                (3, 50, SpriteKind.Predator), (3, 50, SpriteKind.You) },
            scene.Sprites.Select(s => (s.Row, s.X, s.Kind)));
    }

    // ---- Cost -----------------------------------------------------------------------------

    [Fact]
    public void AFrameAllocatesNothing()
    {
        var (_, scene) = HuntedScene();
        scene.Numbers.Add(new SceneNumber { Value = 12, X = 200, Y = 200 });
        var buffer = new PixelBuffer(BoardLayout.Width, BoardLayout.Height);
        foreach (var art in new[] { ArtBook.Empty, RealArt, Digits })
        {
            for (var warm = 0; warm < 3; warm++) BoardPainter.Paint(buffer, scene, art);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++) BoardPainter.Paint(buffer, scene, art);
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
    }
}
