using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// The paged settings dialog. The overlay's own pages: Account (the in-app
/// sign-in, SettingsWindow.Account.cs), General (app-wide: start-up, the
/// look, the sounds), Themes (the theme list, 1.39, SettingsWindow.Themes.cs),
/// Hotkeys (capture + availability check), Widgets (the
/// stats panel, Prime tracker, Activity feed and control panel, one group
/// each), Maps (the minimap, and the trail both maps share), Waypoints and
/// Friends (the two lists); then the website's pages, Skins, Dino storage and
/// Server rules; then About. The grouping is the owner's (Oct 22 2026, 1.37).
/// Since 1.38 every change applies at once and there is no Save or Cancel
/// (SettingsWindow.Apply.cs); a click on the game, Esc, ✕ or Close closes
/// it. MainWindow reads CookieChanged afterwards (a sign-in rebuilds the
/// client) and writes config.json once.
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

    /// <summary>
    /// A hint's or a status line's tone. The Settings hints read the text
    /// roles, not a group of their own (themes PLAN.md section 6): Neutral is
    /// Text.Dim, Good Text.Good, Warn Text.Caution, Bad Text.Danger.
    /// </summary>
    private enum Tone { Neutral, Good, Warn, Bad }

    private static string ToneKey(Tone tone) => tone switch
    {
        Tone.Good => "Theme.Text.Good",
        Tone.Warn => "Theme.Text.Caution",
        Tone.Bad => "Theme.Text.Danger",
        _ => "Theme.Text.Dim",
    };

    /// <summary>
    /// A line that stays on screen while the page lives (a page's status
    /// line, the hotkey hint, the Account card's lines) takes its tone by
    /// resource reference: a theme switch recolours it by itself, with no
    /// redraw and without forgetting what it says.
    /// </summary>
    private static void ShowTone(TextBlock line, Tone tone) => line.SetResourceReference(TextBlock.ForegroundProperty, ToneKey(tone));

    // The same tones as brushes, read from the theme at the moment a page
    // builds what it shows (a built page redraws on a theme switch: RedrawIfStale).
    private static Brush HintNeutral => Theme.Current.Text.Dim.ToBrush();
    private static Brush HintGood => Theme.Current.Text.Good.ToBrush();
    private static Brush HintWarn => Theme.Current.Text.Caution.ToBrush();
    private static Brush HintBad => Theme.Current.Text.Danger.ToBrush();

    /// <summary>The page shown last, so reopening the dialog lands where you were (session-only; first run forces Account).</summary>
    private static string _lastPage = "Account";

    private sealed class HotkeyEntry
    {
        public HotkeyEntry(string configured, string defaultText, string label)
        {
            Initial = HotkeySpec.ParseOr(configured, defaultText);
            Chosen = Initial;
            Default = HotkeySpec.ParseOr(defaultText, defaultText);
            Label = label;
        }

        public HotkeySpec Initial { get; }
        public HotkeySpec Chosen { get; set; }
        /// <summary>The 1.38 layout's key (OverlayConfig.Default…), for "Use the default keys".</summary>
        public HotkeySpec Default { get; }
        public string Label { get; }
    }

    private const string HotkeyIdleHint = "Click a box, then press the combination you want.";

    private readonly OverlayConfig _config;
    private readonly bool _firstRun;
    private readonly bool _initialStartup;
    private readonly Dictionary<TextBox, HotkeyEntry> _hotkeyEntries = new();

    /// <summary>
    /// True once a sign-in or sign-out happened in this dialog (they act at
    /// once and are saved on the spot): MainWindow then rebuilds the client
    /// from the stored session, or stops polling without one.
    /// </summary>
    public bool CookieChanged { get; private set; }

    private bool _resetPositionsArmed;

    /// <summary>
    /// Two clicks, like Delete all: it acts at once since 1.38, and moves every
    /// panel. Positions only: scales are readability preferences with a slider each.
    /// </summary>
    private void ResetPositions_Click(object sender, RoutedEventArgs e)
    {
        if (!_resetPositionsArmed)
        {
            _resetPositionsArmed = true;
            ResetPositionsButton.Content = "Really reset?";
            return;
        }
        _resetPositionsArmed = false;
        ResetPositionsButton.Content = "Reset positions";
        Changed(SettingsChange.Positions);
    }

    /// <param name="poll">The shared poll service: the friends roster for the Friends page, and the Skins and Dino storage pages' only way to the network.</param>
    /// <param name="currentDino">The live dino when the dialog opened (null if not in game) — the Rules page highlights its pack limit.</param>
    /// <param name="page">A page to open on (the tray's "Server rules…" passes "Rules"); null = the page you were on last.</param>
    /// <param name="apply">MainWindow's re-apply for a batch of changes (SettingsWindow.Apply.cs); null in the preview harnesses.</param>
    public SettingsWindow(OverlayConfig config, WaypointLibrary library, FriendBook book, PollService poll,
                          string? currentDino = null, string? page = null, UpdateHooks? updates = null,
                          Action<SettingsChange>? apply = null)
    {
        // The control templates are the app's (Themes/Controls.xaml), read
        // by StaticResource at parse time: a harness without the App gets
        // them here. In the overlay App.xaml has merged them already.
        Theme.EnsureLoaded();
        InitializeComponent();
        _config = config;
        _apply = apply;
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
        // dialog running off the bottom. On the root, not the window, since
        // the frame is fixed (SettingsFrame, 1.39): the window sizes to the
        // root, and a root's own MaxHeight wins over its Height, where the
        // window's would only cut the root's bottom off. The title and the
        // Close row are docked outside the page ScrollViewer, so a page
        // scrolls and the buttons are never lost.
        RootPanel.MaxHeight = SystemParameters.WorkArea.Height * 0.92;

        // Hotkeys
        // A new global hotkey needs its box here, its line in WriteHotkeys, and its
        // Unregister in MainWindow.OpenSettingsOn — unsuspended, it would fire
        // behind this dialog and its own combo would probe as taken.
        // In the page's order: the 1.38 layout's (OverlayConfig.DefaultHotkey).
        _hotkeyEntries[EditHotkeyBox] = new HotkeyEntry(config.Hotkey, OverlayConfig.DefaultHotkey, "edit mode");
        _hotkeyEntries[HideHotkeyBox] = new HotkeyEntry(config.HotkeyHideAll, OverlayConfig.DefaultHotkeyHideAll, "hide/show overlay");
        _hotkeyEntries[StatsViewHotkeyBox] = new HotkeyEntry(config.HotkeyStatsView, OverlayConfig.DefaultHotkeyStatsView, "the stats view toggle");
        _hotkeyEntries[ViewHotkeyBox] = new HotkeyEntry(config.HotkeyMinimapView, OverlayConfig.DefaultHotkeyMinimapView, "the minimap view toggle");
        _hotkeyEntries[HeatmapHotkeyBox] = new HotkeyEntry(config.HotkeyHeatmap, OverlayConfig.DefaultHotkeyHeatmap, "the heatmap toggle");
        _hotkeyEntries[PrimeHotkeyBox] = new HotkeyEntry(config.HotkeyPrimeCheck, OverlayConfig.DefaultHotkeyPrimeCheck, "Check Prime");
        _hotkeyEntries[SkinHotkeyBox] = new HotkeyEntry(config.HotkeySkin, OverlayConfig.DefaultHotkeySkin, "apply my skin");
        _hotkeyEntries[BigMapHotkeyBox] = new HotkeyEntry(config.HotkeyBigMap, OverlayConfig.DefaultHotkeyBigMap, "the map");
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
        ControlPanelScaleSlider.Value = Math.Clamp(config.ControlPanelScale, ControlPanelScaleSlider.Minimum, ControlPanelScaleSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(config.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        FadeCheck.IsChecked = config.FadeEnabled;
        FadeSlider.Value = Math.Clamp(config.FadeIdleOpacity, FadeSlider.Minimum, FadeSlider.Maximum);

        // Themes: one radio row per bundled theme (SettingsWindow.Themes.cs)
        InitThemesPage();

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

        // Server rules (reference only): the cards are built on the first look
        PrepareRulesPage();

        InitApply(); // after the values above: filling the controls writes nothing
        SetPage(_firstRun ? "Account" : page ?? _lastPage);
    }

    // ---- Navigation ---------------------------------------------------------
    private void Nav_Click(object sender, RoutedEventArgs e) => SetPage((string)((FrameworkElement)sender).Tag);

    /// <summary>
    /// Shows one page and checks its nav button (the NavButton template draws
    /// the selected row in the theme's look). The pages not shown are
    /// Collapsed, so each page is measured on its own and only a page that
    /// overflows the frame ever scrolls (the fit factor's plan, FIT.md section
    /// 6). Until then they were Hidden, which kept the page grid as tall as
    /// the tallest page: the dialog sized itself to its content then, and must
    /// not jump between pages. The fixed frame (<see cref="SettingsFrame"/>,
    /// 1.39) took that job over, so a Hidden page only made every page scroll
    /// once one theme's tallest did (the Nightshade attempt, Oct 29 2026).
    /// </summary>
    private void SetPage(string key)
    {
        _lastPage = key;
        foreach (var button in Nav.Children.OfType<ToggleButton>())
        {
            button.IsChecked = (string)button.Tag == key; // also after a click on the shown page, which unchecked it
        }
        foreach (var page in Pages.Children.OfType<FrameworkElement>())
        {
            page.Visibility = (string)page.Tag == key ? Visibility.Visible : Visibility.Collapsed;
        }
        // A page built in code in an earlier theme is redrawn from what it
        // holds first (SettingsWindow.Themes.cs), then opened as on any look.
        RedrawIfStale(key);
        // Pages whose content is built in code build it on the first look:
        // anything built up front is paid for on every opening of the dialog,
        // whichever page shows (the pages were Hidden, and so laid out, until
        // the fit factor's release; Collapsed ones are not, which only makes
        // the rule cheaper to keep).
        // The RULE for every page (the owner reported slow openings twice,
        // Oct 1 2026): build on the first look, here; a long list goes in
        // batches — the first screenful at once, the rest per Background
        // tick, a version counter letting a newer build stop an older one
        // (AddWaypointRowBatch, the skin tiles). The one exception, from the
        // days the tallest page set the dialog's height: Server rules' stamp
        // and note are still filled eagerly (PrepareRulesPage).
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

    // ---- Account: SettingsWindow.Account.cs (the in-app sign-in, v1.31) -----

    // ---- Hotkeys (capture) --------------------------------------------------
    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHint.Text = "Press the new combination now (Esc keeps the current one).";
        ShowTone(HotkeyHint, Tone.Neutral);
        DisarmDefaultKeys(); // a key set by hand: the armed button goes back
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
            ShowTone(HotkeyHint, Tone.Neutral);
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
            ShowTone(HotkeyHint, Tone.Warn);
            return;
        }

        var spec = new HotkeySpec(mods, key);

        var clash = _hotkeyEntries.FirstOrDefault(kv => kv.Key != box && kv.Value.Chosen == spec);
        if (clash.Key is not null)
        {
            HotkeyHint.Text = $"{spec} is already assigned to {clash.Value.Label}.";
            ShowTone(HotkeyHint, Tone.Bad);
            return;
        }

        // MainWindow suspends its registrations while this dialog is open,
        // so our own combos probe as free like any other.
        // It re-registers from config in a finally when the dialog closes.
        if (!IsHotkeyAvailable(spec))
        {
            HotkeyHint.Text = $"{spec} is taken by another app — keeping {entry.Chosen}.";
            ShowTone(HotkeyHint, Tone.Bad);
            return;
        }

        entry.Chosen = spec;
        box.Text = spec.ToString();
        WriteHotkeys();
        HotkeyHint.Text = spec == entry.Initial
            ? "Unchanged."
            : $"{spec} is set. It works once Settings is closed.";
        ShowTone(HotkeyHint, spec == entry.Initial ? Tone.Neutral : Tone.Good);
    }

    private bool _defaultKeysArmed;

    /// <summary>
    /// "Use the default keys" (1.38): every key to the layout a fresh install
    /// gets (OverlayConfig.DefaultHotkey's comment). An update keeps a
    /// player's keys, so this is how one moves over. Two clicks, since it
    /// changes every key at once. All or nothing: if another program holds
    /// one of the defaults, nothing changes and the hint names it — a key
    /// left behind could clash with another's new default.
    /// </summary>
    private void DefaultKeys_Click(object sender, RoutedEventArgs e)
    {
        if (!_defaultKeysArmed)
        {
            _defaultKeysArmed = true;
            DefaultKeysButton.Content = "Really change them all?";
            return;
        }
        DisarmDefaultKeys();

        if (_hotkeyEntries.Values.All(entry => entry.Chosen == entry.Default))
        {
            HotkeyHint.Text = "These are the default keys already.";
            ShowTone(HotkeyHint, Tone.Neutral);
            return;
        }
        var held = _hotkeyEntries.Values
            .Where(entry => entry.Chosen != entry.Default && !IsHotkeyAvailable(entry.Default))
            .Select(entry => entry.Default.ToString())
            .ToList();
        if (held.Count > 0)
        {
            HotkeyHint.Text = $"{string.Join(", ", held)} {(held.Count == 1 ? "is" : "are")} taken by another app, so nothing changed. Close that app, or set the keys one by one.";
            ShowTone(HotkeyHint, Tone.Bad);
            return;
        }
        foreach (var (box, entry) in _hotkeyEntries)
        {
            entry.Chosen = entry.Default;
            box.Text = entry.Chosen.ToString();
        }
        WriteHotkeys();
        HotkeyHint.Text = "The default keys are set. They work once Settings is closed.";
        ShowTone(HotkeyHint, Tone.Good);
    }

    private void DisarmDefaultKeys()
    {
        _defaultKeysArmed = false;
        DefaultKeysButton.Content = "Use the default keys";
    }

    /// <summary>Written at once like every setting; registered when the dialog closes, since the hotkeys are suspended while it is open.</summary>
    private void WriteHotkeys()
    {
        _config.Hotkey = _hotkeyEntries[EditHotkeyBox].Chosen.ToString();
        _config.HotkeyHideAll = _hotkeyEntries[HideHotkeyBox].Chosen.ToString();
        _config.HotkeyMinimapView = _hotkeyEntries[ViewHotkeyBox].Chosen.ToString();
        _config.HotkeyHeatmap = _hotkeyEntries[HeatmapHotkeyBox].Chosen.ToString();
        _config.HotkeyPrimeCheck = _hotkeyEntries[PrimeHotkeyBox].Chosen.ToString();
        _config.HotkeyStatsView = _hotkeyEntries[StatsViewHotkeyBox].Chosen.ToString();
        _config.HotkeyBigMap = _hotkeyEntries[BigMapHotkeyBox].Chosen.ToString();
        _config.HotkeySkin = _hotkeyEntries[SkinHotkeyBox].Chosen.ToString();
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

    // ---- Widgets (the scale labels) -----------------------------------------
    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleLabel != null) ScaleLabel.Text = $"{e.NewValue:0%}";
    }

    private void PrimeScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PrimeScaleLabel != null) PrimeScaleLabel.Text = $"{e.NewValue:0%}";
    }

    private void ControlPanelScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ControlPanelScaleLabel != null) ControlPanelScaleLabel.Text = $"{e.NewValue:0%}";
    }

    // ---- General (the look) -------------------------------------------------
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null) OpacityLabel.Text = $"{e.NewValue:0%}";
    }

    private void FadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeLabel != null) FadeLabel.Text = $"{e.NewValue:0%}";
    }

    // ---- Maps ---------------------------------------------------------------
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomLabel != null) ZoomLabel.Text = $"{e.NewValue:0.##}×";
    }

    private void MinimapScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinimapScaleLabel != null) MinimapScaleLabel.Text = $"{e.NewValue:0%}";
    }

    // ---- No Save -------------------------------------------------------------
    // Since 1.38 every control applies its own change (SettingsWindow.Apply.cs).
    // A new setting writes its config key in WireLiveSettings and names what
    // MainWindow must re-apply (a SettingsChange), or is read live by MainWindow.
}
