namespace PandoraOverlay;

/// <summary>
/// Decides whether the stats panel needs the player's eyes right now — the
/// wake/calm rule behind the attention fade. Pure and tested; the window only
/// maps the answer onto its opacity. Wakes on a low stat, a fracture, damage
/// taken between two polls (held for a few seconds, since one poll's drop is
/// momentary) and a hunger/thirst estimate under a quarter hour. Calms only
/// once everything sits clear of the thresholds, with a gap between the two
/// so a stat hovering at the line cannot make the panel blink. Stamina is
/// deliberately ignored, as with the pulses: it drains by design.
/// </summary>
public sealed class StatsAttention
{
    private const double WakeBelow = 0.50;
    private const double CalmAbove = 0.55;
    private const double DamageStep = 0.005; // a real hit, not the 3-decimal quantization wobble
    private static readonly TimeSpan DamageHold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WakeLeftBelow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CalmLeftAbove = TimeSpan.FromMinutes(20);

    private string? _identity;
    private double _lastGrowth;
    private double? _lastHealth;
    private DateTime _damageUntil;

    /// <summary>True while the panel should show at full opacity. Starts lit: nothing is known yet.</summary>
    public bool Lit { get; private set; } = true;

    /// <summary>Call when leaving the game: the next spawn starts with no damage baseline.</summary>
    public void Reset()
    {
        _identity = null;
        _lastHealth = null;
        _damageUntil = default;
    }

    public bool Update(PlayerState p, TimeSpan? hungerLeft, TimeSpan? thirstLeft) =>
        Update(p, hungerLeft, thirstLeft, DateTime.UtcNow);

    /// <summary>Timestamped overload for the tests.</summary>
    internal bool Update(PlayerState p, TimeSpan? hungerLeft, TimeSpan? thirstLeft, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001)
        {
            Reset(); // died, rerolled, or swapped dinos: a lower health is not damage
        }
        _identity = identity;
        _lastGrowth = p.Growth;

        if (_lastHealth is { } last && p.Health < last - DamageStep) _damageUntil = now + DamageHold;
        _lastHealth = p.Health;

        var lowest = Math.Min(p.Health, Math.Min(p.Hunger, p.Thirst));
        var fractured = p.HeadFractured || p.BodyFractured || p.LegsFractured;
        var damaged = now < _damageUntil;
        TimeSpan? soonest = (hungerLeft, thirstLeft) switch
        {
            ({ } h, { } t) => h < t ? h : t,
            ({ } h, null) => h,
            (null, { } t) => t,
            _ => null
        };

        var wake = lowest < WakeBelow || fractured || damaged || soonest < WakeLeftBelow;
        var calm = lowest > CalmAbove && !fractured && !damaged && (soonest is null || soonest >= CalmLeftAbove);
        if (wake) Lit = true;
        else if (calm) Lit = false;
        return Lit;
    }
}
