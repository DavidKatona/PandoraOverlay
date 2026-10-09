namespace PandoraOverlay;

/// <summary>
/// Reads the stamina trend off the poll stream and turns it into a time —
/// pure and tested. While stamina falls poll after poll (a sprint, a swim):
/// how long until it is empty at that pace. While it climbs (resting): how
/// long until it is full. Stamina is the one stat with BOTH readings: it
/// refills by itself at a steady pace, where hunger and thirst refill in
/// bites and gulps the player controls and have no recovery rate to
/// extrapolate (so DrainTracker only ever says "time left").
///
/// A reading needs two consistent steps (three polls, about six seconds
/// into a sprint at the default cadence) and follows the last few polls,
/// so a changed pace shows within seconds. One flat poll, a reversal, a
/// long gap between polls or a new dino ends it — better no number than a
/// stale one; the number is up to one poll old as it is, which the overlay
/// cannot improve (the poll interval has a floor). Information only: no
/// pulse, no fade wake, no chime — stamina drains by design.
/// Built on a player's request.
/// </summary>
public sealed class StaminaTracker
{
    public enum Trend { Steady, Draining, Recovering }

    private const double Step = 0.004;   // a per-poll change under this is "steady" (the API carries 3 decimals)
    private const int MinSteps = 2;      // two consistent steps before any estimate
    private const int MaxSamples = 6;    // the pace is measured over the last ~15 s
    private const double Floor = 0.005;  // at the ends there is nothing to estimate
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(8);      // idle polling or a stall: the run is over
    private static readonly TimeSpan MaxEstimate = TimeSpan.FromMinutes(5); // a crawl this slow is not worth a number

    private readonly List<(DateTime At, double Value)> _run = new();
    private string? _identity;

    /// <summary>Which way stamina has been moving over the current run.</summary>
    public Trend Direction { get; private set; }

    /// <summary>Time until empty (Draining) or until full (Recovering); null while there is no consistent pace.</summary>
    public TimeSpan? Estimate { get; private set; }

    /// <summary>Bar label: "~25s" while draining, "full ~40s" while recovering, null otherwise.</summary>
    public string? Label { get; private set; }

    public void Reset()
    {
        _run.Clear();
        _identity = null;
        Direction = Trend.Steady;
        Estimate = null;
        Label = null;
    }

    public void Add(PlayerState p) => Add(p, DateTime.UtcNow);

    /// <summary>Timestamped overload for the tests.</summary>
    internal void Add(PlayerState p, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity) Reset();
        _identity = identity;

        var value = Math.Clamp(p.Stamina, 0, 1);
        if (_run.Count > 0)
        {
            var last = _run[^1];
            var gap = now - last.At;
            if (gap <= TimeSpan.Zero) return;

            var delta = value - last.Value;
            var step = Math.Abs(delta) < Step ? Trend.Steady : delta < 0 ? Trend.Draining : Trend.Recovering;
            if (gap > MaxGap || step == Trend.Steady)
            {
                _run.Clear(); // nothing to extrapolate from: start over at this sample
                Direction = Trend.Steady;
            }
            else if (step != Direction)
            {
                _run.RemoveRange(0, _run.Count - 1); // a new direction: the run starts at the previous sample
                Direction = step;
            }
        }

        _run.Add((now, value));
        if (_run.Count > MaxSamples) _run.RemoveAt(0);
        Evaluate(value);
    }

    private void Evaluate(double value)
    {
        Estimate = null;
        Label = null;
        if (Direction == Trend.Steady || _run.Count - 1 < MinSteps) return;

        var seconds = (_run[^1].At - _run[0].At).TotalSeconds;
        var perSecond = Math.Abs(_run[^1].Value - _run[0].Value) / seconds;
        if (perSecond <= 0) return;

        var remaining = Direction == Trend.Draining ? value : 1 - value;
        if (remaining <= Floor) return;

        var estimate = TimeSpan.FromSeconds(remaining / perSecond);
        if (estimate > MaxEstimate) return;

        Estimate = estimate;
        Label = Direction == Trend.Draining ? Format(estimate) : "full " + Format(estimate);
    }

    /// <summary>"~25s" under 100 seconds, "~2m" above.</summary>
    private static string Format(TimeSpan t) =>
        t.TotalSeconds < 100
            ? $"~{Math.Max(1, Math.Round(t.TotalSeconds)):0}s"
            : $"~{Math.Round(t.TotalMinutes, MidpointRounding.AwayFromZero):0}m";
}
