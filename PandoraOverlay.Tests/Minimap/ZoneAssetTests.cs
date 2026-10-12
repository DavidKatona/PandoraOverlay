using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

public sealed class ZoneAssetTests
{
    // World cm -> fraction (50 + x) / 100 across and (50 + y) / 100 down: world (0, 0) is the picture's middle.
    private static readonly MapCalibration Calibration = new(50, 50, 1, -1, 100);

    /// <summary>A map fraction in zone units, where the shapes are drawn.</summary>
    private static Point At(double fx, double fy) => new(fx * ZoneAsset.Units, fy * ZoneAsset.Units);

    [Fact]
    public void OverlappingZonesOfOneKindFillAsOneWhicheverWayRoundTheyRun()
    {
        var set = ZoneSet.Parse("""
            { "Zones": [
              { "Kind": "Patrol", "Polygon": [[0,0],[20,0],[20,20],[0,20]] },
              { "Kind": "Patrol", "Polygon": [[10,10],[10,30],[30,30],[30,10]] } ] }
            """)!; // the second runs the other way round: a non-zero fill of mixed windings would leave a hole

        var patrols = ZoneAsset.Draw(set, Calibration).Patrols!;

        Assert.True(patrols.FillContains(At(0.65, 0.65))); // where the two overlap
        Assert.True(patrols.FillContains(At(0.55, 0.55)));
        Assert.True(patrols.FillContains(At(0.75, 0.75)));
        Assert.False(patrols.FillContains(At(0.75, 0.55))); // beside both
    }

    [Fact]
    public void ACircleIsDrawnRoundItsCentreAtItsRadius()
    {
        var set = ZoneSet.Parse("""{ "Zones": [ { "Kind": "Sanctuary", "Circle": { "X": -30, "Y": -30, "R": 5 } } ] }""")!;

        var shapes = ZoneAsset.Draw(set, Calibration);

        Assert.True(shapes.Sanctuaries!.FillContains(At(0.20, 0.20)));
        Assert.True(shapes.Sanctuaries.FillContains(At(0.24, 0.20)));
        Assert.False(shapes.Sanctuaries.FillContains(At(0.26, 0.20)));
        Assert.True(shapes.Sanctuaries.FillContains(At(0.20, 0.24)));
        Assert.False(shapes.Sanctuaries.FillContains(At(0.20, 0.26)));
        Assert.Null(shapes.Patrols);   // a kind the file lacks has no shape
        Assert.Null(shapes.Migrations);
    }

    [Fact]
    public void MassMigrationsGetTheirOwnShape()
    {
        var set = ZoneSet.Parse("""
            { "Zones": [
              { "Kind": "Migration", "Circle": { "X": -30, "Y": -30, "R": 5 } },
              { "Kind": "Migration", "Mass": true, "Circle": { "X": 30, "Y": 30, "R": 5 } } ] }
            """)!;

        var shapes = ZoneAsset.Draw(set, Calibration);

        Assert.True(shapes.Migrations!.FillContains(At(0.2, 0.2)));
        Assert.False(shapes.Migrations.FillContains(At(0.8, 0.8)));
        Assert.True(shapes.MassMigrations!.FillContains(At(0.8, 0.8)));
    }

    [Fact]
    public void TheBundledZonesDrawOnThePicture()
    {
        var shapes = ZoneAsset.ShapesFor(PollService.BundledCalibration()!)!;

        var drawn = new[] { shapes.Migrations, shapes.MassMigrations, shapes.Patrols, shapes.Sanctuaries }.OfType<Geometry>().ToList();
        Assert.NotEmpty(drawn);
        Assert.All(drawn, g => Assert.True(
            g.Bounds.Left >= 0 && g.Bounds.Top >= 0 && g.Bounds.Right <= ZoneAsset.Units && g.Bounds.Bottom <= ZoneAsset.Units, $"{g.Bounds} leaves the picture"));
        Assert.Same(shapes, ZoneAsset.ShapesFor(PollService.BundledCalibration()!)); // drawn once per calibration
    }
}
