namespace PandoraOverlay;

/// <summary>
/// Estimates the growth rate from the poll stream and the time to the NEXT
/// growth stage at that rate (the owner, Nov 2 2026: the stats header reads
/// "64% · ~1h 20m to Adult"; it counted to full growth before). The stage
/// lines are GrowthMilestones' (25%, 50%, 75%, the full-grown line): the
/// target is the first one above the current growth, so crossing 75%
/// re-targets the full-grown line with the same measured rate.
/// Keeps a sliding window of (time, growth) samples taken while in-game and
/// reads the slope off its endpoints, so it adapts within minutes to rate
/// changes (server growth events, diet buffs — Isla Pandora's 1.3× multiplier
/// is simply part of the measured slope). Session-only by design: growth only
/// advances while spawned, so persisted history would add nothing. Resets on
/// death/dino swap (growth decreased or identity changed) and on leaving the
/// game, where a wall-clock gap would flatten the slope. The header's words
/// are GrowthReadout's.
/// </summary>
public sealed class GrowthTracker
{
    public enum GrowthStatus
    {
        /// <summary>Not enough baseline yet — the UI shows nothing extra.</summary>
        Estimating,

        /// <summary>Rate measured; Eta and NextStage are valid.</summary>
        Growing,

        /// <summary>A full baseline with no measurable growth — worth an amber warning.</summary>
        Paused,

        /// <summary>Fully grown, an elder — the estimate retires itself.</summary>
        Full
    }

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinBaseline = TimeSpan.FromMinutes(5);
    private const double MinDelta = 0.0005;     // real growth over MinBaseline sits well above this
    // GrowthMilestones' full-grown line (the server reports a grown dino a hair
    // under 1), the last stage the tracker counts to.
    private const double FullThreshold = GrowthMilestones.FullyGrown;

    private readonly List<(DateTime At, double Growth)> _samples = new();
    private string? _identity;

    public GrowthStatus Status { get; private set; } = GrowthStatus.Estimating;

    /// <summary>In-game time remaining to the next stage line (<see cref="NextStage"/>); null unless Status is Growing.</summary>
    public TimeSpan? Eta { get; private set; }

    /// <summary>The milestone Eta counts to (25, 50, 75 or 100, GrowthMilestones' percents); null unless Status is Growing.</summary>
    public int? NextStage { get; private set; }

    /// <summary>Call when leaving the game (or on credential swap): re-baseline on the next spawn.</summary>
    public void Reset()
    {
        _samples.Clear();
        _identity = null;
        Status = GrowthStatus.Estimating;
        Eta = null;
        NextStage = null;
    }

    public void Add(PlayerState p) => Add(p, DateTime.UtcNow);

    /// <summary>Timestamped overload so tests can simulate 15 minutes in microseconds.</summary>
    internal void Add(PlayerState p, DateTime now)
    {
        Eta = null;       // set again only when Growing
        NextStage = null;
        if (p.Growth >= FullThreshold)
        {
            Status = GrowthStatus.Full;
            return;
        }

        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity ||
            (_samples.Count > 0 && p.Growth < _samples[^1].Growth - 0.001))
        {
            Reset(); // died, rerolled, or swapped dinos
        }
        _identity = identity;

        _samples.Add((now, p.Growth));
        while (now - _samples[0].At > Window)
        {
            _samples.RemoveAt(0);
        }

        var span = _samples[^1].At - _samples[0].At;
        if (span < MinBaseline)
        {
            Status = GrowthStatus.Estimating;
            return;
        }

        var delta = _samples[^1].Growth - _samples[0].Growth;
        if (delta < MinDelta)
        {
            Status = GrowthStatus.Paused;
            return;
        }

        // Under the full-grown line there is always a next line (it is the last one).
        var (line, stage) = GrowthMilestones.NextLine(p.Growth) ?? (FullThreshold, 100);
        var perHour = delta / span.TotalHours;
        Status = GrowthStatus.Growing;
        Eta = TimeSpan.FromHours((line - p.Growth) / perHour);
        NextStage = stage;
    }
}
