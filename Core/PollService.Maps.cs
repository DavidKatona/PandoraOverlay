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
    private DateTime _heatmapUtc = DateTime.MinValue;

    /// <summary>True while the big map is on screen (MainWindow sets it): it widens the heatmap and friends gates.</summary>
    public bool BigMapOpen { get; set; }

    /// <summary>
    /// The last heatmap picture fetched, kept so the big map can open on a
    /// fresh one without a request (null: none yet, or the last fetch failed
    /// or the site switched the heatmap off). About a megabyte.
    /// </summary>
    public byte[]? Heatmap { get; private set; }

    /// <summary>The heatmap is fetched while a map shows it: the minimap with its layer on, or the open big map with its own.</summary>
    internal static bool HeatmapWantedFor(OverlayConfig c, bool bigMapOpen) =>
        (c.HeatmapEnabled && c.MinimapEnabled) || (bigMapOpen && c.BigMapHeatmap);

    /// <summary>The friends roster is fetched while a friends surface is on screen: the Activity feed with friends' events, the minimap's arrows, or the open big map's Friends layer.</summary>
    internal static bool FriendsWantedFor(OverlayConfig c, bool bigMapOpen) =>
        (c.ActivityEnabled && c.ActivityIncludeFriends) || (c.MinimapEnabled && c.FriendsOnMinimap) || (bigMapOpen && c.BigMapFriends);

    /// <summary>A kept picture younger than the fetch pace is as good as a new one: showing it costs nothing.</summary>
    internal static bool HeatmapFresh(DateTime fetchedUtc, DateTime nowUtc) =>
        nowUtc - fetchedUtc < TimeSpan.FromSeconds(HeatmapIntervalSeconds);

    /// <summary>
    /// One heatmap fetch — the slow timer and the hot-apply paths (settings
    /// save, minimap re-show, the heatmap hotkey) all land here. Gated, so a
    /// layer nobody shows costs zero requests; raises null (hide the layer)
    /// when no map wants it, when the site switched it off, or when the fetch
    /// fails — the next tick retries. With <paramref name="reuseFresh"/> (the
    /// big map opening, or its layer switched on) a kept picture under 60 s
    /// old is handed out again instead: opening and closing the map in a
    /// hurry must not become a request per press.
    /// </summary>
    public async Task RefreshHeatmapAsync(bool reuseFresh = false)
    {
        if (_heatmapBusy) return; // the fetch in flight will deliver
        if (!HeatmapWantedFor(_config, BigMapOpen))
        {
            HeatmapChanged?.Invoke(null); // the kept picture stays, for a quick reopen
            return;
        }
        if (reuseFresh && Heatmap is { } kept && HeatmapFresh(_heatmapUtc, DateTime.UtcNow))
        {
            HeatmapChanged?.Invoke(kept);
            return;
        }
        _heatmapBusy = true;
        try
        {
            Heatmap = await _client.FetchHeatmapAsync();
            _heatmapUtc = DateTime.UtcNow;
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
