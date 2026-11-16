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
/// Fine on purpose: <c>System.IO.Stream</c>, <c>MemoryStream</c>, the readers and
/// writers that take only a stream or a string (<c>BinaryReader</c>,
/// <c>StringReader</c>, <c>TextReader</c> and their writers; the engine's atlas
/// hands the art's streams to <c>JsonDocument</c> and its PNG reader), and WPF's own
/// drawing, layout and mouse input. <c>StreamReader</c> and <c>StreamWriter</c>
/// are refused whole, since their path constructors share the stream ones' name,
/// and so is every other way of opening a file by its path that a game would reach
/// for (A69, A71, A73). An assembly's embedded files are the engine's alone
/// to read, a game's own art and sounds (A94): asked by a game, they would be
/// any assembly's it holds, the app's too.
/// </para>
/// <para>
/// What it still can't see, written down so nobody reads more into it (A60):
/// an address computed at run time that reaches WPF through a binding or
/// XAML's own conversion (a string property bound to an <c>Image</c>'s
/// <c>Source</c>, or a game's string read there by an <c>x:Static</c>, is
/// turned into a picture fetched by WPF, with no imaging type in the game's
/// code), or through a decoder's <c>Uri</c> constructor
/// (<c>PngBitmapDecoder</c> takes a <c>Uri</c> as well as the stream the engine
/// gives it, and the two are one name in the metadata); and memory
/// reinterpreted (an object read as another type's layout). The source scans
/// keep what is written out of the source, an address (A45) and a file path
/// (A72: a drive letter, a UNC path or <c>file:</c> starting a string or a
/// value), and review keeps the rest: the metadata says what an assembly
/// names, never what a string it builds will hold. A file named by a
/// <c>Uri</c> built at run time and reaching WPF (<c>PngBitmapDecoder</c>'s, a
/// font's, a resource dictionary's <c>Source</c>) is the address's gap above,
/// and WPF's fonts read from a folder by its path
/// (<c>Fonts.GetFontFamilies</c>, <c>GetTypefaces</c>, which only read) sit
/// with it (A70); and a door no game would reach for by accident (a
/// certificate, a temporary-file collection or a file's access rules by its
/// path, a file's version information, and a COM class a game declares itself,
/// <c>[ComImport]</c> with its <c>[Guid]</c>, which the native-call entry does
/// not see) is review's (A69, A74). So are the app's WPF resources (its
/// map, its fonts, its compiled XAML; no credential: the access header is an
/// embedded file, the engine's alone to read), which a <c>ResourceManager</c>
/// given the app's assembly, or a component URI naming it in a view's XAML
/// (<c>/PandoraOverlay;component/...</c>, the address's gap), still reaches (A94).
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
    // A94, after the constraint review r4's N21: any assembly a game holds can be asked
    // for its embedded files, the app's too (Host.GetType().Assembly), so only the
    // engine asks, and only a game's own assembly.
    private const string TheEnginesResources =
        "a game's embedded files are read by the engine alone (Atlas, SoundBank), from a game's own assembly: another assembly's, the app's among them with the site's access header in a release build, are never a game's (constraint 4, A94)";

    private const string EngineAssembly = "PandoraOverlay.Games.Engine";

    private static ForbiddenEntry Assembly(string name) => Assembly(name, TheApp);
    private static ForbiddenEntry Assembly(string name, string reason) => new(ForbiddenKind.Assembly, name, reason);
    private static ForbiddenEntry Namespace(string name, string reason) => new(ForbiddenKind.Namespace, name, reason);
    private static ForbiddenEntry Type(string name, string reason) => new(ForbiddenKind.Type, name, reason);
    private static ForbiddenEntry Member(string type, string member, string reason, params string[] except) =>
        new(ForbiddenKind.Member, type, reason) { Member = member, Except = except };

    /// <summary>Every entry, in the brief's order (A02): assemblies, namespaces, types, members, native calls, the engine's one exception; A43's additions, A44's Environment by member, A60's reflection and picture doors, A69's path doors, A70's two assemblies, A71 to A74's doors, A94's embedded files and A95's IReflect and invokers sit in their groups.</summary>
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
        // A70: the two framework assemblies behind A69's ConfigurationManager and EventLog,
        // whole: a settings class (ApplicationSettingsBase, then Save()) writes user.config
        // under the user's AppData naming neither type, and EventLogReader reads a log file
        // by its path. Nothing a game needs is in either; the type entries below stay for
        // their words.
        Assembly("System.Configuration.ConfigurationManager", StorageIsTheHosts),
        Assembly("System.Diagnostics.EventLog", ReachingOut),
        // A74, hard constraint 1 and the promise of no request: UI Automation's client
        // walks every top-level window on the desktop, The Isle's included, and asks each
        // window's process for its UI (AutomationElement.RootElement); the performance
        // counters name every running process and read its figures
        // (PerformanceCounterCategory("Process").GetInstanceNames()); the directory
        // services ask the domain's directory over LDAP, with no System.Net type
        // (DirectorySearcher.FindAll). Whole: nothing in them is a game's. UI
        // Automation's provider side stays (see "Not on the list, on purpose").
        Assembly("UIAutomationClient", ReachingOut),
        Assembly("System.Diagnostics.PerformanceCounter", ReachingOut),
        Assembly("System.DirectoryServices", ReachingOut),

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
        // A69, a file or a folder by its path, with no File, FileStream or Path named:
        // the XML readers, writers and documents open and save the file a path names,
        // and their loaders fetch an address too (XmlReader.Create, XmlWriter.Create,
        // XmlDocument.Load and Save; below it, System.Xml.Linq's XDocument and XElement
        // Load and Save); a DataSet reads and writes its XML the same way (ReadXml,
        // WriteXml); a package, a memory-mapped file, a folder's walk, a tar archive and
        // an XPS document each open what their path names (Package.Open,
        // MemoryMappedFile.CreateFromFile, FileSystemEnumerable, TarFile, XpsDocument).
        // Nothing shipped names any of them: the atlas reads JSON with System.Text.Json.
        Namespace("System.Xml", StorageIsTheHosts),
        Namespace("System.Data", StorageIsTheHosts),
        Namespace("System.IO.Packaging", StorageIsTheHosts),
        Namespace("System.IO.MemoryMappedFiles", StorageIsTheHosts),
        Namespace("System.IO.Enumeration", StorageIsTheHosts),
        Namespace("System.Formats.Tar", StorageIsTheHosts),
        Namespace("System.Windows.Xps", StorageIsTheHosts),
        // A69: XAML read from a file at run time (XamlServices.Load(path),
        // XamlXmlReader(path)), as XamlReader below does. The namespace, not the
        // assembly: System.Xaml.dll also holds System.Windows.Markup's types, which a
        // view's generated code names (IComponentConnector).
        Namespace("System.Xaml", NothingLoaded),
        // A69: VisualBasic's FileIO.FileSystem reads, writes and walks files by their
        // paths, and Interaction.Shell starts a program.
        Namespace("Microsoft.VisualBasic", ReachingOut),
        // A73, beside A69's resource entries: the preserialized .resources reader and
        // writer, each by a path (DeserializingResourceReader, PreserializedResourceWriter).
        Namespace("System.Resources.Extensions", StorageIsTheHosts),
        // A73: printing. A file sent to the PC's printer by its path
        // (PrintQueue.AddJob(name, path, fastCopy)), and every queue of the PC
        // (LocalPrintServer); the print dialog is with the types below.
        Namespace("System.Printing", ReachingOut),

        // ---- Types ----------------------------------------------------------------
        Type("System.IO.File", StorageIsTheHosts),
        Type("System.IO.Directory", StorageIsTheHosts),
        Type("System.IO.FileInfo", StorageIsTheHosts),
        Type("System.IO.DirectoryInfo", StorageIsTheHosts),
        Type("System.IO.FileStream", StorageIsTheHosts),
        Type("System.IO.Path", StorageIsTheHosts),
        Type("System.IO.DriveInfo", StorageIsTheHosts),
        Type("System.IO.FileSystemWatcher", StorageIsTheHosts),
        // A69, a file by its path. StreamReader and StreamWriter whole: their path
        // constructors share the name .ctor with their stream ones, so
        // new StreamWriter("debug.txt") names no File and no FileStream (the readers
        // and writers that take only a stream or a string stay, see below). The trace
        // listeners that write the file their path names; a .resources file read or
        // written by its path; the exe's own config file, read and written by
        // ConfigurationManager (the type, not its namespace: System.Configuration.Assemblies
        // is a framework enum's, AssemblyHashAlgorithm).
        Type("System.IO.StreamReader", StorageIsTheHosts),
        Type("System.IO.StreamWriter", StorageIsTheHosts),
        Type("System.Diagnostics.TextWriterTraceListener", StorageIsTheHosts),
        Type("System.Diagnostics.DelimitedListTraceListener", StorageIsTheHosts),
        Type("System.Diagnostics.XmlWriterTraceListener", StorageIsTheHosts),
        Type("System.Resources.ResourceReader", StorageIsTheHosts),
        Type("System.Resources.ResourceWriter", StorageIsTheHosts),
        Type("System.Configuration.ConfigurationManager", StorageIsTheHosts),
        // A69: the PC's event logs, read and written by a log's name (and another
        // machine's, given its name). In the WPF framework a game builds against, so
        // within a game's reach with no package.
        Type("System.Diagnostics.EventLog", ReachingOut),
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
        // A95, after the F10 verdict: the interface every Type implements, whose members
        // are System.Type's lookups and InvokeMember again under its own name. A call
        // through it is written against IReflect, the type that declares it, so the
        // System.Type entries below never see it: ((IReflect)typeof(Assembly))
        // .InvokeMember("GetManifestResourceStream", ...) read the app's embedded files
        // past A94 with every member allowed. And .NET 8's invokers, which call a
        // MethodBase or a ConstructorInfo as MethodBase.Invoke and ConstructorInfo.Invoke
        // do, so a member found by IReflect.GetMethod, or got without a lookup
        // (Delegate.Method, Type.GetInterfaceMap), is still shut on the other side.
        Type("System.Reflection.IReflect", NoReflection),
        Type("System.Reflection.MethodInvoker", NoReflection),
        Type("System.Reflection.ConstructorInvoker", NoReflection),
        // A60, a picture by address: each of these turns an address into a picture WPF
        // fetches by itself (BitmapImage's UriSource, the converter XAML and bindings
        // use for "Source"), or builds the address (UriBuilder) with no "://" written.
        Type("System.Windows.Media.Imaging.BitmapImage", NoAddress),
        Type("System.Windows.Media.ImageSourceConverter", NoAddress),
        Type("System.UriBuilder", NoAddress),
        // A71, a file by its path: the converter that turns a ".cur" or ".ani" name into
        // a cursor read from that file (or fetched). Cursor's constructors are refused by
        // member, below, so Cursor as a type stays.
        Type("System.Windows.Input.CursorConverter", StorageIsTheHosts),
        // A73: the print dialog, a window of its own over The Isle, whose PrintVisual
        // prints to the PC's printer with no dialog at all. The file dialogs are
        // Microsoft.Win32's, refused above.
        Type("System.Windows.Controls.PrintDialog", LeavingTheFrame),
        // A73, a picture by address (the constraint review r2's note): the six decoders
        // beside the engine's PngBitmapDecoder each open what a Uri names, a file or an
        // address, in their constructor. Nothing uses them; PngBitmapDecoder stays (its
        // Uri overload is the summary's gap).
        Type("System.Windows.Media.Imaging.JpegBitmapDecoder", NoAddress),
        Type("System.Windows.Media.Imaging.GifBitmapDecoder", NoAddress),
        Type("System.Windows.Media.Imaging.BmpBitmapDecoder", NoAddress),
        Type("System.Windows.Media.Imaging.TiffBitmapDecoder", NoAddress),
        Type("System.Windows.Media.Imaging.WmpBitmapDecoder", NoAddress),
        Type("System.Windows.Media.Imaging.IconBitmapDecoder", NoAddress),
        // A74: A60's two XAML providers, in code. An ObjectDataProvider calls a public
        // method by its name, a static one of a type named only by typeof included
        // (ObjectType = typeof(Environment), MethodName = "Exit", past A44's member
        // entry); an XmlDataProvider reads or fetches what its Source names.
        Type("System.Windows.Data.ObjectDataProvider", NoReflection),
        Type("System.Windows.Data.XmlDataProvider", NoAddress),
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
        // - An assembly's name (Assembly.GetName): the engine's readers name the
        //   assembly in their errors. Its embedded files are the engine's alone (A94,
        //   among the members below).
        // - PngBitmapDecoder's constructor: the engine decodes the art's PNG streams
        //   with it (its Uri overload is the same name: see the summary's gaps). The
        //   six other decoders are refused whole above (A73).
        // - System.Windows.Input.Cursor as a type, Cursors and FrameworkElement.Cursor
        //   (A71): a view's code may set Cursor = Cursors.Hand. Only Cursor's
        //   constructors (a file's or a stream's) and CursorConverter (a ".cur" name)
        //   are refused.
        // - System.Resources.ResourceSet as a type (A73): only its constructors are
        //   refused.
        // - UI Automation's provider side, UIAutomationTypes and UIAutomationProvider,
        //   and WPF's own automation peers and AutomationProperties (A74): they describe
        //   a game's own controls. The client, which reads other windows, is refused
        //   whole above.
        // - System.Windows.Input.KeyboardNavigation: a view may switch tab navigation
        //   off with it.
        // - The readers and writers that take only a stream or a string (BinaryReader,
        //   StringReader, TextReader and their writers), System.Text.Json (the atlas's
        //   JsonDocument, the saves' JsonSerializer), Debug and Trace, and
        //   DefaultTraceListener and ResourceManager as types (A69): none opens a file
        //   by its path; the two members that do are refused below, and
        //   ForbiddenApiTests.TheStreamsStayAllowed holds the rest allowed.
        // - System.Drawing (Image.FromFile, Save; A69): left out. Its imaging is in
        //   System.Drawing.Common, which the WPF framework the games build against
        //   (Games/Directory.Build.props: UseWPF) does not hold, only Windows Forms'
        //   does: Image.FromFile does not compile in the games' tests (CS1069), which
        //   build as a game does. A game's project that switched Windows Forms on
        //   would reach it, and ProjectRulesTests.NoProjectSwitchesOnAnotherFramework
        //   refuses that switch and any other framework (A70).
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
        // A69, a file by its path, on types that otherwise stay: the default listener
        // (the one Debug and Trace write to) also writes a file once its LogFileName is
        // set, and a file-based resource manager reads the .resources files of a folder
        // its path names (ResourceManager's GetString on the game's own resources stays).
        Member("System.Diagnostics.DefaultTraceListener", "set_LogFileName", StorageIsTheHosts),
        Member("System.Resources.ResourceManager", "CreateFileBasedResourceManager", StorageIsTheHosts),
        // A71 and A73, a file by its path on types that otherwise stay: every Cursor
        // constructor (its path ones share the name .ctor with its stream ones, as
        // StreamReader's do; Cursors.Hand stays), and every ResourceSet constructor
        // (ResourceSet(fileName) beside its stream and reader ones).
        Member("System.Windows.Input.Cursor", ".ctor", StorageIsTheHosts),
        Member("System.Resources.ResourceSet", ".ctor", StorageIsTheHosts),

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

        // A94, after the constraint review r4's N21: an assembly's embedded files
        // (GetManifestResourceStream, both overloads, GetManifestResourceNames,
        // GetManifestResourceInfo) are the engine's to read, for a game's own art and
        // sounds (Atlas.Load, Atlas.NamesIn, SoundBank.Load, which take a games assembly
        // only). Asked by a game, they are any assembly's it holds: the app's
        // (Host.GetType().Assembly) holds its data and the scrambled access header.
        Member("System.Reflection.Assembly", "GetManifestResource*", TheEnginesResources) with { AllowedIn = EngineAssembly },

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
