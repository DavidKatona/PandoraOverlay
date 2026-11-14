namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Each species' signature perk: one rule the hand feels, never a percentage of a
/// number (H13). In the species table's order: each is used by exactly one species.
/// </summary>
internal enum Perk
{
    Tireless, Freeze, Spikes, Headbutt, Braced, Charge, BigBites, Thagomizer, Frill, HighView,
    Venom, Pounce, Latch, Fleet, Scavenger, Spit, Lunge, Bleeder, CrushingBite, Wader, Evasive,
}

/// <summary>A species of the server, as the game plays it.</summary>
/// <param name="Name">Spelled as the server spells it, so a reading's species finds it (A23).</param>
/// <param name="Threat">The species' own factor on the predators' threat, on top of the build's (H17). 1.00 for every one in the first pass; the balance work retunes them from the C# bot (H33).</param>
internal sealed record SpeciesInfo(string Name, Build Build, Perk Perk, double Threat);

/// <summary>
/// The server's 21 species (A23). The game owns this copy: it can't read the
/// app's resources, so a test in the app's tests holds the names equal to the
/// pack-limit table of the overlay's <c>Assets/rules.json</c>, in its order, and a
/// species the server adds fails that test until the game learns it.
/// </summary>
internal static class SpeciesTable
{
    /// <summary>The 21, in the order of <c>Assets/rules.json</c>'s pack-limit table (herbivores, carnivores, omnivores).</summary>
    public static IReadOnlyList<SpeciesInfo> All { get; } = new[]
    {
        new SpeciesInfo("Dryosaurus", Build.Runner, Perk.Tireless, 1.00),
        new SpeciesInfo("Hypsilophodon", Build.Runner, Perk.Freeze, 1.00),
        new SpeciesInfo("Kentrosaurus", Build.Grazer, Perk.Spikes, 1.00),
        new SpeciesInfo("Pachycephalosaurus", Build.Grazer, Perk.Headbutt, 1.00),
        new SpeciesInfo("Tenontosaurus", Build.Grazer, Perk.Braced, 1.00),
        new SpeciesInfo("Diabloceratops", Build.Grazer, Perk.Charge, 1.00),
        new SpeciesInfo("Maiasaura", Build.Grazer, Perk.BigBites, 1.00),
        new SpeciesInfo("Stegosaurus", Build.Tank, Perk.Thagomizer, 1.00),
        new SpeciesInfo("Triceratops", Build.Tank, Perk.Frill, 1.00),
        new SpeciesInfo("Pteranodon", Build.Flyer, Perk.HighView, 1.00),
        new SpeciesInfo("Troodon", Build.Hunter, Perk.Venom, 1.00),
        new SpeciesInfo("Herrerasaurus", Build.Hunter, Perk.Pounce, 1.00),
        new SpeciesInfo("Omniraptor", Build.Hunter, Perk.Latch, 1.00),
        new SpeciesInfo("Carnotaurus", Build.Stalker, Perk.Fleet, 1.00),
        new SpeciesInfo("Ceratosaurus", Build.Stalker, Perk.Scavenger, 1.00),
        new SpeciesInfo("Dilophosaurus", Build.Hunter, Perk.Spit, 1.00),
        new SpeciesInfo("Deinosuchus", Build.Swimmer, Perk.Lunge, 1.00),
        new SpeciesInfo("Allosaurus", Build.Stalker, Perk.Bleeder, 1.00),
        new SpeciesInfo("Tyrannosaurus", Build.Apex, Perk.CrushingBite, 1.00),
        new SpeciesInfo("Beipiaosaurus", Build.Forager, Perk.Wader, 1.00),
        new SpeciesInfo("Gallimimus", Build.Forager, Perk.Evasive, 1.00),
    };

    private static readonly Dictionary<string, SpeciesInfo> ByName =
        All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The species named <paramref name="name"/>, ignoring case (H36); the species
    /// found carries the table's own spelling. An unknown name, or none, is false
    /// and never an error: a species the server adds before the game knows it is
    /// ignored (A23). Ignoring case because the engine's <c>SeenGrowth</c> and the
    /// overlay's own lookup do: an exact match would pay nothing, silently, for a
    /// reading that differed from the table only in case.
    /// </summary>
    public static bool TryGet(string? name, out SpeciesInfo species)
    {
        if (name is not null && ByName.TryGetValue(name, out var found))
        {
            species = found;
            return true;
        }
        species = null!;
        return false;
    }

    /// <summary>Unlocked in a fresh save: the prototype's two starters, kept.</summary>
    public static IReadOnlyList<string> Starters { get; } = new[] { "Dryosaurus", "Herrerasaurus" };

    /// <summary>The field journal never asks for these: the server's most played (the owner's word).</summary>
    public static IReadOnlyList<string> JournalNever { get; } = new[] { "Tyrannosaurus", "Deinosuchus" };
}
