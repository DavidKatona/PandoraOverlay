using System.IO;
using Xunit;

namespace PandoraOverlay.Tests;

public sealed class ZoneSetTests
{
    /// <summary>A zones file around the given zone lines.</summary>
    private static string File(params string[] zones) =>
        "{ \"Source\": \"S\", \"Dataset\": \"D\", \"GameVersions\": \"1-2\", \"CopiedOn\": \"2026-10-12\", \"Zones\": [" + string.Join(",", zones) + "] }";

    private const string Square = """{ "Kind": "Patrol", "Polygon": [[0,0],[100,0],[100,100],[0,100]] }""";

    // ---- Reading the file ------------------------------------------------------------

    [Fact]
    public void TheFileGivesKindsShapesAndTheMassFlag()
    {
        var set = ZoneSet.Parse(File(
            Square,
            """{ "Kind": "Migration", "Mass": true, "Circle": { "X": 500, "Y": -500, "R": 50 } }""",
            """{ "Kind": "sanctuary", "Polygon": [[0,0],[10,0],[0,10]] }"""))!;

        Assert.Equal(new[] { ZoneKind.Patrol, ZoneKind.Migration, ZoneKind.Sanctuary }, set.Zones.Select(z => z.Kind));
        Assert.Equal(4, set.Zones[0].Polygon!.Count);
        Assert.Equal(new ZoneCircle(500, -500, 50), set.Zones[1].Circle);
        Assert.True(set.Zones[1].Mass);
        Assert.False(set.Zones[2].Mass);
        Assert.Equal(("S", "D", "1-2", "2026-10-12"), (set.Source, set.Dataset, set.GameVersions, set.CopiedOn));
    }

    [Fact]
    public void UnusableZonesAreSkipped()
    {
        var set = ZoneSet.Parse(File(
            """{ "Kind": "Volcano", "Circle": { "X": 0, "Y": 0, "R": 5 } }""",                        // an unknown kind
            """{ "Kind": "Patrol", "Polygon": [[0,0],[1,1]] }""",                                       // too few points
            """{ "Kind": "Patrol", "Polygon": [[0,0],[1],[1,1]] }""",                                   // a point without a y
            """{ "Kind": "Patrol", "Circle": { "X": 0, "Y": 0, "R": 0 } }""",                          // no radius
            """{ "Kind": "Patrol", "Circle": { "X": 0, "Y": 0, "R": 5 }, "Polygon": [[0,0],[1,0],[0,1]] }""", // two shapes
            """{ "Kind": "Patrol" }""",                                                                 // no shape
            """{ "Kind": "Patrol", "Mass": true, "Circle": { "X": 0, "Y": 0, "R": 5 } }"""))!;            // usable; only a migration is mass

        var only = Assert.Single(set.Zones);
        Assert.Equal(ZoneKind.Patrol, only.Kind);
        Assert.False(only.Mass);
    }

    [Fact]
    public void AFileWithNothingUsableIsNull()
    {
        Assert.Null(ZoneSet.Parse("not json"));
        Assert.Null(ZoneSet.Parse("null"));
        Assert.Null(ZoneSet.Parse("""{ "Source": "S" }"""));
        Assert.Null(ZoneSet.Parse(File()));
        Assert.Null(ZoneSet.Parse(File("""{ "Kind": "Volcano", "Circle": { "X": 0, "Y": 0, "R": 5 } }""")));
    }

    // ---- What is where ---------------------------------------------------------------

    [Fact]
    public void APointIsInsideAConcavePolygonButNotInItsNotch()
    {
        // A "U": two arms joined at the bottom, the notch between them open at the top.
        var u = ZoneSet.Parse(File("""{ "Kind": "Migration", "Polygon": [[0,0],[30,0],[30,70],[70,70],[70,0],[100,0],[100,100],[0,100]] }"""))!.Zones[0];

        Assert.True(u.Contains(15, 50));   // the left arm
        Assert.True(u.Contains(85, 50));   // the right arm
        Assert.True(u.Contains(50, 85));   // the bottom
        Assert.False(u.Contains(50, 30));  // the notch
        Assert.False(u.Contains(150, 50)); // outside altogether
    }

    [Fact]
    public void ACircleHoldsItsInsideAndItsEdgeOnly()
    {
        var circle = new Zone(ZoneKind.Patrol, false, null, new ZoneCircle(1000, -1000, 100));

        Assert.True(circle.Contains(1000, -1000));
        Assert.True(circle.Contains(1100, -1000)); // on the edge
        Assert.False(circle.Contains(1071, -929)); // inside the box, outside the circle
        Assert.False(circle.Contains(1101, -1000));
    }

