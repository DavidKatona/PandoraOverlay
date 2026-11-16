using System.Windows.Input;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The late-added hotkeys' fallback lists (MainWindow.Hotkeys.cs) and every
/// default: the rules their comments state, held here. The game sees the
/// bare F-key even with Ctrl held, so a key The Isle or Steam uses would do
/// two things at once (F11 switched the game to fullscreen, Oct 24 2026).
/// </summary>
public class HotkeyCandidateTests
{
    /// <summary>F2 records, F10 hides the HUD, F11 switches windowed / fullscreen, F12 is Steam's screenshot.</summary>
    private static readonly Key[] GameKeys = { Key.F2, Key.F10, Key.F11, Key.F12 };

    /// <summary>Each list with how many hotkeys come before it and may already hold one of its combos.</summary>
    private static readonly (string Name, HotkeySpec[] List, int Takers)[] Lists =
    {
        ("heatmap", MainWindow.HeatmapHotkeyCandidates, 3),
        ("Check Prime", MainWindow.PrimeHotkeyCandidates, 4),
        ("stats view", MainWindow.StatsViewHotkeyCandidates, 5),
        ("the map", MainWindow.BigMapHotkeyCandidates, 6),
        ("apply my skin", MainWindow.SkinHotkeyCandidates, 7),
        ("the Games window", MainWindow.GamesHotkeyCandidates, 8),
    };

    private static IEnumerable<HotkeySpec> Defaults()
    {
        var c = new OverlayConfig();
        return new[] { c.Hotkey, c.HotkeyHideAll, c.HotkeyMinimapView, c.HotkeyHeatmap, c.HotkeyPrimeCheck, c.HotkeyStatsView, c.HotkeyBigMap, c.HotkeySkin, c.HotkeyGames }
            .Select(s => HotkeySpec.TryParse(s) ?? throw new InvalidOperationException($"default {s} doesn't parse"));
    }

    private static bool Allowed(HotkeySpec spec) =>
        spec.Modifiers != ModifierKeys.None
        && !GameKeys.Contains(spec.Key)
        && spec != new HotkeySpec(ModifierKeys.Control, Key.F3); // held globally by third-party software in the wild

    [Fact]
    public void EveryListHasOneMoreCandidateThanTakersSoOneIsAlwaysFree()
    {
        foreach (var (name, list, takers) in Lists)
        {
            Assert.True(list.Distinct().Count() == takers + 1, $"{name}: {list.Distinct().Count()} distinct candidates for {takers} takers");
        }
    }

    [Fact]
    public void NoCandidateIsAKeyTheGameOrSteamUses()
    {
        foreach (var (name, list, _) in Lists)
        {
            foreach (var spec in list) Assert.True(Allowed(spec), $"{name}: {spec}");
        }
    }

    [Fact]
    public void EveryDefaultIsClearOfTheGamesKeysAndOfEachOther()
    {
        var defaults = Defaults().ToList();
        foreach (var spec in defaults) Assert.True(Allowed(spec), $"default {spec}");
        Assert.Equal(defaults.Count, defaults.Distinct().Count());
    }

    /// <summary>A late key's own default comes first in its list, so a fresh install gets exactly it.</summary>
    [Fact]
    public void EachListStartsWithItsOwnDefault()
    {
        var c = new OverlayConfig();
        Assert.Equal(HotkeySpec.TryParse(c.HotkeyHeatmap), MainWindow.HeatmapHotkeyCandidates[0]);
        Assert.Equal(HotkeySpec.TryParse(c.HotkeyPrimeCheck), MainWindow.PrimeHotkeyCandidates[0]);
        Assert.Equal(HotkeySpec.TryParse(c.HotkeyStatsView), MainWindow.StatsViewHotkeyCandidates[0]);
        Assert.Equal(HotkeySpec.TryParse(c.HotkeyBigMap), MainWindow.BigMapHotkeyCandidates[0]);
        Assert.Equal(HotkeySpec.TryParse(c.HotkeySkin), MainWindow.SkinHotkeyCandidates[0]);
        Assert.Equal(HotkeySpec.TryParse(c.HotkeyGames), MainWindow.GamesHotkeyCandidates[0]);
    }

