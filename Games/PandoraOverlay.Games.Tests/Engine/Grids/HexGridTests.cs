using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The hex grid against the prototype it was ported from: the neighbour lists and
/// their order, the distances over a whole 11 x 11 board, the breadth-first walks
/// on hand-built boards, and the edges (cells off the board throw). A03.
///
/// <para>Every pinned number here was printed by running the prototype's own code
/// in Node (<c>HX_NEAR</c>, <c>HX_DIST</c>, <c>hxStepToward</c> and
/// <c>hxMoveTargets</c> from <c>prototype\hatchling\tools\hatch-rules.js</c>, whose
/// board is 11 x 11). A03's report holds the script. The few lists on other board
/// sizes are worked out by hand from the offsets and say so.</para>
/// </summary>
public class HexGridTests
{
    private static readonly HexGrid Board = new(11, 11);

    // ---- Cells ------------------------------------------------------------------------

    /// <summary>A cell is row * Columns + column, and Index answers -1 off the board.</summary>
    [Fact]
    public void ACellIsRowTimesColumnsPlusColumn()
    {
        var grid = new HexGrid(5, 4);
        Assert.Equal(20, grid.Count);
        for (var cell = 0; cell < grid.Count; cell++)
        {
            Assert.Equal(cell % 5, grid.Column(cell));
            Assert.Equal(cell / 5, grid.Row(cell));
            Assert.Equal(cell, grid.Index(grid.Column(cell), grid.Row(cell)));
        }
        Assert.Equal(-1, grid.Index(-1, 0));
        Assert.Equal(-1, grid.Index(5, 0));
        Assert.Equal(-1, grid.Index(0, -1));
        Assert.Equal(-1, grid.Index(0, 4));
    }

