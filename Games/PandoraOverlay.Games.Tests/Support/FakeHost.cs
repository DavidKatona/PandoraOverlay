using System.Runtime.ExceptionServices;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The app's side of the Contract in a test: what a game gets at Attach, with
/// nothing real behind it. Every game's tests and the engine's view tests are
/// built on it: a <see cref="ManualClock"/> for the time, a
/// <see cref="MemoryStorage"/> for the save, a <see cref="CollectingLog"/> for
/// the log, and counters for the two calls a game makes back to the host.
/// <para>
/// A fault nobody expected fails the test where it happened (A41):
/// <see cref="Fault"/> records the exception and throws it again, with the
/// game's own stack. Recorded only, a view's test that asserts on layout would
/// pass while the real window would have closed that game. A test that MEANS a
/// fault to happen (the engine's guard and loop, a game's own fault path: the
/// second layer of containment, A10) sets <see cref="ExpectFaults"/> and
/// asserts on <see cref="Faults"/>.
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

    /// <summary>False to begin with: a fault is thrown again. True: the test means faults to happen, and they are only recorded.</summary>
    public bool ExpectFaults { get; set; }

    /// <summary>Every exception reported through <see cref="Fault"/>, in order, whether it was thrown again or not.</summary>
    public IReadOnlyList<Exception> Faults => _faults;

    public void TileChanged() => TileChanges++;

    /// <summary>
    /// Records <paramref name="error"/>; then, unless <see cref="ExpectFaults"/>
    /// is set, throws that same exception again with its stack kept, so an
    /// unexpected fault fails the test at the throw (A41). A test of a fault
    /// path sets <see cref="ExpectFaults"/> to true and asserts on
    /// <see cref="Faults"/>.
    /// </summary>
    public void Fault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _faults.Add(error);
        if (!ExpectFaults) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
