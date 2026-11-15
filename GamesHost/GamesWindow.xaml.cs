using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The Games window (G11, A11): one fixed frame (<see cref="GamesFrame"/>),
/// two views in it (G13): the grid of games, the home screen (G1, G19), and a
/// game inside the same frame, with "‹ Games" back to the grid. Opened by the
/// Games hotkey (MainWindow's), closed by its hotkey again, ✕, Esc or a bare J
/// on their release (G12, <see cref="GamesKeys"/>) and a click on the game
/// (G17: <see cref="OverlayFocus"/>, MainWindow closes it on
/// <c>OverlayFocus.Left</c>; the window never closes itself on Deactivated,
/// A12, unlike the map, so a tray click, which is ours, keeps it).
/// <para>
/// <b>Made per opening, closed for good.</b> It holds no state of its own
/// between openings: where the player was is in the config (A31,
/// <c>GamesView</c>: the game on screen, "" for the grid, always what the
/// window shows; <c>GamesLastPlayed</c>: the tile marked), and each game's
/// progress is in its own save. So reopening shows where you left off (G14),
/// mid-run included: a game's view is a fresh view of a state that is whole
/// at every moment (A20). Closing disposes the view and leaves
/// <c>GamesView</c> as it is: that is "where you left off".
/// </para>
/// <para>
/// <b>Every call into a game is guarded</b> (A10): the host's own calls
/// (a tile, its art, a view: <see cref="HostedGame"/>) and the window's on a
/// view (<c>Root</c>, <c>Pause</c>, <c>Resume</c>, <c>Dispose</c>) go through
/// <see cref="GameGuard"/>, so a game that throws is stopped and the window,
/// the overlay with it, goes on. The host's two events are the app's own code
/// here and are NOT caught by the host (A50): their handlers do little and
/// guard every call they make into a game.
/// </para>
/// <para>
/// <b>The health warning is the window's</b> (G18, A15): MainWindow decides
/// when (<c>HealthWatch</c>), the window covers the view area with it and
/// pauses the view. The watch note says when the overlay can't see the
/// player's health at all (A16). Neither knows how the overlay reads the
/// player: the window is handed states, never a reading.
/// </para>
/// <para>
/// <b>The games' sound</b> (G26, A79, A82): the window tells the host's sound
/// gate what it shows at every change (<c>TellStage</c>), so a game is heard
/// only while it is on screen and not covered, and its header carries the
/// games' mute and volume, the same on the grid and in a game. The rules are
/// the gate's (<see cref="GamesSound"/>); the window clicks and shows.
/// </para>
/// <para>
/// Not an OverlayWindowBase (it is no click-through widget): a plain opaque
/// Window like the map, topmost, a tool window (no Alt-Tab entry), centred on
/// the HUD's monitor at its drawn size, never remembered (G15). The whole
/// window is drawn at one layout scale (<see cref="GamesFrame.FitScale"/>):
/// the player's GamesScale (A75), less on a screen too small for it. The grid
/// and its tiles:
/// GamesWindow.Tiles.cs.
/// </para>
/// </summary>
internal partial class GamesWindow : Window
{
    private readonly GamesHost _host;
    private readonly OverlayConfig _config;
    private readonly GamesKeys _keys = new();

    /// <summary>The view on screen and the game it belongs to; both null on the grid.</summary>
    private IGameView? _view;
    private HostedGame? _onScreen;

    /// <summary>The health warning covers the view area (and the view, if any, is paused).</summary>
    private bool _warningUp;

