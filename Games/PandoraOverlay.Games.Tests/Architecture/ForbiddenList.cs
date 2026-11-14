namespace PandoraOverlay.Games.Tests;

/// <summary>What a <see cref="ForbiddenEntry"/> is matched against in a <see cref="ScannedAssembly"/>.</summary>
internal enum ForbiddenKind
{
    /// <summary>A referenced assembly, by its simple name; a pattern ending in '*' is a prefix.</summary>
    Assembly,

    /// <summary>A referenced type in the namespace or any namespace below it ("System.Net" holds System.Net.Http, never "System.Network").</summary>
    Namespace,

    /// <summary>A referenced type, by its full name, and any type nested in it; a pattern ending in '*' is a prefix.</summary>
    Type,

    /// <summary>A member referenced on one type, by name; '*' alone is every member, a trailing '*' a prefix.</summary>
    Member,

    /// <summary>Any native call.</summary>
    PInvoke,
}

/// <summary>
/// One line of the forbidden list: what it matches and the reason a failure
/// prints. Data, not code: a test that fails names the entry and its reason,
/// and the list is changed only by the architecture planner, never to make a
/// test pass (A29).
/// </summary>
/// <param name="Kind">What it is matched against.</param>
/// <param name="Pattern">The assembly, namespace or type it names (for a member, the type the member is on).</param>
/// <param name="Reason">Printed when it fails: why a game may not.</param>
internal sealed record ForbiddenEntry(ForbiddenKind Kind, string Pattern, string Reason)
{
    /// <summary>A member entry's member name: "get_UtcNow", "Load*", or "*" for every member.</summary>
    public string? Member { get; init; }

    /// <summary>A member entry's names that stay allowed (Application's LoadComponent).</summary>
    public IReadOnlyList<string> Except { get; init; } = Array.Empty<string>();

    /// <summary>The one assembly the entry does not hold (the engine's own timer, A19).</summary>
    public string? AllowedIn { get; init; }

    /// <summary>The entry as a failure names it.</summary>
    public string Describe()
    {
        var what = Kind switch
        {
            ForbiddenKind.Assembly => "assembly " + Pattern,
            ForbiddenKind.Namespace => "namespace " + Pattern + " and below",
            ForbiddenKind.Type => "type " + Pattern,
            ForbiddenKind.Member => "member " + Pattern + "::" + Member + (Except.Count > 0 ? " but " + string.Join(", ", Except) : ""),
            _ => "any native call",
        };
        return AllowedIn is null ? what : what + " outside " + AllowedIn;
    }
}

/// <summary>One forbidden thing an assembly reaches for, and the entry it breaks.</summary>
/// <param name="Assembly">The scanned assembly.</param>
/// <param name="Subject">What it reaches for, as the metadata names it.</param>
/// <param name="Entry">The line of the list it breaks.</param>
internal readonly record struct Finding(string Assembly, string Subject, ForbiddenEntry Entry)
{
    public override string ToString() => $"{Assembly} reaches {Subject} [{Entry.Describe()}]: {Entry.Reason}";
}

/// <summary>
/// The forbidden list (A29, A33): what no game's assembly may name, held on the
/// built Contract, Engine and every game by <see cref="ForbiddenApiTests"/>.
/// <para>
/// Why it exists: the compiler keeps a game away from the app (nothing under
/// Games/ references it, A2), so a game can't reach the website client, the
/// cookie or the config. Plain .NET is one <c>using</c> away, though, and these
/// are the promises it would break: no request and nothing touching the PC or
/// the game (the promises the games made in public, A5, A29); a game drawn inside its own view,
/// never a second surface over The Isle (G17, A33); mouse only, since Esc and J
/// are the window's (G12, A33); and nothing running, rolling or reading the
/// clock but through the engine's loop, its seeded generator and the host's
/// clock, so the window's pause holds and the rules replay (A19, A20).
/// </para>
/// <para>
/// It guards against a convenient API reached for by accident. It is not a
/// sandbox and does not claim to be: the games are compiled in and reviewed.
/// Fine on purpose: <c>System.IO.Stream</c>, <c>MemoryStream</c>, the readers
/// and writers (the engine's atlas reads the streams of the game's own embedded
/// art), and WPF's own drawing, layout and mouse input.
/// </para>
/// </summary>
internal static class ForbiddenList
{
    // The reasons, by A29's groups (reaching out, leaving the frame, the keyboard,
    // losing determinism or the pause), each printed with the entry.
    private const string TheApp =
        "a game never sees the app, the website or the session (A2): it talks to the overlay through the Contract alone";
    private const string ReachingOut =
        "reaching out: a game makes no request and touches no process, native code, setting or file of the PC (A29); only the host's storage";
    private const string StorageIsTheHosts =
        "storage is the host's (IGameStorage, A7), and art is embedded in the game's own assembly (the engine's atlas reads its streams)";
    private const string LeavingTheFrame =
        "leaving the frame: a game draws inside its own view, with no window, popup, tooltip, effect, WPF animation or sound of its own (A17, A33)";
    private const string TheEnginesClock =
        "the clock and the dice are the engine's: durations come from IGameClock and the loop's ticks, rolls from the seeded Rng, so the pause holds and a run replays (A19, A20)";
    private const string LosingThePause =
        "losing the pause: nothing of a game runs but on the engine's loop, which the window pauses and a closed view ends (A19)";
    private const string TheWindowsKeys =
        "Esc and J are the window's: a game takes no keyboard input, it is mouse only (G12, A33)";
    private const string TheHostsTime =
        "time comes from IGameClock (A29): the calendar through UtcNow, every duration through Elapsed";
    private const string NoGuid =
        "the dice are the engine's: a Guid.NewGuid is a roll no seed replays (A20)";
    private const string NothingLoaded =
        "nothing is loaded at run time: the games are compiled in (A1, A2), never read from a folder";
    private const string NoApplication =
        "a game never reaches the application object (A2, A25): the theme reaches its view by resource key, and LoadComponent alone is allowed, the one call a view's generated code makes";
    private const string NoNativeCall = "never a native call (A29)";
    private const string GamesTickThroughTheLoop =
        "games tick through GameLoop (A19): the engine's loop is the one timer, and it exists only while a view has something to move";

