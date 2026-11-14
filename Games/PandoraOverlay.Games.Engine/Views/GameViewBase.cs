using System.Windows;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// What every game's view is built on: the loop, the playback, the guard and
/// the lifecycle, so that no game writes its own and none gets them wrong.
///
/// <para><b>The loop and the playback.</b> The constructor makes a
/// <see cref="Playback"/> and a <see cref="GameLoop"/> over the host's clock.
/// Each loop tick advances the playback once per engine tick, then calls the
/// subclass's <see cref="Step"/>, then its <see cref="Draw"/>; anything either
/// throws goes to the host's <c>Fault</c> (A10).</para>
///
/// <para><b>The subclass sets <see cref="Loop"/>'s rate</b>:
/// <see cref="LoopRate.Full"/> when it enqueues a playback,
/// <see cref="LoopRate.Off"/> (or <see cref="LoopRate.Ambient"/>, for a quiet
/// cue) on <see cref="Engine.Playback.Drained"/>. The base does not, because
/// only the game knows whether something on its board still breathes; and it
/// starts Off, because nothing moves until a game says so: nothing moves,
/// nothing runs (A19). A view waiting for the player's click costs nothing
/// while The Isle runs underneath.</para>
///
/// <para><b>The guard.</b> Every handler a view subscribes (a button's click,
/// a surface's pointer events, a playback's <c>Drained</c>) goes through
/// <see cref="Guard"/>: a disposed or paused view takes no input, and an
/// exception becomes the game's fault, never the overlay's (A10). A skip is a
/// handler like any other: <see cref="Engine.Playback.SkipAll"/> throws on an
/// endless chain (A35), and through the guard that is one fault.</para>
///
/// <para><b>Disposal loses nothing.</b> The game's state is final at every
/// moment (A20): what the playback shows moving is a cosmetic replay of a
/// state already committed and saved. So <see cref="Dispose"/> only stops
/// the loop and drops the playback; it never touches the game's state, and
/// the next view paints the final state. It never throws, and it may be
/// called from anywhere, from inside a step's own code included: a step that
/// reports a fault (through <see cref="Guard"/> or the host) makes the window
/// dispose the view while the playback's walk is still on the stack, where
/// <see cref="Engine.Playback.Clear"/> refuses to run (A06). The view is then
/// marked disposed first, its loop disposed, and the refused clear left to
/// the garbage collector with the rest of the object; from that moment the
/// view's step and draw return at once, so nothing of a disposed view is
/// stepped or painted.</para>
///
/// <para><b>Overruns.</b> A playback step still running at its limit is
/// finished for it (<see cref="Engine.Playback.StepOverran"/>) and logged
/// here once, as a Warning: a bug in a game's step, never a fault, since the
/// picture left is still right.</para>
/// </summary>
public abstract class GameViewBase : IGameView
{
    /// <summary>The log's line for an overrun: a fact, the same for every game.</summary>
    internal const string OverranWarning = "a playback step ran past its limit and was finished";

    private bool _disposed;

    /// <summary>The loop over the host's clock and the playback, both Off and empty to begin with.</summary>
    protected GameViewBase(IGameHost host)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Playback = new Playback();
        Playback.StepOverran += OnStepOverran;
        Loop = new GameLoop(host.Clock, OnLoopStep, OnLoopDraw, host.Fault);
    }

    /// <summary>The host the game was attached to: its clock, storage, log and fault.</summary>
    protected IGameHost Host { get; }

    /// <summary>The view's loop: the subclass sets its rate (Full to play, Off or Ambient once drained).</summary>
    protected GameLoop Loop { get; }

    /// <summary>The view's playback: advanced once per engine tick by the loop, before <see cref="Step"/>.</summary>
    protected Playback Playback { get; }

    /// <inheritdoc />
    public abstract FrameworkElement Root { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> LayoutStates { get; }

    /// <inheritdoc />
    public abstract void ShowLayoutState(string state);

    /// <summary>One loop tick of <paramref name="ticks"/> engine ticks, after the playback has advanced as many times.</summary>
    protected abstract void Step(int ticks);

    /// <summary>Paint the surfaces (compose their buffers and present them).</summary>
    protected abstract void Draw();

    /// <summary>
    /// Runs <paramref name="action"/> as the view's own handler: nothing when
    /// the view is disposed or paused (a covered view takes no input),
    /// otherwise the action, with any exception handed to the host's
    /// <c>Fault</c> (A10), never thrown on.
    /// </summary>
    protected void Guard(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_disposed || Loop.Paused)
            return;
        try
        {
            action();
        }
        catch (Exception error)
        {
            Host.Fault(error);
        }
    }

    /// <summary>The window covered the view (G18): the loop paused, and <see cref="Guard"/> takes no input. Idempotent.</summary>
    public virtual void Pause() => Loop.Paused = true;

    /// <summary>The warning was acknowledged: the loop and the input carry on where they were. Idempotent.</summary>
    public virtual void Resume() => Loop.Paused = false;

    /// <summary>
    /// The loop gone and the playback dropped; the game's state untouched
    /// (A20). Idempotent, and it never throws (see the class's summary for
    /// the call from inside a step). A subclass that overrides it calls this
    /// too.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Loop.Dispose();
        try
        {
            Playback.Clear();
        }
        catch (InvalidOperationException)
        {
            // Called from inside a step's own code, where the playback refuses
            // to be cleared under its own walk (A06). The steps go with the
            // object: the loop is gone and this view is never stepped again.
        }
    }

    private void OnLoopStep(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            if (_disposed)
                return;
            Playback.Advance();
        }
        if (_disposed)
            return;
        Step(ticks);
    }

    private void OnLoopDraw()
    {
        if (_disposed)
            return;
        Draw();
    }

    private void OnStepOverran() => Host.Log.Warning(OverranWarning);
}
