namespace PandoraOverlay;

/// <summary>
/// The minimap's breadcrumb trail: the player's recent path in world
/// coordinates (cm), fed from the poll stream — zero extra requests. Points
/// expire by age, standing still adds nothing, and the trail belongs to one
/// life: a death/dino swap clears it, and so does a jump no dino could have
/// travelled (respawn, teleport), which would otherwise draw a straight line
/// across the map. Deliberately NOT cleared by a not-in-game spell — after a
/// relog or server restart you stand where you stood, and "where did I come
/// from" is still a fair question. Session-only.
/// </summary>
public sealed class BreadcrumbTrail
{
    private const double MinStepCm = 500;          // 5 m — idling on the spot adds nothing
    private const double MaxSpeedCmPerSec = 6000;  // 60 m/s — beyond any dino, so not travel
    private const double MaxGapJumpCm = 50_000;    // 500 m — across a poll gap the path is unknown anyway

    private readonly List<(DateTime At, double X, double Y)> _points = new();
    private (DateTime At, double X, double Y)? _lastSeen;
    private string? _identity;
    private double _lastGrowth;

    /// <summary>Stored positions, oldest first.</summary>
    public IReadOnlyList<(DateTime At, double X, double Y)> Points => _points;

    public void Reset()
    {
        _points.Clear();
        _lastSeen = null;
        _identity = null;
    }

    public void Add(PlayerState p, TimeSpan keep) => Add(p, keep, DateTime.UtcNow);

    /// <summary>Timestamped overload so tests can simulate an hour in microseconds.</summary>
    internal void Add(PlayerState p, TimeSpan keep, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001)
        {
            Reset(); // died, rerolled, or swapped dinos
        }
        _identity = identity;
        _lastGrowth = p.Growth;

        if (_lastSeen is { } seen)
        {
            var seconds = (now - seen.At).TotalSeconds;
            var allowed = Math.Min(MaxSpeedCmPerSec * Math.Max(seconds, 0), MaxGapJumpCm);
            if (Distance(seen.X, seen.Y, p.X, p.Y) > allowed) _points.Clear(); // respawned / teleported
        }
        _lastSeen = (now, p.X, p.Y);

        if (_points.Count == 0 || Distance(_points[^1].X, _points[^1].Y, p.X, p.Y) >= MinStepCm)
        {
            _points.Add((now, p.X, p.Y));
        }
        while (_points.Count > 0 && now - _points[0].At > keep)
        {
            _points.RemoveAt(0);
        }
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
}
