using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The engine's loop (A19) on a <see cref="ManualClock"/>, its timer's
/// handler called by hand: whole ticks and their carry, the cap after a stall,
/// the pause, no timer while Off, a fault contained (A10), and the two calls
/// that arrive from inside its own step (a <c>Drained</c> handler switching it
/// off, a view disposing it). The timer itself never fires here: no
/// dispatcher runs on the test's thread.
/// </summary>
[Collection(LoopTimers.Name)]
public class GameLoopTests
{
    /// <summary>A loop on a manual clock that writes what it was called with.</summary>
    private sealed class Probe
    {
        public readonly ManualClock Clock = new();
        public readonly List<string> Calls = new();
        public readonly List<Exception> Faults = new();
        public Action<int>? OnStep;
        public Action? OnDraw;
        public readonly GameLoop Loop;

        public Probe()
        {
            Loop = new GameLoop(Clock,
                ticks => { Calls.Add("step " + ticks); OnStep?.Invoke(ticks); },
                () => { Calls.Add("draw"); OnDraw?.Invoke(); },
                error => { Calls.Add("fault"); Faults.Add(error); });
        }

        public void After(int milliseconds)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Loop.Tick();
        }
    }

    [Fact]
    public void AHundredMillisecondsIsThreeTicks() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(100);
        Assert.Equal(new[] { "step 3", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void FractionsCarryToTheNextLook() => Sta.Run(() =>
    {
        // 20 ms is 0.6 of a tick: 0.6, 1.2, 1.8, 2.4, 3.0 in all, so ticks at
        // the second, fourth and fifth looks, one each, and nothing between.
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        for (var i = 0; i < 5; i++)
            p.After(20);
        Assert.Equal(new[] { "step 1", "draw", "step 1", "draw", "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void AThirdOfATickIsCarriedExactlyOverALongPlayback() => Sta.Run(() =>
    {
        // 33 ms looks for a minute: 0.99 of a tick each, so 1800 looks make
        // 1782 ticks exactly, with no drift from rounding.
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        var ticks = 0;
        p.OnStep = n => ticks += n;
        for (var i = 0; i < 1800; i++)
            p.After(33);
        Assert.Equal(1782, ticks);
        p.Loop.Dispose();
    });

    [Fact]
    public void ATenSecondStallIsFiveTicksAndTheNextSmallStepIsSmall() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(10_000);
        p.After(40); // 1.2 ticks: one, and no leftover of the stall
        Assert.Equal(new[] { "step 5", "draw", "step 1", "draw" }, p.Calls);
        p.After(30); // the 0.2 carried, plus 0.9: one more
        Assert.Equal(new[] { "step 5", "draw", "step 1", "draw", "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void JustOverTheCapIsTheCapWithNoFractionKept() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(210); // 6.3 ticks: 5, the 1.3 dropped
        p.After(20);  // 0.6: nothing
        Assert.Equal(new[] { "step 5", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void AtTheCapButNotOverItTheFractionIsKept() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(199); // 5.97 ticks: 5 whole ones, none in excess, the 0.97 carried
        p.After(20);  // 0.97 + 0.6: one
        Assert.Equal(new[] { "step 5", "draw", "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void PausedMakesNoTicksAndOwesNone() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(20); // 0.6 owed
        p.Loop.Paused = true;
        p.After(1_000);
        p.After(1_000);
        Assert.Empty(p.Calls);
        p.Loop.Paused = false;
        p.After(20); // the second paused and the 0.6 before it are not owed: 0.6
        Assert.Empty(p.Calls);
        p.After(20);
        Assert.Equal(new[] { "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void OffHoldsNoTimer() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var p = new Probe();
        Assert.Equal(LoopRate.Off, p.Loop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);

        p.Loop.Rate = LoopRate.Full;
        Assert.Equal(before + 1, GameLoop.LiveTimers);
        p.Loop.Rate = LoopRate.Ambient; // the same timer, another interval
        Assert.Equal(before + 1, GameLoop.LiveTimers);
        p.Loop.Rate = LoopRate.Off;
        Assert.Equal(before, GameLoop.LiveTimers);

        p.After(1_000); // a stray look while Off does nothing
        Assert.Empty(p.Calls);
        p.Loop.Dispose();
        Assert.Equal(before, GameLoop.LiveTimers);
    });

    [Fact]
    public void LeavingOffRestartsTheTimeBase() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(20); // 0.6 owed, then off for a long while
        p.Loop.Rate = LoopRate.Off;
        p.Clock.Advance(TimeSpan.FromSeconds(30));
        p.Loop.Rate = LoopRate.Full;
        p.After(20); // only 0.6 since it came back: nothing
        Assert.Empty(p.Calls);
        p.After(20);
        Assert.Equal(new[] { "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void AtAmbientTimeStillRunsAtThirtyTicksASecond() => Sta.Run(() =>
    {
        // Four looks 250 ms apart, 7.5 ticks each: 7, 8, 7, 8, the half carried,
        // 30 ticks in the second and nothing lost (A55).
        var p = new Probe();
        p.Loop.Rate = LoopRate.Ambient;
        for (var i = 0; i < 4; i++)
            p.After(250);
        Assert.Equal(new[] { "step 7", "draw", "step 8", "draw", "step 7", "draw", "step 8", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void ATenSecondStallAtAmbientIsThirtySevenAndTheNextLookIsSeven() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Ambient;
        p.After(10_000); // five looks' worth: 5 x 30 / 4 = 37
        p.After(250);    // 7.5: 7, the stall's fraction gone with its excess
        Assert.Equal(new[] { "step 37", "draw", "step 7", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void GoingFullFromAmbientStartsAfreshSoTheFirstBeatStartsAtItsStart() => Sta.Run(() =>
    {
        // At rest, 200 ms pass with no look; the click sets Full; the first look,
        // 34 ms later, hands over 1 tick, not the quiet time before the click (A55, G9).
        var p = new Probe();
        p.Loop.Rate = LoopRate.Ambient;
        p.Clock.Advance(TimeSpan.FromMilliseconds(200));
        p.Loop.Rate = LoopRate.Full;
        p.After(34);
        Assert.Equal(new[] { "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void AStepThatSetsAmbientDropsTheCarry() => Sta.Run(() =>
    {
        // Full, a look after 50 ms: 1.5 ticks, a step of 1 and half a tick
        // carried; the step sets Ambient, as a Drained handler does; the look
        // 250 ms later is 7, not 8: the carry went with the change.
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.OnStep = _ => p.Loop.Rate = LoopRate.Ambient;
        p.After(50);
        p.OnStep = null;
        p.After(250);
        Assert.Equal(new[] { "step 1", "draw", "step 7", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void TheSameRateSetAgainKeepsTheCarry() => Sta.Run(() =>
    {
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.After(20); // 0.6: no step
        p.Loop.Rate = LoopRate.Full;
        p.After(20); // 1.2: one
        Assert.Equal(new[] { "step 1", "draw" }, p.Calls);
        p.Loop.Dispose();
    });

    [Fact]
    public void TheCapIsFiveLooksWorthAtEachRate()
    {
        Assert.Equal(5, GameLoop.MaxTicks(LoopRate.Full));
        Assert.Equal(37, GameLoop.MaxTicks(LoopRate.Ambient));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameLoop.MaxTicks(LoopRate.Off));
    }

    [Fact]
    public void TheTimersIntervalsAreTheEnginesRates()
    {
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / EngineTime.TicksPerSecond), GameLoop.Interval(LoopRate.Full));
        Assert.Equal(TimeSpan.FromMilliseconds(250), GameLoop.Interval(LoopRate.Ambient));
    }

    [Fact]
    public void AThrowingStepCallsFaultOnceAndStopsTheLoop() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var p = new Probe();
        var boom = new InvalidOperationException("a step's bug");
        p.OnStep = _ => throw boom;
        p.Loop.Rate = LoopRate.Full;

        p.After(100); // the exception does not leave Tick
        Assert.Equal(new[] { "step 3", "fault" }, p.Calls);
        Assert.Same(boom, Assert.Single(p.Faults));
        Assert.Equal(LoopRate.Off, p.Loop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);

        p.After(100); // stopped: no more calls
        Assert.Single(p.Faults);
        p.Loop.Dispose();
    });

    [Fact]
    public void AThrowingDrawCallsFaultOnceAndStopsTheLoop() => Sta.Run(() =>
    {
        var p = new Probe();
        p.OnDraw = () => throw new ArgumentException("a draw's bug");
        p.Loop.Rate = LoopRate.Full;
        p.After(100);
        Assert.Equal(new[] { "step 3", "draw", "fault" }, p.Calls);
        Assert.IsType<ArgumentException>(Assert.Single(p.Faults));
        Assert.Equal(LoopRate.Off, p.Loop.Rate);
        p.Loop.Dispose();
    });

    [Fact]
    public void AStepThatSetsOffStillGetsItsDrawOnceAndLeavesNoTimer() => Sta.Run(() =>
    {
        // What a Drained handler does, inside the playback's Advance, inside the step.
        var before = GameLoop.LiveTimers;
        var p = new Probe();
        p.OnStep = _ => p.Loop.Rate = LoopRate.Off;
        p.Loop.Rate = LoopRate.Full;
        p.After(100);
        Assert.Equal(new[] { "step 3", "draw" }, p.Calls);
        Assert.Equal(LoopRate.Off, p.Loop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);
        p.Loop.Dispose();
    });

    [Fact]
    public void AStepThatSetsAmbientKeepsItsTimerAndGetsItsDraw() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var p = new Probe();
        p.OnStep = _ => p.Loop.Rate = LoopRate.Ambient;
        p.Loop.Rate = LoopRate.Full;
        p.After(100);
        Assert.Equal(new[] { "step 3", "draw" }, p.Calls);
        Assert.Equal(before + 1, GameLoop.LiveTimers);
        p.Loop.Dispose();
        Assert.Equal(before, GameLoop.LiveTimers);
    });

    [Fact]
    public void AStepThatDisposesTheLoopIsFollowedByNoDraw() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var p = new Probe();
        p.OnStep = _ => p.Loop.Dispose();
        p.Loop.Rate = LoopRate.Full;
        p.After(100);
        Assert.Equal(new[] { "step 3" }, p.Calls);
        Assert.Equal(before, GameLoop.LiveTimers);
        p.After(100);
        Assert.Equal(new[] { "step 3" }, p.Calls);
    });

    [Fact]
    public void ADisposedLoopStaysOffWhateverIsSet() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var p = new Probe();
        p.Loop.Rate = LoopRate.Full;
        p.Loop.Dispose();
        p.Loop.Dispose(); // idempotent
        p.Loop.Rate = LoopRate.Full; // a late handler of a view that went away
        Assert.Equal(LoopRate.Off, p.Loop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);
        p.After(100);
        Assert.Empty(p.Calls);
    });

    [Fact]
    public void ARateTheLoopDoesNotKnowIsRefused() => Sta.Run(() =>
    {
        var p = new Probe();
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Loop.Rate = (LoopRate)7);
        Assert.Equal(LoopRate.Off, p.Loop.Rate);
    });

    [Fact]
    public void NothingIsMissing()
    {
        var clock = new ManualClock();
        Assert.Throws<ArgumentNullException>(() => new GameLoop(null!, _ => { }, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new GameLoop(clock, null!, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new GameLoop(clock, _ => { }, null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new GameLoop(clock, _ => { }, () => { }, null!));
    }
}
