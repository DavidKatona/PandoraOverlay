namespace PandoraOverlay;

/// <summary>
/// Reads the health trend off the poll stream and turns it into a time —
/// pure and tested, on StaminaTracker's pattern with health's own numbers.
/// While health climbs (healing after a fight): how long until it is full,
/// "full ~14m". While it FALLS STEADILY, poll after poll by similar small
/// amounts: how long until it is empty at that pace, "~3m". Anything else —
/// a fight's sudden hits, a flat bar, a gap — shows nothing. The owner's
/// decisions, Nov 2 2026 (pandora-four-features PLAN.md, 4.1–4.7).
/// <para>
/// The two directions are not alike on purpose. A climb reads like
/// stamina's recovery: two consistent steps. A fall can be a fight, where a
/// hit is one big step, and a time extrapolated from a hit would be a
/// scare, not information. So the falling reading asks more: at least four
/// consecutive drops that are SIMILAR (each within <see cref="Similar"/> of
/// the run's median drop; a big drop among small ones is a hit and ends the
/// run), and no SPIKE (one drop of <see cref="SpikeStep"/> or more) in the
/// last 30 s. DamageTracker's InFight can't be that gate: a steady fall's
/// every drop counts as a hit there, so it stays true for as long as the
/// fall lasts and the label would never show. What makes health fall the
/// API does not say, so no player-facing text may: the label is a time,
/// the CHANGELOG says "falls steadily", never why.
/// </para>
/// <para>
/// One flat poll, a reversal, a long gap between polls or a new life ends a
/// run — better no number than a stale one. Capped both ways at an hour
/// with hunger's hysteresis (DrainTracker's pair), in stamina's format.
/// Information only: no pulse, no fade wake, no chime, no feed line — the
/// health bar has its 25% pulse and the Wounded word already.
/// </para>
/// </summary>
public sealed class HealthTracker
{
    public enum Trend { Steady, Falling, Rising }

    // A per-poll change under this is "steady" and ends the run: one unit of
    // the API's 3 decimals is its wobble (DamageTracker's line). Half of
    // stamina's 0.004 because health moves slower: a regeneration that fills
    // the bar in 25 minutes climbs 0.002 a poll at the default 3 s cadence,
    // and stamina's line would call most heals flat. The flip side: a heal
    // slower than that reads as flat polls and shows nothing.
    private const double Step = 0.002;
    private const int MinRisingSteps = 2;   // a climb reads like stamina's recovery: three polls, about 6 s
    private const int MinFallingSteps = 4;  // the owner's "four polls of similar small drops", about 12 s at the default cadence

    // The pace over the last ~27 s at the default cadence, where stamina's
    // is ~15 s: health's steps are only two or three units of the API's
    // third decimal, so over five steps the pace jumps by a tenth from one
    // poll to the next and the minutes flick with it; nine steps halve that
    // and still follow a changed pace within half a minute.
    private const int MaxSamples = 10;

    // Each falling drop must be within this factor of the run's median drop
    // (either way). A steady fall's drops differ by a unit or two of the 3
    // decimals (0.002 next to 0.003 is 1.5×); a hit among them is many times
    // their size.
    private const double Similar = 2.5;

    // A single-poll drop at or over this is a hit (a "spike"): it ends the
    // run and holds the falling reading back for SpikeQuiet. 2% of the bar
    // is ten times the wobble line, so the 3 decimals never make one, and a
    // steady fall that took that much every poll would empty a FULL bar in
    // two and a half minutes at the default cadence: the falls the label is
    // for stay well under it (the owner's "~3m" on a half-full bar is under
    // 1% a poll). A fall steeper than that reads as hits and shows no
    // number; the bar is moving fast enough to say it. Per POLL, as the
    // owner put it ("a single-poll drop"), so a slower poll interval (the
    // player's setting) makes a steady fall's drops bigger in proportion.
    private const double SpikeStep = 0.02;

    private const double Floor = 0.005; // at the ends there is nothing to estimate (0.995 and up is full)
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(8);      // idle polling or a stall: the run is over (stamina's)
    private static readonly TimeSpan SpikeQuiet = TimeSpan.FromSeconds(30); // DamageTracker's FightGap: no falling reading this soon after a hit

