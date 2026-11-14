namespace PandoraOverlay.Games.Engine;

/// <summary>
/// "Play this, then that": a queue of <see cref="IStep"/>s with one current
/// step, played one beat at a time. It is how a game shows its events in
/// order, so it is always clear what happened and in which order (G9),
/// instead of the board simply changing.
/// <para>
/// <b>Cosmetic only</b> (A20). A game's rules return the new state and an
/// ordered list of events; the game commits and saves the state; only then
/// does the view make steps of the events and enqueue them. The state is
/// final before the first step, so everything this class can do to a
/// playback (pause it, speed it up, skip it, drop it) costs the player
/// nothing: neither their progress nor their control.
/// </para>
/// <para>
/// <b>Driven, never running by itself</b> (A19). There is no clock, timer or
/// thread here: the playback moves only when <see cref="Advance"/> is called,
/// once per loop tick, by the view's <c>GameLoop</c>. A paused loop simply
/// stops calling it, so a covered view freezes mid-step and resumes where it
/// was, and a view going away calls <see cref="Clear"/>. Single-threaded, on
/// the view's thread, like everything a view owns.
/// </para>
/// <para>
/// <b>Never in the way.</b> A broken step must never hang a game: a step
/// still running after <see cref="StepLimitTicks"/> is finished for it
/// (<see cref="StepOverran"/>), and a broken chain is cut: a skip that has
/// finished <see cref="SkipLimitSteps"/> steps with more still queued throws
/// instead of freezing the overlay (A35). A step that throws passes its exception to
/// the caller (the loop reports it as the game's fault, A10) and leaves the
/// playback cleared and usable. And whatever ends a playback, a skip, an
/// overrun or running to the end, the picture left is the same, because
/// every step keeps <see cref="IStep"/>'s rule.
/// </para>
/// </summary>
public sealed class Playback
{
    /// <summary>
    /// The most ticks one step may take: 300, ten seconds at speed 1. A step
    /// advanced this many times without finishing is <see cref="IStep.Finish"/>ed
    /// and dropped, and <see cref="StepOverran"/> is raised: a step that never
    /// answers true is a bug, and it must cost the player ten seconds at
    /// most, never the game. The limit counts the step's own advances, not
    /// loop ticks, so <see cref="Speed"/> never changes whether a step
    /// overruns. It applies to each step the queue holds: a
    /// <see cref="Steps.Sequence"/> is one step, so a chain of beats that may
    /// run past ten seconds belongs in the queue as separate steps.
    /// </summary>
    public const int StepLimitTicks = 300;

    /// <summary>
    /// The most steps one <see cref="SkipAll"/> finishes, the current one
    /// included: 10,000 (A35). A step whose Finish keeps enqueuing steps
    /// would otherwise never let the skip return, and the skip runs on the
    /// overlay's UI thread, over the game: every widget, the hotkeys and the
    /// tray would freeze with it, the one thing a game may never cost. Past
    /// the limit, with a step still queued, the skip throws
    /// <see cref="InvalidOperationException"/> and leaves the playback
    /// cleared, and the view's Guard reports it as the game's fault (A10): a
    /// game's bug is allowed to cost the game, never the overlay. A real
    /// playback is tens to hundreds of steps, so the limit is far from
    /// anything a working game reaches. The same idea as
    /// <see cref="StepLimitTicks"/>: a broken step is finished for it, a
    /// broken chain is cut. <see cref="Advance"/> needs no such limit: it
    /// advances at most <see cref="Speed"/> steps a tick, whatever they
    /// enqueue.
    /// </summary>
    public const int SkipLimitSteps = 10_000;

    private readonly Queue<IStep> _queue = new();

    /// <summary>The step being played, already begun; null between steps.</summary>
    private IStep? _current;

    /// <summary>How many times <see cref="_current"/> has been advanced: the overrun's count.</summary>
    private int _currentAdvances;

    /// <summary>
    /// True while a step's own code runs (its Begin, Advance or Finish), so
    /// that code can be refused <see cref="Advance"/>, <see cref="SkipAll"/>
    /// and <see cref="Clear"/>: any of them would pull the queue out from
    /// under the loop that is walking it.
    /// </summary>
    private bool _inStep;

    private int _speed = 1;

    /// <summary>
    /// The queue ran empty: every step enqueued has finished, by itself, by
    /// an overrun or by <see cref="SkipAll"/>. Raised once per emptying,
    /// after the playback is done with its steps (a handler may call
    /// anything on it), and never by <see cref="Clear"/>: a cleared playback
    /// belongs to a view that is going away. The view's cue to slow its loop
    /// down or switch it off (nothing moves, nothing runs, A19).
    /// </summary>
    public event Action? Drained;

    /// <summary>
    /// A step reached <see cref="StepLimitTicks"/> and was finished for it.
    /// Once per such step, before <see cref="Drained"/> when the same tick
    /// emptied the queue. The view logs it as a Warning: it is a bug in a
    /// game's step, and the picture is still right.
    /// </summary>
    public event Action? StepOverran;

    /// <summary>No current step and nothing queued.</summary>
    public bool Idle => _current is null && _queue.Count == 0;

