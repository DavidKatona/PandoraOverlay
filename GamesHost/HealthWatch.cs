namespace PandoraOverlay;

/// <summary>What one poll's reading asks of the Games window.</summary>
internal enum HealthNotice
{
    /// <summary>Nothing to say.</summary>
    None,

    /// <summary>Show the health warning (if the window is open): the view pauses until "Keep playing".</summary>
    Warn,
}

/// <summary>
/// When the Games window warns that the player's real health is dropping
/// (G18, A15, A59): the window's, not each game's, so every game pauses alike,
/// and a game never learns the health at all (A5). Pure, with the time passed
/// in, fed every poll's player by MainWindow whether or not the window is
/// open, so a window opened mid-trouble warns at once (<see cref="TroubledAt"/>).
/// <para>
/// The rule. A DROP is a poll of the same life that is one of two kinds. A
/// HIT: health at least <see cref="Step"/> lower than the last poll's. A
/// DRAIN (A59): health lower than the last poll's by any amount AND at least
/// <see cref="DrainFall"/> under the highest reading this life gave within the
/// last <see cref="DrainWindow"/>. The drain exists because a hit alone never
/// saw a starving, thirsty or bleeding dino: the stats panel's own
/// HealthTracker reads that fall in game as "two or three units of the API's
/// third decimal" a poll, under the step at every poll however far it goes.
/// A poll that rose or stayed level is never a drain, so healing after a hit,
/// still under the old peak, keeps nothing going; and a counted drain leaves
/// the window as it is, so a steady drain stays a drop at every poll that
/// falls (one that falls faster than a point a minute falls at least once
/// every 6 s even in three decimals, well inside <see cref="Quiet"/>) and its
/// trouble ends 30 s after its last fall, never flapping on and off. A fall
/// slower than one point a minute (over 100 minutes a bar) is not caught: the
/// 120 s window holds under 2 points of it, give or take the API's rounding of
/// its last digit. A drop of either kind starts TROUBLE, which ends once
/// <see cref="Quiet"/> passes with no drop. The first drop of a trouble warns;
/// so does every drop while the warning is not acknowledged (it may have been
/// raised with the window closed). "Keep playing", or closing the window,
/// acknowledges it for the rest of that trouble (<see cref="Acknowledge"/>),
/// with one exception: health crossing DOWN into a lower quarter than it was
/// in when acknowledged warns again (the bar's quarters,
/// <see cref="BarStates.Of"/>: 75, 50, 25), and the acknowledgement is then
/// withdrawn. Rising health never warns. At 3-second polls a warning can be up
/// to a poll late; it says nothing the API doesn't (never who or what).
/// </para>
/// <para>
/// A LIFE is the identity the overlay uses everywhere (DamageTracker,
/// GrowthMilestones): Steam ID, dino and gender, and growth not falling by
/// more than 0.001. A new life is a fresh baseline: nothing is a drop from the
/// old one's health, its drain window starts again from that one reading, and
/// its trouble and acknowledgement are gone. Not in game forgets everything.
/// A reading whose time lies ahead of the one being judged (the wall clock set
/// back: MainWindow passes UtcNow) empties the drain window, since "the last
/// 120 s" no longer means anything; the window starts again from that
/// reading.
/// </para>
/// <para>
/// Its thresholds are its own on purpose (DamageTracker's comment says why
/// each tracker keeps its own): <see cref="Step"/> is set for "a real drop,
/// worth pausing a game for", <see cref="DrainFall"/> and
/// <see cref="DrainWindow"/> for "a loss that keeps going", and the quarters
/// are BarStates', never copied numbers. HealthTracker's SpikeStep is also
/// 0.02, but it means a single poll's hit there; nothing here reads it. Zero
/// WPF.
/// </para>
/// </summary>
internal sealed class HealthWatch
{
    /// <summary>A real drop, not the API's last-digit wobble (it gives health to three decimals).</summary>
    internal const double Step = 0.005;

    /// <summary>No drop this long: the trouble is over.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A drain: this far under the highest health of the last
    /// <see cref="DrainWindow"/> (A59). Two points is twenty units of the API's
    /// last digit, so its wobble never makes one; a starving dino's fall (4 to 6
    /// points a minute at 3 s polls) reaches it 20 to 30 s in.
    /// </summary>
    internal const double DrainFall = 0.02;

    /// <summary>
    /// How far back a drain looks for the highest health (A59). Long enough that
    /// a fall of one point a minute still gathers <see cref="DrainFall"/> in it,
    /// whatever the poll interval (constraint 3 allows 2 s and up); short enough
    /// that the health before a hit stops being the peak two minutes later. A
    /// reading exactly this old still counts.
    /// </summary>
    internal static readonly TimeSpan DrainWindow = TimeSpan.FromSeconds(120);

    /// <summary>A growth fall bigger than this is a new life (DamageTracker's reading of it).</summary>
    internal const double NewLifeGrowthFall = 0.001;

    /// <summary>
    /// Slack under <see cref="Step"/> for the subtraction itself: the API's
    /// three decimals are not exact in binary (0.62 - 0.615 can come out a
    /// hair under 0.005), and a drop of exactly the step must count.
    /// </summary>
    private const double Slack = 1e-9;

    private const int MaxRecent = 128; // a bound only: 120 s at the 2 s poll floor (constraint 3) is 61 readings

    private string? _identity;
    private double _lastGrowth;
    private double _lastHealth;
    private DateTime? _lastDrop;      // the last drop of the trouble in progress; null: no trouble
    private bool _acknowledged;
    private BarState _acknowledgedIn; // the quarter health was in at Acknowledge

