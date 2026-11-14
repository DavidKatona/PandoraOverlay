using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The growth lines and the stage a growth sits in (A07). That the lines are
/// the overlay's own is held in the app's tests, <c>GrowthLinesInStepTests</c>,
/// which can see both.
/// </summary>
public class GrowthStagesTests
{
    [Fact]
    public void TheLinesAreTheOverlays()
    {
        Assert.Equal(0.25, GrowthStages.Juvenile);
        Assert.Equal(0.50, GrowthStages.Subadult);
        Assert.Equal(0.75, GrowthStages.Adult);
        Assert.Equal(0.9995, GrowthStages.Elder);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.2499, 0)]
    [InlineData(0.25, 1)]      // a value on a line is past it
    [InlineData(0.4999, 1)]
    [InlineData(0.50, 2)]
    [InlineData(0.7499, 2)]
    [InlineData(0.75, 3)]
    [InlineData(0.9994, 3)]
    [InlineData(0.9995, 4)]    // fully grown a hair under 1, as the server reports it
    [InlineData(1.0, 4)]
    public void TheStageCountsTheLinesReached(double growth, int stage)
    {
        Assert.Equal(stage, GrowthStages.IndexOf(growth));
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.7, 4)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.PositiveInfinity, 4)]
    public void AValueTheWebsiteNeverSendsStillHasAStage(double growth, int stage)
    {
        Assert.Equal(stage, GrowthStages.IndexOf(growth));
    }

    [Fact]
    public void EveryThousandthFromZeroToOneHasTheRightStage()
    {
        // The website's three decimals, as the parser makes them.
        for (var k = 0; k <= 1000; k++)
        {
            var g = k / 1000.0;
            var expected = k < 250 ? 0 : k < 500 ? 1 : k < 750 ? 2 : k < 1000 ? 3 : 4;
            Assert.Equal(expected, GrowthStages.IndexOf(g));
        }
    }
}
