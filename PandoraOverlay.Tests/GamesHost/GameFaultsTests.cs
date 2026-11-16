using System.Reflection;
using PandoraOverlay.Games;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>GameFaults holds statics App's handler reads: its tests never run beside one another or beside a test that sets them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TheGamesLastNet
{
    public const string Name = "The games' last net";
}

/// <summary>
/// The last net (A10): an exception nobody caught is contained when, and only
/// when, its stack passes through a games assembly, and the game it came from
/// is stopped; anything else crashes the overlay as it always did. The
/// engine's own code is the games assembly at hand (a pixel buffer of no
/// size throws inside it); the test's own throws are the app's stand-in.
/// </summary>
[Collection(TheGamesLastNet.Name)]
public sealed class GameFaultsTests : IDisposable
{
    private readonly TempGamesRoot _temp = new();
    private readonly GameSaveStore _store;

    public GameFaultsTests() => _store = new GameSaveStore(_temp.Root, () => DateTime.UtcNow);

    public void Dispose()
    {
        GameFaults.Host = null;
        GameFaults.OnScreen = null;
        _temp.Dispose();
    }

    private static Assembly EngineAssembly => typeof(PixelBuffer).Assembly;

    private static Assembly ContractAssembly => typeof(IGame).Assembly;

    private static Assembly AppAssembly => typeof(GamesHost).Assembly;

    /// <summary>An exception thrown inside the engine's own code.</summary>
    private static Exception FromTheEngine() => Record.Exception(() => new PixelBuffer(0, 0))!;

    /// <summary>An exception thrown by the test itself: no games assembly on its stack.</summary>
    private static Exception FromTheTest()
    {
        static void Throw() => throw new InvalidOperationException("made up");
        return Record.Exception(Throw)!;
    }

    private GamesHost AttachedHost(params FakeGame[] games)
    {
        var host = new GamesHost(_store, games.Select(g => new GameEntry(g.Id, () => g)).ToArray(), new TestGameClock(), _ => new TestGameLog());
        host.AttachAll();
        return host;
    }

    // ---- From a game or not ------------------------------------------------------

    [Fact]
    public void AnExceptionThrownInsideTheEngineIsFromAGame()
    {
        var error = FromTheEngine();

        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.True(GameFaults.IsFromAGame(error));
        Assert.Contains(EngineAssembly, GameFaults.AssembliesIn(error));
    }

    [Fact]
    public void AnExceptionThrownByTheTestIsNot()
    {
        Assert.False(GameFaults.IsFromAGame(FromTheTest()));
    }

    [Fact]
    public void AnInnerExceptionFromTheEngineCounts()
    {
        var wrapped = new InvalidOperationException("never thrown", FromTheEngine());
        var aggregate = new AggregateException(FromTheTest(), FromTheEngine());

        Assert.True(GameFaults.IsFromAGame(wrapped));
        Assert.True(GameFaults.IsFromAGame(aggregate));
        Assert.False(GameFaults.IsFromAGame(new AggregateException(FromTheTest())));
    }

    [Fact]
    public void TheInnermostThrowsFramesComeFirst()
    {
        Exception? caught = null;
        try
        {
            try { _ = new PixelBuffer(0, 0); }
            catch (Exception inner) { throw new InvalidOperationException("wrapped", inner); }
        }
        catch (Exception outer) { caught = outer; }

        var stack = GameFaults.AssembliesIn(caught!);

        Assert.Equal(EngineAssembly, stack[0]);
        Assert.Contains(typeof(GameFaultsTests).Assembly, stack);
        Assert.Equal(stack.Distinct().Count(), stack.Count);
    }

    [Theory]
    [InlineData("PandoraOverlay.Games.Engine", true)]
    [InlineData("PandoraOverlay.Games.Contract", true)]
    [InlineData("PandoraOverlay.Games.Hatchling", true)]
    [InlineData("PandoraOverlay.Games.", false)]
    [InlineData("PandoraOverlay.Games", false)]
    [InlineData("PandoraOverlay", false)]
    [InlineData("PandoraOverlay.Tests", false)]
    [InlineData("PandoraOverlay.GamesHost.X", false)]
    [InlineData("pandoraoverlay.games.engine", false)]
    [InlineData(null, false)]
    public void AGamesAssemblyIsNamedByItsPrefix(string? name, bool games)
    {
        Assert.Equal(games, GameFaults.IsGamesAssembly(name));
    }

    [Fact]
    public void TheRealAssembliesAreSortedRight()
    {
        Assert.True(GameFaults.IsGamesAssembly(EngineAssembly.GetName().Name));
        Assert.True(GameFaults.IsGamesAssembly(ContractAssembly.GetName().Name));
        Assert.False(GameFaults.IsGamesAssembly(AppAssembly.GetName().Name));
        Assert.False(GameFaults.IsGamesAssembly(typeof(GameFaultsTests).Assembly.GetName().Name));
    }

