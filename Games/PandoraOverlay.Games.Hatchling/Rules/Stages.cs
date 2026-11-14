namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The five stages of a run's growth: 0 Hatchling, 1 Juvenile, 2 Subadult,
/// 3 Adult, 4 Elder. The lines are <see cref="Tuning.StageLines"/> (25, 50, 75,
/// 100), and a value ON a line is past it: 25.0 is Juvenile, as the prototype's
/// <c>hxStageIndex</c> counts (<c>growth &gt;= 25</c>). The pools of predators,
/// the offers owed and the pay all ask this one function, so a stage can never
/// mean two things in two rules.
/// </summary>
internal static class Stages
{
    /// <summary>Hatchling, the stage below every line.</summary>
    public const int Hatchling = 0;
    /// <summary>Juvenile: past the first line.</summary>
    public const int Juvenile = 1;
    /// <summary>Subadult: past the second line.</summary>
    public const int Subadult = 2;
    /// <summary>Adult: past the third line, where the scale reaches the build's adult numbers.</summary>
    public const int Adult = 3;
    /// <summary>Elder: full growth; the apex comes.</summary>
    public const int Elder = 4;

    /// <summary>
    /// The stage of <paramref name="growth"/> (a percent): the number of lines it is
    /// on or past, 0 to 4. A NaN growth is past no line, so it is Hatchling: a
    /// broken number never claims a stage it did not reach.
    /// </summary>
    public static int Of(double growth)
    {
        var stage = 0;
        foreach (var line in Tuning.StageLines)
        {
            if (growth >= line) stage++;
            else break;   // the lines rise: a value under one is under every one after it
        }
        return stage;
    }
}
