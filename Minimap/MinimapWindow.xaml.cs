using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// Draggable minimap window with two north-up view modes:
///  - "island": the whole map fills the panel and the arrow moves over it;
///  - "centered": the map is rendered at MapSize × zoom and pans under an
///    arrow fixed at the panel centre. No pan clamping — near coasts the view
///    simply runs into the map image's own ocean border.
/// The waypoint library (WaypointLibrary, up to 256 named places) is drawn as
/// small dots in each entry's colour, the tracked one as a ringed diamond
/// (edge-clamped in the centered view when off-screen), with the tracked —
/// else nearest — one's name, distance and ETA in the footer; a
/// WaypointVisibility policy keeps a big library readable. In-game friends
/// (PollService's roster, FriendBook colours) are smaller arrows above the
/// waypoints; the tracked friend wears a ring and takes the footer first
/// (MinimapWindow.Friends.cs). The edit-mode right-click menu adds, tracks,
/// copies and removes waypoints, tracks friends, and carries share-a-spot; a
/// heading + speed pill sits bottom-right. An optional heatmap
/// layer (HeatmapEnabled) blends the site's pre-rendered activity image over
/// the map, delivered by PollService's slow timer. A breadcrumb trail
/// (MinimapTrailMinutes) draws the recent path and a scale bar
/// (MinimapScaleBarEnabled) sits bottom-left — both computed locally. A pure
/// consumer of the shared PollService stream — it never makes requests of
/// its own. The world→pixel transform mirrors the live-map frontend (see
/// MapCalibration); movement glides between polls and yaw rotates along the
/// shortest arc.
/// </summary>
public partial class MinimapWindow : OverlayWindowBase
{
    private const double MinZoom = 1.25;
    private const double MaxZoom = 6;        // source map is 1000 px — the top of the range upscales slightly

    /// <summary>The map square at 100%: the frame's width minus the 6 px padding and 1 px border each side. MinimapScale multiplies the whole frame.</summary>
    internal const double MapSize = WidgetFrame.Width - 14;
    private const double WaypointMargin = 8; // edge-clamp inset for the tracked marker's off-screen indicator
    private const double SnapRadius = 12;    // a click / hover this close to a marker means that waypoint
    private const double MaxScaleBarPixels = 80; // the bar takes ≤ 30% of the map's width, and never more than this

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly BreadcrumbTrail _trail = new();
    private readonly RotateTransform _arrowRotate = new();
    private readonly TranslateTransform _arrowTranslate = new();
    private readonly TranslateTransform _mapTranslate = new();

