using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The base every game's view is built on, through a small view of the
/// tests' own: the guard (A10), the pause (G18), the lifecycle, a playback
/// that drains switching the loop off with its last picture still drawn
/// (A19), disposal from inside a step's own code (the window closing a game
/// whose step reported a fault), the overrun's one Warning, and an endless
/// skip as one fault (A35). The host is <see cref="FakeHost"/>: a fault
/// nobody meant fails the test where it happened (A41), so only the tests
/// that mean a fault set <see cref="FakeHost.ExpectFaults"/>.
/// </summary>
[Collection(LoopTimers.Name)]
public class GameViewBaseTests
{
    /// <summary>
    /// A view as a game writes one: it plays at the full rate when it
    /// enqueues and switches the loop off when the playback drains, both
    /// through the guard; it writes what it was stepped and drawn with.
    /// </summary>
    private sealed class TestView : GameViewBase
    {
        private readonly Border _root = new();

        public TestView(IGameHost host) : base(host)
        {
            Playback.Drained += () => Guard(() =>
            {
                Calls.Add("drained");
                Loop.Rate = LoopRate.Off;
            });
        }

        public readonly List<string> Calls = new();

        /// <summary>What the view shows of the playback: the value its tween last set.</summary>
        public double Shown;

        public Action<int>? OnStep;

        public override FrameworkElement Root => _root;

        public override IReadOnlyList<string> LayoutStates => Array.Empty<string>();

        public override void ShowLayoutState(string state) { }

        public GameLoop TheLoop => Loop;

        public Playback ThePlayback => Playback;

        public IGameHost TheHost => Host;

        /// <summary>A handler of the view's own, as a button's click would run.</summary>
        public void Handle(Action action) => Guard(action);

        /// <summary>Enqueues and plays, as a view does after a turn.</summary>
        public void Play(params IStep[] steps)
        {
            foreach (var step in steps)
                Playback.Enqueue(step);
            Loop.Rate = LoopRate.Full;
        }

        protected override void Step(int ticks)
        {
            Calls.Add("step " + ticks);
            OnStep?.Invoke(ticks);
        }

        protected override void Draw() => Calls.Add("draw " + Shown.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private static void After(FakeHost host, TestView view, int milliseconds)
    {
        host.ManualClock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        view.TheLoop.Tick();
    }

    [Fact]
    public void ItStartsStillWithItsHostsClock() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var host = new FakeHost();
        var view = new TestView(host);
        Assert.Same(host, view.TheHost);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.True(view.ThePlayback.Idle);
        Assert.Equal(before, GameLoop.LiveTimers);
        view.Dispose();
    });