    // ---- Containing ----------------------------------------------------------------

    [Fact]
    public void WithNoHostNothingIsContained()
    {
        Assert.Null(GameFaults.Host);

        Assert.False(GameFaults.TryContain(FromTheEngine()));
    }

    [Fact]
    public void TheEnginesExceptionStopsTheGameOnScreen()
    {
        var shown = new FakeGame("shown-game");
        var other = new FakeGame("other-game");
        var host = AttachedHost(other, shown);
        var stopped = new List<string>();
        host.ConditionChanged += game => stopped.Add($"{game.Id} {game.Condition}");
        GameFaults.Host = host;
        GameFaults.OnScreen = () => host.Games[1];

        Assert.True(GameFaults.TryContain(FromTheEngine()));

        Assert.Equal(new[] { "shown-game Faulted" }, stopped);
        Assert.Equal(GameCondition.Ready, host.Games[0].Condition);
    }

    [Fact]
    public void AnExceptionThatIsNotAGamesIsNotContained()
    {
        var host = AttachedHost(new FakeGame());
        GameFaults.Host = host;
        GameFaults.OnScreen = () => host.Games[0];

        Assert.False(GameFaults.TryContain(FromTheTest()));

        Assert.Equal(GameCondition.Ready, host.Games[0].Condition);
    }

    [Fact]
    public void AGamesExceptionWithNoGameToNameIsStillContained()
    {
        // The code was a game's: the overlay must outlive it, even when the
        // window shows no game to stop (the grid is up, or it is closed).
        var host = AttachedHost(new FakeGame());
        GameFaults.Host = host;

        Assert.True(GameFaults.TryContain(FromTheEngine()));
        GameFaults.OnScreen = () => null;
        Assert.True(GameFaults.TryContain(FromTheEngine()));

        Assert.Equal(GameCondition.Ready, host.Games[0].Condition);
    }

    [Fact]
    public void ANetThatFailsAnswersFalseSoTheOverlayCrashesAsBefore()
    {
        var host = AttachedHost(new FakeGame());
        GameFaults.Host = host;
        GameFaults.OnScreen = () => throw new InvalidOperationException("the window's own bug");

        Assert.False(GameFaults.TryContain(FromTheEngine()));
    }

    // ---- Which game (pure) ---------------------------------------------------------

    /// <summary>
    /// No game assembly exists yet, so the rule is held on stand-ins: two
    /// listed games whose "own" assemblies are, for the test, the engine's and
    /// the Contract's.
    /// </summary>
    [Fact]
    public void TheGameWhoseOwnAssemblyIsOnTheStackIsBlamedTheThrowsSideFirst()
    {
        var host = AttachedHost(new FakeGame("engine-game"), new FakeGame("contract-game"), new FakeGame("third-game"));
        var engineGame = host.Games[0];
        var contractGame = host.Games[1];
        var third = host.Games[2];
        Assembly? Own(HostedGame game) => game == engineGame ? EngineAssembly : game == contractGame ? ContractAssembly : null;

        Assert.Same(contractGame, GameFaults.Blame(new[] { AppAssembly, ContractAssembly, EngineAssembly }, host.Games, Own, third));
        Assert.Same(engineGame, GameFaults.Blame(new[] { EngineAssembly, ContractAssembly }, host.Games, Own, third));
        Assert.Same(third, GameFaults.Blame(new[] { AppAssembly }, host.Games, Own, third)); // only shared code: the game on screen
        Assert.Null(GameFaults.Blame(new[] { AppAssembly }, host.Games, Own, null));
        Assert.Null(GameFaults.Blame(Array.Empty<Assembly>(), host.Games, Own, null));
    }

    [Fact]
    public void TwoGamesOfOneAssemblyPreferTheOneOnScreen()
    {
        var host = AttachedHost(new FakeGame("first-game"), new FakeGame("second-game"));
        Assembly? Own(HostedGame _) => EngineAssembly;

        Assert.Same(host.Games[1], GameFaults.Blame(new[] { EngineAssembly }, host.Games, Own, host.Games[1]));
        Assert.Same(host.Games[0], GameFaults.Blame(new[] { EngineAssembly }, host.Games, Own, null));
    }

    [Fact]
    public void AGamesOwnAssemblyIsOnlyEverAGamesAssembly()
    {
        // The fakes live in the test assembly, which is no games assembly: the
        // engine's exception thrown here still blames the game on screen, not
        // whichever fake happens to share the test's assembly.
        var first = new FakeGame("first-game");
        var shown = new FakeGame("shown-game");
        var host = AttachedHost(first, shown);
        GameFaults.Host = host;
        GameFaults.OnScreen = () => host.Games[1];

        Assert.True(GameFaults.TryContain(FromTheEngine()));

        Assert.Equal(new[] { GameCondition.Ready, GameCondition.Faulted }, host.Games.Select(g => g.Condition));
    }
}
