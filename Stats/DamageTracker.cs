namespace PandoraOverlay;

/// <summary>
/// Damage taken in the current fight, for the stats panel's combat view —
/// pure and tested. Every health drop between two polls adds to the total;
/// health regenerated between hits is not subtracted (the question is "how
/// much have I lost in this fight", not "where is my health", which the
/// health bar already answers), and a bleed counts, arriving as steady
/// small drops. The fight is over — and the total back to zero — once
/// FightGap has passed without a hit. A new life starts clean.
///
/// It is a readout, not a combat log: one reading per poll, so a hit that
/// lands and regenerates inside a single poll interval is undercounted.
/// </summary>
public sealed class DamageTracker
{
    private const double Step = 0.002; // below this a drop is the API's 3-decimal wobble, not a hit
    private static readonly TimeSpan FightGap = TimeSpan.FromSeconds(30);

    private string? _identity;
    private double _lastGrowth;
    private double? _lastHealth;
    private DateTime _lastHitUtc;

    /// <summary>Health lost in this fight as a fraction of the pool (can pass 1 in a long fight with regeneration); 0 when not fighting.</summary>
    public double Total { get; private set; }

    /// <summary>True while a fight is counted: a hit landed within the last FightGap.</summary>
    public bool InFight => Total > 0;

    public void Reset()
    {
        _identity = null;
        _lastHealth = null;
        Total = 0;
    }

    public void Add(PlayerState p) => Add(p, DateTime.UtcNow);

    /// <summary>Timestamped overload for the tests.</summary>
    internal void Add(PlayerState p, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001)
        {
            Reset(); // died, rerolled or swapped dinos: that fight belonged to the old life
        }
        _identity = identity;
        _lastGrowth = p.Growth;

        if (Total > 0 && now - _lastHitUtc >= FightGap) Total = 0; // the fight is over

        if (_lastHealth is { } was && was - p.Health > Step)
        {
            Total += was - p.Health;
            _lastHitUtc = now;
        }
        _lastHealth = p.Health;
    }
}
