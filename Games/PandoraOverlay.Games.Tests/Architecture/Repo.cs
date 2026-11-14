using System.IO;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The repo the tests were built from, for the tests that read its files (the
/// project rules, the source scans). The games' tests never reference the app,
/// so this is their own copy of the walk the app's tests do (the two test
/// projects share no code).
/// </summary>
internal static class Repo
{
    /// <summary>The repo's root: up from the test's output folder to the folder holding PandoraOverlay.sln (in a worktree, the worktree's own).</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The folder of the games' projects.</summary>
    public static string Games => Path.Combine(Root, "Games");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PandoraOverlay.sln"))) return dir.FullName;
        throw new InvalidOperationException("the repo root (PandoraOverlay.sln) is not above the test's folder");
    }

    /// <summary>The path from the root, with '/' between its folders: how a failure names a file.</summary>
    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>Whether a path from the root is a build's output: a bin or obj folder at any depth.</summary>
    public static bool IsBuildOutput(string relative) =>
        relative.Split('/', '\\').Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase) || s.Equals("obj", StringComparison.OrdinalIgnoreCase));

    /// <summary>The files under <paramref name="folder"/> matching <paramref name="pattern"/>, a build's output left out.</summary>
    public static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Where(f => !IsBuildOutput(Relative(f)))
            : Enumerable.Empty<string>();
}
