using Xunit;

namespace PandoraOverlay.Tests;

public sealed class AreaMapTests
{
    private const string Red = "#FF0000", Green = "#00FF00", Blue = "#0000FF";

    private static readonly AreaLegend ThreeAreas = new("test", "2026-10-02", new[]
    {
        new AreaEntry("Alpha", Red, 0, 0), new AreaEntry("Beta", Green, 0, 0), new AreaEntry("Gamma", Blue, 0, 0)
    });


    /// <summary>A square BGRA image from a per-pixel "#RRGGBB" (null = transparent).</summary>
    private static byte[] Pixels(int size, Func<int, int, string?> colourAt)
    {
        var bgra = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (colourAt(x, y) is not { } hex || !AreaMap.TryParseColour(hex, out var rgb)) continue;
                var o = (y * size + x) * 4;
                bgra[o] = (byte)rgb;
                bgra[o + 1] = (byte)(rgb >> 8);
                bgra[o + 2] = (byte)(rgb >> 16);
                bgra[o + 3] = 255;
            }
        }
        return bgra;
    }

    /// <summary>Vertical bands across a 100 px map: each entry is (first column, colour).</summary>
    private static AreaMap Bands(params (int From, string? Colour)[] bands) =>
        AreaMap.FromPixels(100, 100, Pixels(100, (x, _) => bands.Last(b => x >= b.From).Colour), ThreeAreas)!;

    private static double At(double pixel) => pixel / 100; // the fraction of a pixel position on the 100 px test maps

    // ---- The grid -------------------------------------------------------------

    [Fact]
    public void APixelsColourNamesItsArea()
    {
        var map = Bands((0, Red), (50, Green));

        Assert.Equal("Alpha", map.NameAt(At(10.5), At(40.5)));
        Assert.Equal("Beta", map.NameAt(At(80.5), At(40.5)));
        Assert.Equal("Alpha", map.NameAt(At(49.9), 0.5)); // the last column of the left band
        Assert.Equal("Beta", map.NameAt(At(50), 0.5));    // the first of the right
        Assert.Equal(0, map.UnknownPixels);
    }

    [Fact]
    public void TransparentIsNoArea()
    {
        var map = Bands((0, Red), (50, null));

        Assert.Null(map.NameAt(At(75), 0.5));
        Assert.Equal(AreaMap.None, map.IndexAt(At(75), 0.5));
        Assert.Equal(0, map.UnknownPixels); // empty is not unknown
    }

    [Fact]
    public void AColourOutsideTheLegendIsNoAreaAndIsCounted()
    {
        var bgra = Pixels(4, (x, _) => x < 2 ? Red : "#FE0101"); // a red picked by eye, not the legend's
        bgra[3] = 128;                                           // and one half-transparent pixel of the real red: a soft brush edge

        var map = AreaMap.FromPixels(4, 4, bgra, ThreeAreas)!;

        Assert.Null(map.NameAt(0.9, 0.5));
        Assert.Null(map.NameAt(0.01, 0.01));
        Assert.Equal("Alpha", map.NameAt(0.3, 0.5));
        Assert.Equal(8 + 1, map.UnknownPixels);
    }

    [Fact]
    public void APositionOffTheMapReadsItsEdge()
    {
        var map = Bands((0, Red), (50, Green));

        Assert.Equal("Alpha", map.NameAt(-0.2, 0.5));
        Assert.Equal("Beta", map.NameAt(1.0, 1.0)); // exactly 1 is the last pixel, not one past it
        Assert.Equal("Beta", map.NameAt(3, 0.5));
        Assert.Null(map.NameAt(double.NaN, 0.5));
    }

    [Fact]
    public void AnImageThatIsNotSquareOrIsShortIsRefused()
    {
        Assert.Null(AreaMap.FromPixels(4, 3, new byte[48], ThreeAreas));
        Assert.Null(AreaMap.FromPixels(4, 4, new byte[10], ThreeAreas));
        Assert.Null(AreaMap.FromPixels(0, 0, Array.Empty<byte>(), ThreeAreas));
    }

    // ---- The legend -----------------------------------------------------------

    [Fact]
    public void TheLegendKeepsUsableEntriesOnly()
    {
        var legend = AreaLegend.Parse("""
            { "Source": "s", "CopiedOn": "2026-09-30", "Size": 1000, "Areas": [
              { "Name": " Somewhere ", "Colour": "#F0A3FF", "X": 177000, "Y": 33000 },
              { "Name": "", "Colour": "#000000" },
              { "Name": "No colour" },
              { "Name": "Bad colour", "Colour": "pink" } ] }
            """)!;

        var only = Assert.Single(legend.Areas);
        Assert.Equal("Somewhere", only.Name);
        Assert.Equal(177000, only.X);
        Assert.Equal("2026-09-30", legend.CopiedOn);
        Assert.Null(legend.Calibration); // none given
    }

    [Fact]
    public void TheLegendCarriesTheCalibrationItsLabelsWerePlacedWith()
    {
        const string areas = """ "Areas": [ { "Name": "Somewhere", "Colour": "#F0A3FF", "X": 1, "Y": 2 } ] """;

        var legend = AreaLegend.Parse("{ \"Calibration\": { \"OffsetX\": 1, \"OffsetY\": 2, \"ScaleX\": 0.5, \"ScaleY\": -0.5, \"MapSize\": 100, \"PinOffsetX\": 3, \"PinOffsetY\": 4 }, " + areas + "}")!;
        var unusable = AreaLegend.Parse("{ \"Calibration\": { \"OffsetX\": 1, \"MapSize\": 0 }, " + areas + "}")!;

        Assert.Equal(new MapCalibration(1, 2, 0.5, -0.5, 100, 3, 4), legend.Calibration);
        Assert.Null(unusable.Calibration); // a calibration that can't place anything is as good as none
    }

    [Fact]
    public void ALegendWithNothingUsableIsNull()
    {
        Assert.Null(AreaLegend.Parse("not json"));
        Assert.Null(AreaLegend.Parse("null"));
        Assert.Null(AreaLegend.Parse("""{ "Areas": [] }"""));
        Assert.Null(AreaLegend.Parse("""{ "Source": "s" }"""));
    }

    // ---- The readout: no flicker at a border -------------------------------------

    [Fact]
    public void TheFirstReadingIsTakenAsItIsEvenOnABorder()
    {
        var map = Bands((0, Red), (50, Green));
        var readout = new AreaReadout();

        Assert.Equal("Beta", map.NameOf(readout.Update(map, At(50.5), 0.5)));
    }

    [Fact]
    public void WalkingAlongABorderDoesNotFlipTheArea()
    {
        var map = Bands((0, Red), (50, Green));
        var readout = new AreaReadout();
        readout.Update(map, At(30), 0.5);

        foreach (var x in new[] { 49.5, 50.5, 49.2, 51.5, 50.1, 51.9, 48.7 })
        {
            Assert.Equal("Alpha", map.NameOf(readout.Update(map, At(x), 0.5)));
        }
    }

    [Fact]
    public void ClearlyInsideTheNextAreaItSwitches()
    {
        var map = Bands((0, Red), (50, Green));
        var readout = new AreaReadout();
        readout.Update(map, At(30), 0.5);

        Assert.Equal("Alpha", map.NameOf(readout.Update(map, At(51.5), 0.5))); // 1 px in: the border is still within 25 m
        Assert.Equal("Beta", map.NameOf(readout.Update(map, At(52.5), 0.5)));  // 2 px in: clear of it
        Assert.Equal("Beta", map.NameOf(readout.Update(map, At(50.5), 0.5)));  // back at the border: stays, same rule the other way
        Assert.Equal("Alpha", map.NameOf(readout.Update(map, At(47.5), 0.5)));
    }

    [Fact]
    public void OpenSeaIsAReadingLikeAnyOther()
    {
        var map = Bands((0, Red), (50, null));
        var readout = new AreaReadout();
        readout.Update(map, At(30), 0.5);

        Assert.Equal("Alpha", map.NameOf(readout.Update(map, At(50.5), 0.5)));          // just off the edge
        Assert.Equal(AreaMap.None, readout.Update(map, At(60), 0.5));                   // well out
        Assert.Equal(AreaMap.None, readout.Update(map, At(49.5), 0.5));                 // just back in: not yet
        Assert.Equal("Alpha", map.NameOf(readout.Update(map, At(40), 0.5)));
    }

    [Fact]
    public void AfterAJumpThereIsNoBorderToWaitFor()
    {
        var map = Bands((0, Red), (34, Green), (67, Blue));
        var readout = new AreaReadout();
        readout.Update(map, At(10), 0.5);

        // Respawned on the Beta / Gamma border: Alpha is nowhere near, so its name must not linger.
        Assert.Equal("Beta", map.NameOf(readout.Update(map, At(66.5), 0.5)));
    }

    [Fact]
    public void ResetStartsAfresh()
    {
        var map = Bands((0, Red), (50, Green));
        var readout = new AreaReadout();
        readout.Update(map, At(30), 0.5);
        Assert.Equal("Alpha", map.NameOf(readout.Update(map, At(50.5), 0.5)));

        readout.Reset();

        Assert.Equal("Beta", map.NameOf(readout.Update(map, At(50.5), 0.5)));
    }

    // ---- The journal: when the feed says "Entered …" ------------------------------------

    private static readonly DateTime T0 = new(2026, 10, 2, 18, 0, 0, DateTimeKind.Utc);

    private static DateTime After(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void WhereALifeBeginsIsNotAnEntry()
    {
        var map = Bands((0, Red), (50, Green));
        var journal = new AreaJournal();

        Assert.Null(journal.Update(map, At(30), 0.5, T0));
        Assert.Equal("Alpha", journal.Current);
        Assert.Null(journal.Update(map, At(31), 0.5, After(3)));
    }

    [Fact]
    public void AFirstCrossingIsAnnouncedAtOnce()
    {
        var map = Bands((0, Red), (50, Green));
        var journal = new AreaJournal();
        journal.Update(map, At(30), 0.5, T0);

        Assert.Null(journal.Update(map, At(50.5), 0.5, After(3)));          // on the border: not clearly in Beta yet
        Assert.Equal("Beta", journal.Update(map, At(60), 0.5, After(6)));
        Assert.Equal("Beta", journal.Current);
        Assert.Null(journal.Update(map, At(61), 0.5, After(9)));            // said once
    }

    [Fact]
    public void AStepOverTheBorderAndStraightBackSaysNothingMore()
    {
        var map = Bands((0, Red), (50, Green));
        var journal = new AreaJournal();
        journal.Update(map, At(30), 0.5, T0);
        Assert.Equal("Beta", journal.Update(map, At(60), 0.5, After(60)));

        Assert.Null(journal.Update(map, At(40), 0.5, After(70)));  // back in Alpha, inside the quiet half minute
        Assert.Equal("Alpha", journal.Current);                    // the pill follows at once; only the feed waits
        Assert.Null(journal.Update(map, At(60), 0.5, After(80)));  // and in Beta again: the feed already says Beta
    }

    [Fact]
    public void StayingOnTheOtherSideIsCaughtUpWhenTheQuietIsOver()
    {
        var map = Bands((0, Red), (50, Green));
        var journal = new AreaJournal();
        journal.Update(map, At(30), 0.5, T0);
        Assert.Equal("Beta", journal.Update(map, At(60), 0.5, After(60)));
        Assert.Null(journal.Update(map, At(40), 0.5, After(70)));

        Assert.Null(journal.Update(map, At(40), 0.5, After(89)));
        Assert.Equal("Alpha", journal.Update(map, At(40), 0.5, After(90))); // the feed must not end on an area you left
    }

    [Fact]
    public void OpenSeaIsNeverAnnouncedAndComingBackToTheSameAreaIsNoEntry()
    {
        var map = Bands((0, Red), (50, null), (70, Green));
        var journal = new AreaJournal();
        journal.Update(map, At(30), 0.5, T0);

        Assert.Null(journal.Update(map, At(60), 0.5, After(60)));
        Assert.Null(journal.Current);
        Assert.Null(journal.Update(map, At(30), 0.5, After(120)));           // back where the feed last had you
        Assert.Equal("Beta", journal.Update(map, At(80), 0.5, After(180)));  // across the water: a new area
    }

    [Fact]
    public void ALifeThatBeginsAtSeaAnnouncesItsFirstArea()
    {
        var map = Bands((0, null), (50, Green));
        var journal = new AreaJournal();

        Assert.Null(journal.Update(map, At(20), 0.5, T0));
        Assert.Equal("Beta", journal.Update(map, At(70), 0.5, After(3)));
    }

    [Fact]
    public void AfterAResetTheNextLifeStartsAfresh()
    {
        var map = Bands((0, Red), (50, Green));
        var journal = new AreaJournal();
        journal.Update(map, At(30), 0.5, T0);
        Assert.Equal("Beta", journal.Update(map, At(60), 0.5, After(3)));

        journal.Reset();

        Assert.Null(journal.Current);
        Assert.Null(journal.Update(map, At(30), 0.5, After(6))); // a respawn in Alpha is a beginning, not an entry
        Assert.Equal("Alpha", journal.Current);
    }

    [Fact]
    public void TheFeedLineNamesTheArea()
    {
        var line = SelfActivity.AreaLine("Somewhere", T0);

        Assert.Equal("Entered Somewhere", line.Text);
        Assert.Equal(FeedKind.Area, line.Kind);
        Assert.True(line.Mine);
    }

    // ---- Borders, for the minimap's layer ---------------------------------------------

    [Fact]
    public void ABorderRunsWhereTwoAreasMeet()
    {
        var map = Bands((0, Red), (50, Green));

        var mask = map.BorderMask(1);

        for (var y = 0; y < 100; y++)
        {
            Assert.True(mask[y * 100 + 49]);  // the last Alpha column: its right neighbour is Beta
            Assert.False(mask[y * 100 + 48]);
            Assert.False(mask[y * 100 + 50]);
        }
        Assert.Equal(100, mask.Count(b => b));
    }

    [Fact]
    public void AnAreasEdgeToNothingIsNotABorder()
    {
        var sea = Bands((0, Red), (50, null));
        var strait = Bands((0, Red), (40, null), (60, Green)); // open water between two areas: neither side gets a line

        Assert.DoesNotContain(true, sea.BorderMask(1));
        Assert.DoesNotContain(true, strait.BorderMask(4));
    }

    [Fact]
    public void ThicknessWidensTheLineInWholePixels()
    {
        var map = Bands((0, Red), (50, Green));

        Assert.Equal(new[] { 49, 50 }, Row(map.BorderMask(2), 20));
        Assert.Equal(new[] { 48, 49, 50 }, Row(map.BorderMask(3), 20));
        Assert.Equal(new[] { 48, 49, 50, 51 }, Row(map.BorderMask(4), 20));
        Assert.Equal(new[] { 48, 49, 50, 51 }, Row(map.BorderMask(9), 20)); // capped

        static int[] Row(bool[] mask, int y) => Enumerable.Range(0, 100).Where(x => mask[y * 100 + x]).ToArray();
    }

    [Fact]
    public void WithALandMaskABorderNeedsLandOnBothSides()
    {
        var map = Bands((0, Red), (50, Green));
        // The top half is land; below row 50 the two areas meet in their coastal water.
        var topHalf = Enumerable.Range(0, 100 * 100).Select(i => i / 100 < 50).ToArray();

        var mask = map.BorderMask(1, topHalf);

        Assert.True(mask[10 * 100 + 49]);
        Assert.True(mask[49 * 100 + 49]);
        Assert.False(mask[50 * 100 + 49]);   // the line ends at the coast
        Assert.Equal(50, mask.Count(b => b)); // and the two areas' own coast (row 49) is not traced: they have each other
        Assert.Equal(100, map.BorderMask(1, new bool[7]).Count(b => b)); // a mask of the wrong size is ignored
    }

    private static int[] Columns(bool[] mask, int y) => Enumerable.Range(0, 100).Where(x => mask[y * 100 + x]).ToArray();

    [Fact]
    public void AnAreaThatIsAnIslandOfItsOwnGetsItsShorelineTraced()
    {
        // Alpha and Beta share a land border; Gamma lies across the water, with a strip of coastal water of its own.
        var map = Bands((0, Red), (30, Green), (60, null), (70, Blue));
        var land = Enumerable.Range(0, 100 * 100).Select(i => i % 100 is < 55 or >= 80).ToArray(); // sea from column 55 to 79

        var mask = map.BorderMask(1, land);

        // The Alpha | Beta border, and Gamma's shore: its first land column, with water to its left.
        Assert.Equal(new[] { 29, 80 }, Columns(mask, 40));
        // Beta's own shore (column 54) is NOT traced: it has a neighbour on land, so it is no island.
        Assert.False(mask[40 * 100 + 54]);
        Assert.Equal(200, mask.Count(b => b));
    }

    [Fact]
    public void WhatMakesAnIslandIsItsNeighboursNotItsSize()
    {
        // The big area stands alone; the two small ones share a border.
        var map = Bands((0, Red), (80, null), (90, Green), (95, Blue));
        var land = Enumerable.Range(0, 100 * 100).Select(i => i % 100 is < 70 or >= 90).ToArray();

        Assert.Equal(new[] { 69, 94 }, Columns(map.BorderMask(1, land), 10)); // the big one's shore, and the small ones' border
    }

    [Fact]
    public void WithoutALandMaskNoShoreIsTraced()
    {
        var map = Bands((0, Red), (40, null), (60, Green));

        Assert.DoesNotContain(true, map.BorderMask(1)); // nothing says where the coast is
    }

    // ---- The map that ships -------------------------------------------------------------
    // These hold for ANY map the generator produces: none of them names an area or counts
    // on this island's shape. A new map means new input files and a generator run, no edits here.

    private static AreaMap Bundled => AreaMapAsset.LoadBundled()!;

    [Fact]
    public void TheBundledMapLoadsCleanly()
    {
        var map = Bundled;

        Assert.True(map.Size > 0);
        Assert.InRange(map.Areas.Count, 1, AreaMap.MaxAreas);
        Assert.Equal(map.Areas.Count, map.Areas.Select(a => a.Colour.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(map.Areas.Count, map.Areas.Select(a => a.Name).Distinct().Count());
        Assert.Equal(0, map.UnknownPixels);         // every painted pixel is exactly a legend colour
        Assert.NotNull(map.Legend.Calibration);     // the generator records what it placed the labels with
    }

    [Fact]
    public void EveryAreaIsOnTheMapAtItsOwnLabelPoint()
    {
        var map = Bundled;
        var calibration = map.Legend.Calibration!;

        foreach (var area in map.Areas)
        {
            var (fx, fy) = calibration.ToFraction(area.X, area.Y);
            Assert.Equal(area.Name, map.NameAt(fx, fy));
        }
    }

    [Fact]
    public void TheBundledLandMaskFitsTheMap()
    {
        var map = Bundled;
        var land = AreaMapAsset.LoadBundledLand()!;

        Assert.Equal(map.Size * map.Size, land.Length);
        Assert.Contains(true, land);
        Assert.Contains(false, land);
        // Every piece of land belongs to an area: nothing on the map is nameless.
        for (var i = 0; i < land.Length; i++)
        {
            if (land[i]) Assert.NotEqual(AreaMap.None, map.IndexAtPixel(i % map.Size, i / map.Size));
        }
    }

    [Fact]
    public void TheBundledBordersAreThinLinesOnLand()
    {
        var map = Bundled;
        var land = AreaMapAsset.LoadBundledLand()!;

        var lines = map.BorderMask(1, land);

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i]) Assert.True(land[i]); // no line runs out into the sea
        }
        Assert.True(lines.Count(l => l) * 10 < land.Count(l => l)); // lines, not fills
    }

    [Fact]
    public void OnTheBundledMapAnAreasShoreIsTracedExactlyWhenItHasNoNeighbourOnLand()
    {
        var map = Bundled;
        var land = AreaMapAsset.LoadBundledLand()!;
        var n = map.Size;
        var lines = map.BorderMask(1, land);

        int AreaOf(int i) => map.IndexAtPixel(i % n, i / n);
        IEnumerable<int> Around(int i)
        {
            if (i % n > 0) yield return i - 1;
            if (i % n < n - 1) yield return i + 1;
            if (i >= n) yield return i - n;
            if (i < n * (n - 1)) yield return i + n;
        }
        bool OtherLand(int j, int area) => land[j] && AreaOf(j) != AreaMap.None && AreaOf(j) != area;
        bool IsShore(int i) => Around(i).Any(j => !land[j]);
        bool IsBorder(int i) => (i % n < n - 1 && OtherLand(i + 1, AreaOf(i))) || (i < n * (n - 1) && OtherLand(i + n, AreaOf(i)));

        // Which areas have a neighbour on land — worked out here from the two files, not known in advance.
        var neighboured = new bool[map.Areas.Count];
        for (var i = 0; i < land.Length; i++)
        {
            if (land[i] && Around(i).Any(j => OtherLand(j, AreaOf(i)))) neighboured[AreaOf(i)] = true;
        }

        for (var i = 0; i < land.Length; i++)
        {
            if (!land[i]) continue;
            if (neighboured[AreaOf(i)])
            {
                if (lines[i]) Assert.True(IsBorder(i)); // a line here is a border with the neighbour, never the coast as such
            }
            else
            {
                Assert.Equal(IsShore(i), lines[i]);     // an island of its own: its shore, all of it, and nothing else
            }
        }
    }

    [Fact]
    public void TheCalibrationTransformMatchesTheSitesFormula()
    {
        var calibration = new MapCalibration(100, 200, 0.5, -0.25, 1000, 10, -20);

        var (fx, fy) = calibration.ToFraction(300, 400);

        Assert.Equal((100 + 300 * 0.5 + 10) / 1000, fx, 12);
        Assert.Equal(1 - (200 + 400 * -0.25 - 20) / 1000, fy, 12);
        Assert.Equal((0.0, 0.0), new MapCalibration(0, 0, 1, 1, 100).ToFraction(-500, 500)); // clamped onto the map: x under 0, y flipped past the bottom
    }
}
