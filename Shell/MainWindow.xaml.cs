using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// The stats panel and the app's orchestrator: owns the config, the shared
/// PollService, the waypoint library, the friend book and the activity log
/// (posting your own and your friends' events into it), registers the
/// global hotkeys, and manages the other widgets' lifetimes. Window-style
/// interop lives in OverlayWindowBase; the stats panel's drawing in
/// MainWindow.Stats.cs.
/// </summary>
public partial class MainWindow : OverlayWindowBase
{
    // ---- State ------------------------------------------------------------
    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly TrayIcon _tray;
    private readonly GrowthTracker _growth = new();
    private readonly DrainTracker _hungerDrain = new(p => p.Hunger);
    private readonly DrainTracker _thirstDrain = new(p => p.Thirst);
    private readonly StaminaTracker _stamina = new();
    private readonly DamageTracker _damage = new();
    private readonly SpeedTracker _combatSpeed; // a short window: the combat row should follow a sprint or a stop within a couple of polls
    private readonly StatsAttention _attention = new();
    // The feed's state lives here, not in the Activity widget, so the last ten
    // minutes survive the widget being hidden or closed.
    private readonly FriendFeed _feed = new();
    private readonly ActivityLog _log = new();
    private readonly SelfActivity _self = new();
    private readonly AreaJournal _areaJournal = new(); // where you are: the minimap's pill and the feed's "Entered …" lines
    private PlayerState? _me;          // your last in-game state, for the feed's proximity rule
    private PrimeSnapshot? _primeBefore; // the Prime result on screen when the current check started
    private MinimapWindow? _minimap;
    private PrimeWindow? _prime;
    private ActivityWindow? _activity;
    private ControlPanelWindow? _controlPanel;
    private HotkeySpec _hotkey;
    private HotkeySpec _hotkeyHide;
    private HotkeySpec _hotkeyView;
    private HotkeySpec _hotkeyHeatmap;
    private HotkeySpec _hotkeyPrime;
    private HotkeySpec _hotkeyStatsView;
    private HotkeySpec _hotkeyBigMap;
    private readonly LowStatAlert _lowStat = new();
    private readonly GrowthMilestones _milestones = new();
    private bool _overlayHidden;
    private readonly List<string> _registerFailures = new();

