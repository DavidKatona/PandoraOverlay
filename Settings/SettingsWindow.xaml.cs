using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// Sectioned settings dialog: Account (the in-app sign-in, v1.31 — see
/// SettingsWindow.Account.cs), Controls (edit-mode hotkey with capture +
/// availability check), General (run at Windows startup), Minimap (view mode
/// + centered zoom, the same persisted values the in-overlay gestures write).
/// Save applies config + registry; MainWindow reads the *Changed flags
/// afterwards to hot-apply hotkey/minimap without a restart (CookieChanged is
/// read even after Cancel: a sign-in acts at once). First run: Save stays
/// disabled until signed in.
/// </summary>
/// <remarks>
/// Opening cost, measured Oct 2026 with the overlay's windows up (idle PC,
/// off-screen): ~80 ms a normal opening, ~200 ms the first per launch —
/// ~60 ms reading the dialog's BAML once, ~55 ms the first layout (control
/// templates, six framework assemblies), only ~27 ms of it JIT, so
/// ReadyToRun would barely help. A hidden warm-up (build and lay the dialog
/// out off-screen once after launch, ~180 ms) would bring the first opening
/// to ~90 ms; the owner chose NOT to build it ("we can live with this") —
/// don't re-propose it unasked.
/// </remarks>
public partial class SettingsWindow : Window
{
    private const int TestHotkeyId = 0xA11D; // distinct from MainWindow's real registration

