using Xunit;

namespace PandoraOverlay.Tests;

public class StaminaTrackerTests
{
    private static readonly DateTime T0 = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double stamina, string dino = "Deinosuchus") =>
        new("765611", "Tester", dino, "Male", 0.5, 1, stamina, 1, 1, 0, false, false, false, 0, 0, 0);

    /// <summary>Feeds the values three seconds apart, starting at the given second.</summary>
    private static void Feed(StaminaTracker tracker, int startSecond, params double[] values)
    {
        for (var i = 0; i < values.Length; i++) tracker.Add(Player(values[i]), T0.AddSeconds(startSecond + i * 3));
    }

    [Fact]
    public void NeedsTwoConsistentStepsBeforeSayingAnything()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0);
        Assert.Null(t.Label);
        Feed(t, 3, 0.94);
        Assert.Null(t.Label); // one step is not a pace yet
        Assert.Equal(StaminaTracker.Trend.Draining, t.Direction);
        Feed(t, 6, 0.88);
        Assert.NotNull(t.Label);
    }

    [Fact]
    public void DrainingEstimatesTimeUntilEmpty()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0, 0.94, 0.88); // 0.02 per second → 0.88 lasts 44 s
        Assert.Equal(StaminaTracker.Trend.Draining, t.Direction);
        Assert.Equal(44, t.Estimate!.Value.TotalSeconds, 1);
        Assert.Equal("~44s", t.Label);
    }

    [Fact]
    public void RecoveringEstimatesTimeUntilFullAndSaysSo()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 0.20, 0.25, 0.30); // 0.05 per 3 s → 0.70 to go takes 42 s
        Assert.Equal(StaminaTracker.Trend.Recovering, t.Direction);
        Assert.Equal(42, t.Estimate!.Value.TotalSeconds, 1);
        Assert.Equal("full ~42s", t.Label);
    }

    [Fact]
    public void AFlatPollEndsTheRun()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0, 0.94, 0.88);
        Feed(t, 9, 0.88); // stopped sprinting
        Assert.Null(t.Label);
        Assert.Equal(StaminaTracker.Trend.Steady, t.Direction);
        Feed(t, 12, 0.82);
        Assert.Null(t.Label); // one step again
        Feed(t, 15, 0.76);
        Assert.NotNull(t.Label);
    }

    [Fact]
    public void AReversalStartsANewRunFromThePreviousSample()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0, 0.90, 0.80);
        Feed(t, 9, 0.84); // started resting: one recovering step
        Assert.Equal(StaminaTracker.Trend.Recovering, t.Direction);
        Assert.Null(t.Label);
        Feed(t, 12, 0.88);
        Assert.StartsWith("full ", t.Label);
        // 0.08 over 6 s from 0.80 → 0.12 to go takes 9 s
        Assert.Equal(9, t.Estimate!.Value.TotalSeconds, 1);
    }

    [Fact]
    public void NothingToEstimateAtTheEnds()
    {
        var empty = new StaminaTracker();
        Feed(empty, 0, 0.10, 0.05, 0.0);
        Assert.Null(empty.Label); // already empty

        var full = new StaminaTracker();
        Feed(full, 0, 0.90, 0.95, 1.0);
        Assert.Null(full.Label); // already full
    }

    [Fact]
    public void ALongGapBetweenPollsBreaksTheRun()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0, 0.94);
        t.Add(Player(0.60), T0.AddSeconds(3 + 15)); // an idle-paced poll later
        Assert.Null(t.Label);
        Assert.Equal(StaminaTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void ACrawlIsNotWorthANumber()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 0.500, 0.495, 0.490); // 0.005 per 3 s → 294 s: just inside five minutes
        Assert.NotNull(t.Label);
        Assert.Equal("~5m", t.Label);

        var slower = new StaminaTracker();
        Feed(slower, 0, 0.900, 0.895, 0.890); // → 534 s: too far out
        Assert.Null(slower.Label);
    }

    [Fact]
    public void FollowsAChangedPaceWithinAFewPolls()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.00, 0.98, 0.96, 0.94, 0.92, 0.90); // a slow drain: 0.02 per poll
        var slow = t.Estimate!.Value;
        Feed(t, 18, 0.80, 0.70, 0.60, 0.50, 0.40, 0.30); // a sprint: 0.10 per poll
        // Only the last six samples count now: 0.10 per 3 s → 0.30 lasts 9 s.
        Assert.Equal(9, t.Estimate!.Value.TotalSeconds, 1);
        Assert.True(t.Estimate < slow);
    }

    [Fact]
    public void ANewDinoStartsOver()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0, 0.94, 0.88);
        t.Add(Player(0.82, dino: "Ceratosaurus"), T0.AddSeconds(9));
        Assert.Null(t.Label);
        Assert.Equal(StaminaTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var t = new StaminaTracker();
        Feed(t, 0, 1.0, 0.94, 0.88);
        t.Reset();
        Assert.Null(t.Label);
        Assert.Null(t.Estimate);
        Assert.Equal(StaminaTracker.Trend.Steady, t.Direction);
    }
}
