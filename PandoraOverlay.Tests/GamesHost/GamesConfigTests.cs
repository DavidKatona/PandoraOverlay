using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's four config keys together: its hotkey (G12, A14),
/// where it left off (G14, A31) and its scale (A75). A config.json from
/// before the games (1.44 and older) holds none of them, and reads the
/// hotkey's default, the grid with nothing marked and 100%, whatever the
/// widgets' scales say (no seeding, no migration); the hotkey and the scale
/// survive a save and a load like the other two (GamesViewConfigTests holds
/// those). Read from strings: the real config.json is never touched.
/// </summary>
public class GamesConfigTests
{
    [Fact]
    public void AFileWithoutTheGamesKeysReadsTheirDefaults()
    {
        var config = JsonSerializer.Deserialize<OverlayConfig>("""
            { "Hotkey": "Ctrl+F1", "HotkeyBigMap": "Ctrl+M", "HotkeySkin": "Ctrl+F9", "UiScale": 1.2, "PackEnabled": true }
            """)!;

        Assert.Equal(("Ctrl+J", "", ""), (config.HotkeyGames, config.GamesView, config.GamesLastPlayed));
        Assert.Equal(OverlayConfig.DefaultHotkeyGames, config.HotkeyGames);
        Assert.Equal(1.0, config.GamesScale); // not the stats panel's 1.2: never seeded from another
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TheGamesScaleSurvivesASaveAndALoad(double scale)
    {
        var config = new OverlayConfig { GamesScale = scale };

        var again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal(scale, again.GamesScale);
    }

    [Fact]
    public void TheGamesKeySurvivesASaveAndALoad()
    {
        var config = new OverlayConfig { HotkeyGames = "Ctrl+Shift+J" };

        var again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal("Ctrl+Shift+J", again.HotkeyGames);
    }
}