    public GamesWindow(GamesHost host, OverlayConfig config, Rect monitor)
    {
        Theme.EnsureLoaded(); // the theme's resources and the shared control templates, before the XAML reads them (a no-op in the overlay)
        InitializeComponent();
        _host = host;
        _config = config;

        // Drawn at the player's scale (GamesScale, A75), smaller on a screen
        // too small for it, never cut off (A11): one transform over the fixed
        // layout, read once. Settings closes the window (A13), so the scale
        // can't change while it is open; the next opening reads the new one.
        // Centred on the HUD's monitor at the size it is drawn, on whole units.
        var scale = GamesFrame.FitScale(monitor.Size, _config.GamesScale);
        var drawn = scale > 0 ? scale : 1;
        if (drawn != 1) RootPanel.LayoutTransform = new ScaleTransform(drawn, drawn);
        Left = Math.Round(monitor.Left + (monitor.Width - GamesFrame.Width * drawn) / 2);
        Top = Math.Round(monitor.Top + (monitor.Height - GamesFrame.Height * drawn) / 2);

        OverlayFocus.Watch(this); // a click on the game: MainWindow closes the window (A12, G17)

        BuildPips();
        ShowSound();

        _host.TileChanged += OnTileChanged;
        _host.ConditionChanged += OnConditionChanged;
        Closed += (_, _) =>
        {
            _host.TileChanged -= OnTileChanged;
            _host.ConditionChanged -= OnConditionChanged;
            CloseView(); // GamesView is kept: where you left off (G14)
            _host.Sound.Stage(null, paused: false); // the window closed: nobody is heard, every sound stops (A79)
            LetGoOfArt(); // a game's kept art element is free for the next window's tile
        };

        // Where you left off (G14, A31): the game the config names, if it is
        // listed and can play; else the grid.
        var wanted = _host.Games.FirstOrDefault(g => g.Id == _config.GamesView && g.Plays);
        if (wanted is null || !TryShowGame(wanted)) ShowGrid();
    }

    /// <summary>Shows the window and takes focus: the hotkey press grants the right, the game releases the cursor (A12). Only our own window is activated.</summary>
    public void Open()
    {
        Show();
        Activate();
    }

    /// <summary>The game shown, null on the grid.</summary>
    internal HostedGame? OnScreen => _onScreen;

