namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Camp's choices: the species, the danger and the pace of the next run. Camp is
/// what is shown when no run exists (H20).
///
/// <para>Any of the 21 can be CHOSEN, a locked one too, so its card can show what
/// it is and what unlocks it; only an unlocked one can be STARTED. A danger can be
/// chosen up to the highest unlocked (H15).</para>
/// </summary>
internal static class Camp
{
    /// <summary>
    /// Chooses <paramref name="species"/> (found ignoring case and kept in the
    /// table's spelling, H36), locked or not. A name the table does not know
    /// changes nothing: a new state like the one given.
    /// </summary>
    public static GameState Select(GameState state, string species)
    {
        var next = state.Clone();
        if (SpeciesTable.TryGet(species, out var info)) next.Selected = info.Name;
        return next;
    }

    /// <summary>Chooses the next run's danger; null outside 1 to 5 or above <see cref="GameState.MaxDanger"/>.</summary>
    public static GameState? SetDanger(GameState state, int danger)
    {
        if (danger < Dangers.Lowest || danger > Dangers.Highest || danger > state.MaxDanger) return null;
        var next = state.Clone();
        next.Danger = danger;
        return next;
    }

    /// <summary>Chooses the playback's pace (H5): Fast plays twice as quick.</summary>
    public static GameState SetFast(GameState state, bool fast)
    {
        var next = state.Clone();
        next.Fast = fast;
        return next;
    }

    /// <summary>
    /// A run can begin: none exists (H20: a new run never drops an old one), the
    /// chosen species is unlocked, and the chosen danger is one that is unlocked.
    /// </summary>
    public static bool CanStart(GameState state) =>
        state.Run is null
        && Unlocks.IsUnlocked(state, state.Selected)
        && state.Danger >= Dangers.Lowest && state.Danger <= state.MaxDanger;
}
