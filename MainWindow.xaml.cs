using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

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
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_F8 = 0x77;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- Brushes ----------------------------------------------------------
    private static readonly Brush HealthGood = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush HealthWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush HealthCrit = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    // ---- State ------------------------------------------------------------
    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly TrayIcon _tray;
    private MinimapWindow? _minimap;

    public MainWindow()
    {
        InitializeComponent();

        _config = OverlayConfig.Load();
        Left = _config.WindowX;
        Top = _config.WindowY;

        _poll = new PollService(_config);
        _poll.SnapshotReceived += OnSnapshot;
        _poll.PollFailed += OnPollFailed;

        _tray = new TrayIcon(
            toggleEditMode: ToggleEditMode,
            toggleMinimap: ToggleMinimap,
            openSettings: OpenSettings,
            exit: () => Application.Current.Shutdown());

        Loaded += (_, _) =>
        {
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
        DinoText.Text = "Connecting…";
        StatusText.Text = "";
        _poll.RebuildClient();
    }

    private void ShowNoCookieState()
    {
        DinoText.Text = "Not set up yet";
        StatusText.Text = "Press Ctrl+F8, then click ⚙ to connect your account";
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
        RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL, VK_F8);
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
    }

    private void OnSnapshot(MyLocationResponse result)
    {
        UpdateUi(result);

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
            DinoText.Text = "Not in-game";
            GrowthText.Text = "";
            SetBar(HealthFill, HealthPct, 0);
            SetBar(StaminaFill, StaminaPct, 0);
            SetBar(HungerFill, HungerPct, 0);
            SetBar(ThirstFill, ThirstPct, 0);
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
        GrowthText.Text = $"Growth {p.Growth * 100:0.#}%";

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

        FracHead.Visibility = p.HeadFractured ? Visibility.Visible : Visibility.Collapsed;
        FracBody.Visibility = p.BodyFractured ? Visibility.Visible : Visibility.Collapsed;
        FracLegs.Visibility = p.LegsFractured ? Visibility.Visible : Visibility.Collapsed;
        FractureRow.Visibility = (p.HeadFractured || p.BodyFractured || p.LegsFractured)
            ? Visibility.Visible
            : Visibility.Collapsed;

        StatusText.Text = $"Live · updated {DateTime.Now:HH:mm:ss}";
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
