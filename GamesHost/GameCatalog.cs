using PandoraOverlay.Games;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay;

/// <summary>
/// One listed game: its id and how to make an instance of it. The id is the
/// catalog's, not the instance's: the host names the game's save folder, the
/// config's memory of it (A31) and its log lines by this id, so it is known
/// even when making an instance throws. A test holds every listed game's
/// <c>Create().Id</c> equal to it, valid by <see cref="GameIds.IsValid"/> and
/// unique (A37, G3).
/// </summary>
/// <param name="Id">The game's id, exactly as its <see cref="IGame.Id"/> says it.</param>
/// <param name="Create">A new instance, attached by the host before anything else is asked of it. Called through the host's guard: a constructor that throws stops that game, never the overlay (A10).</param>
internal sealed record GameEntry(string Id, Func<IGame> Create);

/// <summary>
/// The games, in the order they were added (G1): the grid shows them in this
/// order, and a new game takes the next cell, so a new game is one line at the
/// END of <see cref="All"/>, never in the middle (a player's grid would
/// reshuffle). Adding a game is that line, a project under Games/, a
/// ProjectReference in the app's csproj and in the games' tests, and its entry
/// in the solution (A2).
/// <para>
/// This is the ONLY app file that may name a game's namespace (A2, held by a
/// test): everywhere else the app sees a game as the Contract's
/// <see cref="IGame"/>, through <see cref="GamesHost"/>. And no app file names
/// the engine's, this one included: a game's picture, its loop and its saves'
/// shape are the game's business, reached through the Contract alone.
/// </para>
/// <para>
/// Hatchling is the first (G7) and so the first cell (G19: the library
/// existed from day one, with nothing in it, and a host over an empty catalog
/// still plays nothing and asks nothing). A line has this shape:
/// <c>new("hatchling", () =&gt; new HatchlingGame()),</c>
/// </para>
/// </summary>
internal static class GameCatalog
{
    public static IReadOnlyList<GameEntry> All { get; } = new GameEntry[]
    {
        new("hatchling", () => new HatchlingGame()),
    };
}
