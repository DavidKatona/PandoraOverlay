using System.IO;
using System.Xml.Linq;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>An MSBuild file under Games/ (a project, or a Directory.Build file), read as XML.</summary>
/// <param name="Path">Its full path.</param>
/// <param name="Xml">Its contents.</param>
internal sealed record ProjectFile(string Path, XDocument Xml)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path)!;

    public static ProjectFile Load(string path) => new(path, XDocument.Load(path));

    /// <summary>Every element of that name, wherever it sits (an SDK project has no XML namespace; an old one has MSBuild's: the local name is what counts).</summary>
    public IEnumerable<XElement> Elements(string name) => Xml.Descendants().Where(e => e.Name.LocalName == name);

    /// <summary>The item's Include, or its Update.</summary>
    public static string ItemName(XElement item) => ((string?)item.Attribute("Include") ?? (string?)item.Attribute("Update") ?? "").Trim();
}

/// <summary>
/// The dependency rule as the project files write it (A2, A30): one way only,
/// a game → Engine → Contract, nothing under Games/ reaching the app, another
/// game, a package or a framework but WPF's (A70), every project seen by the
/// tests' scans, warnings errors.
/// The compiler is what then keeps a game from the app's website client, its
/// cookie and its config: a type a project can't reference is a type it can't
/// name. The rules are pure functions over the files (so each is shown failing
/// on a sample, below); the tests at the top hold the repo's own.
/// </summary>
public class ProjectRulesTests
{
    private const string ContractFile = "PandoraOverlay.Games.Contract.csproj";
    private const string EngineFile = "PandoraOverlay.Games.Engine.csproj";
    private const string TestsFile = "PandoraOverlay.Games.Tests.csproj";

    /// <summary>The test project's packages, exactly: the app's tests' three (A2: the only packages under Games/).</summary>
    internal static readonly string[] TestPackages = { "Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio" };

    private static IReadOnlyList<ProjectFile> RepoProjects() => Repo.Files(Repo.Games, "*.csproj").Select(ProjectFile.Load).ToList();

    private static IReadOnlyList<ProjectFile> RepoBuildFiles() =>
        Repo.Files(Repo.Games, "Directory.Build.props").Concat(Repo.Files(Repo.Games, "Directory.Build.targets")).Select(ProjectFile.Load).ToList();

    private static void AssertNone(IReadOnlyList<string> problems, string rule) =>
        Assert.True(problems.Count == 0, rule + ":\n" + string.Join("\n", problems));

    // ---- The repo's own projects ------------------------------------------------------

    /// <summary>The rules read what is there: the Contract, the Engine and the tests at least.</summary>
    [Fact]
    public void TheRulesSeeTheProjects()
    {
        var names = RepoProjects().Select(p => p.FileName).ToList();

        Assert.Contains(ContractFile, names);
        Assert.Contains(EngineFile, names);
        Assert.Contains(TestsFile, names);
        Assert.Contains(RepoBuildFiles(), f => f.FileName == "Directory.Build.props");
    }

    [Fact]
    public void NoProjectReferenceLeavesGames() =>
        AssertNone(ReferencesLeavingGames(Repo.Games, RepoProjects()), "Nothing under Games/ references the app or anything outside Games/ (A2)");

    [Fact]
    public void NoPackageButTheTestsThree() =>
        AssertNone(Packages(RepoProjects(), RepoBuildFiles()), "No package under Games/ but the tests' xUnit (A2, A29)");

    [Fact]
    public void NoProjectSwitchesOnAnotherFramework() =>
        AssertNone(OtherFrameworks(RepoProjects(), RepoBuildFiles()), "A game builds against the WPF framework alone (A70): no UseWindowsForms and no FrameworkReference under Games/, so no other framework's doors (System.Drawing's imaging, a web server) are within a game's reach");

    [Fact]
    public void TheReferencesRunOneWay() =>
        AssertNone(OneWay(RepoProjects()), "A game → Engine → Contract, one way only (A2)");

    [Fact]
    public void EveryProjectIsReferencedByTheTests() =>
        AssertNone(NotReferencedByTheTests(RepoProjects()), "Every project under Games/ is referenced by PandoraOverlay.Games.Tests, so the forbidden list scans its DLL");