    public MainWindow()
    {
        InitializeComponent();

        _config = OverlayConfig.Load();
        // Two and a half poll intervals, at least 6 s: always room for two steps, whatever cadence is configured.
        _combatSpeed = new SpeedTracker(TimeSpan.FromSeconds(Math.Max(6, 2.5 * Math.Max(2, _config.PollIntervalSeconds))));
        _library = WaypointLibrary.Load();
        MigrateWaypointSlots();
        _library.Changed += () => _library.Save(); // user content: saved on every change, not just on exit
        _book = FriendBook.Load();
        _book.Changed += () => _book.Save(); // membership + preferences; the per-fetch last-seen facts are flushed on exit
        if (_config.WindowX is { } wx && _config.WindowY is { } wy)
        {
            Left = wx;
            Top = wy;
        }
        _hotkey = HotkeySpec.TryParse(_config.Hotkey) ?? HotkeySpec.Default;
        _hotkeyHide = HotkeySpec.TryParse(_config.HotkeyHideAll) ?? new HotkeySpec(ModifierKeys.Control, Key.F4);
        _hotkeyView = HotkeySpec.TryParse(_config.HotkeyMinimapView) ?? new HotkeySpec(ModifierKeys.Control, Key.F5);
        _hotkeyHeatmap = ResolveLateHotkey(_config.HotkeyHeatmap, HeatmapHotkeyCandidates,
            new[] { _hotkey, _hotkeyHide, _hotkeyView }, v => _config.HotkeyHeatmap = v);
        _hotkeyPrime = ResolveLateHotkey(_config.HotkeyPrimeCheck, PrimeHotkeyCandidates,
            new[] { _hotkey, _hotkeyHide, _hotkeyView, _hotkeyHeatmap }, v => _config.HotkeyPrimeCheck = v);
        _hotkeyStatsView = ResolveLateHotkey(_config.HotkeyStatsView, StatsViewHotkeyCandidates,
            new[] { _hotkey, _hotkeyHide, _hotkeyView, _hotkeyHeatmap, _hotkeyPrime }, v => _config.HotkeyStatsView = v);
        _hotkeyBigMap = ResolveLateHotkey(_config.HotkeyBigMap, BigMapHotkeyCandidates,
            new[] { _hotkey, _hotkeyHide, _hotkeyView, _hotkeyHeatmap, _hotkeyPrime, _hotkeyStatsView }, v => _config.HotkeyBigMap = v);
        ApplyAppearance(_config);
        ApplyStatsView();

        _poll = new PollService(_config);
        _poll.SnapshotReceived += OnSnapshot;
        _poll.PollFailed += OnPollFailed;
        _poll.SignedOut += OnSignedOut;
        // Subscribed here, before any widget exists, so this handler runs FIRST:
        // the book is synced (new friends named and coloured) before the
        // minimap and the Activity widget see the same roster.
        _poll.FriendsChanged += OnFriendsRoster;
        _poll.PrimeCheckStarted += () => _primeBefore = _config.Prime; // PollService swaps in the new result before PrimeChecked
        _poll.PrimeChecked += result =>
        {
            if (result.Outcome == PrimeCheckOutcome.Ok && result.Snapshot is { } fresh)
            {
                _log.Post(SelfActivity.PrimeLines(_primeBefore, fresh, DateTime.UtcNow));
            }
        };
        _poll.SkinApplied += applied => _log.Post(SelfActivity.SkinLine(applied.SkinName, applied.Pattern, DateTime.UtcNow));

        _tray = new TrayIcon(
            toggleEditMode: ToggleEditMode,
            toggleOverlay: ToggleOverlayVisibility,
            openSettings: OpenSettings,
            openRules: () => OpenSettingsOn("Rules"),
            exit: () => Application.Current.Shutdown());
        UpdateHotkeyTexts();

        Loaded += (_, _) =>
        {
            StartupRegistration.RepointToThisExe(); // an older copy's "Start with Windows" entry would start the older copy
            _ = CheckForUpdateAsync();
            FillDefaultPositions(); // before the other windows exist: they read their positions from config
            if (!_config.StatsEnabled) Hide();
            if (_config.MinimapEnabled) ShowMinimap();
            if (_config.PrimeEnabled) ShowPrime();
            if (_config.ActivityEnabled) ShowActivity();

            if (string.IsNullOrWhiteSpace(_config.GetCookie()))
            {
                ShowNoCookieState();
                OpenSettings(); // first run: walk the user through setup
                return;
            }
            _poll.Start();
        };

        Closing += (_, _) => _exiting = true;
        Closed += (_, _) =>
        {
            _tray.Dispose();
            _poll.Stop();
            _bigMap?.Close();
            PersistState();
            _poll.Dispose();
            _minimap?.Close();
            _prime?.Close();
            _activity?.Close();
            _controlPanel?.Close();
        };
    }

    // ---- Settings flow ----------------------------------------------------
    private void OpenSettings() => OpenSettingsOn(null);

    /// <param name="page">A page to open on ("Rules" from the tray), or null for the one used last.</param>
    private void OpenSettingsOn(string? page)
    {
        CloseBigMap(); // the dialog opens over the widgets, not over the map
        // Suspend the global hotkeys while the dialog is open: WM_HOTKEY is
        // system-level, so they would fire behind the modal dialog (Ctrl+F4
        // hiding the overlay mid-configuration), and suspension also frees
        // our own combos so the capture boxes can see and reassign them.
        var hwnd = new WindowInteropHelper(this).Handle;
        HotkeySpec.Unregister(hwnd, HotkeyId);
        HotkeySpec.Unregister(hwnd, HideAllHotkeyId);
        HotkeySpec.Unregister(hwnd, ViewHotkeyId);
        HotkeySpec.Unregister(hwnd, HeatmapHotkeyId);
        HotkeySpec.Unregister(hwnd, PrimeHotkeyId);
        HotkeySpec.Unregister(hwnd, StatsViewHotkeyId);
        HotkeySpec.Unregister(hwnd, BigMapHotkeyId);
        try
        {
            var dialog = new SettingsWindow(_config, _library, _book, _poll, _me?.Dino, page, UpdateHooksForSettings()) { Topmost = true };
            var saved = dialog.ShowDialog() == true;

            // A sign-in or sign-out acted at once (like the Skins page), so it
            // holds whether the dialog was saved or cancelled.
            if (dialog.CookieChanged) ApplySession();
            if (!saved)
            {
                if (string.IsNullOrWhiteSpace(_config.GetCookie())) ShowNoCookieState();
                return;
            }
            // Everything a Save changed is hot-applied from here on: a setting
            // never needs a restart.
            if (TrailKeep <= TimeSpan.Zero) _trail.Reset(); // the trail switched off: forget the path, not just hide it
            if (dialog.MinimapChanged || dialog.AppearanceChanged || dialog.FriendsChanged) _minimap?.ApplySettings();
            UpdateArea(); // the pill or the feed's area lines may have been switched on: name the area now, not a poll later
            ApplyStatsView(); // the default view is a Settings choice; cheap to reapply
            if (dialog.AppearanceChanged)
            {
                ApplyAppearance(_config);
                RenderTimeLeft(); // the time-left checkbox rides the Appearance flag
                _prime?.ApplySettingsFromConfig();
                _controlPanel?.ApplySettingsFromConfig();
            }
            if (dialog.AppearanceChanged || dialog.ActivityChanged || dialog.FriendsChanged) _activity?.ApplySettingsFromConfig();
            if (dialog.FriendsChanged) _ = _poll.RefreshFriendsAsync(); // the minimap layer may have been switched on or off
            if (dialog.PositionsReset) ResetPositions();
            if (_autoHidden && !_config.HideWhenNotInGame) RevealAutoHidden(); // switched off while hidden by it
        }
        finally
        {
            // Restore (or apply changed) registrations from config — this
            // also covers the cancel path.
            ApplyHotkeysFromConfig();
        }
    }

