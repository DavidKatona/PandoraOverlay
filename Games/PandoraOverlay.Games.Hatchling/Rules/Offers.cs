using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The offers of instincts (H14): at Juvenile, Subadult and Adult a run chooses one
/// of three (four with Sharp instincts) for the rest of the run, so every run asks a
/// different question. One offer is open at a time; the next owed is drawn when it
/// is answered. Nothing here passes a turn: choosing is not a move.
///
/// <para>Two kinds of member. <see cref="Owe"/> and <see cref="OpenIfOwed"/> are the
/// building blocks a rule uses on ITS OWN copy of the run (the start of a run, the
/// world's turn crossing a line), so they change the run given. <see cref="Pick"/>
/// and <see cref="Redraw"/> are the player's answers, rules in their own right: they
/// return a new run and never change the one given.</para>
/// </summary>
internal static class Offers
{
    /// <summary>Adds <paramref name="picks"/> to the offers owed (none for 0 or less). Changes the run given.</summary>
    public static void Owe(RunState run, int picks)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (picks > 0) run.PicksOwed += picks;
    }

    /// <summary>
    /// Draws the next offer when one is owed and none is open; else nothing, and no
    /// roll. Its rolls come from <paramref name="rng"/>, the caller's generator for the
    /// run, which the caller writes back. Changes the run given.
    /// </summary>
    public static void OpenIfOwed(RunState run, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rng);
        if (run.Offer is null && run.PicksOwed > 0)
            Draw(run, rng, shown: null);
    }

    /// <summary>
    /// The player takes the offer's instinct at <paramref name="index"/>: the run gains
    /// it, the offer closes, one pick is paid off, and the next owed offer opens (its
    /// rolls from the run's own generator). Its effect is at once; health being a
    /// fraction, Hardy raises the maximum and keeps the share. Null when no offer is
    /// open or the index is not one of its places.
    /// </summary>
    public static RunState? Pick(RunState run, int index)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Offer is null || index < 0 || index >= run.Offer.Count) return null;

        var next = run.Clone();
        next.Instincts.Add(next.Offer![index]);
        next.Offer = null;
        next.PicksOwed = Math.Max(0, next.PicksOwed - 1);
        var rng = new Rng(next.Rng);
        OpenIfOwed(next, rng);
        next.Rng = rng.State;
        return next;
    }

    /// <summary>
    /// What the next redraw costs: nothing while Second thoughts' free one is unspent;
    /// else <see cref="Tuning.RedrawPrice"/> doubled for each paid redraw so far this
    /// run (25, 50, 100 ...), held at int's top rather than wrapping.
    /// </summary>
    public static int RedrawPrice(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Rank(Trait.SecondThoughts) > 0 && !run.FreeRedrawUsed) return 0;
        var price = (long)Tuning.RedrawPrice;
        for (var n = 0; n < run.Redraws && price < int.MaxValue; n++) price *= 2;
        return (int)Math.Min(price, int.MaxValue);
    }

    /// <summary>
    /// A new offer in place of the open one: the free redraw spent if Second thoughts
    /// has one left, else one more paid redraw counted (the next costs twice). The
    /// caller has already paid <see cref="RedrawPrice"/>; the wallet is not the run's.
    /// The new offer leaves out the one just shown while enough others are left. Null
    /// when no offer is open.
    /// </summary>
    public static RunState? Redraw(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Offer is null) return null;

        var next = run.Clone();
        if (next.Rank(Trait.SecondThoughts) > 0 && !next.FreeRedrawUsed) next.FreeRedrawUsed = true;
        else next.Redraws++;
        var shown = next.Offer!;
        next.Offer = null;
        var rng = new Rng(next.Rng);
        Draw(next, rng, shown);
        next.Rng = rng.State;
        return next;
    }

    /// <summary>How many instincts an offer holds: <see cref="Tuning.OfferSize"/>, or <see cref="Tuning.SharpOfferSize"/> with Sharp instincts.</summary>
    public static int Size(RunState run) =>
        run.Rank(Trait.SharpInstincts) > 0 ? Tuning.SharpOfferSize : Tuning.OfferSize;

    /// <summary>
    /// Opens an offer: the instincts the run does not have, in the table's order,
    /// without those in <paramref name="shown"/> while at least an offer's worth of
    /// others is left, shuffled by the run's generator, the first <see cref="Size"/>
    /// of them (all of them when fewer are left). The table's order before the
    /// shuffle is what makes a seeded run's offers the same for ever.
    ///
    /// <para>When every instinct is owned nothing can be offered: no offer opens and
    /// nothing stays owed, so a run is never stuck on an empty offer. Twelve
    /// instincts against at most five picks a run, it can't happen; it is said so a
    /// future table change can't make it a trap.</para>
    /// </summary>
    private static void Draw(RunState run, Rng rng, IReadOnlyCollection<Instinct>? shown)
    {
        var size = Size(run);
        var notOwned = Instincts.All.Where(i => !run.Has(i)).ToList();
        if (notOwned.Count == 0)
        {
            run.PicksOwed = 0;
            return;
        }
        var candidates = notOwned;
        if (shown is not null)
        {
            var others = notOwned.Where(i => !shown.Contains(i)).ToList();
            if (others.Count >= size) candidates = others;
        }
        rng.Shuffle(candidates);
        run.Offer = candidates.Take(size).ToList();
    }
}
