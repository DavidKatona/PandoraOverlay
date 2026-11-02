using Xunit;

namespace PandoraOverlay.Tests;

public class GrowthReadoutTests
{
    private static readonly TimeSpan OneTwenty = TimeSpan.FromMinutes(80);

    /// <summary>The header's forms, the owner's (Nov 2 2026): no word "Growth", no decimal, "to" the next stage.</summary>
    [Theory]
    [InlineData(GrowthTracker.GrowthStatus.Growing, 0.642, 75, "64% · ~1h 20m to Adult")]
    [InlineData(GrowthTracker.GrowthStatus.Growing, 0.12, 25, "12% · ~1h 20m to Juvenile")]
    [InlineData(GrowthTracker.GrowthStatus.Growing, 0.38, 50, "38% · ~1h 20m to Subadult")]
    [InlineData(GrowthTracker.GrowthStatus.Growing, 0.87, 100, "87% · ~1h 20m to Elder")]
    [InlineData(GrowthTracker.GrowthStatus.Estimating, 0.642, null, "64%")]
    [InlineData(GrowthTracker.GrowthStatus.Paused, 0.642, null, "64% · paused")]
    [InlineData(GrowthTracker.GrowthStatus.Full, 1.0, null, "Elder")]
    [InlineData(GrowthTracker.GrowthStatus.Full, GrowthMilestones.FullyGrown, null, "Elder")]
    public void TheHeaderReadsAsTheOwnerWordedIt(GrowthTracker.GrowthStatus status, double growth, int? next, string text) =>
        Assert.Equal(text, GrowthReadout.Text(status, growth, status == GrowthTracker.GrowthStatus.Growing ? OneTwenty : null, next));

    [Fact]
    public void GrowingWithoutAnEstimateReadsAsMeasuring() =>
        Assert.Equal("64%", GrowthReadout.Text(GrowthTracker.GrowthStatus.Growing, 0.642, null, null));

    /// <summary>Floored like Dino storage's: a hair under the full-grown line is still 99%, and still counts to Elder.</summary>
    [Fact]
    public void ThePercentIsRoundedDown()
    {
        Assert.Equal("99% · ~2m to Elder",
            GrowthReadout.Text(GrowthTracker.GrowthStatus.Growing, 0.999, TimeSpan.FromMinutes(2), 100));
        Assert.Equal("64% · paused", GrowthReadout.Text(GrowthTracker.GrowthStatus.Paused, 0.6499, null, null));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(-0.1, 0)]
    [InlineData(0.58, 58)]     // 57.999… in floating point
    [InlineData(0.6499, 64)]
    [InlineData(0.996, 99)]
    [InlineData(0.9994, 99)]
    [InlineData(GrowthMilestones.FullyGrown, 100)]
    [InlineData(1.0, 100)]
    [InlineData(1.7, 100)]
    public void PercentIsWholeAndNeverFullBeforeTheLine(double growth, int percent) =>
        Assert.Equal(percent, GrowthReadout.Percent(growth));

    [Theory]
    [InlineData(30, "1m")]
    [InlineData(45 * 60, "45m")]
    [InlineData(80 * 60, "1h 20m")]
    [InlineData(190 * 60, "3h 10m")]
    [InlineData((23 * 60 + 59) * 60, "23h 59m")]
    [InlineData(28 * 3600, "1d 4h")]
    public void TheTimeKeepsItsForm(int seconds, string text) =>
        Assert.Equal(text, GrowthReadout.FormatEta(TimeSpan.FromSeconds(seconds)));
}
