using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
/// shortest arc. The DRAWING is the shared MapCanvas (Map, big map plan
/// phase 2); this window decides what it shows, in which view, and when.
/// </summary>
public partial class MinimapWindow : OverlayWindowBase
{
    private const double MinZoom = 1.25;
    private const double MaxZoom = 6;        // source map is 1000 px — the top of the range upscales slightly

    /// <summary>The map square at 100%: the frame's width minus the 6 px padding and 1 px border each side. MinimapScale multiplies the whole frame.</summary>
    internal const double MapSize = WidgetFrame.Width - 14;
    private const double SnapRadius = 12;    // a click / hover this close to a marker means that waypoint
    private const double MaxScaleBarPixels = 80; // the bar takes ≤ 30% of the map's width, and never more than this

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly BreadcrumbTrail _trail;  // MainWindow's (D6: both maps draw one path); its own only when built alone
    private readonly bool _ownsTrail;         // built alone: this window feeds its trail itself

    // ---- Waypoints, heading/speed, map menu -----------------------------------
    private const int NearestCount = 10;         // the "nearest" visibility policy
    private const int FooterNameLength = 14;
    private const double MinClosingMps = 0.3;    // slower than this toward a waypoint = no ETA worth showing
    private static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(3);

    private readonly SpeedTracker _speed = new();
    private readonly DispatcherTimer _noticeTimer;
    private readonly MapActions _actions;          // what the menu's entries do (shared with every map)
    private readonly MapMenuBuilder _menuBuilder;  // and the entries themselves
    private Waypoint? _hover;                 // the waypoint under the cursor in edit mode (footer shows its name)
    private string? _notice;                  // a brief footer message (copied / pasted / nothing to paste)
    private bool _centered;
    private double _zoom;
    private bool _hasFix;                                 // false → next render snaps instead of gliding
    private (double Fx, double Fy, double Yaw)? _lastFix; // map fractions (0–1) + screen yaw
    private (double X, double Y)? _lastWorld;             // player world position (cm), for waypoint distance

    /// <param name="trail">The shared breadcrumb trail, fed by MainWindow; null = a trail of this window's own, fed here.</param>
    /// <param name="actions">The menu actions shared with the big map, so tracking on one redraws the other; null = this window's own.</param>
    public MinimapWindow(OverlayConfig config, PollService poll, WaypointLibrary library, FriendBook book,
                         BreadcrumbTrail? trail = null, MapActions? actions = null)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        _library = library;
        _book = book;
        _trail = trail ?? new BreadcrumbTrail();
        _ownsTrail = trail is null;
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

