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
        Assert.Null(tracker.NextStage);
    }

    [Fact]
    public void EstimatesTheTimeToTheNextStageFromSlope()
    {
        var tracker = new GrowthTracker();
        // 1% over 10 minutes → 6%/h → (0.50 − 0.41) / 0.06 = 1.5 h to the subadult line.
        Feed(tracker, 0.40, 0.41, TimeSpan.FromMinutes(10));
        Assert.Equal(GrowthTracker.GrowthStatus.Growing, tracker.Status);
        Assert.Equal(50, tracker.NextStage);
        Assert.NotNull(tracker.Eta);
        Assert.InRange(tracker.Eta!.Value.TotalHours, 1.45, 1.55);
    }

    [Fact]
    public void SixtyFourPercentCountsToTheAdultLine()
    {
        var tracker = new GrowthTracker();
        // 6%/h at 64% → (0.75 − 0.64) / 0.06 ≈ 1.83 h, not the 6 h to full growth.
        Feed(tracker, 0.63, 0.64, TimeSpan.FromMinutes(10));
        Assert.Equal(75, tracker.NextStage);
        Assert.InRange(tracker.Eta!.Value.TotalHours, 1.78, 1.88);
    }

    [Fact]
    public void CrossingALineTargetsTheNextWithTheSameRate()
    {
        var tracker = new GrowthTracker();
        // 4% over 10 minutes → 24%/h: at 74%, 2.5 minutes to the adult line.
        Feed(tracker, 0.70, 0.74, TimeSpan.FromMinutes(10));
        Assert.Equal(75, tracker.NextStage);
        Assert.InRange(tracker.Eta!.Value.TotalMinutes, 2.4, 2.6);

        // Past 75% the full-grown line is next, the slope still measured over the
        // window: 6% in 11 minutes → (0.9995 − 0.76) / 0.327 ≈ 0.73 h.
        tracker.Add(Player(0.76), T0 + TimeSpan.FromMinutes(11));
        Assert.Equal(GrowthTracker.GrowthStatus.Growing, tracker.Status);
        Assert.Equal(100, tracker.NextStage);
        Assert.InRange(tracker.Eta!.Value.TotalHours, 0.72, 0.745);
    }

    [Fact]
    public void TheLastStretchCountsToTheFullGrownLine()
    {
        var tracker = new GrowthTracker();
        // 6%/h at 99% → (0.9995 − 0.99) / 0.06 = 9.5 minutes to elder.
        Feed(tracker, 0.98, 0.99, TimeSpan.FromMinutes(10));
        Assert.Equal(100, tracker.NextStage);
        Assert.InRange(tracker.Eta!.Value.TotalMinutes, 9.3, 9.7);
    }

    [Fact]
    public void FlatGrowthAfterBaselineIsPaused()
    {
        var tracker = new GrowthTracker();
        Feed(tracker, 0.40, 0.40, TimeSpan.FromMinutes(6));
        Assert.Equal(GrowthTracker.GrowthStatus.Paused, tracker.Status);
        Assert.Null(tracker.Eta);
        Assert.Null(tracker.NextStage);
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
        Assert.Null(tracker.Eta);
        Assert.Null(tracker.NextStage);
    }

    [Fact]
    public void FullGrowthRetiresTheEstimate()
    {
        var tracker = new GrowthTracker();
        Feed(tracker, 0.98, 0.99, TimeSpan.FromMinutes(10));
        tracker.Add(Player(1.0), T0 + TimeSpan.FromMinutes(10.1));
        Assert.Equal(GrowthTracker.GrowthStatus.Full, tracker.Status);
        Assert.Null(tracker.Eta);
        Assert.Null(tracker.NextStage);
    }
}
