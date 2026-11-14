using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What one seen step of server play did: the new state, and whether more than
/// pay happened (a star, an unlock, a journal entry finished, an achievement),
/// which the game saves at once instead of soon and which may change the tile.
/// </summary>
/// <param name="State">The state after the step: always a new one, never the one given.</param>
/// <param name="Notable">More than pay happened.</param>
internal sealed record ServerResult(GameState State, bool Notable);

/// <summary>
/// What the player's real growing on the server pays in Hatchling (H10, H18, H12):
/// the game's second purpose, as much as its first (G6), which is to move players
/// to the species nobody plays without punishing anyone.
///
/// <para><b>Nothing pays but a seen step.</b> The engine's <see cref="SeenGrowth"/>
/// decides what the overlay saw (A6): a step between two consecutive in-game
/// readings of one life, no faster than a dino grows, above the life's high-water
/// mark; nothing across a gap, a swap of stored dinos, a restart. This is the only
/// way in, it never looks at a reading, and it never makes up for anything it was
/// not given: no growth is estimated for time the overlay was closed (the promise
/// in public).</para>
///
/// <para><b>Nobody is punished.</b> A Tyrannosaurus at a freshness pays exactly what
/// any species pays at that freshness; the only difference the game makes is that
/// the journal never asks for the two most played (<see cref="Journal"/>). Playing
/// something different pays more because it is fresher, and freshness comes back
/// by the calendar (<see cref="Freshness"/>) with no timer, no countdown and no
/// "come back" (G4).</para>
/// </summary>
internal static class ServerPlay
{
    /// <summary>
    /// What one seen step pays, in this order: the species found in the table
    /// (A23, H36), freshness brought up to date, the pay, the record, the line of
    /// "lately", a star crossed, Adult crossed, the journal's entries, the
    /// achievements of server play. A new state; the state given is left unchanged.
    /// A species the table does not know, or a step with no growth in it (which
    /// <see cref="SeenGrowth"/> never makes), changes nothing and is not notable.
    /// </summary>
    public static ServerResult Apply(GameState state, SeenStep step, DateTime utcNow)
    {
        var next = state.Clone();

        // 1. A species the game does not know is ignored (A23). From here on the
        // table's spelling is the name, never the step's own string (H36).
        if (!SpeciesTable.TryGet(step.Species, out var species)) return new ServerResult(next, false);
        var name = species.Name;
        var percent = step.Grown * 100;
        // Fail soft: SeenGrowth always rises, but a step that did not would pay a
        // negative amount, which the wallet refuses by throwing.
        if (!(percent > 0) || double.IsInfinity(percent)) return new ServerResult(next, false);

        var notable = false;
        var starsBefore = next.Stars(name);

        // 2. Freshness up to the calendar's moment (H18), lazily.
        Freshness.Advance(next, utcNow);

        // 3. The pay: ServerRate a percent times freshness, plus what was owed
        // from before; the whole part is earned, the fraction carried, so a thousand
        // steps of 0.1% pay exactly what one step of 100% would at the same rate.
        var record = next.RecordToChange(name);
        var fresh = record.Fresh;
        var owed = percent * Tuning.ServerRate * fresh + next.Carry;
        var earned = Math.Floor(owed + CarryEpsilon);
        next.Carry = Math.Max(0, owed - earned);
        next.Wallet.Earn((long)earned);

        // 4. The record: grown, and the freshness it wore off.
        record.Grown += percent;
        record.Fresh = Math.Max(Tuning.FreshFloor, record.Fresh - Tuning.FreshPerPercent * percent);

        // 5. Lately: one growing line a species while it is the newest.
        Grew(next, name, percent, fresh, (int)earned);

        // 6. A star crossed: decided by the state's own Stars before and after, never
        // by a comparison of our own, so the two can never disagree (its slack).
        for (var star = starsBefore; star < next.Stars(name); star++)
        {
            Write(next, new LatelyLine(LatelyKind.Star, name, 0, 0, 0, 0));
            notable = true;
        }

        // 7. Adult crossed while watched (H10): counted, and a locked species is
        // unlocked for nothing. A dino first seen past Adult did not cross it while
        // watched; the next one will. Grant does nothing for one unlocked already,
        // so whether to write the line is asked first.
        if (step.From < GrowthStages.Adult && step.To >= GrowthStages.Adult)
        {
            record.Adults++;
            if (!Unlocks.IsUnlocked(next, name))
            {
                Unlocks.Grant(next, name);
                Write(next, new LatelyLine(LatelyKind.Unlocked, name, 0, 0, 0, 0));
                notable = true;
            }
        }

        // 8. The journal (H12): every open entry for this species counts the step.
        if (CountJournal(next, name, percent)) notable = true;

        // 9. The achievements server play can earn, in the table's order. (six and
        // island are the unlock's own, granted inside Unlocks.Grant above.)
        foreach (var id in Earned(next))
            if (next.Achievements.Grant(id, next.Wallet) is not null) notable = true;

        return new ServerResult(next, notable);
    }

