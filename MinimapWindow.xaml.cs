using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// Draggable minimap window with two north-up view modes:
///  - "island": the whole map fills the panel and the arrow moves over it;
///  - "centered": the map is rendered at MinimapSize × zoom and pans under an
///    arrow fixed at the panel centre. No pan clamping — near coasts the view
///    simply runs into the map image's own ocean border.
/// Three waypoint slots (blue, green, purple — set, cleared, copied and pasted
/// from the edit-mode right-click menu) are stored in world coordinates and
/// drawn as diamonds (edge-clamped in the centered view when off-screen), the
/// nearest one's distance and ETA in the footer; a heading + speed pill sits
/// bottom-right. An optional heatmap layer (HeatmapEnabled) blends the site's
/// pre-rendered activity image over the map, delivered by PollService's slow
/// timer. A breadcrumb trail (MinimapTrailMinutes) draws the recent path and
/// a scale bar (MinimapScaleBarEnabled) sits bottom-left — both computed
/// locally. A pure consumer of the shared PollService stream — it never
/// makes requests of its own. The world→pixel transform mirrors the live-map
/// frontend (see MapCalibration); movement glides between polls and yaw
/// rotates along the shortest arc.
/// </summary>
public partial class MinimapWindow : OverlayWindowBase
{
    private const double MinZoom = 1.25;
    private const double MaxZoom = 6;        // source map is 1000 px — the top of the range upscales slightly
    private const double MinMapSize = 160;
    private const double MaxMapSize = 400;
    private const double WaypointMargin = 8; // edge-clamp inset for the off-screen indicator
    private const double SnapRadius = 12;    // a right-click this close to a marker means that waypoint, exactly
    private const double MaxScaleBarPixels = 80; // the bar takes ≤ 30% of the map's width, and never more than this

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly BreadcrumbTrail _trail = new();
    private readonly RotateTransform _arrowRotate = new();
    private readonly TranslateTransform _arrowTranslate = new();
    private readonly TranslateTransform _mapTranslate = new();

