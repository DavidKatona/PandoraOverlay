using System.Reflection;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Whose embedded files the engine reads (A94): a games assembly's alone, one
/// named as the games are, <c>PandoraOverlay.Games.*</c>. The engine's readers
/// (<see cref="Atlas"/>, <see cref="SoundBank"/>) ask here before they ask an
/// assembly for anything. A game never asks an assembly for its embedded files
/// itself (the forbidden list leaves that call to the engine), so no game reads
/// another assembly's: the app's hold its data and, in a release build, the
/// site's scrambled access header (CLAUDE.md, constraint 4).
/// </summary>
internal static class GameAssembly
{
    /// <summary>How every games assembly's name starts: the Contract's, the engine's, each game's (and the games' tests').</summary>
    internal const string Prefix = "PandoraOverlay.Games.";

    /// <summary>The assembly's simple name, for a reader's errors, when it is a games assembly; any other is an <see cref="ArgumentException"/> naming it, before anything is read.</summary>
    internal static string NameOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var name = assembly.GetName().Name;
        if (name is null || !name.StartsWith(Prefix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"The engine reads a game's own embedded files only (A94): '{name ?? "an unnamed assembly"}' is not a games assembly ({Prefix}*).",
                nameof(assembly));
        return name;
    }
}