    [Fact]
    public void WarningsAreErrorsUnderGames() =>
        AssertNone(WarningsAsErrors(Repo.Games, RepoProjects(), RepoBuildFiles()), "Warnings are errors under Games/ (A30)");

    // ---- The rules ------------------------------------------------------------------

    /// <summary>
    /// A project reference resolved from its project's folder. Null when it
    /// can't be resolved by reading the file (an MSBuild property, a wildcard):
    /// a reference the test can't follow is one it can't vouch for.
    /// </summary>
    internal static string? Resolve(ProjectFile project, XElement reference)
    {
        var include = ProjectFile.ItemName(reference);
        if (include.Length == 0 || include.Contains("$(", StringComparison.Ordinal) || include.Contains('*') || include.Contains(';')) return null;
        return Path.GetFullPath(Path.Combine(project.Folder, include.Replace('/', '\\')));
    }

    private static bool IsUnder(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>No project under Games/ references a project outside it: the app above all (A2).</summary>
    internal static IReadOnlyList<string> ReferencesLeavingGames(string gamesFolder, IReadOnlyList<ProjectFile> projects)
    {
        var problems = new List<string>();
        foreach (var project in projects)
            foreach (var reference in project.Elements("ProjectReference"))
            {
                var target = Resolve(project, reference);
                if (target is null)
                    problems.Add($"{project.FileName}: the reference \"{ProjectFile.ItemName(reference)}\" can't be checked: write it as a plain relative path");
                else if (!IsUnder(target, gamesFolder))
                    problems.Add($"{project.FileName} references {target}, outside Games/: a game never sees the app (A2)");
            }
        return problems;
    }

    /// <summary>
    /// No package under Games/ but the tests' three (A2, A29: the standing
    /// advice is to add none). A Directory.Build file's package would reach
    /// every project under it, so those are held too; and a DLL referenced by
    /// path (a Reference item) would dodge both this rule and the one-way one,
    /// so there is none anywhere.
    /// </summary>
    internal static IReadOnlyList<string> Packages(IReadOnlyList<ProjectFile> projects, IReadOnlyList<ProjectFile> buildFiles)
    {
        var problems = new List<string>();
        foreach (var file in projects.Concat(buildFiles))
        {
            foreach (var reference in file.Elements("Reference"))
                problems.Add($"{file.FileName}: a Reference to \"{ProjectFile.ItemName(reference)}\": nothing under Games/ references a DLL by path");
            var packages = file.Elements("PackageReference").Select(ProjectFile.ItemName).ToList();
            if (file.FileName == TestsFile)
            {
                var expected = TestPackages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                var actual = packages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{file.FileName} has the packages [{string.Join(", ", actual)}]: exactly [{string.Join(", ", expected)}], the app's tests' three");
            }
            else
            {
                foreach (var package in packages)
                    problems.Add($"{file.FileName} has the package {package}: no package under Games/ but the tests' (A2)");
            }
        }
        return problems;
    }

    /// <summary>
    /// A game builds against the WPF framework alone (A70): no project or build file
    /// under Games/ switches Windows Forms on or names a FrameworkReference. A
    /// framework switched on is a reference like a package (the rule above), with no
    /// package to see: Windows Forms' brings System.Drawing.Common within a game's
    /// reach (Image.FromFile and Save, a file by its path), ASP.NET Core's a web
    /// server, and the forbidden list names neither's namespaces. One rule keeps
    /// every framework's doors out, not one entry at a time. A Directory.Build
    /// file's switch would reach every project under it, so those are held too.
    /// </summary>
    internal static IReadOnlyList<string> OtherFrameworks(IReadOnlyList<ProjectFile> projects, IReadOnlyList<ProjectFile> buildFiles)
    {
        var problems = new List<string>();
        foreach (var file in projects.Concat(buildFiles))
        {
            foreach (var _ in file.Elements("UseWindowsForms").Where(IsTrue))
                problems.Add($"{file.FileName}: UseWindowsForms is on: a game builds against the WPF framework alone (A70)");
            foreach (var reference in file.Elements("FrameworkReference"))
                problems.Add($"{file.FileName}: a FrameworkReference to \"{ProjectFile.ItemName(reference)}\": a game builds against the WPF framework alone (A70)");
        }
        return problems;
    }

    /// <summary>The projects a project references, by their file names (where they sit is <see cref="ReferencesLeavingGames"/>'s business).</summary>
    private static HashSet<string> ReferencedFiles(ProjectFile project) =>
        project.Elements("ProjectReference")
            .Select(r => Resolve(project, r))
            .Where(p => p is not null)
            .Select(p => Path.GetFileName(p!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Contract references no project; the Engine the Contract only; every other project but the tests (a game) the Engine and the Contract and nothing else (A2).</summary>
    internal static IReadOnlyList<string> OneWay(IReadOnlyList<ProjectFile> projects)
    {
        var problems = new List<string>();
        foreach (var project in projects)
        {
            if (project.FileName.Equals(TestsFile, StringComparison.OrdinalIgnoreCase)) continue;
            var (expected, who) = project.FileName switch
            {
                ContractFile => (Array.Empty<string>(), "the Contract references no project"),
                EngineFile => (new[] { ContractFile }, "the Engine references the Contract only"),
                _ => (new[] { ContractFile, EngineFile }, "a game references the Engine and the Contract and nothing else"),
            };
            var actual = ReferencedFiles(project);
            if (!actual.SetEquals(expected))
                problems.Add($"{project.FileName} references [{string.Join(", ", actual.OrderBy(a => a))}]: {who}");
        }
        return problems;
    }

    /// <summary>Every project under Games/ but the tests is referenced by the tests, so its DLL lands beside them and the forbidden list scans it: a new game can't dodge the scans.</summary>
    internal static IReadOnlyList<string> NotReferencedByTheTests(IReadOnlyList<ProjectFile> projects)
    {
        var tests = projects.FirstOrDefault(p => p.FileName.Equals(TestsFile, StringComparison.OrdinalIgnoreCase));
        if (tests is null) return new[] { $"no {TestsFile} under Games/" };
        var referenced = tests.Elements("ProjectReference")
            .Select(r => Resolve(tests, r))
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return projects
            .Where(p => p != tests && !referenced.Contains(Path.GetFullPath(p.Path)))
            .Select(p => $"{p.FileName} is not referenced by {TestsFile}: add a ProjectReference there beside the app's")
            .ToList();
    }

    /// <summary>
    /// Games/Directory.Build.props makes warnings errors (A30), with no
    /// condition; no project or build file under Games/ sets it to anything
    /// else; and no other Directory.Build.props sits under Games/, since MSBuild
    /// reads only the nearest one and it would take the shared settings away
    /// from the projects below it.
    /// </summary>
    internal static IReadOnlyList<string> WarningsAsErrors(string gamesFolder, IReadOnlyList<ProjectFile> projects, IReadOnlyList<ProjectFile> buildFiles)
    {
        var problems = new List<string>();
        var shared = Path.Combine(Path.GetFullPath(gamesFolder), "Directory.Build.props");
        var props = buildFiles.FirstOrDefault(f => string.Equals(Path.GetFullPath(f.Path), shared, StringComparison.OrdinalIgnoreCase));
        if (props is null)
            problems.Add("no Games/Directory.Build.props");
        else if (!props.Elements("TreatWarningsAsErrors").Any(e => IsTrue(e) && !IsConditional(e)))
            problems.Add("Games/Directory.Build.props does not set TreatWarningsAsErrors to true for every build");

        foreach (var file in projects.Concat(buildFiles))
            foreach (var setting in file.Elements("TreatWarningsAsErrors").Where(e => !IsTrue(e)))
                problems.Add($"{file.FileName} sets TreatWarningsAsErrors to \"{setting.Value.Trim()}\"");

        foreach (var other in buildFiles.Where(f => f != props && f.FileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)))
            problems.Add($"{other.Path} would shadow Games/Directory.Build.props for the projects below it");
        return problems;
    }

    private static bool IsTrue(XElement setting) => setting.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool IsConditional(XElement element) =>
        element.AncestorsAndSelf().Any(e => e.Attribute("Condition") is not null);

    // ---- Each rule shown failing on a sample ------------------------------------------

    private const string SampleGames = @"C:\sample\Games";
    private const string SampleGame = "PandoraOverlay.Games.Sample";

    private static ProjectFile Project(string folder, string body) =>
        new(Path.Combine(SampleGames, folder, folder + ".csproj"), XDocument.Parse($"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>"));

    private static string References(params string[] folders) =>
        "<ItemGroup>" + string.Concat(folders.Select(f => $"<ProjectReference Include=\"..\\{f}\\{f}.csproj\" />")) + "</ItemGroup>";

    private static string PackagesOf(params string[] names) =>
        "<ItemGroup>" + string.Concat(names.Select(n => $"<PackageReference Include=\"{n}\" Version=\"1.0.0\" />")) + "</ItemGroup>";

    private static ProjectFile Props(string body, string folder = "") =>
        new(Path.Combine(SampleGames, folder, "Directory.Build.props"), XDocument.Parse($"<Project>{body}</Project>"));

    private static readonly ProjectFile GoodProps = Props("<PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>");

    /// <summary>The shape every rule passes: Contract, Engine → Contract, a game → both, the tests → all three with their three packages.</summary>
    private static List<ProjectFile> GoodProjects(string? gameBody = null) => new()
    {
        Project("PandoraOverlay.Games.Contract", ""),
        Project("PandoraOverlay.Games.Engine", References("PandoraOverlay.Games.Contract")),
        Project(SampleGame, gameBody ?? References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine")),
        Project("PandoraOverlay.Games.Tests", PackagesOf(TestPackages) + References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine", SampleGame)),
    };

    [Fact]
    public void TheGoodShapePassesEveryRule()
    {
        var projects = GoodProjects();
        var build = new[] { GoodProps };

        Assert.Empty(ReferencesLeavingGames(SampleGames, projects));
        Assert.Empty(Packages(projects, build));
        Assert.Empty(OtherFrameworks(projects, build));
        Assert.Empty(OneWay(projects));
        Assert.Empty(NotReferencedByTheTests(projects));
        Assert.Empty(WarningsAsErrors(SampleGames, projects, build));
    }

    [Fact]
    public void AReferenceToTheAppIsCaught()
    {
        var projects = GoodProjects(References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine") + "<ItemGroup><ProjectReference Include=\"..\\..\\PandoraOverlay.csproj\" /></ItemGroup>");

        var problem = Assert.Single(ReferencesLeavingGames(SampleGames, projects));
        Assert.Contains(@"C:\sample\PandoraOverlay.csproj", problem);
        Assert.Contains("outside Games/", problem);
        // A reference the file alone can't resolve is a problem too.
        var hidden = GoodProjects("<ItemGroup><ProjectReference Include=\"$(AppDir)PandoraOverlay.csproj\" /></ItemGroup>");
        Assert.Contains(ReferencesLeavingGames(SampleGames, hidden), p => p.Contains("can't be checked"));
    }

    [Fact]
    public void APackageOutsideTheTestsIsCaught()
    {
        var contract = Project("PandoraOverlay.Games.Contract", PackagesOf("Newtonsoft.Json"));
        var projects = GoodProjects();
        projects[0] = contract;

        var problem = Assert.Single(Packages(projects, new[] { GoodProps }));
        Assert.Contains("PandoraOverlay.Games.Contract.csproj has the package Newtonsoft.Json", problem);
        // In a Directory.Build file it would reach every project, so it is caught there too.
        Assert.Single(Packages(GoodProjects(), new[] { Props(PackagesOf("Newtonsoft.Json")) }));
        // And a DLL by path dodges nothing.
        Assert.Single(Packages(GoodProjects(References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine")
            + "<ItemGroup><Reference Include=\"PandoraOverlay\"><HintPath>..\\..\\bin\\PandoraOverlay.dll</HintPath></Reference></ItemGroup>"), new[] { GoodProps }));
    }

    /// <summary>
    /// A70's rule on samples: Windows Forms switched on in a game's project (in
    /// any case), a FrameworkReference there, and Windows Forms switched on in a
    /// Directory.Build file each give exactly one problem; Windows Forms switched
    /// off, and the good shape, give none.
    /// </summary>
    [Fact]
    public void AnotherFrameworkIsCaught()
    {
        var game = References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine");

        var forms = Assert.Single(OtherFrameworks(GoodProjects(game + "<PropertyGroup><UseWindowsForms>true</UseWindowsForms></PropertyGroup>"), new[] { GoodProps }));
        Assert.StartsWith(SampleGame + ".csproj: UseWindowsForms is on", forms);
        Assert.Single(OtherFrameworks(GoodProjects(game + "<PropertyGroup><UseWindowsForms>TRUE</UseWindowsForms></PropertyGroup>"), new[] { GoodProps }));
        var web = Assert.Single(OtherFrameworks(GoodProjects(game + "<ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup>"), new[] { GoodProps }));
        Assert.Contains("a FrameworkReference to \"Microsoft.AspNetCore.App\"", web);
        // In a Directory.Build file it would reach every project, so it is caught there too.
        Assert.Single(OtherFrameworks(GoodProjects(), new[] { Props("<PropertyGroup><UseWindowsForms>true</UseWindowsForms></PropertyGroup>") }));

        Assert.Empty(OtherFrameworks(GoodProjects(game + "<PropertyGroup><UseWindowsForms>false</UseWindowsForms></PropertyGroup>"), new[] { GoodProps }));
        Assert.Empty(OtherFrameworks(GoodProjects(), new[] { GoodProps }));
    }

    [Fact]
    public void TheTestsHaveTheirThreePackagesExactly()
    {
        var projects = GoodProjects();
        projects[3] = Project("PandoraOverlay.Games.Tests", PackagesOf(TestPackages.Append("Moq").ToArray()) + References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine", SampleGame));
        Assert.Single(Packages(projects, new[] { GoodProps }));

        projects[3] = Project("PandoraOverlay.Games.Tests", PackagesOf("xunit") + References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine", SampleGame));
        Assert.Single(Packages(projects, new[] { GoodProps }));
    }

    [Fact]
    public void AReferenceAgainstTheFlowIsCaught()
    {
        // A game on another game.
        var projects = GoodProjects(References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine", "PandoraOverlay.Games.Other"));
        Assert.Contains(OneWay(projects), p => p.StartsWith(SampleGame + ".csproj", StringComparison.Ordinal));
        // A game without the Engine.
        Assert.Single(OneWay(GoodProjects(References("PandoraOverlay.Games.Contract"))));
        // The Engine on a game, the Contract on the Engine.
        var backwards = GoodProjects();
        backwards[0] = Project("PandoraOverlay.Games.Contract", References("PandoraOverlay.Games.Engine"));
        backwards[1] = Project("PandoraOverlay.Games.Engine", References("PandoraOverlay.Games.Contract", SampleGame));
        Assert.Equal(2, OneWay(backwards).Count);
    }

    [Fact]
    public void AProjectTheTestsDoNotReferenceIsCaught()
    {
        var projects = GoodProjects();
        projects[3] = Project("PandoraOverlay.Games.Tests", PackagesOf(TestPackages) + References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine"));

        var problem = Assert.Single(NotReferencedByTheTests(projects));
        Assert.StartsWith(SampleGame + ".csproj is not referenced", problem);
        Assert.Single(NotReferencedByTheTests(projects.Take(3).ToList())); // and no tests at all
    }

    [Fact]
    public void WarningsThatStopBeingErrorsAreCaught()
    {
        var projects = GoodProjects();

        Assert.Single(WarningsAsErrors(SampleGames, projects, new[] { Props("") }));
        Assert.Single(WarningsAsErrors(SampleGames, projects, new[] { Props("<PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>") }));
        Assert.Single(WarningsAsErrors(SampleGames, projects, Array.Empty<ProjectFile>()));
        // A game that turns it off, and a props of its own that would shadow the shared one.
        Assert.Single(WarningsAsErrors(SampleGames, GoodProjects("<PropertyGroup><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup>"
            + References("PandoraOverlay.Games.Contract", "PandoraOverlay.Games.Engine")), new[] { GoodProps }));
        Assert.Single(WarningsAsErrors(SampleGames, projects, new[] { GoodProps, Props("", SampleGame) }));
    }
}
