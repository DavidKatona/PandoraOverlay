using Xunit;

namespace PandoraOverlay.Tests;

public class DrainTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double hunger, double growth = 0.5, string dino = "Pteranodon") =>
        new("765611", "Tester", dino, "Male", growth, 1, 1, hunger, 1, 0, false, false, false, 0, 0, 0);

    private static DrainTracker Tracker() => new(p => p.Hunger);

    /// <summary>Feeds linearly interpolated hunger from → to over the given span, starting at the offset.</summary>
    private static void Feed(DrainTracker tracker, double from, double to, TimeSpan over,
                             TimeSpan startAt = default, int steps = 60)
    {
        for (var i = 0; i <= steps; i++)
        {
            var f = (double)i / steps;
            tracker.Add(Player(from + (to - from) * f), T0 + startAt + over * f);
        }
    }

    [Fact]
    public void NoEstimateBeforeBaseline()
    {
        var tracker = Tracker();
        Feed(tracker, 0.50, 0.49, TimeSpan.FromMinutes(2));
        Assert.Null(tracker.TimeLeft);
        Assert.Null(tracker.Label);
    }

    [Fact]
    public void EstimatesTimeLeftFromDrain()
    {
        var tracker = Tracker();
        // 5% over 5 minutes → 60%/h → 0.45 / 0.6 = 45 min left.
        Feed(tracker, 0.50, 0.45, TimeSpan.FromMinutes(5));
        Assert.NotNull(tracker.TimeLeft);
        Assert.InRange(tracker.TimeLeft!.Value.TotalMinutes, 44, 46);
        Assert.Matches(@"^~4[456]m$", tracker.Label);
    }

    [Fact]
    public void FlatStatAfterBaselineShowsNothing()
    {
        var tracker = Tracker();
        Feed(tracker, 0.80, 0.80, TimeSpan.FromMinutes(5));
        Assert.Null(tracker.TimeLeft);
        Assert.Null(tracker.Label);
    }

    [Fact]
    public void RefillKeepsTheRate() // ate: the level jumps, the metabolism didn't change
    {
        var tracker = Tracker();
        Feed(tracker, 0.50, 0.45, TimeSpan.FromMinutes(5)); // 60%/h
        tracker.Add(Player(0.90), T0 + TimeSpan.FromMinutes(5.1));
        Assert.NotNull(tracker.TimeLeft);
        Assert.InRange(tracker.TimeLeft!.Value.TotalMinutes, 88, 92); // 0.9 / 0.6 h
    }

    [Fact]
    public void NewBaselineAfterRefillReplacesTheRate()
    {
        var tracker = Tracker();
        Feed(tracker, 0.50, 0.45, TimeSpan.FromMinutes(5)); // 60%/h
        // After eating the drain is half as fast: 2.5% over 5 min → 30%/h.
        Feed(tracker, 0.90, 0.875, TimeSpan.FromMinutes(5), startAt: TimeSpan.FromMinutes(5.1));
        Assert.InRange(tracker.TimeLeft!.Value.TotalMinutes, 170, 180); // 0.875 / 0.3 h
    }

    [Fact]
    public void FarOutEstimateIsHidden()
    {
        var tracker = Tracker();
        // 1% over 5 minutes → 12%/h → 0.89 / 0.12 ≈ 7.4 h: measured, but not worth a label.
        Feed(tracker, 0.90, 0.89, TimeSpan.FromMinutes(5));
        Assert.NotNull(tracker.TimeLeft);
        Assert.Null(tracker.Label);
    }

    [Fact]
    public void JustOverAnHourIsHiddenWhenNeverShown()
    {
        var tracker = Tracker();
        // 60%/h → 0.63 / 0.6 = 63 min: above the 60 min show threshold.
        Feed(tracker, 0.68, 0.63, TimeSpan.FromMinutes(5));
        Assert.InRange(tracker.TimeLeft!.Value.TotalMinutes, 62, 64);
        Assert.Null(tracker.Label);
    }

    [Fact]
    public void ShownLabelSurvivesADriftJustOverAnHour() // no blinking at the threshold
    {
        var tracker = Tracker();
        Feed(tracker, 0.63, 0.58, TimeSpan.FromMinutes(5)); // 60%/h → 58 min: shown
        Assert.Matches(@"^~5[78]m$", tracker.Label);

        tracker.Add(Player(0.63), T0 + TimeSpan.FromMinutes(5.1)); // a bite: 63 min, inside the 65 min hide gap
        Assert.Matches(@"^~1h 0[23]m$", tracker.Label);

        tracker.Add(Player(0.70), T0 + TimeSpan.FromMinutes(5.2)); // a meal: 70 min — gone
        Assert.Null(tracker.Label);
    }

    [Fact]
    public void DinoSwapDropsTheRate()
    {
        var tracker = Tracker();
        Feed(tracker, 0.50, 0.45, TimeSpan.FromMinutes(5));
        tracker.Add(Player(0.45, dino: "Deinosuchus"), T0 + TimeSpan.FromMinutes(5.1));
        Assert.Null(tracker.TimeLeft);
    }

    [Fact]
    public void GrowthDecreaseDropsTheRate() // death → fresh spawn of the same species
    {
        var tracker = Tracker();
        Feed(tracker, 0.50, 0.45, TimeSpan.FromMinutes(5));
        tracker.Add(Player(1.0, growth: 0.25), T0 + TimeSpan.FromMinutes(5.1));
        Assert.Null(tracker.TimeLeft);
    }

    [Fact]
    public void EmptyStatShowsNothing()
    {
        var tracker = Tracker();
        Feed(tracker, 0.05, 0.0, TimeSpan.FromMinutes(5));
        Assert.Null(tracker.TimeLeft);
        Assert.Null(tracker.Label);
    }
}
