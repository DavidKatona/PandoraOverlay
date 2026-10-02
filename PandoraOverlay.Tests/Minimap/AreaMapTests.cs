using Xunit;

namespace PandoraOverlay.Tests;

public sealed class AreaMapTests
{
    private const string Red = "#FF0000", Green = "#00FF00", Blue = "#0000FF";

    private static readonly AreaLegend ThreeAreas = new("test", "2026-10-02", new[]
    {
        new AreaEntry("Alpha", Red, 0, 0), new AreaEntry("Beta", Green, 0, 0), new AreaEntry("Gamma", Blue, 0, 0)
    });

    /// <summary>The site's calibration (Sep 2026) — the constants tools/area-map generated the bundled map with.</summary>
    private static readonly MapCalibration Calibration = new(
        1160.9249840132136, 1223.2852629424794, 0.0020010632626191725, -0.0020003567492836005, 2500, -15, 25);

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
              { "Name": " Delta ", "Colour": "#F0A3FF", "X": 177000, "Y": 33000 },
              { "Name": "", "Colour": "#000000" },
              { "Name": "No colour" },
              { "Name": "Bad colour", "Colour": "pink" } ] }
            """)!;

        var only = Assert.Single(legend.Areas);
        Assert.Equal("Delta", only.Name);
        Assert.Equal(177000, only.X);
        Assert.Equal("2026-09-30", legend.CopiedOn);
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

    // ---- The map that ships ---------------------------------------------------------

    [Fact]
    public void TheBundledMapLoadsCleanly()
    {
        var map = AreaMapAsset.LoadBundled()!;

        Assert.Equal(1000, map.Size);
        Assert.Equal(26, map.Areas.Count);
        Assert.Equal(26, map.Areas.Select(a => a.Colour.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(26, map.Areas.Select(a => a.Name).Distinct().Count());
        Assert.Equal(0, map.UnknownPixels); // every painted pixel is exactly a legend colour
    }

    [Fact]
    public void EveryAreasOwnLabelPointIsInThatArea()
    {
        var map = AreaMapAsset.LoadBundled()!;

        foreach (var area in map.Areas)
        {
            var (fx, fy) = Calibration.ToFraction(area.X, area.Y);
            Assert.Equal(area.Name, map.NameAt(fx, fy));
        }
    }

    [Fact]
    public void KnownSpotsOfTheBundledMap()
    {
        var map = AreaMapAsset.LoadBundled()!;

        Assert.Null(map.NameAt(0.02, 0.02));  // open sea in the corner
        Assert.Null(map.NameAt(0.98, 0.98));
        var (px, py) = Calibration.ToFraction(-394000, 329000); // the pale quarry in the south-west

        Assert.Equal("The Pit", map.NameAt(px, py));
    }

    [Fact]
    public void TheCalibrationTransformMatchesTheSitesFormula()
    {
        var (fx, fy) = Calibration.ToFraction(177000, 33000); // the Delta label

        Assert.Equal((1160.9249840132136 + 177000 * 0.0020010632626191725 - 15) / 2500, fx, 12);
        Assert.Equal(1 - (1223.2852629424794 + 33000 * -0.0020003567492836005 + 25) / 2500, fy, 12);
        Assert.Equal((0.0, 0.0), new MapCalibration(0, 0, 1, 1, 100).ToFraction(-500, 500)); // clamped onto the map: x under 0, y flipped past the bottom
    }
}