    [Fact]
    public void ATickAdvancesThePlaybackOncePerEngineTickThenStepsThenDraws() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);
        var advances = 0;
        view.Play(Steps.Tween(10, v => { advances++; view.Shown = v; }));
        After(host, view, 100); // three engine ticks: Begin's apply(0), then three advances
        Assert.Equal(4, advances);
        Assert.Equal(new[] { "step 3", "draw 0.3" }, view.Calls);
        view.Dispose();
    });

    [Fact]
    public void GuardReportsAndTheViewSurvives() => Sta.Run(() =>
    {
        var host = new FakeHost { ExpectFaults = true };
        var view = new TestView(host);
        var boom = new InvalidOperationException("a click handler's bug");

        view.Handle(() => throw boom); // nothing leaves the guard
        Assert.Same(boom, Assert.Single(host.Faults));

        var ran = false;
        view.Handle(() => ran = true);
        Assert.True(ran);
        view.Dispose();
    });

    [Fact]
    public void PausedTheGuardRunsNothingAndResumedItDoes() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);
        var runs = 0;

        view.Pause();
        view.Pause(); // idempotent
        Assert.True(view.TheLoop.Paused);
        view.Handle(() => runs++);
        Assert.Equal(0, runs);

        view.Resume();
        view.Resume();
        Assert.False(view.TheLoop.Paused);
        view.Handle(() => runs++);
        Assert.Equal(1, runs);
        view.Dispose();
    });

    [Fact]
    public void APausedPlaybackFreezesAndResumesWhereItWas() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);
        view.Play(Steps.Tween(6, v => view.Shown = v));
        After(host, view, 100); // 3 of 6
        view.Pause();
        After(host, view, 5_000);
        Assert.Equal(0.5, view.Shown, 9);
        view.Resume();
        After(host, view, 100); // the other 3, nothing owed for the pause
        Assert.Equal(new[] { "step 3", "draw 0.5", "drained", "step 3", "draw 1" }, view.Calls);
        view.Dispose();
    });

    [Fact]
    public void DisposeTwiceIsFineAndLeavesNoTimer() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var host = new FakeHost();
        var view = new TestView(host);
        view.Play(Steps.Wait(90));
        Assert.Equal(before + 1, GameLoop.LiveTimers);

        view.Dispose();
        view.Dispose();
        Assert.Equal(before, GameLoop.LiveTimers);
        Assert.True(view.ThePlayback.Idle); // dropped, not finished: the next view paints the state (A20)

        After(host, view, 100);
        Assert.Empty(view.Calls);
        var ran = false;
        view.Handle(() => ran = true); // a disposed view takes no input
        Assert.False(ran);
    });

    [Fact]
    public void APlaybackThatDrainsSwitchesTheLoopOffAndItsLastPictureIsStillDrawn() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var host = new FakeHost();
        var view = new TestView(host);
        view.Play(Steps.Tween(3, v => view.Shown = v));
        Assert.Equal(before + 1, GameLoop.LiveTimers);

        After(host, view, 100); // the tween's third advance drains the playback, inside the step
        Assert.Equal(new[] { "drained", "step 3", "draw 1" }, view.Calls);
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);
        view.Dispose();
    });

    [Fact]
    public void DisposeFromInsideAStepsOwnCodeDoesNotThrowAndNothingIsSteppedOrDrawnAfter() => Sta.Run(() =>
    {
        // What the window does when a step's code reports a fault: it disposes
        // the view while the playback's walk is on the stack, where Clear refuses.
        var before = GameLoop.LiveTimers;
        var host = new FakeHost();
        var view = new TestView(host);
        var log = new List<string>();
        var later = new ProbeStep("later", log);
        view.Play(Steps.Call(() => view.Dispose()), later);

        After(host, view, 100); // no exception
        Assert.Empty(view.Calls);
        Assert.Empty(log); // the step after it was never begun
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);

        After(host, view, 100);
        Assert.Empty(view.Calls);
        Assert.Empty(log);
        view.Dispose(); // and again, from outside
        Assert.Empty(host.Faults);
    });

    [Fact]
    public void AFaultReportedFromAStepThatClosesTheViewEndsAsThatOneFault() => Sta.Run(() =>
    {
        // The whole path as the window takes it: the step's code reports
        // through the guard, and the window's answer to the fault is to dispose
        // the view, still inside the step.
        var host = new FakeHost { ExpectFaults = true };
        var view = new TestView(host);
        var log = new List<string>();
        view.Play(Steps.Call(() =>
        {
            view.Handle(() => throw new InvalidOperationException("a step's handler bug"));
            if (host.Faults.Count > 0) view.Dispose();
        }), new ProbeStep("later", log));

        After(host, view, 100);
        Assert.IsType<InvalidOperationException>(Assert.Single(host.Faults));
        Assert.Empty(view.Calls);
        Assert.Empty(log);
    });

    [Fact]
    public void AThrowingStepFaultsTheGameOnceAndStopsTheLoop() => Sta.Run(() =>
    {
        var before = GameLoop.LiveTimers;
        var host = new FakeHost { ExpectFaults = true };
        var view = new TestView(host);
        view.OnStep = _ => throw new NotSupportedException("a step's bug");
        view.Play(Steps.Wait(30));

        After(host, view, 100);
        Assert.IsType<NotSupportedException>(Assert.Single(host.Faults));
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate);
        Assert.Equal(before, GameLoop.LiveTimers);
        view.Dispose();
    });

    [Fact]
    public void AnOverrunStepWritesOneWarningToTheHostsLog() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);
        view.Play(ProbeStep.Never("stuck", new List<string>()));

        // 3 engine ticks a look: the step is finished for it at its 300th advance.
        for (var i = 0; i < Playback.StepLimitTicks / 3 + 5; i++)
            After(host, view, 100);

        Assert.Equal("a playback step ran past its limit and was finished", Assert.Single(host.Collected.Warnings));
        Assert.Equal(LoopRate.Off, view.TheLoop.Rate); // and it drained like any playback
        Assert.Empty(host.Faults);
        view.Dispose();
    });

    [Fact]
    public void ASkipOverAnEndlessChainThroughTheGuardIsOneFaultAndNoException() => Sta.Run(() =>
    {
        var host = new FakeHost { ExpectFaults = true };
        var view = new TestView(host);
        var log = new List<string>();
        var count = 0;
        IStep Endless()
        {
            var step = new ProbeStep("s" + count++, log);
            step.OnFinish = () => view.ThePlayback.Enqueue(Endless());
            return step;
        }
        view.Play(Endless());

        view.Handle(() => view.ThePlayback.SkipAll());

        Assert.IsType<InvalidOperationException>(Assert.Single(host.Faults));
        Assert.True(view.ThePlayback.Idle);
        view.Dispose();
    });

    [Fact]
    public void TheHostIsRequired() => Sta.Run(() =>
        Assert.Throws<ArgumentNullException>(() => new TestView(null!)));
}
