using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Hatchling's species against the overlay's own list (A23). The game owns its
/// table of the 21 because it can't read the app's resources; this test sees both
/// sides and holds them equal: the names of the bundled <c>Assets/rules.json</c>'s
/// pack-limit table, as a set and in its order, spelled exactly (a reading names
/// the dino in the server's spelling). A species the server adds fails here until
/// the game learns it.
/// </summary>
public sealed class SpeciesInStepTests
{
    private static IReadOnlyList<string> RulesSpecies()
    {
        var rules = ServerRules.LoadBundled();
        Assert.NotNull(rules);
        return rules!.PackLimits.SelectMany(category => category.Species).Select(species => species.Name).ToList();
    }

    [Fact]
    public void TheGamesSpeciesAreTheRulesSpeciesInTheirOrder()
    {
        var rules = RulesSpecies();
        var game = SpeciesTable.All.Select(species => species.Name).ToList();

        Assert.Equal(21, rules.Count);
        Assert.Equal(rules.ToHashSet(StringComparer.Ordinal), game.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(rules, game);
    }

    [Fact]
    public void EveryRulesSpeciesIsFoundByItsSpelling()
    {
        foreach (var name in RulesSpecies())
            Assert.True(SpeciesTable.TryGet(name, out _), name);
    }
}
