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
/// those). Read from strings: the real config.json is never touched. And the
/// games' sound's two (A82): step 3 and sound on by default, a save and a
/// load, and a step out of range read as the nearest and never written back.
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

    /// <summary>The games' sound (A82): a file without its two keys reads step 3, sound on; never seeded, no migration.</summary>
    [Fact]
    public void AFileWithoutTheSoundKeysReadsStepThreeSoundOn()
    {
        var config = JsonSerializer.Deserialize<OverlayConfig>("""
            { "Hotkey": "Ctrl+F1", "UiScale": 1.2, "GamesScale": 1.5, "GrowthChimeEnabled": false, "FriendsChimeEnabled": true }
            """)!;

        Assert.Equal((3, false), (config.GamesSoundVolume, config.GamesSoundMuted));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, true)]
    [InlineData(3, true)]
    public void TheSoundKeysSurviveASaveAndALoad(int step, bool muted)
    {
        var config = new OverlayConfig { GamesSoundVolume = step, GamesSoundMuted = muted };

        var again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal((step, muted), (again.GamesSoundVolume, again.GamesSoundMuted));
    }

    /// <summary>A hand-edited 0, 9 and -2 are READ as 1, 5 and 1 by the games' sound, and stay 0, 9 and -2 in the config, through a save too (A82: never written back).</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 5)]
    [InlineData(-2, 1)]
    public void AHandEditedStepIsReadAsTheNearestAndKeptAsWritten(int stored, int read)
    {
        var config = JsonSerializer.Deserialize<OverlayConfig>($$"""{ "GamesSoundVolume": {{stored}} }""")!;
        var gate = new GamesSound(new FakeSpeaker(), config, new TestGameClock());

        Assert.Equal(read, gate.Step);
        Assert.Equal(GamesSound.GainOf(read), GamesSound.GainOf(config.GamesSoundVolume));
        Assert.Equal(stored, config.GamesSoundVolume);
        Assert.Equal(stored, JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!.GamesSoundVolume);
    }

    [Fact]
    public void TheGamesKeySurvivesASaveAndALoad()
    {
        var config = new OverlayConfig { HotkeyGames = "Ctrl+Shift+J" };

        var again = JsonSerializer.Deserialize<OverlayConfig>(JsonSerializer.Serialize(config))!;

        Assert.Equal("Ctrl+Shift+J", again.HotkeyGames);
    }
}
