namespace PandoraOverlay;

/// <summary>
/// MainWindow, Pack part (Nov 2026): the Pack widget's lifetime. The Pack is
/// shown while it is switched on (<see cref="OverlayConfig.PackEnabled"/>,
/// OFF by default: only the control panel's PACK Show/hide turns it on, the
/// owner's call of Nov 10 2026), and, when it shows only the friends you tick
/// (<see cref="OverlayConfig.PackShows"/> "ticked"), while at least one is
/// ticked: with none ticked there is no window at all, in edit mode too, and
/// the PACK button is greyed. In that mode, with the Pack switched on, the
/// first Watch tick (Settings → Friends, a map menu) brings it up and the last
/// one taken off closes it; a tick never switches it on. The first time it is
/// switched on it appears where the default layout puts it, under the stats
/// panel (FillDefaultPositions fills its place at start-up, on or off). The
/// window exists only while it is shown, like the other widgets'; hide-all,
/// the auto-hide and the map only hide it.
/// <para>
/// It is a friends surface (<see cref="PollService.PackShown"/>): appearing,
/// it asks for a roster through the existing hot trigger, under its usual
/// floor (one poll interval); going away it sends nothing
/// (<see cref="PollService.FriendsSurfaceHidden"/>). A Watch tick on a Pack
/// already shown sends nothing: the window just redraws from the book.
/// Same class as MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </para>
/// </summary>
public partial class MainWindow
{
    private PackWindow? _pack;

    /// <summary>The Pack has someone to show: every friend on your dino, or in the ticked mode a friend ticked.</summary>
    private bool PackHasAnyone => PackRows.HasAnyone(_config, _book.WatchedCount);

    /// <summary>The "is it shown" rule (PackRows.Wanted): switched on, with someone to show.</summary>
    private bool PackWanted => PackRows.Wanted(_config, _book.WatchedCount);

    /// <summary>The control panel's PACK Show/hide (greyed while it has nobody to show, so it is never pressed then).</summary>
    private void TogglePack()
    {
        _config.PackEnabled = _pack is null;
        UpdatePack();
    }

    /// <summary>
    /// The one place the Pack comes and goes, after a toggle, a Watch tick (the
    /// book's Changed), a change of who it shows (Settings) and at start-up;
    /// the control panel's button follows.
    /// </summary>
    private void UpdatePack()
    {
        if (_exiting || !IsLoaded) return; // start-up calls it once Loaded; nothing comes up on the way out
        _controlPanel?.SetPackAvailable(PackHasAnyone);
        if (PackWanted && _pack is null) ShowPack();
        else if (!PackWanted && _pack is not null) ClosePack();
    }

    private void ShowPack()
    {
        var pack = new PackWindow(_config, _poll, _book);
        _pack = pack;
        pack.Closed += (_, _) =>
        {
            if (ReferenceEquals(_pack, pack)) _pack = null;
        };
        _poll.PackShown = true;
        // Hidden with the overlay (hide-all, the auto-hide) or under the map: it
        // shows when they let the widgets back (ShowWindows).
        if (!_overlayHidden && !_autoHidden && _bigMap is null) pack.Show();
        AppLog.App.Log("Pack widget shown");
        _ = _poll.RefreshFriendsAsync("Pack widget shown"); // the widget wants a roster
        if (EditMode) pack.SetEditMode(true);
    }

    private void ClosePack()
    {
        if (_pack is not { } pack) return;
        if (!double.IsNaN(pack.Left) && !double.IsNaN(pack.Top)) (_config.PackX, _config.PackY) = (pack.Left, pack.Top); // a hidden widget comes back where it was
        _pack = null;
        pack.Close();
        _poll.PackShown = false;
        AppLog.App.Log("Pack widget hidden");
        _poll.FriendsSurfaceHidden("Pack widget hidden"); // stops the friends fetch unless another surface still shows them
    }
}
