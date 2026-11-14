namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Freshness (H18): the multiplier on what server play pays for a species, from
/// <see cref="Tuning.FreshMax"/> (a species not grown lately, or never) down to
/// <see cref="Tuning.FreshFloor"/> (one grown all the time, which still pays). It
/// falls as the species is grown (server play's) and comes back
/// <see cref="Tuning.FreshPerDay"/> a calendar day (here).
///
/// <para><b>A number, never a timer (G4).</b> Nothing here counts down, schedules
/// anything or answers "how long until": freshness is brought up to date LAZILY,
/// when server play pays or a screen reads it, from the moment it was last brought
/// up to date (<see cref="GameState.FreshAsOf"/>) to the moment handed in. There is
/// no clock here (A29): the calendar's moment is always a parameter, which is also
/// what lets a test walk a week in a line. The way to earn more is always to grow
/// something else now; the public post says "a species you haven't grown in a while
/// earns more", and that is all the game ever says about it.</para>
///
/// <para><b>A clock set back gains nothing.</b> A moment before
/// <see cref="GameState.FreshAsOf"/> changes nothing and leaves FreshAsOf where it
/// was, so the time counted is always from the latest moment seen: turning the
/// calendar back and then forward again counts each real day once.</para>
/// </summary>
internal static class Freshness
{
    /// <summary>
    /// Brings every stored record's freshness up to <paramref name="utcNow"/>, IN
    /// the state given (server play's, on its own clone): with <c>days</c> the
    /// calendar days since <see cref="GameState.FreshAsOf"/>, when there are any,
    /// each record's <c>Fresh</c> rises by <see cref="Tuning.FreshPerDay"/> a day, to
    /// <see cref="Tuning.FreshMax"/> at most, and FreshAsOf becomes
    /// <paramref name="utcNow"/>. A moment at or before FreshAsOf changes nothing at
    /// all. A species with no record is at FreshMax already and stays unstored.
    /// </summary>
    public static void Advance(GameState state, DateTime utcNow)
    {
        var days = DaysSince(state, utcNow);
        if (days <= 0) return;
        foreach (var record in state.Species.Values)
            record.Fresh = Raised(record.Fresh, days);
        state.FreshAsOf = utcNow;
    }

    /// <summary>
    /// The freshness of <paramref name="species"/> at <paramref name="utcNow"/>: what
    /// <see cref="Advance"/> would make it, worked out without changing anything.
    /// What every screen shows (H18), as a multiplier and never as a wait. A species
    /// with no record, or one the table does not know, is at
    /// <see cref="Tuning.FreshMax"/>.
    /// </summary>
    public static double At(GameState state, string species, DateTime utcNow)
    {
        var fresh = state.Record(species).Fresh;
        var days = DaysSince(state, utcNow);
        return days > 0 ? Raised(fresh, days) : fresh;
    }

    /// <summary>
    /// The calendar days from <see cref="GameState.FreshAsOf"/> to
    /// <paramref name="utcNow"/>, fractions included; 0 or less when the calendar
    /// did not move forward (H18: then nothing is counted).
    /// </summary>
    private static double DaysSince(GameState state, DateTime utcNow) => (utcNow - state.FreshAsOf).TotalDays;

    /// <summary><paramref name="fresh"/> after <paramref name="days"/> (more than 0) of coming back, never above <see cref="Tuning.FreshMax"/>.</summary>
    private static double Raised(double fresh, double days) =>
        Math.Min(Tuning.FreshMax, fresh + Tuning.FreshPerDay * days);
}
