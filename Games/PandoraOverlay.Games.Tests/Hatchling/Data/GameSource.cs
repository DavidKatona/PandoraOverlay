using System.IO;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The game's source files, for the scans that hold it to its rules (A24, H24):
/// every file under <c>Games/PandoraOverlay.Games.Hatchling/</c> of THIS
/// checkout (found by walking up to <c>PandoraOverlay.sln</c>, so a worktree
/// scans its own), but the build's <c>bin</c> and <c>obj</c> at any depth.
/// </summary>
internal static class GameSource
{
    /// <summary>The game's project folder.</summary>
    public static string Folder => Path.Combine(RepoRoot(), "Games", "PandoraOverlay.Games.Hatchling");

    /// <summary>Every file of the game's source, as (a path relative to the project folder with '/', its text).</summary>
    public static IReadOnlyList<(string Path, string Text)> Files()
    {
        var folder = Folder;
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .Where(relative => !IsBuildOutput(relative))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .Select(relative => (relative, File.ReadAllText(Path.Combine(folder, relative))))
            .ToList();
    }

    /// <summary>A path inside a <c>bin</c> or <c>obj</c> folder at any depth.</summary>
    public static bool IsBuildOutput(string relative) =>
        relative.Split('/').SkipLast(1).Any(part => part is "bin" or "obj");

    /// <summary>The lines of <paramref name="files"/> that hold <paramref name="word"/>, ignoring case, as "path:line".</summary>
    public static IReadOnlyList<string> LinesHolding(IEnumerable<(string Path, string Text)> files, string word) =>
        files.SelectMany(f => f.Text.Split('\n')
                .Select((line, index) => (line, index))
                .Where(x => x.line.Contains(word, StringComparison.OrdinalIgnoreCase))
                .Select(x => $"{f.Path}:{x.index + 1}"))
            .ToList();

    /// <summary>The checkout's root: up from the test's bin folder to the folder holding PandoraOverlay.sln.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PandoraOverlay.sln"))) return dir.FullName;
        throw new InvalidOperationException("The repo root (PandoraOverlay.sln) is not above the test's folder.");
    }
}