    /// <summary>
    /// v1.20's three colour slots become library entries (Blue / Green /
    /// Purple, in their colours) on the first launch of the library, and the
    /// config slots are blanked so this runs once. Nobody loses a waypoint.
    /// </summary>
    private void MigrateWaypointSlots()
    {
        if (_config.Waypoints.All(w => w is null)) return;
        string[] names = { "Blue", "Green", "Purple" };
        for (var i = 0; i < _config.Waypoints.Length; i++)
        {
            if (_config.Waypoints[i] is { } slot) _library.Add(names[i], slot.X, slot.Y, i);
        }
        Array.Fill(_config.Waypoints, null);
        _library.Save();
        _config.Save();
    }

    private void ShowNoCookieState()
    {
        DinoText.Text = "Not signed in";
        SetStatus("Open Settings from the tray icon to sign in");
        SetAttention(true);
    }

    // ---- The session (v1.31) ---------------------------------------------------

    /// <summary>
    /// The stored session changed — a sign-in (Settings, or the tray's "Sign
    /// in again…") or a sign-out: the poll follows it. A session = a fresh
    /// client and polling from now; none = polling stops and the panel says
    /// so, and nothing of the old account stays in the service.
    /// </summary>
    private void ApplySession()
    {
        _tray.ClearSignedOut();
        if (string.IsNullOrWhiteSpace(_config.GetCookie()))
        {
            _poll.ForgetSession();
            UpdateUi(new MyLocationResponse(false, null)); // bars to zero, trackers reset
            ShowNoCookieState();
            _tray.SetStatus("Pandora Overlay — not signed in");
            return;
        }
        DinoText.Text = "Connecting…";
        SetStatus("");
        SetAttention(true);
        _poll.RebuildClient();
    }

    /// <summary>
    /// PollService gave up on the cookie (the site refused it twice running):
    /// say so in words instead of "Disconnected · retrying" for good, and put
    /// the fix where it survives a locked, hidden overlay — the tray.
    /// </summary>
    private void OnSignedOut()
    {
        UpdateUi(new MyLocationResponse(false, null));
        DinoText.Text = "Not signed in";
        SetStatus("Session ended · sign in again from the tray");
        SetAttention(true);
        _tray.SetStatus("Pandora Overlay — session ended");
        _tray.ShowSignedOut(OpenSignIn);
    }

    /// <summary>The tray's "Sign in again…": the sign-in window straight away, no Settings in between.</summary>
    private void OpenSignIn()
    {
        CloseBigMap(); // the sign-in window is topmost too: not under a dimmed screen
        var outcome = SignInWindow.Run(owner: null);
        if (outcome is null) return;
        _config.ApplySignIn(outcome.CookieHeader, outcome.UserAgent, outcome.Account.Username, DateTime.UtcNow, outcome.Account.Avatar);
        _config.Save();
        ApplySession();
    }

    /// <summary>The stats panel takes part in the attention fade; its verdict comes from StatsAttention.</summary>
    protected override bool Fades => true;

