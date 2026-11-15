using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The sounds, held in step with their recipes (A84), the art's twin
/// (<see cref="ArtInStepTests"/>, A28). The sound tool
/// (<c>tools/games-sound/</c>, plain Node) is never run by a build or a test, so
/// the WAVs it writes are CHECKED IN, and each game's <c>build.js</c> writes
/// <c>tools/games-sound/&lt;game&gt;/manifest.json</c>: the SHA-256 of every .js
/// source and of every WAV it wrote, and each cue's level and length. These tests
/// read those files (tests may; a game may not) and fail when they drift apart: a
/// source edited without a rebuild, a WAV edited by hand, a new .js no manifest
/// knows, a file in a game's <c>Sound/</c> folder nobody wrote, a cue without its
/// WAV, and any WAV out of its format (RIFF/WAVE, PCM, 1 channel, 44,100 Hz, 16
/// bits, A78), its length (1 to <c>GameSound.MaxSamples</c>), its level (within
/// 0.5 dB of the manifest's, which is within -24 to -5 dBFS, R4), or with a click
/// at an end.
///
/// <para>The clicks set (G27, A106) is one more set of the tool, and NOT a game:
/// <c>tools/games-sound/clicks/</c>, named here by name
/// (<see cref="SoundInStep.ClickSet"/>). It is held as a game is (rules 1, 2 and 5
/// unchanged, rule 4's format and silent ends) but for its place and its bounds:
/// its outputs under <c>Assets/Clicks/</c> only, and every file there one of them
/// (rule 3's twin), since the app embeds them, not a game; and R8's bounds, 15 to
/// 80 ms and -24 to -14 dBFS, held by this file's own constants beside R4's, never
/// read from a manifest (a bound the manifest could set would guard nothing). A
/// game's rules stay exactly as strict: a game's output under <c>Assets/Clicks/</c>
/// is still not under a game's Sound folder, and a click's under a game's
/// <c>Sound/</c> is not under <c>Assets/Clicks/</c>.</para>
///
/// <para>The rules are <see cref="SoundInStep"/>'s functions over a repo root, so
/// they are tested twice: on the real repo (it must be in step now), and on small
/// temporary trees (<see cref="FakeSoundRepo"/>) that break each rule once, so a
/// rule that stopped checking fails a test instead of passing silently. The WAVs
/// are read by a few lines of this file's own: the engine's reader is another task's
/// (S02's <c>SoundBank</c>), and the game's cues are held to it there (S04).</para>
/// </summary>
public class SoundInStepTests
{
    // ---- The real repo ----

    [Fact]
    public void TheRepoHasASoundManifestWithCues()
    {
        // Guards the guards: with none, every rule below would pass on nothing.
        var manifests = SoundInStep.Manifests(SoundInStep.RepoRoot(), new List<string>());
        Assert.NotEmpty(manifests);
        Assert.All(manifests, m => Assert.NotEmpty(m.Cues));
    }

