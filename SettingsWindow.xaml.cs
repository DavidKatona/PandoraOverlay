using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// Sectioned settings dialog: Account (cookie — an empty box means "keep the
/// current one"), Controls (edit-mode hotkey with capture + availability
/// check), General (run at Windows startup), Minimap (view mode + centered
/// zoom, the same persisted values the in-overlay gestures write). Save
/// applies config + registry; MainWindow reads the *Changed flags afterwards
/// to hot-apply cookie/hotkey/minimap without a restart. First run keeps the
/// old behaviour: Save stays disabled until a valid cookie is pasted.
/// </summary>
public partial class SettingsWindow : Window
{
    private const int TestHotkeyId = 0xA11D; // distinct from MainWindow's real registration

    private static readonly Brush HintNeutral = new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush HintGood = new SolidColorBrush(Color.FromRgb(0x7C, 0xC8, 0x84));
    private static readonly Brush HintWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));
    private static readonly Brush HintBad = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));

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

    /// <summary>True after Save when a new cookie was pasted (client rebuild needed).</summary>
    public bool CookieChanged { get; private set; }

    /// <summary>True after Save when the hotkey differs (re-registration needed).</summary>
    public bool HotkeyChanged { get; private set; }

    /// <summary>True after Save when minimap view/zoom differ (ApplySettings needed).</summary>
    public bool MinimapChanged { get; private set; }

    /// <summary>True after Save when a scale, the background opacity or the time-left checkbox differ (ApplyAppearance needed).</summary>
    public bool AppearanceChanged { get; private set; }

    public SettingsWindow(OverlayConfig config)
    {
        InitializeComponent();
        _config = config;
        _firstRun = string.IsNullOrWhiteSpace(config.GetCookie());

        // Account
        CookieStatus.Text = _firstRun
            ? "No cookie stored yet — connect your account below."
            : "Cookie stored ✓ — the session rolls forward automatically.";
        CookieStatus.Foreground = _firstRun ? HintWarn : HintGood;
        if (_firstRun) SetHowToVisible(true); // first run: show the walkthrough up front

        // Controls
        _hotkeyEntries[EditHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.Hotkey) ?? HotkeySpec.Default, "edit mode");
        _hotkeyEntries[HideHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyHideAll) ?? new HotkeySpec(ModifierKeys.Control, Key.F4), "hide/show overlay");
        _hotkeyEntries[ViewHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyMinimapView) ?? new HotkeySpec(ModifierKeys.Control, Key.F5), "the minimap view toggle");
        _hotkeyEntries[HeatmapHotkeyBox] = new HotkeyEntry(
            HotkeySpec.TryParse(config.HotkeyHeatmap) ?? new HotkeySpec(ModifierKeys.Control, Key.F6), "the heatmap toggle");
        foreach (var (box, entry) in _hotkeyEntries)
        {
            box.Text = entry.Chosen.ToString();
        }

        // General
        _initialStartup = StartupRegistration.IsEnabled();
        StartupCheck.IsChecked = _initialStartup;
        TimeLeftCheck.IsChecked = config.StatTimeLeftEnabled;
        ScaleSlider.Value = Math.Clamp(config.UiScale, ScaleSlider.Minimum, ScaleSlider.Maximum);
        PrimeScaleSlider.Value = Math.Clamp(config.PrimeScale ?? config.UiScale, PrimeScaleSlider.Minimum, PrimeScaleSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(config.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);

        // Minimap
        var centered = string.Equals(config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        ModeCentered.IsChecked = centered;
        ModeIsland.IsChecked = !centered;
        ZoomSlider.Value = Math.Clamp(config.MinimapZoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        MapSizeSlider.Value = Math.Clamp(config.MinimapSize, MapSizeSlider.Minimum, MapSizeSlider.Maximum);

        Validate();
    }

    // ---- Window chrome ----------------------------------------------------
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---- Account ------------------------------------------------------------
    private void OpenMap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://islapandora.eu/live-map")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            HintText.Text = "Couldn't open the browser — visit islapandora.eu/live-map manually.";
            HintText.Foreground = HintWarn;
        }
    }

    private void HowToLink_Click(object sender, MouseButtonEventArgs e) =>
        SetHowToVisible(HowToText.Visibility != Visibility.Visible);

    private void SetHowToVisible(bool show)
    {
        HowToText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HowToLink.Text = (show ? "▾" : "▸") + " How do I get my cookie?";
    }

    private void CookieBox_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    /// <summary>Fixes the usual paste accidents without changing valid input.</summary>
    internal static string Clean(string raw)
    {
        var s = (raw ?? "").Trim();
        s = Regex.Replace(s, @"^\s*cookie\s*:\s*", "", RegexOptions.IgnoreCase);
        s = s.Trim().Trim('"', '\'').Trim();
        s = Regex.Replace(s, @"\s*[\r\n]+\s*", " ");
        return s.TrimEnd(';', ' ');
    }

    private void Validate()
    {
        var s = Clean(CookieBox.Text);
        var hasSid = s.Contains("connect.sid=");
        var hasCf = s.Contains("cf_clearance=");
        bool ok;

        if (s.Length == 0)
        {
            HintText.Text = _firstRun
                ? "Waiting for a pasted cookie…"
                : "Leave empty to keep the current cookie.";
            HintText.Foreground = HintNeutral;
            ok = !_firstRun;
        }
        else if (hasSid && hasCf)
        {
            HintText.Text = "Looks good ✓ — both session and Cloudflare cookies found.";
            HintText.Foreground = HintGood;
            ok = true;
        }
        else if (hasSid)
        {
            HintText.Text = "cf_clearance is missing. This can still work, but if the overlay " +
                            "gets blocked, go back and copy the WHOLE cookie value.";
            HintText.Foreground = HintWarn;
            ok = true;
        }
        else
        {
            HintText.Text = "connect.sid not found — that doesn't look like the cookie header. " +
                            "Make sure you copy the full value of \"cookie\" under Request Headers.";
            HintText.Foreground = HintBad;
            ok = false;
        }
        SaveButton.IsEnabled = ok;
    }

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

    // ---- Minimap ------------------------------------------------------------
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomLabel != null) ZoomLabel.Text = $"{e.NewValue:0.##}×";
    }

    private void MapSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MapSizeLabel != null) MapSizeLabel.Text = $"{e.NewValue:0} px";
    }

    // ---- Save ---------------------------------------------------------------
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = Clean(CookieBox.Text);
        if (s.Length > 0)
        {
            _config.SetCookie(s);
            CookieChanged = true;
        }

        if (_hotkeyEntries.Any(kv => kv.Value.Chosen != kv.Value.Initial))
        {
            _config.Hotkey = _hotkeyEntries[EditHotkeyBox].Chosen.ToString();
            _config.HotkeyHideAll = _hotkeyEntries[HideHotkeyBox].Chosen.ToString();
            _config.HotkeyMinimapView = _hotkeyEntries[ViewHotkeyBox].Chosen.ToString();
            _config.HotkeyHeatmap = _hotkeyEntries[HeatmapHotkeyBox].Chosen.ToString();
            HotkeyChanged = true;
        }

        var startup = StartupCheck.IsChecked == true;
        if (startup != _initialStartup) StartupRegistration.SetEnabled(startup);

        var scale = Math.Round(ScaleSlider.Value, 2);
        var primeScale = Math.Round(PrimeScaleSlider.Value, 2);
        var bgOpacity = Math.Round(OpacitySlider.Value, 2);
        var timeLeft = TimeLeftCheck.IsChecked == true;
        if (Math.Abs(scale - _config.UiScale) > 0.001 ||
            Math.Abs(primeScale - (_config.PrimeScale ?? _config.UiScale)) > 0.001 ||
            Math.Abs(bgOpacity - _config.BackgroundOpacity) > 0.001 ||
            timeLeft != _config.StatTimeLeftEnabled)
        {
            _config.UiScale = scale;
            _config.PrimeScale = primeScale;
            _config.BackgroundOpacity = bgOpacity;
            _config.StatTimeLeftEnabled = timeLeft;
            AppearanceChanged = true;
        }

        var mode = ModeCentered.IsChecked == true ? "centered" : "island";
        var zoom = Math.Round(ZoomSlider.Value, 2);
        var mapSize = Math.Round(MapSizeSlider.Value);
        if (mode != _config.MinimapMode ||
            Math.Abs(zoom - _config.MinimapZoom) > 0.005 ||
            Math.Abs(mapSize - _config.MinimapSize) > 0.5)
        {
            _config.MinimapMode = mode;
            _config.MinimapZoom = zoom;
            _config.MinimapSize = mapSize;
            MinimapChanged = true;
        }

        _config.Save();
        DialogResult = true;
    }
}
