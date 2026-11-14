using System.Windows.Interop;

namespace PandoraOverlay;

/// <summary>
/// MainWindow, Games part (G11, A13): opening and closing the Games window,
/// its rules against the overlay's other states, the feed from each reading
/// of the player, the health warning, and the saves on the way out. The
/// map's part (MainWindow.BigMap.cs) is its model; this is its sibling.
/// <para>
/// <b>What opens it.</b> Its hotkey (Ctrl+J by default, G12, A14), and only
/// that: no tray line, no Settings page, no control-panel button. Opening
/// locks edit mode first (lock and persist, as the map does) and closes the
/// map. It works while the overlay is hidden and leaves the widgets hidden
/// (G16), as the map's hotkey does. Under a modal dialog of ours it does
/// nothing (A57): Settings suspends the hotkeys itself, and the tray's sign-in
/// window, which leaves them registered, is answered the same way.
/// </para>
/// <para>
/// <b>What closes it.</b> Its hotkey again, ✕, and Esc or a bare J on their
/// release (the window's own keys, GamesKeys); a click on the game (G17:
/// OverlayFocus.Left); hide-all hiding (G16); edit mode, Settings, Server
/// rules, the tray's sign-in and the map, each of which closes it first
/// (one of them on screen at a time, A13); and the overlay ending. Each of
/// those places asks <see cref="GamesGate"/>, the table, through
/// <see cref="CloseGamesFor"/>. Nothing is lost by a close: a game's state is
/// whole at every moment (A20), the window flushes every pending save as it
/// goes, and reopening shows where you left off (G14: the config's
/// GamesView, which the window keeps).
/// </para>
/// <para>
/// <b>Why the widgets stay.</b> Unlike the map, Games hides nothing (G15):
/// the stats panel shows your real health, which is the whole point of
/// playing while a dino grows. So closing it has nothing to show again, and
/// what shows a widget while it is open (hide-all showing, a spawn after the
/// auto-hide, and Check Prime making the Prime widget that was switched off,
/// A58) puts the Games window back on top of them (<see cref="RaiseGames"/>):
/// the widgets sit beside Games, never over it. The auto-hide and the mid-game
/// keys (Check Prime, the views, the heatmap, the skin) leave it open.
/// </para>
/// <para>
/// <b>Why nothing here asks the website.</b> The games add no request and no
/// request trigger, and opening Games asks for nothing (constraint 2; A5,
/// A13). The host is FED: the snapshot handler's last line hands it each
/// successful reading (<see cref="FeedGames"/>), and a failed one feeds
/// nothing, so a game sees a gap as a gap. The same reading drives the
/// health warning (G18, A15: <see cref="HealthWatch"/>, fed whether or not
/// the window is open) and the watch note (A16: what the overlay can't see).
/// A source scan holds this file and GamesHost/ to that: no word of the
/// request side appears in either.
/// </para>
/// <para>
/// <b>The last net (A10).</b> The host is made in the constructor with no IO
/// (tests and the render harness build this window unshown and must never
/// read or write a real save) and handed to <see cref="GameFaults"/>, which
/// App's crash handler asks first; the games are attached on Loaded.
/// </para>
/// Same class as MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MainWindow
{
    /// <summary>The listed games, attached, fed and saved; made in the constructor (no IO), attached on Loaded.</summary>
    private readonly GamesHost _games;

    /// <summary>The window, while it is open: made at each opening, destroyed at each close (A11).</summary>
    private GamesWindow? _gamesWindow;

    /// <summary>When the window warns that your health is dropping (G18, A15): fed every reading, window open or not, so a window opened mid-trouble warns at once.</summary>
    private readonly HealthWatch _healthWatch = new();

    /// <summary>What the window's watch note says (A16), kept so a window opened later starts with it.</summary>
    private GamesWatch _gamesWatch = GamesWatch.NotInGame;

    /// <summary>The Games hotkey: open, or close.</summary>
    private void ToggleGames()
    {
        if (!CloseGames()) OpenGames();
    }

    private void OpenGames()
    {
        // One at a time (A13, A57): a modal dialog of ours is up. Settings suspends
        // the hotkeys itself; the tray's sign-in window leaves them registered, so
        // the key arrives here and does nothing, as it does while Settings is open.
        // WPF's own flag for our own thread: true inside every ShowDialog.
        if (ComponentDispatcher.IsThreadModal)
        {
            AppLog.Games.Log("Games not opened: a dialog of the overlay is open");
            return;
        }
        if (EditMode) ToggleEditMode(); // lock and persist first, as the map does
        CloseBigMap(); // one of the two at a time (A13)

        // The monitor the player arranged their HUD on: the minimap's, else the
        // stats panel's, exactly as the map picks its monitor (A11).
        var monitor = ((OverlayWindowBase?)_minimap ?? this).ScreenBounds;
        var window = new GamesWindow(_games, _config, monitor);
        _gamesWindow = window;
        window.Closed += (_, _) => OnGamesClosed();
        window.WarningAcknowledged += _healthWatch.Acknowledge; // "Keep playing" or "Back to Games"
        window.SetWatch(_gamesWatch);
        window.Open(); // activated: the hotkey press grants the right, and only our own window is touched (A12)
        if (_healthWatch.TroubledAt(DateTime.UtcNow)) window.ShowHealthWarning(_healthWatch.HealthPercent); // opened mid-trouble: at once (A15)
        AppLog.Games.Log("Games opened");
        // Nothing else: the widgets stay as they are (G15; hidden if the overlay
        // is), and nothing is asked of the website (A13).
    }

    /// <summary>Closes the Games window if it is open; true when it was.</summary>
    private bool CloseGames()
    {
        if (_gamesWindow is not { } window) return false;
        window.Close(); // Closed → OnGamesClosed
        return true;
    }

    /// <summary>The table's answer for this event (<see cref="GamesGate"/>): the window closed when the event closes it; true when it was open and is closed now.</summary>
    private bool CloseGamesFor(OverlayEvent what) => GamesGate.Closes(what) && CloseGames();

    /// <summary>
    /// The window is gone, whatever closed it: its view was disposed first
    /// (its own Closed handler runs before this one), so what the game handed
    /// over on the way out is pending, and is written now. Closing
    /// acknowledges a warning (A15). Nothing to show again: Games hid nothing.
    /// </summary>
    private void OnGamesClosed()
    {
        _gamesWindow = null;
        _healthWatch.Acknowledge();
        _games.Flush();
        AppLog.Games.Log("Games closed");
    }

    /// <summary>
    /// Every successful reading, the snapshot handler's last line: one reading
    /// to every game (A5: in game or not, the species, the gender, the growth;
    /// nothing else), the watch note, and the health warning's rule. Cheap on
    /// purpose: it runs every poll, window open or not.
    /// </summary>
    private void FeedGames(MyLocationResponse result)
    {
        _games.Feed(result);
        var player = result.InGame ? result.Player : null;
        SetGamesWatch(player is not null ? GamesWatch.Watching : GamesWatch.NotInGame);
        var notice = _healthWatch.Update(player, DateTime.UtcNow);
        if (notice == HealthNotice.Warn && _gamesWindow is { } window) window.ShowHealthWarning(_healthWatch.HealthPercent);
    }

    /// <summary>What the overlay can see of the player now (A16): remembered for the next opening, and told to the window if it is open.</summary>
    private void SetGamesWatch(GamesWatch watch)
    {
        _gamesWatch = watch;
        _gamesWindow?.SetWatch(watch);
    }

    /// <summary>
    /// The widgets were shown again (hide-all showing, a spawn after the
    /// auto-hide), or Check Prime made the Prime widget that was switched off
    /// (A58), with Games open: a topmost window shown goes to the top of the
    /// topmost band, over Games, so Games is put back on top of them.
    /// Our own window only, and no focus change: a Topmost flip moves a window
    /// in the z-order without activating it.
    /// </summary>
    private void RaiseGames()
    {
        if (_gamesWindow is not { } window) return;
        window.Topmost = false;
        window.Topmost = true;
    }
}