    /// <summary>A grid has at least one column and one row.</summary>
    [Fact]
    public void AnEmptyGridThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HexGrid(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HexGrid(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HexGrid(-1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HexGrid(int.MaxValue, 2));
    }

    /// <summary>A cell off the board is a caller's bug: every member that takes one throws.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    [InlineData(int.MaxValue)]
    public void ACellOffTheBoardThrows(int bad)
    {
        Func<int, bool> all = _ => true;
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Column(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Row(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Neighbours(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Distance(bad, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Distance(0, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Within(bad, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Reach(bad, 2, all));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.FirstStepToward(bad, _ => false, all));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Path(bad, 0, all));
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Path(0, bad, all));
    }

    // ---- Neighbours -------------------------------------------------------------------

    /// <summary>
    /// The prototype's <c>HX_NEAR</c> for corners (0, 10, 110, 120: even rows), edges
    /// (11, 21: odd rows; 5, 115: even) and middles (49: an even row, 60 and 61: odd),
    /// order included: upper-left, upper-right, left, right, lower-left, lower-right.
    /// </summary>
    [Theory]
    [InlineData(0, new[] { 1, 11 })]
    [InlineData(10, new[] { 9, 20, 21 })]
    [InlineData(110, new[] { 99, 111 })]
    [InlineData(120, new[] { 108, 109, 119 })]
    [InlineData(11, new[] { 0, 1, 12, 22, 23 })]
    [InlineData(21, new[] { 10, 20, 32 })]
    [InlineData(5, new[] { 4, 6, 15, 16 })]
    [InlineData(115, new[] { 103, 104, 114, 116 })]
    [InlineData(49, new[] { 37, 38, 48, 50, 59, 60 })]
    [InlineData(60, new[] { 49, 50, 59, 61, 71, 72 })]
    [InlineData(61, new[] { 50, 51, 60, 62, 72, 73 })]
    public void TheNeighboursAreThePrototypes(int cell, int[] expected)
    {
        Assert.Equal(expected, Board.Neighbours(cell));
    }

    /// <summary>
    /// The odd rows' corners, which an 11-row board doesn't have: on 5 x 4 the last
    /// row is odd. Worked out by hand from the odd row's offsets (0,-1) (1,-1) (-1,0)
    /// (1,0) (0,1) (1,1).
    /// </summary>
    [Theory]
    [InlineData(15, new[] { 10, 11, 16 })]
    [InlineData(19, new[] { 14, 18 })]
    [InlineData(0, new[] { 1, 5 })]
    [InlineData(4, new[] { 3, 8, 9 })]
    [InlineData(6, new[] { 1, 2, 5, 7, 11, 12 })]
    [InlineData(12, new[] { 6, 7, 11, 13, 16, 17 })]
    public void TheNeighboursOfAnOddLastRow(int cell, int[] expected)
    {
        Assert.Equal(expected, new HexGrid(5, 4).Neighbours(cell));
    }

    /// <summary>Being neighbours goes both ways, no cell neighbours itself, and every cell off the edges has all six.</summary>
    [Theory]
    [InlineData(11, 11)]
    [InlineData(5, 4)]
    [InlineData(1, 1)]
    [InlineData(1, 6)]
    [InlineData(7, 1)]
    public void NeighboursGoBothWays(int columns, int rows)
    {
        var grid = new HexGrid(columns, rows);
        for (var a = 0; a < grid.Count; a++)
        {
            var near = grid.Neighbours(a);
            Assert.InRange(near.Count, 0, 6);
            Assert.DoesNotContain(a, near);
            Assert.Equal(near.Count, near.Distinct().Count());
            foreach (var b in near)
                Assert.Contains(a, grid.Neighbours(b));

            var c = grid.Column(a);
            var r = grid.Row(a);
            if (c > 0 && r > 0 && c < columns - 1 && r < rows - 1)
                Assert.Equal(6, near.Count);
        }
    }

    /// <summary>The list handed out can't be changed by its caller.</summary>
    [Fact]
    public void TheNeighboursCantBeChanged()
    {
        var near = Board.Neighbours(60);
        Assert.False(near is int[]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)near)[0] = 0);
    }

    // ---- Distance ---------------------------------------------------------------------

    /// <summary>
    /// The whole 11 x 11 table equals the prototype's <c>HX_DIST</c>: its sum, and a
    /// sum weighted by position so a moved entry would show (Node: 86064 and
    /// 630074544, the weight of [a][b] being a * 121 + b + 1).
    /// </summary>
    [Fact]
    public void TheDistanceTableIsThePrototypes()
    {
        long sum = 0, weighted = 0;
        for (var a = 0; a < Board.Count; a++)
        {
            for (var b = 0; b < Board.Count; b++)
            {
                var d = Board.Distance(a, b);
                sum += d;
                weighted += d * (long)(a * Board.Count + b + 1);
            }
        }
        Assert.Equal(86064, sum);
        Assert.Equal(630074544, weighted);
    }

    /// <summary>A dozen pairs from the prototype's <c>HX_DIST</c>.</summary>
    [Theory]
    [InlineData(0, 120, 15)]
    [InlineData(0, 10, 10)]
    [InlineData(10, 110, 15)]
    [InlineData(5, 60, 5)]
    [InlineData(60, 61, 1)]
    [InlineData(60, 49, 1)]
    [InlineData(60, 50, 1)]
    [InlineData(11, 0, 1)]
    [InlineData(12, 0, 2)]
    [InlineData(33, 87, 12)]
    [InlineData(119, 1, 13)]
    [InlineData(64, 26, 7)]
    public void DistancePairsAreThePrototypes(int a, int b, int expected)
    {
        Assert.Equal(expected, Board.Distance(a, b));
    }

    /// <summary>Symmetric, zero only from a cell to itself, and exactly 1 for neighbours and only for them.</summary>
    [Theory]
    [InlineData(11, 11)]
    [InlineData(5, 4)]
    [InlineData(9, 2)]
    public void DistanceIsAMeasureOfSteps(int columns, int rows)
    {
        var grid = new HexGrid(columns, rows);
        for (var a = 0; a < grid.Count; a++)
        {
            for (var b = 0; b < grid.Count; b++)
            {
                var d = grid.Distance(a, b);
                Assert.Equal(d, grid.Distance(b, a));
                Assert.Equal(a == b, d == 0);
                Assert.Equal(grid.Neighbours(a).Contains(b), d == 1);
            }
        }
    }

    /// <summary>On an empty board the distance IS the breadth-first step count.</summary>
    [Fact]
    public void DistanceIsTheStepsOfAnEmptyBoard()
    {
        for (var a = 0; a < Board.Count; a += 7)
        {
            var reach = Board.Reach(a, 100, _ => true);
            for (var b = 0; b < Board.Count; b++)
                Assert.Equal(Board.Distance(a, b), a == b ? 0 : reach[b]);
        }
    }

    // ---- Within -----------------------------------------------------------------------

    /// <summary>Every cell within the radius, the centre included, in rising cell order: the brute force over all cells, on both boards.</summary>
    [Theory]
    [InlineData(11, 11, 60, 0)]
    [InlineData(11, 11, 60, 1)]
    [InlineData(11, 11, 60, 3)]
    [InlineData(11, 11, 0, 2)]
    [InlineData(11, 11, 120, 4)]
    [InlineData(11, 11, 11, 15)]
    [InlineData(11, 11, 11, 200)]
    [InlineData(5, 4, 12, 1)]
    [InlineData(5, 4, 19, 2)]
    public void WithinIsEveryCellInTheRadius(int columns, int rows, int centre, int radius)
    {
        var grid = new HexGrid(columns, rows);
        var expected = Enumerable.Range(0, grid.Count).Where(c => grid.Distance(centre, c) <= radius);
        Assert.Equal(expected, grid.Within(centre, radius));
    }

    /// <summary>Radius 0 is the centre alone; one step out adds its neighbours; a negative radius holds nothing.</summary>
    [Fact]
    public void WithinAtTheSmallRadii()
    {
        Assert.Equal(new[] { 60 }, Board.Within(60, 0));
        Assert.Equal(new[] { 49, 50, 59, 60, 61, 71, 72 }, Board.Within(60, 1));
        Assert.Empty(Board.Within(60, -1));
    }

    // ---- Reach ------------------------------------------------------------------------

    /// <summary>The wall of rock across row 4 (cells 47 to 53) on the prototype's board.</summary>
    private static readonly int[] Wall = { 47, 48, 49, 50, 51, 52, 53 };

    /// <summary>
    /// The prototype's sprint (<c>hxMoveTargets</c>: rocks and creatures block) round
    /// a wall, by a corner, and boxed in, cells AND order: the order the walk reached
    /// them. Node printed each list.
    /// </summary>
    [Theory]
    [InlineData("wall", 38, 2, new[] { 27, 28, 37, 39, 15, 16, 26, 17, 29, 36, 40 })]
    [InlineData("wall", 38, 4, new[] { 27, 28, 37, 39, 15, 16, 26, 17, 29, 36, 40, 4, 5, 14, 6, 25, 7, 18, 30, 35, 41, 3, 13, 24, 8, 19, 31, 34, 46, 42 })]
    [InlineData("corner", 0, 3, new[] { 1, 11, 23, 24, 33, 34 })]
    [InlineData("boxed", 60, 3, new int[0])]
    public void ReachIsThePrototypesSprint(string board, int from, int maxSteps, int[] expected)
    {
        var blocked = board switch
        {
            "wall" => Wall,
            "corner" => new[] { 12, 22, 2 },          // rocks 12 and 22, a creature on 2
            _ => new[] { 48, 49, 59, 61, 71, 72, 50 }, // rocks round 60, a creature on 50
        };

        var reach = Board.Reach(from, maxSteps, cell => !blocked.Contains(cell));

        Assert.Equal(expected, reach.Keys);
        Assert.Equal(expected, reach.Select(pair => pair.Key));
        Assert.Equal(expected.Length, reach.Count);
    }

    /// <summary>
    /// Round the wall the step counts are the walk's: each is the length of a
    /// shortest path, the start is left out, nothing is past the limit, and no cell
    /// within the limit is missing.
    /// </summary>
    [Theory]
    [InlineData(38, 1)]
    [InlineData(38, 3)]
    [InlineData(38, 6)]
    [InlineData(60, 4)]
    [InlineData(49, 5)] // starting on the wall itself: the start need not be passable
    public void ReachCountsTheStepsRoundAWall(int from, int maxSteps)
    {
        Func<int, bool> passable = cell => !Wall.Contains(cell);
        var reach = Board.Reach(from, maxSteps, passable);

        Assert.False(reach.ContainsKey(from));
        for (var cell = 0; cell < Board.Count; cell++)
        {
            if (cell == from)
                continue;
            var path = Board.Path(from, cell, passable);
            var within = path.Count > 0 && path.Count <= maxSteps;
            Assert.Equal(within, reach.ContainsKey(cell));
            if (within)
            {
                Assert.Equal(path.Count, reach[cell]);
                Assert.True(reach.TryGetValue(cell, out var steps));
                Assert.Equal(path.Count, steps);
            }
        }
        Assert.Equal(reach.Keys.Select(k => reach[k]), reach.Values);
        Assert.Equal(reach.Values.OrderBy(v => v), reach.Values); // nearest first
    }

    /// <summary>The start is never asked whether it is passable; a walk of 0 steps reaches nothing.</summary>
    [Fact]
    public void ReachNeverAsksAboutTheStart()
    {
        var asked = new List<int>();
        Board.Reach(60, 2, cell => { asked.Add(cell); return true; });
        Assert.DoesNotContain(60, asked);

        Assert.Empty(Board.Reach(60, 0, _ => true));
        Assert.Empty(Board.Reach(60, -3, _ => true));
        Assert.Throws<ArgumentNullException>(() => Board.Reach(60, 2, null!));
    }

    // ---- FirstStepToward --------------------------------------------------------------

    /// <summary>
    /// The prototype's <c>hxStepToward</c> on hand-built boards. Its rule, rebuilt
    /// from the grid's parts: the goal is the target, or, when the target is the
    /// one being hunted, any cell next to them; rock, the hunted one's cell and
    /// every other creature's cell block the way. Node printed every answer.
    /// </summary>
    [Theory]
    // open: no rock, the hunted one on 60
    [InlineData("", 60, "", 0, 60, 1)]
    [InlineData("", 60, "", 120, 60, 108)]
    [InlineData("", 60, "", 5, 60, 15)]
    [InlineData("", 60, "", 62, 60, 61)]
    [InlineData("", 60, "", 0, 120, 1)]
    [InlineData("", 60, "", 61, 60, -1)]
    // a wall across row 4, the hunted one on 82
    [InlineData("47 48 49 50 51 52 53", 82, "", 38, 82, 37)]
    [InlineData("47 48 49 50 51 52 53", 82, "", 27, 82, 26)]
    [InlineData("47 48 49 50 51 52 53", 82, "", 16, 82, 15)]
    [InlineData("47 48 49 50 51 52 53", 82, "", 38, 93, 37)]
    // a ring of rock round 60 with one gap (50)
    [InlineData("48 49 59 61 71 72", 60, "", 0, 60, 1)]
    [InlineData("48 49 59 61 71 72", 60, "", 104, 60, 94)]
    [InlineData("48 49 59 61 71 72", 60, "", 37, 60, 38)]
    // other creatures in the way
    [InlineData("24 35 46", 70, "58 69 81", 13, 70, 25)]
    [InlineData("24 35 46", 70, "58 69 81", 36, 70, 48)]
    [InlineData("24 35 46", 70, "58 69 81", 100, 70, 90)]
    [InlineData("24 35 46", 70, "58 69 81", 57, 70, 47)]
    // the two top rows all rock
    [InlineData("0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21", 60, "", 22, 60, 23)]
    // 60 sealed in: nothing reaches it, nothing leaves it
    [InlineData("48 49 50 59 61 70 71 72", 120, "", 60, 120, -1)]
    [InlineData("48 49 50 59 61 70 71 72", 120, "", 0, 60, -1)]
    [InlineData("48 49 50 59 61 70 71 72", 120, "", 0, 120, 1)]
    public void FirstStepTowardIsThePrototypes(string rocks, int hunted, string others, int from, int target, int expected)
    {
        var rock = Cells(rocks);
        var creatures = Cells(others);
        bool IsGoal(int cell) => cell == target || (target == hunted && Board.Distance(cell, target) == 1);
        bool Passable(int cell) => !rock.Contains(cell) && cell != hunted && !creatures.Contains(cell);

        Assert.Equal(expected, Board.FirstStepToward(from, IsGoal, Passable));
    }

    /// <summary>From a goal there is nowhere to go: -1, and the walk is never started.</summary>
    [Fact]
    public void FirstStepFromAGoalIsNone()
    {
        var asked = 0;
        Assert.Equal(-1, Board.FirstStepToward(60, cell => cell == 60, _ => { asked++; return true; }));
        Assert.Equal(0, asked);
    }

    /// <summary>A goal the walk can't enter is never reached, and never asked whether it is a goal.</summary>
    [Fact]
    public void AGoalMustBePassable()
    {
        var askedAboutGoal = false;
        var step = Board.FirstStepToward(0,
            cell => { if (cell == 120) askedAboutGoal = true; return cell == 120; },
            cell => cell != 120);
        Assert.Equal(-1, step);
        Assert.False(askedAboutGoal);
    }

    /// <summary>
    /// Ties between goals at one distance go to the one the neighbour order meets
    /// first: from 60, goals at 49 (upper-left) and 72 (lower-right) both one step
    /// away, 49 wins; between 71 (lower-left) and 72 (lower-right), 71; and two
    /// steps out, the goal met first decides the first step.
    /// </summary>
    [Fact]
    public void TiesGoToTheNeighbourOrder()
    {
        Assert.Equal(49, Board.FirstStepToward(60, c => c == 49 || c == 72, _ => true));
        Assert.Equal(71, Board.FirstStepToward(60, c => c == 72 || c == 71, _ => true));
        // 38 (above, by 49) and 83 (below, by 72) are both two steps away; the walk
        // goes on from 49 before 72, so it meets 38 first.
        Assert.Equal(49, Board.FirstStepToward(60, c => c == 38 || c == 83, _ => true));
    }

    /// <summary>The first step agrees with the first cell of the path to one goal, on boards with rock, for every pair.</summary>
    [Fact]
    public void FirstStepIsThePathsFirstCell()
    {
        var rock = Cells("24 35 46 47 48 49 50 51 52 53 70 81 92");
        Func<int, bool> passable = cell => !rock.Contains(cell);
        for (var from = 0; from < Board.Count; from += 3)
        {
            for (var to = 0; to < Board.Count; to++)
            {
                var path = Board.Path(from, to, passable);
                var step = Board.FirstStepToward(from, cell => cell == to, passable);
                Assert.Equal(path.Count > 0 ? path[0] : -1, step);
            }
        }
    }

    // ---- Path -------------------------------------------------------------------------

    /// <summary>
    /// A path is a walk: every cell next to the one before it, all passable, ending
    /// on the target, as long as the breadth-first distance (the step count Reach
    /// gives), round a wall and through a gap.
    /// </summary>
    [Fact]
    public void APathIsAShortestWalk()
    {
        var rock = Cells("47 48 49 50 51 52 53 58 59 60 61 62");
        Func<int, bool> passable = cell => !rock.Contains(cell);
        for (var from = 0; from < Board.Count; from += 5)
        {
            var reach = Board.Reach(from, Board.Count, passable);
            for (var to = 0; to < Board.Count; to++)
            {
                var path = Board.Path(from, to, passable);
                if (to == from || !reach.ContainsKey(to))
                {
                    Assert.Empty(path);
                    continue;
                }

                Assert.Equal(reach[to], path.Count);
                Assert.Equal(to, path[^1]);
                var at = from;
                foreach (var cell in path)
                {
                    Assert.Contains(cell, Board.Neighbours(at));
                    Assert.True(passable(cell));
                    at = cell;
                }
            }
        }
    }

    /// <summary>On an empty board a path is as long as the distance.</summary>
    [Fact]
    public void APathOnAnEmptyBoardIsTheDistance()
    {
        for (var from = 0; from < Board.Count; from += 4)
            for (var to = 0; to < Board.Count; to++)
                Assert.Equal(Board.Distance(from, to), Board.Path(from, to, _ => true).Count);
    }

    /// <summary>
    /// Ties by neighbour order: 0 to 12 is two steps by 1 or by 11; the walk meets
    /// 1 (right) before 11 (lower-right), so the path goes by 1.
    /// </summary>
    [Fact]
    public void APathBreaksTiesByNeighbourOrder()
    {
        Assert.Equal(new[] { 1, 12 }, Board.Path(0, 12, _ => true));
        Assert.Equal(new[] { 11, 12 }, Board.Path(0, 12, cell => cell != 1));
    }

    /// <summary>No path to itself, none through a sealed ring, none to a target that can't be entered; the start need not be passable.</summary>
    [Fact]
    public void APathCanBeEmpty()
    {
        var ring = Cells("49 50 59 61 71 72");
        Assert.Empty(Board.Path(60, 60, _ => true));
        Assert.Empty(Board.Path(0, 60, cell => !ring.Contains(cell)));
        Assert.Empty(Board.Path(60, 0, cell => !ring.Contains(cell)));
        Assert.Empty(Board.Path(0, 2, cell => cell != 2));
        Assert.Equal(new[] { 1, 2 }, Board.Path(0, 2, cell => cell != 0));
        Assert.Throws<ArgumentNullException>(() => Board.Path(0, 2, null!));
    }

    /// <summary>A board's cells written as "1 2 3".</summary>
    private static HashSet<int> Cells(string cells) =>
        cells.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
}