    [Fact]
    public void TheRepoHasTheClicksSetWithItsSixCues()
    {
        // Guards the clicks' guards, and the names every click task shares (PLAN-CLICKS 3.3):
        // the app's ClickCue and the Contract's GameClick read these files by name.
        var clicks = Assert.Single(SoundInStep.Manifests(SoundInStep.RepoRoot(), new List<string>()), m => m.IsClicks);
        Assert.Equal(new[] { "arm", "back", "press", "select", "toggle-off", "toggle-on" }, clicks.Cues.Keys.OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(clicks.Outputs.Keys, o => Assert.StartsWith(SoundInStep.ClickFolder + "/", o));
    }

    [Fact]
    public void EverySourceAndWavIsWhatTheLastBuildWrote()
    {
        AssertNone(SoundInStep.HashProblems(SoundInStep.RepoRoot()));
    }

    [Fact]
    public void EveryToolSourceIsInTheManifests()
    {
        AssertNone(SoundInStep.SourceProblems(SoundInStep.RepoRoot()));
    }

    [Fact]
    public void EverySoundFileIsAnOutputAndNothingElseIs()
    {
        AssertNone(SoundInStep.OutputProblems(SoundInStep.RepoRoot()));
    }

    [Fact]
    public void EveryWavIsInItsFormatAtItsLevelWithSilentEnds()
    {
        AssertNone(SoundInStep.WavProblems(SoundInStep.RepoRoot()));
    }

    [Fact]
    public void EveryCueHasItsWavAndEveryWavItsCue()
    {
        AssertNone(SoundInStep.CueProblems(SoundInStep.RepoRoot()));
    }

    // ---- The rules, each broken once in a small tree ----

    [Fact]
    public void ATreeInStepHasNoProblem()
    {
        using var tree = new FakeSoundRepo();
        AssertNone(SoundInStep.All(tree.Root));
    }

    // Rule 1: every listed file exists and holds its hash.

    [Fact]
    public void ASourceChangedWithoutARebuildFailsAndSaysWhatToRun()
    {
        using var tree = new FakeSoundRepo();
        File.AppendAllText(tree.Path("tools/games-sound/hatchling/cues.js"), "// a comment\n");

        var one = Assert.Single(SoundInStep.HashProblems(tree.Root));
        Assert.Contains("tools/games-sound/hatchling/cues.js", one);
        Assert.Contains("run node tools/games-sound/hatchling/build.js", one);
    }

    [Fact]
    public void AWavEditedByHandFails()
    {
        using var tree = new FakeSoundRepo();
        var bytes = File.ReadAllBytes(tree.Path(FakeSoundRepo.Sound + "bite.wav"));
        bytes[^1] ^= 1;
        File.WriteAllBytes(tree.Path(FakeSoundRepo.Sound + "bite.wav"), bytes);

        var one = Assert.Single(SoundInStep.HashProblems(tree.Root));
        Assert.Contains("bite.wav", one);
        Assert.Contains("run node tools/games-sound/hatchling/build.js", one);
    }

    [Fact]
    public void AListedFileThatIsGoneFails()
    {
        using var tree = new FakeSoundRepo();
        File.Delete(tree.Path("tools/games-sound/wav.js"));

        var one = Assert.Single(SoundInStep.HashProblems(tree.Root));
        Assert.Contains("tools/games-sound/wav.js", one);
        Assert.Contains("does not exist", one);
    }

    // Rule 2: every .js of the tool (out/ aside) is a source of the right manifest.

    [Fact]
    public void ANewSharedSourceThatNoManifestListsFails()
    {
        using var tree = new FakeSoundRepo();
        tree.Write("tools/games-sound/reverb.js", "// new\n");

        var one = Assert.Single(SoundInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-sound/reverb.js", one);
        Assert.Contains("run node tools/games-sound/hatchling/build.js", one);
    }

    [Fact]
    public void ANewGameSourceNotInItsGamesManifestFails()
    {
        using var tree = new FakeSoundRepo();
        tree.Write("tools/games-sound/hatchling/more-cues.js", "// new\n");

        var one = Assert.Single(SoundInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-sound/hatchling/more-cues.js", one);
        Assert.Contains("run node tools/games-sound/hatchling/build.js", one);
    }

    [Fact]
    public void ASourceInAFolderWithNoManifestFails()
    {
        using var tree = new FakeSoundRepo();
        tree.Write("tools/games-sound/other/build.js", "// a game that never built\n");

        var one = Assert.Single(SoundInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-sound/other/build.js", one);
        Assert.Contains("run node tools/games-sound/other/build.js", one);
    }

    [Fact]
    public void AnythingUnderOutIsNotASource()
    {
        using var tree = new FakeSoundRepo();
        tree.Write("tools/games-sound/out/scratch.js", "// scratch\n");
        tree.Write("tools/games-sound/out/sound-board-v2.html", "listened to, never built from");

        AssertNone(SoundInStep.SourceProblems(tree.Root));
    }

    // Rule 3: every file under a game's Sound/ is an output, and every output is under one.

    [Fact]
    public void ASoundFileNobodyWroteFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Sound + "stray.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(-10)));

        var one = Assert.Single(SoundInStep.OutputProblems(tree.Root));
        Assert.Contains("Games/PandoraOverlay.Games.Hatchling/Sound/stray.wav", one);
        Assert.Contains("not an output", one);
    }

    [Fact]
    public void AnOutputOutsideASoundFolderFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes("Games/PandoraOverlay.Games.Hatchling/elsewhere.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(-10)));
        tree.WriteManifest(extraOutput: "Games/PandoraOverlay.Games.Hatchling/elsewhere.wav");

        var one = Assert.Single(SoundInStep.OutputProblems(tree.Root));
        Assert.Contains("elsewhere.wav", one);
        Assert.Contains("not under a game's Sound folder", one);
    }

    [Fact]
    public void TheTestsOwnFolderNamedSoundIsNotAGamesSound()
    {
        // Games/PandoraOverlay.Games.Tests/Sound/ holds THIS file; the Contract and the Engine are not games either.
        using var tree = new FakeSoundRepo();
        tree.Write("Games/PandoraOverlay.Games.Tests/Sound/SoundInStepTests.cs", "// not a sound\n");
        tree.WriteBytes("Games/PandoraOverlay.Games.Engine/Sound/anything.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(-10)));

        AssertNone(SoundInStep.OutputProblems(tree.Root));
    }

    // Rule 4: every WAV in its format, its length, its level, with silent ends.

    public static TheoryData<string, string> WrongFormats => new()
    {
        { "riff", "not RIFF/WAVE" },
        { "pcm", "not PCM" },
        { "stereo", "not 1 channel" },
        { "rate", "not 44,100 Hz" },
        { "bits", "not 16 bits" },
        { "chunk", "a chunk other than fmt and data" },
        { "short", "shorter than a WAV header" },
    };

    [Theory]
    [MemberData(nameof(WrongFormats))]
    public void AWavOutOfItsFormatFailsAndSaysWhy(string wrong, string why)
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Broken(FakeSoundRepo.Wav(FakeSoundRepo.Tone(-5)), wrong));
        tree.WriteManifest();

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("bite.wav", one);
        Assert.Contains(why, one);
    }

    [Fact]
    public void AnEmptyWavFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(Array.Empty<short>()));
        tree.WriteManifest();

        Assert.Contains(SoundInStep.WavProblems(tree.Root), p => p.Contains("bite.wav") && p.Contains("0 samples"));
    }