    private const string EngineAssembly = "PandoraOverlay.Games.Engine";

    private static ForbiddenEntry Assembly(string name) => new(ForbiddenKind.Assembly, name, TheApp);
    private static ForbiddenEntry Namespace(string name, string reason) => new(ForbiddenKind.Namespace, name, reason);
    private static ForbiddenEntry Type(string name, string reason) => new(ForbiddenKind.Type, name, reason);
    private static ForbiddenEntry Member(string type, string member, string reason, params string[] except) =>
        new(ForbiddenKind.Member, type, reason) { Member = member, Except = except };

    /// <summary>Every entry, in the brief's order (A02): assemblies, namespaces, types, members, native calls, the engine's one exception.</summary>
    public static IReadOnlyList<ForbiddenEntry> All { get; } = new[]
    {
        // ---- Assembly references ------------------------------------------------
        Assembly("PandoraOverlay"),
        Assembly("System.Windows.Forms"),
        Assembly("Velopack"),
        Assembly("Microsoft.Web.WebView2*"),
        Assembly("System.Security.Cryptography.ProtectedData"),
        Assembly("System.Net*"),

        // ---- Namespaces, and every namespace below them ----------------------------
        Namespace("System.Net", ReachingOut),
        Namespace("System.IO.Pipes", ReachingOut),
        Namespace("System.IO.IsolatedStorage", ReachingOut),
        Namespace("System.IO.Compression", ReachingOut),
        Namespace("Microsoft.Win32", ReachingOut),
        Namespace("System.Windows.Forms", ReachingOut),
        Namespace("System.Windows.Interop", ReachingOut),
        Namespace("System.Reflection.Emit", ReachingOut),
        Namespace("System.Windows.Media.Effects", LeavingTheFrame),
        Namespace("System.Windows.Media.Animation", LeavingTheFrame),
        Namespace("System.Media", LeavingTheFrame),
        Namespace("System.Timers", LosingThePause),
        Namespace("System.Threading.Tasks", LosingThePause),

        // ---- Types ----------------------------------------------------------------
        Type("System.IO.File", StorageIsTheHosts),
        Type("System.IO.Directory", StorageIsTheHosts),
        Type("System.IO.FileInfo", StorageIsTheHosts),
        Type("System.IO.DirectoryInfo", StorageIsTheHosts),
        Type("System.IO.FileStream", StorageIsTheHosts),
        Type("System.IO.Path", StorageIsTheHosts),
        Type("System.IO.DriveInfo", StorageIsTheHosts),
        Type("System.IO.FileSystemWatcher", StorageIsTheHosts),
        // Process* as A29's list has it: Process and ProcessStartInfo, and
        // the rest of the family (ProcessModule, ProcessThread ...) that only a
        // Process hands out.
        Type("System.Diagnostics.Process*", ReachingOut),
        Type("System.Diagnostics.Stopwatch", TheEnginesClock),
        Type("System.Environment", ReachingOut),
        Type("System.Random", TheEnginesClock),
        Type("System.Runtime.InteropServices.Marshal", ReachingOut),
        Type("System.Runtime.InteropServices.NativeLibrary", ReachingOut),
        // [DllImport] compiles to a flag on the method, not to a reference to this
        // type, so the native-call entry below is what catches it; the line stays
        // so the list reads as the brief's.
        Type("System.Runtime.InteropServices.DllImportAttribute", ReachingOut),
        Type("System.Windows.Clipboard", ReachingOut),
        Type("System.Windows.Window", LeavingTheFrame),
        Type("System.Windows.Controls.Primitives.Popup", LeavingTheFrame),
        Type("System.Windows.Controls.ToolTip", LeavingTheFrame),
        Type("System.Windows.Controls.MediaElement", LeavingTheFrame),
        Type("System.Windows.Media.MediaPlayer", LeavingTheFrame),
        Type("System.Windows.Media.CompositionTarget", TheEnginesClock),
        Type("System.Windows.Input.Keyboard", TheWindowsKeys),
        Type("System.Windows.Input.KeyEventArgs", TheWindowsKeys),
        Type("System.Windows.Input.KeyBinding", TheWindowsKeys),
        Type("System.Windows.Input.KeyGesture", TheWindowsKeys),
        Type("System.Windows.Input.Key", TheWindowsKeys),
        Type("System.Threading.Thread", TheEnginesClock),
        Type("System.Threading.ThreadPool", TheEnginesClock),
        Type("System.Threading.Timer", TheEnginesClock),

        // ---- Members ----------------------------------------------------------------
        Member("System.DateTime", "get_Now", TheHostsTime),
        Member("System.DateTime", "get_UtcNow", TheHostsTime),
        Member("System.DateTime", "get_Today", TheHostsTime),
        Member("System.DateTimeOffset", "get_Now", TheHostsTime),
        Member("System.DateTimeOffset", "get_UtcNow", TheHostsTime),
        Member("System.Guid", "NewGuid", NoGuid),
        Member("System.Reflection.Assembly", "Load*", NothingLoaded),
        Member("System.Reflection.Assembly", "UnsafeLoad*", NothingLoaded),
        Member("System.Windows.Application", "*", NoApplication, "LoadComponent"),

        // ---- Native calls -------------------------------------------------------------
        new(ForbiddenKind.PInvoke, "any", NoNativeCall),

        // ---- The engine's one exception ---------------------------------------------
        Type("System.Windows.Threading.DispatcherTimer", GamesTickThroughTheLoop) with { AllowedIn = EngineAssembly },
    };

