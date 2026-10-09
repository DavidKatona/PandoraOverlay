namespace PandoraOverlay;

/// <summary>
/// MainWindow, big map part (the big map plan, phase 3): opening and closing
/// the big map, and its rules against the overlay's other states.
/// Opening locks edit mode first (like hide-all), hides the widgets and
/// tells PollService, whose heatmap and friends gates widen to the map's
/// layers (the same endpoints at the same pace). While the map is open
/// nothing shows the widgets again — not a spawn after the auto-hide, not
/// an un-hide — and closing brings them back only when neither hide is in
/// force, exactly as they were. What would show a widget closes the map
/// first: the edit and Check Prime hotkeys, the tray's Settings, Server
/// rules and sign-in; hide-all closes it and leaves the screen clear. The
/// heatmap hotkey flips the big map's own layer while it is open. Also the
/// breadcrumb trail lives here now (D6), so both maps draw the same path
/// and closing the minimap no longer forgets it. Same class as
/// MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MainWindow
{
    private BigMapWindow? _bigMap;
    private MapActions? _mapActions;
    private bool _exiting; // set as this window starts closing: the map closing then must not show widgets again

    /// <summary>The recent path, fed from every snapshot before any map draws it (OnSnapshot).</summary>
    private readonly BreadcrumbTrail _trail = new();

    private TimeSpan TrailKeep => BreadcrumbTrail.KeepFor(_config.MinimapTrailMinutes);

    /// <summary>The map menu's actions, one instance for both maps, so tracking on one redraws the other.</summary>
    private MapActions MapActions => _mapActions ??= new MapActions(_config, _library, _book, MinimapWindow.Short);

    /// <summary>The big map hotkey: open, or close.</summary>
    private void ToggleBigMap()
    {
        if (!CloseBigMap()) OpenBigMap();
    }

    private void OpenBigMap()
    {
        if (EditMode) ToggleEditMode(); // lock and persist first, like hide-all

        // The monitor the player arranged their HUD on: the minimap's, else the stats panel's.
        var monitor = ((OverlayWindowBase?)_minimap ?? this).ScreenBounds;
        HideWindows();
        _poll.BigMapOpen = true;
        UpdateArea(); // the header's "you are in": the journal runs while the map is open, whatever else is off

        _bigMap = new BigMapWindow(_config, _poll, _library, _book, _trail, MapActions, monitor);
        _bigMap.Closed += (_, _) => OnBigMapClosed();
        _bigMap.SetArea(_areaJournal.Current);
        _bigMap.Open();

        _ = _poll.RefreshFriendsAsync(); // its arrows want a roster (floored at one poll interval)
        if (_config.BigMapHeatmap) _ = _poll.RefreshHeatmapAsync(reuseFresh: true); // a picture under a minute old costs nothing
    }

    /// <summary>Closes the big map if it is open; true when it was.</summary>
    private bool CloseBigMap()
    {
        if (_bigMap is null) return false;
        _bigMap.Close(); // Closed → OnBigMapClosed
        return true;
    }

    private void OnBigMapClosed()
    {
        _bigMap = null;
        _poll.BigMapOpen = false;
        if (_exiting) return; // the overlay is shutting down: a closed window can't be shown again
        if (!_overlayHidden && !_autoHidden) ShowWindows();
        UpdateArea(); // the journal's gate may have closed with the map
        // The request gates narrow by themselves: the next poll drops the
        // roster and the next heatmap tick the picture, if no widget wants them.
    }
}
