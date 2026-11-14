namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Unlocking a species (H10). The way meant is growing it: a seen step on the
/// server that crosses Adult unlocks it for nothing (server play, task H06, through
/// <see cref="Grant"/>). The way round, so nobody is shut out, is paying for it:
/// <c>min(1500, 400 + 100 * Bought)</c>, 400, 500, 600 ... and never more than
/// 1,500. The prototype's flat price was three runs, and nobody would have grown
/// anything for an unlock.
///
/// <para>Species are named as the table spells them (H36), found ignoring case.
/// The achievements of unlocking, <see cref="AchievementList.Six"/> and
/// <see cref="AchievementList.Island"/>, are granted here, whichever way the
/// species came, each once.</para>
/// </summary>
internal static class Unlocks
{
    /// <summary>The next species' price: <see cref="Tuning.UnlockPrice"/> plus <see cref="Tuning.UnlockStep"/> for each one bought, at most <see cref="Tuning.UnlockMost"/>.</summary>
    public static int PriceNow(GameState state) =>
        (int)Math.Min(Tuning.UnlockMost, Tuning.UnlockPrice + (long)Tuning.UnlockStep * state.Bought);

    /// <summary>The species is unlocked (found ignoring case, H36); an unknown name is not.</summary>
    public static bool IsUnlocked(GameState state, string species) =>
        SpeciesTable.TryGet(species, out var info) && state.Unlocked.Contains(info.Name, StringComparer.Ordinal);

    /// <summary>
    /// Buys <paramref name="species"/> for <see cref="PriceNow"/>: a new state with
    /// the species unlocked, the price spent from the game's one wallet (G3), the
    /// count of species bought one higher; null, and nothing changed, when it is
    /// unlocked already, unknown, or the wallet is short.
    /// </summary>
    public static GameState? Buy(GameState state, string species)
    {
        if (!SpeciesTable.TryGet(species, out var info) || IsUnlocked(state, info.Name)) return null;
        var price = PriceNow(state);
        if (state.Wallet.Balance < price) return null;

        var next = state.Clone();
        if (!next.Wallet.TrySpend(price)) return null;
        next.Bought++;
        Add(next, info.Name);
        return next;
    }

    /// <summary>
    /// Unlocks <paramref name="species"/> for nothing, IN the state given: server
    /// play's (task H06), which works on its own clone. It spends nothing and does
    /// not move the price (a species grown is not a species bought). An unknown
    /// species or one unlocked already: nothing happens.
    /// </summary>
    public static void Grant(GameState state, string species)
    {
        if (!SpeciesTable.TryGet(species, out var info) || IsUnlocked(state, info.Name)) return;
        Add(state, info.Name);
    }

    /// <summary>Appends the species and grants what the new count earns: the engine pays each achievement once.</summary>
    private static void Add(GameState state, string name)
    {
        state.Unlocked.Add(name);
        if (state.Unlocked.Count >= AchievementList.WideLineage)
            state.Achievements.Grant(AchievementList.Six, state.Wallet);
        if (SpeciesTable.All.All(s => state.Unlocked.Contains(s.Name, StringComparer.Ordinal)))
            state.Achievements.Grant(AchievementList.Island, state.Wallet);
    }
}
