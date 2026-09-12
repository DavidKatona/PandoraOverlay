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
/// A pure consumer of the shared PollService stream — it never makes requests
/// of its own, so opening it adds zero traffic. The world→pixel transform
/// mirrors the live-map frontend (see MapCalibration); movement glides between
/// polls and yaw rotates along the shortest arc.
/// </summary>
public partial class MinimapWindow : OverlayWindowBase
{
    private const double MinZoom = 1.25;
    private const double MaxZoom = 4; // source map is 1000 px — beyond this it's just blur

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly RotateTransform _arrowRotate = new();
    private readonly TranslateTransform _arrowTranslate = new();
    private readonly TranslateTransform _mapTranslate = new();
    private bool _centered;
    private double _zoom;
    private bool _hasFix;                                 // false → next render snaps instead of gliding
    private (double Fx, double Fy, double Yaw)? _lastFix; // map fractions (0–1) + screen yaw

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

        ApplyViewMode();

        _poll.SnapshotReceived += OnSnapshot;
        _poll.CalibrationChanged += OnCalibrationChanged;
        Closed += (_, _) =>
        {
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.CalibrationChanged -= OnCalibrationChanged;
        };
    }

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

    private void View_Click(object sender, RoutedEventArgs e)
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

        ModeFooter.Text = _centered ? $"centered · {_zoom:0.##}×" : "island view";

        _hasFix = false; // next render snaps into place
        RenderLastFix();
    }

    private void ClearAnimations()
    {
        _arrowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _arrowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _mapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _mapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
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
            PlayerArrow.Visibility = Visibility.Collapsed;
            MapStatus.Text = cal is null ? "waiting for map calibration…" : "not in-game";
            MapStatus.Visibility = Visibility.Visible;
            return;
        }

        var p = result.Player;
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
        }
    }

    private void AnimateYaw(double yaw, TimeSpan duration)
    {
        var delta = ((yaw - _arrowRotate.Angle) % 360 + 540) % 360 - 180;
        Animate(_arrowRotate, RotateTransform.AngleProperty, _arrowRotate.Angle + delta, duration);
    }

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration), HandoffBehavior.SnapshotAndReplace);
}
