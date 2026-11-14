namespace PandoraOverlay.Games.Hatchling;

/// <summary>The nine body plans every species is one of. Their numbers are the prototype's, kept on purpose.</summary>
internal enum Build { Runner, Grazer, Tank, Hunter, Stalker, Apex, Swimmer, Flyer, Forager }

/// <summary>What a dino can eat. A build eats one or two; Wader adds fish.</summary>
[Flags]
internal enum Diet { None = 0, Plants = 1, Meat = 2, Fish = 4 }

/// <summary>One kind of food: what a meal was, and what <see cref="Ate"/> reports.</summary>
internal enum Food { Plants, Meat, Fish }

/// <summary>Who hunts whom: the pools of predators are by size class and stage.</summary>
internal enum SizeClass { Small, Medium, Large }

/// <summary>
/// A build's adult numbers. <paramref name="Health"/>,
/// <paramref name="Attack"/> and <paramref name="Defense"/> are at 75% growth,
/// before the scale, the stars, the traits and the instincts.
/// </summary>
/// <param name="Noise">Added to every move's noise: -1 for the quiet builds, +2 for the Apex.</param>
/// <param name="SprintDrain">A sprint costs this times a turn's food and water.</param>
/// <param name="SprintRange">Hexes a sprint can cover.</param>
/// <param name="Hides">Forest is cover for it.</param>
/// <param name="Swims">Water is cover for it, it sprints only in water, and it never wades.</param>
/// <param name="Flies">Rock and water are open to it, and it never wades.</param>
/// <param name="Sight">Added to <see cref="Tuning.BaseSight"/>: +1 for the Flyer.</param>
/// <param name="Hunger">Food drains this many times as fast: 1.3 for the Apex.</param>
/// <param name="Size">Which pool of predators hunts it.</param>
/// <param name="Big">A big build's journal entries ask for less growth and pay more.</param>
/// <param name="Threat">The build's share of the predators' threat (H17): the bot's first pass; the balance work retunes the species' factor instead (H33).</param>
internal sealed record BuildInfo(Build Build, int Health, double Attack, int Defense, Diet Eats, int Noise,
    double SprintDrain, int SprintRange, bool Hides, bool Swims, bool Flies, int Sight, double Hunger,
    SizeClass Size, bool Big, double Threat);

/// <summary>The builds' table, in the enum's order.</summary>
internal static class Builds
{
    /// <summary>The nine, in <see cref="Build"/>'s order, so <c>All[(int)build]</c> is that build's row.</summary>
    public static IReadOnlyList<BuildInfo> All { get; } = new[]
    {
        //            build          hp   atk def eats                     noise drain range hides  swims  flies  sight hunger size               big    threat
        new BuildInfo(Build.Runner,   70,  7, 1, Diet.Plants,              -1,   1.5,  2,    true,  false, false, 0,    1.0,   SizeClass.Small,   false, 0.84),
        new BuildInfo(Build.Grazer,  110, 11, 3, Diet.Plants,               0,   2,    2,    true,  false, false, 0,    1.0,   SizeClass.Medium,  false, 1.05),
        new BuildInfo(Build.Tank,    170, 16, 6, Diet.Plants,               1,   2.5,  2,    false, false, false, 0,    1.0,   SizeClass.Large,   true,  1.16),
        new BuildInfo(Build.Hunter,   80, 12, 1, Diet.Meat,                 0,   1.5,  2,    true,  false, false, 0,    1.0,   SizeClass.Small,   false, 0.98),
        new BuildInfo(Build.Stalker, 130, 18, 3, Diet.Meat,                 0,   2,    2,    true,  false, false, 0,    1.0,   SizeClass.Medium,  true,  1.30),
        new BuildInfo(Build.Apex,    210, 28, 5, Diet.Meat,                 2,   2,    2,    false, false, false, 0,    1.3,   SizeClass.Large,   true,  1.43),
        new BuildInfo(Build.Swimmer, 160, 24, 4, Diet.Meat | Diet.Fish,     0,   2,    2,    false, true,  false, 0,    1.0,   SizeClass.Large,   true,  1.85),
        new BuildInfo(Build.Flyer,    55,  8, 0, Diet.Meat | Diet.Fish,     0,   1.5,  3,    false, false, true,  1,    1.0,   SizeClass.Small,   false, 0.98),
        new BuildInfo(Build.Forager,  75,  8, 1, Diet.Plants | Diet.Meat,  -1,   1.5,  2,    true,  false, false, 0,    1.0,   SizeClass.Small,   false, 0.94),
    };

    /// <summary>The row of <paramref name="build"/>. Throws for a value that is not a build: a defect, never data.</summary>
    public static BuildInfo Of(Build build) =>
        (int)build >= 0 && (int)build < All.Count
            ? All[(int)build]
            : throw new ArgumentOutOfRangeException(nameof(build), build, "not a build");
}