    /// <summary>The 1.38 layout (the owner's, Oct 24 2026): the F-row's blocks, the overlay, the widgets in the control panel's order, the website; M the map; J the games (G12).</summary>
    [Fact]
    public void AFreshInstallGetsTheBlockLayout()
    {
        var c = new OverlayConfig();
        Assert.Equal("Ctrl+F1", c.Hotkey);
        Assert.Equal("Ctrl+F4", c.HotkeyHideAll);
        Assert.Equal("Ctrl+F5", c.HotkeyStatsView);
        Assert.Equal("Ctrl+F6", c.HotkeyMinimapView);
        Assert.Equal("Ctrl+F7", c.HotkeyHeatmap);
        Assert.Equal("Ctrl+F8", c.HotkeyPrimeCheck);
        Assert.Equal("Ctrl+F9", c.HotkeySkin);
        Assert.Equal("Ctrl+M", c.HotkeyBigMap);
        Assert.Equal("Ctrl+J", c.HotkeyGames);
    }

    /// <summary>
    /// An update keeps a player's keys: a config from 1.37 holds the old layout
    /// (and no skin key, which arrives with its new default Ctrl+F9, the old
    /// stats view's). The skin key then takes the next free candidate, Ctrl+F1,
    /// and every other key stays where the player had it.
    /// </summary>
    [Fact]
    public void AnUpdateFrom137KeepsItsKeysAndPutsTheSkinKeyOnCtrlF1()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<OverlayConfig>("""
            { "Hotkey": "Ctrl+F7", "HotkeyHideAll": "Ctrl+F4", "HotkeyMinimapView": "Ctrl+F5", "HotkeyHeatmap": "Ctrl+F6",
              "HotkeyPrimeCheck": "Ctrl+F8", "HotkeyStatsView": "Ctrl+F9", "HotkeyBigMap": "Ctrl+M" }
            """)!;
        Assert.Equal("Ctrl+F7", old.Hotkey);
        Assert.Equal("Ctrl+F9", old.HotkeyStatsView);

        var taken = new[] { old.Hotkey, old.HotkeyHideAll, old.HotkeyMinimapView, old.HotkeyHeatmap, old.HotkeyPrimeCheck, old.HotkeyStatsView, old.HotkeyBigMap }
            .Select(s => HotkeySpec.TryParse(s)!).ToArray();
        string? written = null;
        var skin = MainWindow.ResolveLateHotkey(old.HotkeySkin, MainWindow.SkinHotkeyCandidates, taken, v => written = v);

