using Xunit;

namespace PandoraOverlay.Tests;

public class SpeedTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoSpeedFromOneSample()
    {
        var s = new SpeedTracker();
        s.Add(0, 0, T0);
        Assert.Null(s.SpeedMps);
        Assert.Null(s.ClosingMps(1000, 0));
    }

    [Fact]
    public void MeasuresPathOverTheWindow() // 10 m per 3 s poll, east
    {
        var s = new SpeedTracker();
        for (var i = 0; i < 5; i++) s.Add(i * 1000, 0, T0 + TimeSpan.FromSeconds(3 * i));
        Assert.InRange(s.SpeedMps!.Value, 3.3, 3.4);
    }

    [Fact]
    public void ZigZagCountsThePathNotTheDisplacement()
    {
        var s = new SpeedTracker();
        s.Add(0, 0, T0);
        s.Add(1000, 0, T0 + TimeSpan.FromSeconds(3));
        s.Add(0, 0, T0 + TimeSpan.FromSeconds(6)); // back where it started
        Assert.InRange(s.SpeedMps!.Value, 3.3, 3.4);
    }

    [Fact]
    public void ClosingSpeedIsSignedTowardTheTarget()
    {
        var s = new SpeedTracker();
        s.Add(0, 0, T0);
        s.Add(1000, 0, T0 + TimeSpan.FromSeconds(3)); // 10 m east in 3 s
        Assert.InRange(s.ClosingMps(10_000, 0)!.Value, 3.3, 3.4);   // target ahead
        Assert.InRange(s.ClosingMps(-10_000, 0)!.Value, -3.4, -3.3); // target behind
        Assert.InRange(s.ClosingMps(500, 100_000)!.Value, -0.1, 0.1); // target abeam
    }

    [Fact]
    public void ImpossibleJumpRestartsTheWindow()
    {
        var s = new SpeedTracker();
        s.Add(0, 0, T0);
        s.Add(1000, 0, T0 + TimeSpan.FromSeconds(3));
        s.Add(500_000, 0, T0 + TimeSpan.FromSeconds(6)); // 5 km in 3 s: respawn
        Assert.Null(s.SpeedMps);
    }

    [Fact]
    public void LongGapRestartsTheWindow()
    {
        var s = new SpeedTracker();
        s.Add(0, 0, T0);
        s.Add(1000, 0, T0 + TimeSpan.FromSeconds(3));
        s.Add(1500, 0, T0 + TimeSpan.FromMinutes(2)); // idle polling resumed
        Assert.Null(s.SpeedMps);
    }
}

public class CompassTests
{
    [Theory]
    [InlineData(0, "N")]
    [InlineData(22, "N")]
    [InlineData(23, "NE")]
    [InlineData(90, "E")]
    [InlineData(180, "S")]
    [InlineData(270, "W")]
    [InlineData(337, "NW")]
    [InlineData(338, "N")]
    [InlineData(-90, "W")]
    [InlineData(450, "E")]
    public void EightPoints(double degrees, string expected) => Assert.Equal(expected, Compass.Letter(degrees));
}

public class ShareCodeTests
{
    [Fact]
    public void RoundTripsInMetres()
    {
        var code = ShareCode.Format(6167.09, -316782.07);
        Assert.Equal("pandora:62,-3168", code);
        Assert.True(ShareCode.TryParse(code, out var x, out var y));
        Assert.Equal(6200, x);
        Assert.Equal(-316800, y);
    }

    [Theory]
    [InlineData("62,-3168")]
    [InlineData("  pandora: 62 , -3168  ")]
    [InlineData("meet me at pandora:62,-3168 by the river")]
    public void ParsesForgivingly(string text)
    {
        Assert.True(ShareCode.TryParse(text, out var x, out var y));
        Assert.Equal(6200, x);
        Assert.Equal(-316800, y);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("hello")]
    [InlineData("62")]
    [InlineData("999999,1")]
    public void RejectsNonCodes(string? text) => Assert.False(ShareCode.TryParse(text, out _, out _));
}
