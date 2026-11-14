using System.Windows.Threading;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Fixed steps on the host's clock: the only thing in a game that ticks
/// (A19). A timer exists only while <see cref="Rate"/> is not
/// <see cref="LoopRate.Off"/>: nothing moves, nothing runs. A view sets
/// <see cref="LoopRate.Full"/> when it enqueues a playback and
/// <see cref="LoopRate.Off"/> (or <see cref="LoopRate.Ambient"/> for a quiet
/// cue) when the playback drains, so a game waiting for the player's click
/// costs the PC nothing at all while The Isle runs underneath.
///
/// <para><b>Time is the host's, counted in the engine's ticks.</b> The timer
/// only says "look now"; how far to move is read from
/// <see cref="IGameClock.Elapsed"/>, the host's monotonic clock: the whole
/// engine ticks (<see cref="EngineTime.TicksPerSecond"/>) since the last look,
/// the fraction carried to the next. So a late timer moves the game as far as
/// time moved, and a test moves the clock by hand and calls
/// <see cref="Tick"/>. The carry is kept exactly, in units of a
/// <see cref="TimeSpan"/> tick over the engine's rate, so no rounding drifts
/// over a long playback.</para>
///
/// <para><b>Never caught up.</b> After a stall (the PC busy, the window being
/// dragged, a breakpoint) at most <see cref="EngineTime.MaxTicksPerFrame"/>
/// ticks are made up; the rest is dropped with its fraction. A playback is
/// cosmetic (A20), so dropping time costs nothing but the frames, while
/// catching up would play a burst of beats nobody could follow.</para>
///
/// <para><b>Paused</b> (the health warning, G18): the timer keeps its rate but
/// every tick only restarts the time base, so nothing is stepped and nothing
/// is owed when it resumes: the playback carries on where it froze.</para>
///
/// <para><b>Contained</b> (A10). An exception from the step or the draw stops
/// the loop (<see cref="Rate"/> Off) and is handed to the fault callback, the
/// host's <c>Fault</c>; it is never rethrown into the dispatcher, where it
/// would reach the app.</para>
///
/// <para><b>Re-entered on purpose.</b> The step may set <see cref="Rate"/>,
/// and does: the playback raises <c>Drained</c> inside its own
/// <c>Advance</c>, inside this loop's step, and the view's handler switches
/// the loop off there. The tick in progress still draws once after its step,
/// so the playback's last picture reaches the screen; only the timer is
/// already gone. <see cref="Dispose"/> from inside the step is the other
/// case: then nothing more is called, the draw included, because what it
/// would draw is going away.</para>
///
/// <para>Single-threaded, on the dispatcher of the thread that first gives it
/// a rate, like every WPF object a view owns.</para>
/// </summary>
public sealed class GameLoop : IDisposable
{
    /// <summary>
    /// A gap longer than an hour is not worked out tick by tick: it is a stall
    /// like any other, and the multiplication that counts ticks stays far from
    /// overflowing a long.
    /// </summary>
    private static readonly long LongestCountedGap = TimeSpan.TicksPerHour;

    private static int s_liveTimers;

    private readonly IGameClock _clock;
    private readonly Action<int> _step;
    private readonly Action _draw;
    private readonly Action<Exception> _fault;

    private DispatcherTimer? _timer;
    private LoopRate _rate;
    private bool _disposed;

    /// <summary>The clock's reading at the last look: the time base.</summary>
    private TimeSpan _last;

    /// <summary>The fraction of a tick owed, in units of 1 / <see cref="TimeSpan.TicksPerSecond"/> of an engine tick: always under <see cref="TimeSpan.TicksPerSecond"/>.</summary>
    private long _carry;

