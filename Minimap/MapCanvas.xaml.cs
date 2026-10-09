using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>A name on the map: its text, where its box's top-left goes (in the canvas's marker space), and its look.</summary>
public readonly record struct MapLabel(string Text, Point At, bool IsArea, bool Strong);

/// <summary>
/// The map drawing every map shares (big map plan, phase 2): the island
/// picture with what moves with it — the heatmap, the area borders and the
/// two outlines, the breadcrumb trail — and above it the waypoint markers,
/// the names, the friends' arrows and yours. It draws and glides; it decides
/// nothing: which waypoints and friends, which view, when to glide and for
/// how long are its map's business (MinimapWindow, BigMapWindow). It knows
/// no config, no PollService and no footer. Positions are in its own panel
/// coordinates, which are its host's: it fills the host from the top-left.
/// Moved out of MinimapWindow without changing a pixel (golden renders).
/// The big map switches it to MAP SPACE (UseMapSpace): markers, names,
/// friends and your arrow then ride the map's own translate, so a pan moves
/// one transform and re-places nothing, and the still layers are cached.
/// </summary>
public partial class MapCanvas : UserControl
{
    /// <summary>How wide a border line comes out on screen, whatever the zoom and the host's scale: thin, but not lost.</summary>
    private const double BorderScreenPixels = 1.2;

    /// <summary>A highlighted outline is a little wider than the borders it lies on.</summary>
    private const double OutlineScreenPixels = 1.5;