    /// <summary>
    /// What the whole part of the amount owed is taken with: an amount a hair under
    /// a whole number from floating point (ten times 0.1, added up, is
    /// 0.9999999999999999) is that whole number, so an exact pay is never held back
    /// a step. Far below the smallest pay a seen step can owe.
    /// </summary>
    internal const double CarryEpsilon = 1e-9;

    /// <summary>The lines of "on the server lately" kept: the newest, at most this many.</summary>
    internal const int LatelyMost = 6;

    /// <summary>
    /// The growth line (4.4's "lately"): when the newest line is this species'
    /// growth, it grows (its percent and its pay add up; its multiplier stays the
    /// one at its first step); otherwise a new line is written.
    /// </summary>
    private static void Grew(GameState state, string name, double percent, double fresh, int earned)
    {
        if (state.Lately.Count > 0 && state.Lately[0] is { Kind: LatelyKind.Grew } newest
            && string.Equals(newest.Species, name, StringComparison.Ordinal))
        {
            state.Lately[0] = newest with { Percent = newest.Percent + percent, Amount = newest.Amount + earned };
            return;
        }
        Write(state, new LatelyLine(LatelyKind.Grew, name, percent, fresh, earned, 0));
    }

    /// <summary>A new line, newest first, keeping <see cref="LatelyMost"/>.</summary>
    private static void Write(GameState state, LatelyLine line)
    {
        state.Lately.Insert(0, line);
        if (state.Lately.Count > LatelyMost)
            state.Lately.RemoveRange(LatelyMost, state.Lately.Count - LatelyMost);
    }

    /// <summary>
    /// Counts <paramref name="percent"/> into every open entry for the species; an
    /// entry that reaches its target pays its reward into the game's one wallet
    /// (G3), counts as done, writes its line, and a new draw takes its place, which
    /// never asks for the species just finished. True when one was finished.
    /// </summary>
    private static bool CountJournal(GameState state, string name, double percent)
    {
        var finished = false;
        var open = state.Journal.Open;
        for (var i = 0; i < open.Count; i++)
        {
            var entry = open[i];
            if (!SpeciesTable.TryGet(entry.Species, out var asked) || asked.Name != name) continue;
            entry.Seen += percent;
            if (!Journal.IsDone(entry)) continue;

            state.Wallet.Earn(entry.Reward);
            state.Journal.Done++;
            Write(state, new LatelyLine(LatelyKind.Journal, name, 0, 0, entry.Reward, (int)entry.Target));
            open[i] = Journal.Draw(state, name);
            finished = true;
        }
        return finished;
    }

    /// <summary>
    /// The ids of server play whose condition holds now, in the table's order (the
    /// engine pays only those not had yet): entries finished, species seen crossing
    /// Adult, a star on all 21, three on all 21. A star is the only power here, and
    /// no achievement gives one (G22).
    /// </summary>
    private static IEnumerable<string> Earned(GameState state)
    {
        if (state.Journal.Done >= AchievementList.ResearcherEntries) yield return AchievementList.Researcher;
        if (state.Journal.Done >= AchievementList.NaturalistEntries) yield return AchievementList.Naturalist;
        if (SpeciesTable.All.Count(s => state.Record(s.Name).Adults > 0) >= AchievementList.DiverseSpecies)
            yield return AchievementList.Diverse;
        if (SpeciesTable.All.All(s => state.Stars(s.Name) >= 1)) yield return AchievementList.Star21;
        if (SpeciesTable.All.All(s => state.Stars(s.Name) >= Tuning.StarsAt.Count)) yield return AchievementList.Mastery;
    }
}
