using Xunit;

namespace PandoraOverlay.Tests;

public class ConfigDefaultsTests
{
    /// <summary>
    /// Every widget's size slider starts at 100% on a fresh install, and no
    /// scale is seeded from another: a 230 px legacy minimap default once made
    /// a new install start the minimap at 81% while the rest started at 100%.
    /// </summary>
    [Fact]
    public void AFreshConfigStartsEveryWidgetAtFullScale()
    {
        var cfg = new OverlayConfig();
        Assert.Equal(1.0, cfg.UiScale);
        Assert.Equal(1.0, cfg.MinimapScale);
        Assert.Equal(1.0, cfg.PrimeScale);
        Assert.Equal(1.0, cfg.ActivityScale);
        Assert.True(cfg.UpdateCheckEnabled, "the launch update check is on until switched off");
    }

    /// <summary>A pre-1.25 file's pixel size is just an unknown key now: it does not touch the scale.</summary>
    [Fact]
    public void TheOldPixelMinimapSizeIsIgnored()
    {
        var cfg = System.Text.Json.JsonSerializer.Deserialize<OverlayConfig>("""{ "MinimapSize": 230, "UiScale": 1.3 }""");
        Assert.NotNull(cfg);
        Assert.Equal(1.0, cfg!.MinimapScale);
        Assert.Equal(1.0, cfg.PrimeScale);
        Assert.Equal(1.3, cfg.UiScale);
    }
}