    /// <summary>
    /// The bundled copy of the site's island map (Assets/map.png), decoded
    /// once at native resolution — so a zoomed view stays sharp — and shared
    /// by every map.
    /// </summary>
    private static readonly Lazy<BitmapImage> Island = new(() =>
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri("pack://application:,,,/Assets/map.png");
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    });

    private readonly TranslateTransform _mapTranslate = new();   // the map and everything in map space
    private readonly RotateTransform _arrowRotate = new();
    private readonly TranslateTransform _arrowTranslate = new();
    private int _ownOutline = AreaMap.None;   // the area each highlight path draws now
    private int _hoverOutline = AreaMap.None;
    private bool _mapSpace;   // markers, names, friends and arrow ride the map's translate (the big map)
    private int? _glideFps;   // a cap on every glide's frame rate (the big map: 30)

    public MapCanvas()
    {
        InitializeComponent();
        MapImage.Source = Island.Value;
        MapImage.RenderTransform = _mapTranslate;
        HeatmapImage.RenderTransform = _mapTranslate;    // the heatmap pans with the map
        AreaBordersPath.RenderTransform = _mapTranslate; // and the area borders, with their two highlights
        AreaOwnPath.RenderTransform = _mapTranslate;
        AreaHoverPath.RenderTransform = _mapTranslate;
        TrailOld.RenderTransform = TrailMid.RenderTransform = TrailNew.RenderTransform = _mapTranslate; // so does the trail
        PlayerArrow.RenderTransform = new TransformGroup { Children = { _arrowRotate, _arrowTranslate } };
    }

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The big map's mode, set once before use. Markers, names, friends and
    /// your arrow live in MAP space — placed at fraction × MapSize, under
    /// the map's own translate — so a pan moves one transform and nothing is
    /// re-placed; their hit tests take the pan into account. The two still
    /// layers (the map with what moves with it; the markers with the names)
    /// are cached as bitmaps and glides capped at a frame rate: the perf
    /// spike's choice (Oct 9 2026), about 5% of one core while panning. The
    /// caches snap to device pixels, so a cached name is never resampled
    /// between pixels; the names' text is pixel-snapped too.
    /// </summary>
    public void UseMapSpace(int glideFrameRate)
    {
        _mapSpace = true;
        _glideFps = glideFrameRate;
        StillMarks.RenderTransform = _mapTranslate;
        FriendLayer.RenderTransform = _mapTranslate;
        PlayerArrow.RenderTransform = new TransformGroup { Children = { _arrowRotate, _arrowTranslate, _mapTranslate } };
        MapLayer.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        StillMarks.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        TextOptions.SetTextFormattingMode(LabelLayer, TextFormattingMode.Display);
        TextOptions.SetTextFormattingMode(FriendLayer, TextFormattingMode.Display);
    }

    // ---- The map ------------------------------------------------------------

    /// <summary>The map picture's own size in pixels (it is square): the sharpness cap counts in these.</summary>
    public static int PicturePixels => Island.Value.PixelWidth;

    /// <summary>The map's rendered side (the picture is square).</summary>
    public double MapSize => MapImage.Width;

    /// <summary>Where the map's top-left sits NOW: mid-glide, the point it has reached, not its target.</summary>
    /// <remarks>
    /// Some of the minimap's redraws (a library, tracking or calibration
    /// change) place the markers against this rather than the glide's target,
    /// exactly as they did before the drawing moved here; the golden renders
    /// hold the minimap to that.
    /// </remarks>
    public Point MapOffset => new(_mapTranslate.X, _mapTranslate.Y);

    /// <summary>The map picture's rendered side, the heatmap's with it. The borders, outlines and trail follow through their own calls.</summary>
    public void SetMapSize(double size)
    {
        MapImage.Width = MapImage.Height = size;
        HeatmapImage.Width = HeatmapImage.Height = size;
    }

    /// <summary>Moves the map and everything in map space to an offset: gliding over the given time, or at once.</summary>
    public void MoveMap(Point offset, TimeSpan? glide) => Move(_mapTranslate, offset, glide);

    // ---- Your arrow -----------------------------------------------------------

    public bool ArrowVisible
    {
        set => PlayerArrow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Moves your arrow to a point (panel space; map space with UseMapSpace): gliding, or at once.</summary>
    public void MoveArrow(Point at, TimeSpan? glide) => Move(_arrowTranslate, at, glide);

    /// <summary>Turns your arrow to a screen heading; a glide takes the shortest way round.</summary>
    public void TurnArrow(double degrees, TimeSpan? glide)
    {
        if (glide is { } d)
        {
            var delta = ((degrees - _arrowRotate.Angle) % 360 + 540) % 360 - 180;
            Animate(_arrowRotate, RotateTransform.AngleProperty, _arrowRotate.Angle + delta, d);
            return;
        }
        _arrowRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _arrowRotate.Angle = degrees;
    }

    /// <summary>Stops every glide where it stands: the map, your arrow, the markers and the friends.</summary>
    public void ClearAnimations()
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
        foreach (var m in _friendMarkers.Values)
        {
            m.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            m.Translate.BeginAnimation(TranslateTransform.YProperty, null);
            m.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        _arrowRotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    // ---- Layers ---------------------------------------------------------------

    /// <summary>The site's heatmap picture over the map at 55%; null — or bytes that don't decode — hides the layer.</summary>
    public void ShowHeatmap(byte[]? png)
    {
        if (png is null)
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

    /// <summary>
    /// The border layer: a dark outline round every area, over the map (and
    /// the heatmap) and panning with it. Lines, not the area colours — those
    /// are picked to be told apart and would bury the terrain. VECTOR lines
    /// (AreaBorders): the geometry is in map pixels and scaled to the map's
    /// rendered size through the GEOMETRY's transform, which moves the
    /// points but leaves the stroke alone, so the line is the same thin
    /// width at every zoom and stays sharp. The width undoes the host's own
    /// scale (<paramref name="hostScale"/>), so it is about a screen pixel at
    /// any widget scale. Call again when the map's size changes.
    /// <paramref name="on"/> is tested BEFORE AreaMapAsset.Shared on purpose:
    /// with the area features off, the area map is never decoded.
    /// </summary>
    public void ShowBorders(bool on, double hostScale)
    {
        if (!on || AreaMapAsset.Shared is not { } map || AreaMapAsset.Borders is not { } lines)
        {
            AreaBordersPath.Visibility = Visibility.Collapsed;
            AreaBordersPath.Data = null;
            return;
        }
        var scale = MapImage.Width / map.Size;
        AreaBordersPath.Data = new GeometryGroup { Children = { lines }, Transform = new ScaleTransform(scale, scale) };
        AreaBordersPath.StrokeThickness = BorderScreenPixels / Math.Max(0.1, hostScale);
        AreaBordersPath.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The two highlights on the border layer: an area's outline in the soft
    /// light line (yours) and one in the edit orange (the cursor's), each the
    /// very lines of the border layer with that area on a side
    /// (AreaMapAsset.OutlineOf), so it sits exactly on them. AreaMap.None
    /// hides one. Redrawn only when its area changed, unless forced (the
    /// map's size or the host's scale changed).
    /// </summary>
    public void ShowOutlines(int own, int hover, double hostScale, bool force)
    {
        DrawOutline(AreaOwnPath, own, ref _ownOutline, hostScale, force);
        DrawOutline(AreaHoverPath, hover, ref _hoverOutline, hostScale, force);
    }

    private void DrawOutline(Path path, int area, ref int shown, double hostScale, bool force)
    {
        if (area == shown && !force) return;
        shown = area;
        if (AreaMapAsset.OutlineOf(area) is not { } outline)
        {
            path.Visibility = Visibility.Collapsed;
            path.Data = null;
            return;
        }
        var scale = MapImage.Width / AreaMapAsset.Shared!.Size; // an outline exists, so the map does
        path.Data = new GeometryGroup { Children = { outline }, Transform = new ScaleTransform(scale, scale) };
        path.StrokeThickness = OutlineScreenPixels / Math.Max(0.1, hostScale);
        path.Visibility = Visibility.Visible;
    }

    /// <summary>The breadcrumb trail's three age bands, newest first, in map pixels (fraction × MapSize); they pan with the map.</summary>
    public void ShowTrail(PointCollection newest, PointCollection middle, PointCollection oldest)
    {
        TrailNew.Points = newest;
        TrailMid.Points = middle;
        TrailOld.Points = oldest;
    }

    /// <summary>
    /// The trail split into three age bands for a map drawn at a size,
    /// newest first, in map pixels; each band starts on the previous band's
    /// last point so they meet. One polyline can't fade along its length,
    /// so the oldest band is drawn faintest.
    /// </summary>
    public static PointCollection[] TrailBands(IReadOnlyList<(DateTime At, double X, double Y)> points, MapCalibration cal,
                                               double mapSize, TimeSpan keep, DateTime now)
    {
        var bands = new[] { new PointCollection(), new PointCollection(), new PointCollection() }; // newest → oldest
        Point? previous = null;
        var previousBand = -1;
        foreach (var (at, x, y) in points)
        {
            var (fx, fy) = cal.ToFraction(x, y);
            var point = new Point(fx * mapSize, fy * mapSize);
            var band = Math.Clamp((int)((now - at).TotalSeconds / keep.TotalSeconds * 3), 0, 2);
            if (band != previousBand && previous is { } join) bands[band].Add(join);
            bands[band].Add(point);
            previous = point;
            previousBand = band;
        }
        return bands;
    }

    // ---- Names ------------------------------------------------------------------

    /// <summary>
    /// The names to draw (already decluttered, LabelLayout), replacing the
    /// last set; none clears them. All drawn by the one NameLayer: crisp
    /// outlined text, each name formatted once and reused at every zoom.
    /// </summary>
    public void ShowLabels(IEnumerable<MapLabel> labels) =>
        LabelLayer.Show(labels.Select(l => (l.Text, LabelSize(l.IsArea), l.IsArea || l.Strong, LabelBrush(l.IsArea), l.At)));

    /// <summary>The box a label of this text and kind will take, its outline included: what LabelLayout places.</summary>
    public static Size MeasureLabel(string text, bool isArea, bool strong, double pixelsPerDip) =>
        NameLayer.Measure(text, LabelSize(isArea), isArea || strong, LabelBrush(isArea), pixelsPerDip);

    private static double LabelSize(bool isArea) => isArea ? NameLayer.AreaFontSize : NameLayer.NameFontSize;

    private static Brush LabelBrush(bool isArea) => isArea ? NameLayer.AreaBrush : NameLayer.NameBrush;

    // ---- Glides ---------------------------------------------------------------

    private void Move(TranslateTransform transform, Point to, TimeSpan? glide)
    {
        if (glide is { } d)
        {
            Animate(transform, TranslateTransform.XProperty, to.X, d);
            Animate(transform, TranslateTransform.YProperty, to.Y, d);
            return;
        }
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = to.X;
        transform.Y = to.Y;
    }

    private void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation(to, duration);
        if (_glideFps is { } fps) Timeline.SetDesiredFrameRate(animation, fps);
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
