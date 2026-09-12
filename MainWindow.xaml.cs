using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

public partial class MainWindow : Window
{
    // ---- Layout constants -------------------------------------------------
    private const double TrackWidth = 170; // must match bar track width in XAML

    // ---- Win32 interop ----------------------------------------------------
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020; // clicks fall through to the game
    private const long WS_EX_TOOLWINDOW = 0x00000080;  // no Alt-Tab entry
    private const long WS_EX_NOACTIVATE = 0x08000000;  // never steals focus

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA11C;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_F8 = 0x77;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- Brushes ----------------------------------------------------------
    private static readonly Brush HealthGood = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush HealthWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush HealthCrit = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush BorderLocked = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BorderEdit = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));

    // ---- State ------------------------------------------------------------
    private readonly OverlayConfig _config;
    private PandoraClient _client;
    private readonly DispatcherTimer _timer;
    private bool _editMode;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _config = OverlayConfig.Load();
        Left = _config.WindowX;
        Top = _config.WindowY;

        _client = new PandoraClient(_config.GetCookie(), _config.UserAgent);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(2, _config.PollIntervalSeconds))
        };
        _timer.Tick += async (_, _) => await PollAsync();

        Loaded += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_config.GetCookie()))
            {
                ShowNoCookieState();
                OpenSettings(); // first run: walk the user through setup
                return;
            }
            _timer.Start();
            await PollAsync();
        };

        Closed += (_, _) =>
        {
            _timer.Stop();
            PersistState();
            _client.Dispose();
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
        RebuildClient();
    }

    private void RebuildClient()
    {
        _client.Dispose();
        _client = new PandoraClient(_config.GetCookie(), _config.UserAgent);
        DinoText.Text = "Connecting…";
        StatusText.Text = "";
        if (!_timer.IsEnabled) _timer.Start();
        _ = PollAsync();
    }

    private void ShowNoCookieState()
    {
        DinoText.Text = "Not set up yet";
        StatusText.Text = "Press Ctrl+F8, then click \u2699 to connect your account";
    }

    // ---- Window setup -----------------------------------------------------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        ApplyClickThrough(hwnd, clickThrough: true);
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

    private void ApplyClickThrough(IntPtr hwnd, bool clickThrough)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW;
        if (clickThrough)
        {
            style |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        }
        else
        {
            style &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        }
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    // ---- Edit mode ----------------------------------------------------------
    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        var hwnd = new WindowInteropHelper(this).Handle;

        ApplyClickThrough(hwnd, clickThrough: !_editMode);
        EditBanner.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
        RootPanel.BorderBrush = _editMode ? BorderEdit : BorderLocked;

        if (!_editMode)
        {
            PersistState();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void PersistState()
    {
        _config.WindowX = Left;
        _config.WindowY = Top;
        if (!string.IsNullOrWhiteSpace(_client.CurrentCookie))
        {
            _config.SetCookie(_client.CurrentCookie); // re-encrypt the rolled connect.sid
        }
        _config.Save();
    }

    // ---- Polling ------------------------------------------------------------
    private async Task PollAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var result = await _client.FetchAsync();
            UpdateUi(result);

            // Cheap re-assert in case the game reshuffles the z-order.
            if (!_editMode && !Topmost)
            {
                Topmost = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Disconnected · retrying ({ex.GetType().Name})";
        }
        finally
        {
            _busy = false;
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
        var symbol = gender.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "\u2642"
                   : gender.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "\u2640"
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
