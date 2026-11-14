using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The art, held in step with its source (A28). The art tool
/// (<c>tools/games-art/</c>, plain Node) is never run by a build or a test, so
/// the PNGs and frame tables it writes are CHECKED IN, and each game's
/// <c>build.js</c> writes <c>tools/games-art/&lt;game&gt;/manifest.json</c>: the
/// SHA-256 of every .js source and of every file it wrote. These tests read
/// those files (tests may; a game may not) and fail when the two drift apart:
/// a source edited without a rebuild, an output edited by hand, a new .js that
/// no manifest knows, a file in a game's <c>Art/</c> folder nobody wrote, an
/// atlas the engine would refuse or a frame with nothing drawn in it.
///
/// <para>The rules are <see cref="ArtInStep"/>'s functions over a repo root, so
/// they are tested twice: on the real repo (it must be in step now), and on
/// small temporary trees that break each rule once (so a rule that stopped
/// checking would fail a test instead of passing silently).</para>
/// </summary>
public class ArtInStepTests
{
    // ---- The real repo ----

    [Fact]
    public void TheRepoHasAManifest()
    {
        // Guards the guards: with none, every rule below would pass on nothing.
        Assert.NotEmpty(ArtInStep.Manifests(ArtInStep.RepoRoot(), new List<string>()));
    }

    [Fact]
    public void EverySourceAndOutputIsWhatTheLastBuildWrote()
    {
        AssertNone(ArtInStep.HashProblems(ArtInStep.RepoRoot()));
    }

    [Fact]
    public void EveryGeneratorSourceIsInTheManifests()
    {
        AssertNone(ArtInStep.SourceProblems(ArtInStep.RepoRoot()));
    }

    [Fact]
    public void EveryArtFileIsAnOutputAndNothingElseIs()
    {
        AssertNone(ArtInStep.OutputProblems(ArtInStep.RepoRoot()));
    }

    [Fact]
    public void EveryAtlasLoadsAndEveryFrameHoldsAPixel()
    {
        AssertNone(ArtInStep.AtlasProblems(ArtInStep.RepoRoot()));
    }

    // ---- The rules, each broken once in a small tree ----

    [Fact]
    public void ATreeInStepHasNoProblem()
    {
        using var tree = new FakeRepo();
        AssertNone(ArtInStep.All(tree.Root));
    }

    [Fact]
    public void ASourceChangedWithoutARebuildFailsAndSaysWhatToRun()
    {
        using var tree = new FakeRepo();
        File.AppendAllText(tree.Path("tools/games-art/hatchling/species.js"), "// a comment\n");

        var problems = ArtInStep.HashProblems(tree.Root);

        var one = Assert.Single(problems);
        Assert.Contains("tools/games-art/hatchling/species.js", one);
        Assert.Contains("run node tools/games-art/hatchling/build.js", one);
    }

    [Fact]
    public void AnOutputEditedByHandFails()
    {
        using var tree = new FakeRepo();
        File.AppendAllText(tree.Path("Games/PandoraOverlay.Games.Hatchling/Art/creatures.json"), " ");

        var one = Assert.Single(ArtInStep.HashProblems(tree.Root));
        Assert.Contains("creatures.json", one);
        Assert.Contains("run node tools/games-art/hatchling/build.js", one);
    }

    [Fact]
    public void AListedFileThatIsGoneFails()
    {
        using var tree = new FakeRepo();
        File.Delete(tree.Path("tools/games-art/hex.js"));

        var one = Assert.Single(ArtInStep.HashProblems(tree.Root));
        Assert.Contains("tools/games-art/hex.js", one);
        Assert.Contains("does not exist", one);
    }