    // ---- Waypoint slots, heading/speed, map menu ----------------------------
    private static readonly Brush[] SlotBrushes =
    {
        new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), // blue
        new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84)), // green
        new SolidColorBrush(Color.FromRgb(0xCE, 0x93, 0xD8))  // purple
    };
    private static readonly string[] SlotNames = { "Blue", "Green", "Purple" };
    private const double MinClosingMps = 0.3;  // slower than this toward a waypoint = no ETA worth showing
    private static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DismissGrace = TimeSpan.FromMilliseconds(400); // the click that closed the menu is not a new one

    private readonly System.Windows.Shapes.Path[] _marks;
    private readonly TranslateTransform[] _markTranslate = { new(), new(), new() };
    private readonly SpeedTracker _speed = new();
    private readonly DispatcherTimer _noticeTimer;
    private (double X, double Y)? _menuWorld; // the map point under the cursor when the menu opened
    private DateTime _menuClosedAt;           // when the popup last closed, to swallow the dismissing click
    private string? _notice;                  // a brief footer message (copied / pasted / nothing to paste)
    private bool _centered;
    private double _zoom;
    private bool _hasFix;                                 // false → next render snaps instead of gliding
    private (double Fx, double Fy, double Yaw)? _lastFix; // map fractions (0–1) + screen yaw
    private (double X, double Y)? _lastWorld;             // player world position (cm), for waypoint distance

    public MinimapWindow(OverlayConfig config, PollService poll)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        _centered = string.Equals(config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        _zoom = Math.Clamp(config.MinimapZoom, MinZoom, MaxZoom);

        Left = config.MinimapX;
        Top = config.MinimapY;
        MapHost.Width = MapHost.Height = Math.Clamp(config.MinimapSize, MinMapSize, MaxMapSize);
        ApplyAppearance(config);

        // Bundled copy of the site's island map (Assets/map.png) — decoded at
        // native resolution so the centered view's zoom stays sharp.
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri("pack://application:,,,/Assets/map.png");
        bmp.EndInit();
        bmp.Freeze();
        MapImage.Source = bmp;
        MapImage.RenderTransform = _mapTranslate;
        HeatmapImage.RenderTransform = _mapTranslate; // shared: heatmap pans with the map
        TrailOld.RenderTransform = TrailMid.RenderTransform = TrailNew.RenderTransform = _mapTranslate; // so does the trail

        PlayerArrow.RenderTransform = new TransformGroup
        {
            Children = { _arrowRotate, _arrowTranslate }
        };
        _marks = new[] { WaypointBlue, WaypointGreen, WaypointPurple };
        for (var i = 0; i < _marks.Length; i++) _marks[i].RenderTransform = _markTranslate[i];
        _noticeTimer = new DispatcherTimer { Interval = NoticeHold };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice = null;
            UpdateFooter();
        };
        MapMenu.Closed += (_, _) => _menuClosedAt = DateTime.UtcNow;

        ApplyViewMode();

        _poll.SnapshotReceived += OnSnapshot;
        _poll.CalibrationChanged += OnCalibrationChanged;
        _poll.HeatmapChanged += OnHeatmap;
        Closed += (_, _) =>
        {
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.CalibrationChanged -= OnCalibrationChanged;
            _poll.HeatmapChanged -= OnHeatmap;
        };
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!EditMode || !_centered) return;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), MinZoom, MaxZoom);
        _config.MinimapZoom = Math.Round(_zoom, 2); // persisted with the next Save()
        ApplyViewMode();
    }

    /// <summary>Flips island ↔ centered — the control panel button and the global hotkey both land here.</summary>
    public void ToggleView()
    {
        _centered = !_centered;
        _config.MinimapMode = _centered ? "centered" : "island";
        ApplyViewMode();
    }

    private void OnCalibrationChanged(MapCalibration _)
    {
        if (MapStatus.Visibility == Visibility.Visible)
        {
            MapStatus.Text = "connecting…";
        }
        UpdateScaleBar();
        RenderTrail();
    }

    /// <summary>The minimap is sized natively (MinimapSize) — never scale-transformed.</summary>
    protected override double AppearanceScale(OverlayConfig config) => 1.0;

    /// <summary>Re-reads view mode, zoom, size and appearance from config after the settings dialog saves.</summary>
    public void ApplySettings()
    {
        _centered = string.Equals(_config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        _zoom = Math.Clamp(_config.MinimapZoom, MinZoom, MaxZoom);
        MapHost.Width = MapHost.Height = Math.Clamp(_config.MinimapSize, MinMapSize, MaxMapSize);
        if (!_config.HeatmapEnabled) OnHeatmap(null); // toggled off: clear immediately
        if (TrailKeep <= TimeSpan.Zero) _trail.Reset(); // switched off: forget the path, not just hide it
        ApplyAppearance(_config);
        ApplyViewMode();
        UpdateSpeedPill();
    }

    /// <summary>Fresh heatmap bytes from PollService's slow timer; null hides the layer.</summary>
    private void OnHeatmap(byte[]? png)
    {
        if (png is null || !_config.HeatmapEnabled)
        {
            HeatmapImage.Visibility = Visibility.Collapsed;
            HeatmapImage.Source = null;
            return;
        }
        try
        {
            using var stream = new MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            HeatmapImage.Source = bmp;
            HeatmapImage.Visibility = Visibility.Visible;
        }
        catch
        {
            HeatmapImage.Visibility = Visibility.Collapsed; // undecodable image: keep the map usable
        }
    }

    /// <summary>Sizes the map for the current mode, resets transforms, and snap-renders the last fix.</summary>
    private void ApplyViewMode()
    {
        var size = MapHost.Width;
        ClearAnimations();

        if (_centered)
        {
            MapImage.Width = MapImage.Height = size * _zoom;
            _arrowTranslate.X = size / 2;
            _arrowTranslate.Y = size / 2;
        }
        else
        {
            MapImage.Width = MapImage.Height = size;
            _mapTranslate.X = 0;
            _mapTranslate.Y = 0;
        }
        HeatmapImage.Width = HeatmapImage.Height = MapImage.Width;
        UpdateScaleBar();
        RenderTrail(); // map-pixel space: the rendered size just changed

        _hasFix = false; // next render snaps into place
        RenderLastFix();
        if (_lastFix is null)
        {
            UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
            UpdateFooter();
        }
    }

    private void ClearAnimations()
    {
        _arrowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _arrowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _mapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _mapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        foreach (var t in _markTranslate)
        {
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
        }
        _arrowRotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private void OnSnapshot(MyLocationResponse result)
    {
        if (!EditMode && !Topmost) Topmost = true;

        var cal = _poll.Calibration;
        if (cal is null || !result.InGame || result.Player is null)
        {
            _hasFix = false;
            _lastFix = null;
            _lastWorld = null;
            _speed.Reset();
            UpdateSpeedPill();
            PlayerArrow.Visibility = Visibility.Collapsed;
            MapStatus.Text = cal is null ? "waiting for map calibration…" : "not in-game";
            MapStatus.Visibility = Visibility.Visible;
            RenderTrail(); // hidden with the arrow; the path itself is kept (see BreadcrumbTrail)
            UpdateFooter();
            return;
        }

        var p = result.Player;
        _lastWorld = (p.X, p.Y);
        _speed.Add(p.X, p.Y); // before the footer renders: the ETA reads it
        var (fx, fy) = ToFraction(cal, p.X, p.Y);
        _lastFix = (fx, fy, p.Yaw + _config.MinimapYawOffsetDegrees);
        if (TrailKeep > TimeSpan.Zero) _trail.Add(p, TrailKeep);
        RenderTrail();
        RenderLastFix();
        UpdateSpeedPill();
    }

    /// <summary>World cm → map fractions (0–1, y flipped) — mirrors the live-map frontend, pinOffset included.</summary>
    private static (double Fx, double Fy) ToFraction(MapCalibration cal, double x, double y) =>
        (Math.Clamp((cal.OffsetX + x * cal.ScaleX + cal.PinOffsetX) / cal.MapSize, 0, 1),
         Math.Clamp(1 - (cal.OffsetY + y * cal.ScaleY + cal.PinOffsetY) / cal.MapSize, 0, 1));

    // ---- Breadcrumb trail + scale bar ----------------------------------------
    private TimeSpan TrailKeep => TimeSpan.FromMinutes(Math.Clamp(_config.MinimapTrailMinutes, 0, 120));

    /// <summary>
    /// Redraws the trail from the tracker — per snapshot, and whenever the
    /// map's rendered size changes (the points live in map-pixel space and
    /// pan with the map). Split into three age bands, oldest faintest; each
    /// band starts on the previous band's last point so they meet.
    /// </summary>
    private void RenderTrail()
    {
        var bands = new[] { new PointCollection(), new PointCollection(), new PointCollection() }; // newest → oldest
        var keep = TrailKeep;
        if (keep > TimeSpan.Zero && _lastFix is not null && _poll.Calibration is { } cal && _trail.Points.Count > 1)
        {
            var render = MapImage.Width;
            var now = DateTime.UtcNow;
            Point? previous = null;
            var previousBand = -1;
            foreach (var (at, x, y) in _trail.Points)
            {
                var (fx, fy) = ToFraction(cal, x, y);
                var point = new Point(fx * render, fy * render);
                var band = Math.Clamp((int)((now - at).TotalSeconds / keep.TotalSeconds * 3), 0, 2);
                if (band != previousBand && previous is { } join) bands[band].Add(join);
                bands[band].Add(point);
                previous = point;
                previousBand = band;
            }
        }
        TrailNew.Points = bands[0];
        TrailMid.Points = bands[1];
        TrailOld.Points = bands[2];
    }

    /// <summary>A round real-world length for the current view; follows mode, zoom and map size.</summary>
    private void UpdateScaleBar()
    {
        var (meters, pixels) = _config.MinimapScaleBarEnabled && _poll.Calibration is { MapSize: > 0 } cal
            // World cm → map units (ScaleX) → rendered pixels; ×100 for metres.
            ? ScaleBar.Pick(100 * Math.Abs(cal.ScaleX) * MapImage.Width / cal.MapSize,
                            Math.Min(MapHost.Width * 0.3, MaxScaleBarPixels))
            : (0, 0);
        if (meters <= 0)
        {
            ScaleBarPanel.Visibility = Visibility.Collapsed;
            return;
        }
        ScaleBarLabel.Text = ScaleBar.Format(meters);
        ScaleBarLine.Width = pixels;
        ScaleBarPanel.Visibility = Visibility.Visible;
    }

    private void RenderLastFix()
    {
        if (_lastFix is not { } fix) return;

        MapStatus.Visibility = Visibility.Collapsed;
        PlayerArrow.Visibility = Visibility.Visible;

        var size = MapHost.Width;
        var snap = !_hasFix; // first fix after startup/spawn/mode change: no glide
        _hasFix = true;
        if (snap) ClearAnimations();

        var duration = TimeSpan.FromSeconds(Math.Max(2, _config.PollIntervalSeconds) * 0.9);

        if (_centered)
        {
            // Arrow pinned at the centre; the map pans so the player sits under it.
            var mapSize = size * _zoom;
            var tx = size / 2 - fix.Fx * mapSize;
            var ty = size / 2 - fix.Fy * mapSize;
            if (snap)
            {
                _mapTranslate.X = tx;
                _mapTranslate.Y = ty;
                _arrowRotate.Angle = fix.Yaw;
            }
            else
            {
                Animate(_mapTranslate, TranslateTransform.XProperty, tx, duration);
                Animate(_mapTranslate, TranslateTransform.YProperty, ty, duration);
                AnimateYaw(fix.Yaw, duration);
            }
            // The waypoint glides with the same targets/duration so it stays
            // glued to the terrain while the map pans.
            UpdateWaypointVisual(tx, ty, snap ? null : duration);
        }
        else
        {
            var px = fix.Fx * size;
            var py = fix.Fy * size;
            if (snap)
            {
                _arrowTranslate.X = px;
                _arrowTranslate.Y = py;
                _arrowRotate.Angle = fix.Yaw;
            }
            else
            {
                Animate(_arrowTranslate, TranslateTransform.XProperty, px, duration);
                Animate(_arrowTranslate, TranslateTransform.YProperty, py, duration);
                AnimateYaw(fix.Yaw, duration);
            }
            UpdateWaypointVisual(0, 0, glide: null); // static map, static marker
        }

        UpdateFooter();
    }

    // ---- Map menu (edit mode, right-click) ------------------------------------

    /// <summary>
    /// Opens on the button RELEASE, like a Windows context menu: opened on
    /// the press, the popup's own "click outside closes me" logic took the
    /// matching release as that outside click, so the menu only survived if
    /// the button was held until the cursor reached it. And like a Windows
    /// menu, the click that dismisses it is swallowed: a right-click outside
    /// an open menu closes it (the press) and must not reopen it at the new
    /// spot (the release) — hence the grace after Closed.
    /// </summary>
    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!EditMode || _poll.Calibration is not { } cal) return;
        if (DateTime.UtcNow - _menuClosedAt < DismissGrace) return; // this click closed the menu; that's all it does
        var pos = e.GetPosition(MapHost);
        var size = MapHost.Width;
        if (pos.X < 0 || pos.Y < 0 || pos.X > size || pos.Y > size) return; // footer, not the map

        // Inverse of the calibration transform: panel point → map fraction → world cm.
        double fx, fy;
        if (_centered)
        {
            var mapSize = size * _zoom;
            fx = (pos.X - _mapTranslate.X) / mapSize;
            fy = (pos.Y - _mapTranslate.Y) / mapSize;
        }
        else
        {
            fx = pos.X / size;
            fy = pos.Y / size;
        }
        fx = Math.Clamp(fx, 0, 1);
        fy = Math.Clamp(fy, 0, 1);
        _menuWorld = ((fx * cal.MapSize - cal.OffsetX - cal.PinOffsetX) / cal.ScaleX,
                      ((1 - fy) * cal.MapSize - cal.OffsetY - cal.PinOffsetY) / cal.ScaleY);

        // On or near a marker, the spot IS that waypoint — so sharing a nest
        // waypoint is right-click on it, "Copy this spot", with no aiming.
        for (var i = 0; i < _marks.Length; i++)
        {
            if (_marks[i].Visibility == Visibility.Visible && _config.Waypoints[i] is { } w &&
                Math.Abs(pos.X - _markTranslate[i].X) < SnapRadius &&
                Math.Abs(pos.Y - _markTranslate[i].Y) < SnapRadius)
            {
                _menuWorld = (w.X, w.Y);
                break;
            }
        }

        BuildMapMenu();
        MapMenu.IsOpen = true;
        // Focus the panel once the popup's window exists, so Escape reaches it
        // (a plain Popup doesn't close on Escape by itself, unlike ContextMenu).
        Dispatcher.BeginInvoke(() => MapMenuPanel.Focus(), DispatcherPriority.Input);
        e.Handled = true;
    }

    private void MapMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        MapMenu.IsOpen = false;
        e.Handled = true;
    }

    /// <summary>
    /// Rebuilds the menu for the current state: a "here" entry per slot first
    /// (so placing stays right-click + click), Clear for the set ones (+ Clear
    /// all once two are set), then share-a-spot: the clicked spot ("meet
    /// here"), my position ("come to me"), and Paste into the first empty
    /// slot, else blue — the entry says which.
    /// </summary>
    private void BuildMapMenu()
    {
        MapMenuItems.Children.Clear();
        for (var i = 0; i < SlotNames.Length; i++)
        {
            var slot = i;
            AddMenuItem($"{SlotNames[i]} waypoint here", SlotBrushes[i], () => SetSlot(slot, _menuWorld));
        }

        var setCount = _config.Waypoints.Count(w => w is not null);
        if (setCount > 0)
        {
            AddMenuSeparator();
            for (var i = 0; i < SlotNames.Length; i++)
            {
                if (_config.Waypoints[i] is null) continue;
                var slot = i;
                AddMenuItem($"Clear {SlotNames[i].ToLowerInvariant()}", null, () => SetSlot(slot, null));
            }
            if (setCount > 1) AddMenuItem("Clear all waypoints", null, ClearAllSlots);
        }

        AddMenuSeparator();
        AddMenuItem("Copy this spot", null, CopySpot);
        AddMenuItem("Copy my position", null, CopyPosition, enabled: _lastWorld is not null);
        var target = PasteTarget();
        AddMenuItem($"Paste waypoint → {SlotNames[target].ToLowerInvariant()}", SlotBrushes[target], PasteWaypoint,
                    enabled: ClipboardHasCode());
    }

    private void AddMenuItem(string text, Brush? dot, Action onClick, bool enabled = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot is not null)
        {
            content.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Fill = dot, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
        }
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Content = content, Style = (Style)FindResource("MenuButton"), IsEnabled = enabled };
        button.Click += (_, _) =>
        {
            MapMenu.IsOpen = false;
            onClick();
        };
        MapMenuItems.Children.Add(button);
    }

    private void AddMenuSeparator() =>
        MapMenuItems.Children.Add(new Border
        {
            Height = 1, Background = BorderLocked, Margin = new Thickness(4, 3, 4, 3)
        });

    private void SetSlot(int slot, (double X, double Y)? world)
    {
        _config.Waypoints[slot] = world is { } w ? new WaypointSlot(w.X, w.Y) : null; // persisted with the next Save()
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
    }

    private void ClearAllSlots()
    {
        Array.Fill(_config.Waypoints, null);
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
    }

    /// <summary>"Meet here": the right-clicked point (or the marker under it) as a share code.</summary>
    private void CopySpot()
    {
        if (_menuWorld is not { } spot) return;
        try
        {
            Clipboard.SetText(ShareCode.Format(spot.X, spot.Y));
            ShowNotice("spot copied");
        }
        catch
        {
            ShowNotice("couldn't reach the clipboard");
        }
    }

    /// <summary>The first empty slot, else blue: what a paste replaces.</summary>
    private int PasteTarget()
    {
        var empty = Array.FindIndex(_config.Waypoints, w => w is null);
        return empty < 0 ? 0 : empty;
    }

    private static bool ClipboardHasCode()
    {
        try
        {
            return Clipboard.ContainsText() && ShareCode.TryParse(Clipboard.GetText(), out _, out _);
        }
        catch
        {
            return false; // the clipboard is a shared resource; another app can hold it briefly
        }
    }

    private void CopyPosition()
    {
        if (_lastWorld is not { } p) return;
        try
        {
            Clipboard.SetText(ShareCode.Format(p.X, p.Y));
            ShowNotice("position copied");
        }
        catch
        {
            ShowNotice("couldn't reach the clipboard");
        }
    }

    private void PasteWaypoint()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch
        {
            ShowNotice("couldn't reach the clipboard");
            return;
        }
        if (!ShareCode.TryParse(text, out var x, out var y))
        {
            ShowNotice("no position in the clipboard");
            return;
        }
        var slot = PasteTarget();
        SetSlot(slot, (x, y));
        ShowNotice($"waypoint pasted → {SlotNames[slot].ToLowerInvariant()}");
    }

    /// <summary>A short footer message in place of the usual line; the next poll or the timer restores it.</summary>
    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        UpdateFooter();
    }

    // ---- Waypoints ----------------------------------------------------------

    /// <summary>A slot's map fractions (0–1), or null when empty / no calibration.</summary>
    private (double Fx, double Fy)? SlotFraction(int slot) =>
        _config.Waypoints[slot] is { } w && _poll.Calibration is { } cal ? ToFraction(cal, w.X, w.Y) : null;

    /// <summary>
    /// Positions the marker for the given map translation (targets during a
    /// glide, current values otherwise). In the centered view an off-screen
    /// waypoint clamps to the panel edge as a direction indicator.
    /// </summary>
    private void UpdateWaypointVisual(double mapTx, double mapTy, TimeSpan? glide)
    {
        var size = MapHost.Width;
        for (var i = 0; i < _marks.Length; i++)
        {
            if (SlotFraction(i) is not { } f)
            {
                _marks[i].Visibility = Visibility.Collapsed;
                continue;
            }

            double x, y;
            if (_centered)
            {
                var mapSize = size * _zoom;
                x = Math.Clamp(f.Fx * mapSize + mapTx, WaypointMargin, size - WaypointMargin);
                y = Math.Clamp(f.Fy * mapSize + mapTy, WaypointMargin, size - WaypointMargin);
            }
            else
            {
                x = f.Fx * size;
                y = f.Fy * size;
            }

            _marks[i].Visibility = Visibility.Visible;
            var t = _markTranslate[i];
            if (glide is { } d)
            {
                Animate(t, TranslateTransform.XProperty, x, d);
                Animate(t, TranslateTransform.YProperty, y, d);
            }
            else
            {
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.BeginAnimation(TranslateTransform.YProperty, null);
                t.X = x;
                t.Y = y;
            }
        }
    }

    /// <summary>The nearest set waypoint to the player: slot index and metres.</summary>
    private (int Slot, double Meters)? NearestWaypoint()
    {
        if (_lastWorld is not { } p) return null;
        (int Slot, double Meters)? best = null;
        for (var i = 0; i < _config.Waypoints.Length; i++)
        {
            if (_config.Waypoints[i] is not { } w) continue;
            var meters = Math.Sqrt(Math.Pow(w.X - p.X, 2) + Math.Pow(w.Y - p.Y, 2)) / 100;
            if (best is null || meters < best.Value.Meters) best = (i, meters);
        }
        return best;
    }

    /// <summary>
    /// View mode (+ zoom when centered), then the nearest waypoint in its
    /// colour with the distance and, while actually closing on it, the ETA
    /// at the current pace. A notice (copied / pasted) replaces the line
    /// briefly.
    /// </summary>
    private void UpdateFooter()
    {
        ModeFooter.Inlines.Clear();
        if (_notice is not null)
        {
            ModeFooter.Inlines.Add(_notice);
            return;
        }

        ModeFooter.Inlines.Add(_centered ? $"centered · {_zoom:0.##}×" : "island view");
        if (NearestWaypoint() is not { } near || _config.Waypoints[near.Slot] is not { } w) return;

        ModeFooter.Inlines.Add(" · ");
        ModeFooter.Inlines.Add(new Run("◆") { Foreground = SlotBrushes[near.Slot] });
        var text = near.Meters >= 1000 ? $" {near.Meters / 1000:0.0} km" : $" {near.Meters:0} m";
        if (_speed.ClosingMps(w.X, w.Y) is { } closing && closing >= MinClosingMps)
        {
            text += $" · ~{FormatEta(TimeSpan.FromSeconds(near.Meters / closing))}";
        }
        ModeFooter.Inlines.Add(text);
    }

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalMinutes < 1 ? "<1 min"
        : eta.TotalHours < 1 ? $"{eta.TotalMinutes:0} min"
        : $"{(int)eta.TotalHours} h {eta.Minutes:00} min";

    /// <summary>Bottom-right pill: compass letter from the arrow's heading, and km/h once the speed is known.</summary>
    private void UpdateSpeedPill()
    {
        if (!_config.MinimapSpeedEnabled || _lastFix is not { } fix)
        {
            SpeedPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var heading = Compass.Letter(fix.Yaw);
        SpeedText.Text = _speed.SpeedMps is { } mps ? $"{heading} · {mps * 3.6:0} km/h" : heading;
        SpeedPanel.Visibility = Visibility.Visible;
    }

    private void AnimateYaw(double yaw, TimeSpan duration)
    {
        var delta = ((yaw - _arrowRotate.Angle) % 360 + 540) % 360 - 180;
        Animate(_arrowRotate, RotateTransform.AngleProperty, _arrowRotate.Angle + delta, duration);
    }

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration), HandoffBehavior.SnapshotAndReplace);
}
