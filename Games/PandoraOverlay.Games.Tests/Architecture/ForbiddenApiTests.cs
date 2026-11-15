using System.IO;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The forbidden list held on the built assemblies (A29): the Contract, the
/// Engine and every game, as the test run finds them beside itself. A game
/// reaches its project's DLLs only through a ProjectReference here, and
/// <see cref="ProjectRulesTests.EveryProjectIsReferencedByTheTests"/> makes
/// every project under Games/ one, so no game can dodge this scan.
/// </summary>
public class ForbiddenApiTests
{
    private const string Contract = "PandoraOverlay.Games.Contract";
    private const string Engine = "PandoraOverlay.Games.Engine";
    private const string TestsDll = "PandoraOverlay.Games.Tests.dll";

    /// <summary>Every <c>PandoraOverlay.Games.*.dll</c> in the test's output folder but the tests' own: the Contract, the Engine and each game.</summary>
    internal static IReadOnlyList<string> AssembliesUnderTheRule() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "PandoraOverlay.Games.*.dll")
            .Where(f => !string.Equals(Path.GetFileName(f), TestsDll, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The rule itself: nothing on the list in any assembly under it.</summary>
    [Fact]
    public void NoGamesAssemblyReachesForAnythingForbidden()
    {
        var findings = AssembliesUnderTheRule()
            .SelectMany(f => ForbiddenList.Check(MetadataScan.Read(f)))
            .Select(f => f.ToString())
            .ToList();

        Assert.True(findings.Count == 0, "The forbidden list (A29) is broken:\n" + string.Join("\n", findings));
    }

    /// <summary>The class WPF's markup compiler adds to an assembly whose XAML places an element of an internal type of its own.</summary>
    private const string WpfsHelper = "GeneratedInternalTypeHelper";

    /// <summary>
    /// A game's XAML places only public types of its own (A67). An element of an
    /// internal type makes WPF's markup compiler add its GeneratedInternalTypeHelper,
    /// which reads and calls members by name: the forbidden list would then refuse
    /// the game for code it never wrote, whichever members a given WPF version's
    /// helper names. This fact names the cause and the fix instead. Read from each
    /// assembly's type definitions, in any namespace, with System.Reflection.Metadata
    /// (nothing loaded or run, as <see cref="MetadataScan"/>).
    /// </summary>
    [Fact]
    public void NoGameCompilesWpfsInternalTypeHelper()
    {
        var helpers = new List<string>();
        foreach (var file in AssembliesUnderTheRule())
        {
            using var stream = File.OpenRead(file);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                if (md.GetString(type.Name) != WpfsHelper) continue;
                var ns = md.GetString(type.Namespace);
                helpers.Add($"{Path.GetFileName(file)}: {(ns.Length == 0 ? WpfsHelper : ns + "." + WpfsHelper)}");
            }
        }

        Assert.True(helpers.Count == 0,
            "A game's XAML places only public types of its own (A67): WPF compiled its GeneratedInternalTypeHelper into the assemblies below, because their XAML places an element of an internal type of their own, and that helper reads and calls members by name (PropertyInfo.GetValue and SetValue, Type.InvokeMember, EventInfo.AddEventHandler), which the forbidden list refuses for code the game never wrote. Make that element's type public:\n"
            + string.Join("\n", helpers));
    }

    /// <summary>A scan that found nothing to check would pass for ever: the Contract and the Engine are always there, and the tests' own assembly never is.</summary>
    [Fact]
    public void TheScanChecksAtLeastTheContractAndTheEngine()
    {
        var names = AssembliesUnderTheRule().Select(f => MetadataScan.Read(f).Name).ToList();

        Assert.Contains(Contract, names);
        Assert.Contains(Engine, names);
        Assert.DoesNotContain("PandoraOverlay.Games.Tests", names);
    }

    /// <summary>
    /// The dependency rule as the compiler wrote it down (A2), beside the
    /// project files' version of it (<see cref="ProjectRulesTests"/>): of the
    /// overlay's own assemblies, the Contract references none, the Engine the
    /// Contract at most, a game the Engine and the Contract at most. Never the
    /// app (the list above says so too), never another game.
    /// </summary>
    [Fact]
    public void TheBuiltAssembliesReferenceOneWay()
    {
        var problems = new List<string>();
        foreach (var scan in AssembliesUnderTheRule().Select(MetadataScan.Read))
        {
            var allowed = scan.Name switch
            {
                Contract => Array.Empty<string>(),
                Engine => new[] { Contract },
                _ => new[] { Contract, Engine },
            };
            foreach (var reference in scan.AssemblyReferences.Where(a => a.StartsWith("PandoraOverlay", StringComparison.OrdinalIgnoreCase)))
                if (!allowed.Contains(reference, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{scan.Name} references {reference}");
        }

        Assert.True(problems.Count == 0, "A game → Engine → Contract, one way only (A2):\n" + string.Join("\n", problems));
    }

    // ---- The scan proves itself ------------------------------------------------------

    /// <summary>
    /// The test assembly is not under the rule, and <see cref="Planted"/> uses one
    /// thing of each kind the list names: the scan of this very assembly must
    /// report every one. (The tests' own code reads files too, so a file is
    /// found here twice over; the point is that each kind is found at all.)
    /// </summary>
    [Fact]
    public void TheScanFindsWhatWasPlanted()
    {
        var scan = MetadataScan.Read(typeof(Planted).Assembly.Location);
        var findings = ForbiddenList.Check(scan);

        Assert.Equal("PandoraOverlay.Games.Tests", scan.Name);
        AssertFound(findings, ForbiddenKind.Type, "System.IO.File", "the type System.IO.File");
        AssertFound(findings, ForbiddenKind.Type, "System.Random", "the type System.Random");
        AssertFound(findings, ForbiddenKind.Member, "System.DateTime", "the member System.DateTime::get_UtcNow");
        AssertFound(findings, ForbiddenKind.PInvoke, "any", "a native call");
        AssertFound(findings, ForbiddenKind.Type, "System.Windows.Input.Keyboard", "the type System.Windows.Input.Keyboard");
        AssertFound(findings, ForbiddenKind.Type, "System.Windows.Threading.DispatcherTimer", "the type System.Windows.Threading.DispatcherTimer");
        AssertFound(findings, ForbiddenKind.Assembly, "System.Net*", "the assembly System.Net.Primitives");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Net", "the type System.Net.IPAddress");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Timers", "the type System.Timers.ElapsedEventArgs");
        AssertFound(findings, ForbiddenKind.Member, "System.Windows.Application", "the member System.Windows.Application::get_Current");

        // A60, reflection by name: each under the owner the compiler wrote. MethodInfo
        // inherits Invoke, so a MethodInfo's call reaches the metadata as MethodBase's.
        AssertFound(findings, ForbiddenKind.Member, "System.Type", "the member System.Type::GetField");
        AssertFound(findings, ForbiddenKind.Member, "System.Reflection.FieldInfo", "the member System.Reflection.FieldInfo::GetValue");
        AssertFound(findings, ForbiddenKind.Member, "System.Type", "the member System.Type::GetMethod");
        AssertFound(findings, ForbiddenKind.Member, "System.Reflection.MethodBase", "the member System.Reflection.MethodBase::Invoke");
        AssertFound(findings, ForbiddenKind.Member, "System.Type", "the member System.Type::GetProperty");
        AssertFound(findings, ForbiddenKind.Member, "System.Reflection.PropertyInfo", "the member System.Reflection.PropertyInfo::GetValue");
        AssertFound(findings, ForbiddenKind.Member, "System.Delegate", "the member System.Delegate::CreateDelegate");
        AssertFound(findings, ForbiddenKind.Member, "System.Reflection.MethodInfo", "the member System.Reflection.MethodInfo::CreateDelegate");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Linq.Expressions", "the type System.Linq.Expressions.Expression");
        AssertFound(findings, ForbiddenKind.Assembly, "Microsoft.CSharp", "the assembly Microsoft.CSharp");
        AssertFound(findings, ForbiddenKind.Member, "System.Type", "the member System.Type::GetType");
        AssertFound(findings, ForbiddenKind.Member, "System.Reflection.TypeInfo", "the member System.Reflection.TypeInfo::get_DeclaredFields");
        AssertFound(findings, ForbiddenKind.Type, "System.Reflection.RuntimeReflectionExtensions", "the type System.Reflection.RuntimeReflectionExtensions");
        AssertFound(findings, ForbiddenKind.Member, "System.Reflection.Assembly", "the member System.Reflection.Assembly::GetEntryAssembly");

        // A68: Activator's overloads that make an object from an assembly and a type
        // given as text, found by the ObjectHandle they answer.
        AssertFound(findings, ForbiddenKind.Type, "System.Runtime.Remoting.ObjectHandle", "the type System.Runtime.Remoting.ObjectHandle");

        // A60, a picture by address.
        AssertFound(findings, ForbiddenKind.Type, "System.Windows.Media.Imaging.BitmapImage", "the type System.Windows.Media.Imaging.BitmapImage");
        AssertFound(findings, ForbiddenKind.Type, "System.Windows.Media.ImageSourceConverter", "the type System.Windows.Media.ImageSourceConverter");
        AssertFound(findings, ForbiddenKind.Type, "System.UriBuilder", "the type System.UriBuilder");
        AssertFound(findings, ForbiddenKind.Member, "System.Uri", "the member System.Uri::UriSchemeHttps");
        AssertFound(findings, ForbiddenKind.Member, "System.Uri", "the member System.Uri::SchemeDelimiter");
        AssertFound(findings, ForbiddenKind.Member, "System.Windows.Media.Imaging.BitmapFrame", "the member System.Windows.Media.Imaging.BitmapFrame::Create");
        AssertFound(findings, ForbiddenKind.Member, "System.Windows.Media.Imaging.BitmapDecoder", "the member System.Windows.Media.Imaging.BitmapDecoder::Create");

        // A69, a file or a folder by its path: each under its entry, as the compiler
        // wrote it (a namespace entry reports the type the call is on).
        AssertFound(findings, ForbiddenKind.Type, "System.IO.StreamReader", "the type System.IO.StreamReader");
        AssertFound(findings, ForbiddenKind.Type, "System.IO.StreamWriter", "the type System.IO.StreamWriter");
        AssertFound(findings, ForbiddenKind.Type, "System.Diagnostics.TextWriterTraceListener", "the type System.Diagnostics.TextWriterTraceListener");
        AssertFound(findings, ForbiddenKind.Type, "System.Diagnostics.DelimitedListTraceListener", "the type System.Diagnostics.DelimitedListTraceListener");
        AssertFound(findings, ForbiddenKind.Type, "System.Diagnostics.XmlWriterTraceListener", "the type System.Diagnostics.XmlWriterTraceListener");
        AssertFound(findings, ForbiddenKind.Member, "System.Diagnostics.DefaultTraceListener", "the member System.Diagnostics.DefaultTraceListener::set_LogFileName");
        AssertFound(findings, ForbiddenKind.Type, "System.Resources.ResourceReader", "the type System.Resources.ResourceReader");
        AssertFound(findings, ForbiddenKind.Type, "System.Resources.ResourceWriter", "the type System.Resources.ResourceWriter");
        AssertFound(findings, ForbiddenKind.Member, "System.Resources.ResourceManager", "the member System.Resources.ResourceManager::CreateFileBasedResourceManager");
        AssertFound(findings, ForbiddenKind.Type, "System.Configuration.ConfigurationManager", "the type System.Configuration.ConfigurationManager");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Xml", "the type System.Xml.XmlReader");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Xml", "the type System.Xml.Linq.XDocument");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Data", "the type System.Data.DataSet");
        AssertFound(findings, ForbiddenKind.Namespace, "System.IO.Packaging", "the type System.IO.Packaging.Package");
        AssertFound(findings, ForbiddenKind.Namespace, "System.IO.MemoryMappedFiles", "the type System.IO.MemoryMappedFiles.MemoryMappedFile");
        AssertFound(findings, ForbiddenKind.Namespace, "System.IO.Enumeration", "the type System.IO.Enumeration.FileSystemEnumerable`1");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Formats.Tar", "the type System.Formats.Tar.TarFile");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Xaml", "the type System.Xaml.XamlServices");
        AssertFound(findings, ForbiddenKind.Namespace, "System.Windows.Xps", "the type System.Windows.Xps.Packaging.XpsDocument");
        AssertFound(findings, ForbiddenKind.Namespace, "Microsoft.VisualBasic", "the type Microsoft.VisualBasic.FileIO.FileSystem");
        AssertFound(findings, ForbiddenKind.Type, "System.Diagnostics.EventLog", "the type System.Diagnostics.EventLog");

        // A70: the two assemblies whole, found by the reference a settings class and a
        // log file read by its path write (neither names A69's two types).
        AssertFound(findings, ForbiddenKind.Assembly, "System.Configuration.ConfigurationManager", "the assembly System.Configuration.ConfigurationManager");
        AssertFound(findings, ForbiddenKind.Assembly, "System.Diagnostics.EventLog", "the assembly System.Diagnostics.EventLog");
    }

    private static void AssertFound(IReadOnlyList<Finding> findings, ForbiddenKind kind, string pattern, string subject) =>
        Assert.True(
            findings.Any(f => f.Entry.Kind == kind && f.Entry.Pattern == pattern && f.Subject == subject),
            $"the scan of the tests' own assembly did not report {subject} under {kind} {pattern}; it reported:\n"
            + string.Join("\n", findings.Select(f => f.ToString())));

    /// <summary>
    /// The compiler writes Environment.CurrentManagedThreadId into every
    /// iterator (yield return), so the one member of System.Environment a game
    /// may name is that one (A44). Planted.AnIterator is such an iterator.
    /// </summary>
    [Fact]
    public void TheCompilersIteratorCallIsAllowed()
    {
        var scan = MetadataScan.Read(typeof(Planted).Assembly.Location);

        // If this assertion ever fails, the compiler no longer writes the call into
        // an iterator, and A44's exception can go: Environment back to a type entry.
        Assert.Contains(new MemberName("System.Environment", "get_CurrentManagedThreadId"), scan.MemberReferences);
        Assert.DoesNotContain(ForbiddenList.Check(scan), f => f.Subject == "the member System.Environment::get_CurrentManagedThreadId");
    }

    /// <summary>Every other member of System.Environment is refused: the PC's folders, variables, settings and clock (A29, A44).</summary>
    [Fact]
    public void EnvironmentIsRefusedButForTheIteratorsCall()
    {
        Assert.Empty(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName("System.Environment", "get_CurrentManagedThreadId") })));
        foreach (var member in new[] { "get_NewLine", "get_TickCount64", "GetEnvironmentVariable", "GetFolderPath", "Exit", "get_ProcessPath" })
            Assert.Single(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName("System.Environment", member) })));
    }

    /// <summary>A43's additions: the frame's other doors (a message box, menus, drop-downs, pages, a browser, a sound), the keyboard's (typed text, text boxes) and loading's.</summary>
    [Fact]
    public void TheOtherDoorsAreRefused()
    {
        var types = new[]
        {
            T("System.Windows", "MessageBox"),
            T("System.Windows.Controls", "ToolTipService"), T("System.Windows.Controls", "ContextMenu"), T("System.Windows.Controls", "ContextMenuService"),
            T("System.Windows.Controls", "Menu"), T("System.Windows.Controls", "MenuItem"), T("System.Windows.Controls", "ComboBox"),
            T("System.Windows.Controls", "DatePicker"), T("System.Windows.Controls", "Frame"), T("System.Windows.Controls", "WebBrowser"),
            T("System.Windows.Controls", "SoundPlayerAction"),
            T("System.Windows.Navigation", "NavigationService"),
            T("System.Windows.Input", "KeyboardDevice"), T("System.Windows.Input", "KeyboardEventArgs"), T("System.Windows.Input", "KeyEventHandler"),
            T("System.Windows.Input", "KeyboardEventHandler"), T("System.Windows.Input", "TextCompositionEventArgs"),
            T("System.Windows.Input", "TextCompositionEventHandler"), T("System.Windows.Input", "TextCompositionManager"),
            T("System.Windows.Controls", "TextBox"), T("System.Windows.Controls", "PasswordBox"), T("System.Windows.Controls", "RichTextBox"),
            T("System.Windows.Controls.Primitives", "TextBoxBase"),
            T("System.Runtime.Loader", "AssemblyLoadContext"), T("System", "AppDomain"), T("System.Windows.Markup", "XamlReader"),
        };
        foreach (var type in types)
            Assert.True(ForbiddenList.Check(Made(AGame, types: new[] { type })).Count == 1, type.FullName + " should give exactly one finding");

        foreach (var owner in new[] { "System.Windows.FrameworkElement", "System.Windows.FrameworkContentElement" })
            foreach (var member in new[] { "set_ToolTip", "ToolTipProperty", "set_ContextMenu", "ContextMenuProperty" })
                Assert.True(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(owner, member) })).Count == 1, $"{owner}::{member} should give exactly one finding");
    }

    /// <summary>What A43 leaves alone on purpose: the ordinary controls, the generated code's Activator and Uri, tab navigation, and a view's own sizes and tags.</summary>
    [Fact]
    public void TheOtherDoorsLeaveTheHarmlessAlone()
    {
        var types = new[]
        {
            T("System.Windows.Controls", "TextBlock"), T("System.Windows.Controls", "CheckBox"), T("System.Windows.Controls", "ContentControl"),
            T("System.Windows", "MessageBoxResult"), T("System.Windows.Input", "KeyboardNavigation"),
            T("System.Windows.Markup", "IComponentConnector"), T("System", "Activator"), T("System", "Uri"),
        };
        foreach (var type in types)
            Assert.True(ForbiddenList.Check(Made(AGame, types: new[] { type })).Count == 0, type.FullName + " should give no finding");

        Assert.Empty(ForbiddenList.Check(Made(AGame, members: new[]
        {
            new MemberName("System.Windows.FrameworkElement", "get_ActualWidth"),
            new MemberName("System.Windows.FrameworkElement", "set_Tag"),
        })));
    }

    /// <summary>
    /// A60's members of reflection by name, as the brief lists them; a prefix entry
    /// by the names it is there to catch. Each must give exactly one finding.
    /// </summary>
    private static readonly (string Type, string[] Members)[] ReflectionMembers =
    {
        ("System.Type", new[]
        {
            "GetField", "GetFields", "GetProperty", "GetProperties", "GetMethod", "GetMethods", "GetMember", "GetMembers",
            "GetConstructor", "GetConstructors", "GetEvent", "GetEvents", "GetNestedType", "GetNestedTypes", "GetDefaultMembers",
            "FindMembers", "InvokeMember", "GetType", "GetTypeFromProgID", "GetTypeFromCLSID", "ReflectionOnlyGetType",
        }),
        ("System.Reflection.TypeInfo", new[]
        {
            "GetDeclaredField", "GetDeclaredMethod", "GetDeclaredMethods", "GetDeclaredProperty", "GetDeclaredEvent", "GetDeclaredNestedType",
            "get_DeclaredFields", "get_DeclaredMethods", "get_DeclaredMembers", "get_DeclaredProperties", "get_DeclaredConstructors",
        }),
        ("System.Reflection.FieldInfo", new[] { "GetValue", "GetValueDirect", "SetValue", "SetValueDirect" }),
        ("System.Reflection.PropertyInfo", new[] { "GetValue", "SetValue", "GetGetMethod", "GetSetMethod", "get_GetMethod", "get_SetMethod", "GetAccessors" }),
        ("System.Reflection.MethodBase", new[] { "Invoke" }),
        ("System.Reflection.MethodInfo", new[] { "Invoke", "CreateDelegate" }),
        ("System.Reflection.ConstructorInfo", new[] { "Invoke" }),
        ("System.Reflection.EventInfo", new[] { "AddEventHandler", "RemoveEventHandler" }),
        ("System.Delegate", new[] { "CreateDelegate" }),
        ("System.Reflection.Assembly", new[]
        {
            "GetEntryAssembly", "GetCallingAssembly", "GetType", "GetTypes", "GetExportedTypes", "GetForwardedTypes",
            "get_DefinedTypes", "get_ExportedTypes", "CreateInstance", "get_EntryPoint",
        }),
        ("System.Reflection.Module", new[]
        {
            "GetType", "GetTypes", "FindTypes", "GetField", "GetFields", "GetMethod", "GetMethods",
            "ResolveField", "ResolveMethod", "ResolveMember", "ResolveType", "ResolveString", "ResolveSignature",
        }),
    };

    /// <summary>
    /// Reflection by name is refused (A60): every lookup of a member by its name and
    /// every use of one so found, the extension methods that hand them out,
    /// expression trees, and <c>dynamic</c> (the assembly of its run-time binder).
    /// Each made-up reference gives exactly one finding, so no two entries overlap.
    /// </summary>
    [Fact]
    public void TheReflectionDoorsAreRefused()
    {
        foreach (var (type, members) in ReflectionMembers)
            foreach (var member in members)
                Assert.True(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) })).Count == 1, $"{type}::{member} should give exactly one finding");

        foreach (var type in new[]
                 {
                     T("System.Reflection", "RuntimeReflectionExtensions"), T("System.Runtime.Remoting", "ObjectHandle"),
                     T("System.Linq.Expressions", "Expression"), T("System.Linq.Expressions", "MemberExpression"), T("System.Linq.Expressions", "Expression`1"),
                 })
            Assert.True(ForbiddenList.Check(Made(AGame, types: new[] { type })).Count == 1, type.FullName + " should give exactly one finding");

        var binder = Assert.Single(ForbiddenList.Check(Made(AGame, assemblies: new[] { "Microsoft.CSharp" })));
        Assert.Equal(ForbiddenKind.Assembly, binder.Entry.Kind);
        Assert.Contains("A60", binder.Entry.Reason);
    }

    /// <summary>
    /// A picture by address is refused (A60): the types that turn an address into a
    /// picture WPF fetches by itself or build the address, BitmapFrame.Create and
    /// BitmapDecoder.Create (every overload), and the scheme names that make
    /// "https://" without a "://" in the source.
    /// </summary>
    [Fact]
    public void ThePictureByAddressDoorsAreRefused()
    {
        foreach (var type in new[] { T("System.Windows.Media.Imaging", "BitmapImage"), T("System.Windows.Media", "ImageSourceConverter"), T("System", "UriBuilder") })
            Assert.True(ForbiddenList.Check(Made(AGame, types: new[] { type })).Count == 1, type.FullName + " should give exactly one finding");

        foreach (var (type, member) in new[]
                 {
                     ("System.Windows.Media.Imaging.BitmapFrame", "Create"), ("System.Windows.Media.Imaging.BitmapDecoder", "Create"),
                     ("System.Uri", "UriSchemeHttp"), ("System.Uri", "UriSchemeHttps"), ("System.Uri", "UriSchemeFile"), ("System.Uri", "UriSchemeWss"),
                     ("System.Uri", "SchemeDelimiter"),
                 })
            Assert.True(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) })).Count == 1, $"{type}::{member} should give exactly one finding");
    }

    /// <summary>
    /// What A60 leaves alone on purpose (A43): typeof, a type's assembly and a
    /// member's name, the object's and the exception's GetType, the game's own
    /// embedded art, Uri's constructor (a view's generated code), the engine's PNG
    /// decoder, and the imaging the engine does today (its decoder's frames, a
    /// BitmapFrame and a BitmapDecoder as types, events' Combine and Remove).
    /// </summary>
    [Fact]
    public void TheHarmlessReflectionStaysAllowed()
    {
        foreach (var (type, member) in new[]
                 {
                     ("System.Type", "GetTypeFromHandle"), ("System.Type", "get_Assembly"), ("System.Type", "op_Equality"),
                     ("System.Reflection.MemberInfo", "get_Name"), ("System.Object", "GetType"), ("System.Exception", "GetType"),
                     ("System.Reflection.Assembly", "GetManifestResourceStream"), ("System.Reflection.Assembly", "GetManifestResourceNames"),
                     ("System.Reflection.Assembly", "GetName"), ("System.Uri", ".ctor"),
                     ("System.Windows.Media.Imaging.PngBitmapDecoder", ".ctor"), ("System.Windows.Media.Imaging.BitmapDecoder", "get_Frames"),
                     ("System.Delegate", "Combine"), ("System.Delegate", "Remove"),
                 })
            Assert.True(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) })).Count == 0, $"{type}::{member} should give no finding");

        foreach (var type in new[]
                 {
                     T("System", "Activator"), T("System", "Uri"), T("System.Windows.Media.Imaging", "PngBitmapDecoder"),
                     T("System.Windows.Media.Imaging", "WriteableBitmap"), T("System.Windows.Media.Imaging", "BitmapFrame"),
                     T("System.Windows.Media.Imaging", "BitmapDecoder"), T("System.Windows.Media", "ImageSource"),
                     T("System.Reflection", "Assembly"), T("System.Reflection", "MemberInfo"), T("System", "Delegate"),
                 })
            Assert.True(ForbiddenList.Check(Made(AGame, types: new[] { type })).Count == 0, type.FullName + " should give no finding");
    }

    // The starts of the three reasons A69 gives its entries (ForbiddenList's
    // StorageIsTheHosts, NothingLoaded and ReachingOut): a door kept under the wrong
    // reason would print the wrong why.
    private const string StorageReason = "storage is the host's";
    private const string LoadingReason = "nothing is loaded";
    private const string ReachingOutReason = "reaching out";

    /// <summary>
    /// The framework's ways of opening a file or a folder by a path the caller gives
    /// are refused (A69), each by one entry and under its reason: StreamReader and
    /// StreamWriter whole, the trace listeners that write a file, the .resources
    /// reader and writer, the exe's config, the event log; the default listener's
    /// file and the file-based resource manager by member; and the XML, data,
    /// package, mapped-file, folder-walk, tar, XPS, run-time XAML and VisualBasic
    /// namespaces, each by a type in it; System.Xml also by two types below it, and
    /// System.Windows.Xps and Microsoft.VisualBasic by the type below each that
    /// <see cref="Planted"/> calls; and the two framework assemblies behind
    /// ConfigurationManager and EventLog, whole, as references (A70).
    /// </summary>
    [Fact]
    public void ThePathDoorsAreRefused()
    {
        foreach (var (type, reason) in new[]
                 {
                     (T("System.IO", "StreamReader"), StorageReason), (T("System.IO", "StreamWriter"), StorageReason),
                     (T("System.Diagnostics", "TextWriterTraceListener"), StorageReason),
                     (T("System.Diagnostics", "DelimitedListTraceListener"), StorageReason),
                     (T("System.Diagnostics", "XmlWriterTraceListener"), StorageReason),
                     (T("System.Resources", "ResourceReader"), StorageReason), (T("System.Resources", "ResourceWriter"), StorageReason),
                     (T("System.Configuration", "ConfigurationManager"), StorageReason),
                     (T("System.Diagnostics", "EventLog"), ReachingOutReason),
                     (T("System.Xml", "XmlReader"), StorageReason), (T("System.Xml", "XmlDocument"), StorageReason),
                     (T("System.Xml.Linq", "XDocument"), StorageReason), (T("System.Xml.Serialization", "XmlSerializer"), StorageReason),
                     (T("System.Data", "DataSet"), StorageReason),
                     (T("System.IO.Packaging", "Package"), StorageReason),
                     (T("System.IO.MemoryMappedFiles", "MemoryMappedFile"), StorageReason),
                     (T("System.IO.Enumeration", "FileSystemEnumerable`1"), StorageReason),
                     (T("System.Formats.Tar", "TarFile"), StorageReason),
                     (T("System.Windows.Xps", "XpsDocumentWriter"), StorageReason), (T("System.Windows.Xps.Packaging", "XpsDocument"), StorageReason),
                     (T("System.Xaml", "XamlServices"), LoadingReason), (T("System.Xaml", "XamlXmlReader"), LoadingReason),
                     (T("Microsoft.VisualBasic", "Interaction"), ReachingOutReason), (T("Microsoft.VisualBasic.FileIO", "FileSystem"), ReachingOutReason),
                 })
        {
            var findings = ForbiddenList.Check(Made(AGame, types: new[] { type }));
            Assert.True(findings.Count == 1, type.FullName + " should give exactly one finding");
            Assert.StartsWith(reason, findings[0].Entry.Reason);
        }

        foreach (var (type, member) in new[]
                 {
                     ("System.Diagnostics.DefaultTraceListener", "set_LogFileName"),
                     ("System.Resources.ResourceManager", "CreateFileBasedResourceManager"),
                 })
        {
            var findings = ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) }));
            Assert.True(findings.Count == 1, $"{type}::{member} should give exactly one finding");
            Assert.StartsWith(StorageReason, findings[0].Entry.Reason);
        }

        // A70: the two framework assemblies behind ConfigurationManager and EventLog, whole.
        foreach (var (assembly, reason) in new[]
                 {
                     ("System.Configuration.ConfigurationManager", StorageReason),
                     ("System.Diagnostics.EventLog", ReachingOutReason),
                 })
        {
            var findings = ForbiddenList.Check(Made(AGame, assemblies: new[] { assembly }));
            Assert.True(findings.Count == 1, $"the assembly {assembly} should give exactly one finding");
            Assert.StartsWith(reason, findings[0].Entry.Reason);
        }
    }

    /// <summary>
    /// What A69 leaves alone on purpose: the streams, the readers and writers that
    /// take only a stream or a string, System.Text.Json (the atlas's JsonDocument,
    /// the saves' JsonSerializer), Debug, Trace and the default listener, the
    /// resource manager, the framework enum in System.Configuration.Assemblies (why
    /// ConfigurationManager is refused as a type, not by its namespace), and what
    /// the shipped code names beside them today. No finding for any.
    /// </summary>
    [Fact]
    public void TheStreamsStayAllowed()
    {
        foreach (var type in new[]
                 {
                     T("System.IO", "Stream"), T("System.IO", "MemoryStream"), T("System.IO", "BinaryReader"), T("System.IO", "BinaryWriter"),
                     T("System.IO", "StringReader"), T("System.IO", "StringWriter"), T("System.IO", "TextReader"), T("System.IO", "TextWriter"),
                     T("System.Text.Json", "JsonDocument"), T("System.Text.Json", "JsonSerializer"), T("System.Text.Json", "Utf8JsonWriter"),
                     T("System.Diagnostics", "Debug"), T("System.Diagnostics", "Trace"), T("System.Diagnostics", "DefaultTraceListener"),
                     T("System.Resources", "ResourceManager"),
                     T("System.Configuration.Assemblies", "AssemblyHashAlgorithm"),
                     // What the shipped Contract, Engine and Hatchling name today, beside the new entries.
                     T("System.IO", "InvalidDataException"), T("System.Text.Json", "JsonElement"), T("System.Text.Json.Nodes", "JsonNode"),
                     T("System.Text.Json.Serialization.Metadata", "DefaultJsonTypeInfoResolver"),
                     T("System.Diagnostics", "DebuggerHiddenAttribute"), T("System.Diagnostics", "DebuggerNonUserCodeAttribute"),
                 })
            Assert.True(ForbiddenList.Check(Made(AGame, types: new[] { type })).Count == 0, type.FullName + " should give no finding");

        foreach (var (type, member) in new[]
                 {
                     ("System.IO.Stream", "Read"), ("System.Diagnostics.Debug", "WriteLine"), ("System.Resources.ResourceManager", "GetString"),
                     ("System.Text.Json.JsonDocument", "Parse"), ("System.Text.Json.JsonSerializer", "Serialize"), ("System.Text.Json.JsonSerializer", "Deserialize"),
                 })
            Assert.True(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) })).Count == 0, $"{type}::{member} should give no finding");
    }

    /// <summary>The reader names a generic type's member by the generic type, and a nested type by its outer one.</summary>
    [Fact]
    public void TheReaderNamesGenericAndNestedTypes()
    {
        var scan = MetadataScan.Read(typeof(Planted).Assembly.Location);

        // The tests build lists: a member of List<T> is on "List`1".
        Assert.Contains(scan.MemberReferences, m => m.Type == "System.Collections.Generic.List`1");
        // Every type reference has a name, and a nested one carries its outer type's namespace.
        Assert.All(scan.TypeReferences, t => Assert.False(string.IsNullOrEmpty(t.Name)));
        Assert.All(scan.TypeReferences.Where(t => t.Name.Contains('+')), t => Assert.NotEqual("", t.Namespace));
    }

    // ---- The list's own rules, on scans made by hand --------------------------------

    private static ScannedAssembly Made(string name, string[]? assemblies = null, TypeName[]? types = null, MemberName[]? members = null, bool pinvoke = false) =>
        new(name, assemblies ?? Array.Empty<string>(), types ?? Array.Empty<TypeName>(), members ?? Array.Empty<MemberName>(), pinvoke);

    private static TypeName T(string ns, string name) => new(ns, name);

    private const string AGame = "PandoraOverlay.Games.Sample";

    /// <summary>Games tick through the engine's loop: the engine may hold a DispatcherTimer, nothing else may (A19).</summary>
    [Fact]
    public void TheTimerIsTheEnginesAlone()
    {
        var timer = new[] { T("System.Windows.Threading", "DispatcherTimer") };

        Assert.Empty(ForbiddenList.Check(Made(Engine, types: timer)));
        var finding = Assert.Single(ForbiddenList.Check(Made(AGame, types: timer)));
        Assert.Equal("System.Windows.Threading.DispatcherTimer", finding.Entry.Pattern);
        Assert.Single(ForbiddenList.Check(Made(Contract, types: timer)));
        // The engine's exception is that one type: the rest of the list holds it as every other.
        Assert.NotEmpty(ForbiddenList.Check(Made(Engine, types: new[] { T("System", "Random") })));
    }

    /// <summary>A view's generated code calls Application.LoadComponent; every other member of the application object is refused.</summary>
    [Fact]
    public void TheApplicationGivesLoadComponentAlone()
    {
        Assert.Empty(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName("System.Windows.Application", "LoadComponent") })));
        foreach (var member in new[] { "get_Current", "get_MainWindow", "Shutdown", "FindResource", "get_Resources" })
            Assert.Single(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName("System.Windows.Application", member) })));
    }

    /// <summary>Nothing is loaded at run time; an assembly's own resources are read as streams (the atlas), and that stays allowed.</summary>
    [Fact]
    public void LoadingIsRefusedAndResourcesAreNot()
    {
        foreach (var member in new[] { "Load", "LoadFrom", "LoadFile", "UnsafeLoadFrom" })
            Assert.Single(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName("System.Reflection.Assembly", member) })));
        foreach (var member in new[] { "GetManifestResourceStream", "GetManifestResourceNames", "GetName" })
            Assert.Empty(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName("System.Reflection.Assembly", member) })));
    }

    /// <summary>The wall clock and the unseeded id are refused; building a DateTime or reading its parts is not.</summary>
    [Fact]
    public void TheWallClockIsRefusedAndDatesAreNot()
    {
        foreach (var (type, member) in new[] { ("System.DateTime", "get_Now"), ("System.DateTime", "get_UtcNow"), ("System.DateTime", "get_Today"),
                     ("System.DateTimeOffset", "get_Now"), ("System.DateTimeOffset", "get_UtcNow"), ("System.Guid", "NewGuid") })
            Assert.Single(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) })));
        foreach (var (type, member) in new[] { ("System.DateTime", ".ctor"), ("System.DateTime", "get_Year"), ("System.Guid", "Parse") })
            Assert.Empty(ForbiddenList.Check(Made(AGame, members: new[] { new MemberName(type, member) })));
    }

    /// <summary>A namespace entry holds the namespaces below it, and only those: "System.Net" is not "System.Network".</summary>
    [Fact]
    public void ANamespaceHoldsWhatIsBelowItAlone()
    {
        Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Net.Http", "HttpClient") })));
        Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Threading.Tasks", "Task") })));
        Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Windows.Media.Animation", "DoubleAnimation") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Network", "MadeUp") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Threading", "Interlocked") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Windows.Media", "DrawingVisual") })));
    }

    /// <summary>An assembly is named whole, or by a prefix where the list writes a '*': the app is refused, the Contract (whose name starts the same) is not.</summary>
    [Fact]
    public void AnAssemblyIsNamedWholeOrByItsPrefix()
    {
        foreach (var name in new[] { "PandoraOverlay", "System.Net.Http", "System.Net.Primitives", "Microsoft.Web.WebView2.Core", "Velopack", "System.Windows.Forms", "System.Security.Cryptography.ProtectedData" })
            Assert.Single(ForbiddenList.Check(Made(AGame, assemblies: new[] { name })));
        foreach (var name in new[] { Contract, Engine, "System.Runtime", "PresentationFramework", "System.Text.Json" })
            Assert.Empty(ForbiddenList.Check(Made(AGame, assemblies: new[] { name })));
    }

    /// <summary>A type entry holds the type and the types nested in it, never a type whose name only starts the same (System.IO.Path is not PathTooLongException).</summary>
    [Fact]
    public void ATypeHoldsItsNestedTypesAndNoNamesake()
    {
        Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System.IO", "File+Inner") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.IO", "PathTooLongException") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Windows", "WindowState") })));
        // Process* is the family: everything a Process hands out.
        foreach (var name in new[] { "Process", "ProcessStartInfo", "ProcessModule", "ProcessThread" })
            Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Diagnostics", name) })));
        foreach (var name in new[] { "Debug", "DebuggableAttribute", "DebuggerHiddenAttribute" })
            Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Diagnostics", name) })));
    }

    /// <summary>What the brief keeps fine on purpose: streams and the readers that take one (the atlas reads embedded art), and WPF's drawing and mouse input. StreamReader left this list with A69, which refuses it whole for its path constructor; <see cref="TheStreamsStayAllowed"/> holds the rest of the readers and writers.</summary>
    [Fact]
    public void StreamsDrawingAndTheMouseAreFine()
    {
        var fine = new[]
        {
            T("System.IO", "Stream"), T("System.IO", "MemoryStream"), T("System.IO", "BinaryReader"),
            T("System.Windows.Media.Imaging", "WriteableBitmap"), T("System.Windows.Media", "DrawingContext"),
            T("System.Windows.Input", "MouseButtonEventArgs"), T("System.Windows.Input", "Mouse"), T("System.Windows", "FrameworkElement"),
        };
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: fine)));
    }

    /// <summary>Any native call is refused, wherever it is declared.</summary>
    [Fact]
    public void ANativeCallIsRefused()
    {
        Assert.Single(ForbiddenList.Check(Made(Engine, pinvoke: true)));
        Assert.Empty(ForbiddenList.Check(Made(Engine)));
    }

    /// <summary>Every line of the list prints a reason and says what it matches.</summary>
    [Fact]
    public void EveryEntryHasItsReason()
    {
        Assert.All(ForbiddenList.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Reason));
            Assert.False(string.IsNullOrWhiteSpace(e.Pattern));
            if (e.Kind == ForbiddenKind.Member) Assert.False(string.IsNullOrWhiteSpace(e.Member));
        });
        Assert.Equal(ForbiddenList.All.Count, ForbiddenList.All.Select(e => e.Describe()).Distinct().Count());
    }
}
