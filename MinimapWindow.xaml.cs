using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PandoraOverlay;

/// <summary>
/// Draggable minimap window: bundled island map + the player's arrow. A pure
/// consumer of the shared PollService stream — it never makes requests of its
/// own, so opening it adds zero traffic. The world→pixel transform mirrors the
/// live-map frontend (see MapCalibration); positions glide between polls and
/// yaw rotates along the shortest arc.
/// </summary>
public partial class MinimapWindow : OverlayWindowBase
{
    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly RotateTransform _arrowRotate = new();
    private readonly TranslateTransform _arrowTranslate = new();
    private bool _hasFix; // false → next snapshot snaps instead of gliding

    public MinimapWindow(OverlayConfig config, PollService poll)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;

        Left = config.MinimapX;
        Top = config.MinimapY;
        MapHost.Width = MapHost.Height = config.MinimapSize;

        // Bundled copy of the site's island map (Assets/map.png) — the live
        // asset URL is content-hashed and changes on redeploys, so we ship it.
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri("pack://application:,,,/Assets/map.png");
        bmp.DecodePixelWidth = (int)(config.MinimapSize * 2); // crisp under DPI scaling
        bmp.EndInit();
        bmp.Freeze();
        MapImage.Source = bmp;

        PlayerArrow.RenderTransform = new TransformGroup
        {
            Children = { _arrowRotate, _arrowTranslate }
        };

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

    private void OnSnapshot(MyLocationResponse result)
    {
        if (!EditMode && !Topmost) Topmost = true;

        var cal = _poll.Calibration;
        if (cal is null || !result.InGame || result.Player is null)
        {
            _hasFix = false;
            PlayerArrow.Visibility = Visibility.Collapsed;
            MapStatus.Text = cal is null ? "waiting for map calibration…" : "not in-game";
            MapStatus.Visibility = Visibility.Visible;
            return;
        }

        MapStatus.Visibility = Visibility.Collapsed;
        PlayerArrow.Visibility = Visibility.Visible;

        var p = result.Player;
        var size = MapHost.Width;
        var px = Math.Clamp((cal.OffsetX + p.X * cal.ScaleX) / cal.MapSize * size, 0, size);
        var py = Math.Clamp((1 - (cal.OffsetY + p.Y * cal.ScaleY) / cal.MapSize) * size, 0, size);
        var yaw = p.Yaw + _config.MinimapYawOffsetDegrees;

        if (!_hasFix)
        {
            _hasFix = true;
            // First fix after startup/spawn: snap, don't glide across the island.
            _arrowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _arrowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _arrowRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _arrowTranslate.X = px;
            _arrowTranslate.Y = py;
            _arrowRotate.Angle = yaw;
            return;
        }

        var duration = TimeSpan.FromSeconds(Math.Max(2, _config.PollIntervalSeconds) * 0.9);
        var yawDelta = ((yaw - _arrowRotate.Angle) % 360 + 540) % 360 - 180;
        Animate(_arrowTranslate, TranslateTransform.XProperty, px, duration);
        Animate(_arrowTranslate, TranslateTransform.YProperty, py, duration);
        Animate(_arrowRotate, RotateTransform.AngleProperty, _arrowRotate.Angle + yawDelta, duration);
    }

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration), HandoffBehavior.SnapshotAndReplace);
}
