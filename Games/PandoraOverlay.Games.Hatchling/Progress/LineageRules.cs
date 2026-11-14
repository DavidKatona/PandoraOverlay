namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Buying the lineage (H11): sixteen traits in four branches of four tiers, 38
/// ranks, 42,180 in all. A trait needs the one a tier above it in its branch at
/// rank 1; rank r costs its tier's base times r (<see cref="Lineage"/>).
///
/// <para><b>A run under way is untouched (H19).</b> A run began with a copy of
/// the ranks (<see cref="GameState.SetupFor"/>), and nothing here reads or writes
/// <see cref="GameState.Run"/>: a trait bought on the Lineage page during a run
/// counts from the next run, so a saved run always replays the same.</para>
///
/// <para>Paid only from the game's one wallet (G3). The two achievements of the
/// lineage, <see cref="AchievementList.Roots"/> and
/// <see cref="AchievementList.Blood"/>, are granted here, in the purchase that
/// earns them.</para>
/// </summary>
internal static class LineageRules
{
    /// <summary>The next rank's price: the tier's base times that rank; 0 when the trait is complete.</summary>
    public static int PriceOf(GameState state, Trait trait)
    {
        var info = Lineage.Of(trait);
        var rank = state.Rank(trait);
        return rank >= info.MaxRank ? 0 : info.Price * (rank + 1);
    }

    /// <summary>The trait a tier above this one in its branch is still at rank 0. A first tier is never blocked.</summary>
    public static bool Blocked(GameState state, Trait trait)
    {
        var above = Lineage.Above(trait);
        return above is not null && state.Rank(above.Trait) == 0;
    }

    /// <summary>Every trait is at its last rank.</summary>
    public static bool Complete(GameState state) =>
        Lineage.All.All(info => state.Rank(info.Trait) >= info.MaxRank);

    /// <summary>The next rank can be bought now: the trait is not complete, not blocked, and the wallet holds its price.</summary>
    public static bool CanBuy(GameState state, Trait trait) =>
        !IsComplete(state, trait) && !Blocked(state, trait) && state.Wallet.Balance >= PriceOf(state, trait);

    /// <summary>
    /// Buys the next rank of <paramref name="trait"/>: a new state whose wallet
    /// spent exactly its price and whose rank rose by one, with "roots" granted at
    /// the tenth rank bought and "blood" at the last; null, and nothing changed,
    /// when it can't be bought (complete, blocked, short). The state given is left
    /// as it was, and so is its run (H19).
    /// </summary>
    public static GameState? Buy(GameState state, Trait trait)
    {
        if (!CanBuy(state, trait)) return null;
        var price = PriceOf(state, trait);

        var next = state.Clone();
        if (!next.Wallet.TrySpend(price)) return null;   // CanBuy checked; a wallet's own refusal still wins
        next.Ranks[trait] = next.Rank(trait) + 1;
        next.Totals.RanksBought++;

        if (next.Totals.RanksBought >= AchievementList.RootsRanks)
            next.Achievements.Grant(AchievementList.Roots, next.Wallet);
        if (Complete(next))
            next.Achievements.Grant(AchievementList.Blood, next.Wallet);
        return next;
    }

    private static bool IsComplete(GameState state, Trait trait) =>
        state.Rank(trait) >= Lineage.Of(trait).MaxRank;
}
