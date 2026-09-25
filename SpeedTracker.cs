namespace PandoraOverlay;

/// <summary>
/// Ground speed from the poll stream — path length over a short window of
/// positions (world cm), so it settles in a few polls and follows sprints
/// and stops. Also the closing speed toward a target (positive = getting
/// nearer), which the waypoint ETA needs. A jump no dino could travel, or a
/// gap longer than the window, restarts the window: respawns and relogs
/// must not read as a 400 km/h dash. Pure and tested.
/// </summary>
public sealed class SpeedTracker
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    private const double MaxSpeedCmPerSec = 6000; // 60 m/s — beyond any dino, so not travel

    private readonly List<(DateTime At, double X, double Y)> _samples = new();

    public void Reset() => _samples.Clear();

    public void Add(double x, double y) => Add(x, y, DateTime.UtcNow);

    /// <summary>Timestamped overload for the tests.</summary>
    internal void Add(double x, double y, DateTime now)
    {
        if (_samples.Count > 0)
        {
            var last = _samples[^1];
            var seconds = (now - last.At).TotalSeconds;
            if (seconds <= 0) return;
            if (seconds > Window.TotalSeconds || Distance(last.X, last.Y, x, y) / seconds > MaxSpeedCmPerSec)
            {
                _samples.Clear();
            }
        }
        _samples.Add((now, x, y));
        while (now - _samples[0].At > Window)
        {
            _samples.RemoveAt(0);
        }
    }

    /// <summary>Metres per second over the window; null until two samples exist.</summary>
    public double? SpeedMps
    {
        get
        {
            if (Span is not { } seconds) return null;
            double path = 0;
            for (var i = 1; i < _samples.Count; i++)
            {
                path += Distance(_samples[i - 1].X, _samples[i - 1].Y, _samples[i].X, _samples[i].Y);
            }
            return path / 100 / seconds;
        }
    }

    /// <summary>Metres per second of approach toward a world point (negative = moving away); null until known.</summary>
    public double? ClosingMps(double targetX, double targetY)
    {
        if (Span is not { } seconds) return null;
        var first = _samples[0];
        var last = _samples[^1];
        return (Distance(first.X, first.Y, targetX, targetY) - Distance(last.X, last.Y, targetX, targetY)) / 100 / seconds;
    }

    private double? Span
    {
        get
        {
            if (_samples.Count < 2) return null;
            var seconds = (_samples[^1].At - _samples[0].At).TotalSeconds;
            return seconds > 0 ? seconds : null;
        }
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
}