    // Far-out estimates are clutter (the owner, 4.4): the label appears
    // under ShowBelow and leaves only above HideAbove, DrainTracker's pair,
    // so it doesn't blink while the estimate hovers at the edge. At the
    // default cadence the slowest pace the label reads (Step a poll) is a
    // full bar in 25 minutes, so the cap bites only at a slower poll interval.
    private static readonly TimeSpan ShowBelow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan HideAbove = TimeSpan.FromMinutes(65);

    private readonly List<(DateTime At, double Value)> _run = new();
    private string? _identity;
    private double _lastGrowth;
    private DateTime _lastSpikeUtc = DateTime.MinValue;
    private bool _shown;

    /// <summary>Which way health has been moving over the current run.</summary>
    public Trend Direction { get; private set; }

    /// <summary>Time until empty (Falling) or until full (Rising) while a label stands; null otherwise.</summary>
    public TimeSpan? Estimate { get; private set; }

    /// <summary>Bar label: "~3m" while falling steadily, "full ~14m" while climbing, null otherwise.</summary>
    public string? Label { get; private set; }

    /// <summary>Call when leaving the game (or on death/dino swap): the run belonged to that life.</summary>
    public void Reset()
    {
        _run.Clear();
        _identity = null;
        _lastSpikeUtc = DateTime.MinValue;
        _shown = false;
        Direction = Trend.Steady;
        Estimate = null;
        Label = null;
    }

    public void Add(PlayerState p) => Add(p, DateTime.UtcNow);

    /// <summary>Timestamped overload for the tests.</summary>
    internal void Add(PlayerState p, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001)
        {
            Reset(); // died, rerolled or swapped dinos
        }
        _identity = identity;
        _lastGrowth = p.Growth;

        var value = Math.Clamp(p.Health, 0, 1);
        if (_run.Count > 0)
        {
            var last = _run[^1];
            var gap = now - last.At;
            if (gap <= TimeSpan.Zero) return;

            // Rounded to the API's 3 decimals, so a 0.002 step is 0.002 in
            // double arithmetic and lands on the right side of the lines.
            var delta = Math.Round(value - last.Value, 3);
            var step = Math.Abs(delta) < Step ? Trend.Steady : delta < 0 ? Trend.Falling : Trend.Rising;
            var spike = -delta >= SpikeStep;
            if (spike) _lastSpikeUtc = now;
            var hit = spike || (step == Trend.Falling && Direction == Trend.Falling && !IsSimilar(-delta));

            if (gap > MaxGap || step == Trend.Steady || hit)
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
        Evaluate(value, now);
    }

    /// <summary>Whether a new drop is within <see cref="Similar"/> of the falling run's median drop.</summary>
    private bool IsSimilar(double drop)
    {
        var drops = new List<double>(_run.Count);
        for (var i = 1; i < _run.Count; i++) drops.Add(Math.Round(_run[i - 1].Value - _run[i].Value, 3));
        if (drops.Count == 0) return true;

        drops.Sort();
        var mid = drops.Count / 2;
        var median = drops.Count % 2 == 1 ? drops[mid] : (drops[mid - 1] + drops[mid]) / 2;
        return drop <= median * Similar && drop >= median / Similar;
    }

    private void Evaluate(double value, DateTime now)
    {
        Estimate = null;
        Label = null;

        var steps = _run.Count - 1;
        var ready = Direction switch
        {
            Trend.Rising => steps >= MinRisingSteps,
            Trend.Falling => steps >= MinFallingSteps && now - _lastSpikeUtc >= SpikeQuiet,
            _ => false,
        };
        var remaining = Direction == Trend.Falling ? value : 1 - value;

        TimeSpan? estimate = null;
        if (ready && remaining > Floor)
        {
            var seconds = (_run[^1].At - _run[0].At).TotalSeconds;
            var perSecond = Math.Abs(_run[^1].Value - _run[0].Value) / seconds;
            if (perSecond > 0) estimate = TimeSpan.FromSeconds(remaining / perSecond);
        }

        _shown = estimate is { } e && e <= (_shown ? HideAbove : ShowBelow);
        if (!_shown) return;

        Estimate = estimate;
        Label = Direction == Trend.Falling ? Format(estimate!.Value) : "full " + Format(estimate!.Value);
    }

    /// <summary>Stamina's: "~45s" under 100 seconds, "~14m" above.</summary>
    private static string Format(TimeSpan t) =>
        t.TotalSeconds < 100
            ? $"~{Math.Max(1, Math.Round(t.TotalSeconds)):0}s"
            : $"~{Math.Round(t.TotalMinutes, MidpointRounding.AwayFromZero):0}m";
}
