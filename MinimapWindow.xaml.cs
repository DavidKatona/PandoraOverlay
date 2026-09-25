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
///  - "centered": the map is rendered at MinimapSize × zoom and pans under an
///    arrow fixed at the panel centre. No pan clamping — near coasts the view
///    simply runs into the map image's own ocean border.
/// The waypoint library (WaypointLibrary, up to 256 named places) is drawn as
/// small dots in each entry's colour, the tracked one as a ringed diamond
/// (edge-clamped in the centered view when off-screen), with the tracked —
/// else nearest — one's name, distance and ETA in the footer; a
/// WaypointVisibility policy keeps a big library readable. The edit-mode
/// right-click menu adds, tracks, copies and removes waypoints and carries
/// share-a-spot; a heading + speed pill sits bottom-right. An optional heatmap
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
    private const double MinMapSize = 160;
    private const double MaxMapSize = 400;
    private const double WaypointMargin = 8; // edge-clamp inset for the tracked marker's off-screen indicator
    private const double SnapRadius = 12;    // a click / hover this close to a marker means that waypoint
    private const double MaxScaleBarPixels = 80; // the bar takes ≤ 30% of the map's width, and never more than this

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly WaypointLibrary _library;
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

    public MinimapWindow(OverlayConfig config, PollService poll, WaypointLibrary library)
    {
        InitializeComponent();

        _config = config;
        _poll = poll;
        _library = library;
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
        _noticeTimer = new DispatcherTimer { Interval = NoticeHold };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice = null;
            UpdateFooter();
        };

        RebuildMarkers();
        ApplyViewMode();

        _poll.SnapshotReceived += OnSnapshot;
        _poll.CalibrationChanged += OnCalibrationChanged;
        _poll.HeatmapChanged += OnHeatmap;
        _library.Changed += OnLibraryChanged;
        Closed += (_, _) =>
        {
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.CalibrationChanged -= OnCalibrationChanged;
            _poll.HeatmapChanged -= OnHeatmap;
            _library.Changed -= OnLibraryChanged;
        };
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
        if (!editMode && _hover is not null)
        {
            _hover = null;
            UpdateFooter();
        }
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

    /// <summary>The minimap is sized natively (MinimapSize) — never scale-transformed.</summary>
    protected override double AppearanceScale(OverlayConfig config) => 1.0;

    /// <summary>Re-reads view mode, zoom, size, waypoint policy and appearance from config after the settings dialog saves.</summary>
    public void ApplySettings()
    {
        _centered = string.Equals(_config.MinimapMode, "centered", StringComparison.OrdinalIgnoreCase);
        _zoom = Math.Clamp(_config.MinimapZoom, MinZoom, MaxZoom);
        MapHost.Width = MapHost.Height = Math.Clamp(_config.MinimapSize, MinMapSize, MaxMapSize);
        if (!_config.HeatmapEnabled) OnHeatmap(null); // toggled off: clear immediately
        if (TrailKeep <= TimeSpan.Zero) _trail.Reset(); // switched off: forget the path, not just hide it
        ApplyAppearance(_config);
        RebuildMarkers(); // policy or tracked waypoint may have changed
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
        foreach (var m in _markers.Values)
        {
            m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
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
        if (_config.WaypointVisibility == "nearest") RebuildMarkersIfSetChanged(); // the nearest ten follow the player
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

    // ---- Map menu (edit mode, right-click) ------------------------------------

    /// <summary>
    /// Opens on the button RELEASE, like a Windows context menu: opened on
    /// the press, the popup's own "click outside closes me" logic took the
    /// matching release as that outside click, so the menu only survived if
    /// the button was held until the cursor reached it. Right-click always
    /// means "menu here": with the menu open, a right-click elsewhere moves
    /// it, as Explorer does; left click, Escape or an entry closes it. (A
    /// timing rule that swallowed the dismissing click was tried and felt
    /// worse — whether the menu closed or moved depended on how long the
    /// button was held.)
    /// </summary>
    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!EditMode || _poll.Calibration is not { } cal) return;
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

        // On or near a marker, the spot IS that waypoint — the menu gains its
        // own entries, and "Copy this spot" shares it exactly, no aiming.
        _menuHit = HitTest(pos);
        if (_menuHit is { } hit) _menuWorld = (hit.X, hit.Y);

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

    /// <summary>In edit mode the footer names the marker under the cursor.</summary>
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!EditMode) return;
        var hit = HitTest(e.GetPosition(MapHost));
        if (ReferenceEquals(hit, _hover)) return;
        _hover = hit;
        UpdateFooter();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_hover is null) return;
        _hover = null;
        UpdateFooter();
    }

    /// <summary>The drawn waypoint within SnapRadius of a panel point, nearest first; the tracked one wins ties.</summary>
    private Waypoint? HitTest(Point pos)
    {
        Marker? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in _markers.Values)
        {
            if (m.Shape.Visibility != Visibility.Visible) continue;
            var d = Math.Max(Math.Abs(pos.X - m.Translate.X), Math.Abs(pos.Y - m.Translate.Y));
            if (d >= SnapRadius) continue;
            if (d < bestDistance || (d == bestDistance && m.Tracked))
            {
                best = m;
                bestDistance = d;
            }
        }
        return best?.Waypoint;
    }

    /// <summary>
    /// Rebuilds the menu for the current state. On a marker: track / copy /
    /// remove that waypoint first. Always: "Waypoint here" (one click, the
    /// new one is auto-named and tracked), then share-a-spot: the clicked
    /// spot ("meet here"), my position ("come to me"), and Paste, which
    /// creates a waypoint named from the code.
    /// </summary>
    private void BuildMapMenu()
    {
        MapMenuItems.Children.Clear();
        var full = _library.IsFull;

        if (_menuHit is { } hit)
        {
            var brush = PaletteBrushes[WaypointPalette.Wrap(hit.Colour)];
            var name = Short(hit.Name);
            var tracked = hit.Id == _config.TrackedWaypointId;
            AddMenuItem(tracked ? $"Untrack {name}" : $"Track {name}", brush, () => SetTracked(tracked ? null : hit.Id));
            AddMenuItem($"Copy {name}", brush, () => CopyCode(ShareCode.Format(hit.X, hit.Y, hit.Name), "spot copied"));
            AddMenuItem($"Remove {name}", brush, () => RemoveWaypoint(hit));
            AddMenuSeparator();
        }

        AddMenuItem(full ? $"Library full ({WaypointLibrary.Capacity})" : "Waypoint here", null, AddWaypointHere, enabled: !full);
        AddMenuSeparator();
        AddMenuItem("Copy this spot", null, () =>
        {
            if (_menuWorld is { } spot) CopyCode(ShareCode.Format(spot.X, spot.Y), "spot copied");
        });
        AddMenuItem("Copy my position", null, () =>
        {
            if (_lastWorld is { } p) CopyCode(ShareCode.Format(p.X, p.Y), "position copied");
        }, enabled: _lastWorld is not null);
        AddMenuItem("Paste waypoint", null, PasteWaypoint, enabled: !full && ClipboardHasCode());
    }

    private void AddMenuItem(string text, Brush? dot, Action onClick, bool enabled = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot is not null)
        {
            content.Children.Add(new Ellipse
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

    private void AddWaypointHere()
    {
        if (_menuWorld is not { } spot) return;
        var wp = _library.Add(_library.NextName(), spot.X, spot.Y, _library.NextColour());
        if (wp is null)
        {
            ShowNotice("library full");
            return;
        }
        SetTracked(wp.Id);
        ShowNotice($"{Short(wp.Name)} added");
    }

    private void RemoveWaypoint(Waypoint wp)
    {
        if (wp.Id == _config.TrackedWaypointId) _config.TrackedWaypointId = null;
        _library.Remove(wp.Id); // Changed → OnLibraryChanged redraws
        ShowNotice($"{Short(wp.Name)} removed");
    }

    /// <summary>Tracks (or untracks with null); the tracked waypoint is what the footer follows and the ring marks.</summary>
    private void SetTracked(Guid? id)
    {
        _config.TrackedWaypointId = id; // persisted with the next Save()
        RebuildMarkers();
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
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

    private void CopyCode(string code, string notice)
    {
        try
        {
            Clipboard.SetText(code);
            ShowNotice(notice);
        }
        catch
        {
            ShowNotice("couldn't reach the clipboard");
        }
    }

    /// <summary>A pasted code becomes a new, tracked waypoint, named from the code when it carries a name.</summary>
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
        if (!ShareCode.TryParse(text, out var x, out var y, out var name))
        {
            ShowNotice("no position in the clipboard");
            return;
        }
        var wp = _library.Add(name.Length > 0 ? name : _library.NextName(), x, y, _library.NextColour());
        if (wp is null)
        {
            ShowNotice("library full");
            return;
        }
        SetTracked(wp.Id);
        ShowNotice($"{Short(wp.Name)} added");
    }

    /// <summary>A short footer message in place of the usual line; the next poll or the timer restores it.</summary>
    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        UpdateFooter();
    }

    // ---- Waypoint markers ---------------------------------------------------

    private Waypoint? TrackedWaypoint => _library.Find(_config.TrackedWaypointId);

    /// <summary>
    /// Which library entries are drawn under the WaypointVisibility policy:
    /// every visible one, only the tracked one, or the NearestCount visible
    /// ones closest to the player. The tracked one is always drawn — you
    /// asked to follow it.
    /// </summary>
    private List<Waypoint> ShownWaypoints()
    {
        var tracked = TrackedWaypoint;
        List<Waypoint> shown;
        switch (_config.WaypointVisibility)
        {
            case "tracked":
                shown = new List<Waypoint>();
                break;
            case "nearest" when _lastWorld is { } p:
                shown = _library.Items.Where(w => w.Visible)
                    .OrderBy(w => WaypointLibrary.Distance(w.X, w.Y, p.X, p.Y))
                    .Take(NearestCount).ToList();
                break;
            case "nearest":
                shown = _library.Items.Where(w => w.Visible).Take(NearestCount).ToList();
                break;
            default:
                shown = _library.Items.Where(w => w.Visible).ToList();
                break;
        }
        if (tracked is not null && !shown.Contains(tracked)) shown.Add(tracked);
        return shown;
    }

    private void OnLibraryChanged()
    {
        if (TrackedWaypoint is null && _config.TrackedWaypointId is not null) _config.TrackedWaypointId = null; // deleted elsewhere
        RebuildMarkers();
        UpdateWaypointVisual(_mapTranslate.X, _mapTranslate.Y, glide: null);
        UpdateFooter();
    }

    /// <summary>Under the "nearest" policy the set follows the player: rebuild only when it actually changed.</summary>
    private void RebuildMarkersIfSetChanged()
    {
        var wanted = ShownWaypoints();
        if (wanted.Count == _markers.Count && wanted.All(w => _markers.ContainsKey(w.Id))) return;
        RebuildMarkers();
    }

    /// <summary>
    /// Recreates the marker shapes from the library: a small dot per shown
    /// waypoint in its colour, the tracked one a diamond with a ring. Each
    /// gets its own translate so positions can glide with the map.
    /// </summary>
    private void RebuildMarkers()
    {
        MarkerLayer.Children.Clear();
        _markers.Clear();
        var trackedId = _config.TrackedWaypointId;
        foreach (var w in ShownWaypoints())
        {
            var brush = PaletteBrushes[WaypointPalette.Wrap(w.Colour)];
            var tracked = w.Id == trackedId;
            Marker marker;
            if (tracked)
            {
                var ring = new Ellipse
                {
                    Width = RingSize, Height = RingSize, Stroke = brush, StrokeThickness = 1.5,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(ring, -RingSize / 2);
                Canvas.SetTop(ring, -RingSize / 2);
                var diamond = new Path
                {
                    Data = Geometry.Parse($"M 0,-{DiamondRadius} L {DiamondRadius},0 L 0,{DiamondRadius} L -{DiamondRadius},0 Z"),
                    Fill = brush, Stroke = MarkerOutline, StrokeThickness = 1.25
                };
                marker = new Marker { Waypoint = w, Shape = diamond, Ring = ring, Tracked = true };
                ring.RenderTransform = marker.Translate;
                diamond.RenderTransform = marker.Translate;
                MarkerLayer.Children.Add(ring);
                MarkerLayer.Children.Add(diamond);
            }
            else
            {
                var dot = new Ellipse
                {
                    Width = DotSize, Height = DotSize, Fill = brush, Stroke = MarkerOutline, StrokeThickness = 1
                };
                Canvas.SetLeft(dot, -DotSize / 2);
                Canvas.SetTop(dot, -DotSize / 2);
                marker = new Marker { Waypoint = w, Shape = dot };
                dot.RenderTransform = marker.Translate;
                MarkerLayer.Children.Add(dot);
            }
            _markers[w.Id] = marker;
        }
    }

    private static readonly Brush MarkerOutline = new SolidColorBrush(Color.FromArgb(0x99, 0x0A, 0x15, 0x20));

    /// <summary>
    /// Positions every marker for the given map translation (targets during
    /// a glide, current values otherwise). In the centered view the tracked
    /// marker clamps to the panel edge as a direction indicator; the others
    /// simply leave the panel.
    /// </summary>
    private void UpdateWaypointVisual(double mapTx, double mapTy, TimeSpan? glide)
    {
        if (_poll.Calibration is not { } cal) return;
        var size = MapHost.Width;
        foreach (var m in _markers.Values)
        {
            var f = ToFraction(cal, m.Waypoint.X, m.Waypoint.Y);
            double x, y;
            var onScreen = true;
            if (_centered)
            {
                var mapSize = size * _zoom;
                x = f.Fx * mapSize + mapTx;
                y = f.Fy * mapSize + mapTy;
                if (m.Tracked)
                {
                    x = Math.Clamp(x, WaypointMargin, size - WaypointMargin);
                    y = Math.Clamp(y, WaypointMargin, size - WaypointMargin);
                }
                else
                {
                    onScreen = x >= -DotSize && x <= size + DotSize && y >= -DotSize && y <= size + DotSize;
                }
            }
            else
            {
                x = f.Fx * size;
                y = f.Fy * size;
            }

            var visibility = onScreen ? Visibility.Visible : Visibility.Hidden;
            m.Shape.Visibility = visibility;
            if (m.Ring is not null) m.Ring.Visibility = visibility;

            if (glide is { } d)
            {
                Animate(m.Translate, TranslateTransform.XProperty, x, d);
                Animate(m.Translate, TranslateTransform.YProperty, y, d);
            }
            else
            {
                m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
                m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                m.Translate.X = x;
                m.Translate.Y = y;
            }
        }
    }

    /// <summary>The footer's subject: the tracked waypoint, else the nearest visible one; with metres.</summary>
    private (Waypoint Waypoint, double Meters)? FooterTarget()
    {
        if (_lastWorld is not { } p) return null;
        if (TrackedWaypoint is { } t) return (t, WaypointLibrary.Distance(t.X, t.Y, p.X, p.Y) / 100);
        return _library.Nearest(p.X, p.Y, w => w.Visible);
    }

    private static string Short(string name) =>
        name.Length <= FooterNameLength ? name : name[..(FooterNameLength - 1)] + "…";

    /// <summary>
    /// View mode (+ zoom when centered), then the tracked (else nearest)
    /// waypoint in its colour with name, distance and, while actually
    /// closing on it, the ETA at the current pace. Hovering a marker in
    /// edit mode names that one instead; a notice (copied / pasted)
    /// replaces the line briefly.
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

        Waypoint subject;
        double? meters;
        if (_hover is { } h)
        {
            subject = h;
            meters = _lastWorld is { } p ? WaypointLibrary.Distance(h.X, h.Y, p.X, p.Y) / 100 : null;
        }
        else if (FooterTarget() is { } target)
        {
            subject = target.Waypoint;
            meters = target.Meters;
        }
        else
        {
            return;
        }

        ModeFooter.Inlines.Add(" · ");
        ModeFooter.Inlines.Add(new Run("◆") { Foreground = PaletteBrushes[WaypointPalette.Wrap(subject.Colour)] });
        var text = " " + Short(subject.Name);
        if (meters is { } m)
        {
            text += m >= 1000 ? $" {m / 1000:0.0} km" : $" {m:0} m";
            if (_speed.ClosingMps(subject.X, subject.Y) is { } closing && closing >= MinClosingMps)
            {
                text += $" · ~{FormatEta(TimeSpan.FromSeconds(m / closing))}";
            }
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
