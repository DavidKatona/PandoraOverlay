namespace PandoraOverlay;

/// <summary>
/// Decides whether the stats panel needs the player's eyes right now — the
/// wake/calm rule behind the attention fade. Pure and tested; the window only
/// maps the answer onto its opacity. One ruleset per view of the panel, on the
/// principle that lit means "something ON this panel is worth a look":
/// <para>
/// Survival wakes on a low health/hunger/thirst, a fracture, damage taken
/// between two polls (held for a few seconds, since one poll's drop is
/// momentary), a hunger/thirst estimate under a quarter hour and a noted
/// event. Stamina is deliberately ignored there, as with the pulses: it
/// drains by design.
/// </para>
/// <para>
/// Combat wakes on health or stamina under three quarters (stamina is that
/// view's subject, and a chase can run without a single hit), a fracture and
/// a fight in progress — the caller's DamageTracker, so the panel is lit
/// exactly as long as the Damage row shows something. Hunger, thirst, their
/// estimates and growth events are not on that view and do not wake it.
/// </para>
/// Each view calms only once everything sits clear of its thresholds, with a
/// gap between wake and calm so a stat hovering at the line cannot make the
/// panel blink. Both share the identity and the damage baseline, so flipping
/// views mid-fight loses nothing. The flip itself lights the panel for a few
/// seconds (NoteViewFlip); then the new view's rules decide afresh.
/// The shared state is why this is one class with two rules, not a class per view.
/// </summary>
public sealed class StatsAttention
{
    private const double WakeBelow = 0.50;
    private const double CalmAbove = 0.55;
    private const double CombatWakeBelow = 0.75; // owner's call: in a fight, sooner
    private const double CombatCalmAbove = 0.80;
    // Both combat lines are the owner's numbers (Sep 30 2026). Accepted with them:
    // a wounded dino keeps the combat view lit until it has healed past CombatCalmAbove.
    private const double DamageStep = 0.005; // a real hit, not the 3-decimal quantization wobble
    private static readonly TimeSpan DamageHold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WakeLeftBelow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CalmLeftAbove = TimeSpan.FromMinutes(20);

    private static readonly TimeSpan EventHold = TimeSpan.FromSeconds(10);

    private string? _identity;
    private double _lastGrowth;
    private double? _lastHealth;
    private DateTime _damageUntil;
    private DateTime _eventUntil;
    private bool? _combatView; // the view the last verdict was for
    private bool _afresh;      // the view changed: the next verdict must not inherit the old one
    private DateTime _flipUntil;

    /// <summary>True while the panel should show at full opacity. Starts lit: nothing is known yet.</summary>
    public bool Lit { get; private set; } = true;

    /// <summary>
    /// The view was flipped: a change to the panel itself is always shown at
    /// full opacity for a few seconds, whatever the new view's rules say
    /// (owner's call). After the hold the new view decides afresh.
    /// </summary>
    public void NoteViewFlip() => NoteViewFlip(DateTime.UtcNow);

    internal void NoteViewFlip(DateTime now) => _flipUntil = now + EventHold;

    /// <summary>True while a flip's hold runs — for the states Update never sees (not in game).</summary>
    public bool FlipHeld => FlipHeldAt(DateTime.UtcNow);

    internal bool FlipHeldAt(DateTime now) => now < _flipUntil;

    /// <summary>Call when leaving the game: the next spawn starts with no damage baseline.</summary>
    public void Reset()
    {
        _identity = null;
        _lastHealth = null;
        _damageUntil = default;
    }

    /// <summary>Something worth a glance just happened (a growth milestone): keeps the survival view lit for a few seconds.</summary>
    public void NoteEvent() => NoteEvent(DateTime.UtcNow);

    internal void NoteEvent(DateTime now) => _eventUntil = now + EventHold;

    /// <param name="combatView">Which view the panel shows — it picks the ruleset.</param>
    /// <param name="inFight">The combat view's Damage row has something in it (DamageTracker.InFight).</param>
    public bool Update(PlayerState p, TimeSpan? hungerLeft, TimeSpan? thirstLeft, bool combatView, bool inFight) =>
        Update(p, hungerLeft, thirstLeft, DateTime.UtcNow, combatView, inFight);

    /// <summary>Timestamped overload for the tests.</summary>
    internal bool Update(PlayerState p, TimeSpan? hungerLeft, TimeSpan? thirstLeft, DateTime now,
                         bool combatView = false, bool inFight = false)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001)
        {
            Reset(); // died, rerolled, or swapped dinos: a lower health is not damage
        }
        _identity = identity;
        _lastGrowth = p.Growth;

        // The damage baseline runs in both views, so a flip keeps the hold.
        if (_lastHealth is { } last && p.Health < last - DamageStep) _damageUntil = now + DamageHold;
        _lastHealth = p.Health;

        var fractured = p.HeadFractured || p.BodyFractured || p.LegsFractured;
        var (wake, calm) = combatView
            ? CombatRule(p, fractured, inFight)
            : SurvivalRule(p, fractured, now < _damageUntil || now < _eventUntil, hungerLeft, thirstLeft);

        if (_combatView is { } before && before != combatView) _afresh = true;
        _combatView = combatView;

        if (now < _flipUntil)
        {
            Lit = true; // the flip itself is worth a look
        }
        else if (_afresh)
        {
            Lit = wake; // a value between the new view's lines must not inherit the other view's verdict (or the flip's hold)
            _afresh = false;
        }
        else if (wake) Lit = true;
        else if (calm) Lit = false;
        return Lit;
    }

    private static (bool Wake, bool Calm) SurvivalRule(PlayerState p, bool fractured, bool damaged,
                                                       TimeSpan? hungerLeft, TimeSpan? thirstLeft)
    {
        var lowest = Math.Min(p.Health, Math.Min(p.Hunger, p.Thirst));
        TimeSpan? soonest = (hungerLeft, thirstLeft) switch
        {
            ({ } h, { } t) => h < t ? h : t,
            ({ } h, null) => h,
            (null, { } t) => t,
            _ => null
        };

        return (lowest < WakeBelow || fractured || damaged || soonest < WakeLeftBelow,
                lowest > CalmAbove && !fractured && !damaged && (soonest is null || soonest >= CalmLeftAbove));
    }

    // A fight is DamageTracker's word, not a hold timer of this class's own:
    // a second timer could disagree with the Damage row. Keeping the combat
    // view lit all the time was considered and dropped: combat can be the
    // default view, and the fade would then be silently off for that player.
    private static (bool Wake, bool Calm) CombatRule(PlayerState p, bool fractured, bool inFight)
    {
        var lowest = Math.Min(p.Health, p.Stamina);
        return (lowest < CombatWakeBelow || fractured || inFight,
                lowest > CombatCalmAbove && !fractured && !inFight);
    }
}
