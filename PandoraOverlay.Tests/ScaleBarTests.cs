using Xunit;

namespace PandoraOverlay.Tests;

public class ScaleBarTests
{
    // The live calibration: 12.5 km of world across the map image.
    private static double PixelsPerMeter(double renderedMapPixels) => renderedMapPixels / 12_500;

    [Theory]
    [InlineData(230, 69, 2000)]       // island view, default size
    [InlineData(230 * 5, 69, 500)]    // centered, default 5× zoom
    [InlineData(230 * 1.25, 69, 2000)] // centered, min zoom
    [InlineData(400 * 6, 80, 200)]    // biggest map, max zoom, capped budget
    [InlineData(160, 48, 2000)]       // smallest map, island view
    public void PicksTheLargestRoundLengthThatFits(double rendered, double maxPixels, double expectedMeters)
    {
        var (meters, pixels) = ScaleBar.Pick(PixelsPerMeter(rendered), maxPixels);
        Assert.Equal(expectedMeters, meters);
        Assert.InRange(pixels, maxPixels * 0.2, maxPixels); // fits, and a 1-2-5 step never leaves it tiny
    }

    [Theory]
    [InlineData(0, 69)]
    [InlineData(-1, 69)]
    [InlineData(double.NaN, 69)]
    [InlineData(0.1, 0)]
    public void UnusableScaleGivesNoBar(double pixelsPerMeter, double maxPixels)
    {
        Assert.Equal((0d, 0d), ScaleBar.Pick(pixelsPerMeter, maxPixels));
    }

    [Theory]
    [InlineData(500, "500 m")]
    [InlineData(50, "50 m")]
    [InlineData(1000, "1 km")]
    [InlineData(2000, "2 km")]
    public void FormatsMetresAndKilometres(double meters, string expected)
    {
        Assert.Equal(expected, ScaleBar.Format(meters));
    }
}
