using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// BigMapWindow, layers part: what the map draws and the Layers menu's
/// ticks (D3: remembered in config, independent of the minimap's) —
/// Areas (the borders, your area's outline, the outline under the cursor),
/// Names (area names at the legend's label points, waypoint names, friends'
/// names, decluttered by LabelLayout), Waypoints (every one with its Show
/// tick, and the tracked one: the minimap's visibility policy is a
/// small-map rule), Friends (the server's consent and each friend's Map
/// tick, as on the minimap), Trail (the shared path, its length the shared
/// setting), Heatmap (off by default: it costs requests) and the three zone
/// kinds (off by default; the map's only, never the minimap's: owner, Oct 12
/// 2026). Your arrow is always drawn while in game. Also the header's "you
/// are in", the cursor readout and the scale bar. Same class as
/// BigMapWindow.xaml.cs.
/// The six layer buttons of 1.34 became ONE menu when the zones came (the
/// owner's idea, Oct 12 2026): the header no longer grows with the layers,
/// and the zones' swatches double as the map's legend.
/// </summary>
public partial class BigMapWindow
{
    private const double ReadoutGap = 16;       // the readout sits this far from the cursor
    private const double LabelGap = 3;          // names keep this far apart (LabelLayout)
    private const double UnchartedOpacity = 0.6; // the minimap pill's dimming of "nothing is named here"

    private static readonly Brush Light = Frozen(Color.FromRgb(0xEC, 0xF2, 0xF8));
    private static readonly Brush Grey = Frozen(Color.FromRgb(0x9A, 0xA7, 0xB0));

    private readonly TranslateTransform _readoutAt = new(); // where the cursor readout sits (Readout.RenderTransform)
    private IReadOnlyList<FriendState>? _friends;
    private (double Fx, double Fy, double Yaw)? _lastFix; // map fractions + screen yaw
    private (double X, double Y)? _lastWorld;             // world cm, for distances
    private bool _hasFix;                                 // the arrow has a place to glide from
    private string? _area;                                // MainWindow's AreaJournal: where you are
    private (double Fx, double Fy)? _hoverSpot;           // the map point under the cursor
    private static int[]? _areaPixels;                    // each area's size, for ranking names
    private static AreaMap? _areaPixelsOf;

    private Brush FriendBrush(FriendState f) => MapBrushes.Palette[_book.ColourOf(f.SteamId!)];

    private string FriendName(FriendState f) => _book.DisplayName(f.SteamId, f.Name);

    private TimeSpan Glide => TimeSpan.FromSeconds(Math.Max(2, _config.PollIntervalSeconds) * 0.9);

    /// <summary>The layers sit in MAP space (MapCanvas.UseMapSpace): placed at fraction × MapSize, the pan applied by the map's own translate.</summary>
    private MapViewport MarkerView => new(_view.Panel, _view.MapSize, new Point(0, 0));

    // ---- From the stream --------------------------------------------------------

    private void OnSnapshot(MyLocationResponse result)
    {
        if (_poll.Calibration is not { } cal || !result.InGame || result.Player is null)
        {
            _lastFix = null;
            _lastWorld = null;
            _hasFix = false;
            Map.ArrowVisible = false;
            RenderTrail(); // hidden with the arrow; the path itself is kept
        }
        else
        {
            TakeFix(result.Player, cal);
            PlaceArrow(Glide);
            RenderTrail();
        }
        UpdateHeader();
        UpdateOutlines(force: false);
    }

    private void TakeFix(PlayerState p, MapCalibration cal)
    {
        var (fx, fy) = cal.ToFraction(p.X, p.Y);
        _lastFix = (fx, fy, p.Yaw + _config.MinimapYawOffsetDegrees);
        _lastWorld = (p.X, p.Y);
    }

    private void OnFriends(IReadOnlyList<FriendState>? roster)
    {
        _friends = roster;
        if (!_hasView) return;
        ShowFriends();
        PlaceFriends(Glide);
    }

