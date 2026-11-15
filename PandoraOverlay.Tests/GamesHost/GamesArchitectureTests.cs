using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The games' architecture as the app keeps it (A2; CLAUDE.md constraints 1
/// to 4): the app talks to a game only through the Contract, names an Engine
/// type nowhere and a game's namespace in one file only, and its games host
/// names nothing that could make a request or touch a process. The games'
/// side of the same rules is the games' own tests
/// (Games/PandoraOverlay.Games.Tests/Architecture); the two test projects
/// share no code, so the few lines that read metadata are written again here.
/// </summary>
public class GamesArchitectureTests
{
    private const string EngineNamespace = "PandoraOverlay.Games.Engine";

    // ---- No app source names an Engine type (A2) ---------------------------------------

    /// <summary>The full name of every type the assembly at <paramref name="path"/> references from another assembly, read from its metadata (a nested type as "Outer+Inner").</summary>
    internal static IReadOnlyList<string> ReferencedTypes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        return md.TypeReferences.Select(h => FullName(md, h)).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string FullName(MetadataReader md, TypeReferenceHandle handle)
    {
        var reference = md.GetTypeReference(handle);
        var name = md.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return FullName(md, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name;
        var ns = md.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>In the Engine's namespace or one below it.</summary>
    internal static bool IsEngineType(string fullName) => fullName.StartsWith(EngineNamespace + ".", StringComparison.Ordinal);

    /// <summary>
    /// The app's assembly references no type of the Engine (A2): the Engine's
    /// DLL reaches the app through a game, and the app talks to every game
    /// through the Contract alone, so what the Engine offers a game (its
    /// surface, its loop, its saves' shape) is never the app's to reach.
    /// The app's TESTS may, and do: only its source is barred.
    /// </summary>
    [Fact]
    public void TheAppNamesNoEngineType()
    {
        var engine = ReferencedTypes(typeof(App).Assembly.Location).Where(IsEngineType).ToList();

        Assert.True(engine.Count == 0, "The app talks to a game through the Contract alone (A2), yet its assembly names:\n" + string.Join("\n", engine));
    }

    // The scan's proof: this test assembly names an Engine type (the tests may), so a
    // scan of it must find one. typeof writes a type reference; a constant would not.
    private static readonly Type PlantedEngineType = typeof(PandoraOverlay.Games.Engine.EngineTime);

    [Fact]
    public void TheMetadataScanSeesAnEngineTypeWhereThereIsOne()
    {
        var types = ReferencedTypes(typeof(GamesArchitectureTests).Assembly.Location);

        Assert.Contains(PlantedEngineType.FullName!, types);
        Assert.Contains(types, IsEngineType);
        Assert.False(IsEngineType("PandoraOverlay.Games.GameFrame")); // the Contract's namespace is not the Engine's
        Assert.False(IsEngineType("PandoraOverlay.Games.EngineRoom.X"));
    }

    // ---- Only the catalog names a game (A2) -----------------------------------------------

    private const string Catalog = "GamesHost/GameCatalog.cs";

    // A game's namespace or the Engine's: "PandoraOverlay.Games." and a letter. The
    // Contract's own namespace, PandoraOverlay.Games, is the app's to use anywhere; a
    // string that ends at the dot (the crash net's "an assembly named
    // PandoraOverlay.Games.*") names no namespace.
    private static readonly Regex GameNamespace = new(@"PandoraOverlay\.Games\.[A-Za-z]", RegexOptions.Compiled);

    // A global using hands a namespace to every file of the app: even the catalog may not.
    private static readonly Regex GlobalUsingOfAGame = new(@"\bglobal\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?PandoraOverlay\.Games\.[A-Za-z]", RegexOptions.Compiled);

    // The root folders that hold no app source: the games' projects, the tests, the
    // tools, the build's, git's, Visual Studio's, the release outputs, and .claude (the
    // rules, and the app's worktrees: whole checkouts of other branches).
    private static readonly HashSet<string> NotTheApp = new(StringComparer.OrdinalIgnoreCase)
    {
        "Games", "PandoraOverlay.Tests", "tools", "bin", "obj", ".claude", ".git", ".vs", "publish", "Releases",
    };

    /// <summary>Whether a file, by its path from the repo root with '/', is the app's own source: a .cs or .xaml outside the folders above and outside any bin or obj.</summary>
    internal static bool IsAppSource(string relative)
    {
        var segments = relative.Split('/');
        if (segments.Length > 1 && NotTheApp.Contains(segments[0])) return false;
        if (segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase) || s.Equals("obj", StringComparison.OrdinalIgnoreCase))) return false;
        return relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where a file names a game's or the Engine's namespace and may not: anywhere but the catalog, and there too by a global using.</summary>
    internal static IReadOnlyList<string> GameNamesIn(string relative, string text)
    {
        var found = GlobalUsingOfAGame.Matches(text).Select(m => "a global using: " + m.Value).ToList();
        if (relative != Catalog)
            found.AddRange(GameNamespace.Matches(text).Select(m => text.Substring(m.Index, Math.Min(48, text.Length - m.Index)).Split('\n')[0].Trim()));
        return found;
    }

    /// <summary>Every file under the repo root, the skipped root folders not walked at all (a main checkout's .claude holds whole worktrees).</summary>
    private static IEnumerable<string> RepoFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root)) yield return file;
        foreach (var folder in Directory.EnumerateDirectories(root).Where(d => !NotTheApp.Contains(Path.GetFileName(d))))
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                yield return file;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// The one app file that names a game is the catalog (A2): adding a game is a
    /// line there, and every other file sees games only as the Contract's IGame.
    /// A global using in the app's project file would hand a game's namespace to
    /// every file the same way, so it is read too.
    /// </summary>
    [Fact]
    public void OnlyTheCatalogNamesAGame()
    {
        var root = ThemeFileTests.RepoRoot();
        var sources = RepoFiles(root).Select(f => Relative(root, f)).Where(IsAppSource).ToList();
        Assert.Contains("App.xaml.cs", sources); // the walk reaches the app's source
        Assert.Contains("Shell/MainWindow.xaml.cs", sources);

        var problems = sources
            .SelectMany(f => GameNamesIn(f, File.ReadAllText(Path.Combine(root, f))).Select(p => $"{f}: {p}"))
            .ToList();
        var usings = XDocument.Load(Path.Combine(root, "PandoraOverlay.csproj")).Descendants()
            .Where(e => e.Name.LocalName == "Using")
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .Where(n => GameNamespace.IsMatch(n));
        problems.AddRange(usings.Select(n => $"PandoraOverlay.csproj: a global using of {n}"));

        Assert.True(problems.Count == 0,
            $"Only {Catalog} names a game's namespace, and the Engine's is named nowhere in the app (A2):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheCatalogScanFindsWhatItIsFor()
    {
        Assert.NotEmpty(GameNamesIn("Shell/MainWindow.Games.cs", "using PandoraOverlay.Games.Hatchling;"));
        Assert.NotEmpty(GameNamesIn("GamesHost/GamesHost.cs", "var loop = new PandoraOverlay.Games.Engine.GameLoop(clock, Step, Draw, Fault);"));
        Assert.NotEmpty(GameNamesIn(Catalog, "global using PandoraOverlay.Games.Hatchling;"));
        Assert.NotEmpty(GameNamesIn(Catalog, "global using static PandoraOverlay.Games.Hatchling.HatchlingText;"));
        Assert.Empty(GameNamesIn(Catalog, "using PandoraOverlay.Games.Hatchling;"));
        Assert.Empty(GameNamesIn("GamesHost/GamesHost.cs", "using PandoraOverlay.Games;"));
        Assert.Empty(GameNamesIn("GamesHost/GameFaults.cs", "frame.Assembly.StartsWith(\"PandoraOverlay.Games.\", StringComparison.Ordinal)"));

        Assert.True(IsAppSource("GamesHost/GamesWindow.xaml"));
        Assert.True(IsAppSource("App.xaml.cs"));
        Assert.False(IsAppSource("Games/PandoraOverlay.Games.Engine/Time/EngineTime.cs"));
        Assert.False(IsAppSource("PandoraOverlay.Tests/GamesHost/GamesArchitectureTests.cs"));
        Assert.False(IsAppSource("tools/area-map/x.cs"));
        Assert.False(IsAppSource(".claude/worktrees/games-A01/App.xaml.cs"));
        Assert.False(IsAppSource("obj/Debug/net8.0-windows/App.g.cs"));
        Assert.False(IsAppSource("GamesHost/obj/x.cs"));
        Assert.False(IsAppSource("Assets/rules.json"));
    }

    // ---- The host makes no request (constraints 1 to 4) --------------------------------------

    private const string MainWindowGames = "Shell/MainWindow.Games.cs";

    /// <summary>
    /// What would make a request, a trigger of one, or a reach for the cookie or
    /// a process: the poll's service and its field, the website client, the
    /// access header, the cookie's two readers, a process, a nudge. The games add
    /// no request and no request trigger, and opening Games nudges no poll (A5,
    /// A13): their readings ride on the polls the overlay already makes.
    /// </summary>
    internal static readonly string[] RequestWords =
    {
        "PollService", "PandoraClient", "_poll", "HttpClient", "OverlayAccess", "GetCookie", "CurrentCookie", "Process.", "Nudge(",
    };

    /// <summary>The games host and MainWindow's games part: every file under GamesHost/, and Shell/MainWindow.Games.cs.</summary>
    internal static bool IsGamesHostFile(string relative) =>
        (relative.StartsWith("GamesHost/", StringComparison.Ordinal) || relative == MainWindowGames)
        && !relative.Split('/').Any(s => s is "bin" or "obj");

    internal static IReadOnlyList<string> RequestWordsIn(string text) =>
        RequestWords.Where(w => text.Contains(w, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Nothing in GamesHost/ or MainWindow.Games.cs names a request, the
    /// cookie or a process (CLAUDE.md constraints 1 to 4; A5, A13). The files
    /// come with the host's tasks: until then the test reads whatever is there,
    /// and its self-test is what proves it.
    /// </summary>
    [Fact]
    public void TheGamesHostNamesNoRequestCookieOrProcess()
    {
        var root = ThemeFileTests.RepoRoot();
        var problems = RepoFiles(root)
            .Select(f => Relative(root, f))
            .Where(IsGamesHostFile)
            .SelectMany(f => RequestWordsIn(File.ReadAllText(Path.Combine(root, f))).Select(w => $"{f}: {w}"))
            .ToList();

        Assert.True(problems.Count == 0,
            "The games add no request, no request trigger and no reach for the cookie or a process (constraints 1 to 4): the host is fed by MainWindow's OnSnapshot, never asks:\n"
            + string.Join("\n", problems));
    }

    // ---- The games' sound never sets the app's volume (A80, A86) -------------------------------

    /// <summary>
    /// winmm's call that sets a wave-out handle's volume: measured, it sets the
    /// whole app's session volume in Windows' own mixer (every handle's at once),
    /// which is the player's level for the overlay, never the games' to move. The
    /// games' level is their own mix's gain (GamesSound's steps).
    /// </summary>
    internal static readonly string[] AppVolumeWords = { "waveOutSetVolume" };

    internal static IReadOnlyList<string> AppVolumeWordsIn(string text) =>
        AppVolumeWords.Where(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Nothing in GamesHost/ or MainWindow.Games.cs names the call that sets the
    /// overlay's own volume in Windows' mixer (A80): the speaker's files come with
    /// a later task and are read as every file there is, comments included.
    /// </summary>
    [Fact]
    public void TheGamesHostNeverSetsTheAppsVolume()
    {
        var root = ThemeFileTests.RepoRoot();
        var files = RepoFiles(root).Select(f => Relative(root, f)).Where(IsGamesHostFile).ToList();
        Assert.Contains("GamesHost/GamesSound.cs", files); // the walk reaches the sound's files
        Assert.Contains("GamesHost/ISpeaker.cs", files);

        var problems = files
            .SelectMany(f => AppVolumeWordsIn(File.ReadAllText(Path.Combine(root, f))).Select(w => $"{f}: {w}"))
            .ToList();

        Assert.True(problems.Count == 0,
            "The games' level is their own mix's gain; the overlay's volume in Windows' mixer is the player's (A80):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheAppVolumeScanFindsWhatItIsFor()
    {
        Assert.Equal(new[] { "waveOutSetVolume" }, AppVolumeWordsIn("_ = waveOutSetVolume(handle, 0x80008000);"));
        Assert.Equal(new[] { "waveOutSetVolume" }, AppVolumeWordsIn("[DllImport(\"winmm.dll\")] static extern int WaveOutSetVolume(IntPtr h, uint v);"));
        Assert.Empty(AppVolumeWordsIn("_speaker.Play(samples, GamesSound.GainOf(step)); // the games' own gain"));
        Assert.Empty(AppVolumeWordsIn("[DllImport(\"winmm.dll\")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, uint size);"));
    }

    [Fact]
    public void TheRequestScanFindsWhatItIsFor()
    {
        Assert.Equal(new[] { "_poll", "Nudge(" }, RequestWordsIn("_poll.Nudge(\"games\");"));
        Assert.Equal(new[] { "PandoraClient" }, RequestWordsIn("public void Feed(PandoraClient client)"));
        Assert.Equal(new[] { "HttpClient" }, RequestWordsIn("using var http = new HttpClient();"));
        Assert.Equal(new[] { "Process." }, RequestWordsIn("System.Diagnostics.Process.Start(folder);"));
        Assert.Equal(new[] { "GetCookie" }, RequestWordsIn("var c = _config.GetCookie();"));
        Assert.Empty(RequestWordsIn("public void Feed(MyLocationResponse snapshot) => _games.Feed(snapshot);"));
        Assert.Empty(RequestWordsIn("// a click on the game: the process is never asked"));

        Assert.True(IsGamesHostFile("GamesHost/GamesHost.cs"));
        Assert.True(IsGamesHostFile("GamesHost/GamesWindow.xaml"));
        Assert.True(IsGamesHostFile(MainWindowGames));
        Assert.False(IsGamesHostFile("Shell/MainWindow.xaml.cs"));
        Assert.False(IsGamesHostFile("Core/PollService.cs"));
    }
}
