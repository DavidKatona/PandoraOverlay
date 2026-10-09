using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// Where the map sits in a map panel: the panel's size, the map's rendered
/// side (the map picture is square) and the panel point its top-left corner
/// sits at. The one home of the panel ⇄ map-fraction arithmetic, for the
/// minimap's two views and the big map (pan, wheel zoom around the cursor,
/// fit to the land, a pan limit). Fractions are 0–1 across and down the map,
/// as MapCalibration.ToFraction gives them. Pure, tested; it knows no window.
/// Zoom is the map's side over the panel's shorter side, so at 1 the whole
/// map fits — the minimap's centred zoom means the same.
/// </summary>
public readonly record struct MapViewport(Size Panel, double MapSize, Point Offset)
{
    /// <summary>The panel's shorter side: the whole map fits it at zoom 1.</summary>
    public double Side => Math.Min(Panel.Width, Panel.Height);

    public double Zoom => MapSize / Side;

    public Point Center => new(Panel.Width / 2, Panel.Height / 2);

    /// <summary>The whole map, fitted to the panel's shorter side and centred along the longer one. On the minimap's square panel: the island view, offset 0.</summary>
    public static MapViewport Whole(Size panel)
    {
        var side = Math.Min(panel.Width, panel.Height);
        return new MapViewport(panel, side, new Point((panel.Width - side) / 2, (panel.Height - side) / 2));
    }

    /// <summary>
    /// The map at a zoom with the given map point at the panel's centre: the
    /// minimap's centred view. Written exactly as the minimap computes it
    /// (side × zoom, then half the panel minus the point), so moving the
    /// minimap onto it changes no pixel.
    /// </summary>
    public static MapViewport Centered(Size panel, double zoom, double fx, double fy)
    {
        var mapSize = Math.Min(panel.Width, panel.Height) * zoom;
        return new MapViewport(panel, mapSize, new Point(panel.Width / 2 - fx * mapSize, panel.Height / 2 - fy * mapSize));
    }

    /// <summary>
    /// The view that fits part of the map (in fractions, e.g. LandBounds)
    /// into the panel, as large as it fits and centred on it, the zoom
    /// clamped. An empty part gives the whole map, centred.
    /// </summary>
    public static MapViewport Fit(Size panel, Rect part, double minZoom, double maxZoom)
    {
        if (part.IsEmpty || part.Width <= 0 || part.Height <= 0) return Centered(panel, Math.Clamp(1, minZoom, maxZoom), 0.5, 0.5);
        var side = Math.Min(panel.Width, panel.Height);
        var zoom = Math.Clamp(Math.Min(panel.Width / (part.Width * side), panel.Height / (part.Height * side)), minZoom, maxZoom);
        return Centered(panel, zoom, part.X + part.Width / 2, part.Y + part.Height / 2);
    }

    /// <summary>A map point → the panel point it is drawn at.</summary>
    public Point ToPanel(double fx, double fy) => new(Offset.X + fx * MapSize, Offset.Y + fy * MapSize);

    /// <summary>The map fraction under a panel point. Not clamped: past the map's edge it runs below 0 or above 1.</summary>
    public (double Fx, double Fy) FractionAt(Point p) => ((p.X - Offset.X) / MapSize, (p.Y - Offset.Y) / MapSize);

    /// <summary>Whether a panel point is on the panel, its edges included. Slack widens it, so a marker half past the edge still counts.</summary>
    public bool InPanel(Point p, double slack = 0) =>
        p.X >= -slack && p.Y >= -slack && p.X <= Panel.Width + slack && p.Y <= Panel.Height + slack;

    /// <summary>A panel point pulled inside the panel by a margin: the tracked marker's edge indicator.</summary>
    public Point ClampIntoPanel(Point p, double margin) =>
        new(Math.Clamp(p.X, margin, Panel.Width - margin), Math.Clamp(p.Y, margin, Panel.Height - margin));

    /// <summary>The part of the map in view, in fractions; it reaches past 0–1 where the panel shows beyond the map.</summary>
    public Rect VisibleFractions
    {
        get
        {
            var (left, top) = FractionAt(new Point(0, 0));
            var (right, bottom) = FractionAt(new Point(Panel.Width, Panel.Height));
            return new Rect(new Point(left, top), new Point(right, bottom));
        }
    }

    /// <summary>The map moved by a drag: every point follows the cursor.</summary>
    public MapViewport PanBy(Vector delta) => this with { Offset = Offset + delta };

    /// <summary>
    /// Zooms by a factor around a panel point, the cursor: the map point
    /// under it stays under it. The zoom is clamped to [min, max], and a step
    /// that the clamp turns into no change returns this view as it is, so
    /// turning the wheel at the limit can't creep.
    /// </summary>
    public MapViewport ZoomAround(Point anchor, double factor, double minZoom, double maxZoom)
    {
        var zoom = Math.Clamp(Zoom * factor, minZoom, maxZoom);
        if (Math.Abs(zoom - Zoom) <= 1e-12 * zoom) return this;
        var mapSize = Side * zoom;
        var (fx, fy) = FractionAt(anchor);
        return this with { MapSize = mapSize, Offset = new Point(anchor.X - fx * mapSize, anchor.Y - fy * mapSize) };
    }

    /// <summary>
    /// Keeps the map point at the panel's centre inside a part of the map
    /// (the land's bounds), so a drag can't lose the island: past an edge the
    /// view moves back just far enough, and inside it nothing changes.
    /// </summary>
    public MapViewport ClampPan(Rect part)
    {
        if (part.IsEmpty) return this;
        var (fx, fy) = FractionAt(Center);
        var cx = Math.Clamp(fx, part.Left, part.Right);
        var cy = Math.Clamp(fy, part.Top, part.Bottom);
        if (cx == fx && cy == fy) return this;
        return this with { Offset = new Point(Center.X - cx * MapSize, Center.Y - cy * MapSize) };
    }

    /// <summary>
    /// The zoom at which one pixel of the map picture covers the given number
    /// of panel units: the sharpness cap. A 1000 px picture on a 900 unit
    /// side reaches 3 units per picture pixel at zoom 3.33.
    /// </summary>
    public static double ZoomForDensity(double side, int picturePixels, double unitsPerPicturePixel) =>
        unitsPerPicturePixel * picturePixels / side;
}
