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
    }

    private static void AssertFound(IReadOnlyList<Finding> findings, ForbiddenKind kind, string pattern, string subject) =>
        Assert.True(
            findings.Any(f => f.Entry.Kind == kind && f.Entry.Pattern == pattern && f.Subject == subject),
            $"the scan of the tests' own assembly did not report {subject} under {kind} {pattern}; it reported:\n"
            + string.Join("\n", findings.Select(f => f.ToString())));

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
        Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System", "Environment+SpecialFolder") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.IO", "PathTooLongException") })));
        Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Windows", "WindowState") })));
        // Process* is the family: everything a Process hands out.
        foreach (var name in new[] { "Process", "ProcessStartInfo", "ProcessModule", "ProcessThread" })
            Assert.Single(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Diagnostics", name) })));
        foreach (var name in new[] { "Debug", "DebuggableAttribute", "DebuggerHiddenAttribute" })
            Assert.Empty(ForbiddenList.Check(Made(AGame, types: new[] { T("System.Diagnostics", name) })));
    }

    /// <summary>What the brief keeps fine on purpose: streams, readers and writers (the atlas reads embedded art), and WPF's drawing and mouse input.</summary>
    [Fact]
    public void StreamsDrawingAndTheMouseAreFine()
    {
        var fine = new[]
        {
            T("System.IO", "Stream"), T("System.IO", "MemoryStream"), T("System.IO", "StreamReader"), T("System.IO", "BinaryReader"),
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