    [Fact]
    public void ANewSharedSourceThatNoManifestListsFails()
    {
        using var tree = new FakeRepo();
        tree.Write("tools/games-art/terrain-shapes.js", "// new\n");

        var one = Assert.Single(ArtInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-art/terrain-shapes.js", one);
        Assert.Contains("hatchling", one);
    }

    [Fact]
    public void ANewGameSourceNotInItsGamesManifestFails()
    {
        using var tree = new FakeRepo();
        tree.Write("tools/games-art/hatchling/terrain.js", "// new\n");

        var one = Assert.Single(ArtInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-art/hatchling/terrain.js", one);
    }

    [Fact]
    public void ASourceInAFolderWithNoManifestFails()
    {
        using var tree = new FakeRepo();
        tree.Write("tools/games-art/other/build.js", "// a game that never built\n");

        var one = Assert.Single(ArtInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-art/other/build.js", one);
        Assert.Contains("run node tools/games-art/other/build.js", one);
    }

    [Fact]
    public void AnythingUnderOutIsNotASource()
    {
        using var tree = new FakeRepo();
        tree.Write("tools/games-art/out/scratch.js", "// scratch\n");
        tree.Write("tools/games-art/out/creatures-sheet.png", "not looked at by anything");

        AssertNone(ArtInStep.SourceProblems(tree.Root));
    }

    [Fact]
    public void AnArtFileNobodyWroteFails()
    {
        using var tree = new FakeRepo();
        tree.Write("Games/PandoraOverlay.Games.Hatchling/Art/stray.png", "x");

        var one = Assert.Single(ArtInStep.OutputProblems(tree.Root));
        Assert.Contains("Games/PandoraOverlay.Games.Hatchling/Art/stray.png", one);
        Assert.Contains("not an output", one);
    }

    [Fact]
    public void AnOutputOutsideAnArtFolderFails()
    {
        using var tree = new FakeRepo();
        tree.Write("Games/PandoraOverlay.Games.Hatchling/elsewhere.png", "x");
        tree.WriteManifest("hatchling", extraOutput: "Games/PandoraOverlay.Games.Hatchling/elsewhere.png");

        var one = Assert.Single(ArtInStep.OutputProblems(tree.Root));
        Assert.Contains("elsewhere.png", one);
        Assert.Contains("not under a game's Art folder", one);
    }

    [Fact]
    public void TheTestsOwnFolderNamedArtIsNotAGamesArt()
    {
        // Games/PandoraOverlay.Games.Tests/Art/ holds THIS file; the Contract and the Engine hold none, but never a game's art.
        using var tree = new FakeRepo();
        tree.Write("Games/PandoraOverlay.Games.Tests/Art/ArtInStepTests.cs", "// not art\n");
        tree.Write("Games/PandoraOverlay.Games.Engine/Art/anything.png", "x");

        AssertNone(ArtInStep.OutputProblems(tree.Root));
    }

    [Fact]
    public void AnAtlasWithAMissingTableFails()
    {
        using var tree = new FakeRepo();
        File.Delete(tree.Path("Games/PandoraOverlay.Games.Hatchling/Art/creatures.json"));

        Assert.Contains(ArtInStep.AtlasProblems(tree.Root), p => p.Contains("creatures.png") && p.Contains("frame table"));
    }

    [Fact]
    public void AnAtlasTheEngineRefusesFails()
    {
        using var tree = new FakeRepo();
        // A pivot outside its frame: the engine's own check, named by it.
        tree.Write("Games/PandoraOverlay.Games.Hatchling/Art/creatures.json", FakeRepo.Table(px: 9));

        Assert.Contains(ArtInStep.AtlasProblems(tree.Root), p => p.Contains("creatures") && p.Contains("pivot"));
    }

    [Fact]
    public void AFrameWithNothingDrawnInItFails()
    {
        using var tree = new FakeRepo();
        // The first frame (0, 0, 2 x 2) is the opaque one; this table adds a frame over the transparent half.
        tree.Write("Games/PandoraOverlay.Games.Hatchling/Art/creatures.json", FakeRepo.Table(extraBlankFrame: true));

        var one = Assert.Single(ArtInStep.AtlasProblems(tree.Root));
        Assert.Contains("Blank/stand/0", one);
        Assert.Contains("no pixel", one);
    }

    private static void AssertNone(IReadOnlyList<string> problems) =>
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
}

/// <summary>
/// The rules, as functions over a repo root. Each returns what is wrong, one
/// line per problem, and every line that has a cure says it: "run node
/// tools/games-art/&lt;game&gt;/build.js".
/// </summary>
internal static class ArtInStep
{
    private const string ToolFolder = "tools/games-art";

    // The projects under Games/ that are not games: their Art/ folder, if one
    // exists, is not a game's (the tests' own is the one that holds this file).
    private static readonly string[] NotGames =
    {
        "PandoraOverlay.Games.Contract",
        "PandoraOverlay.Games.Engine",
        "PandoraOverlay.Games.Tests",
    };

    /// <summary>One game's manifest: its sources and outputs, repo-relative with forward slashes, to their hashes.</summary>
    internal sealed record Manifest(string Game, string Path, Dictionary<string, string> Sources, Dictionary<string, string> Outputs);

    /// <summary>Up from the test's bin folder to the folder holding PandoraOverlay.sln: a worktree finds its own.</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "PandoraOverlay.sln"))) return dir.FullName;
        throw new InvalidOperationException("The repo root (PandoraOverlay.sln) is not above the test's folder.");
    }

    public static List<string> All(string root) =>
        HashProblems(root).Concat(SourceProblems(root)).Concat(OutputProblems(root)).Concat(AtlasProblems(root)).ToList();

    /// <summary>Every <c>tools/games-art/*/manifest.json</c>, read. A manifest that can't be read is a problem.</summary>
    public static List<Manifest> Manifests(string root, List<string> problems)
    {
        var found = new List<Manifest>();
        string tool = System.IO.Path.Combine(root, ToolFolder);
        if (!Directory.Exists(tool)) return found;
        foreach (string folder in Directory.GetDirectories(tool).OrderBy(f => f, StringComparer.Ordinal))
        {
            string game = System.IO.Path.GetFileName(folder);
            string file = System.IO.Path.Combine(folder, "manifest.json");
            if (game == "out" || !File.Exists(file)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                found.Add(new Manifest(game, $"{ToolFolder}/{game}/manifest.json",
                    Hashes(document.RootElement.GetProperty("sources")), Hashes(document.RootElement.GetProperty("outputs"))));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                problems.Add($"{ToolFolder}/{game}/manifest.json can't be read ({e.GetType().Name}): {Cure(game)}");
            }
        }
        return found;
    }

    /// <summary>Every file a manifest lists exists and holds the listed hash.</summary>
    public static List<string> HashProblems(string root)
    {
        var problems = new List<string>();
        foreach (var manifest in Manifests(root, problems))
        {
            foreach (var (file, hash) in manifest.Sources.Concat(manifest.Outputs))
            {
                string full = System.IO.Path.Combine(root, file);
                if (!File.Exists(full))
                    problems.Add($"{file}, listed in {manifest.Path}, does not exist: {Cure(manifest.Game)}");
                else if (Sha256(full) != hash)
                    problems.Add($"{file} has changed since the last build ({manifest.Path}): {Cure(manifest.Game)}");
            }
        }
        return problems;
    }

    /// <summary>
    /// Every .js under the tool (out/ aside) is a source: in EVERY manifest when
    /// it sits directly in tools/games-art (shared), in its own game's when it is
    /// under that game's folder.
    /// </summary>
    public static List<string> SourceProblems(string root)
    {
        var problems = new List<string>();
        var manifests = Manifests(root, problems);
        string tool = System.IO.Path.Combine(root, ToolFolder);
        if (!Directory.Exists(tool)) return problems;
        foreach (string file in Directory.EnumerateFiles(tool, "*.js", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string rel = Relative(root, file);
            string[] parts = rel[(ToolFolder.Length + 1)..].Split('/');
            if (parts[0] == "out") continue;
            if (parts.Length == 1)
            {
                foreach (var manifest in manifests.Where(m => !m.Sources.ContainsKey(rel)))
                    problems.Add($"{rel} (shared) is not a source in {manifest.Path}: {Cure(manifest.Game)}");
            }
            else
            {
                var own = manifests.FirstOrDefault(m => m.Game == parts[0]);
                if (own is null)
                    problems.Add($"{rel} belongs to a game with no manifest: {Cure(parts[0])}");
                else if (!own.Sources.ContainsKey(rel))
                    problems.Add($"{rel} is not a source in {own.Path}: {Cure(own.Game)}");
            }
        }
        return problems;
    }

    /// <summary>
    /// Every file under a game's <c>Games/&lt;project&gt;/Art/</c> is an output of a
    /// manifest, and every output is such a file (nothing else is written by a
    /// build).
    /// </summary>
    public static List<string> OutputProblems(string root)
    {
        var problems = new List<string>();
        var outputs = Manifests(root, problems).SelectMany(m => m.Outputs.Keys.Select(o => (Output: o, m.Game))).ToList();
        var art = ArtFiles(root);
        foreach (string file in art)
            if (!outputs.Any(o => o.Output == file))
                problems.Add($"{file} is not an output of any manifest: art is only ever written by a build (node tools/games-art/<game>/build.js)");
        foreach (var (output, game) in outputs)
            if (!art.Contains(output) && !IsArtPath(output))
                problems.Add($"{output}, an output of {ToolFolder}/{game}/manifest.json, is not under a game's Art folder (Games/<project>/Art/)");
        return problems;
    }

    /// <summary>
    /// Every atlas loads through <see cref="Atlas.Load(Stream, Stream)"/>, as a
    /// game's own will, and every frame has at least one pixel that is not
    /// transparent (an empty frame is a drawing that went wrong). The PNG comes
    /// from Node, not from WPF: this is where the engine's decode first sees one.
    /// </summary>
    public static List<string> AtlasProblems(string root)
    {
        var problems = new List<string>();
        var art = ArtFiles(root);
        foreach (string png in art.Where(f => f.EndsWith(".png", StringComparison.Ordinal)))
        {
            string json = png[..^4] + ".json";
            if (!art.Contains(json))
            {
                problems.Add($"{png} has no frame table ({System.IO.Path.GetFileName(json)} beside it)");
                continue;
            }
            try
            {
                using var pngStream = File.OpenRead(System.IO.Path.Combine(root, png));
                using var jsonStream = File.OpenRead(System.IO.Path.Combine(root, json));
                var atlas = Atlas.Load(pngStream, jsonStream);
                if (!atlas.Names.Any()) problems.Add($"{png} has no frame");
                foreach (string name in atlas.Names)
                    if (!HoldsAPixel(atlas[name]))
                        problems.Add($"{png}: frame '{name}' has no pixel that is not transparent");
                using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, json)));
                if (document.RootElement.GetProperty("image").GetString() != System.IO.Path.GetFileName(png))
                    problems.Add($"{json} names another image than {System.IO.Path.GetFileName(png)}");
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException or IOException)
            {
                problems.Add($"{png} doesn't load: {e.Message}");
            }
        }
        foreach (string json in art.Where(f => f.EndsWith(".json", StringComparison.Ordinal)))
            if (!art.Contains(json[..^5] + ".png"))
                problems.Add($"{json} has no image beside it");
        return problems;
    }

