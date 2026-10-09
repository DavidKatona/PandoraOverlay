using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Path = System.Windows.Shapes.Path;

namespace PandoraOverlay;

/// <summary>
/// The map drawing every map shares (big map plan, phase 2): the island
/// picture with what moves with it — the heatmap, the area borders and the
/// two outlines, the breadcrumb trail — and above it the waypoint markers,
/// the friends' arrows and yours. It draws and glides; it decides nothing:
/// which waypoints and friends, which view, when to glide and for how long
/// are its map's business (MinimapWindow; the big map next). It knows no
/// config, no PollService and no footer. Positions are in its own panel
/// coordinates, which are its host's: it fills the host from the top-left.
/// Moved out of MinimapWindow without changing a pixel (golden renders).
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

    // ---- The map ------------------------------------------------------------

    /// <summary>The map's rendered side (the picture is square).</summary>
    public double MapSize => MapImage.Width;

    /// <summary>Where the map's top-left sits NOW: mid-glide, the point it has reached, not its target.</summary>
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

    /// <summary>Moves your arrow to a panel point: gliding, or at once.</summary>
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

    // ---- Glides ---------------------------------------------------------------

    private static void Move(TranslateTransform transform, Point to, TimeSpan? glide)
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

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration), HandoffBehavior.SnapshotAndReplace);
}
