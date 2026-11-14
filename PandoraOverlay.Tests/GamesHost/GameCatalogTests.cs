using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The catalog (G1, A2, A37) and the host's clock. Every listed game's id
/// must be one the save store takes, listed once (two games never share a
/// save, G3), and exactly what its instance says, and each Create a new
/// instance ("Start it again" needs one). The catalog is empty today: the
/// rule is proven on sample catalogs below, and runs on the real one as soon
/// as a game is listed.
/// </summary>
public class GameCatalogTests
{
    /// <summary>What is wrong with a catalog, one line a fault; empty when nothing is.</summary>
    internal static IReadOnlyList<string> CatalogProblems(IReadOnlyList<GameEntry> catalog)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog)
        {
            if (!GameIds.IsValid(entry.Id)) problems.Add($"\"{entry.Id}\" is not a game id (A37)");
            if (!seen.Add(entry.Id)) problems.Add($"\"{entry.Id}\" is listed twice (G3)");
            IGame first, second;
            try
            {
                first = entry.Create();
                second = entry.Create();
            }
            catch (Exception ex)
            {
                problems.Add($"\"{entry.Id}\" can't be made: {ex.GetType().Name}");
                continue;
            }
            if (first is null || second is null)
            {
                problems.Add($"\"{entry.Id}\" makes no instance");
                continue;
            }
            if (first.Id != entry.Id) problems.Add($"\"{entry.Id}\" makes a game whose id is \"{first.Id}\"");
            if (ReferenceEquals(first, second)) problems.Add($"\"{entry.Id}\" hands out one instance: starting it again needs a new one");
        }
        return problems;
    }

    [Fact]
    public void EveryListedGameHasAValidUniqueIdItsInstanceShares() => TheThemeOnScreen.OnSta(() =>
    {
        // On an STA thread: a game's constructor may make WPF objects.
        var problems = CatalogProblems(GameCatalog.All);

        Assert.True(problems.Count == 0, "The catalog:\n" + string.Join("\n", problems));
    });

    [Fact]
    public void TheCatalogRuleFindsWhatItIsFor()
    {
        var shared = new FakeGame("shared-game");
        var catalog = new[]
        {
            new GameEntry("good-game", () => new FakeGame("good-game")),
            new GameEntry("Bad Id", () => new FakeGame("Bad Id")),
            new GameEntry("good-game", () => new FakeGame("good-game")),
            new GameEntry("other-game", () => new FakeGame("not-the-same")),
            new GameEntry("shared-game", () => shared),
            new GameEntry("broken-game", () => throw new InvalidOperationException("made up")),
            new GameEntry("empty-game", () => null!),
        };

        Assert.Equal(new[]
        {
            "\"Bad Id\" is not a game id (A37)",
            "\"good-game\" is listed twice (G3)",
            "\"other-game\" makes a game whose id is \"not-the-same\"",
            "\"shared-game\" hands out one instance: starting it again needs a new one",
            "\"broken-game\" can't be made: InvalidOperationException",
            "\"empty-game\" makes no instance",
        }, CatalogProblems(catalog));
        Assert.Empty(CatalogProblems(new[] { new GameEntry("good-game", () => new FakeGame("good-game")) }));
    }

    [Fact]
    public void TheCatalogIsALibraryEvenWhenEmpty()
    {
        // G19: the library exists from day one; a host over it plays nothing and asks nothing.
        Assert.NotNull(GameCatalog.All);
        var host = new GamesHost(new GameSaveStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PandoraOverlay.Tests", "never-made"), () => DateTime.UtcNow), GameCatalog.All, new TestGameClock());
        Assert.Equal(GameCatalog.All.Count, host.Games.Count);
    }

    // ---- The clock ---------------------------------------------------------------

    [Fact]
    public void TheClocksCalendarIsUtcNow()
    {
        var clock = new GameClock();

        var now = clock.UtcNow;

        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.True((now - DateTime.UtcNow).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TheClocksElapsedStartsWithItAndNeverGoesBack()
    {
        var clock = new GameClock();

        var first = clock.Elapsed;
        var second = clock.Elapsed;

        Assert.True(first >= TimeSpan.Zero && first < TimeSpan.FromMinutes(1));
        Assert.True(second >= first);
    }
}
