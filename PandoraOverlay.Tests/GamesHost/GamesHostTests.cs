using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PandoraOverlay.Games;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Tests;

/// <summary>
/// The games' host (A5, A7, A8, A10, A38): what a game is made, attached,
/// fed and asked through, what a fault costs it and the overlay, and the
/// tile's buttons that set a save aside or reset it. Over a real
/// <see cref="GameSaveStore"/> in a temporary folder of the test's own (never
/// the real games folder), with games that record every call.
/// </summary>
public sealed class GamesHostTests : IDisposable
{
    private const string Id = "fake-game";

    private readonly ITestOutputHelper _output;
    private readonly TempGamesRoot _temp = new();
    private readonly TestGameClock _clock = new();
    private readonly GameSaveStore _store;
    private readonly Dictionary<string, TestGameLog> _logs = new();
    private readonly List<string> _events = new();

    public GamesHostTests(ITestOutputHelper output)
    {
        _output = output;
        _store = new GameSaveStore(_temp.Root, () => _clock.UtcNow);
    }

    public void Dispose() => _temp.Dispose();

    // ---- Helpers -----------------------------------------------------------------

    /// <summary>A host over these games, each its catalog entry's one instance, its events listed in <see cref="_events"/>.</summary>
    private GamesHost Host(params FakeGame[] games) =>
        Host(games.Select(game => new GameEntry(game.Id, () => game)).ToArray());

    private GamesHost Host(params GameEntry[] entries)
    {
        var host = new GamesHost(_store, entries, _clock, LogOf);
        host.TileChanged += game => _events.Add($"tile {game.Id}");
        host.ConditionChanged += game => _events.Add($"{game.Id} {game.Condition}");
        return host;
    }

    private TestGameLog LogOf(string id) => _logs.TryGetValue(id, out var log) ? log : _logs[id] = new TestGameLog();

    /// <summary>An entry that makes a new fake every time, each set up by <paramref name="setUp"/> (given its index) and listed in <paramref name="made"/>.</summary>
    private static GameEntry Maker(List<FakeGame> made, Action<FakeGame, int>? setUp = null, string id = Id) =>
        new(id, () =>
        {
            var game = new FakeGame(id);
            setUp?.Invoke(game, made.Count);
            made.Add(game);
            return game;
        });

