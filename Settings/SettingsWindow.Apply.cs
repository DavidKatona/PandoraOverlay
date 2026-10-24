using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>What a change in Settings asks MainWindow to re-apply (MainWindow.ApplySettingsChange). Flags: one batch may carry several.</summary>
[Flags]
public enum SettingsChange
{
    None = 0,
    /// <summary>A widget's scale, the background opacity, the fade, the time-left bars.</summary>
    Appearance = 1 << 0,
    /// <summary>The minimap's view, zoom, scale, pills, scale bar, waypoint policy or trail; the waypoint list or the tracked waypoint.</summary>
    Minimap = 1 << 1,
    /// <summary>The Activity feed's choices.</summary>
    Activity = 1 << 2,
    /// <summary>A friend's colour, nickname, map or feed choice, the tracked friend, the minimap's friend arrows.</summary>
    Friends = 1 << 3,
    /// <summary>A friends surface switched ON: the roster's existing "a surface shown" fetch, under its usual floor.</summary>
    FriendsShown = 1 << 4,
    /// <summary>A friends surface switched OFF: nothing is sent (a hidden surface never asks, since 1.36).</summary>
    FriendsHidden = 1 << 5,
    /// <summary>The stats panel's default view.</summary>
    StatsView = 1 << 6,
    /// <summary>About's Detailed log.</summary>
    Log = 1 << 7,
    /// <summary>"Hide the overlay while not in-game".</summary>
    AutoHide = 1 << 8,
    /// <summary>Reset positions, confirmed.</summary>
    Positions = 1 << 9,
}

/// <summary>
/// SettingsWindow, apply part (1.38): everything applies the moment it is
/// changed, like Windows 11's Settings: no Save, no Cancel (owner, Oct 24
/// 2026). It began with a player attacked while the dialog was open: a
/// click on the game left it on top, in the middle of the screen, with
/// every hotkey off. Now a click on the game closes it (OverlayFocus), and
/// so do Esc, ✕ and Close, and nothing is lost by it.
/// <para>
/// Each control writes its value into the shared config at once; what then
/// needs re-applying (a widget's size, the minimap) is batched for a moment
/// (ApplyBatch) so a slider drag redraws a dozen times a second, not on
/// every tick, and handed to MainWindow. config.json is written once, when
/// the dialog closes (MainWindow), as Save did. The waypoint and friend
/// lists keep their working copies (the rows edit those) and are copied
/// into the library and the friend book with each batch; a name is copied
/// when its box is left or Enter is pressed, and on closing. The dialog is
/// still modal with the hotkeys suspended, so nothing else can change the
/// lists while it is open: the working copy is always the truth.
/// </para>
/// <para>
/// What may keep it open when the focus goes (HoldsOpen): one of its own
/// windows (a file picker, the sign-in window, What's new), which the player
/// is meant to use and which keep the dialog under them; and a first run not
/// yet signed in. A link or folder handed to Windows is NOT one: the browser
/// taking the focus closes the dialog like any click away. A trial switch
/// let the owner try it the other way; they chose this (Oct 24 2026: the
/// browser opened behind the overlay, and "it felt controlled").
/// </para>
/// </summary>
public partial class SettingsWindow
{
    private static readonly TimeSpan ApplyBatch = TimeSpan.FromMilliseconds(80);

    private readonly Action<SettingsChange>? _apply;
    private DispatcherTimer? _applyTimer;
    private SettingsChange _pending;
    private int _ownWindowsOpen;

    /// <summary>True when the dialog closed because the focus left the overlay: MainWindow then locks edit mode too.</summary>
    public bool ClosedByFocus { get; private set; }

    /// <summary>
    /// A focus loss is not a click away: one of the dialog's own windows is
    /// up; or a fresh install has not signed in yet (the first run opens it
    /// to walk you through the sign-in, and there is no game to get back to
    /// without one).
    /// </summary>
    public bool HoldsOpen => _ownWindowsOpen > 0 || (_firstRun && !_signedIn);

    /// <summary>Focus left the overlay: close, everything is applied already.</summary>
    public void CloseForFocus()
    {
        ClosedByFocus = true;
        Close();
    }