    private void OnHeatmap(byte[]? png) => Map.ShowHeatmap(_config.BigMapHeatmap ? png : null);

    private void OnCalibrationChanged(MapCalibration _)
    {
        if (_hasView) DrawLayers();
    }

    private void OnLibraryChanged()
    {
        if (!_hasView) return;
        ShowWaypoints();
        LayOutLabels();
    }

    private void OnWaypointTracked() => OnLibraryChanged();

    private void OnBookChanged()
    {
        if (!_hasView) return;
        ShowFriends();
        PlaceFriends(glide: null);
    }

    private void OnFriendTracked() => OnBookChanged();

    /// <summary>MainWindow's word on where you are (its AreaJournal, the minimap pill's source).</summary>
    public void SetArea(string? name)
    {
        _area = name;
        UpdateHeader();
        UpdateOutlines(force: false);
    }

    // ---- The Layers menu --------------------------------------------------------

    /// <summary>The Layers menu's ticks, one per layer.</summary>
    private sealed record LayerTicks(ToggleButton Areas, ToggleButton Names, ToggleButton Waypoints, ToggleButton Friends,
                                     ToggleButton Trail, ToggleButton Heatmap,
                                     ToggleButton Migrations, ToggleButton Patrols, ToggleButton Sanctuaries);

