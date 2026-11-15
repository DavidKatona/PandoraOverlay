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
/// <para>
/// What it still can't see, written down so nobody reads more into it (A60):
/// an address computed at run time that reaches WPF through a binding or
/// XAML's own conversion (a string property bound to an <c>Image</c>'s
/// <c>Source</c>, or a game's string read there by an <c>x:Static</c>, is
/// turned into a picture fetched by WPF, with no imaging type in the game's
/// code), or through
/// a decoder's <c>Uri</c> constructor (<c>PngBitmapDecoder</c> takes a
/// <c>Uri</c> as well as the stream the engine gives it, and the two are one
/// name in the metadata); and memory reinterpreted (an object read as another
/// type's layout). The address scan (A45) keeps the address out of the source,
/// and review keeps the rest: the metadata says what an assembly names, never
/// what a string it builds will hold.
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
    private const string NoEnvironment =
        "the PC's folders, variables, settings and clock are not a game's (A29); CurrentManagedThreadId alone is allowed, because the compiler itself writes it into every iterator (yield return) (A44)";
    private const string NoNativeCall = "never a native call (A29)";
    private const string GamesTickThroughTheLoop =
        "games tick through GameLoop (A19): the engine's loop is the one timer, and it exists only while a view has something to move";
    // A60, after the constraint review's N2: the compiler keeps the app's types from
    // a game (A2), but a view can walk its visual tree up to the Games window, and a
    // member read by its name needs no type named; and WPF fetches a picture given by
    // its address with no System.Net type in the game's code (A45).
    private const string NoReflection =
        "a game calls what it can name: reading or calling a member by its name is how a view could walk to the window and read what the compiler keeps from it (A2, A60)";
    private const string NoAddress =
        "a game makes no request: WPF fetches a picture given by its address itself, with no System.Net type to see, so no way of making one from an address is a game's (A45, A60)";

    private const string EngineAssembly = "PandoraOverlay.Games.Engine";

    private static ForbiddenEntry Assembly(string name) => Assembly(name, TheApp);
    private static ForbiddenEntry Assembly(string name, string reason) => new(ForbiddenKind.Assembly, name, reason);
    private static ForbiddenEntry Namespace(string name, string reason) => new(ForbiddenKind.Namespace, name, reason);
    private static ForbiddenEntry Type(string name, string reason) => new(ForbiddenKind.Type, name, reason);
    private static ForbiddenEntry Member(string type, string member, string reason, params string[] except) =>
        new(ForbiddenKind.Member, type, reason) { Member = member, Except = except };

    /// <summary>Every entry, in the brief's order (A02): assemblies, namespaces, types, members, native calls, the engine's one exception; A43's additions, A44's Environment by member and A60's reflection and picture doors sit in their groups.</summary>
    public static IReadOnlyList<ForbiddenEntry> All { get; } = new[]
    {
        // ---- Assembly references ------------------------------------------------
        Assembly("PandoraOverlay"),
        Assembly("System.Windows.Forms"),
        Assembly("Velopack"),
        Assembly("Microsoft.Web.WebView2*"),
        Assembly("System.Security.Cryptography.ProtectedData"),
        Assembly("System.Net*"),
        // A60: `dynamic` is reflection by name written as a member call: the compiler
        // turns it into calls on this assembly's run-time binder, and names no member
        // of the target at all.
        Assembly("Microsoft.CSharp", NoReflection),

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
        // A43: a page navigated to is a surface of its own; an assembly loaded by a
        // load context is one the build never saw.
        Namespace("System.Windows.Navigation", LeavingTheFrame),
        Namespace("System.Runtime.Loader", NothingLoaded),
        // A60: an expression tree reads a field or calls a method given by its name
        // (Expression.Field(target, "name")), and compiles to a delegate that does it
        // for real.
        Namespace("System.Linq.Expressions", NoReflection),

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
        // System.Environment is held by member, below (A44).
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
        // A43, leaving the frame: a message box is the likeliest window a game would
        // open by accident; the rest are controls that open a surface of their own (a
        // menu, a drop-down, a calendar, a page, a browser) or play a sound.
        Type("System.Windows.MessageBox", LeavingTheFrame),
        Type("System.Windows.Controls.ToolTipService", LeavingTheFrame),
        Type("System.Windows.Controls.ContextMenu", LeavingTheFrame),
        Type("System.Windows.Controls.ContextMenuService", LeavingTheFrame),
        Type("System.Windows.Controls.Menu", LeavingTheFrame),
        Type("System.Windows.Controls.MenuItem", LeavingTheFrame),
        Type("System.Windows.Controls.ComboBox", LeavingTheFrame),
        Type("System.Windows.Controls.DatePicker", LeavingTheFrame),
        Type("System.Windows.Controls.Frame", LeavingTheFrame),
        Type("System.Windows.Controls.WebBrowser", LeavingTheFrame),
        Type("System.Windows.Controls.SoundPlayerAction", LeavingTheFrame),
        // A43, the keyboard by its other doors: typed text arrives with no
        // KeyEventArgs, and a text box takes the keyboard by itself.
        Type("System.Windows.Input.KeyboardDevice", TheWindowsKeys),
        Type("System.Windows.Input.KeyboardEventArgs", TheWindowsKeys),
        Type("System.Windows.Input.KeyEventHandler", TheWindowsKeys),
        Type("System.Windows.Input.KeyboardEventHandler", TheWindowsKeys),
        Type("System.Windows.Input.TextCompositionEventArgs", TheWindowsKeys),
        Type("System.Windows.Input.TextCompositionEventHandler", TheWindowsKeys),
        Type("System.Windows.Input.TextCompositionManager", TheWindowsKeys),
        Type("System.Windows.Controls.TextBox", TheWindowsKeys),
        Type("System.Windows.Controls.PasswordBox", TheWindowsKeys),
        Type("System.Windows.Controls.RichTextBox", TheWindowsKeys),
        Type("System.Windows.Controls.Primitives.TextBoxBase", TheWindowsKeys),
        // A43, nothing loaded: another domain, or XAML read at run time.
        Type("System.AppDomain", NothingLoaded),
        Type("System.Windows.Markup.XamlReader", NothingLoaded),
        // A68: what every Activator overload answers that makes an object from an assembly
        // and a type given as text (CreateInstance(assemblyName, typeName), CreateInstanceFrom
        // (assemblyFile, typeName)): the first loads an assembly by its name, the second from
        // a file. Activator itself stays (A43).
        Type("System.Runtime.Remoting.ObjectHandle", NothingLoaded),
        // A60, reflection by name: the extension methods that hand out a type's fields,
        // properties, methods and events by name (GetRuntimeField, GetRuntimeMethods ...).
        Type("System.Reflection.RuntimeReflectionExtensions", NoReflection),
        // A60, a picture by address: each of these turns an address into a picture WPF
        // fetches by itself (BitmapImage's UriSource, the converter XAML and bindings
        // use for "Source"), or builds the address (UriBuilder) with no "://" written.
        Type("System.Windows.Media.Imaging.BitmapImage", NoAddress),
        Type("System.Windows.Media.ImageSourceConverter", NoAddress),
        Type("System.UriBuilder", NoAddress),
        // Not on the list, on purpose (A43, A60), and held so by
        // ForbiddenApiTests.TheHarmlessReflectionStaysAllowed:
        // - System.Activator: `new T()` on a type parameter compiles to
        //   Activator.CreateInstance<T>(), and WPF's markup compiler writes it into the
        //   GeneratedInternalTypeHelper (see "Watch" below). It makes a new object and
        //   reads nothing of one that exists. Kept (A43); its overloads that make an
        //   object from an assembly and a type given as text answer an ObjectHandle,
        //   refused above (A68).
        // - System.Uri and its constructor: a view's generated InitializeComponent makes
        //   a relative Uri ("/<assembly>;component/views/x.xaml") for LoadComponent.
        //   Its scheme names are refused below, and an address in the source by the
        //   address scan (A45).
        // - typeof (Type.GetTypeFromHandle), Type.Assembly, a member's Name,
        //   Object.GetType and Exception.GetType (a fault names its type, never its
        //   message): each names what the code already holds, and none reaches a member
        //   by its name.
        // - The assembly's manifest resources (Assembly.GetManifestResourceStream and
        //   GetManifestResourceNames): the game's own embedded art, read by the engine's
        //   atlas.
        // - PngBitmapDecoder's constructor: the engine decodes the art's PNG streams
        //   with it (its Uri overload is the same name: see the summary's gaps).
        // - System.Windows.Input.KeyboardNavigation: a view may switch tab navigation
        //   off with it.
        // Watch: a game whose XAML places an element of one of its own INTERNAL types
        // (<local:Board/> where Board is internal) gets WPF's GeneratedInternalTypeHelper
        // compiled in, and that helper calls PropertyInfo.GetValue and SetValue,
        // Type.InvokeMember and EventInfo.AddEventHandler, which A60 refuses below: the
        // scan then fails the game for code it never wrote (seen on a probe library,
        // Nov 15 2026). Make that element's type public:
        // ForbiddenApiTests.NoGameCompilesWpfsInternalTypeHelper says so by name (A67).
        // An x:Static of an internal
        // class and a view's own x:ClassModifier="internal" bring no helper (the same
        // probe; Hatchling has both and none).

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
        // A44: by member, not by type, since the compiler writes
        // Environment.CurrentManagedThreadId into every iterator (yield return).
        Member("System.Environment", "*", NoEnvironment, "get_CurrentManagedThreadId"),
        // A43: a tooltip or a context menu set in code names neither type.
        Member("System.Windows.FrameworkElement", "set_ToolTip", LeavingTheFrame),
        Member("System.Windows.FrameworkElement", "ToolTipProperty", LeavingTheFrame),
        Member("System.Windows.FrameworkElement", "set_ContextMenu", LeavingTheFrame),
        Member("System.Windows.FrameworkElement", "ContextMenuProperty", LeavingTheFrame),
        Member("System.Windows.FrameworkContentElement", "set_ToolTip", LeavingTheFrame),
        Member("System.Windows.FrameworkContentElement", "ToolTipProperty", LeavingTheFrame),
        Member("System.Windows.FrameworkContentElement", "set_ContextMenu", LeavingTheFrame),
        Member("System.Windows.FrameworkContentElement", "ContextMenuProperty", LeavingTheFrame),

        // A60, reflection by name. Every way to a member by its name (the lookups), and
        // every way to use one found so (read, write, call, hook, make a delegate of),
        // so a door missed on one side is still shut on the other. Exact names on
        // System.Type: a GetType* prefix would catch GetTypeFromHandle, the typeof every
        // game writes. A call is written against the type that DECLARES the member, so
        // MethodInfo.Invoke reaches the metadata as MethodBase::Invoke; both are listed.
        Member("System.Type", "GetField", NoReflection),
        Member("System.Type", "GetFields", NoReflection),
        Member("System.Type", "GetProperty", NoReflection),
        Member("System.Type", "GetProperties", NoReflection),
        Member("System.Type", "GetMethod", NoReflection),
        Member("System.Type", "GetMethods", NoReflection),
        Member("System.Type", "GetMember", NoReflection),
        Member("System.Type", "GetMembers", NoReflection),
        Member("System.Type", "GetConstructor", NoReflection),
        Member("System.Type", "GetConstructors", NoReflection),
        Member("System.Type", "GetEvent", NoReflection),
        Member("System.Type", "GetEvents", NoReflection),
        Member("System.Type", "GetNestedType", NoReflection),
        Member("System.Type", "GetNestedTypes", NoReflection),
        Member("System.Type", "GetDefaultMembers", NoReflection),
        Member("System.Type", "FindMembers", NoReflection),
        Member("System.Type", "InvokeMember", NoReflection),
        // The static lookup of a type by its name (Type.GetType("PandoraOverlay.X")).
        Member("System.Type", "GetType", NoReflection),
        Member("System.Type", "GetTypeFromProgID", NoReflection),
        Member("System.Type", "GetTypeFromCLSID", NoReflection),
        Member("System.Type", "ReflectionOnlyGetType", NoReflection),
        // GetDeclaredField, GetDeclaredMethod(s), GetDeclaredProperty, GetDeclaredEvent,
        // GetDeclaredNestedType; DeclaredFields, DeclaredMethods, DeclaredMembers ...
        Member("System.Reflection.TypeInfo", "GetDeclared*", NoReflection),
        Member("System.Reflection.TypeInfo", "get_Declared*", NoReflection),
        // GetValue and GetValueDirect, SetValue and SetValueDirect.
        Member("System.Reflection.FieldInfo", "GetValue*", NoReflection),
        Member("System.Reflection.FieldInfo", "SetValue*", NoReflection),
        Member("System.Reflection.PropertyInfo", "GetValue", NoReflection),
        Member("System.Reflection.PropertyInfo", "SetValue", NoReflection),
        Member("System.Reflection.PropertyInfo", "GetGetMethod", NoReflection),
        Member("System.Reflection.PropertyInfo", "GetSetMethod", NoReflection),
        Member("System.Reflection.PropertyInfo", "get_GetMethod", NoReflection),
        Member("System.Reflection.PropertyInfo", "get_SetMethod", NoReflection),
        Member("System.Reflection.PropertyInfo", "GetAccessors", NoReflection),
        Member("System.Reflection.MethodBase", "Invoke", NoReflection),
        Member("System.Reflection.MethodInfo", "Invoke", NoReflection),
        Member("System.Reflection.MethodInfo", "CreateDelegate", NoReflection),
        Member("System.Reflection.ConstructorInfo", "Invoke", NoReflection),
        Member("System.Reflection.EventInfo", "AddEventHandler", NoReflection),
        Member("System.Reflection.EventInfo", "RemoveEventHandler", NoReflection),
        // Delegate.CreateDelegate(type, target, "MethodName"): a call by name.
        Member("System.Delegate", "CreateDelegate", NoReflection),
        // Beside Load* and UnsafeLoad* above: the app's own assembly (the entry one), and
        // the lookups of an assembly's types by name, or of all of them.
        Member("System.Reflection.Assembly", "GetEntryAssembly", NoReflection),
        Member("System.Reflection.Assembly", "GetCallingAssembly", NoReflection),
        Member("System.Reflection.Assembly", "GetType", NoReflection),
        Member("System.Reflection.Assembly", "GetTypes", NoReflection),
        Member("System.Reflection.Assembly", "GetExportedTypes", NoReflection),
        Member("System.Reflection.Assembly", "GetForwardedTypes", NoReflection),
        Member("System.Reflection.Assembly", "get_DefinedTypes", NoReflection),
        Member("System.Reflection.Assembly", "get_ExportedTypes", NoReflection),
        Member("System.Reflection.Assembly", "CreateInstance", NoReflection),
        Member("System.Reflection.Assembly", "get_EntryPoint", NoReflection),
        // GetField(s), GetMethod(s), and ResolveField, ResolveMethod, ResolveMember,
        // ResolveType, ResolveString, ResolveSignature (a member by its token).
        Member("System.Reflection.Module", "GetType", NoReflection),
        Member("System.Reflection.Module", "GetTypes", NoReflection),
        Member("System.Reflection.Module", "FindTypes", NoReflection),
        Member("System.Reflection.Module", "GetField*", NoReflection),
        Member("System.Reflection.Module", "GetMethod*", NoReflection),
        Member("System.Reflection.Module", "Resolve*", NoReflection),

        // A60, a picture by address. Create is held by name, every overload: the Uri
        // ones fetch, and the stream ones are not the engine's way (it decodes through
        // PngBitmapDecoder's constructor). The scheme names build "https://" with no
        // "://" in the source (UriSchemeHttp, UriSchemeHttps, UriSchemeFile ...).
        Member("System.Windows.Media.Imaging.BitmapFrame", "Create", NoAddress),
        Member("System.Windows.Media.Imaging.BitmapDecoder", "Create", NoAddress),
        Member("System.Uri", "UriScheme*", NoAddress),
        Member("System.Uri", "SchemeDelimiter", NoAddress),

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
