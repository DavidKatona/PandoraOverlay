using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The games' sound gate (<see cref="GamesSound"/>; A79, A81, A82) over a
/// speaker that records (<see cref="FakeSpeaker"/>) and the bench's clock,
/// moved by hand: a sound reaches the speaker only from the current instance
/// of the game on screen, not paused, not muted; every change that leaves it
/// unheard stops what plays; the limits; the refusals; the gate's own copy;
/// the five steps; the mute and the steps writing the config and playing the
/// preview. The stage is told here as the window tells it
/// (<see cref="GamesWindowTests"/> holds the window to telling it).
/// </summary>
public class GamesSoundTests
{
    private static GameSound Sound(string name, int samples = 4410, short value = 1000) =>
        new(name, Enumerable.Repeat(value, samples).ToArray());

    /// <summary>A bench with these games (each a fake), its gate and its speaker.</summary>
    private static GamesBench Bench(int games = 1) =>
        new(Enumerable.Range(1, games).Select(n => new FakeGame($"game-{n}")).ToArray());

    private static GamesSound Gate(GamesBench bench) => bench.Host.Sound;

    /// <summary>What the game's own instance hands its host, as a game's view would.</summary>
    private static void PlayFrom(GamesBench bench, int game, GameSound sound) => bench.Games[game].Host!.Sound.Play(sound);

    private static double Dbfs(short[] samples) => 20 * Math.Log10(samples.Max(s => Math.Abs((int)s)) / (double)short.MaxValue);

    // ---- Only the game on screen, not paused, not muted (A79) ------------------------------

    [Fact]
    public void APlayReachesTheSpeakerFromTheGameOnScreen()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        PlayFrom(bench, 0, Sound("bite", samples: 3, value: 7));