    /// <summary>The view on screen, null on the grid: for the layout tests and the render tool, which walk a game's <c>LayoutStates</c> on it (the window itself never calls those).</summary>
    internal IGameView? ViewOnScreen => _view;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW)); // no Alt-Tab entry
        // Windows 11 rounds the corners where the theme says so, exactly as the
        // map (BigMapWindow): read once, since a theme switch can't happen while
        // the window is open (Settings closes it first, A13). Windows 10 doesn't
        // know the attribute and keeps every corner square.
        var corners = Theme.Current.Map.WindowRounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
    }

    // ---- The two views (G13) ------------------------------------------------------

    /// <summary>The home screen: every listed game's tile, rebuilt from what the games say now. GamesView becomes "": the grid is what is on screen.</summary>
    internal void ShowGrid()
    {
        CloseView();
        TellStage(); // the grid: no game is heard (A79); a fault's close comes here too (OnConditionChanged)
        _config.GamesView = "";
        GameHost.Visibility = Visibility.Collapsed;
        GridScroll.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        TitleText.Text = GamesText.GridTitle;
        BuildTiles();
        ShowHeaderState();
    }

    /// <summary>A game in the frame (G13), when it can play; a game that can't (or that faults on the way) leaves the grid on screen.</summary>
    internal void ShowGame(HostedGame game)
    {
        if (!TryShowGame(game) && _onScreen is null) ShowGrid();
    }

    /// <summary>
    /// The game's fresh view, its Root filling the view area. False when there
    /// is none: the game can't play, or it faulted making the view or its Root
    /// (the guard has stopped it, and ConditionChanged rebuilt its tile), or
    /// it gave no Root at all (a broken view: stopped the same way).
    /// </summary>
    private bool TryShowGame(HostedGame game)
    {
        if (!game.Plays) return false;
        var view = game.CreateView();
        if (view is null) return false;
        var root = GameGuard.Run(game, "Root", () => view.Root);
        if (root is null)
        {
            if (game.Condition == GameCondition.Ready) _host.Stop(game, "Root (its view gave none)", null);
            GameGuard.Run(game, "Dispose", view.Dispose);
            return false;
        }
        // A game can report a fault of its own while it makes its view (through
        // its host, with no throw) and still hand the view back: it is stopped,
        // so nothing of it goes on screen.
        if (!game.Plays)
        {
            GameGuard.Run(game, "Dispose", view.Dispose);
            return false;
        }
        CloseView(); // a view replaced by another: the old one disposed first
        // Placed through the guard too: a root that is already some other
        // element's child is the game's mistake, and WPF throws for it here.
        if (!GameGuard.Run(game, "Root", () => { GameHost.Child = root; }))
        {
            GameHost.Child = null;
            GameGuard.Run(game, "Dispose", view.Dispose);
            return false;
        }
        _view = view;
        _onScreen = game;
        Disarm();
        _config.GamesView = game.Id;
        _config.GamesLastPlayed = game.Id;
        GridScroll.Visibility = Visibility.Collapsed;
        GameHost.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        TitleText.Text = game.Title;
        if (_warningUp) GameGuard.Run(game, "Pause", view.Pause); // opened under a warning: the view starts paused
        TellStage(); // this game on screen, paused if the warning is up (A79); after a Pause that threw, the grid
        ShowHeaderState();
        return _onScreen == game; // a Pause that threw has already sent the window back to the grid
    }

    /// <summary>
    /// What the window shows, told to the games' sound gate at every change
    /// (A79): the game on screen, or none, and whether the warning covers it.
    /// A game shown (<see cref="TryShowGame"/>), the grid (<see cref="ShowGrid"/>,
    /// which a fault's close goes through: <see cref="OnConditionChanged"/>), the
    /// warning up, "Keep playing"; the window's Closed tells it none. The gate
    /// plays a sound only for the instance it was told is heard, and stops every
    /// sound when the heard one changes or goes.
    /// </summary>
    private void TellStage() => _host.Sound.Stage(_onScreen, _warningUp);

    /// <summary>The view on screen, if any, taken off and disposed (guarded): its timers stop, nothing is lost (the state is whole, A20).</summary>
    private void CloseView()
    {
        var (game, view) = (_onScreen, _view);
        _onScreen = null;
        _view = null;
        GameHost.Child = null;
        if (game is not null && view is not null) GameGuard.Run(game, "Dispose", view.Dispose);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowGrid();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>The subtitle and the warning's button: what the header says depends on the view and the warning.</summary>
    private void ShowHeaderState()
    {
        SubtitleText.Text = _onScreen is null ? GamesText.GameCount(_host.Games.Count)
                          : _warningUp ? GamesText.Paused
                          : "";
        WarningButtonText.Text = _onScreen is null ? GamesText.BackToGames : GamesText.KeepPlaying;
    }

    // ---- The host's events (A50: the app's own code, so they do little and guard every call) -----

    /// <summary>A game's tile texts changed: that tile redrawn in place, if the grid shows it.</summary>
    private void OnTileChanged(HostedGame game)
    {
        if (_onScreen is null && TileOf(game) is { } tile) RedrawTile(tile);
    }

    /// <summary>
    /// A game's condition changed: the game on screen that can no longer play
    /// (it faulted) is closed and the grid shown; on the grid, that one tile is
    /// rebuilt (a stopped game's words, or a game started again).
    /// </summary>
    private void OnConditionChanged(HostedGame game)
    {
        if (game == _onScreen)
        {
            if (game.Condition != GameCondition.Ready) ShowGrid(); // which tells the sound gate: the stopped game is heard no more (A79)
            return;
        }
        if (_onScreen is null) RebuildTile(game);
    }

    // ---- The health warning (G18, A15) and the watch note (A16) -----------------------

    /// <summary>Raised when the player clicked the warning's button: MainWindow acknowledges the trouble (HealthWatch).</summary>
    public event Action? WarningAcknowledged;

    /// <summary>
    /// The warning over the view area (not the header), the view paused.
    /// Called again while it is up, it only updates the percent. It says what
    /// the overlay saw, never who or what.
    /// </summary>
    public void ShowHealthWarning(int healthPercent)
    {
        WarningHealth.Text = GamesText.WarningHealth(healthPercent);
        if (_warningUp) return;
        _warningUp = true;
        Disarm(); // the panel covers the grid: nothing stays armed under it
        WarningPanel.Visibility = Visibility.Visible;
        if (_onScreen is { } game && _view is { } view) GameGuard.Run(game, "Pause", view.Pause);
        TellStage(); // the game paused: unheard, its sounds stopped (A79)
        ShowHeaderState();
    }

    /// <summary>"Keep playing" (in a game) or "Back to Games" (on the grid): the panel gone, the view resumed, the acknowledgement raised.</summary>
    private void WarningButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_warningUp) return;
        _warningUp = false;
        WarningPanel.Visibility = Visibility.Collapsed;
        if (_onScreen is { } game && _view is { } view) GameGuard.Run(game, "Resume", view.Resume);
        TellStage(); // "Keep playing": the game heard again (A79)
        ShowHeaderState();
        WarningAcknowledged?.Invoke();
    }

    /// <summary>The watch note (A16): nothing while watching or not in game, else what the overlay can't see.</summary>
    public void SetWatch(GamesWatch watch)
    {
        var note = GamesText.WatchNote(watch);
        WatchNote.Text = note;
        WatchNote.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- The sound group (A82, A83) ------------------------------------------------------
    // The games' mute and volume, on the grid and in a game alike (a player can set
    // the level before playing). The rules are the gate's (GamesSound): the window
    // only clicks and shows. Every colour and corner a theme token (no new token,
    // A32): a lit pip is the growth bar's fill, an unlit one the slots meter's
    // track, visible on the window's glass in every theme (A89).

    /// <summary>The level pips, from the quietest; built once (the theme can't change while the window is open: Settings closes it).</summary>
    private readonly List<Border> _pips = new();

    /// <summary>
    /// Five pips, 4 wide and 2 apart, 4 to 12 high, bottom-aligned in their 12-high box
    /// (GamesFrame's constants). Each pip's corner is the theme's <c>Corner.Progress</c>
    /// held to <see cref="GamesFrame.SoundPipMaxCorner"/>, so the meter reads as rising
    /// bars, never dots, in every theme (A89; the Pack holds its bars' corner the same
    /// way). Read once, here: the window lives inside one theme, since Settings, where
    /// the theme changes, closes it (A13).
    /// </summary>
    private void BuildPips()
    {
        for (var i = 0; i < GamesFrame.SoundPips; i++)
        {
            var pip = new Border
            {
                Width = GamesFrame.SoundPipWidth,
                Height = GamesFrame.SoundPipHeight(i),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = i == 0 ? default : GamesFrame.SoundPipGapBefore,
            };
            pip.CornerRadius = new CornerRadius(Math.Min(Theme.Current.Corner.Progress.Value, GamesFrame.SoundPipMaxCorner));
            _pips.Add(pip);
            SoundPips.Children.Add(pip);
        }
    }

    /// <summary>
    /// The group as the gate stands: the speaker's glyph (on or muted), `−`
    /// disabled at step 1 and `+` at the top, the first step's worth of pips lit
    /// and the rest unlit, every pip unlit while muted. An unlit pip is the slots
    /// meter's track (<c>Theme.Storage.SlotsTrack</c>), visible on the window's
    /// glass in every theme (A89).
    /// </summary>
    private void ShowSound()
    {
        var sound = _host.Sound;
        var (step, muted) = (sound.Step, sound.Muted);
        SoundToggle.Content = muted ? GamesText.SoundOff : GamesText.SoundOn;
        SoundDown.IsEnabled = step > 1;
        SoundUp.IsEnabled = step < GamesSound.Steps;
        for (var i = 0; i < _pips.Count; i++)
            _pips[i].SetResourceReference(Border.BackgroundProperty, !muted && i < step ? "Theme.Storage.Growth" : "Theme.Storage.SlotsTrack");
    }

    /// <summary>The speaker: mutes (every sound stops) or unmutes (the preview at the step).</summary>
    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        _host.Sound.ToggleMute();
        ShowSound();
    }

    /// <summary>`−`: a step quieter, sound on, the preview at the new step.</summary>
    private void SoundDown_Click(object sender, RoutedEventArgs e)
    {
        _host.Sound.StepDown();
        ShowSound();
    }

    /// <summary>`+`: a step louder, sound on, the preview at the new step.</summary>
    private void SoundUp_Click(object sender, RoutedEventArgs e)
    {
        _host.Sound.StepUp();
        ShowSound();
    }

    // ---- Keys (G12) ---------------------------------------------------------------

    /// <summary>Esc and a bare J: remembered on their first press here, the press marked handled (GamesKeys).</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // A repeat by Windows' own flag too, not only WPF's (A53): the J of the
        // Ctrl+J that opened the window never reached WPF, so WPF would call its
        // first bare repeat a first press. The message is our own window's, the
        // one WPF raises this event for (empty in a test's raised event).
        var m = ComponentDispatcher.CurrentKeyboardMessage;
        var isRepeat = e.IsRepeat || GamesKeys.HeldBefore(m.message, m.lParam.ToInt64());
        if (_keys.Down(KeyOf(e), Keyboard.Modifiers, isRepeat)) e.Handled = true;
    }

    /// <summary>Their release closes the window, marked handled too, so the game sees neither (.claude/rules/shell.md).</summary>
    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_keys.Up(KeyOf(e))) return;
        e.Handled = true;
        Close();
    }

    /// <summary>Lost activation: a key released elsewhere was never ours to close on. The window stays (A12: MainWindow closes it on OverlayFocus.Left).</summary>
    private void Window_Deactivated(object? sender, EventArgs e) => _keys.Forget();

    /// <summary>With Alt down WPF reports the key as System; the real key is SystemKey.</summary>
    private static Key KeyOf(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    // ---- Win32 (as BigMapWindow's) ------------------------------------------------------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
