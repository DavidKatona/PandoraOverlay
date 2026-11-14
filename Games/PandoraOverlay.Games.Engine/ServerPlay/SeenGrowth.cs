namespace PandoraOverlay.Games.Engine;

/// <summary>
/// "Only what the overlay saw", as a rule (A6): which growth on the real
/// server a game may count, from the readings the app hands every game
/// (<see cref="DinoReading"/>, one per successful poll).
///
/// <para><b>Why it exists.</b> The prototype paid for any rise in growth
/// between two readings, so swapping a young stored dino for an old one
/// "grew" 40% in one poll, and paid for it. The public promise is that
/// progress counts only what the overlay saw a dino actually grow. So growth
/// is counted only as a step between two consecutive in-game readings of one
/// life, that a real dino could have made: the same species and gender, not
/// lower (but for the website's last-digit wobble), close enough in time, and
/// no faster than any dino grows. Anything else, a different dino, a fall (a
/// new life), a blind stretch, a jump no dino makes (a stored dino swapped
/// in), makes the reading a new baseline and counts nothing. By construction
/// nobody can be credited faster than <see cref="MaxGrowthPerSecond"/>.</para>
///
/// <para><b>Counted once.</b> A step counts only the rise above the life's
/// high-water mark, the highest growth seen in it so far, so the website's
/// last digit going 0.416, 0.415, 0.416 pays nothing however long it goes on.</para>
///
/// <para><b>In memory only, never saved: a promise, not an omission.</b> A new
/// launch (a new object) starts with no baseline, and its first reading never
/// counts, so nothing bridges a restart and no growth is ever counted for time
/// the overlay was closed (the "nothing is estimated" promise). The clock is
/// the host's monotonic <see cref="IGameClock.Elapsed"/>, which starts again
/// at every launch too. A game keeps what a step paid in its save; it never
/// keeps this.</para>
///
/// <para>What a seen step pays, and whether it pays at all, is the game's;
/// this class only says what was seen (A21).</para>
/// </summary>
public sealed class SeenGrowth
{
    /// <summary>
    /// The fastest growth a step may show and still be counted: 100% in ten
    /// minutes. Far faster than any dino grows on the server (the owner knows
    /// the fastest and may lower it), far slower than any swap; at a 3 s poll it
    /// allows 0.5% a step, and the API moves growth in steps of 0.1%.
    /// </summary>
    public const double MaxGrowthPerSecond = 1.0 / 600;

    /// <summary>
    /// The longest time between two readings that still continues a life: a
    /// few failed polls (the poll then slows to 15 s) are bridged, a longer blind
    /// stretch is not, because nobody saw what happened in it.
    /// </summary>
    public static readonly TimeSpan MaxStepGap = TimeSpan.FromSeconds(120);

    /// <summary>
    /// How far a reading may fall below the one before and still continue the
    /// life: the API's last digit wobbles by one step (0.1%). A deeper fall is a
    /// new life.
    /// </summary>
    public const double Slack = 0.001;

    /// <summary>
    /// Floating point's allowance in both comparisons, the rise's and the
    /// slack's: the website's three decimals aren't exact in binary, so
    /// 0.014 − 0.001 comes out a hair above 0.013, and a 0.5% rise in 3 s a hair
    /// above <see cref="MaxGrowthPerSecond"/> × 3. Far below the API's 0.1% step,
    /// so it can't let anything real through.
    /// </summary>
    private const double RoundingAllowance = 1e-9;

    // The life being watched; _species null = no baseline.
    private string? _species;
    private string _gender = "";
    private double _lastGrowth;
    private TimeSpan _lastTime;
    private double _high;

    /// <summary>
    /// Takes the next reading, <paramref name="now"/> being the host's
    /// <see cref="IGameClock.Elapsed"/> when it arrived, and answers the growth
    /// it shows the overlay saw, or null.
    /// <list type="number">
    /// <item>Not in game, or no species: forgets the life (<see cref="Forget"/>),
    /// null.</item>
    /// <item>No baseline: the reading becomes one, null. A first sight never
    /// counts: spawning in at 60% is not reaching Subadult.</item>
    /// <item>The reading continues the life only when it is the same species and
    /// gender (ignoring case; no gender is ""), not more than
    /// <see cref="Slack"/> lower, more than 0 and at most
    /// <see cref="MaxStepGap"/> later, and risen no faster than
    /// <see cref="MaxGrowthPerSecond"/>. Otherwise it is a new baseline, null.</item>
    /// <item>It continues: if its growth is above the life's high-water mark, the
    /// answer is the step from the mark to it, and the mark moves up; if not
    /// (a wobble back up), null.</item>
    /// </list>
    /// A reading whose growth is not a number never continues a life (every
    /// comparison with it fails), so it can only become a baseline that the next
    /// reading replaces: it is never counted.
    /// </summary>
    public SeenStep? Update(DinoReading reading, TimeSpan now)
    {
        if (!reading.InGame || string.IsNullOrWhiteSpace(reading.Species))
        {
            Forget();
            return null;
        }

        var species = reading.Species;
        var gender = reading.Gender ?? "";
        var growth = reading.Growth;

        if (_species is null || !Continues(species, gender, growth, now))
        {
            _species = species;
            _gender = gender;
            _lastGrowth = growth;
            _lastTime = now;
            _high = growth;
            return null;
        }

        _lastGrowth = growth;
        _lastTime = now;
        if (!(growth > _high)) return null;

        var step = new SeenStep(species, gender, _high, growth);
        _high = growth;
        return step;
    }

    /// <summary>
    /// Drops the life being watched, so the next reading is a first sight and
    /// counts nothing. <see cref="Update"/> does this itself for a reading that
    /// is not in game; a game calls it when it stops listening for a while.
    /// </summary>
    public void Forget()
    {
        _species = null;
        _gender = "";
        _lastGrowth = 0;
        _lastTime = TimeSpan.Zero;
        _high = 0;
    }

    /// <summary>Whether a reading continues the life with a baseline (rule 3 of <see cref="Update"/>).</summary>
    private bool Continues(string species, string gender, double growth, TimeSpan now)
    {
        if (!string.Equals(species, _species, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(gender, _gender, StringComparison.OrdinalIgnoreCase)) return false;
        if (!(growth >= _lastGrowth - Slack - RoundingAllowance)) return false;

        var dt = now - _lastTime;
        if (dt <= TimeSpan.Zero || dt > MaxStepGap) return false;

        var rise = growth - _lastGrowth;
        return rise <= MaxGrowthPerSecond * dt.TotalSeconds + RoundingAllowance;
    }
}
