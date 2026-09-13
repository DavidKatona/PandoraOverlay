namespace PandoraOverlay;

/// <summary>
/// Estimates the growth rate and time-to-full-grown from the poll stream.
/// Keeps a sliding window of (time, growth) samples taken while in-game and
/// reads the slope off its endpoints, so it adapts within minutes to rate
/// changes (server growth events, diet buffs — Isla Pandora's 1.3× multiplier
/// is simply part of the measured slope). Session-only by design: growth only
/// advances while spawned, so persisted history would add nothing. Resets on
/// death/dino swap (growth decreased or identity changed) and on leaving the
/// game, where a wall-clock gap would flatten the slope.
/// </summary>
public sealed class GrowthTracker
{
    public enum GrowthStatus
    {
        /// <summary>Not enough baseline yet — the UI shows nothing extra.</summary>
        Estimating,

        /// <summary>Rate measured; Eta is valid.</summary>
        Growing,

        /// <summary>A full baseline with no measurable growth — worth an amber warning.</summary>
        Paused,

        /// <summary>Fully grown — the feature retires itself.</summary>
        Full
    }

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinBaseline = TimeSpan.FromMinutes(5);
    private const double MinDelta = 0.0005;     // real growth over MinBaseline sits well above this
    private const double FullThreshold = 0.9995;

    private readonly List<(DateTime At, double Growth)> _samples = new();
    private string? _identity;

    public GrowthStatus Status { get; private set; } = GrowthStatus.Estimating;

    /// <summary>In-game time remaining to full growth; null unless Status is Growing.</summary>
    public TimeSpan? Eta { get; private set; }

    /// <summary>Call when leaving the game (or on credential swap): re-baseline on the next spawn.</summary>
    public void Reset()
    {
        _samples.Clear();
        _identity = null;
        Status = GrowthStatus.Estimating;
        Eta = null;
    }

    public void Add(PlayerState p)
    {
        if (p.Growth >= FullThreshold)
        {
            Status = GrowthStatus.Full;
            Eta = null;
            return;
        }

        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity ||
            (_samples.Count > 0 && p.Growth < _samples[^1].Growth - 0.001))
        {
            Reset(); // died, rerolled, or swapped dinos
        }
        _identity = identity;

        var now = DateTime.UtcNow;
        _samples.Add((now, p.Growth));
        while (now - _samples[0].At > Window)
        {
            _samples.RemoveAt(0);
        }

        var span = _samples[^1].At - _samples[0].At;
        if (span < MinBaseline)
        {
            Status = GrowthStatus.Estimating;
            Eta = null;
            return;
        }

        var delta = _samples[^1].Growth - _samples[0].Growth;
        if (delta < MinDelta)
        {
            Status = GrowthStatus.Paused;
            Eta = null;
            return;
        }

        var perHour = delta / span.TotalHours;
        Status = GrowthStatus.Growing;
        Eta = TimeSpan.FromHours((1 - p.Growth) / perHour);
    }
}
