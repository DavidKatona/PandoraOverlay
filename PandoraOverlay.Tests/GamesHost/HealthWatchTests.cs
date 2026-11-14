using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's health warning rule (G18, A15), as short streams of
/// readings with times: a drop starts trouble and warns, "Keep playing"
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
    public void SmallDropsAddUpOnlyAsSeparateSteps()
    {
        // Each poll is compared with the last: a slow leak under the step never warns.
        var watch = Watching(0.900);
        for (var i = 1; i <= 20; i++) Assert.Equal(HealthNotice.None, watch.Update(P(0.900 - 0.004 * i), At(3 * i)));
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
}
