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
/// <para><b>The rate is how often it looks, never how fast time runs</b>
/// (A55). Engine time runs at <see cref="EngineTime.TicksPerSecond"/> at
/// every rate: at <see cref="LoopRate.Full"/> a look usually hands the step
/// 1 tick, at <see cref="LoopRate.Ambient"/> (4 looks a second) the 7 or 8
/// that passed, so a quiet cue keeps the art's timing.</para>
///
/// <para><b>Never caught up.</b> After a stall (the PC busy, the window being
/// dragged, a breakpoint) at most <see cref="EngineTime.MaxTicksPerFrame"/>
/// looks' worth of ticks are made up (<see cref="MaxTicks"/>: 5 at the full
/// rate, 37 at the ambient one); the rest is dropped with its fraction. A
/// playback is cosmetic (A20), so dropping time costs nothing but the
/// frames, while catching up would play a burst of beats nobody could
/// follow.</para>
///
/// <para><b>A change of rate starts afresh</b> (A55): the time base is the
/// clock's reading at the change and nothing is carried, whether the timer is
/// new or only its interval changes. So the quiet time before a click is
/// never played into the playback the click starts: the first look after
/// <see cref="LoopRate.Full"/> is set hands the playback 1 tick, and a turn's
/// first beat starts at its start (G9).</para>
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
    /// or 1 / <see cref="EngineTime.TicksPerSecond"/> of a second: how often
    /// the loop looks, while time runs at the engine's rate at both. Every
    /// real change to a rate with a timer restarts the time base and drops the
    /// carry (A55): leaving Off (a loop switched on owes nothing for the time
    /// it was off), and Ambient to Full and back (only the interval changes,
    /// but the quiet time before a click is never played into its playback).
    /// Going Off stops and drops the timer. The same rate set again changes
    /// nothing, the carry kept. Settable from inside the loop's own step or
    /// draw. Once disposed it stays Off, whatever is set: a disposed view's
    /// late handler starts nothing.
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

            // A new rate starts afresh (A55), a new timer or only a new interval.
            _last = _clock.Elapsed;
            _carry = 0;
            var interval = Interval(value);
            if (_timer is null)
            {
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
    /// at most <see cref="MaxTicks"/> of the rate (the excess dropped with its
    /// fraction); with one tick or more, the step with that count,
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
    /// with the carried fraction, capped at <see cref="MaxTicks"/> of the
    /// current rate; moves the time base to now.
    /// </summary>
    private int TicksSince(TimeSpan now)
    {
        var gap = (now - _last).Ticks;
        _last = now;
        if (gap <= 0)
            return 0; // the host's clock never runs back; nothing to count, the carry kept

        var cap = MaxTicks(_rate);
        if (gap > LongestCountedGap)
        {
            _carry = 0;
            return cap;
        }

        var owed = _carry + gap * EngineTime.TicksPerSecond;
        var ticks = owed / TimeSpan.TicksPerSecond;
        _carry = owed % TimeSpan.TicksPerSecond;
        if (ticks > cap)
        {
            // Never caught up: the excess and its fraction are dropped.
            _carry = 0;
            return cap;
        }
        return (int)ticks;
    }

    /// <summary>
    /// The most ticks one look makes up at a rate with a timer (A55):
    /// <see cref="EngineTime.MaxTicksPerFrame"/> looks' worth of engine time,
    /// <c>MaxTicksPerFrame * TicksPerSecond / looks a second</c>, whole: 5 at
    /// Full, 37 at Ambient. A stall is dropped after five looks' time at either
    /// rate, and a look at the ambient rate, 7 or 8 ticks, is never cut short.
    /// </summary>
    internal static int MaxTicks(LoopRate rate) =>
        EngineTime.MaxTicksPerFrame * EngineTime.TicksPerSecond / LooksPerSecond(rate);

    /// <summary>The timer's interval at a rate that has one: a 30th or a 4th of a second, exactly.</summary>
    internal static TimeSpan Interval(LoopRate rate) =>
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond / LooksPerSecond(rate));

    /// <summary>How often the loop looks at a rate with a timer; Off has none.</summary>
    private static int LooksPerSecond(LoopRate rate) => rate switch
    {
        LoopRate.Full => EngineTime.TicksPerSecond,
        LoopRate.Ambient => EngineTime.AmbientPerSecond,
        _ => throw new ArgumentOutOfRangeException(nameof(rate), rate, "Only a rate with a timer looks."),
    };

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