    [Fact]
    public void AWavLongerThanTwoSecondsFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(-5, SoundInStep.MaxSamples + 1)));
        tree.WriteManifest();

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("88201 samples", one);
    }

    [Fact]
    public void AWavOfTwoSecondsExactlyPasses()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(-5, SoundInStep.MaxSamples)));
        tree.WriteManifest();

        AssertNone(SoundInStep.All(tree.Root));
    }

    [Fact]
    public void APeakOffItsCuesLevelFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteManifest(levels: new() { ["bite"] = -5.6, ["step"] = -16 });

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("bite.wav", one);
        Assert.Contains("-5.6", one);
    }

    [Fact]
    public void APeakWithinHalfADecibelOfItsLevelPasses()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteManifest(levels: new() { ["bite"] = -5.4, ["step"] = -16.4 });

        AssertNone(SoundInStep.WavProblems(tree.Root));
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(-25)]
    public void ALevelOutsideTheBoundsFails(int level)
    {
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(level)));
        tree.WriteManifest(levels: new() { ["bite"] = level, ["step"] = -16 });

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("outside -24 to -5 dBFS", one);
    }

    [Fact]
    public void AClickAtTheStartFails()
    {
        using var tree = new FakeSoundRepo();
        var samples = FakeSoundRepo.Tone(-5);
        samples[0] = 1000;
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(samples));
        tree.WriteManifest();

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("first sample", one);
    }

    [Fact]
    public void AClickInTheLastTwoMillisecondsFails()
    {
        using var tree = new FakeSoundRepo();
        var samples = FakeSoundRepo.Tone(-5);
        samples[^SoundInStep.EdgeSamples] = 100;
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(samples));
        tree.WriteManifest();

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("last 2 ms", one);
    }

    [Theory]
    [InlineData(32767)]
    [InlineData(-32767)]
    [InlineData(-32768)]
    public void ASampleAtFullScaleFails(short sample)
    {
        using var tree = new FakeSoundRepo();
        var samples = FakeSoundRepo.Tone(-5);
        samples[samples.Length / 3] = sample;
        tree.WriteBytes(FakeSoundRepo.Sound + "bite.wav", FakeSoundRepo.Wav(samples));
        tree.WriteManifest();

        Assert.Contains(SoundInStep.WavProblems(tree.Root), p => p.Contains("bite.wav") && p.Contains("full scale"));
    }

    [Fact]
    public void AManifestLengthThatIsNotTheWavsFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteManifest(msOffset: 1);

        Assert.Equal(2, SoundInStep.WavProblems(tree.Root).Count(p => p.Contains(" ms, the manifest says ")));
    }

    // Rule 5: every cue has its WAV, and every WAV its cue.

    [Fact]
    public void ACueWithNoWavFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteManifest(levels: new() { ["bite"] = -5, ["step"] = -16, ["roar"] = -7 });

        var one = Assert.Single(SoundInStep.CueProblems(tree.Root));
        Assert.Contains("'roar'", one);
        Assert.Contains("run node tools/games-sound/hatchling/build.js", one);
    }

    [Fact]
    public void AWavWithNoCueFails()
    {
        using var tree = new FakeSoundRepo();
        tree.WriteManifest(levels: new() { ["bite"] = -5 });

        var one = Assert.Single(SoundInStep.CueProblems(tree.Root));
        Assert.Contains("step.wav", one);
        Assert.Contains("no cue", one);
    }

    // ---- The clicks set (A106, R8), each new rule broken once in a small tree ----

    [Fact]
    public void ATreeWithTheClicksSetInStepHasNoProblem()
    {
        // The game's bite (-5 dBFS, 100 ms) passes beside the clicks: R8's bounds are the clicks' only.
        using var tree = new FakeSoundRepo(clicks: true);
        AssertNone(SoundInStep.All(tree.Root));
    }

    // Rules 1 and 2, for the clicks set: unchanged, and the cure names its own build.

    [Fact]
    public void AClickEditedByHandFailsAndSaysWhatToRun()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        var bytes = File.ReadAllBytes(tree.Path(FakeSoundRepo.Clicks + "press.wav"));
        bytes[^1] ^= 1;
        File.WriteAllBytes(tree.Path(FakeSoundRepo.Clicks + "press.wav"), bytes);

        var one = Assert.Single(SoundInStep.HashProblems(tree.Root));
        Assert.Contains("Assets/Clicks/press.wav", one);
        Assert.Contains("run node tools/games-sound/clicks/build.js", one);
    }

    [Fact]
    public void ANewClicksSourceNotInItsManifestFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.Write("tools/games-sound/clicks/more-cues.js", "// new\n");

        var one = Assert.Single(SoundInStep.SourceProblems(tree.Root));
        Assert.Contains("tools/games-sound/clicks/more-cues.js", one);
        Assert.Contains("run node tools/games-sound/clicks/build.js", one);
    }

    [Fact]
    public void ANewSharedSourceIsMissingFromTheGamesManifestAndTheClicks()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.Write("tools/games-sound/reverb.js", "// new\n");

        var problems = SoundInStep.SourceProblems(tree.Root);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("tools/games-sound/reverb.js") && p.Contains("run node tools/games-sound/hatchling/build.js"));
        Assert.Contains(problems, p => p.Contains("tools/games-sound/reverb.js") && p.Contains("run node tools/games-sound/clicks/build.js"));
    }

    // Rule 3's twin: every file under Assets/Clicks/ is an output of the clicks set, and its outputs are there only.

    [Fact]
    public void AClickFileNobodyWroteFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes(FakeSoundRepo.Clicks + "stray.wav", FakeSoundRepo.Wav(FakeSoundRepo.ClickTone(-18)));

        var one = Assert.Single(SoundInStep.OutputProblems(tree.Root));
        Assert.Contains("Assets/Clicks/stray.wav", one);
        Assert.Contains("not an output of the clicks set", one);
    }

    [Fact]
    public void AClickFileIsAProblemWithNoClicksSetAtAll()
    {
        // With no clicks manifest, nothing wrote Assets/Clicks/: rule 3's twin still reads the folder.
        using var tree = new FakeSoundRepo();
        tree.WriteBytes(FakeSoundRepo.Clicks + "press.wav", FakeSoundRepo.Wav(FakeSoundRepo.ClickTone(-18)));

        var one = Assert.Single(SoundInStep.OutputProblems(tree.Root));
        Assert.Contains("Assets/Clicks/press.wav", one);
    }

    [Fact]
    public void AClickOutputOutsideAssetsClicksFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes("Assets/elsewhere.wav", FakeSoundRepo.Wav(FakeSoundRepo.ClickTone(-18)));
        tree.WriteClickManifest(extraOutput: "Assets/elsewhere.wav");

        var one = Assert.Single(SoundInStep.OutputProblems(tree.Root));
        Assert.Contains("Assets/elsewhere.wav", one);
        Assert.Contains("not under Assets/Clicks/", one);
    }

    [Fact]
    public void AClickOutputUnderAGamesSoundFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes(FakeSoundRepo.Sound + "tick.wav", FakeSoundRepo.Wav(FakeSoundRepo.ClickTone(-18)));
        tree.WriteClickManifest(extraOutput: FakeSoundRepo.Sound + "tick.wav");

        var one = Assert.Single(SoundInStep.OutputProblems(tree.Root));
        Assert.Contains("Games/PandoraOverlay.Games.Hatchling/Sound/tick.wav", one);
        Assert.Contains("not under Assets/Clicks/", one);
    }

    [Fact]
    public void AGamesOutputUnderAssetsClicksStillFails()
    {
        // Hatchling's rule as strict as before: a game writes under its own Sound/ only.
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes(FakeSoundRepo.Clicks + "bite.wav", FakeSoundRepo.Wav(FakeSoundRepo.Tone(-5)));
        tree.WriteManifest(extraOutput: FakeSoundRepo.Clicks + "bite.wav");

        var problems = SoundInStep.OutputProblems(tree.Root);
        Assert.Contains(problems, p => p.Contains("Assets/Clicks/bite.wav") && p.Contains("not under a game's Sound folder"));
        Assert.Contains(problems, p => p.Contains("Assets/Clicks/bite.wav") && p.Contains("not an output of the clicks set"));
    }

    // Rule 4, with R8's bounds for the clicks set.

    [Theory]
    [InlineData(661, false)]  // 14.99 ms: under R8's 15
    [InlineData(662, true)]   // 15.01 ms, ceil(0.015 * 44,100): build.js's shortest take
    [InlineData(3528, true)]  // 80 ms exactly: ClickSound.MinGap
    [InlineData(3529, false)] // a sample past it: two clicks could overlap
    public void AClickIsHeldToFifteenToEightyMilliseconds(int length, bool passes)
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes(FakeSoundRepo.Clicks + "press.wav", FakeSoundRepo.Wav(FakeSoundRepo.ClickTone(-18, length)));
        tree.WriteClickManifest();

        var problems = SoundInStep.WavProblems(tree.Root);
        if (passes) AssertNone(problems);
        else
        {
            var one = Assert.Single(problems);
            Assert.Contains($"{length} samples", one);
            Assert.Contains("662 to 3528", one);
        }
    }

    [Fact]
    public void AnEmptyClickFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes(FakeSoundRepo.Clicks + "press.wav", FakeSoundRepo.Wav(Array.Empty<short>()));
        tree.WriteClickManifest();

        Assert.Contains(SoundInStep.WavProblems(tree.Root), p => p.Contains("Assets/Clicks/press.wav") && p.Contains("0 samples"));
    }

    [Theory]
    [InlineData(-13)] // too loud for a click
    [InlineData(-5)]  // a game's loudest, the bite: fine for a game, never for a click
    [InlineData(-25)] // too quiet
    public void AClickLevelOutsideREightsBoundsFails(int level)
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteBytes(FakeSoundRepo.Clicks + "press.wav", FakeSoundRepo.Wav(FakeSoundRepo.ClickTone(level)));
        tree.WriteClickManifest(levels: new() { ["press"] = level, ["back"] = -21 });

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("Assets/Clicks/press.wav", one);
        Assert.Contains("outside -24 to -14 dBFS (R8)", one);
    }

    [Fact]
    public void AClickPeakOffItsLevelFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteClickManifest(levels: new() { ["press"] = -18.6, ["back"] = -21 });

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("Assets/Clicks/press.wav", one);
        Assert.Contains("-18.6", one);
    }

    [Fact]
    public void AClickWithASoundAtItsStartFails()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        var samples = FakeSoundRepo.ClickTone(-18);
        samples[0] = 1000;
        tree.WriteBytes(FakeSoundRepo.Clicks + "press.wav", FakeSoundRepo.Wav(samples));
        tree.WriteClickManifest();

        var one = Assert.Single(SoundInStep.WavProblems(tree.Root));
        Assert.Contains("first sample", one);
    }

    // Rule 5, for the clicks set.

    [Fact]
    public void AClickCueWithNoWavFailsAndNamesItsPlace()
    {
        using var tree = new FakeSoundRepo(clicks: true);
        tree.WriteClickManifest(levels: new() { ["press"] = -18, ["back"] = -21, ["arm"] = -16 });

        var one = Assert.Single(SoundInStep.CueProblems(tree.Root));
        Assert.Contains("'arm'", one);
        Assert.Contains("Assets/Clicks/arm.wav", one);
        Assert.Contains("run node tools/games-sound/clicks/build.js", one);
    }

    private static void AssertNone(IReadOnlyList<string> problems) =>
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
}

