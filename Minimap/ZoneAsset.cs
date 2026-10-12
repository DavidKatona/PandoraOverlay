using System.IO;
using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>The zone layers' shapes for one calibration, one geometry per path, in zone units (map fraction × ZoneAsset.Units); a kind the file lacks is null.</summary>
public sealed record ZoneShapes(Geometry? Migrations, Geometry? MassMigrations, Geometry? Patrols, Geometry? Sanctuaries);

/// <summary>
/// The bundled zones (Assets/zones.json, an embedded resource like
/// areas.json, so the tests read the very file that ships) and their
/// drawing: the WPF half of ZoneSet. Loaded once, on first use (the map's
/// first opening); fail-soft: a missing or unreadable file only greys the
/// zone ticks. Drawn for a calibration through MapCalibration.ToFraction,
/// as vector geometry — MapCanvas scales it to the map's rendered size, so a
/// zone stays sharp at any zoom (the owner's reason for vectors, Oct 12 2026).
/// </summary>
public static class ZoneAsset
{
    public const string Resource = "zones.json";

    /// <summary>
    /// Zone units per side of the map: the shapes are map fractions times
    /// this. Not 0–1 fractions themselves: WPF keeps geometry in single
    /// precision and flattens curves and hit-tests with tolerances in the
    /// geometry's OWN units (0.25 by default), which at 0–1 made a circle of
    /// radius 0.05 "contain" a point 0.06 from its centre. At 10,000 units the
    /// tolerance is a fraction of a screen pixel at the deepest zoom. A unit
    /// of our own, not anything about the map picture.
    /// </summary>
    public const double Units = 10_000;

    private static readonly Lazy<ZoneSet?> Bundled = new(LoadBundled);
    private static MapCalibration? _drawnFor;
    private static ZoneShapes? _drawn;

    /// <summary>The app's one copy: read the first time anything asks.</summary>
    public static ZoneSet? Shared => Bundled.Value;

    public static ZoneSet? LoadBundled()
    {
        try
        {
            using var stream = typeof(ZoneAsset).Assembly.GetManifestResourceStream(Resource);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return ZoneSet.Parse(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The bundled zones drawn for a calibration, kept for the last one (a new calibration draws afresh). Null without zones.</summary>
    public static ZoneShapes? ShapesFor(MapCalibration calibration)
    {
        if (Shared is not { } set) return null;
        if (_drawn is null || _drawnFor != calibration)
        {
            _drawn = Draw(set, calibration);
            _drawnFor = calibration;
        }
        return _drawn;
    }

    /// <summary>
    /// One frozen geometry per path: migrations, mass migrations (their own
    /// path for the dashed edge), patrol zones, sanctuaries. Every shape runs
    /// clockwise on screen, circles included (two arcs, not an
    /// EllipseGeometry, whose direction is WPF's to pick), and the fill rule
    /// is non-zero: zones of one kind that overlap fill as one, where mixed
    /// windings would cancel into a hole.
    /// </summary>
    public static ZoneShapes Draw(ZoneSet set, MapCalibration calibration)
    {
        Geometry? Of(Func<Zone, bool> pick)
        {
            var zones = set.Zones.Where(pick).ToList();
            if (zones.Count == 0) return null;
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var context = geometry.Open())
            {
                foreach (var zone in zones) Figure(context, zone, calibration);
            }
            geometry.Freeze();
            return geometry;
        }

        return new ZoneShapes(
            Of(z => z.Kind == ZoneKind.Migration && !z.Mass),
            Of(z => z.Kind == ZoneKind.Migration && z.Mass),
            Of(z => z.Kind == ZoneKind.Patrol),
            Of(z => z.Kind == ZoneKind.Sanctuary));
    }

    private static void Figure(StreamGeometryContext context, Zone zone, MapCalibration calibration)
    {
        if (zone.Circle is { } c)
        {
            var centre = At(c.X, c.Y);
            var radius = new Size(c.R * Math.Abs(calibration.ScaleX) / calibration.MapSize * Units, c.R * Math.Abs(calibration.ScaleY) / calibration.MapSize * Units);
            context.BeginFigure(new Point(centre.X + radius.Width, centre.Y), isFilled: true, isClosed: true);
            context.ArcTo(new Point(centre.X - radius.Width, centre.Y), radius, 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
            context.ArcTo(new Point(centre.X + radius.Width, centre.Y), radius, 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
            return;
        }

        var points = zone.Polygon!.Select(p => At(p.X, p.Y)).ToList();
        if (ZoneSet.SignedArea(points.Select(p => (p.X, p.Y)).ToList()) < 0) points.Reverse();
        context.BeginFigure(points[0], isFilled: true, isClosed: true);
        context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);

        Point At(double x, double y)
        {
            var (fx, fy) = calibration.ToFraction(x, y);
            return new Point(fx * Units, fy * Units);
        }
    }
}
