using System.IO;
using System.Text.Json;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Every cue has its sound (H95, A78): each <see cref="Cue"/>'s WAV is embedded in the
/// game's assembly as <c>Sound/&lt;file&gt;.wav</c> and read by the engine's one reader;
/// every embedded WAV is a cue's; the 29 files are the sound tool's own names (its
/// manifest, read from the repo); and none is louder than the bite, the loudest of the
/// set (R4).
/// </summary>
[Collection(LoopTimers.Name)]
public class CueFilesTests
{
    private static readonly System.Reflection.Assembly Game = typeof(HatchlingGame).Assembly;

    [Fact]
    public void TheTableHasEveryCueOnceInItsOrder()
    {
        var cues = Enum.GetValues<Cue>();
        Assert.Equal(29, cues.Length);
        Assert.Equal(cues, Cues.All.Select(c => c.Cue));
        Assert.Equal(29, Cues.All.Select(c => c.File).Distinct(StringComparer.Ordinal).Count());
        // A two-word cue with a hyphen, the rest their names in lower case (H95).
        foreach (var (cue, file) in Cues.All)
        {
            var kebab = string.Concat(cue.ToString().Select((ch, i) => char.IsUpper(ch) && i > 0 ? "-" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
            Assert.Equal(kebab, file);
            Assert.Equal(file, Cues.FileOf(cue));
        }
    }

    [Fact]
    public void EveryCuesWavIsEmbeddedAndReadByTheSoundBank()
    {
        var resources = Game.GetManifestResourceNames();
        var bank = SoundBank.Load(Game);
        var book = SoundBook.Load(Game);
        foreach (var (cue, file) in Cues.All)
        {
            Assert.Contains($"Sound/{file}.wav", resources);
            Assert.True(bank.TryGet(file, out var sound), $"the bank has no {file}");
            Assert.True(sound!.Samples.Span.SequenceEqual(book[cue].Samples.Span), $"{cue}'s sound is not {file}'s");
            Assert.Same(book[cue], book[cue]);   // one object a cue: the host keys its copy and its 40 ms by it (A81)
            Assert.Equal(file, book[cue].Name);
            Assert.InRange(book[cue].Samples.Length, 1, GameSound.MaxSamples);
        }
    }

    [Fact]
    public void EveryEmbeddedWavIsACues()
    {
        var files = Cues.All.Select(c => c.File).ToHashSet(StringComparer.Ordinal);
        var wavs = Game.GetManifestResourceNames().Where(r => r.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(29, wavs.Count);
        foreach (var wav in wavs)
        {
            Assert.StartsWith("Sound/", wav, StringComparison.Ordinal);
            Assert.Contains(wav["Sound/".Length..^".wav".Length], files);
        }
        Assert.True(files.SetEquals(SoundBank.Load(Game).Names), "the bank's names are not the cues' files");
    }

    [Fact]
    public void TheFilesAreTheSoundToolsCues()
    {
        var manifest = Path.Combine(Repo.Root, "tools", "games-sound", "hatchling", "manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var names = document.RootElement.GetProperty("cues").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(names, Cues.All.Select(c => c.File).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void NoCueIsLouderThanTheBite()
    {
        var book = SoundBook.Load(Game);
        static int Peak(GameSound sound)
        {
            var peak = 0;
            foreach (var s in sound.Samples.Span) peak = Math.Max(peak, Math.Abs((int)s));
            return peak;
        }
        var bite = Peak(book[Cue.Bite]);
        foreach (var (cue, _) in Cues.All)
            Assert.True(Peak(book[cue]) <= bite, $"{cue} peaks at {Peak(book[cue])}, above the bite's {bite} (R4)");
    }

    [Fact]
    public void TheGameLoadsItsBookOnceAndTheViewTakesIt() => Sta.Run(() =>
    {
        var (game, _) = ViewHarness.Attached(ViewHarness.Running());
        Assert.Same(game.Sounds(), game.Sounds());
        using var view = ViewHarness.ViewOf(game);
        Assert.Same(game.Sounds(), game.Sounds());
    });
}