    /// <summary>
    /// Step ticks per loop tick: 1 (the default) plays at the engine's pace,
    /// 2 twice as fast. It changes the pace, never the outcome: every step
    /// sees the same calls in the same order at any speed, only fewer loop
    /// ticks pass. Read once at the start of each <see cref="Advance"/>, so a
    /// change made by a step's own code takes effect on the next tick. Less
    /// than 1 throws: a playback that never moves would never drain.
    /// </summary>
    public int Speed
    {
        get => _speed;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "A playback's speed is 1 or more.");
            _speed = value;
        }
    }

    /// <summary>
    /// Puts a step at the back of the queue. Allowed at any time, from a
    /// step's own code included (its new steps play after everything already
    /// queued). Null throws: a missing beat is the game's bug, better found
    /// at the enqueue than as a hole in the playback.
    /// </summary>
    public void Enqueue(IStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _queue.Enqueue(step);
    }

    /// <summary>
    /// One loop tick. <see cref="Speed"/> times over: if there is no current
    /// step, the next one is taken and begun; the current step is advanced
    /// once; if it answers true (or has just reached
    /// <see cref="StepLimitTicks"/>), it is dropped. A step that finishes
    /// leaves the rest of the tick to the next step only at the next pass, so
    /// at speed 1 one step advances per tick: a step that finishes on its
    /// first advance still takes a tick, and two beats never land on the same
    /// frame. When the queue runs empty, <see cref="Drained"/> is raised once.
    /// Does nothing while idle.
    /// </summary>
    public void Advance()
    {
        RefuseFromAStep(nameof(Advance));
        if (Idle)
            return;

        var passes = _speed;
        var overruns = 0;
        var dropped = false;

        _inStep = true;
        try
        {
            for (var pass = 0; pass < passes; pass++)
            {
                if (_current is null)
                {
                    if (!_queue.TryDequeue(out var next))
                        break; // emptied on an earlier pass: the rest of the tick has nothing to play
                    _current = next;
                    _currentAdvances = 0;
                    next.Begin();
                }

                var step = _current;
                var finished = step.Advance();
                _currentAdvances++;

                if (!finished && _currentAdvances >= StepLimitTicks)
                {
                    // A broken step: finish it for it, so the picture is
                    // still the end state (IStep's rule), and carry on.
                    step.Finish();
                    overruns++;
                    finished = true;
                }

                if (finished)
                {
                    _current = null;
                    dropped = true;
                }
            }
        }
        catch
        {
            // The exception is the caller's (the loop faults the game, A10);
            // the playback is left cleared and usable, never half-walked.
            DropAll();
            throw;
        }
        finally
        {
            _inStep = false;
        }

        // The events come after the walk, so a handler may call anything.
        for (var i = 0; i < overruns; i++)
            StepOverran?.Invoke();
        // Idle checked again: an overrun's handler may have enqueued more,
        // and then this was not the end of the playback after all.
        if (dropped && Idle)
            Drained?.Invoke();
    }

    /// <summary>
    /// The skip: <see cref="IStep.Finish"/> the current step, then every
    /// queued one in order (never begun: a skipped beat jumps straight to its
    /// end), then <see cref="Drained"/> once if anything was there. Steps a
    /// finishing step enqueues are finished too, in order at the back:
    /// running to the end would have played them, so skipping must leave
    /// their pictures as well. Afterwards the playback is idle and the
    /// picture is the final state. Does nothing while idle.
    /// <para>
    /// At most <see cref="SkipLimitSteps"/> steps are finished, the current
    /// one included (A35). If a step is still queued after the last of them,
    /// the chain is taken as endless: the playback is cleared, no
    /// <see cref="Drained"/> is raised, and the skip throws
    /// <see cref="InvalidOperationException"/>, which the view reports as the
    /// game's fault (A10). Exactly <see cref="SkipLimitSteps"/> steps,
    /// however they got into the queue, finish and drain as usual.
    /// </para>
    /// </summary>
    public void SkipAll()
    {
        RefuseFromAStep(nameof(SkipAll));
        if (Idle)
            return;

        var finished = 0;
        _inStep = true;
        try
        {
            if (_current is not null)
            {
                _current.Finish();
                _current = null;
                finished++;
            }
            while (_queue.Count > 0)
            {
                // Checked before each further Finish, so the limit-th step
                // is finished like any other and only a step queued after it
                // is refused: a playback of exactly the limit completes.
                if (finished >= SkipLimitSteps)
                    throw new InvalidOperationException(
                        "Playback.SkipAll finished " + SkipLimitSteps
                        + " steps and more were still queued: a step's Finish keeps enqueuing steps.");
                _queue.Dequeue().Finish();
                finished++;
            }
        }
        catch
        {
            DropAll();
            throw;
        }
        finally
        {
            _inStep = false;
        }

        Drained?.Invoke();
    }

    /// <summary>
    /// Drops every step, the current one included, with no
    /// <see cref="IStep.Finish"/> and no <see cref="Drained"/>. For a view
    /// going away (the window closed, the game left, a fault): what it drew
    /// goes with it, and the next view paints the game's final state from
    /// the state itself (A20), so nothing needs finishing.
    /// </summary>
    public void Clear()
    {
        RefuseFromAStep(nameof(Clear));
        DropAll();
    }

    private void DropAll()
    {
        _queue.Clear();
        _current = null;
        _currentAdvances = 0;
    }

    /// <summary>
    /// A step's own code may only enqueue. Advancing, skipping or clearing
    /// from inside a step would change the queue the walk is in the middle
    /// of; the exception passes out through the step to the walk's own
    /// caller, and the playback is left cleared, as for any step that throws.
    /// </summary>
    private void RefuseFromAStep(string what)
    {
        if (_inStep)
            throw new InvalidOperationException(
                "A step's own code may enqueue steps but may not call Playback." + what + ".");
    }
}