    // ---- Default layout -------------------------------------------------------

    /// <summary>
    /// Gives every widget without a saved position its place in the
    /// two-column default layout (DefaultLayout.Columns), computed from the
    /// frames × each widget's scale — not from live windows, so a hidden
    /// widget leaves its gap and the arrangement is the same whichever
    /// widgets are shown. Writes the positions INTO config; the windows read
    /// them as usual. Runs once at startup and after Reset positions.
    /// </summary>
    private void FillDefaultPositions()
    {
        var screen = GetScreenBoundsDips();
        var p = DefaultLayout.Columns(screen,
            prime: Frame(WidgetFrame.LargeHeight, _config.PrimeScale),
            activity: Frame(WidgetFrame.SmallHeight, _config.ActivityScale),
            minimap: Frame(WidgetFrame.LargeHeight, _config.MinimapScale),
            stats: Frame(WidgetFrame.SmallHeight, _config.UiScale));

        if (_config.WindowX is null || _config.WindowY is null)
        {
            (_config.WindowX, _config.WindowY) = (p.Stats.X, p.Stats.Y);
            Left = p.Stats.X;
            Top = p.Stats.Y;
        }
        if (_config.MinimapX is null || _config.MinimapY is null) (_config.MinimapX, _config.MinimapY) = (p.Minimap.X, p.Minimap.Y);
        if (_config.PrimeX is null || _config.PrimeY is null) (_config.PrimeX, _config.PrimeY) = (p.Prime.X, p.Prime.Y);
        if (_config.ActivityX is null || _config.ActivityY is null) (_config.ActivityX, _config.ActivityY) = (p.Activity.X, p.Activity.Y);
    }

    private static Size Frame(double height, double scale)
    {
        var s = Math.Clamp(scale, 0.75, 1.5); // the same clamp ApplyAppearance uses
        return new Size(WidgetFrame.Width * s, height * s);
    }

    /// <summary>
    /// Settings → General → Reset positions: every widget back to the default
    /// layout, live windows moved at once, hidden ones placed when next shown.
    /// Positions ONLY — scales are readability preferences with a slider each
    /// (owner's call, Sep 29 2026).
    /// </summary>
    private void ResetPositions()
    {
        _config.WindowX = _config.WindowY = null;
        _config.MinimapX = _config.MinimapY = null;
        _config.PrimeX = _config.PrimeY = null;
        _config.ActivityX = _config.ActivityY = null;
        _config.ControlPanelX = _config.ControlPanelY = null;
        FillDefaultPositions();

        if (_minimap is not null) (_minimap.Left, _minimap.Top) = (_config.MinimapX!.Value, _config.MinimapY!.Value);
        if (_prime is not null) (_prime.Left, _prime.Top) = (_config.PrimeX!.Value, _config.PrimeY!.Value);
        if (_activity is not null) (_activity.Left, _activity.Top) = (_config.ActivityX!.Value, _config.ActivityY!.Value);
        _controlPanel?.PlaceDefault();
        _config.Save();
    }

    private void PersistState()
    {
        _config.WindowX = Left;
        _config.WindowY = Top;
        if (_minimap is not null)
        {
            _config.MinimapX = _minimap.Left;
            _config.MinimapY = _minimap.Top;
        }
        if (_prime is not null)
        {
            _config.PrimeX = _prime.Left;
            _config.PrimeY = _prime.Top;
        }
        if (_activity is not null)
        {
            _config.ActivityX = _activity.Left;
            _config.ActivityY = _activity.Top;
        }
        _book.Save(); // flushes the last-seen facts Sync updates silently
        if (_controlPanel is not null)
        {
            _config.ControlPanelX = _controlPanel.Left;
            _config.ControlPanelY = _controlPanel.Top;
        }
        if (!string.IsNullOrWhiteSpace(_poll.CurrentCookie))
        {
            _config.SetCookie(_poll.CurrentCookie); // re-encrypt the rolled connect.sid
        }
        _config.Save();
    }

    // ---- Poll stream --------------------------------------------------------
    private void OnPollFailed(Exception ex)
    {
        // The exception's TYPE only, never its message: a message could carry
        // the cookie, and the cookie never reaches window text.
        SetStatus($"Disconnected · retrying ({ex.GetType().Name})");
        _tray.SetStatus("Pandora Overlay — disconnected");
        SetAttention(true); // a broken connection is worth eyes
    }

