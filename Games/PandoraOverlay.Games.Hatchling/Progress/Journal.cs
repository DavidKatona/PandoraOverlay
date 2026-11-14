using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The field journal's draws and its swap (H12). Three open entries, no time limit
/// (G4): "grow a Maiasaura by 75%" for 400, or, for a big build (Tank, Apex,
/// Stalker, Swimmer, <see cref="BuildInfo.Big"/>), 50% for 500. An entry counts
/// SEEN growth of its species from the moment it was drawn, over any number of
/// lives (server play counts it). Seen percent can be neither stashed in a stored
/// dino nor lost with a life, which "grow it to Adult" could (H12).
///
/// <para><b>The lean.</b> The draw leans to what the player has grown least: each
/// species weighs <c>1 / (1 + Grown / </c><see cref="Tuning.JournalLean"/><c>)</c>.
/// It never asks for <see cref="SpeciesTable.JournalNever"/> (Tyrannosaurus and
/// Deinosuchus, the server's most played, the owner's word): that is the journal's
/// only difference between species, and a Rex player is paid by growth exactly what
/// anyone is at the same freshness (the public promise: nobody is punished, G6).</para>
///
/// <para><b>Its own dice.</b> The roll is the journal's own generator's
/// (<see cref="JournalState.Rng"/>, the engine's Rng state), so a run's rolls never
/// move the journal's and the journal's never move a run's (A20).</para>
///
/// <para><see cref="Fill"/> and <see cref="Draw"/> work IN the state given (a fresh
/// save being made, or server play's clone): they move the journal's generator.
/// <see cref="Swap"/> is a player's click and, like every rule of
/// <c>Progress/</c>, returns a new state and leaves the one given as it was.</para>
/// </summary>
internal static class Journal
{
    /// <summary>
    /// What summed seen growth may fall short of an entry's target by and still
    /// finish it: the stars' slack (<see cref="GameState.StarSlack"/>), for the same
    /// reason (750 steps of 0.1% are 74.99999999999 in floating point, not 75), and
    /// the same number so the two can never disagree.
    /// </summary>
    internal const double TargetSlack = GameState.StarSlack;

    /// <summary>
    /// Draws entries into the journal, IN the state given, until
    /// <see cref="Tuning.JournalEntries"/> are open. A journal already full draws
    /// nothing and moves nothing. A fresh save is filled by this (GameState.Fresh).
    /// </summary>
    public static void Fill(GameState state)
    {
        while (state.Journal.Open.Count < Tuning.JournalEntries)
            state.Journal.Open.Add(Draw(state));
    }

    /// <summary>
    /// One new entry, NOT added to the journal (the caller puts it in its place),
    /// drawn with one roll of the journal's own generator, which is moved IN the
    /// state given. The pool: every species of the table in its order but
    /// <see cref="SpeciesTable.JournalNever"/>, those in an open entry, and
    /// <paramref name="alsoWithout"/> (the species just finished or swapped away,
    /// so the journal never asks again at once for what was just done). Each weighs
    /// <c>1 / (1 + Grown / JournalLean)</c>; <c>pick = roll * the sum</c>; the
    /// species is the first at which the running sum reaches <c>pick</c>, the last
    /// of the pool if none does (a roll a hair under 1 and the sums' last bit).
    /// Throws when the pool is empty, which no state the rules make can be (21
    /// species, two never asked, at most three open and one left out): a defect.
    /// </summary>
    public static JournalEntry Draw(GameState state, string? alsoWithout = null)
    {
        var pool = Pool(state, alsoWithout);
        if (pool.Count == 0)
            throw new InvalidOperationException("The journal has no species left to draw from.");

        var weights = pool.Select(s => Weight(state, s.Name)).ToList();
        var rng = new Rng(state.Journal.Rng);
        var pick = rng.NextDouble() * weights.Sum();
        state.Journal.Rng = rng.State;

        var chosen = pool[^1];
        var running = 0.0;
        for (var i = 0; i < pool.Count; i++)
        {
            running += weights[i];
            if (running >= pick)
            {
                chosen = pool[i];
                break;
            }
        }
        return EntryFor(chosen);
    }

    /// <summary>
    /// Replaces the open entry at <paramref name="index"/> with a new draw, for
    /// <see cref="Tuning.SwapPrice"/> from the game's one wallet (G3): a new state,
    /// the one given unchanged, the new entry in the old one's place, never for the
    /// species swapped away (nor for another open one). Null, and nothing changed,
    /// when the wallet is short or there is no entry at that index. Without it,
    /// three entries for species a player will never touch would close the journal
    /// for ever (H12); the old entry's count is let go with it.
    /// </summary>
    public static GameState? Swap(GameState state, int index)
    {
        if (index < 0 || index >= state.Journal.Open.Count) return null;
        if (state.Wallet.Balance < Tuning.SwapPrice) return null;

        var next = state.Clone();
        if (!next.Wallet.TrySpend(Tuning.SwapPrice)) return null;
        // Drawn while the old entry is still open, so its species is out of the pool
        // twice over: as an open entry and as the one swapped away.
        var swapped = next.Journal.Open[index].Species;
        next.Journal.Open[index] = Draw(next, swapped);
        return next;
    }

    /// <summary>A new entry for <paramref name="species"/>: a big build's target and reward, or the others'; nothing seen yet.</summary>
    internal static JournalEntry EntryFor(SpeciesInfo species)
    {
        var big = Builds.Of(species.Build).Big;
        return new JournalEntry
        {
            Species = species.Name,
            Target = big ? Tuning.JournalBigTarget : Tuning.JournalTarget,
            Reward = big ? Tuning.JournalBigReward : Tuning.JournalReward,
        };
    }

    /// <summary>The entry is finished: its seen growth has reached its target (with <see cref="TargetSlack"/>).</summary>
    internal static bool IsDone(JournalEntry entry) => entry.Seen + TargetSlack >= entry.Target;

    /// <summary>
    /// The species a draw may choose, in the table's order: none of
    /// <see cref="SpeciesTable.JournalNever"/>, none already open, not
    /// <paramref name="alsoWithout"/>. Names are compared through the table,
    /// ignoring case (H36), so an entry whose name differs only in case still
    /// keeps its species out.
    /// </summary>
    private static List<SpeciesInfo> Pool(GameState state, string? alsoWithout)
    {
        var excluded = new HashSet<string>(SpeciesTable.JournalNever, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in state.Journal.Open) excluded.Add(entry.Species);
        if (alsoWithout is not null) excluded.Add(alsoWithout);
        return SpeciesTable.All.Where(s => !excluded.Contains(s.Name)).ToList();
    }

    /// <summary>A species' weight in the draw: 1 for one never grown, a fifth at 200% grown (H12's lean).</summary>
    private static double Weight(GameState state, string species) =>
        1 / (1 + Math.Max(0, state.Record(species).Grown) / Tuning.JournalLean);
}
