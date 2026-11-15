using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

        /// <summary>A sound, as a step or a handler of the view plays one (A79's first layer).</summary>
        public void Sound(GameSound sound) => base.Play(sound);

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

    // ---- Play: the first of A79's three layers ----------------------------------------------

    private static readonly GameSound Bite = new("bite", new short[] { 1, 2, 3 });
    private static readonly GameSound Thud = new("thud", new short[] { 4 });

    /// <summary>A sound reaches the host once per play, in order: the host decides the rest (the app's gate).</summary>
    [Fact]
    public void APlayReachesTheHostOnce() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);

        view.Sound(Bite);
        view.Sound(Thud);

        Assert.Equal(new[] { "bite", "thud" }, host.Sound.Played);
        Assert.Same(Bite, host.Sound.Sounds[0]); // the object itself: the host goes by it
        view.Dispose();
    });

    /// <summary>A paused view plays nothing, as it takes no input (A19, A56); resumed, it plays again.</summary>
    [Fact]
    public void APausedViewPlaysNothingAndResumedItPlaysAgain() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);

        view.Pause();
        view.Sound(Bite);
        Assert.Empty(host.Sound.Played);

        view.Resume();
        view.Sound(Bite);
        Assert.Equal(new[] { "bite" }, host.Sound.Played);
        view.Dispose();
    });

    /// <summary>A disposed view plays nothing ever again: a step of a closed window's view is silence.</summary>
    [Fact]
    public void ADisposedViewNeverPlays() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);
        view.Sound(Bite);

        view.Dispose();
        view.Sound(Bite);
        view.Resume();
        view.Sound(Thud);

        Assert.Equal(new[] { "bite" }, host.Sound.Played);
    });

    /// <summary>A step that plays, as a game's beat will: it reaches the host while the playback runs, and a paused playback reaches no cue.</summary>
    [Fact]
    public void AStepsSoundReachesTheHostOnlyWhileThePlaybackRuns() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new TestView(host);
        view.Play(Steps.Wait(3), Steps.Call(() => view.Sound(Bite)));

        view.Pause();
        After(host, view, 500);
        Assert.Empty(host.Sound.Played);

        view.Resume();
        for (var i = 0; i < 10; i++) After(host, view, 100);
        Assert.Equal(new[] { "bite" }, host.Sound.Played);
        view.Dispose();
    });

    [Fact]
    public void ASoundIsRequired() => Sta.Run(() =>
    {
        var view = new TestView(new FakeHost());
        Assert.Throws<ArgumentNullException>(() => view.Sound(null!));
        view.Dispose();
    });

    [Fact]
    public void TheHostIsRequired() => Sta.Run(() =>
        Assert.Throws<ArgumentNullException>(() => new TestView(null!)));

    // ---- The click rule (G27, A105; PLAN-CLICKS.md 1.6, 6.2) -----------------------------------
    //
    // A headless window can't hold a mouse capture, so no button of a view never shown
    // clicks for a synthetic release (the planner's probe). The release is opened and
    // closed by raising PreviewMouseLeftButtonUp and MouseLeftButtonUp on the root, as
    // WPF raises them there for a real one, and the click by raising Click on the
    // button between them, its toggle first, as ButtonBase.OnClick does inside a real
    // release; one test drives WPF's own MouseUp from a button that clicks as a
    // captured one does. A real left click on a game's control is the owner's to try.

    /// <summary>
    /// A view whose root holds one of each kind of control a game might have, and a
    /// card, each wired as a game wires its own: a button's Click runs the view's
    /// handler through the guard, the card's release names it as a click.
    /// </summary>
    private sealed class ClickView : GameViewBase
    {
        private FrameworkElement _root;

        public ClickView(IGameHost host) : base(host)
        {
            Panel = new StackPanel();
            foreach (var control in new ButtonBase[] { Button, Check, Radio, Toggle, Repeat, Held })
            {
                control.Click += (_, _) => Guard(() =>
                {
                    Handled++;
                    OnClick?.Invoke();
                });
                Panel.Children.Add(control);
            }
            Card.MouseLeftButtonUp += (_, _) => Guard(() => Clicked(CardKind));
            Panel.Children.Add(Card);
            _root = Panel;
        }

        public StackPanel Panel { get; }
        public Button Button { get; } = new();
        public CheckBox Check { get; } = new();
        public RadioButton Radio { get; } = new();
        public ToggleButton Toggle { get; } = new();
        public RepeatButton Repeat { get; } = new();
        public HeldButton Held { get; } = new();
        public Border Card { get; } = new();

        /// <summary>What the card's release names it as.</summary>
        public GameClick CardKind { get; set; } = GameClick.Select;

        /// <summary>How many times a button's handler ran: the game took the click.</summary>
        public int Handled { get; private set; }

        /// <summary>What the game's handler does with a click, besides counting it.</summary>
        public Action? OnClick { get; set; }

        public override FrameworkElement Root => _root;

        public override IReadOnlyList<string> LayoutStates => Array.Empty<string>();

        public override void ShowLayoutState(string state) { }

        /// <summary>The game's root becomes another element (the next time the host asks, it is wired too).</summary>
        public void UseRoot(FrameworkElement root) => _root = root;

        public Playback ThePlayback => Playback;

        public void Arm() => Arming();

        public void Name(GameClick click) => Clicked(click);

        public void Sound(GameSound sound) => Play(sound);

        /// <summary>A turn begun, as a view does after a move: its steps enqueued and the loop at the full rate.</summary>
        public void Begin(params IStep[] steps)
        {
            foreach (var step in steps)
                Playback.Enqueue(step);
            Loop.Rate = LoopRate.Full;
        }

        protected override void Step(int ticks) { }

        protected override void Draw() { }
    }

    /// <summary>A button that clicks on its own release, as a button that captured the press does (ButtonBase raises Click from its OnMouseLeftButtonUp, then the release bubbles on, handled).</summary>
    private sealed class HeldButton : Button
    {
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);   // marks the release handled, as with a capture
            OnClick();
        }
    }

    private static MouseButtonEventArgs Left(RoutedEvent routed) =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routed };

    /// <summary>A left release on the view as the root sees it: opened, <paramref name="inside"/>, closed.</summary>
    private static void Release(IGameView view, Action inside)
    {
        var root = view.Root;
        root.RaiseEvent(Left(UIElement.PreviewMouseLeftButtonUpEvent));
        inside();
        root.RaiseEvent(Left(UIElement.MouseLeftButtonUpEvent));
    }

    /// <summary>What a release on <paramref name="control"/> does to it: a toggle first (ToggleButton.OnToggle), then its Click.</summary>
    private static void Click(ButtonBase control)
    {
        switch (control)
        {
            case RadioButton radio:
                radio.IsChecked = true;
                break;
            case ToggleButton toggle:
                toggle.IsChecked = toggle.IsChecked != true;
                break;
        }
        control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    /// <summary>A click of <paramref name="control"/> by the left button: inside a release.</summary>
    private static void Clicks(IGameView view, ButtonBase control) => Release(view, () => Click(control));

    [Fact]
    public void EachKindOfButtonClicksByItsType() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);

        Clicks(view, view.Button);
        Clicks(view, view.Check);    // ticked
        Clicks(view, view.Check);    // cleared
        Clicks(view, view.Radio);
        Clicks(view, view.Toggle);   // a tab, a section, a menu opener: select either way
        Clicks(view, view.Toggle);

        Assert.Equal(new[] { GameClick.Press, GameClick.ToggleOn, GameClick.ToggleOff, GameClick.Select, GameClick.Select, GameClick.Select },
            host.Sound.Clicks);
        Assert.Equal(6, view.Handled);
        Assert.Empty(host.Sound.Played);   // a click is the host's, never one of the game's sounds
    });

    [Fact]
    public void ACheckClicksAsItShowsAfterTheGamesHandler() => Sta.Run(() =>
    {
        // The game puts the box back as its state says (a toggle it refused): the click follows the box.
        var host = new FakeHost();
        using var view = new ClickView(host);
        view.OnClick = () => view.Check.IsChecked = false;

        Clicks(view, view.Check);

        Assert.Equal(new[] { GameClick.ToggleOff }, host.Sound.Clicks);
    });

    [Fact]
    public void OneReleaseIsOneClick() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);

        Release(view, () =>
        {
            Click(view.Button);
            Click(view.Toggle);   // a second click in the same release: the first one's
            view.Name(GameClick.Back);
            view.Name(GameClick.Press);   // a second card: the first one's
        });
        Release(view, () =>
        {
            Click(view.Toggle);
            Click(view.Button);
        });

        Assert.Equal(new[] { GameClick.Back, GameClick.Select }, host.Sound.Clicks);
    });

    [Fact]
    public void AClickWithNoReleaseAroundItIsSilent() => Sta.Run(() =>
    {
        // A key, automation and code reach a button's Click with no left release open.
        var host = new FakeHost();
        using var view = new ClickView(host);
        var root = ((IGameView)view).Root;

        Click(view.Button);
        Click(view.Check);
        view.Arm();
        view.Name(GameClick.Select);
        root.RaiseEvent(Left(UIElement.MouseLeftButtonUpEvent));   // an end with no start
        Assert.Empty(host.Sound.Clicks);
        Assert.Equal(2, view.Handled);   // the game still took them

        // Nothing of it leaks into the next release, and a release on nothing is silent.
        Release(view, () => { });
        Clicks(view, view.Button);
        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);
    });

    [Fact]
    public void APausedOrDisposedViewClicksNothing() => Sta.Run(() =>
    {
        var host = new FakeHost();
        var view = new ClickView(host);

        view.Pause();
        Clicks(view, view.Button);
        Assert.Empty(host.Sound.Clicks);
        Assert.Equal(0, view.Handled);   // the guard took no input either

        view.Resume();
        Clicks(view, view.Button);
        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);

        // Covered, or closed, while its own handler ran: decided as it is at the end.
        view.OnClick = () => view.Pause();
        Clicks(view, view.Button);
        view.Resume();
        view.OnClick = () => view.Dispose();
        Clicks(view, view.Button);
        view.OnClick = null;
        Clicks(view, view.Button);

        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);
    });

    [Fact]
    public void AClickDuringAPlaybackIsASkipAndSilent() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);
        view.Begin(Steps.Wait(30));

        Clicks(view, view.Button);   // running at the release and after it
        view.OnClick = () => view.ThePlayback.SkipAll();
        Clicks(view, view.Check);    // the skip: idle after the release, not at it
        Assert.True(view.ThePlayback.Idle);
        Assert.Empty(host.Sound.Clicks);

        view.OnClick = null;
        Clicks(view, view.Button);   // at rest again
        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);
    });

    [Fact]
    public void AClickThatBeginsATurnHasTheTurnForItsSound() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);
        view.OnClick = () => view.Begin(Steps.Wait(30));

        Clicks(view, view.Button);

        Assert.False(view.ThePlayback.Idle);
        Assert.Empty(host.Sound.Clicks);
    });

    private static readonly GameSound Chime = new("chime", new short[] { 7 });

    [Fact]
    public void AClickThatPlayedItsOwnCueIsThatCue() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);

        view.Sound(Thud);            // before the release: not this click's
        Clicks(view, view.Button);
        view.OnClick = () => view.Sound(Chime);
        Clicks(view, view.Button);   // its own cue: no click on top of it
        view.OnClick = () => view.Arm();
        Release(view, () =>
        {
            Click(view.Button);
            view.Sound(Chime);       // an armed click that played a cue: the cue
        });
        Release(view, () =>
        {
            view.Name(GameClick.Select);
            view.Sound(Chime);       // a card whose action played its cue: the cue
        });

        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);
        Assert.Equal(new[] { "thud", "chime", "chime", "chime" }, host.Sound.Played);
    });

    [Fact]
    public void ARepeatButtonAndAButtonMarkedNoneAreSilent() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);

        Clicks(view, view.Repeat);
        GameClicks.SetCue(view.Button, GameClickMark.None);
        Clicks(view, view.Button);
        view.OnClick = () => view.Arm();
        Clicks(view, view.Button);   // None says nothing, armed or not
        Release(view, () =>
        {
            Click(view.Button);
            view.Name(GameClick.Select);   // nor for a card named with it
        });

        Assert.Empty(host.Sound.Clicks);
        Assert.Equal(4, view.Handled);   // the game took all four: only the sound is left out
    });

    [Fact]
    public void AClickThatArmsIsArm() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);

        view.OnClick = () => view.Arm();
        Clicks(view, view.Button);   // the first click asks
        Clicks(view, view.Check);
        view.OnClick = null;
        Clicks(view, view.Button);   // the second acts: an ordinary press
        Release(view, () => view.Arm());   // an arming with no button or card: still the arm

        Assert.Equal(new[] { GameClick.Arm, GameClick.Arm, GameClick.Press, GameClick.Arm }, host.Sound.Clicks);
    });

    [Fact]
    public void ACardTheGameNamesClicksAsItsKindAndOnlyInARelease() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);

        // The card's own release handler names it, as a game's card does.
        Release(view, () => view.Card.RaiseEvent(Left(UIElement.MouseLeftButtonUpEvent)));
        view.CardKind = GameClick.Back;
        Release(view, () => view.Card.RaiseEvent(Left(UIElement.MouseLeftButtonUpEvent)));
        // A card named with a button: the card's kind.
        Release(view, () =>
        {
            Click(view.Button);
            view.Name(GameClick.Select);
        });
        // Outside a release: nothing.
        view.Card.RaiseEvent(Left(UIElement.MouseLeftButtonUpEvent));
        view.Name(GameClick.Press);

        Assert.Equal(new[] { GameClick.Select, GameClick.Back, GameClick.Select }, host.Sound.Clicks);
    });

    [Fact]
    public void AMarkSaysWhatItsButtonIsAndIsNotInherited() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);
        Assert.Equal(GameClickMark.Auto, GameClicks.GetCue(view.Button));   // the default

        GameClicks.SetCue(view.Button, GameClickMark.Back);
        Clicks(view, view.Button);
        GameClicks.SetCue(view.Check, GameClickMark.Press);
        Clicks(view, view.Check);
        GameClicks.SetCue(view.Radio, GameClickMark.Select);
        Clicks(view, view.Radio);
        GameClicks.SetCue(view.Toggle, GameClickMark.Toggle);
        Clicks(view, view.Toggle);   // on
        Clicks(view, view.Toggle);   // off
        GameClicks.SetCue(view.Held, GameClickMark.Toggle);
        Clicks(view, view.Held);     // a button that is no toggle: off
        // A mark on the panel reaches none of its buttons.
        GameClicks.SetCue(view.Panel, GameClickMark.None);
        GameClicks.SetCue(view.Held, GameClickMark.Auto);
        Clicks(view, view.Held);

        Assert.Equal(new[]
        {
            GameClick.Back, GameClick.Press, GameClick.Select, GameClick.ToggleOn, GameClick.ToggleOff, GameClick.ToggleOff, GameClick.Press,
        }, host.Sound.Clicks);
    });

    [Fact]
    public void TheRuleIsWiredOnTheRootTheHostIsGivenOnceEach() => Sta.Run(() =>
    {
        var host = new FakeHost();
        using var view = new ClickView(host);
        var first = view.Panel;

        // The game's own reads of its root wire nothing: only the host's way in does.
        Assert.Same(first, view.Root);
        first.RaiseEvent(Left(UIElement.PreviewMouseLeftButtonUpEvent));
        Click(view.Button);
        first.RaiseEvent(Left(UIElement.MouseLeftButtonUpEvent));
        Assert.Equal(0, view.RootsWired);
        Assert.Empty(host.Sound.Clicks);

        IGameView asHost = view;
        Assert.Same(first, asHost.Root);
        Assert.Same(first, asHost.Root);
        Assert.Same(first, asHost.Root);
        Assert.Equal(1, view.RootsWired);
        Clicks(view, view.Button);
        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);   // once, not once per ask

        // A root the game changes to is wired the first time the host is given it.
        var second = new Grid();
        var other = new ToggleButton();
        second.Children.Add(other);
        view.UseRoot(second);
        Assert.Same(second, asHost.Root);
        Assert.Equal(2, view.RootsWired);
        Clicks(view, other);
        Assert.Equal(new[] { GameClick.Press, GameClick.Select }, host.Sound.Clicks);

        // And the first again: never twice.
        view.UseRoot(first);
        Assert.Same(first, asHost.Root);
        Assert.Equal(2, view.RootsWired);
        Clicks(view, view.Button);
        Assert.Equal(new[] { GameClick.Press, GameClick.Select, GameClick.Press }, host.Sound.Clicks);
    });

    [Fact]
    public void TheReleaseReachesTheRootThroughWpfsOwnMouseUpWhenTheButtonHandledIt() => Sta.Run(() =>
    {
        // The real order: the release tunnels to the button as PreviewMouseUp, then
        // bubbles back as MouseUp; WPF re-raises each as its left-button event on every
        // element on the way; the button clicks inside its own MouseLeftButtonUp and
        // marks the release handled, so only a handler added with handledEventsToo still
        // hears it at the root.
        var host = new FakeHost();
        using var view = new ClickView(host);
        var root = ((IGameView)view).Root;
        var unhandledHeard = 0;
        root.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => unhandledHeard++));

        view.Held.RaiseEvent(Left(Mouse.PreviewMouseUpEvent));
        view.Held.RaiseEvent(Left(Mouse.MouseUpEvent));
        Assert.Equal(1, view.Handled);
        Assert.Equal(0, unhandledHeard);   // the button handled it
        Assert.Equal(new[] { GameClick.Press }, host.Sound.Clicks);

        // A card's release, the same way: its own handler names it, then the root decides.
        view.Card.RaiseEvent(Left(Mouse.PreviewMouseUpEvent));
        view.Card.RaiseEvent(Left(Mouse.MouseUpEvent));
        Assert.Equal(1, unhandledHeard);   // nobody handled this one
        Assert.Equal(new[] { GameClick.Press, GameClick.Select }, host.Sound.Clicks);

        // A handler that takes its own button out of the view (a page changed): the
        // release still reaches the root along the route WPF built before it ran.
        view.OnClick = () => view.Panel.Children.Remove(view.Held);
        view.Held.RaiseEvent(Left(Mouse.PreviewMouseUpEvent));
        view.Held.RaiseEvent(Left(Mouse.MouseUpEvent));
        Assert.Null(System.Windows.Media.VisualTreeHelper.GetParent(view.Held));   // out of the view
        Assert.Equal(new[] { GameClick.Press, GameClick.Select, GameClick.Press }, host.Sound.Clicks);
    });

    /// <summary>A host whose click throws, as the Contract says no host does: what the rule does with it.</summary>
    private sealed class ThrowingClicks : IGameHost, IGameSound
    {
        public FakeHost Inner { get; } = new() { ExpectFaults = true };

        public IGameClock Clock => Inner.Clock;

        public IGameStorage Storage => Inner.Storage;

        public IGameLog Log => Inner.Log;

        public IGameSound Sound => this;

        public void TileChanged() => Inner.TileChanged();

        public void Fault(Exception error) => Inner.Fault(error);

        public void Play(GameSound sound) => Inner.Sound.Play(sound);

        public void Click(GameClick click) => throw new InvalidOperationException("a host's click bug");
    }

    [Fact]
    public void AClickThatThrowsIsTheGuardsNeverTheGamesHandler() => Sta.Run(() =>
    {
        var host = new ThrowingClicks();
        using var view = new ClickView(host);

        Clicks(view, view.Button);   // nothing escapes into the release

        Assert.IsType<InvalidOperationException>(Assert.Single(host.Inner.Faults));
        Assert.Equal(1, view.Handled);
    });
}
