namespace PandoraOverlay.Games.Hatchling;

/// <summary>Which hint the context line gives: one for each row of the hints' table, and None.</summary>
internal enum HintCode { None, Beside, HuntedHide, Hunted, Rest, Thirst, HungerPlants, HungerMeat, First, Grow }

/// <summary>
/// The hints of a player's first runs (H23): one line in the turn bar that teaches
/// the rules from the screen, with no pop-up and nothing to dismiss (A33 allows
/// none). Pure: what it says is read off the run and what the player may know.
/// Whether a hint shows at all (only in the first <see cref="Tuning.HintRuns"/>
/// runs, and never over a playback or a cell under the mouse) is the view's rule;
/// this says which.
/// </summary>
internal static class Hints
{
    /// <summary>
    /// The first hint that applies, in the table's priority: a predator beside
    /// you; one you know hunting you; hurt with no predator known; thirsty;
    /// hungry; the first turn; otherwise growing. None once the run is over: its
    /// summary is up.
    /// </summary>
    /// <param name="run">The run as it stands.</param>
    /// <param name="hides">Your build hides in forest (the hunted hint says where to break its line of sight).</param>
    /// <param name="eatsPlants">You eat plants (the hungry hint says where to eat).</param>
    /// <param name="maxHealthPercent">Your health as a whole percent of your maximum, as the panel shows it.</param>
    /// <param name="known">What the player may know: a hint never names a danger the board does not show.</param>
    public static HintCode For(RunState run, bool hides, bool eatsPlants, int maxHealthPercent, Known known)
    {
        if (run.Over is not null) return HintCode.None;

        var predators = run.Creatures.Where(c => c.Kind == CreatureKind.Predator && known(run, c)).ToList();
        if (predators.Any(c => Island.Grid.Distance(run.You.At, c.At) == 1)) return HintCode.Beside;
        if (predators.Any(c => c.Alert >= Tuning.AlertSeen)) return hides ? HintCode.HuntedHide : HintCode.Hunted;
        if (maxHealthPercent < Tuning.HintHurt * 100 && predators.Count == 0) return HintCode.Rest;
        if (run.You.Water < Tuning.HintThirsty) return HintCode.Thirst;
        if (run.You.Food < Tuning.HintHungry) return eatsPlants ? HintCode.HungerPlants : HintCode.HungerMeat;
        if (run.Turn == 0) return HintCode.First;
        return HintCode.Grow;
    }

    /// <summary>The hint's words; "" for None.</summary>
    public static string Text(HintCode code) => code switch
    {
        HintCode.Beside => HatchlingText.HintBeside,
        HintCode.HuntedHide => HatchlingText.HintHuntedHide,
        HintCode.Hunted => HatchlingText.HintHunted,
        HintCode.Rest => HatchlingText.HintRest,
        HintCode.Thirst => HatchlingText.HintThirst,
        HintCode.HungerPlants => HatchlingText.HintHungerPlants,
        HintCode.HungerMeat => HatchlingText.HintHungerMeat,
        HintCode.First => HatchlingText.HintFirst,
        HintCode.Grow => HatchlingText.HintGrow,
        _ => "",
    };
}
