using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// What one game instance is given (A10): the host's clock, its own storage,
/// and its log lines as "&lt;id&gt;: &lt;fact&gt;", one line of at most 200
/// characters, so a game can't fill the log or break its one-line shape.
/// </summary>
public sealed class GameHostTests : IDisposable
{
    private readonly TempGamesRoot _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ALineIsTheIdThenTheFact()
    {
        Assert.Equal("hatch-test: 3 runs", GameLog.Line("hatch-test", "3 runs"));
        Assert.Equal("hatch-test: ", GameLog.Line("hatch-test", null));
        Assert.Equal("hatch-test: ", GameLog.Line("hatch-test", ""));
    }

    // The breaks by number: a line or paragraph separator written into this
    // source would end the source line itself.
    [Theory]
    [InlineData(0x0A)]
    [InlineData(0x0D)]
    [InlineData(0x85)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    public void AFactIsCutAtItsFirstLineBreak(int lineBreak)
    {
        Assert.Equal("g: first", GameLog.Line("g", "first" + (char)lineBreak + "second"));
        Assert.Equal("g: first", GameLog.Line("g", "first" + (char)lineBreak + "\nthird"));
    }

    [Fact]
    public void AWindowsLineEndIsALineBreakToo()
    {
        Assert.Equal("g: first", GameLog.Line("g", "first\r\nsecond"));
        Assert.Equal("g: first\tsecond", GameLog.Line("g", "first\tsecond")); // a tab is not a break
    }

    [Fact]
    public void AFactIsCutToTwoHundredCharacters()
    {
        Assert.Equal(200, GameLog.MaxFact);
        var line = GameLog.Line("g", new string('x', 250));

        Assert.Equal("g: " + new string('x', 200), line);
        Assert.Equal("g: " + new string('x', 200), GameLog.Line("g", new string('x', 200)));
    }

    [Fact]
    public void ACutNeverSplitsASurrogatePair()
    {
        var fact = new string('x', 199) + "\U0001F996" + "tail"; // a pair at 199 and 200

        var line = GameLog.Line("g", fact);

        Assert.Equal("g: " + new string('x', 199), line);
        Assert.False(char.IsHighSurrogate(line[^1]));
    }

    [Fact]
    public void TheHostGivesTheClockAndTheStorageItWasMadeWith()
    {
        var clock = new TestGameClock();
        var log = new TestGameLog();
        var store = new GameSaveStore(_temp.Root, () => clock.UtcNow);
        var storage = new GameStorage(store, "sample-game", clock, log);
        var games = new GamesHost(store, new[] { new GameEntry("sample-game", () => new FakeGame("sample-game")) }, clock, _ => log);

        var host = new GameHost(games, games.Games[0], clock, storage, log);

        Assert.Same(clock, host.Clock);
        Assert.Same(storage, host.Storage);
        Assert.Same(log, host.Log);
        Assert.True(host.Current);
    }

    [Fact]
    public void ADetachedHostWritesNothing()
    {
        var clock = new TestGameClock();
        var log = new TestGameLog();
        var store = new GameSaveStore(_temp.Root, () => clock.UtcNow);
        var storage = new GameStorage(store, "sample-game", clock, log);
        var games = new GamesHost(store, new[] { new GameEntry("sample-game", () => new FakeGame("sample-game")) }, clock, _ => log);
        var host = new GameHost(games, games.Games[0], clock, storage, log);

        host.Detach();
        host.Storage.Write(1, "stale", SaveUrgency.Now);

        Assert.False(host.Current);
        Assert.Null(_temp.SavedPayload("sample-game"));
    }

    /// <summary>
    /// A detached instance's sound reaches nothing (A79), as its storage
    /// doesn't: the game is on screen and heard, its instance is detached, and
    /// what it plays then, through its door or the door it kept, is dropped.
    /// </summary>
    [Fact]
    public void ADetachedHostsSoundReachesNothing()
    {
        var clock = new TestGameClock();
        var speaker = new FakeSpeaker();
        var game = new FakeGame("sample-game");
        var store = new GameSaveStore(_temp.Root, () => clock.UtcNow);
        var games = new GamesHost(store, new[] { new GameEntry("sample-game", () => game) }, clock, _ => new TestGameLog(),
            new GamesSound(speaker, new OverlayConfig(), clock));
        games.AttachAll();
        var hosted = games.Games[0];
        var host = hosted.Host!;
        var door = host.Sound;
        games.Sound.Stage(hosted, paused: false);
        door.Play(new GameSound("before", new short[] { 1 }));
        Assert.Single(speaker.Played);

        host.Detach();
        door.Play(new GameSound("kept door", new short[] { 1 }));
        host.Sound.Play(new GameSound("its door", new short[] { 1 }));

        Assert.Single(speaker.Played);
    }

    /// <summary>
    /// A closed door's click reaches nothing (A105), as its sound doesn't: the
    /// game is on screen and heard, its click reaches the click gate; once its
    /// instance is detached, a click through the door it kept or its door now
    /// is dropped.
    /// </summary>
    [Fact]
    public void ADetachedHostsClickReachesNothing()
    {
        var clock = new TestGameClock();
        var clicks = new FakeClickSink();
        var game = new FakeGame("sample-game");
        var store = new GameSaveStore(_temp.Root, () => clock.UtcNow);
        var games = new GamesHost(store, new[] { new GameEntry("sample-game", () => game) }, clock, _ => new TestGameLog(),
            new GamesSound(new FakeSpeaker(), new OverlayConfig(), clock, clicks));
        games.AttachAll();
        var hosted = games.Games[0];
        var host = hosted.Host!;
        var door = host.Sound;
        games.Sound.Stage(hosted, paused: false);
        door.Click(GameClick.Press);
        Assert.Equal(new[] { (ClickCue.Press, ClickScope.Games) }, clicks.Clicks);

        host.Detach();
        door.Click(GameClick.Select);
        host.Sound.Click(GameClick.Back);

        Assert.Single(clicks.Clicks);
    }
}
