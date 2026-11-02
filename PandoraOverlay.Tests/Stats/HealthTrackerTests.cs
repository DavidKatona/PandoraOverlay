using Xunit;

namespace PandoraOverlay.Tests;

public class HealthTrackerTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double health, double growth = 0.5, string dino = "Deinosuchus") =>
        new("765611", "Tester", dino, "Male", growth, health, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    /// <summary>Feeds the values three seconds apart (the default cadence), starting at the given second.</summary>
    private static void Feed(HealthTracker tracker, int startSecond, params double[] values) =>
        FeedEvery(tracker, 3, startSecond, values);

    /// <summary>Feeds the values the given number of seconds apart, starting at the given second.</summary>
    private static void FeedEvery(HealthTracker tracker, int every, int startSecond, params double[] values)
    {
        for (var i = 0; i < values.Length; i++) tracker.Add(Player(values[i]), T0.AddSeconds(startSecond + i * every));
    }

    // ---- Rising ---------------------------------------------------------------

    [Fact]
    public void ClimbingReadsAfterTwoStepsAsTimeUntilFull()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.160);
        Assert.Null(t.Label);
        Feed(t, 3, 0.163);
        Assert.Null(t.Label); // one step is not a pace yet
        Assert.Equal(HealthTracker.Trend.Rising, t.Direction);
        Feed(t, 6, 0.166); // 0.001 per second → 0.834 to go takes 834 s
        Assert.Equal(834, t.Estimate!.Value.TotalSeconds, 0);
        Assert.Equal("full ~14m", t.Label);
    }

    [Fact]
    public void HealingRightAfterAHitReads()
    {
        // A hit holds back only the FALLING reading: healing after a fight is the climb the label is for.
        var t = new HealthTracker();
        Feed(t, 0, 0.80, 0.70, 0.703, 0.706);
        Assert.Equal(HealthTracker.Trend.Rising, t.Direction);
        Assert.StartsWith("full ~", t.Label);
    }

    // ---- Falling --------------------------------------------------------------

    [Fact]
    public void FourSteadySmallDropsReadAsTimeUntilEmpty()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476, 0.468); // 0.032 over 12 s → 0.468 lasts 175.5 s
        Assert.Equal(HealthTracker.Trend.Falling, t.Direction);
        Assert.Equal(175.5, t.Estimate!.Value.TotalSeconds, 1);
        Assert.Equal("~3m", t.Label);
    }

    [Fact]
    public void ThreeDropsAreNotEnough()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476);
        Assert.Equal(HealthTracker.Trend.Falling, t.Direction);
        Assert.Null(t.Label);
        Assert.Null(t.Estimate);
    }

    [Fact]
    public void ASlowFallsUnevenDropsStillReadAsOne()
    {
        // 0.0025 a poll arrives as 0.002 and 0.003 in the API's 3 decimals: similar, not a hit.
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.498, 0.495, 0.493, 0.490);
        Assert.Equal("~10m", t.Label);
    }

    [Fact]
    public void OneSpikyDropShowsNothing()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.80, 0.70);
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void AFightsHitsShowNothing()
    {
        var t = new HealthTracker();
        for (var i = 0; i < 8; i++)
        {
            Feed(t, i * 3, 0.90 - i * 0.05); // a 5% hit every poll
            Assert.Null(t.Label);
        }
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void ASpikeHoldsTheFallBackForThirtySeconds()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.80, 0.70); // the hit, at 3 s
        for (var k = 1; k <= 10; k++)
        {
            Feed(t, 3 + 3 * k, 0.70 - 0.008 * k); // then a steady fall
            if (k == 4)
            {
                Assert.Equal(HealthTracker.Trend.Falling, t.Direction);
                Assert.Null(t.Label); // four similar drops, but the hit was 12 s ago
            }
            if (k == 9) Assert.Null(t.Label); // 27 s after the hit
        }
        // 30 s after the hit: the last ten samples, 0.072 over 27 s → 0.62 lasts 232.5 s
        Assert.Equal(232.5, t.Estimate!.Value.TotalSeconds, 1);
        Assert.Equal("~4m", t.Label);
    }

    [Fact]
    public void ABigDropAmongSmallOnesEndsTheRun()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.600, 0.596, 0.592, 0.588, 0.584);
        Assert.NotNull(t.Label);

        Feed(t, 15, 0.569); // 0.015: under a spike, but almost four times the run's drops
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);

        Feed(t, 18, 0.565, 0.561, 0.557);
        Assert.Null(t.Label); // three drops since the big one
        Feed(t, 27, 0.553);
        Assert.NotNull(t.Label); // four: and no 30 s hold, the big drop was not a spike
    }

    // ---- What ends a run ------------------------------------------------------

    [Fact]
    public void AFlatPollEndsTheRun()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476, 0.468);
        Feed(t, 15, 0.468);
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void AOneUnitChangeIsTheWobble()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.501, 0.502, 0.503, 0.504);
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void ALongGapBetweenPollsEndsTheRun()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476, 0.468);
        t.Add(Player(0.428), T0.AddSeconds(12 + 15)); // an idle-paced poll later
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void AReversalStartsANewRunFromThePreviousSample()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476, 0.468);
        Assert.NotNull(t.Label);

        Feed(t, 15, 0.471); // the fall stopped and health climbs: one rising step
        Assert.Equal(HealthTracker.Trend.Rising, t.Direction);
        Assert.Null(t.Label);
        Feed(t, 18, 0.474);
        // 0.006 over 6 s from 0.468 → 0.526 to go takes 526 s
        Assert.Equal(526, t.Estimate!.Value.TotalSeconds, 0);
        Assert.Equal("full ~9m", t.Label);

        Feed(t, 21, 0.470, 0.466, 0.462); // falling again: three drops
        Assert.Equal(HealthTracker.Trend.Falling, t.Direction);
        Assert.Null(t.Label);
        Feed(t, 30, 0.458);
        Assert.NotNull(t.Label);
    }

    [Fact]
    public void NothingToEstimateAtTheEnds()
    {
        var full = new HealthTracker();
        Feed(full, 0, 0.990, 0.993, 0.996);
        Assert.Null(full.Label); // already full

        var empty = new HealthTracker();
        Feed(empty, 0, 0.020, 0.016, 0.012, 0.008, 0.004);
        Assert.Null(empty.Label); // already empty
    }

    // ---- The cap ----------------------------------------------------------------

    [Fact]
    public void AnEstimateOverAnHourShowsNothing()
    {
        var t = new HealthTracker();
        FeedEvery(t, 8, 0, 0.050, 0.052, 0.054); // 0.00025 per second → 0.946 to go takes 63 min
        Assert.Equal(HealthTracker.Trend.Rising, t.Direction);
        Assert.Null(t.Label);
        Assert.Null(t.Estimate);
    }

    [Fact]
    public void AShownLabelStaysUntilSixtyFiveMinutes()
    {
        var t = new HealthTracker();
        FeedEvery(t, 6, 0, 0.000, 0.002, 0.004); // → 50 min: shown
        Assert.NotNull(t.Label);

        // The polls slow to 8 s apart and the estimate climbs past the hour.
        FeedEvery(t, 8, 20, 0.006, 0.008, 0.010, 0.012, 0.014, 0.016, 0.018, 0.020);
        Assert.InRange(t.Estimate!.Value.TotalMinutes, 60, 65); // 63.5 min: kept, a fresh run would not show it
        Assert.Equal("full ~64m", t.Label);

        FeedEvery(t, 8, 84, 0.022); // 65.2 min
        Assert.Null(t.Label);
        Assert.Null(t.Estimate);
    }

    // ---- The format -------------------------------------------------------------

    [Fact]
    public void SecondsUnderAHundredThenWholeMinutes()
    {
        var ninety = new HealthTracker();
        Feed(ninety, 0, 0.34, 0.33, 0.32, 0.31, 0.30); // 0.04 over 12 s → 0.30 lasts 90 s
        Assert.Equal("~90s", ninety.Label);

        var past = new HealthTracker();
        Feed(past, 0, 0.40, 0.39, 0.38, 0.37, 0.36); // → 108 s
        Assert.Equal("~2m", past.Label);

        var fourteen = new HealthTracker();
        Feed(fourteen, 0, 0.568, 0.566, 0.564, 0.562, 0.560); // 0.008 over 12 s → 0.56 lasts 840 s
        Assert.Equal("~14m", fourteen.Label);
    }

    // ---- A new life ---------------------------------------------------------------

    [Fact]
    public void ANewDinoStartsOver()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476, 0.468);
        t.Add(Player(0.460, dino: "Ceratosaurus"), T0.AddSeconds(15));
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void AGrowthDropStartsOver()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.500, 0.492, 0.484, 0.476, 0.468);
        t.Add(Player(0.460, growth: 0.1), T0.AddSeconds(15)); // a fresh life of the same dino
        Assert.Null(t.Label);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);
    }

    [Fact]
    public void ResetClearsEverythingAndForgetsAHit()
    {
        var t = new HealthTracker();
        Feed(t, 0, 0.80, 0.70, 0.692, 0.684, 0.676, 0.668);
        t.Reset();
        Assert.Null(t.Label);
        Assert.Null(t.Estimate);
        Assert.Equal(HealthTracker.Trend.Steady, t.Direction);

        Feed(t, 18, 0.660, 0.652, 0.644, 0.636, 0.628);
        Assert.NotNull(t.Label); // the hit 15 s ago belonged to the run before the reset
    }
}
