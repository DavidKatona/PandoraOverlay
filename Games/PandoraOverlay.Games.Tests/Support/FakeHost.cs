namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The app's side of the Contract in a test: what a game gets at Attach, with
/// nothing real behind it. Every game's tests and the engine's view tests are
/// built on it: a <see cref="ManualClock"/> for the time, a
/// <see cref="MemoryStorage"/> for the save, a <see cref="CollectingLog"/> for
/// the log, and counters for the two calls a game makes back to the host.
/// <para>
/// <see cref="Fault"/> records the exception and does not rethrow it, so a test
/// can make a view's handler or the engine's loop throw and assert that the
/// fault was reported (A10: the second layer of containment), with the test
/// itself carrying on as the overlay would. A test that expects no fault asserts
/// <see cref="Faults"/> is empty.
/// </para>
/// </summary>
public sealed class FakeHost : IGameHost
{
    private readonly List<Exception> _faults = new();

    /// <summary>The clock, as the test moves it.</summary>
    public ManualClock ManualClock { get; } = new();

    /// <summary>The save, as the test sets it and reads its writes.</summary>
    public MemoryStorage Memory { get; } = new();

    /// <summary>The log's lines.</summary>
    public CollectingLog Collected { get; } = new();

    public IGameClock Clock => ManualClock;

    public IGameStorage Storage => Memory;

    public IGameLog Log => Collected;

    /// <summary>How many times the game said its tile changed.</summary>
    public int TileChanges { get; private set; }

    /// <summary>Every exception reported through <see cref="Fault"/>, in order.</summary>
    public IReadOnlyList<Exception> Faults => _faults;

    public void TileChanged() => TileChanges++;

    /// <summary>Records it; never rethrows (the window would close the game and the overlay would live).</summary>
    public void Fault(Exception error) => _faults.Add(error);
}
