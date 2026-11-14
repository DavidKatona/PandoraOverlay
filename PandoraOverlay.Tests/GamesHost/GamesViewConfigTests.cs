using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's two config keys (G14, A31): where it was when it last
/// closed and the game played last. Both survive a save and a load (a restart,
/// an update), a config.json from before the games reads them as "", and a
/// null from a hand-edited file reads as "" too. Read from strings: the real
/// config.json is never touched.
/// </summary>
public class GamesViewConfigTests
{
    [Fact]
    public void AFreshConfigShowsTheGridWithNothingMarked()
    {
        var config = new OverlayConfig();

        Assert.Equal("", config.GamesView);
        Assert.Equal("", config.GamesLastPlayed);
    }

    [Fact]
    public void AConfigFromBeforeTheGamesReadsThemEmpty()
    {
        var config = JsonSerializer.Deserialize<OverlayConfig>("""{ "UiScale": 1.3, "BigMapNames": false }""")!;

        Assert.Equal(("", ""), (config.GamesView, config.GamesLastPlayed));
    }

    [Fact]
    public void BothSurviveASaveAndALoad()
    {
        var config = new OverlayConfig { GamesView = "hatchling", GamesLastPlayed = "hatchling" };

        var again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal(("hatchling", "hatchling"), (again.GamesView, again.GamesLastPlayed));
        config.GamesView = "";
        again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;
        Assert.Equal(("", "hatchling"), (again.GamesView, again.GamesLastPlayed));
    }

    [Fact]
    public void ANullFromAHandEditedFileReadsEmpty()
    {
        var config = JsonSerializer.Deserialize<OverlayConfig>("""{ "GamesView": null, "GamesLastPlayed": null }""")!;

        Assert.Equal(("", ""), (config.GamesView, config.GamesLastPlayed));
    }
}