    /// <summary>A player record with nothing personal in it: a made-up id, no name, no position.</summary>
    private static PlayerState Player(string? dino = "Deinosuchus", string? gender = "Male", double growth = 0.42, double health = 0.9) =>
        new("player-a", null, dino, gender, growth, health, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    private static MyLocationResponse InGame(string? dino = "Deinosuchus", string? gender = "Male", double growth = 0.42) =>
        new(true, Player(dino, gender, growth));

    private static readonly MyLocationResponse NotInGame = new(false, null);

    private string? OnDisk(string id = Id) => _temp.SavedPayload(id);

    private FileStream Lock(string name, string id = Id) =>
        new(_temp.FileOf(id, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static string PayloadOf(string path) =>
        JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Payload").GetString()!;

    /// <summary>The save on disk before the game is attached: none, a good one, one whose save.json is damaged but its backup good, or one nothing can be read of.</summary>
    private void PlantSave(string state, string id = Id)
    {
        switch (state)
        {
            case "none":
                break;
            case "ok":
                Assert.True(_store.Write(id, 1, "saved"));
                break;
            case "backup":
                Assert.True(_store.Write(id, 1, "older"));
                Assert.True(_store.Write(id, 1, "saved"));
                _temp.Plant(id, GameSaveStore.SaveFile, "damaged");
                break;
            case "unreadable":
                _temp.Plant(id, GameSaveStore.SaveFile, "damaged");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private HostedGame Attached(FakeGame game, out GamesHost host)
    {
        host = Host(game);
        host.AttachAll();
        return host.Games[0];
    }

    // ---- Made with no IO ---------------------------------------------------------

    [Fact]
    public void TheConstructorTouchesNoDiskAndAttachesNothing()
    {
        var game = new FakeGame();

        var host = Host(game);

        Assert.False(Directory.Exists(_temp.Root));
        Assert.Equal(new[] { "Title", "Pitch" }, game.Calls);
        var hosted = Assert.Single(host.Games);
        Assert.Equal((Id, "Fake game", "A game that records what it is asked.", GameCondition.Ready), (hosted.Id, hosted.Title, hosted.Pitch, hosted.Condition));
        Assert.Null(hosted.Tile()); // asked nothing until attached
        Assert.Null(hosted.CreateView());
        host.Feed(InGame());
        Assert.Equal(0, game.Readings);
    }

    [Fact]
    public void AFreshGameAttachedWritesNothing()
    {
        var game = new FakeGame();
        Attached(game, out _);

        Assert.Equal(default, game.ReadAtAttach);
        Assert.False(Directory.Exists(_temp.Root));
    }

    [Fact]
    public void AConstructorThatThrowsLeavesTheGameFaultedTitledByItsId()
    {
        var host = Host(new GameEntry("broken-game", () => throw new InvalidOperationException("made up")));

        var hosted = Assert.Single(host.Games);
        Assert.Equal((GameCondition.Faulted, "broken-game", ""), (hosted.Condition, hosted.Title, hosted.Pitch));
        host.AttachAll();
        host.Feed(InGame());
        Assert.Null(hosted.Tile());
    }

    [Fact]
    public void ACatalogThatMakesNoInstanceIsFaulted()
    {
        var host = Host(new GameEntry(Id, () => null!));

        Assert.Equal(GameCondition.Faulted, host.Games[0].Condition);
    }

    [Theory]
    [InlineData(FakeThrows.Title)]
    [InlineData(FakeThrows.Pitch)]
    public void ATitleOrPitchThatThrowsFaultsTheGame(FakeThrows member)
    {
        var host = Host(new FakeGame { Throws = member });

        var hosted = host.Games[0];
        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.Equal(member == FakeThrows.Title ? Id : "Fake game", hosted.Title);
        Assert.Equal("", hosted.Pitch);
    }

    [Fact]
    public void AnEmptyTitleIsTheId()
    {
        var host = Host(new FakeGame { TitleText = "" });

        Assert.Equal(Id, host.Games[0].Title);
    }

    [Theory]
    [InlineData("Bad Id")]
    [InlineData("nul")]
    [InlineData("")]
    public void AnEntryWithAnIdTheStoreWouldRefuseIsFaultedAndNeverMade(string id)
    {
        var made = 0;
        var host = Host(new GameEntry(id, () => { made++; return new FakeGame(id); }));

        var hosted = host.Games[0];
        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.NotNull(hosted.Defect);
        host.AttachAll();
        Assert.True(host.StartAgain(hosted)); // nothing to start, nothing left unsaved
        Assert.Equal(0, made);
        Assert.Equal(GameCondition.Faulted, hosted.Condition);
    }

    [Fact]
    public void TwoEntriesOfOneIdAreNeverTwoGamesInOneFolder()
    {
        var first = new FakeGame();
        var second = new FakeGame();
        var host = Host(first, second);
        host.AttachAll();

        Assert.Equal(new[] { GameCondition.Ready, GameCondition.Faulted }, host.Games.Select(g => g.Condition));
        Assert.Empty(second.Calls);
    }

    [Fact]
    public void AttachAllAttachesOnce()
    {
        var game = new FakeGame();
        var host = Host(game);

        host.AttachAll();
        host.AttachAll();

        Assert.Single(game.Calls, "Attach");
    }

    [Fact]
    public void TheGamesKeepTheCatalogsOrder()
    {
        var host = Host(new FakeGame("first"), new FakeGame("second"), new FakeGame("third"));

        Assert.Equal(new[] { "first", "second", "third" }, host.Games.Select(g => g.Id));
    }

    // ---- Attach (A7) ---------------------------------------------------------------

    [Theory]
    [InlineData("none", AttachResult.Ready, nameof(GameCondition.Ready), false)]
    [InlineData("ok", AttachResult.Ready, nameof(GameCondition.Ready), false)]
    [InlineData("backup", AttachResult.Ready, nameof(GameCondition.Ready), false)]
    [InlineData("none", AttachResult.SaveUnreadable, nameof(GameCondition.SaveUnreadable), true)]
    [InlineData("ok", AttachResult.SaveUnreadable, nameof(GameCondition.SaveUnreadable), true)]
    [InlineData("ok", AttachResult.SaveFromNewerVersion, nameof(GameCondition.SaveFromNewerVersion), true)]
    [InlineData("backup", AttachResult.SaveFromNewerVersion, nameof(GameCondition.SaveFromNewerVersion), true)]
    [InlineData("unreadable", AttachResult.Ready, nameof(GameCondition.SaveUnreadable), true)]
    [InlineData("unreadable", AttachResult.SaveFromNewerVersion, nameof(GameCondition.SaveUnreadable), true)]
    [InlineData("unreadable", AttachResult.SaveUnreadable, nameof(GameCondition.SaveUnreadable), true)]
    [InlineData("ok", (AttachResult)42, nameof(GameCondition.SaveUnreadable), true)]
    public void EachAnswerGivesItsConditionAndHold(string save, AttachResult answer, string conditionName, bool held)
    {
        var condition = Enum.Parse<GameCondition>(conditionName);
        PlantSave(save);
        var game = new FakeGame { Answer = answer };

        var hosted = Attached(game, out _);

        Assert.Equal(condition, hosted.Condition);
        Assert.Equal(held, _store.IsHeld(Id));
        Assert.Equal(condition, GamesHost.ConditionOf(answer, game.ReadAtAttach.State));
    }

    [Fact]
    public void ASaveReadFromItsBackupPlaysWithAWarning()
    {
        PlantSave("backup");
        var game = new FakeGame();

        var hosted = Attached(game, out _);

        Assert.Equal(new SaveRead(SaveReadState.FromBackup, 1, "older"), game.ReadAtAttach);
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Contains(LogOf(Id).Warnings, w => w.Contains("backup", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAttachThatThrowsIsFaultedAndFedNothing()
    {
        var game = new FakeGame { Throws = FakeThrows.Attach };

        var hosted = Attached(game, out var host);
        host.Feed(InGame());

        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.Equal(0, game.Readings);
        Assert.Equal(new[] { $"{Id} Faulted" }, _events);
    }

    [Fact]
    public void AGameWritingDuringAttachOverAnUnreadableSaveReplacesNothing()
    {
        // A careless game that starts fresh on a save it could not read: the
        // host held the save at the read, so the damaged file stays in place.
        PlantSave("unreadable");
        var game = new FakeGame { DuringAttach = host => host.Storage.Write(1, "fresh start", SaveUrgency.Now) };

        var hosted = Attached(game, out _);

        Assert.Equal(GameCondition.SaveUnreadable, hosted.Condition);
        Assert.Equal("damaged", File.ReadAllText(_temp.FileOf(Id, GameSaveStore.SaveFile)));
        Assert.Equal(new[] { GameSaveStore.SaveFile }, Directory.GetFiles(_temp.FolderOf(Id)).Select(Path.GetFileName));
    }

    [Fact]
    public void AGameThatNeverReadsItsSaveIsCheckedByTheHost()
    {
        PlantSave("unreadable");
        var game = new FakeGame { ReadsAtAttach = false };

        var hosted = Attached(game, out _);

        Assert.Equal(GameCondition.SaveUnreadable, hosted.Condition);
        Assert.True(_store.IsHeld(Id));
    }

    [Fact]
    public void AGameThatReportsAFaultWhileAttachingStaysStopped()
    {
        var game = new FakeGame { DuringAttach = host => host.Fault(new InvalidOperationException("made up")) };

        var hosted = Attached(game, out _);

        Assert.Equal(GameCondition.Faulted, hosted.Condition);
    }

    // ---- The feed (A5) -------------------------------------------------------------

    [Fact]
    public void InGameTheReadingCarriesTheFourValues()
    {
        var game = new FakeGame();
        Attached(game, out var host);

        host.Feed(InGame("Pachycephalosaurus", "Female", 0.37));

        Assert.Equal(new DinoReading(true, "Pachycephalosaurus", "Female", 0.37), game.LastReading);
    }

    [Fact]
    public void OutOfGameTheReadingIsNotInGame()
    {
        var game = new FakeGame();
        Attached(game, out var host);

        host.Feed(InGame());
        host.Feed(NotInGame);
        Assert.Equal(DinoReading.NotInGame, game.LastReading);

        host.Feed(InGame());
        host.Feed(new MyLocationResponse(true, null)); // "in game" with no player: nothing to tell
        Assert.Equal(DinoReading.NotInGame, game.LastReading);

        host.Feed(InGame());
        host.Feed(new MyLocationResponse(false, Player())); // a player record the API says is not in game
        Assert.Equal(DinoReading.NotInGame, game.LastReading);
        Assert.Equal(6, game.Readings);
    }

    [Fact]
    public void OnlyReadyGamesAreFed()
    {
        var ready = new FakeGame("ready-game");
        var held = new FakeGame("held-game") { Answer = AttachResult.SaveUnreadable };
        var faulted = new FakeGame("faulted-game") { Throws = FakeThrows.Attach };
        var host = Host(ready, held, faulted);
        host.AttachAll();

        host.Feed(InGame());

        Assert.Equal((1, 0, 0), (ready.Readings, held.Readings, faulted.Readings));
    }

    [Fact]
    public void AReadingThatThrowsStopsThatGameAndTheOthersAreStillFed()
    {
        var first = new FakeGame("first");
        var broken = new FakeGame("broken") { Throws = FakeThrows.OnReading };
        var third = new FakeGame("third");
        var host = Host(first, broken, third);
        host.AttachAll();

        host.Feed(InGame()); // returns: it never throws for a game
        host.Feed(InGame());

        Assert.Equal((2, 1, 2), (first.Readings, broken.Readings, third.Readings));
        Assert.Equal(GameCondition.Faulted, host.Games[1].Condition);
        Assert.Equal(new[] { "broken Faulted" }, _events);
    }

    [Fact]
    public void AFeedWritesNoLogLine()
    {
        var host = Host(new FakeGame("first"), new FakeGame("second"));
        host.AttachAll();
        var before = _logs.Values.Sum(log => log.Lines.Count + log.Warnings.Count);

        for (var i = 0; i < 1000; i++) host.Feed(i % 2 == 0 ? InGame() : NotInGame);

        Assert.Equal(before, _logs.Values.Sum(log => log.Lines.Count + log.Warnings.Count));
    }

    [Fact]
    public void ASoonWriteIsWrittenOnAFeedOnlyAMinuteAfterTheLastWriteOrOnFlush()
    {
        var game = new FakeGame();
        game.WhenRead = (h, _) => h.Storage.Write(1, $"seen {game.Readings}", SaveUrgency.Soon);
        Attached(game, out var host);

        host.Feed(InGame()); // no write yet this run: due at once
        Assert.Equal("seen 1", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(30));
        host.Feed(InGame());
        Assert.Equal("seen 1", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(29));
        host.Feed(InGame());
        Assert.Equal("seen 1", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(1)); // a minute after the last write
        host.Feed(InGame());
        Assert.Equal("seen 4", OnDisk());

        _clock.Advance(TimeSpan.FromSeconds(3));
        host.Feed(InGame());
        Assert.Equal("seen 4", OnDisk());
        host.Flush();
        Assert.Equal("seen 5", OnDisk());
    }

    [Fact]
    public void FlushWritesAStoppedGamesPendingSaveButNothingItWroteAfterTheFault()
    {
        var game = new FakeGame { DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now) };
        var hosted = Attached(game, out var host);
        game.Host!.Storage.Write(1, "handed over", SaveUrgency.Soon);

        host.Fault(hosted, new InvalidOperationException("made up"));
        game.Host.Storage.Write(1, "after the fault", SaveUrgency.Now);
        Assert.Equal("turn 1", OnDisk());

        host.Flush();

        Assert.Equal("handed over", OnDisk());
    }

    /// <summary>
    /// The feed runs on every poll of the overlay, window open or not: its cost
    /// is the overlay's. Three Ready games, a hundred thousand readings, timed
    /// and counted in bytes: one struct per poll and nothing on the heap (no
    /// closure, no log line, no boxing).
    /// </summary>
    [Fact]
    public void AHundredThousandFeedsThroughThreeGamesCostLittleAndAllocateNothing()
    {
        const int Feeds = 100_000;
        var games = new[] { new FakeGame("first"), new FakeGame("second"), new FakeGame("third") };
        var host = Host(games);
        host.AttachAll();
        var snapshot = InGame();
        for (var i = 0; i < 1000; i++) host.Feed(snapshot); // warmed up: JIT and first-call caches

        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Feeds; i++) host.Feed(snapshot);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

        _output.WriteLine($"{Feeds:N0} feeds through {games.Length} games: {watch.Elapsed.TotalMilliseconds:F1} ms, {allocated:N0} bytes allocated");
        Assert.All(games, game => Assert.Equal(Feeds + 1000, game.Readings));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"{Feeds:N0} feeds took {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(allocated < Feeds, $"{Feeds:N0} feeds allocated {allocated:N0} bytes: something is made on every poll");
    }

    // ---- What a game is asked through the host -------------------------------------------

    [Theory]
    [InlineData(FakeThrows.Tile)]
    [InlineData(FakeThrows.CreateTileArt)]
    [InlineData(FakeThrows.CreateView)]
    public void EveryCallOnAGameThatThrowsAnswersNullAndStopsIt(FakeThrows member)
    {
        var game = new FakeGame { Throws = member };
        var hosted = Attached(game, out var host);

        object? answer = member switch
        {
            FakeThrows.Tile => hosted.Tile(),
            FakeThrows.CreateTileArt => hosted.CreateTileArt(),
            _ => hosted.CreateView(),
        };

        Assert.Null(answer);
        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.Equal(new[] { $"{Id} Faulted" }, _events);
        host.Feed(InGame());
        Assert.Equal(0, game.Readings);
        Assert.Null(hosted.Tile()); // a stopped game is asked nothing more
        Assert.Single(game.Calls, member.ToString());
    }

    [Theory]
    [InlineData(AttachResult.SaveUnreadable)]
    [InlineData(AttachResult.SaveFromNewerVersion)]
    public void AHeldGameIsAskedNothing(AttachResult answer)
    {
        var game = new FakeGame { Answer = answer };
        var hosted = Attached(game, out _);

        Assert.Null(hosted.Tile());
        Assert.Null(hosted.CreateTileArt());
        Assert.Null(hosted.CreateView());
        Assert.Equal(new[] { "Title", "Pitch", "Attach" }, game.Calls);
    }

    [Fact]
    public void AReadyGameAnswersThroughTheHost() => TheThemeOnScreen.OnSta(() =>
    {
        var game = new FakeGame();
        var hosted = Attached(game, out _);

        Assert.Same(game.TileToShow, hosted.Tile());
        Assert.NotNull(hosted.CreateTileArt());
        var view = hosted.CreateView();
        Assert.Same(game.Views.Single(), view);
        Assert.Equal(GameCondition.Ready, hosted.Condition);
    });

    // ---- Events and faults (A10) -----------------------------------------------------

    [Fact]
    public void AGamesTileChangeIsPassedOnWhileItPlays()
    {
        var ready = new FakeGame("ready-game");
        var held = new FakeGame("held-game") { Answer = AttachResult.SaveUnreadable };
        var host = Host(ready, held);
        host.AttachAll();

        ready.Host!.TileChanged();
        held.Host!.TileChanged();
        host.Fault(host.Games[0], new InvalidOperationException("made up"));
        ready.Host.TileChanged();

        Assert.Equal(new[] { "tile ready-game", "ready-game Faulted" }, _events);
    }

    [Fact]
    public void AFaultTheGameReportsStopsItOnce()
    {
        var game = new FakeGame();
        var hosted = Attached(game, out var host);

        game.Host!.Fault(new InvalidOperationException("made up"));
        game.Host.Fault(new InvalidOperationException("again"));
        host.Fault(hosted, null!); // nothing to log but the fact

        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.Equal(new[] { $"{Id} Faulted" }, _events);
    }

    [Fact]
    public void TheHostsFaultStopsTheGame()
    {
        var game = new FakeGame();
        var hosted = Attached(game, out var host);

        host.Fault(hosted, new InvalidOperationException("made up"));
        host.Feed(InGame());

        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.Equal(0, game.Readings);
    }

    [Fact]
    public void TheGuardAnswersWhetherTheCallRan()
    {
        var game = new FakeGame();
        var hosted = Attached(game, out _);
        var ran = 0;

        Assert.True(GameGuard.Run(hosted, "a call", () => { ran++; }));
        Assert.Equal(7, GameGuard.Run(hosted, "a call", () => 7));
        Assert.True(GameGuard.Run(hosted, "a call", 5, state => ran += state));
        Assert.Equal(6, ran);
        Assert.Equal(GameCondition.Ready, hosted.Condition);

        Assert.Equal(0, GameGuard.Run<int>(hosted, "a call", () => throw new InvalidOperationException("made up")));
        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.False(GameGuard.Run(hosted, "a call", () => throw new InvalidOperationException("again")));
        Assert.Null(GameGuard.Run<string>(hosted, "a call", () => throw new InvalidOperationException("again")));
        Assert.False(GameGuard.Run(hosted, "a call", 1, _ => throw new InvalidOperationException("again")));
        Assert.Equal(new[] { $"{Id} Faulted" }, _events); // stopped once, the later faults only logged
    }

    // ---- Start fresh and Reset (A7, A38) ---------------------------------------------------

    [Theory]
    [InlineData("unreadable", AttachResult.Ready)]
    [InlineData("ok", AttachResult.SaveFromNewerVersion)]
    public void StartFreshOnAHeldSaveSetsItAsideResetsAndPlays(string save, AttachResult answer)
    {
        PlantSave(save);
        var oldBytes = File.ReadAllBytes(_temp.FileOf(Id, GameSaveStore.SaveFile));
        var game = new FakeGame { Answer = answer };
        var hosted = Attached(game, out var host);
        Assert.NotEqual(GameCondition.Ready, hosted.Condition);
        _events.Clear();

        Assert.True(host.StartFresh(hosted));

        Assert.Equal(oldBytes, File.ReadAllBytes(_temp.FileOf(Id, GameSaveStore.UnreadableSave(1))));
        Assert.Contains("Reset", game.Calls);
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.False(_store.IsHeld(Id));
        Assert.Equal("fresh", OnDisk());
        Assert.Equal(new[] { $"{Id} Ready" }, _events);

        game.Host!.Storage.Write(1, "turn 1", SaveUrgency.Now); // it can write
        Assert.Equal("turn 1", OnDisk());
        host.Feed(InGame());
        Assert.Equal(1, game.Readings);
    }

    [Fact]
    public void StartFreshSetsTheBackupAsideToo()
    {
        PlantSave("unreadable");
        _temp.Plant(Id, GameSaveStore.BackupFile, "damaged too");
        var hosted = Attached(new FakeGame(), out var host);

        Assert.True(host.StartFresh(hosted));

        Assert.Equal("damaged", File.ReadAllText(_temp.FileOf(Id, GameSaveStore.UnreadableSave(1))));
        Assert.Equal("damaged too", File.ReadAllText(_temp.FileOf(Id, GameSaveStore.UnreadableBackup(1))));
    }

    [Fact]
    public void ResetOnAReadyGameKeepsTheSaveItReplaces()
    {
        var game = new FakeGame { DuringAttach = h => h.Storage.Write(1, "progress", SaveUrgency.Now) };
        var hosted = Attached(game, out var host);
        _events.Clear();

        Assert.True(host.StartFresh(hosted));

        Assert.Equal("progress", PayloadOf(_temp.FileOf(Id, GameSaveStore.BeforeReset(1))));
        Assert.Contains("Reset", game.Calls);
        Assert.Equal("fresh", OnDisk());
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Equal(new[] { $"tile {Id}" }, _events);
    }

    [Fact]
    public void TheCopyBeforeAResetHoldsWhatWasPendingToo()
    {
        // Its newest state, not only the last minute's write: the copy is the
        // player's way back from a Reset by mistake.
        var game = new FakeGame { DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now) };
        var hosted = Attached(game, out var host);
        game.Host!.Storage.Write(1, "accrued since", SaveUrgency.Soon);

        Assert.True(host.StartFresh(hosted));

        Assert.Equal("accrued since", PayloadOf(_temp.FileOf(Id, GameSaveStore.BeforeReset(1))));
        Assert.Equal("fresh", OnDisk());
        host.Flush();
        Assert.Equal("fresh", OnDisk()); // the old pending state was not written over the reset
    }

    /// <summary>
    /// Keep first, reset second (A38): the copy fails, so nothing is reset.
    /// The copy is made to fail by holding <c>save.json</c> open with no
    /// sharing, so the copy can't read it. Holding the copy's own name no
    /// longer would: a taken name is passed over for the next number (A62).
    /// Nothing is pending here (the game's one write was a Now, on disk at
    /// Attach), so the flush first writes nothing and is not what fails: the
    /// refusal is the copy's (A38), not the pending write's (A49), which the
    /// test below makes fail instead.
    /// </summary>
    [Fact]
    public void AResetWhoseSaveCantBeKeptFirstResetsNothing()
    {
        var game = new FakeGame { DuringAttach = h => h.Storage.Write(1, "progress", SaveUrgency.Now) };
        var hosted = Attached(game, out var host);
        _events.Clear();
        Assert.False(hosted.Host!.OwnStorage.HasPending); // nothing for the flush to write

        bool answer;
        using (Lock(GameSaveStore.SaveFile)) answer = host.StartFresh(hosted);

        Assert.False(answer);
        Assert.DoesNotContain("Reset", game.Calls);
        Assert.Equal("progress", OnDisk());
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Empty(_events);
        Assert.Contains("not reset: its save could not be kept first", LogOf(Id).Warnings);
        Assert.DoesNotContain("not reset: its last pending save could not be written first", LogOf(Id).Warnings);
        Assert.False(File.Exists(_temp.FileOf(Id, GameSaveStore.BeforeReset(1)))); // no copy, not even a torn one

        Assert.True(host.StartFresh(hosted)); // the player can click again
        Assert.Equal("fresh", OnDisk());
        Assert.Equal("progress", PayloadOf(_temp.FileOf(Id, GameSaveStore.BeforeReset(1))));
    }

    /// <summary>
    /// Item 3c of the A10 verdict (A49): the pending write fails while the
    /// copy could still be made. The store's write is made to fail by holding
    /// its temp file, <c>save.json.tmp</c>, open with no sharing: the copy to
    /// <c>save.before-reset-1.json</c> touches only <c>save.json</c>, so it alone
    /// would have worked.
    /// </summary>
    [Fact]
    public void AResetWhosePendingSaveCantBeWrittenFirstResetsNothingAndKeepsIt()
    {
        var game = new FakeGame { DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now) };
        var hosted = Attached(game, out var host);
        game.Host!.Storage.Write(1, "accrued since", SaveUrgency.Soon);
        _events.Clear();

        bool answer;
        using (Lock(GameSaveStore.TempFile)) answer = host.StartFresh(hosted);

        Assert.False(answer);
        Assert.DoesNotContain("Reset", game.Calls);
        Assert.False(File.Exists(_temp.FileOf(Id, GameSaveStore.BeforeReset(1)))); // not even the copy
        Assert.Equal("turn 1", OnDisk());
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Empty(_events);
        Assert.Contains("not reset: its last pending save could not be written first", LogOf(Id).Warnings);

        host.Flush(); // the failure released: the pending text was kept
        Assert.Equal("accrued since", OnDisk());
    }

    [Fact]
    public void AHeldSaveThatCantBeSetAsideStaysHeldAndResetsNothing()
    {
        PlantSave("unreadable");
        var game = new FakeGame();
        var hosted = Attached(game, out var host);
        _events.Clear();

        bool answer;
        using (Lock(GameSaveStore.SaveFile)) answer = host.StartFresh(hosted);

        Assert.False(answer);
        Assert.DoesNotContain("Reset", game.Calls);
        Assert.True(_store.IsHeld(Id));
        Assert.Equal(GameCondition.SaveUnreadable, hosted.Condition);
        Assert.Equal("damaged", File.ReadAllText(_temp.FileOf(Id, GameSaveStore.SaveFile)));
        Assert.Empty(_events);

        Assert.True(host.StartFresh(hosted));
        Assert.Equal(GameCondition.Ready, hosted.Condition);
    }

    [Fact]
    public void AResetThatThrowsStopsTheGame()
    {
        var game = new FakeGame { Throws = FakeThrows.Reset, DuringAttach = h => h.Storage.Write(1, "progress", SaveUrgency.Now) };
        var hosted = Attached(game, out var host);

        Assert.False(host.StartFresh(hosted));

        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.True(File.Exists(_temp.FileOf(Id, GameSaveStore.BeforeReset(1)))); // kept first
        Assert.Equal("progress", OnDisk());
    }

    [Fact]
    public void AStartFreshWhoseResetThrowsStopsTheGameWithItsSaveSetAside()
    {
        PlantSave("unreadable");
        var hosted = Attached(new FakeGame { Throws = FakeThrows.Reset }, out var host);

        Assert.False(host.StartFresh(hosted));

        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.True(File.Exists(_temp.FileOf(Id, GameSaveStore.UnreadableSave(1))));
        Assert.False(_store.IsHeld(Id));
    }

    [Fact]
    public void AStoppedOrUnattachedGameHasNothingToStartFresh()
    {
        var game = new FakeGame();
        var host = Host(game);
        Assert.False(host.StartFresh(host.Games[0])); // not attached yet

        host.AttachAll();
        host.Fault(host.Games[0], new InvalidOperationException("made up"));
        Assert.False(host.StartFresh(host.Games[0]));
        Assert.DoesNotContain("Reset", game.Calls);
    }

    // ---- Start it again (A10) ------------------------------------------------------

    [Fact]
    public void StartAgainMakesANewInstanceAttachedToTheLastSave()
    {
        var made = new List<FakeGame>();
        var host = Host(Maker(made, (game, index) =>
        {
            if (index == 0) game.DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now);
        }));
        host.AttachAll();
        var hosted = host.Games[0];
        made[0].Host!.Storage.Write(1, "handed over", SaveUrgency.Soon); // pending at the fault
        host.Fault(hosted, new InvalidOperationException("made up"));
        _events.Clear();

        Assert.True(host.StartAgain(hosted));

        Assert.Equal(2, made.Count);
        Assert.NotSame(made[0], made[1]);
        Assert.Same(made[1], hosted.Instance);
        Assert.Contains("Attach", made[1].Calls);
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "handed over"), made[1].ReadAtAttach);
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Equal(new[] { $"{Id} Ready" }, _events);
        host.Feed(InGame());
        Assert.Equal((0, 1), (made[0].Readings, made[1].Readings));
    }

    [Fact]
    public void AReplacedInstanceReachesNothing()
    {
        var made = new List<FakeGame>();
        var host = Host(Maker(made));
        host.AttachAll();
        var hosted = host.Games[0];
        var oldHost = made[0].Host!;
        host.Fault(hosted, new InvalidOperationException("made up"));
        Assert.True(host.StartAgain(hosted));
        made[1].Host!.Storage.Write(1, "the new one's", SaveUrgency.Now);
        _events.Clear();

        oldHost.Storage.Write(1, "stale", SaveUrgency.Now);
        oldHost.Storage.Write(1, "stale", SaveUrgency.Soon);
        oldHost.TileChanged();
        oldHost.Fault(new InvalidOperationException("stale"));
        host.Flush();

        Assert.Equal("the new one's", OnDisk());
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Empty(_events);
    }

    [Fact]
    public void StartAgainOnAGameThatIsNotStoppedDoesNothing()
    {
        var made = new List<FakeGame>();
        var host = Host(Maker(made, (game, _) => game.Answer = AttachResult.SaveUnreadable), Maker(made, id: "second-game"));
        host.AttachAll();

        Assert.True(host.StartAgain(host.Games[0]));
        Assert.True(host.StartAgain(host.Games[1]));

        Assert.Equal(2, made.Count);
        Assert.Empty(_events);
    }

    [Fact]
    public void AConstructorThatThrowsAgainLeavesItStoppedAsItWas()
    {
        var calls = 0;
        var host = Host(new GameEntry(Id, () => ++calls == 1 ? new FakeGame() : throw new InvalidOperationException("made up")));
        host.AttachAll();
        host.Fault(host.Games[0], new InvalidOperationException("made up"));
        _events.Clear();

        Assert.True(host.StartAgain(host.Games[0])); // it failed again, and nothing was left unsaved

        Assert.Equal(2, calls);
        Assert.Equal(GameCondition.Faulted, host.Games[0].Condition);
        Assert.Empty(_events);
    }

    [Fact]
    public void AGameWhoseConstructorFailedAtLaunchCanStartAgain()
    {
        var calls = 0;
        var host = Host(new GameEntry(Id, () => ++calls == 1 ? throw new InvalidOperationException("made up") : new FakeGame()));
        host.AttachAll();
        var hosted = host.Games[0];
        Assert.Equal((GameCondition.Faulted, Id), (hosted.Condition, hosted.Title));

        Assert.True(host.StartAgain(hosted));

        Assert.Equal((GameCondition.Ready, "Fake game"), (hosted.Condition, hosted.Title));
        Assert.Contains("Attach", ((FakeGame)hosted.Instance!).Calls);
        host.Feed(InGame());
        Assert.Equal(1, ((FakeGame)hosted.Instance!).Readings);
    }

    [Fact]
    public void ANewInstanceWhoseAttachThrowsIsStoppedOnce()
    {
        var made = new List<FakeGame>();
        var host = Host(Maker(made, (game, index) => { if (index == 1) game.Throws = FakeThrows.Attach; }));
        host.AttachAll();
        host.Fault(host.Games[0], new InvalidOperationException("made up"));
        _events.Clear();

        Assert.True(host.StartAgain(host.Games[0]));

        Assert.Equal(GameCondition.Faulted, host.Games[0].Condition);
        Assert.Equal(new[] { $"{Id} Faulted" }, _events);
    }

    /// <summary>
    /// Item 3a of the A10 verdict (A49): save first, start again second. The
    /// stopped instance's pending save can't be written (the store's temp file
    /// held open with no sharing), so nothing is started and nothing is lost;
    /// once the disk works, the new instance reads exactly that pending text.
    /// </summary>
    [Fact]
    public void StartAgainWhosePendingSaveCantBeWrittenStartsNothingAndLosesNothing()
    {
        var made = new List<FakeGame>();
        var host = Host(Maker(made, (game, index) =>
        {
            if (index == 0) game.DuringAttach = h => h.Storage.Write(1, "turn 1", SaveUrgency.Now);
        }));
        host.AttachAll();
        var hosted = host.Games[0];
        made[0].Host!.Storage.Write(1, "handed over", SaveUrgency.Soon); // pending at the fault
        host.Fault(hosted, new InvalidOperationException("made up"));
        _events.Clear();

        bool answer;
        using (Lock(GameSaveStore.TempFile)) answer = host.StartAgain(hosted);

        Assert.False(answer);
        Assert.Equal(GameCondition.Faulted, hosted.Condition);
        Assert.Single(made); // the catalog made no new instance
        Assert.Same(made[0], hosted.Instance);
        Assert.Empty(_events);
        Assert.Contains("not started again: its last pending save could not be written first", LogOf(Id).Warnings);
        Assert.Equal("turn 1", OnDisk());

        Assert.True(host.StartAgain(hosted)); // the failure released: the player clicks again

        Assert.Equal(2, made.Count);
        Assert.Same(made[1], hosted.Instance);
        Assert.Contains("Attach", made[1].Calls);
        Assert.Equal(new SaveRead(SaveReadState.Ok, 1, "handed over"), made[1].ReadAtAttach);
        Assert.Equal(GameCondition.Ready, hosted.Condition);
        Assert.Equal(new[] { $"{Id} Ready" }, _events);
    }

    /// <summary>
    /// Item 3b of the A10 verdict (A49): false means only "the pending save
    /// could not be written". Every other case answers true: a new instance
    /// that plays, one whose constructor or Attach throws again, a game that is
    /// not stopped, a catalog defect.
    /// </summary>
    [Fact]
    public void StartAgainAnswersTrueWheneverNothingWasLeftUnsaved()
    {
        var plays = new List<FakeGame>();
        var constructorAgain = 0;
        var attachAgain = new List<FakeGame>();
        var host = Host(
            Maker(plays, id: "plays-game"),
            new GameEntry("constructor-game", () => ++constructorAgain == 1 ? new FakeGame("constructor-game") : throw new InvalidOperationException("made up")),
            Maker(attachAgain, (game, index) => { if (index == 1) game.Throws = FakeThrows.Attach; }, id: "attach-game"),
            Maker(new List<FakeGame>(), id: "ready-game"),
            new GameEntry("Bad Id", () => new FakeGame("Bad Id")));
        host.AttachAll();
        for (var i = 0; i < 3; i++) host.Fault(host.Games[i], new InvalidOperationException("made up"));

        Assert.True(host.StartAgain(host.Games[0]));
        Assert.True(host.StartAgain(host.Games[1]));
        Assert.True(host.StartAgain(host.Games[2]));
        Assert.True(host.StartAgain(host.Games[3]));
        Assert.True(host.StartAgain(host.Games[4]));

        Assert.Equal(
            new[] { GameCondition.Ready, GameCondition.Faulted, GameCondition.Faulted, GameCondition.Ready, GameCondition.Faulted },
            host.Games.Select(g => g.Condition));
        Assert.Equal((2, 2, 2), (plays.Count, constructorAgain, attachAgain.Count));
    }
}