    // ---- Waypoints, heading/speed, map menu -----------------------------------
    private static readonly Brush[] PaletteBrushes = WaypointPalette.Colours
        .Select(c => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Hex)); b.Freeze(); return (Brush)b; })
        .ToArray();
    private const int NearestCount = 10;         // the "nearest" visibility policy
    private const double DotSize = 6;            // an untracked waypoint
    private const double DiamondRadius = 5;      // the tracked one (10 px, like the v1.20 slots)
    private const double RingSize = 16;          // the ring around the tracked one
    private const int FooterNameLength = 14;
    private const double MinClosingMps = 0.3;    // slower than this toward a waypoint = no ETA worth showing
    private static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(3);

    /// <summary>One drawn waypoint: its shape(s) and the transform that moves them.</summary>
    private sealed class Marker
    {
        public required Waypoint Waypoint { get; init; }
        public required Shape Shape { get; init; }
        public Ellipse? Ring { get; init; }
        public TranslateTransform Translate { get; } = new();
        public bool Tracked { get; init; }
    }

    private readonly Dictionary<Guid, Marker> _markers = new();
    private readonly SpeedTracker _speed = new();
    private readonly DispatcherTimer _noticeTimer;
    private (double X, double Y)? _menuWorld; // the map point under the cursor when the menu opened
    private Waypoint? _menuHit;               // the waypoint under the cursor when the menu opened
    private Waypoint? _hover;                 // the waypoint under the cursor in edit mode (footer shows its name)
    private string? _notice;                  // a brief footer message (copied / pasted / nothing to paste)
    private bool _centered;
    private double _zoom;
    private bool _hasFix;                                 // false → next render snaps instead of gliding
    private (double Fx, double Fy, double Yaw)? _lastFix; // map fractions (0–1) + screen yaw
    private (double X, double Y)? _lastWorld;             // player world position (cm), for waypoint distance

    public MinimapWindow(OverlayConfig config, PollService poll, WaypointLibrary library, FriendBook book)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        _library = library;
        _book = book;
        _centered = string.Equals(config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        _zoom = Math.Clamp(config.MinimapZoom, MinZoom, MaxZoom);

        // MainWindow fills a missing position from the default layout before
        // creating this window (top-right corner); a null here is a hand-edited config.
        if (config.MinimapX is { } x && config.MinimapY is { } y)
        {
            Left = x;
            Top = y;
        }
        MapHost.Width = MapHost.Height = MapSize;
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
        AreaBordersPath.RenderTransform = _mapTranslate; // and the area borders
        TrailOld.RenderTransform = TrailMid.RenderTransform = TrailNew.RenderTransform = _mapTranslate; // so does the trail

        PlayerArrow.RenderTransform = new TransformGroup
        {
            Children = { _arrowRotate, _arrowTranslate }
        };
        _noticeTimer = new DispatcherTimer { Interval = NoticeHold };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice = null;
            UpdateFooter();
        };

        RebuildMarkers();
        _friends = poll.Friends; // shown mid-session: start from the current roster
        RebuildFriendMarkers();
        ApplyViewMode();

        _poll.SnapshotReceived += OnSnapshot;
        _poll.CalibrationChanged += OnCalibrationChanged;
        _poll.HeatmapChanged += OnHeatmap;
        _poll.FriendsChanged += OnFriends;
        _library.Changed += OnLibraryChanged;
        _book.Changed += OnBookChanged;
        Closed += (_, _) =>
        {
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.CalibrationChanged -= OnCalibrationChanged;
            _poll.HeatmapChanged -= OnHeatmap;
            _poll.FriendsChanged -= OnFriends;
            _library.Changed -= OnLibraryChanged;
            _book.Changed -= OnBookChanged;
        };
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
        if (!editMode && (_hover is not null || _hoverFriend is not null))
        {
            _hover = null;
            _hoverFriend = null;
            UpdateFooter();
        }
        _hoverSpot = null; // locked: the pill is about where YOU are again
        UpdateAreaPill();
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
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
    }

    /// <summary>Its own scale, like every widget (v1.25 — it was sized in pixels before; OverlayConfig.Load migrates that).</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.MinimapScale ?? 1.0;

    /// <summary>Re-reads view mode, zoom, scale, waypoint policy and appearance from config after the settings dialog saves.</summary>
    public void ApplySettings()
    {
        _centered = string.Equals(_config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        _zoom = Math.Clamp(_config.MinimapZoom, MinZoom, MaxZoom);
        if (!_config.HeatmapEnabled) OnHeatmap(null); // toggled off: clear immediately
        if (TrailKeep <= TimeSpan.Zero) _trail.Reset(); // switched off: forget the path, not just hide it
        ApplyAppearance(_config);
        RebuildMarkers(); // policy or tracked waypoint may have changed
        RebuildFriendMarkers(); // the friends layer, or a friend's map visibility / colour, may have too
        ApplyViewMode();
        UpdateSpeedPill();
        UpdateAreaPill(); // the pill's own switch; MainWindow re-sends the area after a save
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
        UpdateAreaBorders(); // scaled to the map's rendered size
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
        foreach (var m in _markers.Values)
        {
            m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
        }
        ClearFriendAnimations();
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
            UpdateAreaPill(); // no position: nothing to place, not even "Uncharted"
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
        if (_config.WaypointVisibility == "nearest") RebuildMarkersIfSetChanged(); // the nearest ten follow the player
        RenderLastFix();
        UpdateSpeedPill();
        UpdateAreaPill(); // MainWindow has already said which area (SetArea); a position is what lets "Uncharted" show
    }

    /// <summary>World cm → map fractions (0–1, y flipped) — mirrors the live-map frontend, pinOffset included.</summary>
    private static (double Fx, double Fy) ToFraction(MapCalibration cal, double x, double y) => cal.ToFraction(x, y);

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
            // The markers glide with the same targets/duration so they stay
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
            UpdateWaypointVisual(0, 0, glide: null); // static map, static markers
        }

        UpdateFooter();
    }

    private void AnimateYaw(double yaw, TimeSpan duration)
    {
        var delta = ((yaw - _arrowRotate.Angle) % 360 + 540) % 360 - 180;
        Animate(_arrowRotate, RotateTransform.AngleProperty, _arrowRotate.Angle + delta, duration);
    }

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration), HandoffBehavior.SnapshotAndReplace);
}
