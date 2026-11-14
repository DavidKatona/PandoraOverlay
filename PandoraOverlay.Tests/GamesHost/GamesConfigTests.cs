using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The Games window's three config keys together: its hotkey (G12, A14) and
/// where it left off (G14, A31). A config.json from before the games (1.44
/// and older) holds none of them, and reads the hotkey's default and the
/// grid with nothing marked; the hotkey survives a save and a load like the
/// other two (GamesViewConfigTests holds those). Read from strings: the real
/// config.json is never touched.
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
    }

    [Fact]
    public void TheGamesKeySurvivesASaveAndALoad()
    {
        var config = new OverlayConfig { HotkeyGames = "Ctrl+Shift+J" };

        var again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal("Ctrl+Shift+J", again.HotkeyGames);
    }
}
