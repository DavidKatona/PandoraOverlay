using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// The stats panel and the app's orchestrator: owns the config and the shared
/// PollService, registers the global Ctrl+F8 hotkey, and manages the minimap
/// window's lifetime. Window-style interop lives in OverlayWindowBase.
/// </summary>
public partial class MainWindow : OverlayWindowBase
{
    // ---- Layout constants -------------------------------------------------
    private const double TrackWidth = 170; // must match bar track width in XAML

    // ---- Global hotkey (registered once, toggles every overlay window) ----
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA11C;

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
    private HotkeySpec _hotkey;

    public MainWindow()
    {
        InitializeComponent();

        _config = OverlayConfig.Load();
        Left = _config.WindowX;
        Top = _config.WindowY;
        _hotkey = HotkeySpec.TryParse(_config.Hotkey) ?? HotkeySpec.Default;
        ApplyAppearance(_config);

        _poll = new PollService(_config);
        _poll.SnapshotReceived += OnSnapshot;
        _poll.PollFailed += OnPollFailed;

        _tray = new TrayIcon(
            toggleEditMode: ToggleEditMode,
            toggleMinimap: ToggleMinimap,
            openSettings: OpenSettings,
            exit: () => Application.Current.Shutdown());
        UpdateHotkeyTexts();

        Loaded += (_, _) =>
        {
            _ = CheckForUpdateAsync();
            if (_config.MinimapEnabled) ShowMinimap();

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
        };
    }

    // ---- Settings flow ----------------------------------------------------
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
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
        if (dialog.HotkeyChanged) ApplyHotkeyFromConfig();
        if (dialog.MinimapChanged || dialog.AppearanceChanged) _minimap?.ApplySettings();
        if (dialog.AppearanceChanged) ApplyAppearance(_config);
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
    /// Re-registers the hotkey after a settings change. The dialog already
    /// availability-checked the combo, but another app can grab it in the
    /// meantime — then we fall back to the old, still-working one.
    /// </summary>
    private void ApplyHotkeyFromConfig()
    {
        var spec = HotkeySpec.TryParse(_config.Hotkey) ?? HotkeySpec.Default;
        if (spec == _hotkey) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        HotkeySpec.Unregister(hwnd, HotkeyId);
        if (!HotkeySpec.Register(hwnd, HotkeyId, spec))
        {
            HotkeySpec.Register(hwnd, HotkeyId, _hotkey);
            _config.Hotkey = _hotkey.ToString();
            _config.Save();
            StatusText.Text = $"Hotkey {spec} unavailable — keeping {_hotkey}";
            return;
        }
        _hotkey = spec;
        UpdateHotkeyTexts();
    }

    private void UpdateHotkeyTexts()
    {
        EditBannerText.Text = $"EDIT MODE — drag to move · {_hotkey} to lock";
        _tray.UpdateHotkeyLabel(_hotkey.ToString());
    }

    private void ShowNoCookieState()
    {
        DinoText.Text = "Not set up yet";
        StatusText.Text = $"Press {_hotkey}, then click ⚙ to connect your account";
    }

    // ---- Minimap ----------------------------------------------------------
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
        if (EditMode) _minimap.SetEditMode(true);
    }

    // ---- Window setup ------------------------------------------------------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); // applies the click-through styles
        var hwnd = new WindowInteropHelper(this).Handle;
        HotkeySpec.Register(hwnd, HotkeyId, _hotkey);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleEditMode();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---- Edit mode ----------------------------------------------------------
    private void ToggleEditMode()
    {
        var on = !EditMode;
        SetEditMode(on);
        _minimap?.SetEditMode(on);

        if (!on)
        {
            PersistState();
        }
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        EditBanner.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void PersistState()
    {
        _config.WindowX = Left;
        _config.WindowY = Top;
        if (_minimap is not null)
        {
            _config.MinimapX = _minimap.Left;
            _config.MinimapY = _minimap.Top;
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
