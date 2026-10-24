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
    };

    private static IEnumerable<HotkeySpec> Defaults()
    {
        var c = new OverlayConfig();
        return new[] { c.Hotkey, c.HotkeyHideAll, c.HotkeyMinimapView, c.HotkeyHeatmap, c.HotkeyPrimeCheck, c.HotkeyStatsView, c.HotkeyBigMap, c.HotkeySkin }
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
    }

    /// <summary>The 1.38 layout (the owner's, Oct 24 2026): the F-row's blocks, the overlay, the widgets in the control panel's order, the website; M the map.</summary>
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
}