    /// <summary>The tray's Settings… or Server rules… while the dialog is open: brought forward instead of a second dialog.</summary>
    public void ShowPage(string? page)
    {
        if (page is not null && !_firstRun) SetPage(page);
        Activate();
    }

    private void InitApply()
    {
        _applyTimer = new DispatcherTimer { Interval = ApplyBatch };
        _applyTimer.Tick += (_, _) => FlushChanges();
        Closing += (_, _) => FlushChanges(); // the last change, and a name still being typed
        KeyDown += (_, e) =>
        {
            // Handled = a box used Esc for itself (a hotkey box, Dino storage's rename).
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            Close();
        };
        OverlayFocus.Watch(this);
        WireLiveSettings();
    }

    /// <summary>
    /// Every preference's control, wired to write its value at once. Wired
    /// after the controls got their values, so filling them writes nothing:
    /// only what the player touches is written (a hand-edited value in
    /// config.json stays until its control is used).
    /// </summary>
    private void WireLiveSettings()
    {
        // General
        StartupCheck.Click += (_, _) => StartupRegistration.SetEnabled(StartupCheck.IsChecked == true);
        Live(HideNotInGameCheck, on => _config.HideWhenNotInGame = on, SettingsChange.AutoHide);
        Live(OpacitySlider, v => _config.BackgroundOpacity = v, SettingsChange.Appearance);
        Live(FadeCheck, on => _config.FadeEnabled = on, SettingsChange.Appearance);
        Live(FadeSlider, v => _config.FadeIdleOpacity = v, SettingsChange.Appearance);
        Live(LowStatChimeCheck, on => _config.LowStatChimeEnabled = on); // the chimes are read live by MainWindow
        Live(GrowthChimeCheck, on => _config.GrowthChimeEnabled = on);
        Live(FriendsChimeCheck, on => _config.FriendsChimeEnabled = on);

        // Widgets
        Live(ScaleSlider, v => _config.UiScale = v, SettingsChange.Appearance);
        Live(StatsViewSurvival, () => _config.StatsView = "survival", SettingsChange.StatsView);
        Live(StatsViewCombat, () => _config.StatsView = "combat", SettingsChange.StatsView);
        Live(TimeLeftCheck, on => _config.StatTimeLeftEnabled = on, SettingsChange.Appearance);
        Live(PrimeScaleSlider, v => _config.PrimeScale = v, SettingsChange.Appearance);
        Live(ActivityScaleSlider, v => _config.ActivityScale = v, SettingsChange.Appearance);
        ActivityFriendsCheck.Click += (_, _) =>
        {
            var on = ActivityFriendsCheck.IsChecked == true;
            _config.ActivityIncludeFriends = on;
            Changed(SettingsChange.Activity | (on ? SettingsChange.FriendsShown : SettingsChange.FriendsHidden));
        };
        Live(ActivityDamageCheck, on => _config.ActivityDamageLines = on); // read live by MainWindow
        Live(ActivityAreaCheck, on => _config.ActivityAreaLines = on, SettingsChange.Activity); // Activity names the area at once
        Live(ControlPanelScaleSlider, v => _config.ControlPanelScale = v, SettingsChange.Appearance);

        // Maps
        Live(MinimapScaleSlider, v => _config.MinimapScale = v, SettingsChange.Minimap);
        Live(ModeIsland, () => _config.MinimapMode = "island", SettingsChange.Minimap);
        Live(ModeCentered, () => _config.MinimapMode = "centered", SettingsChange.Minimap);
        Live(ZoomSlider, v => _config.MinimapZoom = v, SettingsChange.Minimap);
        Live(ScaleBarCheck, on => _config.MinimapScaleBarEnabled = on, SettingsChange.Minimap);
        Live(SpeedCheck, on => _config.MinimapSpeedEnabled = on, SettingsChange.Minimap);
        Live(AreaCheck, on => _config.MinimapAreaEnabled = on, SettingsChange.Minimap);
        Live(WpAll, () => _config.WaypointVisibility = "all", SettingsChange.Minimap);
        Live(WpTracked, () => _config.WaypointVisibility = "tracked", SettingsChange.Minimap);
        Live(WpNearest, () => _config.WaypointVisibility = "nearest", SettingsChange.Minimap);
        Live(TrailOff, () => _config.MinimapTrailMinutes = 0, SettingsChange.Minimap); // hidden, never forgotten (BreadcrumbTrail)
        Live(Trail10, () => _config.MinimapTrailMinutes = 10, SettingsChange.Minimap);
        Live(Trail30, () => _config.MinimapTrailMinutes = 30, SettingsChange.Minimap);
        Live(Trail60, () => _config.MinimapTrailMinutes = 60, SettingsChange.Minimap);

        // Friends (the rows: SettingsWindow.Friends.cs)
        FriendsMapCheck.Click += (_, _) =>
        {
            var on = FriendsMapCheck.IsChecked == true;
            _config.FriendsOnMinimap = on;
            Changed(SettingsChange.Friends | (on ? SettingsChange.FriendsShown : SettingsChange.FriendsHidden));
        };

        // About
        Live(UpdateCheck, on => _config.UpdateCheckEnabled = on); // read at the next launch
        Live(DetailedLogCheck, on => _config.DetailedLog = on, SettingsChange.Log);
    }

