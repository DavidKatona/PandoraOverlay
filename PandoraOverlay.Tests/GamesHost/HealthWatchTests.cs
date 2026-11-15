using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's health warning rule (G18, A15, A59), as short streams of
/// readings with times: a drop (a hit, or a drain: a fall that has added up to
/// two points within two minutes) starts trouble and warns, "Keep playing"
/// quiets it for that trouble unless health crosses down a quarter, 30 quiet
/// seconds end it, and a new life or leaving the game starts clean.
/// </summary>
public class HealthWatchTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>A player record with nothing personal in it: a made-up id, no name, no position.</summary>
    private static PlayerState P(double health, double growth = 0.5, string dino = "Deinosuchus", string gender = "Male", string steamId = "player-a") =>
        new(steamId, null, dino, gender, growth, health, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    private static HealthWatch Watching(double health)
    {
        var watch = new HealthWatch();
        Assert.Equal(HealthNotice.None, watch.Update(P(health), At(0)));
        return watch;
    }

    [Fact]
    public void ItsNumbersAreItsOwn()
    {
        Assert.Equal(0.005, HealthWatch.Step);
        Assert.Equal(TimeSpan.FromSeconds(30), HealthWatch.Quiet);
        Assert.Equal(0.02, HealthWatch.DrainFall);
        Assert.Equal(TimeSpan.FromSeconds(120), HealthWatch.DrainWindow);
    }

    [Fact]
    public void TheFirstReadingIsTheBaseline()
    {
        var watch = new HealthWatch();

        Assert.Equal(HealthNotice.None, watch.Update(P(0.3), At(0)));
        Assert.Equal(30, watch.HealthPercent);
        Assert.False(watch.TroubledAt(At(0)));
    }

    [Fact]
    public void ADropOfExactlyTheStepWarns()
    {
        var watch = Watching(0.62);

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.615), At(3)));
        Assert.True(watch.TroubledAt(At(3)));
    }

    [Theory]
    [InlineData(0.62, 0.616)] // the API's last-digit wobble
    [InlineData(0.62, 0.62)]
    [InlineData(0.62, 0.70)]  // rising
    public void LessThanAStepOrRisingNeverWarns(double from, double to)
    {
        var watch = Watching(from);

        Assert.Equal(HealthNotice.None, watch.Update(P(to), At(3)));
        Assert.False(watch.TroubledAt(At(3)));
    }

    [Fact]
    public void ASlowLeakUnderTheStepWarnsOnceItHasFallenTwoPoints()
    {
        // A59 reverses what this test used to pin ("a slow leak under the step
        // never warns"): no single poll is a hit, but the fall adds up.
        var watch = Watching(0.900);
        for (var i = 1; i <= 4; i++) Assert.Equal(HealthNotice.None, watch.Update(P(0.900 - 0.004 * i), At(3 * i)));

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.880), At(15))); // two points under 0.900
        Assert.True(watch.TroubledAt(At(15)));
    }

    [Fact]
    public void UnacknowledgedTroubleWarnsAtEveryDrop()
    {
        // The window may have been closed at the first warning: it was not seen.
        var watch = Watching(0.9);

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.80), At(6)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.80), At(9)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.78), At(12)));
    }

    /// <summary>The brief's stream: a hit, "Keep playing", a second hit ten seconds later, a third under 50%, 30 quiet seconds, a hit.</summary>
    [Fact]
    public void AHitKeepPlayingASecondHitAThirdUnderHalfThenQuietThenAHit()
    {
        var watch = Watching(0.80);

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.70), At(3)));    // a hit
        watch.Acknowledge();                                                 // "Keep playing", at 70%: the 50-75 quarter
        Assert.False(watch.TroubledAt(At(4)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.60), At(13)));   // ten seconds later, same quarter: quiet
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.45), At(16)));   // crosses below 50%: warns again
        Assert.True(watch.TroubledAt(At(16)));                              // the acknowledgement was withdrawn
        watch.Acknowledge();
        Assert.Equal(HealthNotice.None, watch.Update(P(0.47), At(30)));   // healing
        Assert.Equal(HealthNotice.None, watch.Update(P(0.47), At(46)));   // 30 s after the last drop: over
        Assert.False(watch.TroubledAt(At(46)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.40), At(49)));   // a new trouble: warns, whatever the quarter
    }

    [Fact]
    public void AnAcknowledgedTroubleWarnsAgainAtEachLowerQuarter()
    {
        var watch = Watching(0.95);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.90), At(3)));
        watch.Acknowledge(); // above 75%

        Assert.Equal(HealthNotice.None, watch.Update(P(0.76), At(6)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.74), At(9)));   // into 50-75
        watch.Acknowledge();
        Assert.Equal(HealthNotice.None, watch.Update(P(0.50), At(12)));  // on the line: still the quarter above
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.49), At(15)));  // into 25-50
        watch.Acknowledge();
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.20), At(18)));  // into under 25
        watch.Acknowledge();
        Assert.Equal(HealthNotice.None, watch.Update(P(0.10), At(21)));  // there is no lower quarter
    }

    [Fact]
    public void AQuarterSkippedAtOnceIsOneWarning()
    {
        var watch = Watching(0.95);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.90), At(3)));
        watch.Acknowledge();

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.30), At(6))); // 90 to 30 in one poll
        watch.Acknowledge();
        Assert.Equal(HealthNotice.None, watch.Update(P(0.26), At(9)));
    }

    [Fact]
    public void TheAcknowledgedQuarterIsWhereHealthWasAtTheAcknowledgement()
    {
        // Healing back up a quarter before "Keep playing" moves the line up with it.
        var watch = Watching(0.80);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.70), At(3)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.78), At(6)));
        watch.Acknowledge(); // at 78%: above 75

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.72), At(9)));
    }

    [Fact]
    public void TroubleEndsExactlyAfterQuiet()
    {
        var watch = Watching(0.9);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));
        watch.Acknowledge();

        Assert.Equal(HealthNotice.None, watch.Update(P(0.84), At(32))); // 29 s: the same trouble, acknowledged
        Assert.False(watch.TroubledAt(At(32)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.83), At(62))); // exactly 30 s after that drop: a new trouble
    }

    [Fact]
    public void RisingHealthDoesNotKeepTroubleGoing()
    {
        var watch = Watching(0.9);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));

        Assert.True(watch.TroubledAt(At(32)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.88), At(20)));
        Assert.False(watch.TroubledAt(At(33))); // 30 s after the drop, healing or not
    }

    [Fact]
    public void AWindowOpeningMidTroubleWarnsUntilAcknowledged()
    {
        var watch = Watching(0.9);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3))); // the window was closed

        Assert.True(watch.TroubledAt(At(10)));
        watch.Acknowledge(); // the window closed again (or "Keep playing")
        Assert.False(watch.TroubledAt(At(11)));
        Assert.False(watch.TroubledAt(At(40)));
    }

    [Fact]
    public void AcknowledgingOutsideTroubleDoesNothing()
    {
        var watch = Watching(0.9);
        watch.Acknowledge(); // closing the window with nothing wrong

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));
    }

    [Theory]
    [InlineData("player-b", "Deinosuchus", "Male", 0.5)] // another account
    [InlineData("player-a", "Tyrannosaurus", "Male", 0.5)] // another dino
    [InlineData("player-a", "Deinosuchus", "Female", 0.5)] // a dino of the other gender
    [InlineData("player-a", "Deinosuchus", "Male", 0.4989)] // growth fell: a fresh life
    public void ANewLifeIsAFreshBaselineWithNoTrouble(string steamId, string dino, string gender, double growth)
    {
        var watch = Watching(0.9);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));

        Assert.Equal(HealthNotice.None, watch.Update(P(0.2, growth, dino, gender, steamId), At(6)));
        Assert.False(watch.TroubledAt(At(6)));
        Assert.Equal(20, watch.HealthPercent);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.15, growth, dino, gender, steamId), At(9))); // its own first drop
    }

    [Fact]
    public void GrowthFallingByLessThanATenthOfAPercentIsTheSameLife()
    {
        var watch = Watching(0.9);

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85, growth: 0.4995), At(3)));
    }

    [Fact]
    public void LeavingTheGameForgetsEverything()
    {
        var watch = Watching(0.9);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));

        Assert.Equal(HealthNotice.None, watch.Update(null, At(6)));
        Assert.False(watch.TroubledAt(At(6)));
        Assert.Equal(0, watch.HealthPercent);
        Assert.Equal(HealthNotice.None, watch.Update(P(0.5), At(9))); // back in: a baseline, not a drop from 85%
    }

    [Theory]
    [InlineData(0.629, 62)]
    [InlineData(0.29, 29)]  // 28.999... in binary
    [InlineData(0.57, 57)]
    [InlineData(1.0, 100)]
    [InlineData(0.0, 0)]
    [InlineData(1.5, 100)]
    [InlineData(-0.1, 0)]
    [InlineData(double.NaN, 0)]
    public void TheHealthPercentIsFloored(double health, int percent)
    {
        Assert.Equal(percent, HealthWatch.Percent(health));
        var watch = new HealthWatch();
        watch.Update(P(health), At(0));
        Assert.Equal(percent, watch.HealthPercent);
    }

    // The drain (A59): a poll that fell, two points or more under the life's
    // highest reading of the last 120 s since health was last lower (A66).
    // Every stream below falls by less than the step at each poll, unless it
    // says "a hit".

    [Fact]
    public void TheStatsPanelsSteadyFallWarnsWithinHalfAMinute()
    {
        // HealthTracker's reading of a real steady fall: two or three units of
        // the API's third decimal a poll (three decimals, as the API sends).
        var watch = Watching(0.750);
        var falling = new[] { 0.748, 0.745, 0.743, 0.740, 0.738, 0.735, 0.733 };
        for (var i = 0; i < falling.Length; i++)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(falling[i]), At(3 * (i + 1))));
        }

        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.730), At(24))); // the first reading two points under 0.750
        Assert.True(watch.TroubledAt(At(24)));
    }

    [Theory]
    [InlineData(2, 26)]
    [InlineData(3, 27)] // the default interval
    [InlineData(5, 25)]
    public void TheDrainDoesNotDependOnThePollInterval(int interval, int warnsAt)
    {
        // 0.0008 a second (4.8 points a minute), rounded as the API rounds: a
        // step of 0.0016 to 0.004 a poll, never a hit at any of these intervals.
        static double HealthAt(int seconds) => Math.Round(0.900 - 0.0008 * seconds, 3);
        var watch = Watching(HealthAt(0));

        var previous = HealthAt(0);
        for (var t = interval; t < warnsAt; t += interval)
        {
            Assert.True(previous - HealthAt(t) < HealthWatch.Step);   // the stream: no hit
            Assert.True(HealthAt(0) - HealthAt(t) < 0.02 - 1e-9);      // the stream: not yet two points under the start
            Assert.Equal(HealthNotice.None, watch.Update(P(HealthAt(t)), At(t)));
            previous = HealthAt(t);
        }

        Assert.Equal(0, warnsAt % interval);                           // the stream: warnsAt is a poll
        Assert.True(previous - HealthAt(warnsAt) < HealthWatch.Step);
        Assert.True(HealthAt(0) - HealthAt(warnsAt) >= 0.02 - 1e-9);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(HealthAt(warnsAt)), At(warnsAt)));
    }

    [Fact]
    public void AFallSlowerThanAPointAMinuteNeverWarns()
    {
        // 0.001 every 9 s, two thirds of a point a minute: the 120 s window never
        // holds more than 0.014 of it. A59's known gap, on purpose.
        var watch = Watching(0.900);
        for (var t = 3; t <= 600; t += 3)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(Math.Round(0.900 - 0.001 * (t / 9), 3)), At(t)));
            Assert.False(watch.TroubledAt(At(t)));
        }
    }

    [Fact]
    public void APointAMinuteIsCaughtAtTheWindowsEdge()
    {
        // 0.001 every 6 s, polled every 3 s: half the polls level, half one unit
        // lower. Two points take the whole window.
        var watch = Watching(0.900);
        for (var t = 3; t < 120; t += 3)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(Math.Round(0.900 - 0.001 * (t / 6), 3)), At(t)));
        }

        // 0.880 at 120 s: the 0.900 read at 0 s is exactly DrainWindow old and
        // still counts (read at 3 s too; the next test pins the edge alone).
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.880), At(120)));
    }

    [Fact]
    public void AReadingOlderThanTheWindowIsNoLongerThePeak()
    {
        // The only reading two points above 0.880 is the 0.900 at 0 s: the fall
        // to 0.880 comes in steps of 0.004, and the highest reading between,
        // 0.896, is only 0.016 above it.
        static HealthNotice LastAt(int lastAt)
        {
            var watch = Watching(0.900);
            Assert.Equal(HealthNotice.None, watch.Update(P(0.896), At(109)));
            Assert.Equal(HealthNotice.None, watch.Update(P(0.892), At(112)));
            Assert.Equal(HealthNotice.None, watch.Update(P(0.888), At(115)));
            Assert.Equal(HealthNotice.None, watch.Update(P(0.884), At(118)));
            return watch.Update(P(0.880), At(lastAt));
        }

        Assert.Equal(HealthNotice.None, LastAt(121)); // 121 s old: out of the window
        Assert.Equal(HealthNotice.Warn, LastAt(120)); // the control: exactly DrainWindow old still counts
    }

    [Fact]
    public void ALevelPollUnderThePeakIsNoDrain()
    {
        var watch = Watching(0.90);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.85), At(3)));     // a hit
        watch.Acknowledge();                                                 // "Keep playing"

        for (var t = 6; t <= 30; t += 3)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(0.85), At(t))); // level, five points under the peak: not a drain
        }

        // 30 s after the hit the trouble is over, as with no drain: a hit now is
        // a new trouble and warns (had a level poll counted, it would be the same
        // acknowledged trouble, and quiet).
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.84), At(33)));
    }

    [Fact]
    public void AOneUnitDipWhileHealingAfterAHitIsNoDrain()
    {
        var watch = Watching(0.900);
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.800), At(3)));    // a hit
        watch.Acknowledge();

        for (var k = 1; k <= 12; k++)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(Math.Round(0.800 + 0.001 * k, 3)), At(3 + 3 * k))); // healing: 0.801 at 6 s up to 0.812 at 39 s
        }

        // One unit lower while healing. A59's first build warned here: 0.900 at
        // 0 s was 42 s old, inside the window, and the hit's trouble had ended at
        // 33 s, so the dip started a new one. A66: every reading from the hit up
        // to 0.810 is under 0.811 and restarts the peak, which is then the heal's
        // own top, 0.812: one unit, no drain.
        Assert.Equal(HealthNotice.None, watch.Update(P(0.811), At(42)));
        Assert.False(watch.TroubledAt(At(42)));
    }

    [Fact]
    public void ADrainResumedAfterAHealCountsFromTheHealsTop()
    {
        var watch = Watching(0.800);
        Assert.Equal(HealthNotice.None, watch.Update(P(0.796), At(3)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.792), At(6)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.788), At(9)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.784), At(12)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.780), At(15)));   // two points under 0.800: a drain
        watch.Acknowledge();
        Assert.Equal(HealthNotice.None, watch.Update(P(0.776), At(18)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.772), At(21)));   // the drain's last fall, its low

        // The heal, to a top of 0.796 at 39 s.
        Assert.Equal(HealthNotice.None, watch.Update(P(0.776), At(24)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.780), At(27)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.784), At(30)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.788), At(33)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.792), At(36)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.796), At(39)));

        // Level; the trouble ends at 51 s, 30 s after its last fall at 21 s.
        for (var t = 42; t <= 57; t += 3)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(0.796), At(t)));
        }

        // The drain resumes.
        Assert.Equal(HealthNotice.None, watch.Update(P(0.792), At(60)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.788), At(63)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.784), At(66)));

        // A59's first build warned here, because 0.800 at 0 s is two points
        // above. A66: the last reading under 0.780 is 0.776 at 24 s, which
        // restarts the peak, and the highest since is the heal's top, 0.796:
        // 0.016, no drain.
        Assert.Equal(HealthNotice.None, watch.Update(P(0.780), At(69)));

        // Two points under the heal's top, still above the earlier low of 0.772:
        // a new trouble. The last reading under 0.776 is 0.772 at 21 s (the equal
        // 0.776 at 24 s does not restart the peak), and the highest since is 0.796.
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.776), At(72)));
    }

    [Fact]
    public void ASteadyDrainKeepsOneTroubleUntilThirtySecondsAfterItsLastFall()
    {
        // The same trouble, acknowledged: a hit 29 s after the drain's last fall is quiet.
        var sameTrouble = DrainedToTheQuarterAndAcknowledged();
        for (var t = 66; t <= 90; t += 3)
        {
            Assert.Equal(HealthNotice.None, sameTrouble.Update(P(0.7475), At(t))); // level: the drain stopped at 63 s
        }
        Assert.Equal(HealthNotice.None, sameTrouble.Update(P(0.740), At(92)));

        // A second run of the same stream: 31 s after the last fall the trouble is over.
        var newTrouble = DrainedToTheQuarterAndAcknowledged();
        for (var t = 66; t <= 93; t += 3)
        {
            Assert.Equal(HealthNotice.None, newTrouble.Update(P(0.7475), At(t)));
        }
        Assert.Equal(HealthNotice.Warn, newTrouble.Update(P(0.740), At(94)));
    }

    /// <summary>
    /// 0.800 falling 0.0025 every 3 s: warned at the first drain (24 s) and
    /// acknowledged, quiet at every poll that falls after it while health stays
    /// in the top quarter (0.75 itself is the quarter above, BarStates), warned
    /// again at 63 s crossing under 0.75, acknowledged again.
    /// </summary>
    private static HealthWatch DrainedToTheQuarterAndAcknowledged()
    {
        static double HealthAt(int poll) => Math.Round(0.800 - 0.0025 * poll, 4);
        var watch = Watching(HealthAt(0));

        for (var poll = 1; poll <= 7; poll++)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(HealthAt(poll)), At(3 * poll)));
        }
        Assert.Equal(HealthNotice.Warn, watch.Update(P(HealthAt(8)), At(24)));       // 0.780: the first drain
        watch.Acknowledge();

        for (var poll = 9; poll <= 20; poll++)
        {
            Assert.Equal(HealthNotice.None, watch.Update(P(HealthAt(poll)), At(3 * poll))); // down to 0.750 at 60 s
            Assert.False(watch.TroubledAt(At(3 * poll)));
        }
        Assert.Equal(HealthNotice.Warn, watch.Update(P(HealthAt(21)), At(63)));      // 0.7475: under 75%
        watch.Acknowledge();
        return watch;
    }

    [Fact]
    public void AClockSetBackForgetsTheWindow()
    {
        var watch = new HealthWatch();
        Assert.Equal(HealthNotice.None, watch.Update(P(0.900), At(100)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.896), At(103)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.892), At(106)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.888), At(109)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.884), At(112)));

        // The clock goes back to 50 s: without the clear, 0.900 (at "100 s",
        // ahead of now) would make 0.880 a drain.
        Assert.Equal(HealthNotice.None, watch.Update(P(0.880), At(50)));

        // The window starts again from the reading at 50 s.
        Assert.Equal(HealthNotice.None, watch.Update(P(0.876), At(53)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.872), At(56)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.868), At(59)));
        Assert.Equal(HealthNotice.None, watch.Update(P(0.864), At(62)));
        Assert.Equal(HealthNotice.Warn, watch.Update(P(0.860), At(65)));
    }

    [Fact]
    public void ANewLifeOrLeavingTheGameEmptiesTheWindow()
    {
        // Each life's own readings only: 0.880 is 0.004 under its life's first
        // reading, and two points under the old life's 0.900.
        var newLife = Watching(0.900);
        Assert.Equal(HealthNotice.None, newLife.Update(P(0.884, dino: "Tyrannosaurus"), At(3)));
        Assert.Equal(HealthNotice.None, newLife.Update(P(0.880, dino: "Tyrannosaurus"), At(6)));

        var leftTheGame = Watching(0.900);
        Assert.Equal(HealthNotice.None, leftTheGame.Update(null, At(3)));
        Assert.Equal(HealthNotice.None, leftTheGame.Update(P(0.884), At(6)));
        Assert.Equal(HealthNotice.None, leftTheGame.Update(P(0.880), At(9)));
    }
}
