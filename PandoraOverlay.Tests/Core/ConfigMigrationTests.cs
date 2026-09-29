using Xunit;

namespace PandoraOverlay.Tests;

public class ConfigMigrationTests
{
    [Theory]
    [InlineData(230, 0.81)]   // the old default lands near its old size
    [InlineData(284, 1.0)]    // the new native map size is exactly 100%
    [InlineData(160, 0.75)]   // the old minimum clips to the slider's floor (213 px instead of 160)
    [InlineData(400, 1.41)]   // the old maximum fits under the ceiling: exactly its old size
    [InlineData(340, 1.2)]
    public void MinimapPixelSizeBecomesAScaleOfTheNewMap(double px, double expected) =>
        Assert.Equal(expected, OverlayConfig.MinimapScaleFromSize(px), 3);

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(double.NaN)]
    public void UnusableSizesFallBackToFullScale(double px) =>
        Assert.Equal(1.0, OverlayConfig.MinimapScaleFromSize(px));
}
