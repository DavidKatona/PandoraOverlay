using System.Windows;

namespace PandoraOverlay;

/// <summary>One leg of the route beyond the current stop, in map pixels: fraction × the map's rendered size, as the trail's points are.</summary>
public readonly record struct RouteLeg(Point From, Point To, int Colour);

/// <summary>
/// What the maps draw of a route, pure and shared: both maps show the whole
/// remaining route (the owner's call). The line from your arrow to the current
/// stop is the ordinary tracking line, so a leg here starts AT the current
/// stop; a paused route keeps these and loses only that line. A passed stop
/// draws nothing of its own: it goes back to being a waypoint like any other.
/// </summary>
public static class RouteDrawing
{
    /// <summary>
    /// Each leg from the current stop on, in its TARGET stop's colour, as the
    /// tracking line wears its target's. A leg between two stops at the same
    /// place (a waypoint twice in a row) has nothing to draw.
    /// </summary>
    public static IReadOnlyList<RouteLeg> Legs(Route route, Func<Guid, Waypoint?> find, MapCalibration cal, double mapSize)
    {
        var legs = new List<RouteLeg>();
        Waypoint? from = null;
        foreach (var id in route.Upcoming)
        {
            if (find(id) is not { } to) continue;
            if (from is not null && (from.X != to.X || from.Y != to.Y))
            {
                legs.Add(new RouteLeg(ToMap(from, cal, mapSize), ToMap(to, cal, mapSize), to.Colour));
            }
            from = to;
        }
        return legs;
    }

    /// <summary>The waypoints of the stops not yet passed, each once: always drawn, whatever the visibility setting says, as the tracked one is.</summary>
    public static IReadOnlyList<Waypoint> UpcomingWaypoints(Route route, Func<Guid, Waypoint?> find)
    {
        var seen = new HashSet<Guid>();
        var waypoints = new List<Waypoint>();
        foreach (var id in route.Upcoming)
        {
            if (seen.Add(id) && find(id) is { } w) waypoints.Add(w);
        }
        return waypoints;
    }

    /// <summary>
    /// The stop numbers the map writes beside its waypoints (never the
    /// minimap: no room), by waypoint: "4", or "3, 7" for a waypoint you pass
    /// twice. Counted from the route's first stop, passed ones included, so a
    /// number means the same on the map, the page and the footer's "3/7".
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> Numbers(Route route)
    {
        var numbers = new Dictionary<Guid, string>();
        for (var i = route.Current; i < route.Count; i++)
        {
            var id = route.Stops[i];
            numbers[id] = numbers.TryGetValue(id, out var before) ? $"{before}, {i + 1}" : (i + 1).ToString();
        }
        return numbers;
    }

    /// <summary>A name label with its stop number in front: "3 · Delta", the number alone with the map's Waypoint names off, the name alone for a waypoint not on the route.</summary>
    public static string Label(string? number, string? name) =>
        number is null ? name ?? "" : name is null ? number : $"{number} · {name}";

    private static Point ToMap(Waypoint w, MapCalibration cal, double mapSize)
    {
        var (fx, fy) = cal.ToFraction(w.X, w.Y);
        return new Point(fx * mapSize, fy * mapSize);
    }
}
