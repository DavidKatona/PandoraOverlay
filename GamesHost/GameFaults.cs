using System.Diagnostics;
using System.Reflection;

namespace PandoraOverlay;

/// <summary>
/// The last of the three layers of containment (A10), for what slipped past
/// both the host's guard and the engine's: App's handler for an exception
/// nobody caught on the UI thread asks <see cref="TryContain"/> first (wired
/// by A12). An exception whose stack passes through a games assembly (one
/// named <c>PandoraOverlay.Games.*</c>: a game, the engine or the Contract)
/// is marked handled there, logged, and the game it came from stopped; any
/// other exception crashes the overlay exactly as before, so an app bug is
/// never hidden as a game's.
/// <para>
/// Which game: the listed game whose OWN assembly is in the stack, the
/// throw's side first (each game is its own project, so its assembly names
/// it). When only the engine's or the Contract's is (code every game shares,
/// say a playfield's drawing called from a handler the engine did not wrap),
/// the game on screen, which is the only one whose code runs outside the
/// host's calls. When there is neither, the error is logged and still
/// contained: the code was a game's, and the overlay must outlive it.
/// </para>
/// <para>
/// Static because App's handler has no other way to the host: MainWindow sets
/// <see cref="Host"/> and <see cref="OnScreen"/> once it has both. Never
/// throws: a failure in here answers false, and the overlay crashes as it did
/// before games existed rather than swallowing what it could not place.
/// </para>
/// </summary>
internal static class GameFaults
{
    /// <summary>The start of every games assembly's name: a game's, the engine's and the Contract's ("PandoraOverlay.Games.*").</summary>
    internal const string AssemblyPrefix = "PandoraOverlay.Games.";

    /// <summary>The most exceptions looked at in one error, inner ones included: a bound, never a real case.</summary>
    internal const int MaxExceptions = 16;

    /// <summary>The host whose games are blamed; null until MainWindow made it (and no game can have run before then).</summary>
    public static GamesHost? Host { get; set; }

    /// <summary>The game the Games window shows, if it is open on one.</summary>
    public static Func<HostedGame?>? OnScreen { get; set; }

    /// <summary>A frame of the error's stack, or of an inner exception's, is in a games assembly.</summary>
    public static bool IsFromAGame(Exception error) =>
        AssembliesIn(error).Any(assembly => IsGamesAssembly(assembly.GetName().Name));

    /// <summary>
    /// True: the error was a game's; it is logged and its game stopped (or,
    /// with no game to name, logged alone), and App marks it handled. False:
    /// not ours to contain (no host yet, or not a game's), and the overlay
    /// crashes as it always did.
    /// </summary>
    public static bool TryContain(Exception error)
    {
        var host = Host;
        if (host is null || error is null) return false;
        try
        {
            var stack = AssembliesIn(error);
            if (!stack.Any(assembly => IsGamesAssembly(assembly.GetName().Name))) return false;
            var game = Blame(stack, host.Games, OwnAssembly, OnScreen?.Invoke());
            if (game is null)
            {
                AppLog.Games.Exception(LogVerbosity.Error, "A game's code failed outside the host's calls, and no game could be named", error);
                return true;
            }
            host.Stop(game, "code no guard wrapped", error);
            return true;
        }
        catch (Exception failure)
        {
            AppLog.Games.Exception(LogVerbosity.Error, "The games' last net failed", failure);
            return false;
        }
    }

    /// <summary>An assembly's name is a games assembly's: the prefix and a name after it.</summary>
    internal static bool IsGamesAssembly(string? name) =>
        name is not null && name.Length > AssemblyPrefix.Length && name.StartsWith(AssemblyPrefix, StringComparison.Ordinal);

    /// <summary>A listed game's own assembly: its instance's, when that is a games assembly (a game is its own project, A2).</summary>
    private static Assembly? OwnAssembly(HostedGame game) =>
        game.Instance?.GetType().Assembly is { } assembly && IsGamesAssembly(assembly.GetName().Name) ? assembly : null;

    /// <summary>
    /// The game to stop, pure: walking <paramref name="stack"/> from the
    /// throw's side, the first assembly that is a listed game's own names it
    /// (two listed games of one assembly, a test's case only, prefer the one on
    /// screen); when none is, the game on screen; else null.
    /// </summary>
    internal static HostedGame? Blame(IReadOnlyList<Assembly> stack, IReadOnlyList<HostedGame> games, Func<HostedGame, Assembly?> ownAssembly, HostedGame? onScreen)
    {
        foreach (var assembly in stack)
        {
            var owners = games.Where(game => ownAssembly(game) == assembly).ToList();
            if (owners.Count == 0) continue;
            return onScreen is not null && owners.Contains(onScreen) ? onScreen : owners[0];
        }
        return onScreen;
    }

    /// <summary>
    /// The assemblies of the error's frames, each once: the innermost
    /// exception first (the throw itself), its frames from the throw outwards,
    /// then each exception around it. A frame whose method can't be named is
    /// skipped.
    /// </summary>
    internal static IReadOnlyList<Assembly> AssembliesIn(Exception error)
    {
        var found = new List<Assembly>();
        var errors = Chain(error);
        for (var i = errors.Count - 1; i >= 0; i--)
        {
            foreach (var frame in new StackTrace(errors[i], false).GetFrames())
            {
                if (frame.GetMethod()?.Module.Assembly is { } assembly && !found.Contains(assembly)) found.Add(assembly);
            }
        }
        return found;
    }

    /// <summary>The error and every exception inside it (an aggregate's too), outermost first, at most <see cref="MaxExceptions"/>.</summary>
    private static List<Exception> Chain(Exception error)
    {
        var chain = new List<Exception>();
        var next = new Queue<Exception>();
        next.Enqueue(error);
        while (next.Count > 0 && chain.Count < MaxExceptions)
        {
            var e = next.Dequeue();
            if (chain.Contains(e)) continue;
            chain.Add(e);
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) next.Enqueue(inner);
            }
            else if (e.InnerException is { } inner)
            {
                next.Enqueue(inner);
            }
        }
        return chain;
    }
}
