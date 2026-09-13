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
/// the footer. A pure consumer of the shared PollService stream — it never
/// makes requests of its own. The world→pixel transform mirrors the live-map
/// frontend (see MapCalibration); movement glides between polls and yaw
/// rotates along the shortest arc.
/// </summary>
public partial class MinimapWindow : OverlayWindowBase
{
    private const double MinZoom = 1.25;
    private const double MaxZoom = 6;        // source map is 1000 px — the top of the range upscales slightly
    private const double WaypointMargin = 8; // edge-clamp inset for the off-screen indicator
    private const double ClearRadius = 12;   // right-click this close to the marker removes it

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
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
        MapHost.Width = MapHost.Height = config.MinimapSize;
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

        PlayerArrow.RenderTransform = new TransformGroup
        {
            Children = { _arrowRotate, _arrowTranslate }
        };
        WaypointMark.RenderTransform = _waypointTranslate;

        ApplyViewMode();

        _poll.SnapshotReceived += OnSnapshot;
        _poll.CalibrationChanged += OnCalibrationChanged;
        Closed += (_, _) =>
        {
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.CalibrationChanged -= OnCalibrationChanged;
        };
    }

    protected override FrameworkElement? BannerElement => EditBanner;

    protected override void OnEditModeChanged(bool editMode)
    {
        EditBanner.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
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

    private void View_Click(object sender, RoutedEventArgs e) => ToggleView();

    /// <summary>Flips island ↔ centered — the VIEW button and the global hotkey both land here.</summary>
    public void ToggleView()
    {
        _centered = !_centered;
        _config.MinimapMode = _centered ? "centered" : "island";
        ApplyViewMode();
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        _config.MinimapEnabled = false;
        Close();
    }

    private void OnCalibrationChanged(MapCalibration _)
    {
        if (MapStatus.Visibility == Visibility.Visible)
        {
            MapStatus.Text = "connecting…";
        }
    }

    /// <summary>Re-reads view mode, zoom and appearance from config after the settings dialog saves.</summary>
    public void ApplySettings()
    {
        _centered = string.Equals(_config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        _zoom = Math.Clamp(_config.MinimapZoom, MinZoom, MaxZoom);
        ApplyAppearance(_config);
        ApplyViewMode();
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
            UpdateFooter();
            return;
        }

        var p = result.Player;
        _lastWorld = (p.X, p.Y);
        _lastFix = (
            Math.Clamp((cal.OffsetX + p.X * cal.ScaleX) / cal.MapSize, 0, 1),
            Math.Clamp(1 - (cal.OffsetY + p.Y * cal.ScaleY) / cal.MapSize, 0, 1),
            p.Yaw + _config.MinimapYawOffsetDegrees);
        RenderLastFix();
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
            _config.WaypointX = (fx * cal.MapSize - cal.OffsetX) / cal.ScaleX;
            _config.WaypointY = ((1 - fy) * cal.MapSize - cal.OffsetY) / cal.ScaleY;
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
        return (Math.Clamp((cal.OffsetX + wx * cal.ScaleX) / cal.MapSize, 0, 1),
                Math.Clamp(1 - (cal.OffsetY + wy * cal.ScaleY) / cal.MapSize, 0, 1));
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
