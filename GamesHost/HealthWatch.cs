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
/// (G18, A15): the window's, not each game's, so every game pauses alike, and
/// a game never learns the health at all (A5). Pure, with the time passed in,
/// fed every poll's player by MainWindow whether or not the window is open, so
/// a window opened mid-trouble warns at once (<see cref="TroubledAt"/>).
/// <para>
/// The rule. A DROP is health at least <see cref="Step"/> lower than the last
/// poll's, in the same life. A drop starts TROUBLE, which ends once
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
/// old one's health, and its trouble and acknowledgement are gone. Not in game
/// forgets everything.
/// </para>
/// <para>
/// Its thresholds are its own on purpose (DamageTracker's comment says why
/// each tracker keeps its own): <see cref="Step"/> is set for "a real drop,
/// worth pausing a game for", and the quarters are BarStates', never copied
/// numbers. Zero WPF.
/// </para>
/// </summary>
internal sealed class HealthWatch
{
    /// <summary>A real drop, not the API's last-digit wobble (it gives health to three decimals).</summary>
    internal const double Step = 0.005;

    /// <summary>No drop this long: the trouble is over.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    /// <summary>A growth fall bigger than this is a new life (DamageTracker's reading of it).</summary>
    internal const double NewLifeGrowthFall = 0.001;

    /// <summary>
    /// Slack under <see cref="Step"/> for the subtraction itself: the API's
    /// three decimals are not exact in binary (0.62 - 0.615 can come out a
    /// hair under 0.005), and a drop of exactly the step must count.
    /// </summary>
    private const double Slack = 1e-9;

    private string? _identity;
    private double _lastGrowth;
    private double _lastHealth;
    private DateTime? _lastDrop;      // the last drop of the trouble in progress; null: no trouble
    private bool _acknowledged;
    private BarState _acknowledgedIn; // the quarter health was in at Acknowledge

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
            // A new life (or the first reading): this reading is the baseline.
            _identity = identity;
            _lastDrop = null;
            _acknowledged = false;
            Remember(player.Growth, health);
            return HealthNotice.None;
        }

        EndQuietTrouble(now);
        var notice = HealthNotice.None;
        if (_lastHealth - health >= Step - Slack)
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
        Remember(player.Growth, health);
        return notice;
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
