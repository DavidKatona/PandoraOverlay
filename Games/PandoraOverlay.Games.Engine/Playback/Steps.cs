namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The stock beats a game's playback is built from: a hold
/// (<see cref="Wait"/>), a change drawn at once (<see cref="Call"/>), a change
/// drawn over time (<see cref="Tween"/>), and the two ways of putting beats
/// together (<see cref="Sequence"/>, <see cref="Together"/>). A game's own
/// steps are rare: most of a game's events are a <see cref="Tween"/> that
/// moves a sprite or a bar, inside a <see cref="Sequence"/> with a
/// <see cref="Call"/> that sets what does not move.
/// <para>
/// Every step here keeps <see cref="IStep"/>'s rule (running it to the end
/// and finishing it at any point leave the same picture) and is held to it
/// by the tests, nested ones included. They shrug off calls out of order (a
/// second <see cref="IStep.Begin"/>, an <see cref="IStep.Advance"/> with no
/// Begin, which begins first, anything after they finished, which does
/// nothing), so a game's own composite step that slips cannot break the
/// picture. Each is played once: make a fresh one for every playback.
/// </para>
/// </summary>
public static class Steps
{
    /// <summary>
    /// A hold that draws nothing: finishes on its <paramref name="ticks"/>-th
    /// advance (0 or less finishes on the first, the shortest any step can
    /// be). Between two of a game's events, it is the gap that lets the eye
    /// tell them apart (G9). More than <see cref="Playback.StepLimitTicks"/>
    /// is cut short as an overrun.
    /// </summary>
    public static IStep Wait(int ticks) => new WaitStep(ticks);

    /// <summary>
    /// A change drawn at once: runs <paramref name="action"/> exactly once,
    /// in Begin or in Finish, whichever comes first, and finishes on its
    /// first advance. The action must only change what is drawn (A20).
    /// </summary>
    public static IStep Call(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new CallStep(action);
    }