    /// <summary>
    /// The friends roster, right after your own snapshot: the book mirrors it
    /// first (so new friends have their colour and name on first sight), then
    /// the feed diffs it. Its lines go to the log while friends' events are
    /// included (the roster may still arrive for the minimap's arrows; the
    /// diff runs regardless so the baseline is right when they are switched
    /// back on). Null = cleared: the feed forgets its baseline so the
    /// roster's return seeds again.
    /// </summary>
    private void OnFriendsRoster(IReadOnlyList<FriendState>? roster)
    {
        if (roster is null)
        {
            _feed.ResetBaseline();
            return;
        }
        var now = DateTime.UtcNow;
        _book.Sync(roster, now);
        var fresh = _feed.Update(roster, _me, (id, site) => _book.DisplayName(id, site), _book.Notifies, now, AreaLookup());
        if (_config.ActivityIncludeFriends) _log.Post(fresh);
        // The friend-spawn chime is decided here, like the stats chimes, so it
        // plays whether or not the Activity widget is on screen.
        if (_config.FriendsChimeEnabled && fresh.Any(l => l.Kind is FeedKind.Spawned or FeedKind.NewLife)) Chime();
    }

    private void OnSnapshot(MyLocationResponse result)
    {
        _me = result.InGame ? result.Player : null;
        // The breadcrumb trail is fed here, before any map's own handler draws it.
        if (_poll.Calibration is not null && _me is { } at && TrailKeep > TimeSpan.Zero) _trail.Add(at, TrailKeep);
        UpdateUi(result);
        UpdateArea(); // before your own lines: a spawn line says where you spawned
        // Your own lines are always posted — the feed is yours; only friends'
        // lines have a switch (ActivityIncludeFriends, in OnFriendsRoster).
        _log.Post(_self.Update(result, _config.ActivityDamageLines, DateTime.UtcNow,
                               _config.ActivityAreaLines ? _areaJournal.Current : null));
        UpdateAutoHide(result.InGame && result.Player is not null);
        _tray.SetStatus(result.InGame && result.Player is { } p
            ? $"Pandora Overlay — {p.Dino} · HP {p.Health * 100:0}% · Growth {p.Growth * 100:0.#}%"
            : _autoHidden ? "Pandora Overlay — hidden until you spawn"
            : "Pandora Overlay — not in-game");

        // Cheap re-assert in case the game reshuffles the z-order.
        if (!EditMode && !Topmost)
        {
            Topmost = true;
        }
    }

    /// <summary>
    /// Which named area you are in, from the bundled area map and your own
    /// position — no request. Kept here, not in the minimap, so the feed's
    /// "Entered …" lines don't depend on that widget being shown; the
    /// minimap's pill (and the border layer's highlight of your area) is told
    /// the result. Runs per poll and after a
    /// Settings save; with every use switched off the map is never loaded.
    /// </summary>
    private void UpdateArea()
    {
        if ((!_config.MinimapAreaEnabled && !_config.ActivityAreaLines && !_config.MinimapAreaBordersEnabled && !_poll.BigMapOpen) ||
            _me is not { } me || _poll.Calibration is not { } cal || AreaMapAsset.Shared is not { } map)
        {
            _areaJournal.Reset();
            _minimap?.SetArea(null);
            _bigMap?.SetArea(null);
            return;
        }
        var now = DateTime.UtcNow;
        var (fx, fy) = cal.ToFraction(me.X, me.Y);
        var entered = _areaJournal.Update(map, fx, fy, now);
        if (entered is not null && _config.ActivityAreaLines) _log.Post(SelfActivity.AreaLine(entered, now));
        _minimap?.SetArea(_areaJournal.Current);
        _bigMap?.SetArea(_areaJournal.Current); // its header's "you are in"
    }

    /// <summary>
    /// World cm → the named area there, for a friend's spawn line ("… ·
    /// Swamps"); null while the feed's area naming is off or nothing can be
    /// placed yet. A plain lookup: the no-flicker rule is for someone
    /// walking a border, not for one mention of where a friend appeared.
    /// </summary>
    private Func<double, double, string?>? AreaLookup()
    {
        if (!_config.ActivityAreaLines || _poll.Calibration is not { } cal || AreaMapAsset.Shared is not { } map) return null;
        return (x, y) =>
        {
            var (fx, fy) = cal.ToFraction(x, y);
            return map.NameAt(fx, fy);
        };
    }
}