/// <summary>
/// The rules, as functions over a repo root. Each returns what is wrong, one line
/// per problem, and every line that has a cure says it: "run node
/// tools/games-sound/&lt;game&gt;/build.js".
/// </summary>
internal static class SoundInStep
{
    private const string ToolFolder = "tools/games-sound";

    /// <summary>The rate every game's sound is at (GameSound.SampleRate, A78): the first board's own.</summary>
    public const int SampleRate = 44_100;

    /// <summary>
    /// The longest sound a game may hand the host: 2 s, <c>GameSound.MaxSamples</c>
    /// (A78). S02 adds that constant to the Contract beside this task, so it is the
    /// number here; S04 holds the cues to the Contract's own.
    /// </summary>
    public const int MaxSamples = 2 * SampleRate;

    /// <summary>R4's bounds for a cue's level, the dBFS of its peak: the bite's -5 the loudest, -24 the quietest.</summary>
    public const double MinLevel = -24, MaxLevel = -5;

    /// <summary>How far a WAV's measured peak may sit from its cue's level (A84): rounding to 16 bits moves it by far less.</summary>
    public const double LevelTolerance = 0.5;

    /// <summary>2 ms of samples (88), the end that must be silent: a sound that stops above silence clicks.</summary>
    public const int EdgeSamples = SampleRate * 2 / 1000;