    /// <summary>
    /// A change drawn over <paramref name="ticks"/> ticks:
    /// <paramref name="apply"/>(0) at Begin; at the n-th advance
    /// <paramref name="apply"/>(ease(n / ticks)); finished at
    /// <c>n == ticks</c> with <paramref name="apply"/>(1); and
    /// <see cref="IStep.Finish"/> is <paramref name="apply"/>(1) exactly.
    /// <paramref name="apply"/>(1) is always the last call a tween makes, and
    /// it makes it exactly once, so the end state is the same however the
    /// tween ended. 0 ticks or less is one tick, as for <see cref="Wait"/>.
    /// </summary>
    public static IStep Tween(int ticks, Action<double> apply, Easing easing = Easing.Linear)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (!Enum.IsDefined(easing))
            throw new ArgumentOutOfRangeException(nameof(easing), easing, "Not an easing the engine knows.");
        return new TweenStep(ticks, apply, easing);
    }

    /// <summary>
    /// One after another: a step begins on the advance after the one before
    /// it finished, so <c>Sequence(a, b, c)</c> plays exactly as enqueuing
    /// <c>a</c>, <c>b</c> and <c>c</c> would, tick for tick. Finish finishes
    /// the current step and every later one, in order (the later ones never
    /// begun). Empty, it finishes on its first advance. It is one step to the
    /// playback, so it is held to <see cref="Playback.StepLimitTicks"/> as a
    /// whole.
    /// </summary>
    public static IStep Sequence(params IStep[] steps) => new SequenceStep(Checked(steps));

    /// <summary>
    /// All at once: every step begins with the group, each is advanced until
    /// it finishes, and the group finishes when the last one does. Finish
    /// finishes every step not yet finished, in the order given. The steps
    /// must draw different things (a sprite and a bar, two sprites): two that
    /// drew the same thing would leave whichever happened to finish last,
    /// and a skip cannot know which that would have been. Empty, it finishes
    /// on its first advance.
    /// </summary>
    public static IStep Together(params IStep[] steps) => new TogetherStep(Checked(steps));

    /// <summary>
    /// The easing curves, in their one home: <paramref name="t"/> in [0, 1]
    /// to the share of the change drawn. Each is exactly 0 at 0 and exactly 1
    /// at 1.
    /// </summary>
    internal static double Ease(Easing easing, double t) => easing switch
    {
        Easing.Linear => t,
        Easing.InOut => t * t * (3 - 2 * t),
        Easing.Out => 1 - (1 - t) * (1 - t),
        _ => throw new ArgumentOutOfRangeException(nameof(easing), easing, "Not an easing the engine knows."),
    };

    /// <summary>
    /// A copy of the caller's list, with no hole in it: a missing beat is the
    /// game's bug, found when the group is made rather than as a gap in the
    /// playback, and the copy means a later change to the caller's array
    /// cannot change a group already queued.
    /// </summary>
    private static IStep[] Checked(IStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var copy = (IStep[])steps.Clone();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] is null)
                throw new ArgumentException("Step " + i + " of the group is null.", nameof(steps));
        }
        return copy;
    }

    private sealed class WaitStep : IStep
    {
        private readonly int _ticks;
        private int _advances;
        private bool _finished;

        public WaitStep(int ticks) => _ticks = Math.Max(1, ticks);

        public void Begin()
        {
            // Draws nothing: there is nothing to start.
        }

        public bool Advance()
        {
            if (!_finished && ++_advances >= _ticks)
                _finished = true;
            return _finished;
        }

        public void Finish() => _finished = true;
    }

    private sealed class CallStep : IStep
    {
        /// <summary>Null once it has run: that is the "exactly once", even when the action throws or re-enters.</summary>
        private Action? _action;

        public CallStep(Action action) => _action = action;

        public void Begin() => RunOnce();

        public bool Advance()
        {
            RunOnce(); // only when it was never begun: Begin has run it otherwise
            return true;
        }

        public void Finish() => RunOnce();

        private void RunOnce()
        {
            var action = _action;
            if (action is null)
                return;
            _action = null;
            action();
        }
    }

    private sealed class TweenStep : IStep
    {
        private readonly int _ticks;
        private readonly Action<double> _apply;
        private readonly Easing _easing;
        private int _advances;
        private bool _begun;
        private bool _finished;

        public TweenStep(int ticks, Action<double> apply, Easing easing)
        {
            _ticks = Math.Max(1, ticks);
            _apply = apply;
            _easing = easing;
        }

        public void Begin()
        {
            if (_begun || _finished)
                return; // after a Finish, apply(1) stays the last call
            _begun = true;
            _apply(0);
        }

        public bool Advance()
        {
            if (_finished)
                return true;
            if (!_begun)
                Begin();
            _advances++;
            if (_advances >= _ticks)
            {
                End();
                return true;
            }
            _apply(Ease(_easing, (double)_advances / _ticks));
            return false;
        }

        public void Finish()
        {
            if (!_finished)
                End();
        }

        /// <summary>
        /// The end, by itself or by Finish: the literal 1, not ease(1)
        /// worked out (every curve gives 1 there, but the end state must not
        /// rest on a floating-point sum). Marked finished first, so nothing
        /// can call apply after it, not even a re-entering apply.
        /// </summary>
        private void End()
        {
            _finished = true;
            _apply(1);
        }
    }

    private sealed class SequenceStep : IStep
    {
        private readonly IStep[] _steps;

        /// <summary>The step being played; the length once all have finished.</summary>
        private int _index;

        /// <summary>Whether <c>_steps[_index]</c> has been begun.</summary>
        private bool _currentBegun;

        public SequenceStep(IStep[] steps) => _steps = steps;

        public void Begin() => BeginCurrent();

        public bool Advance()
        {
            if (_index >= _steps.Length)
                return true;
            BeginCurrent();
            if (_steps[_index].Advance())
            {
                // The next one begins on the next advance, as in the queue:
                // two beats never land on the same tick.
                _index++;
                _currentBegun = false;
            }
            return _index >= _steps.Length;
        }

        public void Finish()
        {
            while (_index < _steps.Length)
            {
                // Moved on before the call, so no step is finished twice
                // whatever its Finish does.
                var step = _steps[_index++];
                _currentBegun = false;
                step.Finish();
            }
        }

        private void BeginCurrent()
        {
            if (_index >= _steps.Length || _currentBegun)
                return;
            _currentBegun = true;
            _steps[_index].Begin();
        }
    }

    private sealed class TogetherStep : IStep
    {
        private readonly IStep[] _steps;
        private readonly bool[] _finished;
        private int _left;
        private bool _begun;

        public TogetherStep(IStep[] steps)
        {
            _steps = steps;
            _finished = new bool[steps.Length];
            _left = steps.Length;
        }

        public void Begin()
        {
            if (_begun)
                return;
            _begun = true;
            for (var i = 0; i < _steps.Length; i++)
            {
                if (!_finished[i])
                    _steps[i].Begin();
            }
        }

        public bool Advance()
        {
            if (!_begun)
                Begin();
            for (var i = 0; i < _steps.Length; i++)
            {
                // A finished step is never advanced again: it is done
                // drawing, and its last picture stands.
                if (!_finished[i] && _steps[i].Advance())
                {
                    _finished[i] = true;
                    _left--;
                }
            }
            return _left == 0;
        }

        public void Finish()
        {
            _begun = true; // nothing is begun after the group has finished
            for (var i = 0; i < _steps.Length; i++)
            {
                if (_finished[i])
                    continue;
                _finished[i] = true;
                _left--;
                _steps[i].Finish();
            }
        }
    }
}
