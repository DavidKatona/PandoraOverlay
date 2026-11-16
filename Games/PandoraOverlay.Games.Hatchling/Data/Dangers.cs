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

/// <summary>
/// The five danger levels (H15). Danger 2's threat (1.20 to 1.30) and danger 5's threat
/// (1.85 to 2.00) were turned by the balance work, within its limits (H33), for tests
/// B5 and B4 (H32): with the full lineage the bot won 97.8% of runs at danger 1 and
/// 93.1% at danger 2 (B5 asks for a fall of 5 points), and 46.3% at danger 5. Danger
/// 5's turns to outlast the apex went from 21 to 25 by the planner's ruling (H63): at
/// 23 the bot still won 35.5% with the whole lineage (35.9% over 400 runs a species,
/// B4 asks for 35% at most); at 25 it wins 30.4%.
/// </summary>
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
        new DangerInfo(2,    1.30,  6,    2,   5,  15,     1.25),
        new DangerInfo(3,    1.40,  6,    3,   6,  17,     1.50),
        new DangerInfo(4,    1.60,  5,    3,   6,  19,     1.75),
        new DangerInfo(5,    2.00,  5,    4,   7,  25,     2.00),
    };

    /// <summary>The five, from 1 to 5.</summary>
    public static IReadOnlyList<DangerInfo> All { get; } = Rows;

    /// <summary>The row of <paramref name="level"/>, 1 to 5. Throws outside: a defect (a save holding one is refused when it loads).</summary>
    public static DangerInfo Of(int level) =>
        level >= Lowest && level <= Highest
            ? Rows[level - 1]
            : throw new ArgumentOutOfRangeException(nameof(level), level, "a danger is 1 to 5");
}