        Assert.Equal(new HotkeySpec(ModifierKeys.Control, Key.F1), skin);
        Assert.Equal("Ctrl+F1", written); // remembered, so it stays put
    }

    /// <summary>The eight keys before the Games key, in MainWindow's constructor's order: what may already hold its combo.</summary>
    private static HotkeySpec[] TakersOfTheGamesKey(OverlayConfig c) =>
        new[] { c.Hotkey, c.HotkeyHideAll, c.HotkeyMinimapView, c.HotkeyHeatmap, c.HotkeyPrimeCheck, c.HotkeyStatsView, c.HotkeyBigMap, c.HotkeySkin }
            .Select(s => HotkeySpec.TryParse(s) ?? throw new InvalidOperationException($"{s} doesn't parse")).ToArray();

    /// <summary>
    /// A player who already gave Ctrl+J to the map: the Games key, arriving
    /// with its default on the same combo, takes the next candidate,
    /// Ctrl+Shift+J, and remembers it, so it stays put; the map keeps Ctrl+J.
    /// </summary>
    [Fact]
    public void AMapKeyOnCtrlJPutsTheGamesKeyOnCtrlShiftJ()
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<OverlayConfig>("""
            { "Hotkey": "Ctrl+F1", "HotkeyHideAll": "Ctrl+F4", "HotkeyStatsView": "Ctrl+F5", "HotkeyMinimapView": "Ctrl+F6",
              "HotkeyHeatmap": "Ctrl+F7", "HotkeyPrimeCheck": "Ctrl+F8", "HotkeySkin": "Ctrl+F9", "HotkeyBigMap": "Ctrl+J" }
            """)!;
        Assert.Equal("Ctrl+J", config.HotkeyGames); // the new key's default, from a file that never held it

        var games = MainWindow.ResolveLateHotkey(config.HotkeyGames, MainWindow.GamesHotkeyCandidates, TakersOfTheGamesKey(config), v => config.HotkeyGames = v);

        Assert.Equal(new HotkeySpec(ModifierKeys.Control | ModifierKeys.Shift, Key.J), games);
        Assert.Equal("Ctrl+Shift+J", config.HotkeyGames); // written back, so it stays put
        Assert.Equal("Ctrl+J", config.HotkeyBigMap);
    }

    /// <summary>
    /// An update keeps a player's keys (1.38): a config.json from 1.44 holds
    /// eight keys and no Games key. Resolved as MainWindow's constructor does,
    /// every late key in its order, none of the eight moves and nothing is
    /// written back; the Games key gets its default, Ctrl+J, free in both
    /// layouts a 1.44 player can have: the 1.38 default one, and the old one
    /// an update from 1.37 kept (the skin key then on Ctrl+F1).
    /// </summary>
    [Theory]
    [InlineData("""
        { "Hotkey": "Ctrl+F1", "HotkeyHideAll": "Ctrl+F4", "HotkeyStatsView": "Ctrl+F5", "HotkeyMinimapView": "Ctrl+F6",
          "HotkeyHeatmap": "Ctrl+F7", "HotkeyPrimeCheck": "Ctrl+F8", "HotkeySkin": "Ctrl+F9", "HotkeyBigMap": "Ctrl+M", "UiScale": 1.1 }
        """)]
    [InlineData("""
        { "Hotkey": "Ctrl+F7", "HotkeyHideAll": "Ctrl+F4", "HotkeyMinimapView": "Ctrl+F5", "HotkeyHeatmap": "Ctrl+F6",
          "HotkeyPrimeCheck": "Ctrl+F8", "HotkeyStatsView": "Ctrl+F9", "HotkeyBigMap": "Ctrl+M", "HotkeySkin": "Ctrl+F1", "UiScale": 1.1 }
        """)]
    public void AConfigFrom144KeepsAllEightOfItsKeys(string json)
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<OverlayConfig>(json)!;
        var before = new[] { config.Hotkey, config.HotkeyHideAll, config.HotkeyMinimapView, config.HotkeyHeatmap, config.HotkeyPrimeCheck, config.HotkeyStatsView, config.HotkeyBigMap, config.HotkeySkin };
        var written = new List<string>();

        // MainWindow's constructor, in its order: the three first keys parsed, each late key resolved against those before it.
        var edit = HotkeySpec.ParseOr(config.Hotkey, OverlayConfig.DefaultHotkey);
        var hide = HotkeySpec.ParseOr(config.HotkeyHideAll, OverlayConfig.DefaultHotkeyHideAll);
        var view = HotkeySpec.ParseOr(config.HotkeyMinimapView, OverlayConfig.DefaultHotkeyMinimapView);
        var heatmap = MainWindow.ResolveLateHotkey(config.HotkeyHeatmap, MainWindow.HeatmapHotkeyCandidates, new[] { edit, hide, view }, written.Add);
        var prime = MainWindow.ResolveLateHotkey(config.HotkeyPrimeCheck, MainWindow.PrimeHotkeyCandidates, new[] { edit, hide, view, heatmap }, written.Add);
        var stats = MainWindow.ResolveLateHotkey(config.HotkeyStatsView, MainWindow.StatsViewHotkeyCandidates, new[] { edit, hide, view, heatmap, prime }, written.Add);
        var map = MainWindow.ResolveLateHotkey(config.HotkeyBigMap, MainWindow.BigMapHotkeyCandidates, new[] { edit, hide, view, heatmap, prime, stats }, written.Add);
        var skin = MainWindow.ResolveLateHotkey(config.HotkeySkin, MainWindow.SkinHotkeyCandidates, new[] { edit, hide, view, heatmap, prime, stats, map }, written.Add);
        var games = MainWindow.ResolveLateHotkey(config.HotkeyGames, MainWindow.GamesHotkeyCandidates, new[] { edit, hide, view, heatmap, prime, stats, map, skin }, written.Add);

        Assert.Empty(written); // nothing moved, nothing to remember
        Assert.Equal(before, new[] { edit, hide, view, heatmap, prime, stats, map, skin }.Select(k => k.ToString()));
        Assert.Equal(before, new[] { config.Hotkey, config.HotkeyHideAll, config.HotkeyMinimapView, config.HotkeyHeatmap, config.HotkeyPrimeCheck, config.HotkeyStatsView, config.HotkeyBigMap, config.HotkeySkin });
        Assert.Equal(new HotkeySpec(ModifierKeys.Control, Key.J), games);
        Assert.Equal("Ctrl+J", config.HotkeyGames);
    }
}
