namespace PandoraOverlay;

/// <summary>
/// A shared route as text: one share-a-spot code per stop, one per line (the
/// owner's call). Anyone can read and edit it in a chat, each line still works
/// as a single spot, and an older copy pasting it simply gets the first stop.
/// Twenty stops are about 600 characters; 64 stops with long names can pass
/// Discord's 2000. Colours don't travel: the code has none, so pasted stops
/// take the library's next ones. Never touches the site, like the spot code.
/// <para>
/// Stricter than <see cref="ShareCode.TryParse(string?, out double, out double, out string)"/>,
/// which takes the first pair of numbers anywhere in a text and is right to for
/// one spot pasted on purpose. A route is pasted with whatever surrounds it in a
/// chat ("meet at 20, 21"), so here a line counts only with the code's prefix.
/// </para>
/// </summary>
public static class RouteCode
{
    /// <summary>One stop of a pasted route. World cm, as the spot code gives them; the name may be empty.</summary>
    public sealed record Stop(double X, double Y, string Name);

    public static string Format(IEnumerable<Waypoint> stops) =>
        string.Join("\n", stops.Select(w => ShareCode.Format(w.X, w.Y, w.Name)));

    /// <summary>Every stop in a text, in order. It may hold more than <see cref="Route.Capacity"/>: the caller decides and says so.</summary>
    public static IReadOnlyList<Stop> Parse(string? text)
    {
        var stops = new List<Stop>();
        if (string.IsNullOrWhiteSpace(text)) return stops;
        foreach (var line in text.Split('\n'))
        {
            var at = line.IndexOf(ShareCode.Prefix, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && ShareCode.TryParse(line[at..], out var x, out var y, out var name)) stops.Add(new Stop(x, y, name));
        }
        return stops;
    }

    /// <summary>Two or more stops make a route; one is "Paste waypoint", as it always was.</summary>
    public static bool IsRoute(string? text) => Parse(text).Count >= 2;
}
