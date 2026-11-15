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

    /// <summary>MainWindow's sound part (A101): the app's one speaker, made and closed, and the gates made over its channels.</summary>
    private const string MainWindowSound = "Shell/MainWindow.Sound.cs";

    /// <summary>The app's sound (A101): the speaker the games host plays through, moved out of GamesHost/, and the click layer beside it.</summary>
    private const string SoundFolder = "Sound/";

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

    /// <summary>
    /// The files the four scans read (request, volume, native calls, reach): the games
    /// host and MainWindow's games part, every file under GamesHost/ and
    /// Shell/MainWindow.Games.cs; and since the speaker became the app's (A101, A102),
    /// every file under Sound/ (the speaker, its device, its mix, the click layer) and
    /// MainWindow's sound part, Shell/MainWindow.Sound.cs. More files under the same
    /// words, never fewer. The name stays the plan's: the set is still "what the games
    /// host is and plays through".
    /// </summary>
    internal static bool IsGamesHostFile(string relative) =>
        (relative.StartsWith("GamesHost/", StringComparison.Ordinal) || relative == MainWindowGames
         || relative.StartsWith(SoundFolder, StringComparison.Ordinal) || relative == MainWindowSound)
        && !relative.Split('/').Any(s => s is "bin" or "obj");

    internal static IReadOnlyList<string> RequestWordsIn(string text) =>
        RequestWords.Where(w => text.Contains(w, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Nothing in GamesHost/ or MainWindow.Games.cs, nor in Sound/ or
    /// MainWindow.Sound.cs (A102), names a request, the
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
    /// games' level is their own mix's gain (GamesSound's steps). With it (A93, after
    /// the constraint review r4's N20), every other way to that level or the device's:
    /// winmm's other setters and its mixer's, and the Core Audio interfaces and their
    /// setters, which a move to WASAPI (PLAN-SOUND 2.1's runner-up) would reach for
    /// first (ISimpleAudioVolume is the session's own volume). No word here is part of
    /// another, so each finds only itself.
    /// </summary>
    internal static readonly string[] AppVolumeWords =
    {
        "waveOutSetVolume", "midiOutSetVolume", "auxSetVolume", "mixerSetControlDetails",
        "ISimpleAudioVolume", "IAudioEndpointVolume", "IChannelAudioVolume", "IAudioStreamVolume", "SetMasterVolume", "SetChannelVolume",
    };

    internal static IReadOnlyList<string> AppVolumeWordsIn(string text) =>
        AppVolumeWords.Where(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Nothing in GamesHost/ or MainWindow.Games.cs, nor in Sound/ or
    /// MainWindow.Sound.cs (A102), names the call that sets the
    /// overlay's own volume in Windows' mixer (A80): the speaker's files come with
    /// a later task and are read as every file there is, comments included.
    /// </summary>
    [Fact]
    public void TheGamesHostNeverSetsTheAppsVolume()
    {
        var root = ThemeFileTests.RepoRoot();
        var files = RepoFiles(root).Select(f => Relative(root, f)).Where(IsGamesHostFile).ToList();
        Assert.Contains("GamesHost/GamesSound.cs", files); // the walk reaches the sound's files
        Assert.Contains("Sound/ISpeaker.cs", files); // moved from GamesHost/ (A101)
        Assert.Contains("Sound/WaveOut.cs", files); // the speaker's device, where its native calls are (A102)

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

        // A93: the other setters, each found as itself alone.
        Assert.Equal(new[] { "midiOutSetVolume" }, AppVolumeWordsIn("_ = midiOutSetVolume(handle, 0);"));
        Assert.Equal(new[] { "auxSetVolume" }, AppVolumeWordsIn("_ = auxSetVolume(0, 0x80008000);"));
        Assert.Equal(new[] { "mixerSetControlDetails" }, AppVolumeWordsIn("_ = mixerSetControlDetails(mixer, ref details, 0);"));
        Assert.Equal(new[] { "ISimpleAudioVolume" }, AppVolumeWordsIn("[ComImport] interface ISimpleAudioVolume { }"));
        Assert.Equal(new[] { "IAudioEndpointVolume" }, AppVolumeWordsIn("IAudioEndpointVolume endpoint = Activate();"));
        Assert.Equal(new[] { "IChannelAudioVolume" }, AppVolumeWordsIn("IChannelAudioVolume channels = Session();"));
        Assert.Equal(new[] { "IAudioStreamVolume" }, AppVolumeWordsIn("IAudioStreamVolume stream = Client();"));
        Assert.Equal(new[] { "SetMasterVolume" }, AppVolumeWordsIn("session.SetMasterVolume(0.5f, Guid.Empty);"));
        Assert.Equal(new[] { "SetMasterVolume" }, AppVolumeWordsIn("endpoint.SetMasterVolumeLevelScalar(0.2f, Guid.Empty);"));
        Assert.Equal(new[] { "SetChannelVolume" }, AppVolumeWordsIn("channels.SetChannelVolume(0, 0.5f, Guid.Empty);"));
        Assert.Empty(AppVolumeWordsIn("public int Step => StepOf(_config.GamesSoundVolume);"));

        // A102: the app's speaker in Sound/ is read too, so a setter planted there is found.
        Assert.True(IsGamesHostFile("Sound/WaveOut.cs"));
        Assert.Equal(new[] { "waveOutSetVolume" }, AppVolumeWordsIn("[DllImport(\"winmm.dll\")]\n    public static extern int waveOutSetVolume(IntPtr handle, uint volume);"));
    }

    // ---- The host's native calls: its own window, its own event, the default device (A92) ----

    /// <summary>
    /// Every native entry point the games host declares (constraint 1; A80, A86; A92,
    /// after the constraint review r4's N19), as (library in lower case, entry point):
    /// the Games window's own style and corners (GamesWindow, as BigMapWindow's), and
    /// the speaker's wave-out stream and the event it sleeps on (Sound/WaveOut.cs since
    /// the speaker became the app's, A80, A101, A102: ten of the thirteen). None
    /// finds, lists or opens another window, process or device. A new one is the
    /// architecture's call first, then a line here.
    /// </summary>
    internal static readonly (string Library, string EntryPoint)[] HostNativeCalls =
    {
        ("user32.dll", "GetWindowLongPtrW"), ("user32.dll", "SetWindowLongPtrW"), ("dwmapi.dll", "DwmSetWindowAttribute"),
        ("winmm.dll", "waveOutOpen"), ("winmm.dll", "waveOutPrepareHeader"), ("winmm.dll", "waveOutUnprepareHeader"),
        ("winmm.dll", "waveOutWrite"), ("winmm.dll", "waveOutReset"), ("winmm.dll", "waveOutClose"), ("winmm.dll", "waveOutGetPosition"),
        ("kernel32.dll", "CreateEventW"), ("kernel32.dll", "WaitForSingleObject"), ("kernel32.dll", "CloseHandle"),
    };

    // A native declaration as the host writes one: [DllImport("lib.dll")] or
    // [LibraryImport("lib.dll")], the library as a string (a constant would hide what it
    // names), an EntryPoint as a string if it has one, other attributes after it, then
    // the method's modifiers, its return type and its name.
    private static readonly Regex NativeDeclaration = new(
        @"\[\s*(?:System\.Runtime\.InteropServices\.)?(?:DllImport|LibraryImport)(?:Attribute)?\s*\(\s*""(?<library>[^""]+)""(?<rest>[^\]]*)\)\s*\](?:\s*\[[^\]]*\])*\s*(?:(?:public|private|internal|protected|static|extern|partial|unsafe|new)\s+)+[\w.<>\[\]?*]+\s+(?<name>\w+)\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex EntryPointArgument = new(@"\bEntryPoint\s*=\s*""(?<entry>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex NativeWord = new(@"\b(?:DllImport|LibraryImport)", RegexOptions.Compiled);

    /// <summary>
    /// Every native declaration in a source text, as (library in lower case, entry
    /// point: the EntryPoint if one is written, else the method's name); and what the
    /// scan can't read, each a problem, never a pass: an EntryPoint not written as a
    /// string, and any mention of DllImport or LibraryImport (a comment's included)
    /// that is not a declaration it reads.
    /// </summary>
    internal static (IReadOnlyList<(string Library, string EntryPoint)> Declared, IReadOnlyList<string> Unread) NativeCallsIn(string text)
    {
        var declared = new List<(string Library, string EntryPoint)>();
        var unread = new List<string>();
        var matches = NativeDeclaration.Matches(text);
        foreach (Match m in matches)
        {
            var rest = m.Groups["rest"].Value;
            var entry = EntryPointArgument.Match(rest);
            if (!entry.Success && rest.Contains("EntryPoint", StringComparison.Ordinal))
                unread.Add($"an EntryPoint not written as a string: {m.Value.Split('\n')[0].Trim()}");
            else
                declared.Add((m.Groups["library"].Value.ToLowerInvariant(), entry.Success ? entry.Groups["entry"].Value : m.Groups["name"].Value));
        }
        var mentions = NativeWord.Matches(text).Count - matches.Count;
        if (mentions > 0)
            unread.Add($"{mentions} mention(s) of DllImport or LibraryImport the scan can't read: write each as [DllImport(\"lib.dll\")] (an EntryPoint as a string) on the method it imports, and never name one in a comment");
        return (declared, unread);
    }

    // A call of waveOutOpen and the device it names, its second argument as written
    // (the declaration, "extern int waveOutOpen(", is not a call).
    private static readonly Regex WaveOutOpenCall = new(@"(?<!\bextern\s+int\s+)\bwaveOutOpen\s*\(\s*out\s+[^,()]+,\s*(?<device>[^,()]+?)\s*,", RegexOptions.Compiled);
    private static readonly Regex WaveOutOpenDeclaration = new(@"\bextern\s+int\s+waveOutOpen\s*\(", RegexOptions.Compiled);
    private static readonly Regex WaveOutOpenWord = new(@"\bwaveOutOpen\b", RegexOptions.Compiled);

    /// <summary>The device each call of waveOutOpen in a source text opens, as written; and how many mentions of waveOutOpen are neither its declaration nor a call the scan reads.</summary>
    internal static (IReadOnlyList<string> Devices, int Unread) DevicesOpenedIn(string text)
    {
        var devices = WaveOutOpenCall.Matches(text).Select(m => m.Groups["device"].Value).ToList();
        var unread = WaveOutOpenWord.Matches(text).Count - WaveOutOpenDeclaration.Matches(text).Count - devices.Count;
        return (devices, unread);
    }

    /// <summary>
    /// The host's other ways to native code, or to another process or window, none of
    /// which it uses (constraint 1; A92): a COM class declared in code, a library loaded
    /// by hand, a function pointer made or called, a COM type found by its id, a COM
    /// object made by its moniker's name (A96: WMI's "winmgmts:" lists every process);
    /// and in managed code the Process family (Process, ProcessStartInfo ...), UI
    /// Automation's walk of the desktop's windows, the performance counters' list of
    /// processes (the forbidden list refuses the same three to a game, A74), and
    /// WinForms' SendKeys, keystrokes to whatever window has the focus, the game's
    /// when ours lost it (A96). Case counts: "the process" in a comment is prose.
    /// </summary>
    private static readonly Regex HostReach = new(
        @"\bComImport\b|\bNativeLibrary\b|\bGetDelegateForFunctionPointer\b|\bGetTypeFromCLSID\b|\bGetTypeFromProgID\b|\bBindToMoniker\b|\bdelegate\s*\*\s*unmanaged|\bProcess\w*|\bAutomationElement\b|\bPerformanceCounter\w*|\bSendKeys\b",
        RegexOptions.Compiled);

    internal static IReadOnlyList<string> HostReachIn(string text) => HostReach.Matches(text).Select(m => m.Value).ToList();

    /// <summary>
    /// The games host makes only its own native calls (constraint 1; A80, A86, A92):
    /// every DllImport or LibraryImport under GamesHost/ and in MainWindow.Games.cs, and
    /// under Sound/ and in MainWindow.Sound.cs (A102), is
    /// one of the thirteen above, and each of the thirteen is there; the one device it
    /// opens is Windows' default output (Winmm.WaveMapper), never one picked from a
    /// list; and it names no other way to native code, a process or another window.
    /// Read as every file there is, comments included.
    /// </summary>
    [Fact]
    public void TheGamesHostMakesOnlyItsOwnNativeCalls()
    {
        var root = ThemeFileTests.RepoRoot();
        var files = RepoFiles(root).Select(f => Relative(root, f)).Where(IsGamesHostFile).ToList();
        Assert.Contains("Sound/WaveOut.cs", files); // the walk reaches the native calls' files (moved from GamesHost/, A101)
        Assert.Contains("GamesHost/GamesWindow.xaml.cs", files);

        var problems = new List<string>();
        var declared = new List<(string Library, string EntryPoint)>();
        var devices = 0;
        foreach (var f in files)
        {
            var text = File.ReadAllText(Path.Combine(root, f));
            var (calls, unread) = NativeCallsIn(text);
            declared.AddRange(calls);
            problems.AddRange(calls.Where(c => !HostNativeCalls.Contains(c)).Select(c => $"{f}: {c.EntryPoint} in {c.Library}, not one of the host's own"));
            problems.AddRange(unread.Select(u => $"{f}: {u}"));
            var (opened, unreadOpens) = DevicesOpenedIn(text);
            devices += opened.Count;
            problems.AddRange(opened.Where(d => d != "Winmm.WaveMapper").Select(d => $"{f}: waveOutOpen on the device {d}, not Windows' default output (Winmm.WaveMapper)"));
            if (unreadOpens > 0) problems.Add($"{f}: {unreadOpens} mention(s) of waveOutOpen that are neither its declaration nor a call the scan reads");
            problems.AddRange(HostReachIn(text).Select(w => $"{f}: {w}"));
        }
        problems.AddRange(HostNativeCalls.Except(declared).Select(c => $"{c.EntryPoint} in {c.Library} is no longer found: the scan stopped reading it, or it was removed (then take it off the list)"));
        if (devices == 0) problems.Add("no call of waveOutOpen was found: the scan stopped reading it");

        Assert.Equal(0xFFFFFFFFu, Winmm.WaveMapper); // WAVE_MAPPER: Windows' default output
        Assert.True(problems.Count == 0,
            "The games host calls native code only for its own window, its own event and Windows' default audio output, and never reaches another process or window (constraint 1; A80, A92):\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void TheNativeCallScanFindsWhatItIsFor()
    {
        // Read as the host writes them: the library in lower case, an EntryPoint over the method's name.
        Assert.Equal(new[] { ("user32.dll", "GetWindowLongPtrW") }, NativeCallsIn("[DllImport(\"user32.dll\", EntryPoint = \"GetWindowLongPtrW\")]\n    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);").Declared);
        Assert.Equal(new[] { ("kernel32.dll", "CreateEventW") }, NativeCallsIn("[DllImport(\"kernel32.dll\", SetLastError = true)]\n    public static extern IntPtr CreateEventW(IntPtr a, bool m, bool i, IntPtr n);").Declared);
        // What it is for: a new one, renamed or not, in any of the forms.
        Assert.Equal(new[] { ("user32.dll", "FindWindowW") }, NativeCallsIn("[DllImport(\"User32.dll\", EntryPoint = \"FindWindowW\", CharSet = CharSet.Unicode)]\n[return: MarshalAs(UnmanagedType.SysInt)]\nprivate static extern IntPtr Harmless(string c, string w);").Declared);
        Assert.Equal(new[] { ("kernel32.dll", "OpenProcess") }, NativeCallsIn("[LibraryImport(\"kernel32.dll\")]\ninternal static partial IntPtr OpenProcess(uint access, int inherit, uint id);").Declared);
        Assert.Equal(new[] { ("winmm.dll", "waveOutGetNumDevs") }, NativeCallsIn("[System.Runtime.InteropServices.DllImportAttribute(\"winmm.dll\")] static extern uint waveOutGetNumDevs();").Declared);
        // What it can't read is a problem, never a pass.
        Assert.NotEmpty(NativeCallsIn("[DllImport(User32)] static extern IntPtr GetForegroundWindow();").Unread); // the library by a constant
        Assert.NotEmpty(NativeCallsIn("[DllImport(\"user32.dll\", EntryPoint = nameof(X))] static extern IntPtr X();").Unread);
        Assert.NotEmpty(NativeCallsIn("[DllImport(\"user32.dll\")][DllImport(\"kernel32.dll\")] static extern IntPtr X();").Unread);
        Assert.NotEmpty(NativeCallsIn("// a DllImport of user32 would go here").Unread);
        var plain = NativeCallsIn("var size = Marshal.SizeOf<Winmm.WaveHeader>(); // unmanaged memory");
        Assert.Empty(plain.Declared);
        Assert.Empty(plain.Unread);

        // The device: Windows' default output alone; the declaration is not a call, a method group is unread.
        Assert.Equal(new[] { "Winmm.WaveMapper" }, DevicesOpenedIn("var code = Winmm.waveOutOpen(out _handle, Winmm.WaveMapper, ref format, _event, IntPtr.Zero, Winmm.CallbackEvent);").Devices);
        Assert.Equal(new[] { "2" }, DevicesOpenedIn("_ = waveOutOpen(out h, 2, ref f, e, IntPtr.Zero, 0x50000);").Devices);
        var declaration = DevicesOpenedIn("public static extern int waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);");
        Assert.Empty(declaration.Devices);
        Assert.Equal(0, declaration.Unread);
        Assert.Equal(1, DevicesOpenedIn("Open open = Winmm.waveOutOpen;").Unread);

        // The other doors, and the prose and the host's own calls that are none.
        Assert.Equal(new[] { "ComImport" }, HostReachIn("[ComImport] interface IThing { }"));
        Assert.Equal(new[] { "NativeLibrary" }, HostReachIn("var lib = NativeLibrary.Load(name);"));
        Assert.Equal(new[] { "GetDelegateForFunctionPointer" }, HostReachIn("var f = Marshal.GetDelegateForFunctionPointer<Fn>(pointer);"));
        Assert.Equal(new[] { "delegate* unmanaged" }, HostReachIn("delegate* unmanaged<int, int> f = &X;"));
        Assert.Equal(new[] { "GetTypeFromCLSID" }, HostReachIn("var t = Type.GetTypeFromCLSID(id);"));
        Assert.Equal(new[] { "GetTypeFromProgID" }, HostReachIn("var t = Type.GetTypeFromProgID(name);"));
        Assert.Equal(new[] { "BindToMoniker" }, HostReachIn("var wmi = Marshal.BindToMoniker(name);"));
        Assert.Equal(new[] { "SendKeys" }, HostReachIn("System.Windows.Forms.SendKeys.SendWait(keys);"));
        Assert.Equal(new[] { "Process" }, HostReachIn("var p = new Process { StartInfo = info };"));
        Assert.Equal(new[] { "ProcessStartInfo" }, HostReachIn("var info = new ProcessStartInfo(name);"));
        Assert.Equal(new[] { "Process" }, HostReachIn("foreach (var p in Process.GetProcesses()) { }"));
        Assert.Equal(new[] { "AutomationElement" }, HostReachIn("var all = AutomationElement.RootElement;"));
        Assert.Equal(new[] { "PerformanceCounterCategory" }, HostReachIn("var c = new PerformanceCounterCategory(name);"));
        Assert.Empty(HostReachIn("// a click on the game: the process is never asked"));
        Assert.Empty(HostReachIn("var memory = Marshal.AllocHGlobal(bytes); // unmanaged memory"));
        Assert.Empty(HostReachIn("if (_keys.Down(KeyOf(e), Keyboard.Modifiers, isRepeat)) e.Handled = true;"));

        // A102: Sound/ is read too, so a native call and a reach planted there are found.
        Assert.True(IsGamesHostFile("Sound/WaveOut.cs"));
        Assert.Equal(new[] { ("winmm.dll", "waveOutGetDevCapsW") }, NativeCallsIn("[DllImport(\"winmm.dll\", EntryPoint = \"waveOutGetDevCapsW\")]\n    public static extern int DeviceCaps(uint device, IntPtr caps, uint size);").Declared);
        Assert.Equal(new[] { "Process" }, HostReachIn("var game = Process.GetProcessesByName(name);"));
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

        // A102: the app's sound and MainWindow's sound part are read too, so a request
        // planted there is found.
        Assert.True(IsGamesHostFile("Sound/WaveOutSpeaker.cs"));
        Assert.True(IsGamesHostFile(MainWindowSound));
        Assert.Equal(new[] { "_poll", "Nudge(" }, RequestWordsIn("private void OnClick() => _poll.Nudge(\"click\");"));
    }
}