    /// <summary>Everything in <paramref name="scan"/> that an entry forbids, each once.</summary>
    public static IReadOnlyList<Finding> Check(ScannedAssembly scan)
    {
        var findings = new List<Finding>();
        foreach (var entry in All)
        {
            if (entry.AllowedIn is not null && string.Equals(entry.AllowedIn, scan.Name, StringComparison.OrdinalIgnoreCase)) continue;
            switch (entry.Kind)
            {
                case ForbiddenKind.Assembly:
                    // Assembly names bind without regard to case.
                    foreach (var name in scan.AssemblyReferences.Where(a => NameMatches(entry.Pattern, a, StringComparison.OrdinalIgnoreCase)))
                        findings.Add(new(scan.Name, "the assembly " + name, entry));
                    break;
                case ForbiddenKind.Namespace:
                    foreach (var type in scan.TypeReferences.Where(t => InNamespace(t.Namespace, entry.Pattern)))
                        findings.Add(new(scan.Name, "the type " + type.FullName, entry));
                    break;
                case ForbiddenKind.Type:
                    foreach (var type in scan.TypeReferences.Where(t => TypeMatches(entry.Pattern, t.FullName)))
                        findings.Add(new(scan.Name, "the type " + type.FullName, entry));
                    break;
                case ForbiddenKind.Member:
                    foreach (var member in scan.MemberReferences.Where(m =>
                                 m.Type == entry.Pattern
                                 && NameMatches(entry.Member ?? "*", m.Member, StringComparison.Ordinal)
                                 && !entry.Except.Contains(m.Member, StringComparer.Ordinal)))
                        findings.Add(new(scan.Name, "the member " + member, entry));
                    break;
                case ForbiddenKind.PInvoke:
                    if (scan.HasPInvoke) findings.Add(new(scan.Name, "a native call", entry));
                    break;
            }
        }
        return findings.Distinct().ToList();
    }

    /// <summary>"*" is anything, a trailing '*' a prefix, else the whole name.</summary>
    internal static bool NameMatches(string pattern, string name, StringComparison comparison)
    {
        if (pattern == "*") return true;
        return pattern.EndsWith('*')
            ? name.StartsWith(pattern[..^1], comparison)
            : string.Equals(pattern, name, comparison);
    }

    /// <summary>The namespace itself or one below it: "System.Net" holds "System.Net.Http", never "System.Network".</summary>
    internal static bool InNamespace(string ns, string pattern) =>
        ns == pattern || ns.StartsWith(pattern + ".", StringComparison.Ordinal);

    /// <summary>The type, or a type nested in it ("System.Environment+SpecialFolder" is in System.Environment).</summary>
    internal static bool TypeMatches(string pattern, string fullName) =>
        NameMatches(pattern, fullName, StringComparison.Ordinal)
        || (!pattern.EndsWith('*') && fullName.StartsWith(pattern + "+", StringComparison.Ordinal));
}