        var (samples, gain) = Assert.Single(bench.Speaker.Played);
        Assert.Equal(new short[] { 7, 7, 7 }, samples);
        Assert.Equal(GamesSound.GainOf(GamesSound.DefaultStep), gain);
    }

    [Fact]
    public void AnotherGamesSoundIsDropped()
    {
        using var bench = Bench(games: 2);
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        PlayFrom(bench, 1, Sound("bite"));

        Assert.Empty(bench.Speaker.Played);
    }

    [Fact]
    public void WithNoGameOnScreenEverySoundIsDropped()
    {
        using var bench = Bench();
        PlayFrom(bench, 0, Sound("before any stage"));
        Gate(bench).Stage(null, paused: false); // the grid
        PlayFrom(bench, 0, Sound("on the grid"));

        Assert.Empty(bench.Speaker.Played);
    }

    [Fact]
    public void APausedGameIsSilent()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: true);

        PlayFrom(bench, 0, Sound("bite"));

        Assert.Empty(bench.Speaker.Played);
    }

    [Fact]
    public void AMutedGameIsSilent()
    {
        using var bench = Bench();
        bench.Config.GamesSoundMuted = true;
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        PlayFrom(bench, 0, Sound("bite"));

        Assert.Empty(bench.Speaker.Played);
    }

    /// <summary>After the window closed (it tells the gate none): a game's sound from a reading is silence (A79, A85).</summary>
    [Fact]
    public void AfterTheWindowClosedEverySoundIsDropped()
    {
        using var bench = Bench();
        bench.Games[0].WhenRead = (host, _) => host.Sound.Play(Sound("from a reading"));
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        Gate(bench).Stage(null, paused: false); // Closed

        bench.Host.Feed(new MyLocationResponse(false, null));
        PlayFrom(bench, 0, Sound("bite"));

        Assert.Equal(1, bench.Games[0].Readings);
        Assert.Empty(bench.Speaker.Played);
    }

    /// <summary>
    /// A detached instance (after "Start it again") reaches nothing, through its
    /// own door or straight at the gate; the instance that replaced it is heard.
    /// </summary>
    [Fact]
    public void ADetachedInstanceReachesNothing()
    {
        using var bench = Bench();
        var game = bench.Host.Games[0];
        var old = game.Host!;
        var oldDoor = old.Sound;
        bench.Host.Fault(game, new InvalidOperationException("made up"));
        Assert.True(bench.Host.StartAgain(game));
        Assert.NotSame(old, game.Host);
        Gate(bench).Stage(game, paused: false);

        oldDoor.Play(Sound("from the old door"));
        Gate(bench).Play(old, Sound("straight at the gate"));
        Assert.Empty(bench.Speaker.Played);

        PlayFrom(bench, 0, Sound("from the new instance"));
        Assert.Single(bench.Speaker.Played);
    }

    /// <summary>A stopped game is heard no more even before the window answers its fault: it is not Ready.</summary>
    [Fact]
    public void AStoppedGameIsSilent()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        bench.Host.Fault(bench.Host.Games[0], new InvalidOperationException("made up"));

        PlayFrom(bench, 0, Sound("bite"));

        Assert.Empty(bench.Speaker.Played);
    }

    [Fact]
    public void ANullSoundIsDroppedSilently()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        bench.Games[0].Host!.Sound.Play(null!);

        Assert.Empty(bench.Speaker.Played);
        Assert.Empty(bench.Logs["game-1"].Warnings);
    }

    // ---- A stop at every change that leaves the game unheard (A79) ---------------------------

    [Fact]
    public void EveryChangeThatLeavesTheGameUnheardStopsEverySound()
    {
        using var bench = Bench(games: 2);
        var (first, second) = (bench.Host.Games[0], bench.Host.Games[1]);
        var gate = Gate(bench);
        gate.Stage(first, paused: false);
        var stops = bench.Speaker.Stops;

        gate.Stage(first, paused: false); // the same instance heard again: its sounds keep playing
        Assert.Equal(stops, bench.Speaker.Stops);

        gate.Stage(first, paused: true); // the warning up
        Assert.Equal(++stops, bench.Speaker.Stops);

        gate.Stage(first, paused: false); // "Keep playing"
        stops = bench.Speaker.Stops;
        gate.Stage(second, paused: false); // another game
        Assert.Equal(++stops, bench.Speaker.Stops);

        gate.Stage(null, paused: false); // the grid, or the window closed
        Assert.Equal(++stops, bench.Speaker.Stops);

        gate.Stage(second, paused: false);
        stops = bench.Speaker.Stops;
        gate.ToggleMute(); // muted
        Assert.Equal(++stops, bench.Speaker.Stops);
    }

    /// <summary>A stop clears the gate's count too: what it stopped no longer takes a voice.</summary>
    [Fact]
    public void AStopFreesEveryVoice()
    {
        using var bench = Bench();
        var game = bench.Host.Games[0];
        Gate(bench).Stage(game, paused: false);
        for (var i = 0; i < GamesSound.MaxVoices; i++) PlayFrom(bench, 0, Sound($"s{i}"));

        Gate(bench).Stage(game, paused: true);
        Gate(bench).Stage(game, paused: false);
        PlayFrom(bench, 0, Sound("after the stop"));

        Assert.Equal(GamesSound.MaxVoices + 1, bench.Speaker.Played.Count);
    }

    // ---- The limits (A81) ------------------------------------------------------------------

    [Fact]
    public void SixAtOnceThenTheSeventhIsDroppedUntilOneHasEnded()
    {
        Assert.Equal(6, GamesSound.MaxVoices);
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        var tenth = TimeSpan.FromSeconds(0.1);
        for (var i = 0; i < 6; i++) PlayFrom(bench, 0, Sound($"s{i}", samples: 4410)); // 0.1 s each
        var seventh = Sound("seventh", samples: 4410);

        PlayFrom(bench, 0, seventh);
        Assert.Equal(6, bench.Speaker.Played.Count);

        bench.Clock.Advance(tenth / 2); // still sounding
        PlayFrom(bench, 0, seventh);
        Assert.Equal(6, bench.Speaker.Played.Count);

        bench.Clock.Advance(tenth / 2); // the six have ended
        PlayFrom(bench, 0, seventh);
        Assert.Equal(7, bench.Speaker.Played.Count);
    }

    [Fact]
    public void TheSameSoundWithinFortyMillisecondsIsDroppedAndAtFortyPlayed()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(40), GamesSound.SameSoundGap);
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        var bite = Sound("bite", samples: 10);

        PlayFrom(bench, 0, bite);
        bench.Clock.Advance(TimeSpan.FromMilliseconds(39));
        PlayFrom(bench, 0, bite);
        Assert.Single(bench.Speaker.Played);

        bench.Clock.Advance(TimeSpan.FromMilliseconds(1));
        PlayFrom(bench, 0, bite);
        Assert.Equal(2, bench.Speaker.Played.Count);
    }

    /// <summary>The gap is per sound object: another sound plays at once, and so does an equal record that is another object.</summary>
    [Fact]
    public void ADifferentSoundWithinFortyMillisecondsPlays()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        var samples = new short[] { 1, 2 };

        PlayFrom(bench, 0, Sound("bite", samples: 10));
        PlayFrom(bench, 0, Sound("step", samples: 10));
        PlayFrom(bench, 0, new GameSound("twin", samples));
        PlayFrom(bench, 0, new GameSound("twin", samples));

        Assert.Equal(4, bench.Speaker.Played.Count);
    }

    // ---- Valid, and the gate's own copy (A78) ------------------------------------------------

    [Fact]
    public void AnEmptySoundAndOneTooLongAreRefusedLoggedOnceAndNeverPassed()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        var empty = new GameSound("silence", ReadOnlyMemory<short>.Empty);
        var tooLong = new GameSound("drone", new short[GameSound.MaxSamples + 1]);

        for (var i = 0; i < 3; i++)
        {
            PlayFrom(bench, 0, empty);
            PlayFrom(bench, 0, tooLong);
            bench.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Empty(bench.Speaker.Played);
        Assert.Equal(new[]
        {
            "sound refused: 'silence' is empty or longer than 2 s",
            "sound refused: 'drone' is empty or longer than 2 s",
        }, bench.Logs["game-1"].Warnings);

        PlayFrom(bench, 0, new GameSound("longest", new short[GameSound.MaxSamples]));
        Assert.Single(bench.Speaker.Played); // exactly 2 s is a sound
    }

    /// <summary>A refused sound's name in the log: its first line, cut to 32 characters.</summary>
    [Fact]
    public void ARefusedSoundsNameIsCutForTheLog()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        PlayFrom(bench, 0, new GameSound(new string('n', 40) + "\nsecond line", ReadOnlyMemory<short>.Empty));

        Assert.Equal($"sound refused: '{new string('n', 32)}' is empty or longer than 2 s", Assert.Single(bench.Logs["game-1"].Warnings));
        Assert.Equal("ab", GamesSound.Cut("ab\r\ncd"));
        Assert.Equal("", GamesSound.Cut(null));
        Assert.Equal(new string('x', 31), GamesSound.Cut(new string('x', 31) + "\U0001F996" + "tail")); // never half a pair
    }

    /// <summary>Copied ONCE into an array of the gate's own: a change to the game's array after the first play changes nothing played.</summary>
    [Fact]
    public void TheCopyIsTheGatesOwn()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);
        var mine = new short[] { 1, 2, 3 };
        var bite = new GameSound("bite", mine);

        PlayFrom(bench, 0, bite);
        mine[0] = 99;
        bench.Clock.Advance(TimeSpan.FromSeconds(1));
        PlayFrom(bench, 0, bite);

        Assert.Equal(2, bench.Speaker.Played.Count);
        Assert.NotSame(mine, bench.Speaker.Played[0].Samples);
        Assert.Same(bench.Speaker.Played[0].Samples, bench.Speaker.Played[1].Samples); // once
        Assert.Equal(new short[] { 1, 2, 3 }, bench.Speaker.Played[1].Samples);
    }

    // ---- The level (A82) ---------------------------------------------------------------------

    [Theory]
    [InlineData(1, -24)]
    [InlineData(2, -18)]
    [InlineData(3, -12)]
    [InlineData(4, -6)]
    [InlineData(5, 0)]
    public void TheFiveStepsAreSixDecibelsApart(int step, double decibels)
    {
        Assert.Equal(decibels, 20 * Math.Log10(GamesSound.GainOf(step)), 3);
        Assert.Equal(decibels, GamesSound.DecibelsOf(step));
    }

    [Fact]
    public void TheDefaultIsTheThirdOfFiveSoundOn()
    {
        Assert.Equal((5, 3), (GamesSound.Steps, GamesSound.DefaultStep));
        var config = new OverlayConfig();
        Assert.Equal(GamesSound.DefaultStep, config.GamesSoundVolume);
        Assert.False(config.GamesSoundMuted);
    }

    /// <summary>A sound plays at its step's gain at its play.</summary>
    [Fact]
    public void ASoundPlaysAtTheStepsGain()
    {
        using var bench = Bench();
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        foreach (var step in new[] { 1, 5, 2 })
        {
            bench.Config.GamesSoundVolume = step;
            PlayFrom(bench, 0, Sound($"at {step}", samples: 10));
        }

        Assert.Equal(new[] { GamesSound.GainOf(1), GamesSound.GainOf(5), GamesSound.GainOf(2) }, bench.Speaker.Played.Select(p => p.Gain));
    }

    /// <summary>A hand-edited step out of range is READ as the nearest and never written back by the gate (A82).</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 5)]
    [InlineData(-2, 1)]
    public void AStepOutOfRangeIsReadAsTheNearestAndNotWrittenBack(int stored, int read)
    {
        using var bench = Bench();
        bench.Config.GamesSoundVolume = stored;
        Gate(bench).Stage(bench.Host.Games[0], paused: false);

        PlayFrom(bench, 0, Sound("bite", samples: 10));

        Assert.Equal(read, Gate(bench).Step);
        Assert.Equal(GamesSound.GainOf(read), Assert.Single(bench.Speaker.Played).Gain);
        Assert.Equal(stored, bench.Config.GamesSoundVolume);
        Assert.Equal(GamesSound.GainOf(read), GamesSound.GainOf(stored));
    }

    /// <summary>At an end with sound on, the step that can't move changes nothing: a value out of range stays as it is.</summary>
    [Theory]
    [InlineData(9)]
    [InlineData(5)]
    public void StepUpAtTheTopWritesNothing(int stored)
    {
        using var bench = Bench();
        bench.Config.GamesSoundVolume = stored;

        Gate(bench).StepUp();

        Assert.Equal(stored, bench.Config.GamesSoundVolume);
        Assert.Empty(bench.Speaker.Played);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(1)]
    public void StepDownAtTheBottomWritesNothing(int stored)
    {
        using var bench = Bench();
        bench.Config.GamesSoundVolume = stored;

        Gate(bench).StepDown();

        Assert.Equal(stored, bench.Config.GamesSoundVolume);
        Assert.Empty(bench.Speaker.Played);
    }

    /// <summary>A step out of range moves from where it is read: 9 is the top, so `−` writes 4.</summary>
    [Fact]
    public void AStepFromOutOfRangeMovesFromTheNearest()
    {
        using var bench = Bench();
        bench.Config.GamesSoundVolume = 9;

        Gate(bench).StepDown();

        Assert.Equal(4, bench.Config.GamesSoundVolume);
    }

    // ---- The controls and the preview (A82) -------------------------------------------------

    /// <summary>The mute: on → muted stops every sound and previews nothing; muted → on previews at the step (no stage needed: the click is on the open window).</summary>
    [Fact]
    public void TheToggleWritesTheConfigAndPreviewsOnlyWhenTurningSoundOn()
    {
        using var bench = Bench();
        var gate = Gate(bench);

        gate.ToggleMute();
        Assert.True(bench.Config.GamesSoundMuted);
        Assert.True(gate.Muted);
        Assert.Equal(new[] { "stop" }, bench.Speaker.Calls);

        gate.ToggleMute();
        Assert.False(bench.Config.GamesSoundMuted);
        Assert.Equal(new[] { "stop", "play" }, bench.Speaker.Calls);
        var (samples, gain) = bench.Speaker.Played[0];
        Assert.Equal(GamesSound.Preview.Samples.ToArray(), samples);
        Assert.Equal(GamesSound.GainOf(GamesSound.DefaultStep), gain);
    }

    [Fact]
    public void TheStepsWriteTheConfigUnmuteAndPreviewAtTheNewStep()
    {
        using var bench = Bench();
        var gate = Gate(bench);

        gate.StepDown();
        Assert.Equal((2, false), (bench.Config.GamesSoundVolume, bench.Config.GamesSoundMuted));
        bench.Clock.Advance(TimeSpan.FromSeconds(1));

        bench.Config.GamesSoundMuted = true;
        gate.StepUp();
        Assert.Equal((3, false), (bench.Config.GamesSoundVolume, bench.Config.GamesSoundMuted));
        bench.Clock.Advance(TimeSpan.FromSeconds(1));

        gate.StepUp();
        gate.StepUp();
        Assert.Equal(5, bench.Config.GamesSoundVolume);

        // Each change previewed at its new step; the second + came within the
        // preview's own 40 ms and was not heard (the same limits, A81).
        Assert.Equal(new[] { GamesSound.GainOf(2), GamesSound.GainOf(3), GamesSound.GainOf(4) }, bench.Speaker.Played.Select(p => p.Gain));
    }

    /// <summary>Muted, `−` or `+` at an end that can't move still unmutes, and previews.</summary>
    [Fact]
    public void AStepAtAnEndWhileMutedStillUnmutes()
    {
        using var bench = Bench();
        bench.Config.GamesSoundVolume = 5;
        bench.Config.GamesSoundMuted = true;

        Gate(bench).StepUp();

        Assert.Equal((5, false), (bench.Config.GamesSoundVolume, bench.Config.GamesSoundMuted));
        Assert.Equal(GamesSound.GainOf(5), Assert.Single(bench.Speaker.Played).Gain);
    }

    [Fact]
    public void NoPreviewWhileMuted()
    {
        using var bench = Bench();
        var gate = Gate(bench);

        gate.ToggleMute(); // muting: no preview
        Assert.Empty(bench.Speaker.Played);
        Assert.Equal(1, bench.Speaker.Stops);
    }

    /// <summary>The preview: 0.08 s, a peak of −14 dBFS (±0.5), silence at both ends; the gate's own, made once.</summary>
    [Fact]
    public void ThePreviewIsAShortQuietBlip()
    {
        var samples = GamesSound.Preview.Samples.ToArray();

        Assert.Equal(0.08, samples.Length / (double)GameSound.SampleRate, 4);
        Assert.InRange(Dbfs(samples), -14.5, -13.5);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);
        Assert.Equal(GamesSound.MakePreview(), samples); // made in code, the same every time
        Assert.InRange(samples.Length, 1, GameSound.MaxSamples);
    }

    // ---- The end -------------------------------------------------------------------------------

    [Fact]
    public void DisposeStopsThenDisposesTheSpeakerAndTwiceIsFine()
    {
        using var bench = Bench();
        var gate = Gate(bench);
        gate.Stage(bench.Host.Games[0], paused: false);
        bench.Speaker.Calls.Clear();

        gate.Dispose();
        gate.Dispose();

        Assert.Equal(new[] { "stop", "dispose" }, bench.Speaker.Calls);
        PlayFrom(bench, 0, Sound("after"));
        gate.ToggleMute();
        gate.StepUp();
        Assert.Empty(bench.Speaker.Played);
        Assert.False(bench.Config.GamesSoundMuted);
    }

    /// <summary>A speaker that breaks its promise and throws on the way out: Dispose still never throws, and still disposes it.</summary>
    [Fact]
    public void DisposeNeverThrows()
    {
        var speaker = new ThrowingSpeaker();
        var gate = new GamesSound(speaker, new OverlayConfig(), new TestGameClock());

        gate.Dispose();

        Assert.True(speaker.Disposed);
    }

    /// <summary>Made with nothing: the host made without a gate (the tests', the harness's) has one over a speaker that plays nothing.</summary>
    [Fact]
    public void AHostMadeWithoutAGateHasOneThatPlaysNothing()
    {
        var host = new GamesHost(new GameSaveStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PandoraOverlay.Tests", "never-made"), () => DateTime.UtcNow),
            Array.Empty<GameEntry>(), new TestGameClock());

        Assert.NotNull(host.Sound);
        Assert.Equal((GamesSound.DefaultStep, false), (host.Sound.Step, host.Sound.Muted));
        host.Sound.StepUp(); // a NullSpeaker: nothing to hear, nothing thrown
    }

    // ---- A game's clicks (A105) -----------------------------------------------------------------

    /// <summary>
    /// A host whose games' gate hands its clicks to a sink (MainWindow's line,
    /// with a test's sink): the games here are fakes, attached in a temporary
    /// games folder of the bench's own, and their gate plays on a speaker that
    /// records. <see cref="GamesBench"/> makes its gate without a click gate,
    /// as every construction before A105 does.
    /// </summary>
    private sealed class ClickBench : IDisposable
    {
        private readonly TempGamesRoot _temp = new();

        public ClickBench(IClickSink? clicks, int games = 1)
        {
            Games = Enumerable.Range(1, games).Select(n => new FakeGame($"game-{n}")).ToArray();
            var store = new GameSaveStore(_temp.Root, () => Clock.UtcNow);
            Host = new GamesHost(store, Games.Select(g => new GameEntry(g.Id, () => g)).ToArray(), Clock, _ => new TestGameLog(),
                new GamesSound(Speaker, Config, Clock, clicks));
            Host.AttachAll();
        }

        public TestGameClock Clock { get; } = new();

        public FakeSpeaker Speaker { get; } = new();

        public OverlayConfig Config { get; } = new();

        public FakeGame[] Games { get; }

        public GamesHost Host { get; }

        public GamesSound Gate => Host.Sound;

        /// <summary>A click as the Engine's rule hands it: through the game's own door.</summary>
        public void ClickFrom(int game, GameClick click) => Games[game].Host!.Sound.Click(click);

        public void Dispose()
        {
            Host.Sound.Dispose();
            _temp.Dispose();
        }
    }

    private static (ClickCue, ClickScope) InGames(ClickCue cue) => (cue, ClickScope.Games);

    /// <summary>The Contract's six and the app's six are the same by name, one for one (A100, A105); a value outside them is none.</summary>
    [Fact]
    public void AGamesClickIsTheCueOfTheSameName()
    {
        var clicks = Enum.GetValues<GameClick>();
        Assert.Equal(Enum.GetNames<ClickCue>(), Enum.GetNames<GameClick>());
        foreach (var click in clicks) Assert.Equal(click.ToString(), GamesSound.CueOf(click).ToString());
        Assert.Null(GamesSound.CueOf((GameClick)99));
        Assert.Null(GamesSound.CueOf((GameClick)(-1)));
    }

    /// <summary>The game on screen, not covered: each of its clicks reaches the click gate once, as its cue, in the Games scope; nothing reaches the games' own speaker (a click is the Clicks channel's, A101).</summary>
    [Fact]
    public void AHeardInstancesClickReachesTheClickGateInTheGamesScope()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        bench.Gate.Stage(bench.Host.Games[0], paused: false);

        foreach (var click in Enum.GetValues<GameClick>()) bench.ClickFrom(0, click);

        Assert.Equal(Enum.GetValues<ClickCue>().Select(InGames), sink.Clicks);
        Assert.Empty(bench.Speaker.Played);
    }

    [Fact]
    public void AnotherGamesClickIsDropped()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink, games: 2);
        bench.Gate.Stage(bench.Host.Games[0], paused: false);

        bench.ClickFrom(1, GameClick.Press);

        Assert.Empty(sink.Clicks);
    }

    /// <summary>Before any stage and on the grid nobody is heard, so no click goes anywhere.</summary>
    [Fact]
    public void OnTheGridNoGamesClickIsHeard()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        bench.ClickFrom(0, GameClick.Press);
        bench.Gate.Stage(null, paused: false); // the grid

        bench.ClickFrom(0, GameClick.Select);

        Assert.Empty(sink.Clicks);
    }

    [Fact]
    public void WithTheWarningUpNoGamesClickIsHeard()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        bench.Gate.Stage(bench.Host.Games[0], paused: true);

        bench.ClickFrom(0, GameClick.Press);
        Assert.Empty(sink.Clicks);

        bench.Gate.Stage(bench.Host.Games[0], paused: false); // "Keep playing"
        bench.ClickFrom(0, GameClick.Press);
        Assert.Equal(new[] { InGames(ClickCue.Press) }, sink.Clicks);
    }

    /// <summary>A detached instance (after "Start it again") reaches nothing, through the door it kept or straight at the gate; the instance that replaced it is heard.</summary>
    [Fact]
    public void ADetachedInstancesClickReachesNothing()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        var game = bench.Host.Games[0];
        var old = game.Host!;
        var oldDoor = old.Sound;
        bench.Host.Fault(game, new InvalidOperationException("made up"));
        Assert.True(bench.Host.StartAgain(game));
        bench.Gate.Stage(game, paused: false);

        oldDoor.Click(GameClick.Press);
        bench.Gate.Click(old, GameClick.Press);
        Assert.Empty(sink.Clicks);

        bench.ClickFrom(0, GameClick.Back);
        Assert.Equal(new[] { InGames(ClickCue.Back) }, sink.Clicks);
    }

    /// <summary>A stopped game is heard no more, before the window answers its fault: it is not Ready.</summary>
    [Fact]
    public void AStoppedGamesClickIsDropped()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        bench.Gate.Stage(bench.Host.Games[0], paused: false);
        bench.Host.Fault(bench.Host.Games[0], new InvalidOperationException("made up"));

        bench.ClickFrom(0, GameClick.Press);

        Assert.Empty(sink.Clicks);
    }

    /// <summary>The window closed (it tells the gate none), and the gate disposed at the exit: no click goes anywhere.</summary>
    [Fact]
    public void OnceClosedNoGamesClickIsHeard()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        bench.Gate.Stage(bench.Host.Games[0], paused: false);
        bench.Gate.Stage(null, paused: false); // Closed
        bench.ClickFrom(0, GameClick.Press);
        Assert.Empty(sink.Clicks);

        bench.Gate.Stage(bench.Host.Games[0], paused: false);
        bench.Gate.Dispose(); // the exit
        bench.ClickFrom(0, GameClick.Press);
        Assert.Empty(sink.Clicks);
    }

    /// <summary>A gate made without a click gate (every construction before A105: the tests', the harness's) drops every click, and throws for none.</summary>
    [Fact]
    public void WithNoClickGateNoGamesClickGoesAnywhere()
    {
        using var bench = new ClickBench(clicks: null);
        bench.Gate.Stage(bench.Host.Games[0], paused: false);

        foreach (var click in Enum.GetValues<GameClick>()) bench.ClickFrom(0, click);

        Assert.Empty(bench.Speaker.Played);
    }

    /// <summary>The mute is the click gate's to read for the Games scope (A103), not repeated here: muted, a heard click is still handed on, and the click gate drops it.</summary>
    [Fact]
    public void TheMuteIsTheClickGatesToRead()
    {
        var sink = new FakeClickSink();
        using var bench = new ClickBench(sink);
        bench.Config.GamesSoundMuted = true;
        bench.Gate.Stage(bench.Host.Games[0], paused: false);

        bench.ClickFrom(0, GameClick.Select);

        Assert.Equal(new[] { InGames(ClickCue.Select) }, sink.Clicks);
    }

    /// <summary>
    /// Through the real click gate (MainWindow's pair, over the bundled
    /// clicks and a second recording speaker for the Clicks channel): a heard
    /// click plays at the Games window's step; the window muted, or clicks
    /// turned off in Settings, it is silence; the 80 ms gap holds (A99, A103).
    /// </summary>
    [Fact]
    public void ThroughTheClickGateAGamesClickObeysTheWindowsMuteAndStepAndTheSwitch()
    {
        var clickSpeaker = new FakeSpeaker();
        var clock = new TestGameClock();
        var config = new OverlayConfig { GamesSoundVolume = 2, ClickSoundVolume = 5 };
        var clicks = new ClickSound(clickSpeaker, config, () => clock.Elapsed, new ClickBank(null, (_, line) => throw new Xunit.Sdk.XunitException(line)));
        using var temp = new TempGamesRoot();
        var game = new FakeGame("game-1");
        var host = new GamesHost(new GameSaveStore(temp.Root, () => clock.UtcNow), new[] { new GameEntry(game.Id, () => game) }, clock, _ => new TestGameLog(),
            new GamesSound(new FakeSpeaker(), config, clock, clicks));
        host.AttachAll();
        host.Sound.Stage(host.Games[0], paused: false);

        game.Host!.Sound.Click(GameClick.Press);
        Assert.Equal(GamesSound.GainOf(2), Assert.Single(clickSpeaker.Played).Gain); // the window's step, not the overlay's 5

        clock.Advance(TimeSpan.FromMilliseconds(40));
        game.Host.Sound.Click(GameClick.Press); // within the gap
        Assert.Single(clickSpeaker.Played);

        clock.Advance(ClickSound.MinGap);
        config.GamesSoundMuted = true;
        game.Host.Sound.Click(GameClick.Press);
        Assert.Single(clickSpeaker.Played);

        config.GamesSoundMuted = false;
        config.ClickSoundEnabled = false;
        game.Host.Sound.Click(GameClick.Press);
        Assert.Single(clickSpeaker.Played);

        config.ClickSoundEnabled = true;
        game.Host.Sound.Click(GameClick.Back);
        Assert.Equal(2, clickSpeaker.Played.Count);
        host.Sound.Dispose();
    }

    private sealed class ThrowingSpeaker : ISpeaker
    {
        public bool Disposed { get; private set; }

        public void Play(short[] samples, float gain) => throw new InvalidOperationException("made up");

        public void StopAll() => throw new InvalidOperationException("made up");

        public void Dispose()
        {
            Disposed = true;
            throw new InvalidOperationException("made up");
        }
    }
}
