using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PandoraOverlay;

/// <summary>
/// Draggable minimap window with two north-up view modes:
///  - "island": the whole map fills the panel and the arrow moves over it;
///  - "centered": the map is rendered at MinimapSize × zoom and pans under an
///    arrow fixed at the panel centre. No pan clamping — near coasts the view
///    simply runs into the map image's own ocean border.
/// A personal waypoint (right-click in edit mode: place/move; right-click the
/// marker: clear) is stored in world coordinates, drawn as a blue diamond
/// (edge-clamped in the centered view when off-screen), with the distance in
/// the footer. An optional heatmap layer (HeatmapEnabled) blends the site's
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
    private const double ClearRadius = 12;   // right-click this close to the marker removes it
    private const double MaxScaleBarPixels = 80; // the bar takes ≤ 30% of the map's width, and never more than this

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly BreadcrumbTrail _trail = new();
    private readonly RotateTransform _arrowRotate = new();
    private readonly TranslateTransform _arrowTranslate = new();
    private readonly TranslateTransform _mapTranslate = new();
    private readonly TranslateTransform _waypointTranslate = new();
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
        WaypointMark.RenderTransform = _waypointTranslate;

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
        _waypointTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _waypointTranslate.BeginAnimation(TranslateTransform.YProperty, null);
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
            PlayerArrow.Visibility = Visibility.Collapsed;
            MapStatus.Text = cal is null ? "waiting for map calibration…" : "not in-game";
            MapStatus.Visibility = Visibility.Visible;
            RenderTrail(); // hidden with the arrow; the path itself is kept (see BreadcrumbTrail)
            UpdateFooter();
            return;
        }

        var p = result.Player;
        _lastWorld = (p.X, p.Y);
        var (fx, fy) = ToFraction(cal, p.X, p.Y);
        _lastFix = (fx, fy, p.Yaw + _config.MinimapYawOffsetDegrees);
        if (TrailKeep > TimeSpan.Zero) _trail.Add(p, TrailKeep);
        RenderTrail();
        RenderLastFix();
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

    // ---- Waypoint -----------------------------------------------------------
    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!EditMode || _poll.Calibration is not { } cal) return;
        var pos = e.GetPosition(MapHost);
        var size = MapHost.Width;
        if (pos.X < 0 || pos.Y < 0 || pos.X > size || pos.Y > size) return; // banner/footer, not the map

        // Right-click on (or near) the existing marker clears it.
        if (WaypointMark.Visibility == Visibility.Visible &&
            Math.Abs(pos.X - _waypointTranslate.X) < ClearRadius &&
            Math.Abs(pos.Y - _waypointTranslate.Y) < ClearRadius)
        {
            _config.WaypointX = _config.WaypointY = null;
        }
        else
        {
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

            // Inverse of the calibration transform: map fraction → world cm.
            _config.WaypointX = (fx * cal.MapSize - cal.OffsetX - cal.PinOffsetX) / cal.ScaleX;
            _config.WaypointY = ((1 - fy) * cal.MapSize - cal.OffsetY - cal.PinOffsetY) / cal.ScaleY;
        }

        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
        e.Handled = true;
    }

    /// <summary>The waypoint's map fractions (0–1), or null when unset/no calibration.</summary>
    private (double Fx, double Fy)? WaypointFraction()
    {
        if (_config.WaypointX is not { } wx || _config.WaypointY is not { } wy ||
            _poll.Calibration is not { } cal)
        {
            return null;
        }
        return ToFraction(cal, wx, wy);
    }

    /// <summary>
    /// Positions the marker for the given map translation (targets during a
    /// glide, current values otherwise). In the centered view an off-screen
    /// waypoint clamps to the panel edge as a direction indicator.
    /// </summary>
    private void UpdateWaypointVisual(double mapTx, double mapTy, TimeSpan? glide)
    {
        if (WaypointFraction() is not { } f)
        {
            WaypointMark.Visibility = Visibility.Collapsed;
            return;
        }

        var size = MapHost.Width;
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

        WaypointMark.Visibility = Visibility.Visible;
        if (glide is { } d)
        {
            Animate(_waypointTranslate, TranslateTransform.XProperty, x, d);
            Animate(_waypointTranslate, TranslateTransform.YProperty, y, d);
        }
        else
        {
            _waypointTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _waypointTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _waypointTranslate.X = x;
            _waypointTranslate.Y = y;
        }
    }

    /// <summary>View mode (+ zoom when centered), plus waypoint distance when both ends are known.</summary>
    private void UpdateFooter()
    {
        var text = _centered ? $"centered · {_zoom:0.##}×" : "island view";
        if (_config.WaypointX is { } wx && _config.WaypointY is { } wy && _lastWorld is { } p)
        {
            var meters = Math.Sqrt(Math.Pow(wx - p.X, 2) + Math.Pow(wy - p.Y, 2)) / 100;
            text += meters >= 1000 ? $" · ◆ {meters / 1000:0.0}km" : $" · ◆ {meters:0}m";
        }
        ModeFooter.Text = text;
    }

    private void AnimateYaw(double yaw, TimeSpan duration)
    {
        var delta = ((yaw - _arrowRotate.Angle) % 360 + 540) % 360 - 180;
        Animate(_arrowRotate, RotateTransform.AngleProperty, _arrowRotate.Angle + delta, duration);
    }

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration), HandoffBehavior.SnapshotAndReplace);
}