    private void Live(ToggleButton box, Action<bool> set, SettingsChange what = SettingsChange.None) =>
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            Changed(what);
        };

    /// <summary>A radio: clicked = chosen (a radio can't be clicked off).</summary>
    private void Live(RadioButton radio, Action choose, SettingsChange what) =>
        radio.Click += (_, _) =>
        {
            choose();
            Changed(what);
        };

    /// <summary>Two decimals, as Save rounded them: a drag lands on 0.8500000001 otherwise.</summary>
    private void Live(Slider slider, Action<double> set, SettingsChange what) =>
        slider.ValueChanged += (_, e) =>
        {
            set(Math.Round(e.NewValue, 2));
            Changed(what);
        };

    /// <summary>Queues a re-apply; the batch runs a moment later (or when the dialog closes).</summary>
    private void Changed(SettingsChange what)
    {
        _pending |= what;
        if (what != SettingsChange.None) QueueFlush();
    }

    /// <summary>The waypoint or friend lists changed: copied over with the next batch.</summary>
    private void QueueFlush()
    {
        if (_applyTimer is { IsEnabled: false } timer) timer.Start();
    }

    private void FlushChanges()
    {
        _applyTimer?.Stop();
        var what = _pending;
        _pending = SettingsChange.None;
        if (_waypointsDirty)
        {
            _waypointsDirty = false;
            _config.TrackedWaypointId = _draftTracked;
            // Copies: the rows go on editing the page's own. Raises Changed: the minimap redraws, MainWindow saves the file.
            _library.ReplaceWith(_draft.Select(w => w.Clone()));
            what |= SettingsChange.Minimap; // the tracked one may have changed
        }
        if (_friendsDirty)
        {
            _friendsDirty = false;
            _config.TrackedFriendSteamId = _draftTrackedFriend;
            _book.ApplyPrefs(_friendDraft); // raises Changed: the minimap redraws, MainWindow saves the file
            what |= SettingsChange.Friends;
        }
        if (what != SettingsChange.None) _apply?.Invoke(what);
    }

    /// <summary>
    /// One of the dialog's own windows (a file picker, the sign-in window,
    /// What's new): while it is up, a focus loss doesn't close the dialog,
    /// and when it closes the focus is checked again, since the player may
    /// have left from inside it.
    /// </summary>
    private T OwnWindow<T>(Func<T> show)
    {
        _ownWindowsOpen++;
        try
        {
            return show();
        }
        finally
        {
            _ownWindowsOpen--;
            OverlayFocus.Arm();
        }
    }

    /// <summary>Hands a web address or a folder to Windows: the browser or Explorer takes the focus, which closes the dialog (and locks edit mode) like any click away.</summary>
    private void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Fail soft — every page names its addresses.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