        _noticeTimer = new DispatcherTimer { Interval = NoticeHold };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice = null;
            UpdateFooter();
        };
        _actions = actions ?? new MapActions(config, library, book, Short);
        _actions.WaypointTracked += OnWaypointTracked;
        _actions.FriendTracked += OnFriendTracked;
        _menuBuilder = new MapMenuBuilder(MapMenuItems, (Style)FindResource("MenuButton"), BorderLocked, _actions, FriendBrush,
                                          () => _lastWorld, () => MapMenu.IsOpen = false, ShowNotice);

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
            _actions.WaypointTracked -= OnWaypointTracked; // a shared instance outlives this window
            _actions.FriendTracked -= OnFriendTracked;
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

    /// <summary>
    /// Edit-mode drag. A left click that closes an open map menu reaches this
    /// too (or the drag of whichever widget was clicked), so the snap guides
    /// flash and a moving hand can nudge the widget a pixel. Known and
    /// accepted (owner's call): a flag swallowing that press was designed and
    /// declined — don't add it unasked.
    /// </summary>
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
        UpdateWaypointVisual(Map.MapOffset.X, Map.MapOffset.Y, glide: null);
    }

    /// <summary>Its own scale, like every widget (v1.25 — it was sized in pixels before; the old "MinimapSize" key is ignored since Oct 6 2026).</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.MinimapScale;

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
    private void OnHeatmap(byte[]? png) => Map.ShowHeatmap(_config.HeatmapEnabled ? png : null);

    /// <summary>Sizes the map for the current mode, resets transforms, and snap-renders the last fix.</summary>
    private void ApplyViewMode()
    {
        var view = View();
        Map.ClearAnimations();

        Map.SetMapSize(view.MapSize);
        if (_centered) Map.MoveArrow(view.Center, glide: null);
        else Map.MoveMap(new Point(0, 0), glide: null);
        UpdateAreaBorders(); // scaled to the map's rendered size
        UpdateScaleBar();
        RenderTrail(); // map-pixel space: the rendered size just changed

        _hasFix = false; // next render snaps into place
        RenderLastFix();
        if (_lastFix is null)
        {
            UpdateWaypointVisual(Map.MapOffset.X, Map.MapOffset.Y, glide: null);
            UpdateFooter();
        }
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
            Map.ArrowVisible = false;
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
        if (_ownsTrail && TrailKeep > TimeSpan.Zero) _trail.Add(p, TrailKeep); // a shared trail is fed by MainWindow, before this handler
        RenderTrail();
        if (_config.WaypointVisibility == "nearest") RebuildMarkersIfSetChanged(); // the nearest ten follow the player
        RenderLastFix();
        UpdateSpeedPill();
        UpdateAreaPill(); // MainWindow has already said which area (SetArea); a position is what lets "Uncharted" show
    }

    /// <summary>World cm → map fractions (0–1, y flipped) — mirrors the live-map frontend, pinOffset included.</summary>
    private static (double Fx, double Fy) ToFraction(MapCalibration cal, double x, double y) => cal.ToFraction(x, y);

    /// <summary>
    /// The map square as a MapViewport with the map's top-left at (tx, ty):
    /// the whole map at offset 0 in the island view, the zoomed map in the
    /// centred one. MapViewport holds the arithmetic, tested bit-identical
    /// to what this window computed inline before.
    /// </summary>
    private MapViewport View(double tx = 0, double ty = 0)
    {
        var size = MapHost.Width;
        return new MapViewport(new Size(size, size), _centered ? size * _zoom : size, new Point(tx, ty));
    }

    // ---- Breadcrumb trail + scale bar ----------------------------------------
    private TimeSpan TrailKeep => BreadcrumbTrail.KeepFor(_config.MinimapTrailMinutes);

    /// <summary>
    /// Redraws the trail from the tracker — per snapshot, and whenever the
    /// map's rendered size changes (the points live in map-pixel space and
    /// pan with the map). Three age bands, oldest faintest (MapCanvas.TrailBands).
    /// </summary>
    private void RenderTrail()
    {
        var keep = TrailKeep;
        var bands = keep > TimeSpan.Zero && _lastFix is not null && _poll.Calibration is { } cal && _trail.Points.Count > 1
            ? MapCanvas.TrailBands(_trail.Points, cal, Map.MapSize, keep, DateTime.UtcNow)
            : new[] { new PointCollection(), new PointCollection(), new PointCollection() };
        Map.ShowTrail(bands[0], bands[1], bands[2]);
    }

    /// <summary>A round real-world length for the current view; follows mode, zoom and map size.</summary>
    private void UpdateScaleBar()
    {
        var (meters, pixels) = _config.MinimapScaleBarEnabled && _poll.Calibration is { MapSize: > 0 } cal
            // World cm → map units (ScaleX) → rendered pixels; ×100 for metres.
            ? ScaleBar.Pick(100 * Math.Abs(cal.ScaleX) * Map.MapSize / cal.MapSize,
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
        Map.ArrowVisible = true;

        var size = MapHost.Width;
        var snap = !_hasFix; // first fix after startup/spawn/mode change: no glide
        _hasFix = true;
        if (snap) Map.ClearAnimations();

        var duration = TimeSpan.FromSeconds(Math.Max(2, _config.PollIntervalSeconds) * 0.9);
        TimeSpan? glide = snap ? null : duration;

        if (_centered)
        {
            // Arrow pinned at the centre; the map pans so the player sits under it.
            var view = MapViewport.Centered(new Size(size, size), _zoom, fix.Fx, fix.Fy);
            Map.MoveMap(view.Offset, glide);
            Map.TurnArrow(fix.Yaw, glide);
            // The markers glide with the same targets/duration so they stay
            // glued to the terrain while the map pans.
            UpdateWaypointVisual(view.Offset.X, view.Offset.Y, glide);
        }
        else
        {
            Map.MoveArrow(View().ToPanel(fix.Fx, fix.Fy), glide);
            Map.TurnArrow(fix.Yaw, glide);
            UpdateWaypointVisual(0, 0, glide: null); // static map, static markers
        }

        UpdateFooter();
    }
}