    private static readonly Brush HintNeutral = new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush HintGood = new SolidColorBrush(Color.FromRgb(0x7C, 0xC8, 0x84));
    private static readonly Brush HintWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));
    private static readonly Brush HintBad = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
    private static readonly Brush NavSelected = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x38));
    private static readonly Brush NavSelectedText = new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xF8));
    private static readonly Brush NavText = new SolidColorBrush(Color.FromRgb(0xC7, 0xD1, 0xDA));

    /// <summary>The page shown last, so reopening the dialog lands where you were (session-only; first run forces Account).</summary>
    private static string _lastPage = "Account";

    private sealed class HotkeyEntry
    {
        public HotkeyEntry(HotkeySpec initial, string label)
        {
            Initial = initial;
            Chosen = initial;
            Label = label;
        }

        public HotkeySpec Initial { get; }
        public HotkeySpec Chosen { get; set; }
        public string Label { get; }
    }

    private const string HotkeyIdleHint = "Click a box, then press the combination you want.";

    private readonly OverlayConfig _config;
    private readonly bool _firstRun;
    private readonly bool _initialStartup;
    private readonly Dictionary<TextBox, HotkeyEntry> _hotkeyEntries = new();

    /// <summary>
    /// True once a sign-in or sign-out happened in this dialog — set AT ONCE,
    /// not on Save, so MainWindow reads it whether the dialog was saved or
    /// cancelled (a stored session = rebuild the client, none = stop polling).
    /// </summary>
    public bool CookieChanged { get; private set; }

    /// <summary>True after Save when the hotkey differs (re-registration needed).</summary>
    public bool HotkeyChanged { get; private set; }

    /// <summary>True after Save when minimap view/zoom/scale, the trail length or the scale bar differ (ApplySettings needed).</summary>
    public bool MinimapChanged { get; private set; }

    /// <summary>True after Save when a scale, the background opacity or the time-left checkbox differ (ApplyAppearance needed).</summary>
    /// <remarks>The fade row rides it too. The minimap's scale does not: it rides MinimapChanged.</remarks>
    public bool AppearanceChanged { get; private set; }

    /// <summary>True after Save when the minimap's friend layer, a friend's preferences or the tracked friend differ.</summary>
    public bool FriendsChanged { get; private set; }

    /// <summary>True after Save when the Activity widget's feed choices differ.</summary>
    public bool ActivityChanged { get; private set; }

    /// <summary>True after Save when the user pressed Reset positions (MainWindow re-places every widget; sizes untouched).</summary>
    public bool PositionsReset { get; private set; }

    private bool _resetPositionsArmed;

    /// <summary>
    /// Arms a positions reset that Save applies — nothing in this dialog acts
    /// before Save, and Cancel drops it like everything else. Positions only:
    /// scales are readability preferences with a slider each.
    /// </summary>
    private void ResetPositions_Click(object sender, RoutedEventArgs e)
    {
        _resetPositionsArmed = true;
        ResetPositionsButton.Content = "Resets on Save";
        ResetPositionsButton.IsEnabled = false;
    }

    /// <param name="poll">The shared poll service: the friends roster for the Friends page, and the Skins and Dino storage pages' only way to the network.</param>
    /// <param name="currentDino">The live dino when the dialog opened (null if not in game) — the Rules page highlights its pack limit.</param>
    /// <param name="page">A page to open on (the tray's "Server rules…" passes "Rules"); null = the page you were on last.</param>
    public SettingsWindow(OverlayConfig config, WaypointLibrary library, FriendBook book, PollService poll,
                          string? currentDino = null, string? page = null, UpdateHooks? updates = null)
    {
        InitializeComponent();
        _config = config;
        _poll = poll;
        _updates = updates;
        _currentDino = currentDino;
        _library = library;
        _draft = library.Clone();
        _draftTracked = config.TrackedWaypointId;
        _book = book;
        _roster = poll.Friends;
        _friendDraft = book.Clone();
        _draftTrackedFriend = config.TrackedFriendSteamId;
        _firstRun = string.IsNullOrWhiteSpace(config.GetCookie());

        // Account: the sign-in card, or the signed-in card (SettingsWindow.Account.cs)
        InitAccountPage();

        // Safety net for small screens: the sections scroll rather than the
        // dialog running off the bottom (SizeToContent honours MaxHeight).
        // The title and the Cancel / Save row are docked outside the page
        // ScrollViewer, so a page scrolls and the buttons are never lost.
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;

        // Controls
        // A new global hotkey needs its box here, its line in Save, and its
        // Unregister in MainWindow.OpenSettingsOn — unsuspended, it would fire
        // behind this dialog and its own combo would probe as taken.
        _hotkeyEntries[EditHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.Hotkey) ?? HotkeySpec.Default, "edit mode");
        _hotkeyEntries[HideHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyHideAll) ?? new HotkeySpec(ModifierKeys.Control, Key.F4), "hide/show overlay");
        _hotkeyEntries[ViewHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyMinimapView) ?? new HotkeySpec(ModifierKeys.Control, Key.F5), "the minimap view toggle");
        _hotkeyEntries[HeatmapHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyHeatmap) ?? new HotkeySpec(ModifierKeys.Control, Key.F6), "the heatmap toggle");
        _hotkeyEntries[PrimeHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyPrimeCheck) ?? new HotkeySpec(ModifierKeys.Control, Key.F8), "Check Prime");
        _hotkeyEntries[StatsViewHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyStatsView) ?? new HotkeySpec(ModifierKeys.Control, Key.F9), "the stats view toggle");
        _hotkeyEntries[BigMapHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyBigMap) ?? new HotkeySpec(ModifierKeys.Control, Key.M), "the map");
        foreach (var (box, entry) in _hotkeyEntries)
        {
            box.Text = entry.Chosen.ToString();
        }

        // General
        _initialStartup = StartupRegistration.IsEnabled();
        StartupCheck.IsChecked = _initialStartup;
        InitAboutPage(config); // version, the update check, the settings folder (SettingsWindow.About.cs)
        TimeLeftCheck.IsChecked = config.StatTimeLeftEnabled;
        var combatView = string.Equals(config.StatsView, "combat", StringComparison.OrdinalIgnoreCase);
        StatsViewCombat.IsChecked = combatView;
        StatsViewSurvival.IsChecked = !combatView;
        HideNotInGameCheck.IsChecked = config.HideWhenNotInGame;
        LowStatChimeCheck.IsChecked = config.LowStatChimeEnabled;
        GrowthChimeCheck.IsChecked = config.GrowthChimeEnabled;
        ScaleSlider.Value = Math.Clamp(config.UiScale, ScaleSlider.Minimum, ScaleSlider.Maximum);
        PrimeScaleSlider.Value = Math.Clamp(config.PrimeScale, PrimeScaleSlider.Minimum, PrimeScaleSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(config.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        FadeCheck.IsChecked = config.FadeEnabled;
        FadeSlider.Value = Math.Clamp(config.FadeIdleOpacity, FadeSlider.Minimum, FadeSlider.Maximum);

        // Minimap
        var centered = string.Equals(config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        ModeCentered.IsChecked = centered;
        ModeIsland.IsChecked = !centered;
        ZoomSlider.Value = Math.Clamp(config.MinimapZoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        MinimapScaleSlider.Value = Math.Clamp(config.MinimapScale, MinimapScaleSlider.Minimum, MinimapScaleSlider.Maximum);
        // A hand-edited in-between value shows as the next option up.
        var trailRadio = config.MinimapTrailMinutes switch { <= 0 => TrailOff, <= 10 => Trail10, <= 30 => Trail30, _ => Trail60 };
        trailRadio.IsChecked = true;
        ScaleBarCheck.IsChecked = config.MinimapScaleBarEnabled;
        SpeedCheck.IsChecked = config.MinimapSpeedEnabled;
        AreaCheck.IsChecked = config.MinimapAreaEnabled;
        var visibilityRadio = config.WaypointVisibility switch { "tracked" => WpTracked, "nearest" => WpNearest, _ => WpAll };
        visibilityRadio.IsChecked = true;

        // Waypoints: the rows are built when the page is first looked at (OpenWaypointsPage) —
        // a row per waypoint is five templated controls, and with the packs imported that
        // made every opening of the dialog wait for a page nobody had asked for.
        WaypointList.ScrollChanged += (_, _) => AlignWaypointHeader(); // fires when the extent/viewport changes, i.e. when the bar comes or goes
        WaypointList.SizeChanged += (_, _) => AlignWaypointHeader();

        // Activity
        ActivityScaleSlider.Value = Math.Clamp(config.ActivityScale, ActivityScaleSlider.Minimum, ActivityScaleSlider.Maximum);
        ActivityFriendsCheck.IsChecked = config.ActivityIncludeFriends;
        ActivityDamageCheck.IsChecked = config.ActivityDamageLines;
        ActivityAreaCheck.IsChecked = config.ActivityAreaLines;

        // Friends
        FriendsMapCheck.IsChecked = config.FriendsOnMinimap;
        FriendsChimeCheck.IsChecked = config.FriendsChimeEnabled;
        FriendList.ScrollChanged += (_, _) => AlignHeader(FriendHeader, FriendList, top: 14); // the rows are built on the first look (SetPage)
        FriendList.SizeChanged += (_, _) => AlignHeader(FriendHeader, FriendList, top: 14);

        // Server rules (reference only, nothing to save): the cards are built on the first look
        PrepareRulesPage();

        GateSave();
        SetPage(_firstRun ? "Account" : page ?? _lastPage);
    }

    // ---- Navigation ---------------------------------------------------------
    private void Nav_Click(object sender, RoutedEventArgs e) => SetPage((string)((Button)sender).Tag);

    /// <summary>
    /// Shows one page and highlights its nav button. Unselected pages are
    /// Hidden rather than Collapsed so the page grid keeps the tallest
    /// page's height and the dialog never jumps between pages.
    /// </summary>
    private void SetPage(string key)
    {
        _lastPage = key;
        foreach (var button in Nav.Children.OfType<Button>())
        {
            var selected = (string)button.Tag == key;
            button.Background = selected ? NavSelected : Brushes.Transparent;
            button.Foreground = selected ? NavSelectedText : NavText;
        }
        foreach (var page in Pages.Children.OfType<FrameworkElement>())
        {
            page.Visibility = (string)page.Tag == key ? Visibility.Visible : Visibility.Hidden;
        }
        // Pages whose content is built in code build it on the first look:
        // hidden pages are still laid out, so anything built up front is
        // paid for on every opening of the dialog, whichever page shows.
        // The RULE for every page (the owner reported slow openings twice,
        // Oct 1 2026): build on the first look, here; a long list goes in
        // batches — the first screenful at once, the rest per Background
        // tick, a version counter letting a newer build stop an older one
        // (AddWaypointRowBatch, the skin tiles). The one exception: what
        // sets a page's HEIGHT stays eager, since the tallest page sets the
        // dialog's (PrepareRulesPage).
        switch (key)
        {
            case "Account": OpenAccountPage(); break; // one auth/me per dialog, for the card's live facts
            case "Skins": OpenSkinsPage(); break; // talks to the site, like Dino storage
            case "Storage": OpenStoragePage(); break;
            case "Waypoints": OpenWaypointsPage(); break;
            case "Friends": OpenFriendsPage(); break;
            case "Rules": OpenRulesPage(); break;
        }
    }

    // ---- Window chrome ----------------------------------------------------
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---- Account: SettingsWindow.Account.cs (the in-app sign-in, v1.31) -----

    // ---- Controls (hotkey capture) ------------------------------------------
    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHint.Text = "Press the new combination now (Esc keeps the current one).";
        HotkeyHint.Foreground = HintNeutral;
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        box.Text = _hotkeyEntries[box].Chosen.ToString();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var entry = _hotkeyEntries[box];
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            box.Text = entry.Chosen.ToString();
            HotkeyHint.Text = HotkeyIdleHint;
            HotkeyHint.Foreground = HintNeutral;
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // modifier held — wait for the main key
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            HotkeyHint.Text = "Add a modifier (Ctrl, Alt or Shift) — a bare key would be swallowed from the game.";
            HotkeyHint.Foreground = HintWarn;
            return;
        }

        var spec = new HotkeySpec(mods, key);

        var clash = _hotkeyEntries.FirstOrDefault(kv => kv.Key != box && kv.Value.Chosen == spec);
        if (clash.Key is not null)
        {
            HotkeyHint.Text = $"{spec} is already assigned to {clash.Value.Label}.";
            HotkeyHint.Foreground = HintBad;
            return;
        }

        // MainWindow suspends its registrations while this dialog is open,
        // so our own combos probe as free like any other.
        // It re-registers from config in a finally when the dialog closes,
        // saved or cancelled.
        if (!IsHotkeyAvailable(spec))
        {
            HotkeyHint.Text = $"{spec} is taken by another app — keeping {entry.Chosen}.";
            HotkeyHint.Foreground = HintBad;
            return;
        }

        entry.Chosen = spec;
        box.Text = spec.ToString();
        HotkeyHint.Text = spec == entry.Initial
            ? "Unchanged."
            : $"{spec} is available — applies on Save.";
        HotkeyHint.Foreground = spec == entry.Initial ? HintNeutral : HintGood;
    }

    /// <summary>Briefly registers the combo on this window to see whether the OS allows it.</summary>
    private bool IsHotkeyAvailable(HotkeySpec spec)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return true; // pre-init: let MainWindow's fallback handle it
        if (!HotkeySpec.Register(hwnd, TestHotkeyId, spec)) return false;
        HotkeySpec.Unregister(hwnd, TestHotkeyId);
        return true;
    }

    // ---- General (appearance) -----------------------------------------------
    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleLabel != null) ScaleLabel.Text = $"{e.NewValue:0%}";
    }

    private void PrimeScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PrimeScaleLabel != null) PrimeScaleLabel.Text = $"{e.NewValue:0%}";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null) OpacityLabel.Text = $"{e.NewValue:0%}";
    }

    private void FadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeLabel != null) FadeLabel.Text = $"{e.NewValue:0%}";
    }

    // ---- Minimap ------------------------------------------------------------
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomLabel != null) ZoomLabel.Text = $"{e.NewValue:0.##}×";
    }

    private void MinimapScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinimapScaleLabel != null) MinimapScaleLabel.Text = $"{e.NewValue:0%}";
    }

    // ---- Save ---------------------------------------------------------------
    // No setting ever needs a restart: each one either sets a *Changed flag
    // MainWindow hot-applies after the dialog (re-register hotkeys, minimap
    // ApplySettings, ApplyAppearance, …) or is read live by MainWindow. A new
    // setting joins one of the two.
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // The session is not Save's business: signing in and out acted at once (SettingsWindow.Account.cs).

        if (_hotkeyEntries.Any(kv => kv.Value.Chosen != kv.Value.Initial))
        {
            _config.Hotkey = _hotkeyEntries[EditHotkeyBox].Chosen.ToString();
            _config.HotkeyHideAll = _hotkeyEntries[HideHotkeyBox].Chosen.ToString();
            _config.HotkeyMinimapView = _hotkeyEntries[ViewHotkeyBox].Chosen.ToString();
            _config.HotkeyHeatmap = _hotkeyEntries[HeatmapHotkeyBox].Chosen.ToString();
            _config.HotkeyPrimeCheck = _hotkeyEntries[PrimeHotkeyBox].Chosen.ToString();
            _config.HotkeyStatsView = _hotkeyEntries[StatsViewHotkeyBox].Chosen.ToString();
            _config.HotkeyBigMap = _hotkeyEntries[BigMapHotkeyBox].Chosen.ToString();
            HotkeyChanged = true;
        }

        var startup = StartupCheck.IsChecked == true;
        if (startup != _initialStartup) StartupRegistration.SetEnabled(startup);
        _config.UpdateCheckEnabled = UpdateCheck.IsChecked == true; // read at the next launch
        _config.HideWhenNotInGame = HideNotInGameCheck.IsChecked == true; // MainWindow reads these live, no flag needed
        _config.LowStatChimeEnabled = LowStatChimeCheck.IsChecked == true;
        _config.GrowthChimeEnabled = GrowthChimeCheck.IsChecked == true;
        _config.StatsView = StatsViewCombat.IsChecked == true ? "combat" : "survival"; // MainWindow reapplies the view after every save

        var scale = Math.Round(ScaleSlider.Value, 2);
        var primeScale = Math.Round(PrimeScaleSlider.Value, 2);
        var activityScale = Math.Round(ActivityScaleSlider.Value, 2);
        var bgOpacity = Math.Round(OpacitySlider.Value, 2);
        var timeLeft = TimeLeftCheck.IsChecked == true;
        var fade = FadeCheck.IsChecked == true;
        var fadeOpacity = Math.Round(FadeSlider.Value, 2);
        if (Math.Abs(scale - _config.UiScale) > 0.001 ||
            Math.Abs(primeScale - (_config.PrimeScale)) > 0.001 ||
            Math.Abs(activityScale - (_config.ActivityScale)) > 0.001 ||
            Math.Abs(bgOpacity - _config.BackgroundOpacity) > 0.001 ||
            timeLeft != _config.StatTimeLeftEnabled ||
            fade != _config.FadeEnabled ||
            Math.Abs(fadeOpacity - _config.FadeIdleOpacity) > 0.001)
        {
            _config.UiScale = scale;
            _config.PrimeScale = primeScale;
            _config.ActivityScale = activityScale;
            _config.BackgroundOpacity = bgOpacity;
            _config.StatTimeLeftEnabled = timeLeft;
            _config.FadeEnabled = fade;
            _config.FadeIdleOpacity = fadeOpacity;
            AppearanceChanged = true;
        }

        var mode = ModeCentered.IsChecked == true ? "centered" : "island";
        var zoom = Math.Round(ZoomSlider.Value, 2);
        var mapScale = Math.Round(MinimapScaleSlider.Value, 2);
        var trail = TrailOff.IsChecked == true ? 0 : Trail10.IsChecked == true ? 10 : Trail30.IsChecked == true ? 30 : 60;
        var scaleBar = ScaleBarCheck.IsChecked == true;
        var speed = SpeedCheck.IsChecked == true;
        var area = AreaCheck.IsChecked == true;
        var visibility = WpTracked.IsChecked == true ? "tracked" : WpNearest.IsChecked == true ? "nearest" : "all";
        if (mode != _config.MinimapMode ||
            Math.Abs(zoom - _config.MinimapZoom) > 0.005 ||
            Math.Abs(mapScale - _config.MinimapScale) > 0.001 ||
            trail != _config.MinimapTrailMinutes ||
            scaleBar != _config.MinimapScaleBarEnabled ||
            speed != _config.MinimapSpeedEnabled ||
            area != _config.MinimapAreaEnabled ||
            visibility != _config.WaypointVisibility)
        {
            _config.MinimapMode = mode;
            _config.MinimapZoom = zoom;
            _config.MinimapScale = mapScale;
            _config.MinimapTrailMinutes = trail;
            _config.MinimapScaleBarEnabled = scaleBar;
            _config.MinimapSpeedEnabled = speed;
            _config.MinimapAreaEnabled = area;
            _config.WaypointVisibility = visibility;
            MinimapChanged = true;
        }

        if (_waypointsDirty)
        {
            _config.TrackedWaypointId = _draftTracked;
            _library.ReplaceWith(_draft); // raises Changed: the minimap redraws, MainWindow saves the file
            MinimapChanged = true;
        }

        var includeFriends = ActivityFriendsCheck.IsChecked == true;
        _config.ActivityDamageLines = ActivityDamageCheck.IsChecked == true; // read live by MainWindow, no flag needed
        _config.ActivityAreaLines = ActivityAreaCheck.IsChecked == true;     // likewise
        _config.FriendsChimeEnabled = FriendsChimeCheck.IsChecked == true;  // likewise
        if (includeFriends != _config.ActivityIncludeFriends)
        {
            _config.ActivityIncludeFriends = includeFriends;
            ActivityChanged = true;
        }

        var friendsOnMap = FriendsMapCheck.IsChecked == true;
        if (friendsOnMap != _config.FriendsOnMinimap || _friendsDirty)
        {
            _config.FriendsOnMinimap = friendsOnMap;
            _config.TrackedFriendSteamId = _draftTrackedFriend;
            if (_friendsDirty) _book.ApplyPrefs(_friendDraft); // raises Changed: the minimap redraws, MainWindow saves the file
            FriendsChanged = true;
        }

        PositionsReset = _resetPositionsArmed;

        _config.Save();
        DialogResult = true;
    }
}