    /// <summary>Silent: under -60 dBFS, that is under 32.77 in 16-bit steps (full scale 32767, the tool's).</summary>
    public const double SilentDb = -60;

    /// <summary>
    /// The clicks set (G27, A106): a set of the tool that is NOT a game, named here by
    /// name. Its outputs are the app's, under <see cref="ClickFolder"/> only.
    /// </summary>
    public const string ClickSet = "clicks";

    /// <summary>Where the clicks set writes, and nothing else does: the app embeds them (A106).</summary>
    public const string ClickFolder = "Assets/Clicks";

    /// <summary>
    /// R8's length for a click, 15 to 80 ms: never longer than <c>ClickSound.MinGap</c>
    /// (80 ms, A99), so two clicks never overlap; 662 samples is ceil(0.015 * 44,100),
    /// the shortest take build.js renders.
    /// </summary>
    public const int MinClickSamples = 662, MaxClickSamples = SampleRate * 80 / 1000;

    /// <summary>R8's bounds for a click's level: <c>arm</c>'s -16 the loudest, <c>back</c>'s -21 the quietest, in -24 to -14.</summary>
    public const double MinClickLevel = -24, MaxClickLevel = -14;

    /// <summary>A set's bounds and the words its lines use: R4's for a game, R8's for the clicks.</summary>
    private sealed record Bounds(int MinSamples, int MaxSamples, string LengthWhy, double MinLevel, double MaxLevel, string LevelRule, string Kind);

    private static readonly Bounds GameBounds = new(1, MaxSamples, "GameSound.MaxSamples, 2 s", MinLevel, MaxLevel, "R4", "a game's");
    private static readonly Bounds ClickBounds = new(MinClickSamples, MaxClickSamples, "R8: 15 to 80 ms, ClickSound.MinGap", MinClickLevel, MaxClickLevel, "R8", "a click's");

    // The projects under Games/ that are not games: their Sound/ folder, if one
    // exists, is not a game's (the tests' own is the one that holds this file).
    private static readonly string[] NotGames =
    {
        "PandoraOverlay.Games.Contract",
        "PandoraOverlay.Games.Engine",
        "PandoraOverlay.Games.Tests",
    };

    /// <summary>One cue as its manifest states it: its level (dBFS of the peak) and its length (ms, rounded).</summary>
    internal sealed record Cue(double Level, int Ms);

    /// <summary>One set's manifest (a game's, or the clicks set's): its sources and outputs, repo-relative with forward slashes, to their hashes, and its cues.</summary>
    internal sealed record Manifest(string Game, string Path, Dictionary<string, string> Sources, Dictionary<string, string> Outputs, Dictionary<string, Cue> Cues)
    {
        /// <summary>The clicks set's manifest, not a game's: its own place and R8's bounds (A106).</summary>
        public bool IsClicks => Game == ClickSet;
    }

