using System.IO;
using PandoraOverlay.Games.Engine;
using Xunit;
using Xunit.Sdk;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The fixture rule (A9) as the games' tests use it: the engine's own folder of
/// sample saves passes, and every way a folder can break the rule fails with a
/// message naming the file or the version. Every failing case runs on a temporary
/// copy, so the repo's fixtures stay good. A08.
/// </summary>
public class SaveFixturesTests
{
    /// <summary>The sample state's fixtures, one per version, in this project's source folder.</summary>
    private const string Folder = "Engine/Saves/Fixtures";

    // ---- Passing -----------------------------------------------------------------------

    /// <summary>The engine's own sample saves, versions 1 to 3, all load through the steps.</summary>
    [Fact]
    public void TheSampleFixturesAllLoad()
    {
        SaveFixtures.AssertAllLoad(SampleSaves.Schema(), Folder);
    }

    /// <summary>
    /// The current version's fixture is exactly what the schema writes today: a pin on
    /// the save format itself, so a change to the JSON rules shows here as a changed text.
    /// </summary>
    [Fact]
    public void TheCurrentFixtureIsWhatTheSchemaWrites()
    {
        string fixture = File.ReadAllText(Path.Combine(FrameGolden.TestsFolder, "Engine", "Saves", "Fixtures", "v3-current.json"));

        Assert.Equal(fixture, SampleSaves.Schema().Save(SampleSave.Full()));
    }

    /// <summary>Each old fixture reads as the values its version meant, upgraded.</summary>
    [Fact]
    public void TheOldFixturesReadAsTheyMeant()
    {
        var schema = SampleSaves.Schema();
        string Text(string name) => File.ReadAllText(Path.Combine(FrameGolden.TestsFolder, "Engine", "Saves", "Fixtures", name));

        var first = schema.Load(1, Text("v1-start.json"));
        var midway = schema.Load(2, Text("v2-midway.json"));

        Assert.True(first.Upgraded);
        Assert.Equal("First run", first.State!.Name);
        Assert.Equal(1, first.State.Played);
        Assert.True(midway.Upgraded);
        Assert.Equal(6, midway.State!.Played);
        Assert.Equal(uint.MaxValue, midway.State.Seed);
        Assert.Equal(long.MaxValue, midway.State.Banked);
        Assert.Equal(0.1 + 0.2, midway.State.Rate);
    }

    /// <summary>More than one fixture of a version is fine: a mid-run save beside a fresh one.</summary>
    [Fact]
    public void SeveralFixturesOfOneVersionPass()
    {
        using var temp = CopyOfFixtures();
        File.WriteAllText(Path.Combine(temp.Path, "v2-fresh.json"), "{\"Name\":\"\",\"Wins\":0,\"Losses\":0}");

        SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy");
    }

    // ---- Failing -----------------------------------------------------------------------

    /// <summary>A fixture that no longer loads fails, naming its file, its version and why.</summary>
    [Fact]
    public void ABrokenFixtureFailsNamingItsFile()
    {
        using var temp = CopyOfFixtures();
        File.WriteAllText(Path.Combine(temp.Path, "v2-midway.json"), "{\"Name\":\"Second run\",\"Turn\":");

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy"));

        Assert.Contains("v2-midway.json (version 2) loads as Corrupt", message);
        Assert.Contains("is not JSON", message);
    }

    /// <summary>
    /// The real danger: a later build's step breaks an old version's saves. The old
    /// fixture fails, naming the file and the step.
    /// </summary>
    [Fact]
    public void AStepThatBreaksAnOldVersionFailsItsFixture()
    {
        var schema = new SaveSchema<SampleSave>(3)
            .Upgrade(1, o => { o["Wins"] = "a word"; return SampleSaves.RenameTitle()(o); })
            .Upgrade(2, SampleSaves.WorkOutPlayed());

        string message = Failure(() => SaveFixtures.AssertAllLoad(schema, Folder));

        Assert.Contains("v1-start.json (version 1) loads as Corrupt", message);
        Assert.Contains("upgrade step from version 2 threw", message);
        Assert.Contains("version 1 of SampleSave has no fixture that loads", message);
        Assert.DoesNotContain("v2-midway.json", message);
    }

    /// <summary>A version with no fixture fails, naming the version.</summary>
    [Fact]
    public void AVersionWithNoFixtureFailsNamingIt()
    {
        using var temp = CopyOfFixtures();
        File.Delete(Path.Combine(temp.Path, "v2-midway.json"));

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy"));

        Assert.Contains("version 2 of SampleSave has no fixture that loads", message);
    }

