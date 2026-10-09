namespace PandoraOverlay;

/// <summary>
/// PollService, maps part: what the two maps ask of the request stream —
/// the heatmap picture, and the gates the big map widens (the big map plan,
/// phase 3). NO new endpoint and nothing faster: the big map is a second
/// surface for the same heatmap (at the same 60 s pace) and the same
/// friends roster (riding the same polls). A gate is a pure rule of the
/// config plus whether the big map is open, so it can be tested.
/// </summary>
public sealed partial class PollService
{
    /// <summary>A second of slack in the heatmap's minute, so the timer's own tick, re-armed at the last fetch, is never turned away as too early.</summary>
    private static readonly TimeSpan HeatmapSlack = TimeSpan.FromSeconds(1);

    /// <summary>When the last heatmap fetch began, whatever came of it.</summary>
    private DateTime _heatmapUtc = DateTime.MinValue;

    /// <summary>True while the big map is on screen (MainWindow sets it): it widens the heatmap and friends gates.</summary>
    public bool BigMapOpen { get; set; }

    /// <summary>
    /// True while the overlay is hidden, by hide-all or the not-in-game
    /// auto-hide (MainWindow sets it): the minimap's heatmap is not fetched
    /// while nobody can see it (owner, Oct 9 2026; before, about a megabyte a
    /// minute went to a hidden minimap). The friends roster is NOT narrowed by
    /// it, on purpose: the friend chime plays and the Activity log keeps its
    /// ten minutes while the overlay is hidden.
    /// </summary>
    public bool OverlayHidden { get; set; }

    /// <summary>
    /// The last heatmap picture fetched, kept so a map that turns its layer
    /// on shows it without a request (null: none yet, or the last fetch
    /// failed or the site switched the heatmap off). About a megabyte.
    /// </summary>
    public byte[]? Heatmap { get; private set; }

    /// <summary>The heatmap is fetched while a map shows it: the minimap with its layer on (and the overlay not hidden), or the open big map with its own.</summary>
    internal static bool HeatmapWantedFor(OverlayConfig c, bool bigMapOpen, bool overlayHidden) =>
        (c.HeatmapEnabled && c.MinimapEnabled && !overlayHidden) || (bigMapOpen && c.BigMapHeatmap);

    /// <summary>The friends roster is fetched while a friends surface is on screen: the Activity feed with friends' events, the minimap's arrows, or the open big map's Friends layer.</summary>
    internal static bool FriendsWantedFor(OverlayConfig c, bool bigMapOpen) =>
        (c.ActivityEnabled && c.ActivityIncludeFriends) || (c.MinimapEnabled && c.FriendsOnMinimap) || (bigMapOpen && c.BigMapFriends);

    /// <summary>Under a minute since the last fetch began (less the slack): no new fetch, the kept picture stands.</summary>
    internal static bool HeatmapFresh(DateTime fetchedUtc, DateTime nowUtc) =>
        nowUtc - fetchedUtc < TimeSpan.FromSeconds(HeatmapIntervalSeconds) - HeatmapSlack;

    /// <summary>
    /// THE heatmap rule, for every caller alike — the minute timer, a map
    /// turning its layer on, the big map opening, the minimap shown again,
    /// the heatmap hotkey, the overlay shown again: while no map shows the
    /// heatmap (a minimap hidden with the overlay shows nothing), nothing is
    /// fetched and the maps are told to hide it (the kept picture stays);
    /// within a minute of the last fetch, the kept picture is handed out
    /// again (none, after a failed fetch: the next tick retries); otherwise
    /// one fetch, and the minute starts over, so the timer's next tick
    /// comes a full minute after it. Two heatmap fetches are therefore never
    /// less than a minute apart, whatever is pressed (before, an extra fetch
    /// on a hotkey or the map opening could land seconds before the timer's
    /// own, which kept its rhythm — the request log of Oct 9 2026 showed 15
    /// and 32 s). The live-map page refetches every 10 s, also only while
    /// its heatmap layer is on.
    /// </summary>
    public async Task RefreshHeatmapAsync()
    {
        if (_heatmapBusy) return; // the fetch in flight will deliver
        if (!HeatmapWantedFor(_config, BigMapOpen, OverlayHidden))
        {
            HeatmapChanged?.Invoke(null);
            return;
        }
        var now = DateTime.UtcNow;
        if (HeatmapFresh(_heatmapUtc, now))
        {
            HeatmapChanged?.Invoke(Heatmap);
            return;
        }
        _heatmapBusy = true;
        _heatmapUtc = now;
        if (_heatmapTimer.IsEnabled)
        {
            _heatmapTimer.Stop(); // re-armed: the next tick a full minute from this fetch
            _heatmapTimer.Start();
        }
        try
        {
            Heatmap = await _client.FetchHeatmapAsync();
            HeatmapChanged?.Invoke(Heatmap);
        }
        catch
        {
            Heatmap = null;
            HeatmapChanged?.Invoke(null);
        }
        finally
        {
            _heatmapBusy = false;
        }
    }
}
