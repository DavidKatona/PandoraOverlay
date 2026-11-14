namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// One danger level (H15): what there is to do once the lineage is
/// bought. Chosen at Camp; a win at a level unlocks the next, for every species.
/// </summary>
/// <param name="Level">1 to <see cref="Dangers.Highest"/>.</param>
/// <param name="Threat">The danger's factor on the predators' health and attack, on top of the build's and the species' (H17).</param>
/// <param name="Every">A predator arrives on every turn that is a multiple of this.</param>
/// <param name="MostBase">With <paramref name="MostCap"/>: at most min(MostBase + stage, MostCap) predators besides the apex.</param>
/// <param name="MostCap">See <paramref name="MostBase"/>.</param>
/// <param name="Outlast">Turns the apex must be outlasted, before Elder's patience.</param>
/// <param name="Pay">A run's pay is multiplied by this (its own line, "Danger d", for levels 2 to 5).</param>
internal sealed record DangerInfo(int Level, double Threat, int Every, int MostBase, int MostCap, int Outlast, double Pay)
{
    /// <summary>The most predators, the apex not counted, while you are at <paramref name="stage"/>: the formula's one home.</summary>
    public int Most(int stage) => Math.Min(MostBase + stage, MostCap);
}

/// <summary>The five danger levels (H15). The balance work may turn their threat a little (H33).</summary>
internal static class Dangers
{
    /// <summary>The highest level; a win there unlocks nothing more.</summary>
    public const int Highest = 5;

    /// <summary>The lowest level: every save starts with it and nothing else.</summary>
    public const int Lowest = 1;

    private static readonly DangerInfo[] Rows =
    {
        //             level threat every base cap outlast pay
        new DangerInfo(1,    1.00,  7,    2,   5,  15,     1.00),
        new DangerInfo(2,    1.20,  6,    2,   5,  15,     1.25),
        new DangerInfo(3,    1.40,  6,    3,   6,  17,     1.50),
        new DangerInfo(4,    1.60,  5,    3,   6,  19,     1.75),
        new DangerInfo(5,    1.85,  5,    4,   7,  21,     2.00),
    };

    /// <summary>The five, from 1 to 5.</summary>
    public static IReadOnlyList<DangerInfo> All { get; } = Rows;

    /// <summary>The row of <paramref name="level"/>, 1 to 5. Throws outside: a defect (a save holding one is refused when it loads).</summary>
    public static DangerInfo Of(int level) =>
        level >= Lowest && level <= Highest
            ? Rows[level - 1]
            : throw new ArgumentOutOfRangeException(nameof(level), level, "a danger is 1 to 5");
}
