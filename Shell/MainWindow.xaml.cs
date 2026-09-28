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

    private static readonly TimeSpan HeaderPulse = TimeSpan.FromSeconds(10);

    // ---- Brushes ----------------------------------------------------------
    private static readonly Brush HealthGood = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush HealthWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush HealthCrit = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush GrowthNormal = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB0));
    private static readonly Brush HungerTint = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D));     // the bar's orange, lightened for text
    private static readonly Brush ThirstTint = new SolidColorBrush(Color.FromRgb(0x81, 0xD4, 0xFA));     // the bar's blue, lightened for text
    private static readonly Brush TimeLeftOnFill = new SolidColorBrush(Color.FromRgb(0x10, 0x15, 0x1B)); // panel-glass dark, for a label inside the fill
    private const double TimeLeftGap = 5; // px between the fill's tip and its label

    // ---- State ------------------------------------------------------------
    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly TrayIcon _tray;
    private readonly GrowthTracker _growth = new();
    private readonly DrainTracker _hungerDrain = new(p => p.Hunger);
    private readonly DrainTracker _thirstDrain = new(p => p.Thirst);
    private readonly StatsAttention _attention = new();
    private readonly FriendFeed _feed = new();
    private readonly ActivityLog _log = new();
    private readonly SelfActivity _self = new();
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
    private readonly LowStatAlert _lowStat = new();
    private readonly GrowthMilestones _milestones = new();
    private bool _overlayHidden;
    private readonly List<string> _registerFailures = new();

    public MainWindow()
    {
        InitializeComponent();

        _config = OverlayConfig.Load();
        _library = WaypointLibrary.Load();
        MigrateWaypointSlots();
        _library.Changed += () => _library.Save(); // user content: saved on every change, not just on exit
        _book = FriendBook.Load();
        _book.Changed += () => _book.Save(); // membership + preferences; the per-fetch last-seen facts are flushed on exit
        Left = _config.WindowX;
        Top = _config.WindowY;
        _hotkey = HotkeySpec.TryParse(_config.Hotkey) ?? HotkeySpec.Default;
        _hotkeyHide = HotkeySpec.TryParse(_config.HotkeyHideAll) ?? new HotkeySpec(ModifierKeys.Control, Key.F4);
        _hotkeyView = HotkeySpec.TryParse(_config.HotkeyMinimapView) ?? new HotkeySpec(ModifierKeys.Control, Key.F5);
        _hotkeyHeatmap = ResolveLateHotkey(_config.HotkeyHeatmap, HeatmapHotkeyCandidates,
            new[] { _hotkey, _hotkeyHide, _hotkeyView }, v => _config.HotkeyHeatmap = v);
        _hotkeyPrime = ResolveLateHotkey(_config.HotkeyPrimeCheck, PrimeHotkeyCandidates,
            new[] { _hotkey, _hotkeyHide, _hotkeyView, _hotkeyHeatmap }, v => _config.HotkeyPrimeCheck = v);
        ApplyAppearance(_config);

        _poll = new PollService(_config);
        _poll.SnapshotReceived += OnSnapshot;
        _poll.PollFailed += OnPollFailed;
        _poll.FriendsChanged += OnFriendsRoster;
        _poll.PrimeCheckStarted += () => _primeBefore = _config.Prime; // PollService swaps in the new result before PrimeChecked
        _poll.PrimeChecked += result =>
        {
            if (result.Outcome == PrimeCheckOutcome.Ok && result.Snapshot is { } fresh)
            {
                PostMine(SelfActivity.PrimeLines(_primeBefore, fresh, DateTime.UtcNow));
            }
        };

        _tray = new TrayIcon(
            toggleEditMode: ToggleEditMode,
            toggleOverlay: ToggleOverlayVisibility,
            openSettings: OpenSettings,
            exit: () => Application.Current.Shutdown());
        UpdateHotkeyTexts();

        Loaded += (_, _) =>
        {
            _ = CheckForUpdateAsync();
            if (!_config.StatsEnabled) Hide();
            if (_config.MinimapEnabled) ShowMinimap();
            if (_config.PrimeEnabled) ShowPrime();
            if (_config.ActivityEnabled) ShowActivity(); // after Prime: its first placement docks under it

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
    private void OpenSettings()
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
        try
        {
            var dialog = new SettingsWindow(_config, _library, _book, _poll.Friends) { Topmost = true };
            var saved = dialog.ShowDialog() == true;

            if (!saved)
            {
                if (string.IsNullOrWhiteSpace(_config.GetCookie())) ShowNoCookieState();
                return;
            }
            if (dialog.CookieChanged)
            {
                DinoText.Text = "Connecting…";
                StatusText.Text = "";
                SetAttention(true);
                _poll.RebuildClient();
            }
            if (dialog.MinimapChanged || dialog.AppearanceChanged || dialog.FriendsChanged) _minimap?.ApplySettings();
            if (dialog.AppearanceChanged)
            {
                ApplyAppearance(_config);
                RenderTimeLeft(); // the time-left checkbox rides the Appearance flag
                _prime?.ApplySettingsFromConfig();
                _controlPanel?.ApplySettingsFromConfig();
            }
            if (dialog.AppearanceChanged || dialog.ActivityChanged || dialog.FriendsChanged) _activity?.ApplySettingsFromConfig();
            if (dialog.FriendsChanged) _ = _poll.RefreshFriendsAsync(); // the minimap layer may have been switched on or off
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
        StatusText.Text = $"Update available: {tag}";
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
        StatusText.Text = "Open Settings from the tray icon to connect your account";
        SetAttention(true);
    }

    /// <summary>The stats panel takes part in the attention fade; its verdict comes from StatsAttention.</summary>
    protected override bool Fades => true;

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
        StatusText.Text = $"Disconnected · retrying ({ex.GetType().Name})";
        _tray.SetStatus("Pandora Overlay — disconnected");
        SetAttention(true); // a broken connection is worth eyes
    }

    /// <summary>
    /// The friends roster, right after your own snapshot: the book mirrors it
    /// first (so new friends have their colour and name on first sight), then
    /// the feed diffs it and its lines go to the log. Null = cleared: the
    /// feed forgets its baseline so the roster's return seeds again.
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
        var fresh = _feed.Update(roster, _me, (id, site) => _book.DisplayName(id, site), _book.Notifies, now);
        _log.Post(fresh);
        // The friend-spawn chime is decided here, like the stats chimes, so it
        // plays whether or not the Activity widget is on screen.
        if (_config.FriendsChimeEnabled && fresh.Any(l => l.Kind is FeedKind.Spawned or FeedKind.NewLife)) Chime();
    }

    /// <summary>Your own events go to the log only when the feed is set to include them.</summary>
    private void PostMine(IReadOnlyList<FeedLine> lines)
    {
        if (_config.ActivityIncludeMine) _log.Post(lines);
    }

    private void PostMine(FeedLine line)
    {
        if (_config.ActivityIncludeMine) _log.Post(line);
    }

    private void OnSnapshot(MyLocationResponse result)
    {
        _me = result.InGame ? result.Player : null;
        UpdateUi(result);
        PostMine(_self.Update(result, _config.ActivityDamageLines, DateTime.UtcNow));
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

    // ---- Rendering ----------------------------------------------------------
    private void UpdateUi(MyLocationResponse result)
    {
        if (!result.InGame || result.Player is null)
        {
            _growth.Reset(); // a wall-clock gap would flatten the measured slope
            _hungerDrain.Reset();
            _thirstDrain.Reset();
            _attention.Reset();
            _lowStat.Reset();
            _milestones.Reset();
            SetAttention(false); // "Not in-game" is nothing to watch
            RenderTimeLeft();
            DinoText.Text = "Not in-game";
            GrowthText.Text = "";
            GrowthText.Foreground = GrowthNormal;
            SetBar(HealthFill, HealthPct, 0);
            SetBar(StaminaFill, StaminaPct, 0);
            SetBar(HungerFill, HungerPct, 0);
            SetBar(ThirstFill, ThirstPct, 0);
            SetPulse(HealthFill, false);
            SetPulse(HungerFill, false);
            SetPulse(ThirstFill, false);
            FractureRow.Visibility = Visibility.Collapsed;
            // Not in-game the poll idles; say so, or a slow reaction to a spawn reads as frozen.
            StatusText.Text = _poll.IsIdling
                ? $"Connected · checking every {_poll.Interval.TotalSeconds:0}s · {DateTime.Now:HH:mm:ss}"
                : $"Connected · waiting for spawn · {DateTime.Now:HH:mm:ss}";
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

        SetBar(HealthFill, HealthPct, p.Health);
        HealthFill.Background = p.Health switch
        {
            < 0.25 => HealthCrit,
            < 0.50 => HealthWarn,
            _ => HealthGood
        };
        SetBar(StaminaFill, StaminaPct, p.Stamina);
        SetBar(HungerFill, HungerPct, p.Hunger);
        SetBar(ThirstFill, ThirstPct, p.Thirst);
        _hungerDrain.Add(p);
        _thirstDrain.Add(p);
        RenderTimeLeft();

        // Alerts: the rules are pure and always run; the sounds are opt-in,
        // and each moment also becomes a line in the Activity feed.
        var now = DateTime.UtcNow;
        if (_milestones.Update(p) is { } stage)
        {
            PulseBriefly(GrowthText, HeaderPulse); // a stage reached is always worth a blink
            _attention.NoteEvent();
            if (_config.GrowthChimeEnabled) Chime();
            PostMine(SelfActivity.GrowthLine(stage, now));
        }
        if (_lowStat.Update(p))
        {
            if (_config.LowStatChimeEnabled) Chime();
            if (_lowStat.HungerFired) PostMine(SelfActivity.LowStatLine("Hunger", _hungerDrain.Label, now));
            if (_lowStat.ThirstFired) PostMine(SelfActivity.LowStatLine("Thirst", _thirstDrain.Label, now));
        }

        SetAttention(_attention.Update(p, _hungerDrain.TimeLeft, _thirstDrain.TimeLeft));

        // Critical-stat pulse. Stamina is deliberately excluded — it drains to
        // zero every sprint by design and would train the eye to ignore it.
        SetPulse(HealthFill, p.Health < 0.25);
        SetPulse(HungerFill, p.Hunger < 0.25);
        SetPulse(ThirstFill, p.Thirst < 0.25);

        FracHead.Visibility = p.HeadFractured ? Visibility.Visible : Visibility.Collapsed;
        FracBody.Visibility = p.BodyFractured ? Visibility.Visible : Visibility.Collapsed;
        FracLegs.Visibility = p.LegsFractured ? Visibility.Visible : Visibility.Collapsed;
        FractureRow.Visibility = (p.HeadFractured || p.BodyFractured || p.LegsFractured)
            ? Visibility.Visible
            : Visibility.Collapsed;

        StatusText.Text = $"Live · updated {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>The time-left labels on the hunger/thirst bars; the trackers run either way, so the Settings checkbox applies at once.</summary>
    private void RenderTimeLeft()
    {
        SetTimeLeft(HungerLeft, _hungerDrain, HungerFill.Width, HungerTint);
        SetTimeLeft(ThirstLeft, _thirstDrain, ThirstFill.Width, ThirstTint);
    }

    /// <summary>
    /// A bar-chart data label: it rides just past the fill's tip in the bar's
    /// own (lightened) colour, so it reads as part of that bar rather than a
    /// second number next to the percent. With no room left on the track it
    /// flips inside the fill's end, dark on the bright colour.
    /// </summary>
    private void SetTimeLeft(System.Windows.Controls.TextBlock label, DrainTracker tracker, double fillWidth, Brush tint)
    {
        var text = _config.StatTimeLeftEnabled ? tracker.Label : null;
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
