namespace PandoraOverlay.Games.Hatchling;

/// <summary>The five hunters of the island, from the smallest; the Tyrannosaurus is the apex only.</summary>
internal enum PredatorType { Troodon, Herrerasaurus, Carnotaurus, Allosaurus, Tyrannosaurus }

/// <summary>A predator's numbers before the threat; its health and attack are multiplied by the run's threat (H17).</summary>
/// <param name="Fast">Takes <see cref="Tuning.FastSteps"/> steps an action, else <see cref="Tuning.SlowSteps"/>.</param>
/// <param name="Meat">Meals it leaves on its cell when defeated.</param>
internal sealed record PredatorInfo(PredatorType Type, int Health, int Attack, int Defense, bool Fast, int Sight, int Meat);

/// <summary>The predators' table and who hunts whom.</summary>
internal static class Predators
{
    private static readonly PredatorInfo[] Rows =
    {
        //               type                         hp   atk def fast   sight meat
        new PredatorInfo(PredatorType.Troodon,        18,   5, 0, true,  2,    1),
        new PredatorInfo(PredatorType.Herrerasaurus,  40,   9, 1, true,  2,    2),
        new PredatorInfo(PredatorType.Carnotaurus,    75,  15, 2, true,  3,    2),
        new PredatorInfo(PredatorType.Allosaurus,    115,  20, 3, false, 3,    3),
        new PredatorInfo(PredatorType.Tyrannosaurus, 230,  30, 5, false, 3,    4),
    };

    /// <summary>The row of <paramref name="type"/>. Throws for a value that is not a predator: a defect.</summary>
    public static PredatorInfo Of(PredatorType type) =>
        (int)type >= 0 && (int)type < Rows.Length
            ? Rows[(int)type]
            : throw new ArgumentOutOfRangeException(nameof(type), type, "not a predator");

    private const PredatorType T = PredatorType.Troodon;
    private const PredatorType H = PredatorType.Herrerasaurus;
    private const PredatorType C = PredatorType.Carnotaurus;
    private const PredatorType A = PredatorType.Allosaurus;

    // [size][stage]: the design's table, Hatchling to Elder. The apex is never in a
    // pool: it comes by its own rule at Elder. The first predator of a run is a
    // Troodon whatever the pool (H34).
    private static readonly PredatorType[][][] Pools =
    {
        // small: Runner, Forager, Flyer, Hunter
        new[] { new[] { T }, new[] { T, H }, new[] { H, C }, new[] { C, A }, new[] { A } },
        // medium: Grazer, Stalker
        new[] { new[] { T, H }, new[] { H }, new[] { H, C }, new[] { C, A }, new[] { A } },
        // large: Swimmer, Tank, Apex
        new[] { new[] { H }, new[] { H, C }, new[] { C, A }, new[] { A }, new[] { A } },
    };

    /// <summary>
    /// The predators that may arrive for a dino of <paramref name="size"/> at
    /// <paramref name="stage"/> (0 Hatchling to 4 Elder), in the table's order: an
    /// arrival is the pick of this list, so the order is part of
    /// every seeded run. Throws for a stage outside 0 to 4: a defect.
    /// </summary>
    public static IReadOnlyList<PredatorType> Pool(SizeClass size, int stage)
    {
        if ((int)size < 0 || (int)size >= Pools.Length)
            throw new ArgumentOutOfRangeException(nameof(size), size, "not a size class");
        var bySize = Pools[(int)size];
        if (stage < 0 || stage >= bySize.Length)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "a stage is 0 to 4");
        return bySize[stage];
    }

    /// <summary>The predator's species name, spelled as <see cref="SpeciesTable"/> spells it (a test holds it).</summary>
    public static string NameOf(PredatorType type) => type switch
    {
        PredatorType.Troodon => "Troodon",
        PredatorType.Herrerasaurus => "Herrerasaurus",
        PredatorType.Carnotaurus => "Carnotaurus",
        PredatorType.Allosaurus => "Allosaurus",
        PredatorType.Tyrannosaurus => "Tyrannosaurus",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "not a predator"),
    };
}
