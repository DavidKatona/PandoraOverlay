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
/// interop lives in OverlayWindowBase.
/// </summary>
public partial class MainWindow : OverlayWindowBase
{
    // ---- Layout constants -------------------------------------------------
    private const double TrackWidth = 170; // must match bar track width in XAML
    private const double ContentWidth = WidgetFrame.Width - 26; // the frame minus RootPanel's padding (12 a side) and border
    private const double FooterGap = 10; // px the status line and the view name keep between them

    private static readonly TimeSpan HeaderPulse = TimeSpan.FromSeconds(10);
    private const double WoundedBelow = 0.50; // the game calls you Wounded under half health; the health bar turns amber on the same line

    // ---- Brushes ----------------------------------------------------------
    private static readonly Brush HealthGood = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush HealthWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush HealthCrit = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush GrowthNormal = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB0));
    private static readonly Brush HungerTint = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D));     // the bar's orange, lightened for text
    private static readonly Brush ThirstTint = new SolidColorBrush(Color.FromRgb(0x81, 0xD4, 0xFA));     // the bar's blue, lightened for text
    private static readonly Brush StaminaTint = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));    // the bar's yellow, lightened for text
    private static readonly Brush TimeLeftOnFill = new SolidColorBrush(Color.FromRgb(0x10, 0x15, 0x1B)); // panel-glass dark, for a label inside the fill
    private const double TimeLeftGap = 5; // px between the fill's tip and its label

    // Fracture badges: always drawn (the row is part of the fixed frame), lit
    // only while that part is fractured — like the site's own fracture icons.
    private static readonly Brush FracturedBadge = new SolidColorBrush(Color.FromArgb(0xB3, 0x40, 0x20, 0x20));
    private static readonly Brush FracturedText = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
    private static readonly Brush IntactBadge = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IntactText = new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x60));

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
        ApplyAppearance(_config);
        ApplyStatsView();

        _poll = new PollService(_config);
        _poll.SnapshotReceived += OnSnapshot;
        _poll.PollFailed += OnPollFailed;
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

        Closed += (_, _) =>
        {
            _tray.Dispose();
            _poll.Stop();
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
        try
        {
            var dialog = new SettingsWindow(_config, _library, _book, _poll, _me?.Dino, page) { Topmost = true };
            var saved = dialog.ShowDialog() == true;

            if (!saved)
            {
                if (string.IsNullOrWhiteSpace(_config.GetCookie())) ShowNoCookieState();
                return;
            }
            if (dialog.CookieChanged)
            {
                DinoText.Text = "Connecting…";
                SetStatus("");
                SetAttention(true);
                _poll.RebuildClient();
            }
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
    /// One quiet launch-time check: on a newer release, mention it once in the
    /// status line (the next poll overwrites it) and hand it to the tray,
    /// which keeps a tooltip note + menu entry for the session.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        if (await UpdateChecker.CheckAsync() is not { } tag) return;
        SetStatus($"Update available: {tag}");
        _tray.ShowUpdateAvailable(tag);
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
        DinoText.Text = "Not set up yet";
        SetStatus("Open Settings from the tray icon to connect your account");
        SetAttention(true);
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
            prime: Frame(WidgetFrame.LargeHeight, _config.PrimeScale ?? _config.UiScale),
            activity: Frame(WidgetFrame.SmallHeight, _config.ActivityScale ?? _config.PrimeScale ?? _config.UiScale),
            minimap: Frame(WidgetFrame.LargeHeight, _config.MinimapScale ?? 1.0),
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
        UpdateUi(result);
        UpdateArea(); // before your own lines: a spawn line says where you spawned
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
    /// minimap's pill is told the result. Runs per poll and after a
    /// Settings save; with both uses switched off the map is never loaded.
    /// </summary>
    private void UpdateArea()
    {
        if ((!_config.MinimapAreaEnabled && !_config.ActivityAreaLines) ||
            _me is not { } me || _poll.Calibration is not { } cal || AreaMapAsset.Shared is not { } map)
        {
            _areaJournal.Reset();
            _minimap?.SetArea(null);
            return;
        }
        var now = DateTime.UtcNow;
        var (fx, fy) = cal.ToFraction(me.X, me.Y);
        var entered = _areaJournal.Update(map, fx, fy, now);
        if (entered is not null && _config.ActivityAreaLines) _log.Post(SelfActivity.AreaLine(entered, now));
        _minimap?.SetArea(_areaJournal.Current);
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

    // ---- Rendering ----------------------------------------------------------
    private void UpdateUi(MyLocationResponse result)
    {
        if (!result.InGame || result.Player is null)
        {
            _growth.Reset(); // a wall-clock gap would flatten the measured slope
            _hungerDrain.Reset();
            _thirstDrain.Reset();
            _stamina.Reset();
            _damage.Reset();
            _combatSpeed.Reset();
            _attention.Reset();
            _lowStat.Reset();
            _milestones.Reset();
            SetAttention(_attention.FlipHeld); // "Not in-game" is nothing to watch — bar a view flip's few seconds
            RenderTimeLeft();
            DinoText.Text = "Not in-game";
            GrowthText.Text = "";
            GrowthText.Foreground = GrowthNormal;
            ConditionText.Text = "";
            SetBar(HealthFill, HealthPct, 0);
            SetBar(StaminaFill, StaminaPct, 0);
            SetBar(HungerFill, HungerPct, 0);
            SetBar(ThirstFill, ThirstPct, 0);
            SetBar(CombatHealthFill, CombatHealthPct, 0);
            SetBar(CombatStaminaFill, CombatStaminaPct, 0);
            SetBar(DamageFill, DamagePct, 0);
            SpeedValue.Text = "—";
            SetPulse(HealthFill, false);
            SetPulse(CombatHealthFill, false);
            SetPulse(HungerFill, false);
            SetPulse(ThirstFill, false);
            SetFractures(false, false, false);
            // Not in-game the poll idles; say so, or a slow reaction to a spawn reads as frozen.
            SetStatus(_poll.IsIdling
                ? $"Connected · checking every {_poll.Interval.TotalSeconds:0}s · {DateTime.Now:HH:mm:ss}"
                : $"Connected · waiting for spawn · {DateTime.Now:HH:mm:ss}");
            return;
        }

        var p = result.Player;

        var gender = p.Gender ?? "";
        var symbol = gender.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "♂"
                   : gender.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "♀"
                   : "";
        DinoText.Text = $"{p.Dino} {symbol}".Trim();

        _growth.Add(p);
        GrowthText.Text = _growth.Status switch
        {
            GrowthTracker.GrowthStatus.Growing when _growth.Eta is { } eta =>
                $"Growth {p.Growth * 100:0.#}% · ~{FormatEta(eta)}",
            GrowthTracker.GrowthStatus.Paused => $"Growth {p.Growth * 100:0.#}% · paused",
            GrowthTracker.GrowthStatus.Full => "Fully grown",
            _ => $"Growth {p.Growth * 100:0.#}%"
        };
        GrowthText.Foreground = _growth.Status == GrowthTracker.GrowthStatus.Paused
            ? HealthWarn
            : GrowthNormal;

        // The combat view's corner: the game's own word for health under 50%,
        // in the health bar's colour; "Healthy" (quiet) above it, so the slot
        // always answers the question.
        ConditionText.Text = p.Health < WoundedBelow ? "Wounded" : "Healthy";
        ConditionText.Foreground = p.Health switch
        {
            < 0.25 => HealthCrit,
            < WoundedBelow => HealthWarn,
            _ => GrowthNormal
        };

        // Both views are kept up to date, so flipping between them is instant.
        SetBar(HealthFill, HealthPct, p.Health);
        SetBar(CombatHealthFill, CombatHealthPct, p.Health);
        HealthFill.Background = CombatHealthFill.Background = p.Health switch
        {
            < 0.25 => HealthCrit,
            < WoundedBelow => HealthWarn,
            _ => HealthGood
        };
        SetBar(StaminaFill, StaminaPct, p.Stamina);
        SetBar(CombatStaminaFill, CombatStaminaPct, p.Stamina);
        SetBar(HungerFill, HungerPct, p.Hunger);
        SetBar(ThirstFill, ThirstPct, p.Thirst);
        _hungerDrain.Add(p);
        _thirstDrain.Add(p);
        _stamina.Add(p);
        RenderTimeLeft();

        // The combat view's two own rows: damage taken in this fight (a bar
        // that grows, capped at the full track) and the current speed.
        _damage.Add(p);
        SetBar(DamageFill, DamagePct, _damage.Total);
        DamagePct.Text = $"{_damage.Total * 100:0}%"; // the bar stops at the track's end; a long fight's number does not
        _combatSpeed.Add(p.X, p.Y);
        SpeedValue.Text = _combatSpeed.SpeedMps is { } mps ? $"{mps * 3.6:0} km/h" : "—";

        // Alerts: the rules are pure and always run; the sounds are opt-in,
        // and each moment also becomes a line in the Activity feed.
        var now = DateTime.UtcNow;
        if (_milestones.Update(p) is { } stage)
        {
            PulseBriefly(GrowthText, HeaderPulse); // a stage reached is always worth a blink
            _attention.NoteEvent();
            if (_config.GrowthChimeEnabled) Chime();
            _log.Post(SelfActivity.GrowthLine(stage, now));
        }
        if (_lowStat.Update(p))
        {
            if (_config.LowStatChimeEnabled) Chime();
            if (_lowStat.HungerFired) _log.Post(SelfActivity.LowStatLine("Hunger", _hungerDrain.Label, now));
            if (_lowStat.ThirstFired) _log.Post(SelfActivity.LowStatLine("Thirst", _thirstDrain.Label, now));
        }

        SetAttention(_attention.Update(p, _hungerDrain.TimeLeft, _thirstDrain.TimeLeft, CombatViewOn, _damage.InFight));

        // Critical-stat pulse. Stamina is deliberately excluded — it drains to
        // zero every sprint by design and would train the eye to ignore it.
        SetPulse(HealthFill, p.Health < 0.25);
        SetPulse(CombatHealthFill, p.Health < 0.25);
        SetPulse(HungerFill, p.Hunger < 0.25);
        SetPulse(ThirstFill, p.Thirst < 0.25);

        SetFractures(p.HeadFractured, p.BodyFractured, p.LegsFractured);

        SetStatus($"Live · updated {DateTime.Now:HH:mm:ss}");
    }

    /// <summary>
    /// The footer's left half. Every status goes through here so the view
    /// name at the right end can step aside for one that needs the whole
    /// line (the first-run hint, a hotkey conflict) instead of overlapping
    /// it or cutting it short.
    /// </summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        FitViewName();
    }

    /// <summary>Shows the view name only while the status line leaves room for it (hidden, not collapsed: the row keeps its height).</summary>
    private void FitViewName()
    {
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        StatusText.Measure(unbounded);
        ViewText.Measure(unbounded);
        var fits = StatusText.DesiredSize.Width + FooterGap + ViewText.DesiredSize.Width <= ContentWidth;
        ViewText.Visibility = fits ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>The three badges are always there; a fractured part lights up, the rest stay dim. Never changes the panel's size.</summary>
    private void SetFractures(bool head, bool body, bool legs)
    {
        SetBadge(FracHead, FracHeadText, head);
        SetBadge(FracBody, FracBodyText, body);
        SetBadge(FracLegs, FracLegsText, legs);
    }

    private static void SetBadge(Border badge, TextBlock text, bool fractured)
    {
        badge.Background = fractured ? FracturedBadge : IntactBadge;
        text.Foreground = fractured ? FracturedText : IntactText;
    }

    // ---- Stats views --------------------------------------------------------

    private bool CombatViewOn => string.Equals(_config.StatsView, "combat", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shows the Survival view (health, stamina, hunger, thirst, growth) or
    /// the Combat view (health, stamina, damage taken in this fight, speed;
    /// no growth readout) — two layouts of the SAME four rows inside the
    /// same fixed frame, at the same sizes, so flipping moves nothing. A
    /// player's request (Sep 30 2026: "only health, stam and body breaks ...
    /// to focus and help with pvp"). A shorter panel — the literal ask — was
    /// turned down for the frame's sake, and a first build that drew health
    /// and stamina LARGE was rejected on sight by the owner: size is not how
    /// this overlay emphasises anything. The combat view shows different
    /// information instead of bigger information. The header's corner
    /// follows the view (growth ↔ Wounded/Healthy) and the footer names it,
    /// like the minimap's does. Each view also has its own fade ruleset
    /// (StatsAttention).
    /// </summary>
    private void ApplyStatsView()
    {
        var combat = CombatViewOn;
        FullView.Visibility = combat ? Visibility.Collapsed : Visibility.Visible;
        CombatView.Visibility = combat ? Visibility.Visible : Visibility.Collapsed;
        GrowthText.Visibility = combat ? Visibility.Collapsed : Visibility.Visible;
        ConditionText.Visibility = combat ? Visibility.Visible : Visibility.Collapsed;
        ViewText.Text = combat ? "combat view" : "survival view";
        FitViewName();
        RenderTimeLeft();

        // A flip — by hotkey, the control panel or a Settings save — always
        // lights the panel for a few seconds: it is a change to the panel
        // itself (owner's call). Then the new view's ruleset decides.
        if (_shownCombat is { } was && was != combat)
        {
            _attention.NoteViewFlip();
            SetAttention(true);
        }
        _shownCombat = combat;
    }

    private bool? _shownCombat; // the view on screen, to tell a flip from a re-apply

    /// <summary>The stats-view hotkey and the control panel's View button: flips Survival ↔ Combat (persisted with the next Save).</summary>
    private void ToggleStatsView()
    {
        _config.StatsView = CombatViewOn ? "survival" : "combat";
        ApplyStatsView();
    }

    /// <summary>The time labels on the bars; the trackers run either way, so the Settings checkbox applies at once.</summary>
    private void RenderTimeLeft()
    {
        var on = _config.StatTimeLeftEnabled;
        SetBarLabel(HungerLeft, on ? _hungerDrain.Label : null, HungerFill.Width, HungerTint);
        SetBarLabel(ThirstLeft, on ? _thirstDrain.Label : null, ThirstFill.Width, ThirstTint);
        // Stamina reads both ways: "~25s" until empty, "full ~40s" until recovered.
        SetBarLabel(StaminaLeft, on ? _stamina.Label : null, StaminaFill.Width, StaminaTint);
        SetBarLabel(CombatStaminaLeft, on ? _stamina.Label : null, CombatStaminaFill.Width, StaminaTint);
    }

    /// <summary>
    /// A bar-chart data label: it rides just past the fill's tip in the bar's
    /// own (lightened) colour, so it reads as part of that bar rather than a
    /// second number next to the percent. With no room left on the track it
    /// flips inside the fill's end, dark on the bright colour.
    /// </summary>
    private static void SetBarLabel(System.Windows.Controls.TextBlock label, string? text, double fillWidth, Brush tint)
    {
        if (text is null)
        {
            label.Visibility = Visibility.Collapsed;
            return;
        }

        label.Text = text;
        label.Visibility = Visibility.Visible; // before measuring — a collapsed element measures as 0
        label.Margin = default;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = label.DesiredSize.Width;
        var outside = fillWidth + TimeLeftGap + width <= TrackWidth;

        label.Foreground = outside ? tint : TimeLeftOnFill;
        label.Margin = new Thickness(outside ? fillWidth + TimeLeftGap : fillWidth - TimeLeftGap - width, 0, 0, 0);
    }

    /// <summary>The Windows "Exclamation" sound — no bundled audio, and it follows the user's sound scheme.</summary>
    private static void Chime()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch
        {
            // No sound device / scheme: silence is the right fallback.
        }
    }

    // ---- Critical-stat pulse ------------------------------------------------
    private readonly HashSet<Border> _pulsing = new();

    private void SetPulse(Border fill, bool on)
    {
        if (on == _pulsing.Contains(fill)) return;
        if (on)
        {
            _pulsing.Add(fill);
            fill.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(600))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
        else
        {
            _pulsing.Remove(fill);
            fill.BeginAnimation(OpacityProperty, null);
            fill.Opacity = 1;
        }
    }

    /// <summary>"~"-worthy in-game time: "45m", "3h 10m", "1d 4h".</summary>
    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalMinutes < 1) return "1m";
        if (eta.TotalHours < 1) return $"{eta.TotalMinutes:0}m";
        if (eta.TotalDays < 1) return $"{(int)eta.TotalHours}h {eta.Minutes:00}m";
        return $"{(int)eta.TotalDays}d {eta.Hours}h";
    }

    private static void SetBar(System.Windows.Controls.Border fill,
                               System.Windows.Controls.TextBlock pct,
                               double value)
    {
        var v = Math.Clamp(value, 0, 1);
        fill.Width = v * TrackWidth;
        pct.Text = $"{v * 100:0}%";
    }
}