    private static bool HoldsAPixel(Sprite sprite)
    {
        var frame = sprite.Frame;
        for (int y = frame.Y; y < frame.Y + frame.Height; y++)
            for (int x = frame.X; x < frame.X + frame.Width; x++)
                if ((sprite.Sheet.Pixels[y * sprite.Sheet.Width + x] >> 24) != 0) return true;
        return false;
    }

    /// <summary>Every file under <c>Games/&lt;project&gt;/Art/</c> of a game project, repo-relative.</summary>
    private static HashSet<string> ArtFiles(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        string games = System.IO.Path.Combine(root, "Games");
        if (!Directory.Exists(games)) return files;
        foreach (string project in Directory.GetDirectories(games))
        {
            string art = System.IO.Path.Combine(project, "Art");
            if (NotGames.Contains(System.IO.Path.GetFileName(project)) || !Directory.Exists(art)) continue;
            foreach (string file in Directory.EnumerateFiles(art, "*", SearchOption.AllDirectories))
                files.Add(Relative(root, file));
        }
        return files;
    }

    private static bool IsArtPath(string rel)
    {
        string[] parts = rel.Split('/');
        return parts.Length >= 4 && parts[0] == "Games" && parts[2] == "Art" && !NotGames.Contains(parts[1]);
    }