    /// <summary>
    /// Fills the Layers menu, each tick as config has it: the map's own
    /// layers, the heatmap, then the zones under a caption, each with a
    /// swatch of its hue (the map's legend), and a foot line naming where the
    /// zones come from and that the game picks the active one — read from
    /// the zones file, so a data refresh changes it without code. Without
    /// the file the zone ticks are greyed and the foot line says so.
    /// </summary>
    private LayerTicks BuildLayersMenu()
    {
        var style = (Style)FindResource("MenuTick");
        ToggleButton Tick(object content, bool on)
        {
            var tick = new ToggleButton { Content = content, Style = style, IsChecked = on };
            tick.Click += Layer_Click;
            LayersItems.Children.Add(tick);
            return tick;
        }
        void Separator() => LayersItems.Children.Add(new Border { Height = 1, Background = MenuSeparator, Margin = new Thickness(4, 3, 4, 3) });
        object Swatched(string label, ZoneKind kind)
        {
            var hue = MapBrushes.HueOf(kind);
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new Border
            {
                Width = 10, Height = 10, Margin = new Thickness(0, 1, 7, 0), CornerRadius = new CornerRadius(2),
                Background = Frozen(Color.FromArgb(0x99, hue.R, hue.G, hue.B)), BorderBrush = MapBrushes.OfZone(kind), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(new TextBlock { Text = label });
            return line;
        }

        var areas = Tick("Areas", _config.BigMapAreas);
        var names = Tick("Names", _config.BigMapNames);
        Separator();
        var waypoints = Tick("Waypoints", _config.BigMapWaypoints);
        var friends = Tick("Friends", _config.BigMapFriends);
        var trail = Tick("Trail", _config.BigMapTrail);
        Separator();
        var heatmap = Tick("Heatmap", _config.BigMapHeatmap);
        Separator();
        LayersItems.Children.Add(new TextBlock
        {
            Text = "ZONES", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = HintBrush, Margin = new Thickness(12, 3, 10, 2),
        });
        var migrations = Tick(Swatched("Migrations", ZoneKind.Migration), _config.BigMapMigrations);
        var patrols = Tick(Swatched("Patrol zones", ZoneKind.Patrol), _config.BigMapPatrolZones);
        var sanctuaries = Tick(Swatched("Sanctuaries", ZoneKind.Sanctuary), _config.BigMapSanctuaries);

        var zones = ZoneAsset.Shared;
        foreach (var t in new[] { migrations, patrols, sanctuaries }) t.IsEnabled = zones is not null;
        LayersItems.Children.Add(new TextBlock
        {
            Text = zones is null
                ? "Zones unavailable"
                : $"From {zones.Source} · game {zones.GameVersions}\nThe game picks which zone is active.",
            FontSize = 10.5, Foreground = HintBrush, LineHeight = 15, Margin = new Thickness(12, 4, 12, 3),
        });
        return new LayerTicks(areas, names, waypoints, friends, trail, heatmap, migrations, patrols, sanctuaries);
    }

    /// <summary>Right-aligned under the Layers button, a little below it (both sizes come in the same units, so the alignment holds at any DPI).</summary>
    private static CustomPopupPlacement[] PlaceLayersMenu(Size popup, Size target, Point offset) =>
        new[] { new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 4), PopupPrimaryAxis.Horizontal) };

    private void LayersButton_Click(object sender, RoutedEventArgs e) => LayersMenu.IsOpen = LayersButton.IsChecked == true;

    /// <summary>
    /// The button stays lit while the menu is open, and out of the mouse's
    /// way: the click outside that closes the menu would otherwise land on it
    /// and open the menu again. The menu takes the keyboard, so Escape closes
    /// it rather than the map.
    /// </summary>
    private void LayersMenu_Opened(object? sender, EventArgs e)
    {
        LayersButton.IsChecked = true;
        LayersButton.IsHitTestVisible = false;
        Dispatcher.BeginInvoke(() => LayersMenuPanel.Focus(), DispatcherPriority.Input);
    }

    private void LayersMenu_Closed(object? sender, EventArgs e)
    {
        LayersButton.IsChecked = false;
        LayersButton.IsHitTestVisible = true;
    }

    private void LayersMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        LayersMenu.IsOpen = false;
        e.Handled = true;
    }

    // ---- The switches -----------------------------------------------------------

    /// <summary>A tick acts at once, the menu left open for the next.</summary>
    private void Layer_Click(object sender, RoutedEventArgs e)
    {
        _config.BigMapAreas = _ticks.Areas.IsChecked == true; // persisted with the next config Save
        _config.BigMapNames = _ticks.Names.IsChecked == true;
        _config.BigMapWaypoints = _ticks.Waypoints.IsChecked == true;
        _config.BigMapFriends = _ticks.Friends.IsChecked == true;
        _config.BigMapTrail = _ticks.Trail.IsChecked == true;
        _config.BigMapHeatmap = _ticks.Heatmap.IsChecked == true;
        _config.BigMapMigrations = _ticks.Migrations.IsChecked == true;
        _config.BigMapPatrolZones = _ticks.Patrols.IsChecked == true;
        _config.BigMapSanctuaries = _ticks.Sanctuaries.IsChecked == true;
        if (ReferenceEquals(sender, _ticks.Heatmap))
        {
            ApplyHeatmapSwitch();
            return;
        }
        if (ReferenceEquals(sender, _ticks.Friends)) _ = _poll.RefreshFriendsAsync(); // the roster's gate follows the layer
        if (_hasView) DrawLayers();
    }

    /// <summary>The heatmap hotkey while the big map is open (D8): its own layer, not the hidden minimap's.</summary>
    public void ToggleHeatmap()
    {
        _ticks.Heatmap.IsChecked = _ticks.Heatmap.IsChecked != true;
        _config.BigMapHeatmap = _ticks.Heatmap.IsChecked == true;
        ApplyHeatmapSwitch();
    }

    private void ApplyHeatmapSwitch()
    {
        if (_config.BigMapHeatmap) _ = _poll.RefreshHeatmapAsync(); // a picture under a minute old costs nothing
        else Map.ShowHeatmap(null);                                                // the gate narrows at the next tick
    }

    // ---- Drawing ----------------------------------------------------------------

    /// <summary>Every layer for the current zoom: after a zoom, a switch, or new calibration.</summary>
    private void DrawLayers()
    {
        ShowZones();
        Map.ShowBorders(_config.BigMapAreas, hostScale: 1);
        UpdateOutlines(force: true);
        RenderTrail();
        ShowWaypoints();
        ShowFriends();
        PlaceFriends(glide: null);
        PlaceArrow(glide: null);
        LayOutLabels();
    }

    /// <summary>The zone kinds whose ticks are on.</summary>
    private ZoneKinds ShownZones =>
        (_config.BigMapMigrations ? ZoneKinds.Migrations : ZoneKinds.None) |
        (_config.BigMapPatrolZones ? ZoneKinds.Patrols : ZoneKinds.None) |
        (_config.BigMapSanctuaries ? ZoneKinds.Sanctuaries : ZoneKinds.None);

    /// <summary>The zone layers: drawn for the current calibration (once per calibration, ZoneAsset), scaled to the zoom by MapCanvas.</summary>
    private void ShowZones()
    {
        var shown = ShownZones;
        var shapes = shown != ZoneKinds.None && _poll.Calibration is { } cal ? ZoneAsset.ShapesFor(cal) : null;
        Map.ShowZones(shapes, shown, hostScale: 1);
    }

    /// <summary>Every waypoint with its Show tick, and the tracked one — while the Waypoints layer is on.</summary>
    private List<Waypoint> ShownWaypoints() =>
        _config.BigMapWaypoints
            ? _library.Items.Where(w => w.Visible || w.Id == _config.TrackedWaypointId).ToList()
            : new List<Waypoint>();

    private void ShowWaypoints()
    {
        Map.ShowWaypoints(ShownWaypoints(), _config.TrackedWaypointId);
        if (_poll.Calibration is { } cal) Map.PlaceWaypoints(cal, MarkerView, edgeIndicator: false, glide: null);
    }

    private void ShowFriends()
    {
        var drawn = _friends is null || !_config.BigMapFriends
            ? Array.Empty<FriendState>()
            : _friends.Where(f => f.OnMap && f.SteamId is not null && _book.ShowsOnMap(f.SteamId));
        Map.ShowFriends(drawn, _config.TrackedFriendSteamId, FriendBrush, _config.BigMapNames ? FriendName : null);
    }

    private void PlaceFriends(TimeSpan? glide)
    {
        if (_poll.Calibration is { } cal) Map.PlaceFriends(cal, MarkerView, edgeIndicator: false, glide, _config.MinimapYawOffsetDegrees);
    }

    /// <summary>Your arrow at your last fix; the first placing (and every zoom) snaps, later ones glide like the minimap's.</summary>
    private void PlaceArrow(TimeSpan? glide)
    {
        if (!_hasView || _lastFix is not { } fix)
        {
            Map.ArrowVisible = false;
            return;
        }
        var step = _hasFix ? glide : null;
        Map.ArrowVisible = true;
        Map.MoveArrow(new Point(fix.Fx * _view.MapSize, fix.Fy * _view.MapSize), step);
        Map.TurnArrow(fix.Yaw, step);
        _hasFix = true;
    }

    private void RenderTrail()
    {
        var keep = BreadcrumbTrail.KeepFor(_config.MinimapTrailMinutes); // the shared trail length (D7)
        var bands = _hasView && _config.BigMapTrail && keep > TimeSpan.Zero && _lastFix is not null &&
                    _poll.Calibration is { } cal && _trail.Points.Count > 1
            ? MapCanvas.TrailBands(_trail.Points, cal, _view.MapSize, keep, DateTime.UtcNow)
            : new[] { new PointCollection(), new PointCollection(), new PointCollection() };
        Map.ShowTrail(bands[0], bands[1], bands[2]);
    }

    /// <summary>
    /// The two highlights while the Areas layer is on: your area's outline
    /// (the soft light line) and the area under the cursor (orange), the
    /// cursor's winning where they are the same — the minimap's edit-mode
    /// rule, here all the time, since the map is interactive.
    /// </summary>
    private void UpdateOutlines(bool force)
    {
        if (!_hasView) return;
        var map = _config.BigMapAreas ? AreaMapAsset.Shared : null;
        var hover = map is not null && _hoverSpot is { } spot ? map.IndexAt(spot.Fx, spot.Fy) : AreaMap.None;
        var own = map is not null && _lastFix is not null ? map.IndexOf(_area) : AreaMap.None;
        if (own == hover) own = AreaMap.None;
        Map.ShowOutlines(own, hover, hostScale: 1, force);
    }

    private void SetHover((double Fx, double Fy)? spot)
    {
        if (spot == _hoverSpot) return;
        _hoverSpot = spot;
        UpdateOutlines(force: false);
    }

    // ---- Names --------------------------------------------------------------------

    /// <summary>
    /// The Names layer, laid out for the current zoom (a pan moves every box
    /// together, so it needs none): area names centred on the legend's label
    /// points, waypoint names beside their dots (right, else left, above,
    /// below). LabelLayout keeps them apart; what doesn't fit is left out
    /// and appears on zooming in. Priorities: the tracked waypoint, then the
    /// areas (a bigger area's name first), then the other waypoints.
    /// Friends' names ride their arrows (MapCanvas.ShowFriends).
    /// </summary>
    private void LayOutLabels()
    {
        if (!_hasView || !_config.BigMapNames || _poll.Calibration is not { } cal)
        {
            Map.ShowLabels(Array.Empty<MapLabel>());
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var size = _view.MapSize;
        var requests = new List<LabelRequest>();
        var labels = new List<(string Text, bool IsArea, bool Strong)>();

        if (AreaMapAsset.Shared is { } map)
        {
            var pixels = AreaPixels(map);
            var total = Math.Max(1.0, pixels.Sum());
            for (var i = 0; i < map.Areas.Count; i++)
            {
                var area = map.Areas[i];
                var (fx, fy) = cal.ToFraction(area.X, area.Y);
                var text = area.Name.ToUpperInvariant();
                var box = MapCanvas.MeasureLabel(text, isArea: true, strong: false, dpi);
                var at = new Point(fx * size - box.Width / 2, fy * size - box.Height / 2);
                requests.Add(new LabelRequest(labels.Count, 2 + pixels[i] / total, new[] { new Rect(at, box) }));
                labels.Add((text, true, false));
            }
        }

        foreach (var w in ShownWaypoints())
        {
            var tracked = w.Id == _config.TrackedWaypointId;
            var (fx, fy) = cal.ToFraction(w.X, w.Y);
            var p = new Point(fx * size, fy * size);
            var box = MapCanvas.MeasureLabel(w.Name, isArea: false, strong: tracked, dpi);
            var r = tracked ? 10 : 6; // clear of the dot, or of the tracked one's ring
            requests.Add(new LabelRequest(labels.Count, tracked ? 4 : 1, new[]
            {
                new Rect(new Point(p.X + r, p.Y - box.Height / 2), box),
                new Rect(new Point(p.X - r - box.Width, p.Y - box.Height / 2), box),
                new Rect(new Point(p.X - box.Width / 2, p.Y - r - box.Height), box),
                new Rect(new Point(p.X - box.Width / 2, p.Y + r), box),
            }));
            labels.Add((w.Name, false, tracked));
        }

        var placed = LabelLayout.Place(requests, LabelGap);
        Map.ShowLabels(placed.Select(l => new MapLabel(labels[l.Id].Text, l.Box.TopLeft, labels[l.Id].IsArea, labels[l.Id].Strong)));
    }

    /// <summary>Each area's pixel count on the bundled map, counted once.</summary>
    private static int[] AreaPixels(AreaMap map)
    {
        if (_areaPixels is null || !ReferenceEquals(_areaPixelsOf, map))
        {
            _areaPixels = map.PixelCounts();
            _areaPixelsOf = map;
        }
        return _areaPixels;
    }

    // ---- Header, readout, scale bar ----------------------------------------------

    /// <summary>"you are in Swamps"; "Uncharted", dimmed, where no area is named; "not in game".</summary>
    private void UpdateHeader()
    {
        AreaLine.Inlines.Clear();
        if (_lastFix is null)
        {
            AreaLine.Inlines.Add(new Run("not in game") { Foreground = Grey });
        }
        else if (_area is null)
        {
            AreaLine.Inlines.Add(new Run("Uncharted") { Foreground = Light });
            AreaLine.Opacity = UnchartedOpacity;
            return;
        }
        else
        {
            AreaLine.Inlines.Add(new Run("you are in ") { Foreground = Grey });
            AreaLine.Inlines.Add(new Run(_area) { Foreground = Light });
        }
        AreaLine.Opacity = 1;
    }

    /// <summary>
    /// What is under the cursor, beside it: the friend or waypoint there by
    /// name, else the area ("Uncharted", dimmed, where none is named), and how
    /// far it is from you while you are in game. No walking time: with the
    /// map open the dino stands still, so there is no pace to go by.
    /// </summary>
    private void ShowReadout(Point pos)
    {
        if (!_hasView || _poll.Calibration is not { } cal) return;
        var (fx, fy) = _view.FractionAt(pos);
        if (fx < 0 || fy < 0 || fx > 1 || fy > 1)
        {
            Readout.Visibility = Visibility.Collapsed;
            SetHover(null);
            return;
        }
        SetHover((fx, fy));

        ReadoutText.Inlines.Clear();
        (double X, double Y) spot;
        var friend = Map.FriendAt(pos, SnapRadius);
        var waypoint = friend is null ? Map.WaypointAt(pos, SnapRadius) : null;
        if (friend is { X: { } x, Y: { } y })
        {
            ReadoutText.Inlines.Add(new Run("▲ " + FriendName(friend)) { Foreground = FriendBrush(friend) });
            spot = (x, y);
        }
        else if (waypoint is not null)
        {
            ReadoutText.Inlines.Add(new Run("◆ " + waypoint.Name) { Foreground = MapBrushes.Of(waypoint.Colour) });
            spot = (waypoint.X, waypoint.Y);
        }
        else
        {
            var name = AreaMapAsset.Shared?.NameAt(fx, fy);
            ReadoutText.Inlines.Add(new Run(name ?? "Uncharted") { Foreground = name is null ? Grey : Light });
            spot = cal.ToWorld(fx, fy);
        }
        if (_lastWorld is { } me)
        {
            var meters = WaypointLibrary.Distance(spot.X, spot.Y, me.X, me.Y) / 100;
            var distance = meters >= 1000 ? $"{meters / 1000:0.0} km" : $"{meters:0} m";
            ReadoutText.Inlines.Add(new Run($"  ·  {distance} from you") { Foreground = Grey });
        }

        // Placed by a render transform, not a margin: a margin is part of an
        // element's measured size, so the box measured as big as its last
        // offset, "didn't fit" and was clamped to the map's edge — the jump
        // the owner saw (84 of 154 test positions, Oct 9 2026).
        // And measured afresh: a direct Measure with the same available size
        // returns the CACHED size when only the text inside changed, so a
        // flip near the right edge used the last text's width.
        Readout.Visibility = Visibility.Visible;
        ReadoutText.InvalidateMeasure();
        Readout.InvalidateMeasure();
        Readout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var box = Readout.DesiredSize;
        var left = pos.X + ReadoutGap;
        var top = pos.Y + ReadoutGap;
        if (left + box.Width > MapHost.ActualWidth - 4) left = pos.X - ReadoutGap - box.Width; // flip to stay on the map
        if (top + box.Height > MapHost.ActualHeight - 4) top = pos.Y - ReadoutGap - box.Height;
        _readoutAt.X = Math.Round(Math.Clamp(left, 4, Math.Max(4, MapHost.ActualWidth - box.Width - 4)));
        _readoutAt.Y = Math.Round(Math.Clamp(top, 4, Math.Max(4, MapHost.ActualHeight - box.Height - 4)));
    }

    /// <summary>A round real-world length for the current zoom, bottom-left (the minimap's ScaleBar).</summary>
    private void UpdateScaleBar()
    {
        var (meters, pixels) = _poll.Calibration is { MapSize: > 0 } cal
            ? ScaleBar.Pick(100 * Math.Abs(cal.ScaleX) * _view.MapSize / cal.MapSize, Math.Min(MapHost.ActualWidth * 0.2, 160))
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
}
