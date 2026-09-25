using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

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

    /// <summary>True after Save when a new cookie was pasted (client rebuild needed).</summary>
    public bool CookieChanged { get; private set; }

    /// <summary>True after Save when the hotkey differs (re-registration needed).</summary>
    public bool HotkeyChanged { get; private set; }

    /// <summary>True after Save when minimap view/zoom/size, the trail length or the scale bar differ (ApplySettings needed).</summary>
    public bool MinimapChanged { get; private set; }

    /// <summary>True after Save when a scale, the background opacity or the time-left checkbox differ (ApplyAppearance needed).</summary>
    public bool AppearanceChanged { get; private set; }

    public SettingsWindow(OverlayConfig config, WaypointLibrary library)
    {
        InitializeComponent();
        _config = config;
        _library = library;
        _draft = library.Clone();
        _draftTracked = config.TrackedWaypointId;
        _firstRun = string.IsNullOrWhiteSpace(config.GetCookie());

        // Account
        CookieStatus.Text = _firstRun
            ? "No cookie stored yet — connect your account below."
            : "Cookie stored ✓ — the session rolls forward automatically.";
        CookieStatus.Foreground = _firstRun ? HintWarn : HintGood;
        if (_firstRun) SetHowToVisible(true); // first run: show the walkthrough up front
        // Once a cookie is stored the paste box is the exception: folded
        // behind "Replace cookie…" so the everyday dialog stays short.
        CookiePanel.Visibility = _firstRun ? Visibility.Visible : Visibility.Collapsed;
        ReplaceLink.Visibility = _firstRun ? Visibility.Collapsed : Visibility.Visible;

        // Safety net for small screens: the sections scroll rather than the
        // dialog running off the bottom (SizeToContent honours MaxHeight).
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;

        // Controls
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
        foreach (var (box, entry) in _hotkeyEntries)
        {
            box.Text = entry.Chosen.ToString();
        }

        // General
        _initialStartup = StartupRegistration.IsEnabled();
        StartupCheck.IsChecked = _initialStartup;
        TimeLeftCheck.IsChecked = config.StatTimeLeftEnabled;
        HideNotInGameCheck.IsChecked = config.HideWhenNotInGame;
        LowStatChimeCheck.IsChecked = config.LowStatChimeEnabled;
        GrowthChimeCheck.IsChecked = config.GrowthChimeEnabled;
        ScaleSlider.Value = Math.Clamp(config.UiScale, ScaleSlider.Minimum, ScaleSlider.Maximum);
        PrimeScaleSlider.Value = Math.Clamp(config.PrimeScale ?? config.UiScale, PrimeScaleSlider.Minimum, PrimeScaleSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(config.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        FadeCheck.IsChecked = config.FadeEnabled;
        FadeSlider.Value = Math.Clamp(config.FadeIdleOpacity, FadeSlider.Minimum, FadeSlider.Maximum);

        // Minimap
        var centered = string.Equals(config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        ModeCentered.IsChecked = centered;
        ModeIsland.IsChecked = !centered;
        ZoomSlider.Value = Math.Clamp(config.MinimapZoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        MapSizeSlider.Value = Math.Clamp(config.MinimapSize, MapSizeSlider.Minimum, MapSizeSlider.Maximum);
        // A hand-edited in-between value shows as the next option up.
        var trailRadio = config.MinimapTrailMinutes switch { <= 0 => TrailOff, <= 10 => Trail10, <= 30 => Trail30, _ => Trail60 };
        trailRadio.IsChecked = true;
        ScaleBarCheck.IsChecked = config.MinimapScaleBarEnabled;
        SpeedCheck.IsChecked = config.MinimapSpeedEnabled;
        var visibilityRadio = config.WaypointVisibility switch { "tracked" => WpTracked, "nearest" => WpNearest, _ => WpAll };
        visibilityRadio.IsChecked = true;

        // Waypoints
        BuildWaypointRows();

        Validate();
        SetPage(_firstRun ? "Account" : _lastPage);
    }

    // ---- Waypoints page -----------------------------------------------------
    private readonly WaypointLibrary _library;
    private readonly List<Waypoint> _draft;   // edited in place; committed on Save, dropped on Cancel
    private Guid? _draftTracked;
    private bool _waypointsDirty;
    private bool _deleteAllArmed;

    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();

    /// <summary>One row per draft entry: colour dot (click cycles the palette), name box, Show, Track, delete.</summary>
    private void BuildWaypointRows()
    {
        WaypointRows.Children.Clear();
        WaypointCount.Text = $"{_draft.Count} / {WaypointLibrary.Capacity}";
        DeleteAllButton.Content = "Delete all";
        DeleteAllButton.IsEnabled = _draft.Count > 0;
        _deleteAllArmed = false;

        if (_draft.Count == 0)
        {
            WaypointRows.Children.Add(new TextBlock
            {
                Text = "No waypoints yet.",
                Foreground = HintNeutral, FontSize = 12, Margin = new Thickness(0, 6, 0, 0)
            });
            return;
        }

        foreach (var wp in _draft)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            var dot = new Ellipse
            {
                Width = 12, Height = 12, Fill = PaletteBrushes[WaypointPalette.Wrap(wp.Colour)],
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = $"{WaypointPalette.Colours[WaypointPalette.Wrap(wp.Colour)].Name} — click to change"
            };
            dot.MouseLeftButtonDown += (_, _) =>
            {
                wp.Colour = WaypointPalette.Wrap(wp.Colour + 1);
                dot.Fill = PaletteBrushes[wp.Colour];
                dot.ToolTip = $"{WaypointPalette.Colours[wp.Colour].Name} — click to change";
                _waypointsDirty = true;
            };
            row.Children.Add(dot);

            var name = new TextBox { Text = wp.Name, Style = (Style)FindResource("NameBox"), Margin = new Thickness(0, 0, 8, 0) };
            name.TextChanged += (_, _) =>
            {
                wp.Name = name.Text;
                _waypointsDirty = true;
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var show = new CheckBox { IsChecked = wp.Visible, Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center };
            show.Click += (_, _) =>
            {
                wp.Visible = show.IsChecked == true;
                _waypointsDirty = true;
            };
            Grid.SetColumn(show, 2);
            row.Children.Add(show);

            var track = new RadioButton
            {
                GroupName = "TrackWaypoint", IsChecked = wp.Id == _draftTracked,
                Style = (Style)FindResource("Radio"), HorizontalAlignment = HorizontalAlignment.Center
            };
            track.Click += (_, _) =>
            {
                // A radio can't be un-clicked, so clicking the tracked one untracks.
                if (_draftTracked == wp.Id)
                {
                    _draftTracked = null;
                    track.IsChecked = false;
                }
                else
                {
                    _draftTracked = wp.Id;
                }
                _waypointsDirty = true;
            };
            Grid.SetColumn(track, 3);
            row.Children.Add(track);

            var delete = new Button
            {
                Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 10,
                Background = new SolidColorBrush(Color.FromRgb(0x55, 0x2B, 0x2B)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0xDD)), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Delete"
            };
            delete.Click += (_, _) =>
            {
                _draft.Remove(wp);
                if (_draftTracked == wp.Id) _draftTracked = null;
                _waypointsDirty = true;
                BuildWaypointRows();
            };
            Grid.SetColumn(delete, 4);
            row.Children.Add(delete);

            WaypointRows.Children.Add(row);
        }
    }

    /// <summary>First click arms ("Really delete all?"), second click deletes — no modal box on a game overlay.</summary>
    private void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_deleteAllArmed)
        {
            _deleteAllArmed = true;
            DeleteAllButton.Content = "Really delete all?";
            return;
        }
        _draft.Clear();
        _draftTracked = null;
        _waypointsDirty = true;
        BuildWaypointRows();
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
            SetHint("Couldn't open the browser — visit islapandora.eu/live-map manually.", HintWarn);
        }
    }

    private void HowToLink_Click(object sender, MouseButtonEventArgs e) =>
        SetHowToVisible(HowToText.Visibility != Visibility.Visible);

    private void ReplaceLink_Click(object sender, MouseButtonEventArgs e)
    {
        var show = CookiePanel.Visibility != Visibility.Visible;
        CookiePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ReplaceLink.Text = (show ? "▾" : "▸") + " Replace cookie…";
        if (!show) CookieBox.Text = ""; // folding it away must not save a hidden paste
        if (show) CookieBox.Focus();
    }

    /// <summary>The hint line under the Account section; an empty text takes no room.</summary>
    private void SetHint(string text, Brush brush)
    {
        HintText.Text = text;
        HintText.Foreground = brush;
        HintText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

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
            // With a cookie stored, an empty box needs no commentary — the
            // box is folded away most of the time anyway.
            SetHint(_firstRun ? "Waiting for a pasted cookie…" : "", HintNeutral);
            ok = !_firstRun;
        }
        else if (hasSid && hasCf)
        {
            SetHint("Looks good ✓ — both session and Cloudflare cookies found.", HintGood);
            ok = true;
        }
        else if (hasSid)
        {
            SetHint("cf_clearance is missing. This can still work, but if the overlay " +
                    "gets blocked, go back and copy the WHOLE cookie value.", HintWarn);
            ok = true;
        }
        else
        {
            SetHint("connect.sid not found — that doesn't look like the cookie header. " +
                    "Make sure you copy the full value of \"cookie\" under Request Headers.", HintBad);
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

    private void FadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeLabel != null) FadeLabel.Text = $"{e.NewValue:0%}";
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
            _config.HotkeyPrimeCheck = _hotkeyEntries[PrimeHotkeyBox].Chosen.ToString();
            HotkeyChanged = true;
        }

        var startup = StartupCheck.IsChecked == true;
        if (startup != _initialStartup) StartupRegistration.SetEnabled(startup);
        _config.HideWhenNotInGame = HideNotInGameCheck.IsChecked == true; // MainWindow reads these live, no flag needed
        _config.LowStatChimeEnabled = LowStatChimeCheck.IsChecked == true;
        _config.GrowthChimeEnabled = GrowthChimeCheck.IsChecked == true;

        var scale = Math.Round(ScaleSlider.Value, 2);
        var primeScale = Math.Round(PrimeScaleSlider.Value, 2);
        var bgOpacity = Math.Round(OpacitySlider.Value, 2);
        var timeLeft = TimeLeftCheck.IsChecked == true;
        var fade = FadeCheck.IsChecked == true;
        var fadeOpacity = Math.Round(FadeSlider.Value, 2);
        if (Math.Abs(scale - _config.UiScale) > 0.001 ||
            Math.Abs(primeScale - (_config.PrimeScale ?? _config.UiScale)) > 0.001 ||
            Math.Abs(bgOpacity - _config.BackgroundOpacity) > 0.001 ||
            timeLeft != _config.StatTimeLeftEnabled ||
            fade != _config.FadeEnabled ||
            Math.Abs(fadeOpacity - _config.FadeIdleOpacity) > 0.001)
        {
            _config.UiScale = scale;
            _config.PrimeScale = primeScale;
            _config.BackgroundOpacity = bgOpacity;
            _config.StatTimeLeftEnabled = timeLeft;
            _config.FadeEnabled = fade;
            _config.FadeIdleOpacity = fadeOpacity;
            AppearanceChanged = true;
        }

        var mode = ModeCentered.IsChecked == true ? "centered" : "island";
        var zoom = Math.Round(ZoomSlider.Value, 2);
        var mapSize = Math.Round(MapSizeSlider.Value);
        var trail = TrailOff.IsChecked == true ? 0 : Trail10.IsChecked == true ? 10 : Trail30.IsChecked == true ? 30 : 60;
        var scaleBar = ScaleBarCheck.IsChecked == true;
        var speed = SpeedCheck.IsChecked == true;
        var visibility = WpTracked.IsChecked == true ? "tracked" : WpNearest.IsChecked == true ? "nearest" : "all";
        if (mode != _config.MinimapMode ||
            Math.Abs(zoom - _config.MinimapZoom) > 0.005 ||
            Math.Abs(mapSize - _config.MinimapSize) > 0.5 ||
            trail != _config.MinimapTrailMinutes ||
            scaleBar != _config.MinimapScaleBarEnabled ||
            speed != _config.MinimapSpeedEnabled ||
            visibility != _config.WaypointVisibility)
        {
            _config.MinimapMode = mode;
            _config.MinimapZoom = zoom;
            _config.MinimapSize = mapSize;
            _config.MinimapTrailMinutes = trail;
            _config.MinimapScaleBarEnabled = scaleBar;
            _config.MinimapSpeedEnabled = speed;
            _config.WaypointVisibility = visibility;
            MinimapChanged = true;
        }

        if (_waypointsDirty)
        {
            _config.TrackedWaypointId = _draftTracked;
            _library.ReplaceWith(_draft); // raises Changed: the minimap redraws, MainWindow saves the file
            MinimapChanged = true;
        }

        _config.Save();
        DialogResult = true;
    }
}