    [Fact]
    public void EveryZoneHoldingAPointIsFound()
    {
        var set = ZoneSet.Parse(File(
            Square,
            """{ "Kind": "Migration", "Mass": true, "Circle": { "X": 100, "Y": 100, "R": 50 } }""",
            """{ "Kind": "Sanctuary", "Circle": { "X": 1000, "Y": 1000, "R": 5 } }"""))!;

        Assert.Equal(new[] { ZoneKind.Patrol, ZoneKind.Migration }, set.At(90, 90).Select(z => z.Kind));
        Assert.Equal(new[] { ZoneKind.Patrol }, set.At(10, 10).Select(z => z.Kind));
        Assert.Empty(set.At(500, 500));
    }

    // ---- The readout -----------------------------------------------------------------

    [Fact]
    public void TheReadoutNamesEachShownKindOnceInItsOrder()
    {
        var zones = new[]
        {
            new Zone(ZoneKind.Sanctuary, false, null, new ZoneCircle(0, 0, 1)),
            new Zone(ZoneKind.Migration, true, null, new ZoneCircle(0, 0, 1)),
            new Zone(ZoneKind.Patrol, false, null, new ZoneCircle(0, 0, 1)),
            new Zone(ZoneKind.Migration, false, null, new ZoneCircle(0, 0, 1)),
            new Zone(ZoneKind.Patrol, false, null, new ZoneCircle(0, 0, 1)), // a second patrol zone overlapping the first
        };
        var all = ZoneKinds.Migrations | ZoneKinds.Patrols | ZoneKinds.Sanctuaries;

        Assert.Equal(new[] { "Patrol zone", "Migration zone", "Mass migration zone", "Sanctuary" }, ZoneSet.ForReadout(zones, all).Select(z => z.Label));
        Assert.Equal(new[] { "Sanctuary" }, ZoneSet.ForReadout(zones, ZoneKinds.Sanctuaries).Select(z => z.Label));
        Assert.Empty(ZoneSet.ForReadout(zones, ZoneKinds.None));
    }

    [Fact]
    public void ASignedAreaIsPositiveClockwiseOnScreen()
    {
        var clockwise = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }; // right, down, left, up with y pointing down

        Assert.Equal(1, ZoneSet.SignedArea(clockwise), 12);
        Assert.Equal(-1, ZoneSet.SignedArea(clockwise.Reverse().ToArray()), 12);
    }

    // ---- The file that ships ---------------------------------------------------------
    // These hold for ANY zones file the converter writes: none of them counts this
    // island's zones or names one. New data means a converter run, no edits here.

    private static ZoneSet Bundled
    {
        get
        {
            using var stream = typeof(ZoneSet).Assembly.GetManifestResourceStream("zones.json")!;
            using var reader = new StreamReader(stream);
            return ZoneSet.Parse(reader.ReadToEnd())!;
        }
    }

    [Fact]
    public void TheBundledZonesLoadWithWhereTheyCameFrom()
    {
        var set = Bundled;

        Assert.NotEmpty(set.Zones);
        Assert.False(string.IsNullOrWhiteSpace(set.Source));
        Assert.False(string.IsNullOrWhiteSpace(set.Dataset));
        Assert.False(string.IsNullOrWhiteSpace(set.GameVersions));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", set.CopiedOn);
    }

    [Fact]
    public void EveryBundledZoneLiesOnTheMapPicture()
    {
        // ToFraction clamps onto the picture, so a point past its edge would come out at exactly 0 or 1.
        var calibration = PollService.BundledCalibration()!;
        var points = Bundled.Zones.SelectMany(z => z.Circle is { } c
            ? new[] { (c.X - c.R, c.Y - c.R), (c.X + c.R, c.Y + c.R) }
            : z.Polygon!.ToArray());

        Assert.All(points, p =>
        {
            var (fx, fy) = calibration.ToFraction(p.Item1, p.Item2);
            Assert.True(fx is > 0 and < 1 && fy is > 0 and < 1, $"({p.Item1}, {p.Item2}) is off the picture");
        });
    }

    [Fact]
    public void EveryBundledCircleHoldsItsOwnCentre()
    {
        var set = Bundled;

        Assert.All(set.Zones.Where(z => z.Circle is not null), z => Assert.Contains(z, set.At(z.Circle!.X, z.Circle.Y)));
    }
}