    /// <summary>
    /// A loop over <paramref name="clock"/> that calls
    /// <paramref name="step"/>(ticks) then <paramref name="draw"/>() on each
    /// tick that moved at least one engine tick, and hands any exception
    /// either of them throws to <paramref name="fault"/>. It starts
    /// <see cref="LoopRate.Off"/>: no timer until a rate is set.
    /// </summary>
    public GameLoop(IGameClock clock, Action<int> step, Action draw, Action<Exception> fault)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _step = step ?? throw new ArgumentNullException(nameof(step));
        _draw = draw ?? throw new ArgumentNullException(nameof(draw));
        _fault = fault ?? throw new ArgumentNullException(nameof(fault));
    }

    /// <summary>
    /// How many loops hold a timer right now: 0 while no view is on screen
    /// (the test that nothing runs while nothing is shown). Every loop of the
    /// process counts, whatever thread made it.
    /// </summary>
    public static int LiveTimers => Volatile.Read(ref s_liveTimers);

    /// <summary>
    /// <see cref="LoopRate.Off"/>: no timer exists. <see cref="LoopRate.Ambient"/>
    /// and <see cref="LoopRate.Full"/>: a <see cref="DispatcherTimer"/> at
    /// <see cref="DispatcherPriority.Render"/> every 1 / <see cref="EngineTime.AmbientPerSecond"/>
    /// or 1 / <see cref="EngineTime.TicksPerSecond"/> of a second. Leaving Off
    /// restarts the time base (a loop switched on owes nothing for the time it
    /// was off); going Off stops and drops the timer; Ambient to Full and back
    /// only changes the interval, the carry kept. Settable from inside the
    /// loop's own step or draw. Once disposed it stays Off, whatever is set:
    /// a disposed view's late handler starts nothing.
    /// </summary>
    public LoopRate Rate
    {
        get => _rate;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a rate the loop knows.");
            if (_disposed || value == _rate)
                return;

            _rate = value;
            if (value == LoopRate.Off)
            {
                DropTimer();
                return;
            }

            var interval = Interval(value);
            if (_timer is null)
            {
                _last = _clock.Elapsed;
                _carry = 0;
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = interval };
                _timer.Tick += OnTimer;
                _timer.Start();
                Interlocked.Increment(ref s_liveTimers);
            }
            else
            {
                _timer.Interval = interval;
            }
        }
    }

    /// <summary>The window covered the view: every tick only restarts the time base, so nothing is stepped and nothing is owed.</summary>
    public bool Paused { get; set; }

    /// <summary>
    /// The timer's handler, public so tests drive it after moving the clock.
    /// Off or disposed: nothing. Paused: the time base restarts, nothing else.
    /// Otherwise the whole ticks since the last look, the fraction carried,
    /// at most <see cref="EngineTime.MaxTicksPerFrame"/> (the excess dropped
    /// with its fraction); with one tick or more, the step with that count,
    /// then the draw, unless the step disposed the loop. An exception from
    /// either: Off, then the fault callback.
    /// </summary>
    public void Tick()
    {
        if (_disposed || _rate == LoopRate.Off)
            return;

        var now = _clock.Elapsed;
        if (Paused)
        {
            _last = now;
            _carry = 0;
            return;
        }

        var ticks = TicksSince(now);
        if (ticks == 0)
            return;

        try
        {
            _step(ticks);
            // A step that disposed the loop (its view went away from inside
            // it) gets no draw: what it would paint is gone. A step that only
            // set the rate Off still gets it, so the last picture is shown.
            if (_disposed)
                return;
            _draw();
        }
        catch (Exception error)
        {
            Rate = LoopRate.Off;
            _fault(error);
        }
    }

    /// <summary>Off, for good: the timer gone, every later rate ignored. Idempotent, and safe from inside the loop's own step.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        Rate = LoopRate.Off;
        _disposed = true;
    }

    /// <summary>
    /// The whole engine ticks between the last look and <paramref name="now"/>,
    /// with the carried fraction, capped; moves the time base to now.
    /// </summary>
    private int TicksSince(TimeSpan now)
    {
        var gap = (now - _last).Ticks;
        _last = now;
        if (gap <= 0)
            return 0; // the host's clock never runs back; nothing to count, the carry kept

        if (gap > LongestCountedGap)
        {
            _carry = 0;
            return EngineTime.MaxTicksPerFrame;
        }

        var owed = _carry + gap * EngineTime.TicksPerSecond;
        var ticks = owed / TimeSpan.TicksPerSecond;
        _carry = owed % TimeSpan.TicksPerSecond;
        if (ticks > EngineTime.MaxTicksPerFrame)
        {
            // Never caught up: the excess and its fraction are dropped.
            _carry = 0;
            return EngineTime.MaxTicksPerFrame;
        }
        return (int)ticks;
    }

    /// <summary>The timer's interval at a rate that has one: a 30th or a 4th of a second, exactly.</summary>
    internal static TimeSpan Interval(LoopRate rate) => TimeSpan.FromTicks(TimeSpan.TicksPerSecond / (rate switch
    {
        LoopRate.Ambient => EngineTime.AmbientPerSecond,
        _ => EngineTime.TicksPerSecond,
    }));

    private void DropTimer()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer.Tick -= OnTimer;
        _timer = null;
        Interlocked.Decrement(ref s_liveTimers);
    }

    private void OnTimer(object? sender, EventArgs e) => Tick();
}
