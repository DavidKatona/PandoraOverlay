using Xunit;

namespace PandoraOverlay.Tests;

public class BreadcrumbTrailTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(30);

    /// <summary>x/y in metres for readability; the tracker works in cm like the API.</summary>
    private static PlayerState At(double xMeters, double yMeters, double growth = 0.5, string dino = "Pteranodon") =>
        new("765611", "Tester", dino, "Male", growth, 1, 1, 1, 1, 0, false, false, false, xMeters * 100, yMeters * 100, 0);

    /// <summary>Walks east at 10 m/s from the given x, one poll every 3 s.</summary>
    private static void Walk(BreadcrumbTrail trail, int polls, DateTime start, double fromX = 0)
    {
        for (var i = 0; i < polls; i++)
        {
            trail.Add(At(fromX + i * 30, 0), Keep, start + TimeSpan.FromSeconds(3 * i));
        }
    }

    [Fact]
    public void MovingAddsAPointPerPoll()
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        Assert.Equal(10, trail.Points.Count);
        Assert.Equal(0, trail.Points[0].X);
        Assert.Equal(27_000, trail.Points[^1].X); // 270 m, in cm
    }

    [Fact]
    public void StandingStillAddsNothing()
    {
        var trail = new BreadcrumbTrail();
        for (var i = 0; i < 20; i++)
        {
            trail.Add(At(100 + i % 2, 100), Keep, T0 + TimeSpan.FromSeconds(3 * i)); // shuffling within 1 m
        }
        Assert.Single(trail.Points);
    }

    [Fact]
    public void OldPointsExpire()
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        // One more step 31 minutes later, a plausible 100 m on from the last point.
        trail.Add(At(370, 0), Keep, T0 + TimeSpan.FromMinutes(31));
        Assert.Single(trail.Points);
        Assert.Equal(37_000, trail.Points[0].X);
    }

    [Fact]
    public void ImpossibleJumpClearsTheTrail() // respawn / teleport: no straight line across the map
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        trail.Add(At(5_000, 3_000), Keep, T0 + TimeSpan.FromSeconds(30)); // kilometres away, 3 s later
        Assert.Single(trail.Points);
        Assert.Equal(500_000, trail.Points[0].X);
    }

    [Fact]
    public void FarJumpAcrossAPollGapClearsTooEvenIfTheSpeedWouldPass()
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        // 10 minutes of silence, then 2 km away: only ~3 m/s on paper, but the path is unknown.
        trail.Add(At(2_270, 0), Keep, T0 + TimeSpan.FromMinutes(10));
        Assert.Single(trail.Points);
    }

    [Fact]
    public void RelogOnTheSameSpotKeepsTheTrail()
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        trail.Add(At(275, 0), Keep, T0 + TimeSpan.FromMinutes(10)); // back after a restart, 5 m from the last point
        Assert.Equal(11, trail.Points.Count);
    }

    [Fact]
    public void DinoSwapClearsTheTrail()
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        trail.Add(At(300, 0, dino: "Deinosuchus"), Keep, T0 + TimeSpan.FromSeconds(30));
        Assert.Single(trail.Points);
    }

    [Fact]
    public void GrowthDecreaseClearsTheTrail() // death → fresh spawn of the same species
    {
        var trail = new BreadcrumbTrail();
        Walk(trail, 10, T0);
        trail.Add(At(300, 0, growth: 0.1), Keep, T0 + TimeSpan.FromSeconds(30));
        Assert.Single(trail.Points);
    }
}
