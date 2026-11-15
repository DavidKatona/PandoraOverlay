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

    /// <summary>One game's manifest: its sources and outputs, repo-relative with forward slashes, to their hashes, and its cues.</summary>
    internal sealed record Manifest(string Game, string Path, Dictionary<string, string> Sources, Dictionary<string, string> Outputs, Dictionary<string, Cue> Cues);

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
    /// </summary>
    public static List<string> OutputProblems(string root)
    {
        var problems = new List<string>();
        var outputs = Manifests(root, problems).SelectMany(m => m.Outputs.Keys.Select(o => (Output: o, m.Game))).ToList();
        var sound = SoundFiles(root);
        foreach (string file in sound)
            if (!outputs.Any(o => o.Output == file))
                problems.Add($"{file} is not an output of any manifest: a sound is only ever written by a build (node {ToolFolder}/<game>/build.js)");
        foreach (var (output, game) in outputs)
            if (!sound.Contains(output) && !IsSoundPath(output))
                problems.Add($"{output}, an output of {ToolFolder}/{game}/manifest.json, is not under a game's Sound folder (Games/<project>/Sound/)");
        return problems;
    }

    /// <summary>
    /// Rule 4: every output that exists is a WAV in the one format (RIFF/WAVE, a
    /// 16-byte fmt chunk: PCM, 1 channel, 44,100 Hz, 16 bits; then data, and no
    /// other chunk), 1 to <see cref="MaxSamples"/> samples long, its first sample
    /// and its last 2 ms under -60 dBFS (no click), no sample at full scale, its
    /// peak within 0.5 dB of its cue's level, that level within -24 to -5 dBFS
    /// (R4), and the cue's <c>ms</c> its length, rounded.
    /// </summary>
    public static List<string> WavProblems(string root)
    {
        var problems = new List<string>();
        foreach (var manifest in Manifests(root, problems))
        {
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
                    problems.Add($"{output} is not a game's WAV: {e.Message}");
                    continue;
                }
                problems.AddRange(SamplesProblems(output, samples, manifest.Cues.GetValueOrDefault(CueOf(output))));
            }
        }
        return problems;
    }

    private static IEnumerable<string> SamplesProblems(string output, short[] samples, Cue? cue)
    {
        if (samples.Length < 1 || samples.Length > MaxSamples)
        {
            yield return $"{output} has {samples.Length} samples: 1 to {MaxSamples} (GameSound.MaxSamples, 2 s)";
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
        if (cue.Level < MinLevel || cue.Level > MaxLevel)
            yield return string.Create(CultureInfo.InvariantCulture, $"{output}: its cue's level {cue.Level} is outside {MinLevel} to {MaxLevel} dBFS (R4)");
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
                    problems.Add($"the cue '{cue}' in {manifest.Path} has no WAV (Sound/{cue}.wav) among its outputs: {Cure(manifest.Game)}");
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
/// and the manifest its build would write. A test breaks one thing and runs one
/// rule. (Named apart from the art's <c>FakeRepo</c>, which this namespace holds.)
/// </summary>
internal sealed class FakeSoundRepo : IDisposable
{
    public const string Sound = "Games/PandoraOverlay.Games.Hatchling/Sound/";
    private static readonly string[] Shared = { "synth.js", "wav.js", "manifest.js", "board.js" };
    private static readonly Dictionary<string, double> Levels = new() { ["bite"] = -5, ["step"] = -16 };

    public FakeSoundRepo()
    {
        Root = Directory.CreateTempSubdirectory("games-sound-").FullName;
        foreach (string name in Shared) Write($"tools/games-sound/{name}", $"// {name}\n");
        Write("tools/games-sound/hatchling/build.js", "// build\n");
        Write("tools/games-sound/hatchling/cues.js", "// cues\n");
        foreach (var (cue, level) in Levels) WriteBytes($"{Sound}{cue}.wav", Wav(Tone(level)));
        WriteManifest();
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
    public void WriteManifest(string? extraOutput = null, Dictionary<string, double>? levels = null, int msOffset = 0)
    {
        var sources = Directory.EnumerateFiles(Path("tools/games-sound"), "*.js", SearchOption.AllDirectories)
            .Select(f => SoundInStep.Relative(Root, f))
            .Where(f => !f.StartsWith("tools/games-sound/out/", StringComparison.Ordinal))
            .Where(f => f.Count(c => c == '/') == 2 || f.StartsWith("tools/games-sound/hatchling/", StringComparison.Ordinal));
        var outputs = Directory.EnumerateFiles(Path(Sound), "*").Select(f => SoundInStep.Relative(Root, f))
            .Concat(extraOutput is null ? Array.Empty<string>() : new[] { extraOutput });
        string Entries(IEnumerable<string> files) =>
            string.Join(",", files.Select(f => $"\"{f}\": \"{SoundInStep.Sha256(Path(f))}\""));
        string Cue(string name, double level)
        {
            string wav = Path($"{Sound}{name}.wav");
            int ms = 100;
            if (File.Exists(wav))
            {
                try { ms = (int)Math.Round(SoundInStep.ReadWav(File.ReadAllBytes(wav)).Length * 1000.0 / SoundInStep.SampleRate); }
                catch (InvalidDataException) { }
            }
            return $"\"{name}\": {{ \"level\": {level.ToString(CultureInfo.InvariantCulture)}, \"ms\": {ms + msOffset} }}";
        }
        string cues = string.Join(",", (levels ?? Levels).Select(p => Cue(p.Key, p.Value)));
        Write("tools/games-sound/hatchling/manifest.json",
            $"{{ \"sources\": {{ {Entries(sources)} }}, \"outputs\": {{ {Entries(outputs)} }}, \"cues\": {{ {cues} }} }}");
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