    private static Dictionary<string, string> Hashes(JsonElement element) =>
        element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);

    private static string Cure(string game) => $"run node {ToolFolder}/{game}/build.js";

    public static string Relative(string root, string full) =>
        System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');

    public static string Sha256(string file) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
}

/// <summary>
/// A small repo in a temporary folder, in step: the shared sources, one game
/// ("hatchling") with its sources and manifest, and one atlas under that game's
/// Art folder (a 4 x 2 sheet whose left 2 x 2 is opaque red and right 2 x 2 is
/// transparent), written by <see cref="FrameGolden.Save"/>, the same PNG
/// writer the golden frames use. A test breaks one thing and runs one rule.
/// </summary>
internal sealed class FakeRepo : IDisposable
{
    private const string Art = "Games/PandoraOverlay.Games.Hatchling/Art/";
    private static readonly string[] Shared = { "engine.js", "hex.js", "pack.js" };

    public FakeRepo()
    {
        Root = Directory.CreateTempSubdirectory("games-art-").FullName;
        foreach (string name in Shared) Write($"tools/games-art/{name}", $"// {name}\n");
        Write("tools/games-art/hatchling/build.js", "// build\n");
        Write("tools/games-art/hatchling/species.js", "// species\n");

        var sheet = new PixelBuffer(4, 2);
        sheet.Fill(0, 0, 2, 2, PixelBuffer.Colour(255, 0, 0));
        Directory.CreateDirectory(Path(Art));
        FrameGolden.Save(sheet, Path(Art + "creatures.png"));
        Write(Art + "creatures.json", Table());
        WriteManifest("hatchling");
    }