    // This life's readings within DrainWindow, oldest first (A59): where a
    // drain finds its peak. In time order always: a reading earlier than the
    // last one empties it first (a clock set back), so only the front ever
    // ages out.
    private readonly Queue<(DateTime At, double Health)> _recent = new(MaxRecent);
    private DateTime _lastAt; // the previous reading's time, to see a clock set back

    /// <summary>The last reading's health as a whole percent, floored (0 when not in game).</summary>
    public int HealthPercent { get; private set; }

    /// <summary>One poll's player (null: not in game) at <paramref name="now"/>.</summary>
    public HealthNotice Update(PlayerState? player, DateTime now)
    {
        if (player is null)
        {
            Forget();
            return HealthNotice.None;
        }

        var health = player.Health;
        var identity = $"{player.SteamId}|{player.Dino}|{player.Gender}";
        if (identity != _identity || player.Growth < _lastGrowth - NewLifeGrowthFall)
        {
            // A new life (or the first reading): this reading is the baseline,
            // and the drain window starts again from it alone (A59): an old
            // life's health is never this one's peak.
            _identity = identity;
            _lastDrop = null;
            _acknowledged = false;
            _recent.Clear();
            _recent.Enqueue((now, health));
            Remember(player.Growth, health);
            _lastAt = now;
            return HealthNotice.None;
        }

        EndQuietTrouble(now);

        // The drain window, before it is read (A59). A clock set back puts the
        // kept readings ahead of this one, where "the last 120 s" means nothing:
        // they go. Then the front ages out; a reading exactly DrainWindow old
        // still counts, so the comparison is strictly "older than".
        if (now < _lastAt) _recent.Clear();
        while (_recent.TryPeek(out var oldest) && now - oldest.At > DrainWindow) _recent.Dequeue();

        var notice = HealthNotice.None;
        var hit = _lastHealth - health >= Step - Slack;
        if (hit || IsDrain(health))
        {
            if (_lastDrop is null || !_acknowledged)
            {
                notice = HealthNotice.Warn; // a new trouble, or one nobody acknowledged yet
            }
            else if (Depth(BarStates.Of(health)) > Depth(_acknowledgedIn))
            {
                _acknowledged = false;      // down into a lower quarter: warn again
                notice = HealthNotice.Warn;
            }
            _lastDrop = now;
        }

        // This reading joins the window only now, so it is never its own peak.
        // A counted drain leaves the window as it is (A59): the next poll that
        // falls is still a drain, and a steady drain is one trouble while it lasts.
        _recent.Enqueue((now, health));
        while (_recent.Count > MaxRecent) _recent.Dequeue();
        Remember(player.Growth, health);
        _lastAt = now;
        return notice;
    }

    /// <summary>
    /// A drain (A59): this poll fell (by any amount) and lies at least
    /// <see cref="DrainFall"/> under the highest reading the window kept. A poll
    /// that rose or stayed level is never one, however far under the peak.
    /// Walked with the queue's struct enumerator: nothing allocated per poll.
    /// A NaN (never seen from the API) is never a peak and never a drain, as it
    /// is never a hit.
    /// </summary>
    private bool IsDrain(double health)
    {
        if (!(health < _lastHealth - Slack)) return false;
        var peak = double.NegativeInfinity; // an empty window has no peak: no drain
        foreach (var reading in _recent)
        {
            if (reading.Health > peak) peak = reading.Health;
        }
        return peak - health >= DrainFall - Slack;
    }

    /// <summary>
    /// "Keep playing", or the window closing: the warning is acknowledged for
    /// the rest of this trouble, at the quarter health is in now. Outside a
    /// trouble there is nothing to acknowledge.
    /// </summary>
    public void Acknowledge()
    {
        if (_lastDrop is null) return;
        _acknowledged = true;
        _acknowledgedIn = BarStates.Of(_lastHealth);
    }

    /// <summary>A trouble is in progress at <paramref name="now"/> and nobody acknowledged it: a window opening now warns at once.</summary>
    public bool TroubledAt(DateTime now) =>
        _lastDrop is { } last && now - last < Quiet && !_acknowledged;

    private void EndQuietTrouble(DateTime now)
    {
        if (_lastDrop is { } last && now - last >= Quiet)
        {
            _lastDrop = null;
            _acknowledged = false; // the acknowledgement goes with its trouble
        }
    }

    private void Remember(double growth, double health)
    {
        _lastGrowth = growth;
        _lastHealth = health;
        HealthPercent = Percent(health);
    }

    private void Forget()
    {
        _identity = null;
        _lastGrowth = 0;
        _lastHealth = 0;
        _lastDrop = null;
        _acknowledged = false;
        _recent.Clear();
        _lastAt = default;
        HealthPercent = 0;
    }

    /// <summary>Floored, as the growth header floors its percent (GrowthReadout), with its slack for binary fractions (0.29 * 100 is a hair under 29). A NaN (never seen from the API) reads 0.</summary>
    internal static int Percent(double health) =>
        double.IsNaN(health) ? 0 : (int)Math.Floor(Math.Clamp(health, 0, 1) * 100 + 1e-9);

    /// <summary>How far down a quarter is: deeper is lower health. By name, so nothing here leans on the order BarState's members are declared in.</summary>
    private static int Depth(BarState state) => state switch
    {
        BarState.Above75 => 0,
        BarState.From50To75 => 1,
        BarState.From25To50 => 2,
        _ => 3,
    };
}