    /// <summary>Up from the test's bin folder to the folder holding PandoraOverlay.sln: a worktree finds its own.</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "PandoraOverlay.sln"))) return dir.FullName;
        throw new InvalidOperationException("The repo root (PandoraOverlay.sln) is not above the test's folder.");
    }

    public static List<string> All(string root) =>
        HashProblems(root).Concat(SourceProblems(root)).Concat(OutputProblems(root)).Concat(WavProblems(root)).Concat(CueProblems(root)).ToList();

    /// <summary>Every <c>tools/games-sound/*/manifest.json</c>, read. A manifest that can't be read is a problem.</summary>
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
                var top = document.RootElement;
                var cues = top.GetProperty("cues").EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => new Cue(p.Value.GetProperty("level").GetDouble(), p.Value.GetProperty("ms").GetInt32()),
                    StringComparer.Ordinal);
                found.Add(new Manifest(game, $"{ToolFolder}/{game}/manifest.json",
                    Hashes(top.GetProperty("sources")), Hashes(top.GetProperty("outputs")), cues));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                problems.Add($"{ToolFolder}/{game}/manifest.json can't be read ({e.GetType().Name}): {Cure(game)}");
            }
        }
        return found;
    }

    /// <summary>Rule 1: every file a manifest lists exists and holds the listed hash.</summary>
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
    /// Rule 2: every .js under the tool (out/ aside) is a source: in EVERY manifest
    /// when it sits directly in tools/games-sound (shared), in its own game's when it
    /// is under that game's folder.
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
    /// Rule 3: every file under a game's <c>Games/&lt;project&gt;/Sound/</c> is an
    /// output of a manifest, and every output is such a file (nothing else is
    /// written by a build). The Contract, the Engine and the tests are not games.
    /// Its twin for the clicks set (A106): every file under <c>Assets/Clicks/</c> is
    /// an output of the clicks set's manifest, and every output of that manifest is
    /// under <c>Assets/Clicks/</c>. A game's output there is still not under a game's
    /// Sound folder, and a click's under a game's <c>Sound/</c> is not under
    /// <c>Assets/Clicks/</c>.
    /// </summary>
    public static List<string> OutputProblems(string root)
    {
        var problems = new List<string>();
        var outputs = Manifests(root, problems).SelectMany(m => m.Outputs.Keys.Select(o => (Output: o, m.Game, m.IsClicks))).ToList();
        var sound = SoundFiles(root);
        foreach (string file in sound)
            if (!outputs.Any(o => o.Output == file))
                problems.Add($"{file} is not an output of any manifest: a sound is only ever written by a build (node {ToolFolder}/<game>/build.js)");
        foreach (string file in ClickFiles(root))
            if (!outputs.Any(o => o.IsClicks && o.Output == file))
                problems.Add($"{file} is not an output of the clicks set's manifest: a click is only ever written by its build ({Cure(ClickSet)})");
        foreach (var (output, game, isClicks) in outputs)
        {
            if (isClicks)
            {
                if (!IsClickPath(output))
                    problems.Add($"{output}, an output of {ToolFolder}/{game}/manifest.json, is not under {ClickFolder}/ (the clicks are the app's, A106)");
            }
            else if (!sound.Contains(output) && !IsSoundPath(output))
                problems.Add($"{output}, an output of {ToolFolder}/{game}/manifest.json, is not under a game's Sound folder (Games/<project>/Sound/)");
        }
        return problems;
    }

    /// <summary>
    /// Rule 4: every output that exists is a WAV in the one format (RIFF/WAVE, a
    /// 16-byte fmt chunk: PCM, 1 channel, 44,100 Hz, 16 bits; then data, and no
    /// other chunk), 1 to <see cref="MaxSamples"/> samples long, its first sample
    /// and its last 2 ms under -60 dBFS (no click) (A87: the rise between is R3's
    /// attack, which build.js refuses to break), no sample at full scale, its
    /// peak within 0.5 dB of its cue's level, that level within -24 to -5 dBFS
    /// (R4), and the cue's <c>ms</c> its length, rounded. The clicks set's WAVs the
    /// same, with R8's bounds instead: <see cref="MinClickSamples"/> to
    /// <see cref="MaxClickSamples"/> samples (15 to 80 ms) and a level within -24 to
    /// -14 dBFS, this file's own constants, never the manifest's.
    /// </summary>
    public static List<string> WavProblems(string root)
    {
        var problems = new List<string>();
        foreach (var manifest in Manifests(root, problems))
        {
            var bounds = manifest.IsClicks ? ClickBounds : GameBounds;
            foreach (string output in manifest.Outputs.Keys.OrderBy(o => o, StringComparer.Ordinal))
            {
                string full = System.IO.Path.Combine(root, output);
                if (!File.Exists(full)) continue; // rule 1 names it
                short[] samples;
                try
                {
                    samples = ReadWav(File.ReadAllBytes(full));
                }
                catch (InvalidDataException e)
                {
                    problems.Add($"{output} is not {bounds.Kind} WAV: {e.Message}");
                    continue;
                }
                problems.AddRange(SamplesProblems(output, samples, manifest.Cues.GetValueOrDefault(CueOf(output)), bounds));
            }
        }
        return problems;
    }

    private static IEnumerable<string> SamplesProblems(string output, short[] samples, Cue? cue, Bounds bounds)
    {
        if (samples.Length < bounds.MinSamples || samples.Length > bounds.MaxSamples)
        {
            yield return $"{output} has {samples.Length} samples: {bounds.MinSamples} to {bounds.MaxSamples} ({bounds.LengthWhy})";
            yield break;
        }
        double silent = Math.Pow(10, SilentDb / 20) * short.MaxValue;
        if (Math.Abs((int)samples[0]) >= silent)
            yield return $"{output}: its first sample is not under {SilentDb} dBFS (a click at the start)";
        if (samples.Skip(Math.Max(0, samples.Length - EdgeSamples)).Any(s => Math.Abs((int)s) >= silent))
            yield return $"{output}: its last 2 ms are not under {SilentDb} dBFS (a click at the end)";
        if (samples.Any(s => s == short.MaxValue || s == -short.MaxValue || s == short.MinValue))
            yield return $"{output}: a sample at full scale (clipped)";

        int peak = samples.Max(s => Math.Abs((int)s));
        double peakDb = 20 * Math.Log10(Math.Max(peak, 1) / (double)short.MaxValue);
        int ms = (int)Math.Round(samples.Length * 1000.0 / SampleRate);
        if (cue is null) yield break; // rule 5 names it
        // Numbers in the invariant culture, so a line reads the same on every PC.
        if (cue.Level < bounds.MinLevel || cue.Level > bounds.MaxLevel)
            yield return string.Create(CultureInfo.InvariantCulture, $"{output}: its cue's level {cue.Level} is outside {bounds.MinLevel} to {bounds.MaxLevel} dBFS ({bounds.LevelRule})");
        if (Math.Abs(peakDb - cue.Level) > LevelTolerance)
            yield return string.Create(CultureInfo.InvariantCulture, $"{output}: its peak is {peakDb:0.00} dBFS, its cue's level {cue.Level} (more than {LevelTolerance} dB apart)");
        if (cue.Ms != ms)
            yield return $"{output} lasts {ms} ms, the manifest says {cue.Ms}";
    }

    /// <summary>Rule 5: every cue a manifest names has its WAV among its outputs, and every output its cue.</summary>
    public static List<string> CueProblems(string root)
    {
        var problems = new List<string>();
        foreach (var manifest in Manifests(root, problems))
        {
            var byCue = manifest.Outputs.Keys.ToLookup(CueOf, StringComparer.Ordinal);
            foreach (string cue in manifest.Cues.Keys.OrderBy(c => c, StringComparer.Ordinal))
                if (!byCue.Contains(cue))
                    problems.Add($"the cue '{cue}' in {manifest.Path} has no WAV ({(manifest.IsClicks ? ClickFolder : "Sound")}/{cue}.wav) among its outputs: {Cure(manifest.Game)}");
            foreach (string output in manifest.Outputs.Keys.OrderBy(o => o, StringComparer.Ordinal))
                if (!manifest.Cues.ContainsKey(CueOf(output)))
                    problems.Add($"{output}, an output of {manifest.Path}, has no cue in it: {Cure(manifest.Game)}");
        }
        return problems;
    }

    /// <summary>
    /// The test's own WAV reader: the one format the tool writes and the engine
    /// takes (A78), every other field refused by name. Little-endian 16-bit samples.
    /// </summary>
    public static short[] ReadWav(byte[] bytes)
    {
        static string Tag(byte[] b, int at) => Encoding.ASCII.GetString(b, at, 4);
        if (bytes.Length < 44) throw new InvalidDataException("shorter than a WAV header");
        if (Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE") throw new InvalidDataException("not RIFF/WAVE");
        if (BitConverter.ToUInt32(bytes, 4) != bytes.Length - 8) throw new InvalidDataException("the RIFF size is not the file's");
        if (Tag(bytes, 12) != "fmt " || BitConverter.ToUInt32(bytes, 16) != 16) throw new InvalidDataException("the first chunk is not a 16-byte fmt");
        if (BitConverter.ToUInt16(bytes, 20) != 1) throw new InvalidDataException("not PCM");
        if (BitConverter.ToUInt16(bytes, 22) != 1) throw new InvalidDataException("not 1 channel");
        if (BitConverter.ToUInt32(bytes, 24) != SampleRate) throw new InvalidDataException("not 44,100 Hz");
        if (BitConverter.ToUInt16(bytes, 34) != 16) throw new InvalidDataException("not 16 bits");
        if (BitConverter.ToUInt32(bytes, 28) != SampleRate * 2 || BitConverter.ToUInt16(bytes, 32) != 2)
            throw new InvalidDataException("its byte rate or block is not 16-bit mono's");
        if (Tag(bytes, 36) != "data") throw new InvalidDataException("a chunk other than fmt and data");
        uint data = BitConverter.ToUInt32(bytes, 40);
        if (44L + data != bytes.Length || data % 2 != 0) throw new InvalidDataException("a chunk other than fmt and data, or a data size that is not the rest of the file");
        var samples = new short[data / 2];
        for (int k = 0; k < samples.Length; k++) samples[k] = BitConverter.ToInt16(bytes, 44 + 2 * k);
        return samples;
    }

    /// <summary>The cue an output is: its file's name without <c>.wav</c>.</summary>
    private static string CueOf(string output) => System.IO.Path.GetFileNameWithoutExtension(output);

    /// <summary>Every file under <c>Games/&lt;project&gt;/Sound/</c> of a game project, repo-relative.</summary>
    private static HashSet<string> SoundFiles(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        string games = System.IO.Path.Combine(root, "Games");
        if (!Directory.Exists(games)) return files;
        foreach (string project in Directory.GetDirectories(games))
        {
            string sound = System.IO.Path.Combine(project, "Sound");
            if (NotGames.Contains(System.IO.Path.GetFileName(project)) || !Directory.Exists(sound)) continue;
            foreach (string file in Directory.EnumerateFiles(sound, "*", SearchOption.AllDirectories))
                files.Add(Relative(root, file));
        }
        return files;
    }

    /// <summary>Every file under <c>Assets/Clicks/</c>, repo-relative: the clicks set's, and only its (A106).</summary>
    private static HashSet<string> ClickFiles(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        string clicks = System.IO.Path.Combine(root, ClickFolder);
        if (!Directory.Exists(clicks)) return files;
        foreach (string file in Directory.EnumerateFiles(clicks, "*", SearchOption.AllDirectories))
            files.Add(Relative(root, file));
        return files;
    }

    private static bool IsClickPath(string rel) => rel.StartsWith(ClickFolder + "/", StringComparison.Ordinal) && rel.Length > ClickFolder.Length + 1;

    private static bool IsSoundPath(string rel)
    {
        string[] parts = rel.Split('/');
        return parts.Length >= 4 && parts[0] == "Games" && parts[2] == "Sound" && !NotGames.Contains(parts[1]);
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
/// A small repo in a temporary folder, in step: the tool's shared sources, one game
/// ("hatchling") with its sources, two cues' WAVs under that game's Sound folder
/// (written by this file: a tone faded in and out, peak-normalised to its level),
/// and the manifest its build would write. With <c>clicks: true</c> it also holds
/// the clicks set (A106): its sources under <c>tools/games-sound/clicks/</c>, two
/// cues' WAVs under <c>Assets/Clicks/</c> (35 ms each, at R8's levels) and its
/// manifest. A test breaks one thing and runs one rule. (Named apart from the
/// art's <c>FakeRepo</c>, which this namespace holds.)
/// </summary>
internal sealed class FakeSoundRepo : IDisposable
{
    public const string Sound = "Games/PandoraOverlay.Games.Hatchling/Sound/";
    public const string Clicks = SoundInStep.ClickFolder + "/";

    /// <summary>A stand-in click's length: 1,544 samples, 35 ms, `press`'s own.</summary>
    public const int ClickLength = 1544;

    private static readonly string[] Shared = { "synth.js", "wav.js", "manifest.js", "board.js" };
    private static readonly Dictionary<string, double> Levels = new() { ["bite"] = -5, ["step"] = -16 };
    private static readonly Dictionary<string, double> ClickLevels = new() { ["press"] = -18, ["back"] = -21 };

    public FakeSoundRepo(bool clicks = false)
    {
        Root = Directory.CreateTempSubdirectory("games-sound-").FullName;
        foreach (string name in Shared) Write($"tools/games-sound/{name}", $"// {name}\n");
        Write("tools/games-sound/hatchling/build.js", "// build\n");
        Write("tools/games-sound/hatchling/cues.js", "// cues\n");
        foreach (var (cue, level) in Levels) WriteBytes($"{Sound}{cue}.wav", Wav(Tone(level)));
        WriteManifest();
        if (!clicks) return;
        foreach (string name in new[] { "build.js", "cues.js", "board.js" }) Write($"tools/games-sound/{SoundInStep.ClickSet}/{name}", $"// {name}\n");
        foreach (var (cue, level) in ClickLevels) WriteBytes($"{Clicks}{cue}.wav", Wav(ClickTone(level)));
        WriteClickManifest();
    }

    public string Root { get; }

    public string Path(string rel) => System.IO.Path.Combine(Root, rel);

    public void Write(string rel, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(rel))!);
        File.WriteAllText(Path(rel), text);
    }

    public void WriteBytes(string rel, byte[] bytes)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(rel))!);
        File.WriteAllBytes(Path(rel), bytes);
    }

    /// <summary>
    /// The manifest as the build would write it from the files now on disk: every
    /// .js source (out/ aside), every file under the game's Sound folder (and
    /// <paramref name="extraOutput"/>), and the cues of <paramref name="levels"/>
    /// (bite -5 and step -16 by default), each cue's ms its WAV's length
    /// (plus <paramref name="msOffset"/>) or 100 when it has none.
    /// </summary>
    public void WriteManifest(string? extraOutput = null, Dictionary<string, double>? levels = null, int msOffset = 0) =>
        WriteSetManifest("hatchling", Sound, levels ?? Levels, extraOutput, msOffset);

    /// <summary>The clicks set's manifest, as its build would write it: <see cref="WriteManifest"/>'s twin over <c>Assets/Clicks/</c> (press -18 and back -21 by default).</summary>
    public void WriteClickManifest(string? extraOutput = null, Dictionary<string, double>? levels = null) =>
        WriteSetManifest(SoundInStep.ClickSet, Clicks, levels ?? ClickLevels, extraOutput, 0);

    private void WriteSetManifest(string set, string folder, Dictionary<string, double> levels, string? extraOutput, int msOffset)
    {
        var sources = Directory.EnumerateFiles(Path("tools/games-sound"), "*.js", SearchOption.AllDirectories)
            .Select(f => SoundInStep.Relative(Root, f))
            .Where(f => !f.StartsWith("tools/games-sound/out/", StringComparison.Ordinal))
            .Where(f => f.Count(c => c == '/') == 2 || f.StartsWith($"tools/games-sound/{set}/", StringComparison.Ordinal));
        var outputs = Directory.EnumerateFiles(Path(folder), "*").Select(f => SoundInStep.Relative(Root, f))
            .Concat(extraOutput is null ? Array.Empty<string>() : new[] { extraOutput });
        string Entries(IEnumerable<string> files) =>
            string.Join(",", files.Select(f => $"\"{f}\": \"{SoundInStep.Sha256(Path(f))}\""));
        string Cue(string name, double level)
        {
            string wav = Path($"{folder}{name}.wav");
            int ms = 100;
            if (File.Exists(wav))
            {
                try { ms = (int)Math.Round(SoundInStep.ReadWav(File.ReadAllBytes(wav)).Length * 1000.0 / SoundInStep.SampleRate); }
                catch (InvalidDataException) { }
            }
            return $"\"{name}\": {{ \"level\": {level.ToString(CultureInfo.InvariantCulture)}, \"ms\": {ms + msOffset} }}";
        }
        string cues = string.Join(",", levels.Select(p => Cue(p.Key, p.Value)));
        Write($"tools/games-sound/{set}/manifest.json",
            $"{{ \"sources\": {{ {Entries(sources)} }}, \"outputs\": {{ {Entries(outputs)} }}, \"cues\": {{ {cues} }} }}");
    }

    /// <summary>
    /// A click's stand-in: <see cref="Tone"/> over all but its last 2 ms and a
    /// sample (89), then silence, so even the shortest click R8 allows (662 samples,
    /// whose 10% tail would be under 2 ms) has a silent end. 35 ms unless told.
    /// </summary>
    public static short[] ClickTone(double level, int length = ClickLength)
    {
        int quiet = Math.Min(length, SoundInStep.EdgeSamples + 1);
        return Tone(level, length - quiet).Concat(new short[quiet]).ToArray();
    }

    /// <summary>
    /// A cue's stand-in sound: 440 Hz under a raised-cosine rise and fall over its
    /// first 90%, then silence, peak-normalised to <paramref name="level"/> dBFS and
    /// rounded to 16 bits as the tool does. 4,410 samples (0.1 s) unless told.
    /// </summary>
    public static short[] Tone(double level, int length = 4410)
    {
        var x = new double[length];
        int sounding = length * 9 / 10;
        for (int k = 0; k < sounding; k++)
            x[k] = Math.Sin(2 * Math.PI * 440 * k / SoundInStep.SampleRate) * Math.Pow(Math.Sin(Math.PI * k / sounding), 2);
        double peak = x.Max(Math.Abs), target = Math.Pow(10, level / 20);
        return x.Select(v => (short)Math.Clamp(Math.Round(v * target / peak * short.MaxValue), short.MinValue, short.MaxValue)).ToArray();
    }

    /// <summary>The WAV the tool writes: RIFF/WAVE, a 16-byte fmt (PCM, 1 channel, 44,100 Hz, 16 bits), data.</summary>
    public static byte[] Wav(short[] samples)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + samples.Length * 2);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SoundInStep.SampleRate);
        w.Write(SoundInStep.SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(samples.Length * 2);
        foreach (short s in samples) w.Write(s);
        w.Flush();
        return stream.ToArray();
    }

    /// <summary>A WAV with one thing wrong, by name (<see cref="SoundInStepTests.WrongFormats"/>).</summary>
    public static byte[] Broken(byte[] wav, string wrong)
    {
        var b = (byte[])wav.Clone();
        switch (wrong)
        {
            case "riff": b[0] = (byte)'X'; break;
            case "pcm": b[20] = 3; break;                                  // IEEE float
            case "stereo": b[22] = 2; break;
            case "rate": BitConverter.GetBytes(48_000).CopyTo(b, 24); break;
            case "bits": b[34] = 8; break;
            case "chunk":
                // A LIST chunk between fmt and data, the RIFF size kept true.
                var list = Encoding.ASCII.GetBytes("LIST").Concat(BitConverter.GetBytes(4)).Concat(Encoding.ASCII.GetBytes("INFO")).ToArray();
                b = b.Take(36).Concat(list).Concat(b.Skip(36)).ToArray();
                BitConverter.GetBytes(b.Length - 8).CopyTo(b, 4);
                break;
            case "short": b = b.Take(40).ToArray(); break;
            default: throw new ArgumentException(wrong);
        }
        return b;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