    public string Root { get; }

    public string Path(string rel) => System.IO.Path.Combine(Root, rel);

    public void Write(string rel, string text)
    {
        string full = Path(rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>The manifest of a game as its build would write it, from the files now on disk.</summary>
    public void WriteManifest(string game, string? extraOutput = null)
    {
        var sources = Directory.EnumerateFiles(Path("tools/games-art"), "*.js", SearchOption.AllDirectories)
            .Select(f => ArtInStep.Relative(Root, f))
            .Where(f => !f.StartsWith("tools/games-art/out/", StringComparison.Ordinal))
            .Where(f => f.Count(c => c == '/') == 2 || f.StartsWith($"tools/games-art/{game}/", StringComparison.Ordinal));
        var outputs = Directory.EnumerateFiles(Path(Art), "*").Select(f => ArtInStep.Relative(Root, f)).Concat(extraOutput is null ? Array.Empty<string>() : new[] { extraOutput });
        string Entries(IEnumerable<string> files) =>
            string.Join(",", files.Select(f => $"\"{f}\": \"{ArtInStep.Sha256(Path(f))}\""));
        Write($"tools/games-art/{game}/manifest.json", $"{{ \"sources\": {{ {Entries(sources)} }}, \"outputs\": {{ {Entries(outputs)} }} }}");
    }

    /// <summary>The atlas's frame table. <paramref name="px"/> is the first frame's pivot x (2 x 2: 0 to 2 are fine).</summary>
    public static string Table(int px = 1, bool extraBlankFrame = false) =>
        "{ \"image\": \"creatures.png\", \"frames\": { "
        + $"\"Red/stand/0\": {{ \"x\": 0, \"y\": 0, \"w\": 2, \"h\": 2, \"px\": {px}, \"py\": 1 }}"
        + (extraBlankFrame ? ", \"Blank/stand/0\": { \"x\": 2, \"y\": 0, \"w\": 2, \"h\": 2, \"px\": 1, \"py\": 1 }" : "")
        + " }, \"animations\": { \"Red/stand\": { \"frames\": [\"Red/stand/0\"], \"ticks\": [1], \"loops\": true } } }";

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