    /// <summary>An empty folder fails for every version.</summary>
    [Fact]
    public void AnEmptyFolderFailsForEveryVersion()
    {
        using var temp = new TempFolder();

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "empty"));

        Assert.Contains("versions 1, 2, 3 of SampleSave have no fixture", message);
    }

    /// <summary>A file not named as a fixture would never be loaded: it fails, naming it.</summary>
    [Theory]
    [InlineData("start.json")]
    [InlineData("v0-start.json")]
    [InlineData("v01-start.json")]
    [InlineData("V1-other.json")] // not "V1-start.json": the file system ignores case, and would overwrite v1-start.json
    [InlineData("v1_start.json")]
    [InlineData("v1-start.txt")]
    [InlineData("v1-.json")]
    public void AMisnamedFileFailsNamingIt(string name)
    {
        using var temp = CopyOfFixtures();
        File.WriteAllText(Path.Combine(temp.Path, name), "{}");

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy"));

        Assert.Contains(name + " is not named as a fixture", message);
    }

    /// <summary>A fixture from above the schema's version means the version went down: it fails, naming it.</summary>
    [Fact]
    public void AFixtureFromAboveTheCurrentVersionFails()
    {
        using var temp = CopyOfFixtures();
        File.WriteAllText(Path.Combine(temp.Path, "v4-ahead.json"), "{}");

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy"));

        Assert.Contains("v4-ahead.json is from version 4, above the schema's current 3", message);
    }

    /// <summary>A folder inside the fixtures' is never read, so it fails rather than hide fixtures.</summary>
    [Fact]
    public void AFolderInsideFails()
    {
        using var temp = CopyOfFixtures();
        Directory.CreateDirectory(Path.Combine(temp.Path, "old"));

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy"));

        Assert.Contains("'old' is a folder", message);
    }

    /// <summary>Every problem is reported in one failure.</summary>
    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        using var temp = CopyOfFixtures();
        File.WriteAllText(Path.Combine(temp.Path, "v1-start.json"), "[]");
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "");
        File.Delete(Path.Combine(temp.Path, "v3-current.json"));

        string message = Failure(() => SaveFixtures.AssertAllLoadIn(SampleSaves.Schema(), temp.Path, "copy"));

        Assert.Contains("v1-start.json (version 1) loads as Corrupt", message);
        Assert.Contains("notes.txt is not named as a fixture", message);
        Assert.Contains("versions 1, 3 of SampleSave have no fixture", message);
    }

    /// <summary>A folder that is not there fails, naming it.</summary>
    [Fact]
    public void AMissingFolderFailsNamingIt()
    {
        string message = Failure(() => SaveFixtures.AssertAllLoad(SampleSaves.Schema(), "Engine/Saves/NoSuchFixtures"));

        Assert.Contains("'Engine/Saves/NoSuchFixtures'", message);
        Assert.Contains("does not exist", message);
    }

    /// <summary>A folder is a relative name in the tests' source folder, never a way out of it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("../Fixtures")]
    [InlineData("Engine/../..")]
    [InlineData("C:\\Fixtures")]
    [InlineData("Engine//Saves")]
    public void AFolderNameOutsideTheTestsThrows(string folder)
    {
        Assert.ThrowsAny<ArgumentException>(() => SaveFixtures.AssertAllLoad(SampleSaves.Schema(), folder));
    }

    // ---- The round trip ----------------------------------------------------------------

    [Fact]
    public void AStateThatSurvivesPassesTheRoundTrip()
    {
        SaveFixtures.AssertRoundTrips(SampleSaves.Schema(), SampleSave.Full());
    }

    /// <summary>The round trip names what was lost: the text from the first character that differs.</summary>
    [Fact]
    public void ALossyStateFailsTheRoundTripSayingWhere()
    {
        var schema = new SaveSchema<SaveSchemaTests.PrivateSetterSave>(1);
        var state = new SaveSchemaTests.PrivateSetterSave();
        state.Play(12);

        string message = Failure(() => SaveFixtures.AssertRoundTrips(schema, state));

        Assert.Contains("from character 8", message);
        Assert.Contains("saved \"12}\"", message);
        Assert.Contains("private setter", message);
    }

    [Fact]
    public void AStateThatReadsAsCorruptFailsTheRoundTrip()
    {
        var schema = new SaveSchema<SaveSchemaTests.GeneratorSave>(1);

        string message = Failure(() => SaveFixtures.AssertRoundTrips(schema, new SaveSchemaTests.GeneratorSave()));

        Assert.Contains("loads back as Corrupt", message);
    }

    // ---- Helpers -----------------------------------------------------------------------

    /// <summary>The message of the assertion <paramref name="check"/> fails with; it must fail.</summary>
    private static string Failure(Action check) => Assert.ThrowsAny<XunitException>(check).Message;

    /// <summary>The repo's sample fixtures copied to a temporary folder, to be broken there.</summary>
    private static TempFolder CopyOfFixtures()
    {
        var temp = new TempFolder();
        string source = Path.Combine(FrameGolden.TestsFolder, "Engine", "Saves", "Fixtures");
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(temp.Path, Path.GetFileName(file)));
        return temp;
    }

    /// <summary>A folder of its own under the temp folder, deleted with everything in it.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder() => Path = Directory.CreateTempSubdirectory("games-saves-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
