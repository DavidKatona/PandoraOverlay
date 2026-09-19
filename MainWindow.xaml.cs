using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// The stats panel and the app's orchestrator: owns the config and the shared
/// PollService, registers the global hotkeys, and manages the minimap
/// window's lifetime. Window-style interop lives in OverlayWindowBase.
/// </summary>
public partial class MainWindow : OverlayWindowBase
{
    // ---- Layout constants -------------------------------------------------
    private const double TrackWidth = 170; // must match bar track width in XAML

    // ---- Global hotkeys (registered once, on this window's hwnd) ----------
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA11C;        // edit mode (0xA11D is the settings dialog's test id)
    private const int HideAllHotkeyId = 0xA11E;
    private const int ViewHotkeyId = 0xA11F;
    private const int HeatmapHotkeyId = 0xA120;

    // Tried in order when the heatmap combo collides with one of the other
    // three; four candidates against three takers, so one is always free.
    private static readonly HotkeySpec[] HeatmapHotkeyCandidates =
    {
        new(ModifierKeys.Control, Key.F6),
        new(ModifierKeys.Control, Key.F8),
        new(ModifierKeys.Control, Key.F9),
        new(ModifierKeys.Control, Key.F11)
    };

    // ---- Brushes ----------------------------------------------------------
    private static readonly Brush HealthGood = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush HealthWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush HealthCrit = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush GrowthNormal = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB0));

    // ---- State ------------------------------------------------------------
    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly TrayIcon _tray;
    private readonly GrowthTracker _growth = new();
    private MinimapWindow? _minimap;
    private PrimeWindow? _prime;
    private ControlPanelWindow? _controlPanel;
    private HotkeySpec _hotkey;
    private HotkeySpec _hotkeyHide;
    private HotkeySpec _hotkeyView;
    private HotkeySpec _hotkeyHeatmap;
    private bool _overlayHidden;
    private readonly List<string> _registerFailures = new();

    public MainWindow()
    {
        InitializeComponent();

        _config = OverlayConfig.Load();
        Left = _config.WindowX;
        Top = _config.WindowY;
        _hotkey = HotkeySpec.TryParse(_config.Hotkey) ?? HotkeySpec.Default;
        _hotkeyHide = HotkeySpec.TryParse(_config.HotkeyHideAll) ?? new HotkeySpec(ModifierKeys.Control, Key.F4);
        _hotkeyView = HotkeySpec.TryParse(_config.HotkeyMinimapView) ?? new HotkeySpec(ModifierKeys.Control, Key.F5);
        _hotkeyHeatmap = ResolveHeatmapHotkey();
        ApplyAppearance(_config);

        _poll = new PollService(_config);
        _poll.SnapshotReceived += OnSnapshot;
        _poll.PollFailed += OnPollFailed;

        _tray = new TrayIcon(
            toggleEditMode: ToggleEditMode,
            toggleOverlay: ToggleOverlayVisibility,
            checkPrime: CheckPrime,
            openSettings: OpenSettings,
            exit: () => Application.Current.Shutdown());
        UpdateHotkeyTexts();

        Loaded += (_, _) =>
        {
            _ = CheckForUpdateAsync();
            if (!_config.StatsEnabled) Hide();
            if (_config.MinimapEnabled) ShowMinimap();
            if (_config.PrimeEnabled) ShowPrime();

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
        try
        {
            var dialog = new SettingsWindow(_config) { Topmost = true };
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
                _poll.RebuildClient();
            }
            if (dialog.MinimapChanged || dialog.AppearanceChanged) _minimap?.ApplySettings();
            if (dialog.AppearanceChanged)
            {
                ApplyAppearance(_config);
                _prime?.ApplySettingsFromConfig();
                _controlPanel?.ApplySettingsFromConfig();
            }
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
    /// Re-registers all hotkeys after a settings change. Everything is
    /// unregistered first so swapped combos can't collide with themselves;
    /// the dialog availability-checked each combo, but another app can still
    /// grab one in the meantime — then that hotkey falls back to its old,
    /// still-working combo.
    /// </summary>
    private void ApplyHotkeysFromConfig()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HotkeySpec.Unregister(hwnd, HotkeyId);
        HotkeySpec.Unregister(hwnd, HideAllHotkeyId);
        HotkeySpec.Unregister(hwnd, ViewHotkeyId);
        HotkeySpec.Unregister(hwnd, HeatmapHotkeyId);

        _registerFailures.Clear();
        _hotkey = RegisterWithFallback(hwnd, HotkeyId, _config.Hotkey, _hotkey, v => _config.Hotkey = v);
        _hotkeyHide = RegisterWithFallback(hwnd, HideAllHotkeyId, _config.HotkeyHideAll, _hotkeyHide, v => _config.HotkeyHideAll = v);
        _hotkeyView = RegisterWithFallback(hwnd, ViewHotkeyId, _config.HotkeyMinimapView, _hotkeyView, v => _config.HotkeyMinimapView = v);
        _hotkeyHeatmap = RegisterWithFallback(hwnd, HeatmapHotkeyId, _config.HotkeyHeatmap, _hotkeyHeatmap, v => _config.HotkeyHeatmap = v);
        _config.Save();
        UpdateHotkeyTexts();

        if (_registerFailures.Count == 0) _tray.ClearHotkeyConflict();
        else _tray.ShowHotkeyConflict(string.Join(", ", _registerFailures));
    }

    /// <summary>
    /// The heatmap hotkey arrived after people had already customized the
    /// other three, so its default can collide with one of them — and a
    /// duplicate inside our own app would fail to register and read as "in
    /// use by another app". Then the first free candidate wins and is
    /// written back. (The settings dialog rejects duplicates at capture time,
    /// so this only matters for that upgrade case and hand-edited configs.)
    /// </summary>
    private HotkeySpec ResolveHeatmapHotkey()
    {
        var wanted = HotkeySpec.TryParse(_config.HotkeyHeatmap) ?? HeatmapHotkeyCandidates[0];
        var taken = new[] { _hotkey, _hotkeyHide, _hotkeyView };
        if (!taken.Contains(wanted)) return wanted;

        var free = HeatmapHotkeyCandidates.First(c => !taken.Contains(c));
        _config.HotkeyHeatmap = free.ToString();
        return free;
    }

    private HotkeySpec RegisterWithFallback(IntPtr hwnd, int id, string configured, HotkeySpec fallback, Action<string> writeBack)
    {
        var wanted = HotkeySpec.TryParse(configured) ?? fallback;
        if (HotkeySpec.Register(hwnd, id, wanted)) return wanted;

        _registerFailures.Add(wanted.ToString());
        HotkeySpec.Register(hwnd, id, fallback);
        writeBack(fallback.ToString());
        StatusText.Text = $"Hotkey {wanted} unavailable — keeping {fallback}";
        return fallback;
    }

    private void UpdateHotkeyTexts()
    {
        _controlPanel?.SetHotkeyLabel(_hotkey.ToString());
        _tray.UpdateHotkeyLabels(_hotkey.ToString(), _hotkeyHide.ToString());
    }

    private void ShowNoCookieState()
    {
        DinoText.Text = "Not set up yet";
        StatusText.Text = "Open Settings from the tray icon to connect your account";
    }

    // ---- Widget visibility -------------------------------------------------

    /// <summary>Control panel / tray: hides or shows the stats panel itself — the app keeps running via tray + hotkeys.</summary>
    private void ToggleStats()
    {
        _config.StatsEnabled = !_config.StatsEnabled;
        if (_overlayHidden) return; // takes effect when the overlay is shown again
        if (_config.StatsEnabled) Show(); else Hide();
    }

    /// <summary>
    /// Heatmap hotkey / control panel: flips the minimap's heatmap layer
    /// (persisted with the next Save). Ignored while the minimap is hidden —
    /// flipping an invisible layer would only surprise whoever shows the map
    /// again later.
    /// </summary>
    private void ToggleHeatmap()
    {
        if (_minimap is null || _overlayHidden) return;
        _config.HeatmapEnabled = !_config.HeatmapEnabled;
        _ = _poll.RefreshHeatmapAsync(); // delivers fresh bytes, or null to clear the layer
    }

    private void Minimap_Click(object sender, RoutedEventArgs e) => ToggleMinimap();

    private void ToggleMinimap()
    {
        if (_minimap is null)
        {
            ShowMinimap();
        }
        else
        {
            _config.MinimapEnabled = false;
            _minimap.Close();
        }
    }

    private void ShowMinimap()
    {
        if (_minimap is null)
        {
            _minimap = new MinimapWindow(_config, _poll);
            _minimap.Closed += (_, _) => _minimap = null;
            _minimap.Show();
        }
        _config.MinimapEnabled = true;
        _ = _poll.RefreshHeatmapAsync(); // a fresh window starts without the layer
        if (EditMode) _minimap.SetEditMode(true);
    }

    private void TogglePrime()
    {
        if (_prime is null)
        {
            ShowPrime();
        }
        else
        {
            _config.PrimeEnabled = false;
            _config.PrimeX = _prime.Left; // a hidden widget comes back where it was
            _config.PrimeY = _prime.Top;
            _prime.Close();
        }
    }

    private void ShowPrime()
    {
        if (_prime is null)
        {
            _prime = new PrimeWindow(_config, _poll);
            _prime.Closed += (_, _) => _prime = null;
            _prime.Show();
        }
        _config.PrimeEnabled = true;
        if (EditMode) _prime.SetEditMode(true);
    }

    /// <summary>
    /// Control panel / tray: the ONLY trigger for a prime check — user-clicked,
    /// never timed (the endpoint is approved on that condition). Asking for a
    /// check implies wanting to see the answer, so the widget is shown first.
    /// </summary>
    private void CheckPrime()
    {
        if (_overlayHidden) ToggleOverlayVisibility();
        ShowPrime();
        _ = _poll.CheckPrimeAsync();
    }

    // ---- Window setup ------------------------------------------------------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); // applies the click-through styles
        var hwnd = new WindowInteropHelper(this).Handle;

        // Surface registration failures instead of swallowing them: another
        // app holding a combo at our launch would otherwise leave that hotkey
        // silently dead for the whole session.
        _registerFailures.Clear();
        if (!HotkeySpec.Register(hwnd, HotkeyId, _hotkey)) _registerFailures.Add(_hotkey.ToString());
        if (!HotkeySpec.Register(hwnd, HideAllHotkeyId, _hotkeyHide)) _registerFailures.Add(_hotkeyHide.ToString());
        if (!HotkeySpec.Register(hwnd, ViewHotkeyId, _hotkeyView)) _registerFailures.Add(_hotkeyView.ToString());
        if (!HotkeySpec.Register(hwnd, HeatmapHotkeyId, _hotkeyHeatmap)) _registerFailures.Add(_hotkeyHeatmap.ToString());
        if (_registerFailures.Count > 0)
        {
            var combos = string.Join(", ", _registerFailures);
            StatusText.Text = $"Hotkey {combos} in use by another app — rebind in Settings";
            _tray.ShowHotkeyConflict(combos);
        }

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case HotkeyId:
                    ToggleEditMode();
                    handled = true;
                    break;
                case HideAllHotkeyId:
                    ToggleOverlayVisibility();
                    handled = true;
                    break;
                case ViewHotkeyId:
                    _minimap?.ToggleView();
                    handled = true;
                    break;
                case HeatmapHotkeyId:
                    ToggleHeatmap();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Hide-all hotkey / tray: hides both windows for screenshots or cutscenes.
    /// Polling continues (the state stays warm); hidden is never persisted —
    /// the app always starts visible.
    /// </summary>
    private void ToggleOverlayVisibility()
    {
        if (!_overlayHidden)
        {
            if (EditMode) ToggleEditMode(); // lock + persist before vanishing
            _overlayHidden = true;
            Hide();
            _minimap?.Hide();
            _prime?.Hide();
        }
        else
        {
            _overlayHidden = false;
            if (_config.StatsEnabled) Show(); // a deliberately hidden stats panel stays hidden
            _minimap?.Show();
            _prime?.Show();
        }
    }

    // ---- Edit mode ----------------------------------------------------------
    private void ToggleEditMode()
    {
        if (_overlayHidden) ToggleOverlayVisibility(); // un-hide first, then edit as usual

        var on = !EditMode;
        SetEditMode(on);
        _minimap?.SetEditMode(on);
        _prime?.SetEditMode(on);

        if (on)
        {
            ShowControlPanel();
        }
        else
        {
            _controlPanel?.Hide();
            PersistState();
        }
    }

    private void ShowControlPanel()
    {
        if (_controlPanel is null)
        {
            _controlPanel = new ControlPanelWindow(
                _config,
                openSettings: OpenSettings,
                toggleStats: ToggleStats,
                toggleMinimap: ToggleMinimap,
                toggleMinimapView: () => _minimap?.ToggleView(),
                toggleHeatmap: ToggleHeatmap,
                togglePrime: TogglePrime,
                checkPrime: CheckPrime,
                lockOverlay: ToggleEditMode,
                exit: () => Application.Current.Shutdown());
        }
        _controlPanel.Show();
        _controlPanel.SetEditMode(true); // permanently interactive while visible
        _controlPanel.SetHotkeyLabel(_hotkey.ToString());

        // Take focus away from the game so it releases its mouse capture and
        // the cursor becomes visible. Windows grants us foreground rights
        // here because our registered hotkey (or a tray click) triggered
        // this; only OUR window is activated — the game is never touched.
        _controlPanel.Activate();
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

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
    }

    private void OnSnapshot(MyLocationResponse result)
    {
        UpdateUi(result);
        _tray.SetStatus(result.InGame && result.Player is { } p
            ? $"Pandora Overlay — {p.Dino} · HP {p.Health * 100:0}% · Growth {p.Growth * 100:0.#}%"
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
            StatusText.Text = $"Connected · waiting for spawn · {DateTime.Now:HH:mm:ss}";
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
