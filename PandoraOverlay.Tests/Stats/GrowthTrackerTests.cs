using Xunit;

namespace PandoraOverlay.Tests;

public class GrowthTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double growth, string dino = "Pteranodon") =>
        new("765611", "Tester", dino, "Male", growth, 1, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    /// <summary>Feeds linearly interpolated samples from → to over the given span.</summary>
    private static void Feed(GrowthTracker tracker, double from, double to, TimeSpan over, int steps = 60)
    {
        for (var i = 0; i <= steps; i++)
        {
            var f = (double)i / steps;
            tracker.Add(Player(from + (to - from) * f), T0 + over * f);
        }
    }

    [Fact]
    public void NoEstimateBeforeBaseline()
    {
        var tracker = new GrowthTracker();
        Feed(tracker, 0.40, 0.401, TimeSpan.FromMinutes(4));
        Assert.Equal(GrowthTracker.GrowthStatus.Estimating, tracker.Status);
        Assert.Null(tracker.Eta);
    }

    [Fact]
    public void EstimatesEtaFromSlope()
    {
        var tracker = new GrowthTracker();
        // 1% over 10 minutes → 6%/h → (1 − 0.41) / 0.06 ≈ 9.83 h remaining.
        Feed(tracker, 0.40, 0.41, TimeSpan.FromMinutes(10));
        Assert.Equal(GrowthTracker.GrowthStatus.Growing, tracker.Status);
        Assert.NotNull(tracker.Eta);
        Assert.InRange(tracker.Eta!.Value.TotalHours, 9.5, 10.2);
    }

    [Fact]
    public void FlatGrowthAfterBaselineIsPaused()
    {
        var tracker = new GrowthTracker();
        Feed(tracker, 0.40, 0.40, TimeSpan.FromMinutes(6));
        Assert.Equal(GrowthTracker.GrowthStatus.Paused, tracker.Status);
        Assert.Null(tracker.Eta);
    }

    [Fact]
    public void DinoSwapResetsBaseline()
    {
        var tracker = new GrowthTracker();
        Feed(tracker, 0.40, 0.41, TimeSpan.FromMinutes(10));
        tracker.Add(Player(0.10, dino: "Deinosuchus"), T0 + TimeSpan.FromMinutes(10.1));
        Assert.Equal(GrowthTracker.GrowthStatus.Estimating, tracker.Status);
    }

    [Fact]
    public void GrowthDecreaseResetsBaseline() // death → fresh spawn of the same species
    {
        var tracker = new GrowthTracker();
        Feed(tracker, 0.40, 0.41, TimeSpan.FromMinutes(10));
        tracker.Add(Player(0.25), T0 + TimeSpan.FromMinutes(10.1));
        Assert.Equal(GrowthTracker.GrowthStatus.Estimating, tracker.Status);
    }

    [Fact]
    public void FullGrowthRetiresTheEstimate()
    {
        var tracker = new GrowthTracker();
        tracker.Add(Player(1.0), T0);
        Assert.Equal(GrowthTracker.GrowthStatus.Full, tracker.Status);
        Assert.Null(tracker.Eta);
    }
}
