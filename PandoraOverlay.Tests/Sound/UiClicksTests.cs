using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// A sink that plays nothing and remembers every click the layer handed it
/// (<see cref="IClickSink"/>), in order, with its scope.
/// </summary>
internal sealed class FakeClickSink : IClickSink
{
    public List<(ClickCue Cue, ClickScope Scope)> Clicks { get; } = new();

    public void Click(ClickCue cue, ClickScope scope) => Clicks.Add((cue, scope));
}

/// <summary>
/// The click layer's test seam (A107), for this file's tests and for every
/// test that clicks a surface through the layer (C05's, C06's). A window a
/// test builds can't hold a mouse capture, so ButtonBase's own release
/// never clicks there (the planner's probe, <c>click-input.txt</c>): a
/// synthetic left MouseUp DOES go through the input manager and reach the
/// button, but ButtonBase only marks it handled, since the button was never
/// pressed. So <see cref="Click"/> sends a real left MouseUp item through this
/// thread's input manager, aimed at the button, and calls the button's own
/// <c>OnClick</c> inside it, from the button's MouseUp (after ButtonBase's own
/// OnMouseLeftButtonUp has run), which is where ButtonBase calls it on a real
/// release: the bracket is the real one, opened by the real input manager;
/// only the capture is stood in for. Every call runs on the thread the
/// controls were made on.
/// </summary>
internal static class ClickSeam
{
    private static readonly MethodInfo OnClickMethod =
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!;

    /// <summary>What MainWindow does, with a fake: the layer attached to this thread and a fresh sink made this thread's.</summary>
    public static FakeClickSink Listen()
    {
        UiClicks.Attach();
        var sink = new FakeClickSink();
        UiClicks.Sink = sink;
        return sink;
    }

    /// <summary>A test on an STA thread of its own, the layer attached there and a fake sink set on that thread only.</summary>
    public static void OnSta(Action<FakeClickSink> test) => TheThemeOnScreen.OnSta(() => test(Listen()));

    /// <summary>The button's own OnClick (a ToggleButton toggles in it, then Click is raised), as ButtonBase calls it at a release.</summary>
    public static void OnClick(ButtonBase button) => OnClickMethod.Invoke(button, null);

    /// <summary>
    /// A release of <paramref name="changed"/> through this thread's input
    /// manager, aimed at <paramref name="target"/>, with <paramref name="inside"/>
    /// run within it, from the target's own MouseUp (handled events too).
    /// </summary>
    public static void Release(UIElement target, Action inside, MouseButton changed = MouseButton.Left)
    {
        MouseButtonEventHandler up = (_, _) => inside();
        target.AddHandler(Mouse.MouseUpEvent, up, handledEventsToo: true);
        try
        {
            InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, changed)
            {
                RoutedEvent = Mouse.MouseUpEvent,
                Source = target,
            });
        }
        finally
        {
            target.RemoveHandler(Mouse.MouseUpEvent, up);
        }
    }

    /// <summary>A left click on the button: its own OnClick inside a left release on it, then the dispatcher run, so an armed button's cue is decided.</summary>
    public static void Click(ButtonBase button)
    {
        Release(button, () => OnClick(button));
        Pump();
    }

    /// <summary>Everything queued on this thread's dispatcher run (an armed button's decision, automation's Invoke).</summary>
    public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    /// <summary>A slider's left press, as its PreviewMouseLeftButtonDown reaches it.</summary>
    public static void Press(Slider slider) =>
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });

    /// <summary>A slider's left release, as its PreviewMouseLeftButtonUp reaches it.</summary>
    public static void Lift(Slider slider) =>
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
}

/// <summary>
/// The click layer's handler (<see cref="UiClicks"/>; A98, A100, A104), each on
/// an STA thread of its own with a fake sink on that thread only: a click
/// inside a left release sounds once, at the source, by type or by mark; a
/// key, an access key, code, automation, a check set in code, a hover and a
/// focus change are silent; so are a disabled button, a RepeatButton, the
/// None mark and the Game scope; the Games scope reaches the sink as such;
/// an armed button is decided after its handler; a slider sounds once at a
/// release that moved it; <see cref="UiClicks.Clicked"/> plays in its
/// element's scope. And the bracket itself: nested items, cancelled items,
/// another button clicked from inside a release, a nested dispatcher frame.
/// A real mouse click through ButtonBase's own capture is NOT reached here
/// (no test can: <see cref="ClickSeam"/>).
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class UiClicksTests
{
    private static (ClickCue, ClickScope) Overlay(ClickCue cue) => (cue, ClickScope.Overlay);

    /// <summary>A window shown far off the screen, never activated, so a control in it can take the keyboard focus.</summary>
    private static Window ShownOffScreen(UIElement content)
    {
        var window = new Window
        {
            Content = content, Width = 200, Height = 200, Left = -20000, Top = -20000,
            ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
        };
        window.Show();
        return window;
    }

    /// <summary>A key through this thread's input manager, to the focused element's window.</summary>
    private static void Key(UIElement focused, Key key, RoutedEvent routed) =>
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(focused)!, Environment.TickCount, key) { RoutedEvent = routed });

    // ---- A click inside a left release ----------------------------------------------------

    [Fact]
    public void AClickInsideALeftReleaseSoundsOnceAtItsSource() => ClickSeam.OnSta(sink =>
    {
        var inner = new Button { Content = "inner" };
        var outer = new Button { Content = inner }; // a button inside another: the click bubbles through both
        var raised = 0;
        outer.Click += (_, _) => raised++;

        ClickSeam.Click(inner);

        Assert.Equal(1, raised); // the click reached the outer button too
        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks); // but sounded once, for its source
        Assert.Equal(0, UiClicks.OpenFrames);
    });

    [Fact]
    public void TheCueIsTheControlsByType() => ClickSeam.OnSta(sink =>
    {
        var button = new Button();
        var check = new CheckBox();
        var mixed = new CheckBox { IsThreeState = true };
        var radio = new RadioButton();
        var toggle = new ToggleButton();

        ClickSeam.Click(button);
        ClickSeam.Click(check); // ticked
        ClickSeam.Click(check); // cleared
        ClickSeam.Click(mixed); // ticked
        ClickSeam.Click(mixed); // mixed: read as off
        ClickSeam.Click(radio);
        ClickSeam.Click(toggle);
        ClickSeam.Click(toggle);

        Assert.Equal(new[]
        {
            Overlay(ClickCue.Press), Overlay(ClickCue.ToggleOn), Overlay(ClickCue.ToggleOff), Overlay(ClickCue.ToggleOn),
            Overlay(ClickCue.ToggleOff), Overlay(ClickCue.Select), Overlay(ClickCue.Select), Overlay(ClickCue.Select),
        }, sink.Clicks);
        Assert.Null(mixed.IsChecked);
    });

    [Fact]
    public void AMarkOverridesTheType() => ClickSeam.OnSta(sink =>
    {
        var back = new Button();
        UiClicks.SetCue(back, ClickMark.Back);
        var select = new Button();
        UiClicks.SetCue(select, ClickMark.Select);
        var press = new CheckBox();
        UiClicks.SetCue(press, ClickMark.Press);
        var tick = new ToggleButton();
        UiClicks.SetCue(tick, ClickMark.Toggle);
        var none = new Button();
        UiClicks.SetCue(none, ClickMark.None);

        foreach (var b in new ButtonBase[] { back, select, press, tick, tick, none }) ClickSeam.Click(b);

        Assert.Equal(new[]
        {
            Overlay(ClickCue.Back), Overlay(ClickCue.Select), Overlay(ClickCue.Press), Overlay(ClickCue.ToggleOn), Overlay(ClickCue.ToggleOff),
        }, sink.Clicks); // None: nothing
        Assert.Equal(ClickMark.Auto, UiClicks.GetCue(new Button()));
    });

    [Fact]
    public void TheMarkIsTheControlsOwnAndNotInherited() => ClickSeam.OnSta(sink =>
    {
        var button = new Button();
        var panel = new StackPanel();
        UiClicks.SetCue(panel, ClickMark.None);
        panel.Children.Add(button);

        ClickSeam.Click(button);

        Assert.Equal(ClickMark.Auto, UiClicks.GetCue(button));
        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks);
    });

    // ---- Keys, automation and code: nothing -----------------------------------------------------

    [Fact]
    public void SpaceAndEnterThroughTheInputManagerAreSilent() => ClickSeam.OnSta(sink =>
    {
        var button = new Button { Content = "b" };
        var raised = 0;
        button.Click += (_, _) => raised++;
        var window = ShownOffScreen(new StackPanel { Children = { button } });
        try
        {
            Assert.True(button.Focus());
            Key(button, System.Windows.Input.Key.Space, Keyboard.KeyDownEvent);
            Key(button, System.Windows.Input.Key.Space, Keyboard.KeyUpEvent);
            Assert.Equal(1, raised); // Space clicked the button, through the input manager
            Key(button, System.Windows.Input.Key.Enter, Keyboard.KeyDownEvent);
            Key(button, System.Windows.Input.Key.Enter, Keyboard.KeyUpEvent);
            Assert.Equal(2, raised); // and so did Enter
            ClickSeam.Pump();
        }
        finally
        {
            window.Close();
        }

        Assert.Empty(sink.Clicks);
        Assert.Equal(0, UiClicks.OpenFrames);
    });

    [Fact]
    public void AnAccessKeyIsSilent() => ClickSeam.OnSta(sink =>
    {
        var button = new Button { Content = "b" };
        var raised = 0;
        button.Click += (_, _) => raised++;
        var window = ShownOffScreen(new StackPanel { Children = { button } });
        try
        {
            AccessKeyManager.Register("Q", button);
            AccessKeyManager.ProcessKey(PresentationSource.FromVisual(button), "Q", false); // in the window's scope, as Alt+Q would be
            ClickSeam.Pump();
        }
        finally
        {
            AccessKeyManager.Unregister("Q", button);
            window.Close();
        }

        Assert.Equal(1, raised);
        Assert.Empty(sink.Clicks);
    });

    [Fact]
    public void CodeAndAutomationAreSilent() => ClickSeam.OnSta(sink =>
    {
        var button = new Button();
        var raised = 0;
        button.Click += (_, _) => raised++;
        var check = new CheckBox();

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); // code
        ClickSeam.OnClick(button); // the button's own OnClick, outside any release
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); // automation
        ClickSeam.Pump();
        check.IsChecked = true; // a check set in code: no click at all

        Assert.Equal(3, raised);
        Assert.Empty(sink.Clicks);
    });

    [Fact]
    public void AHoverAndAFocusChangeAreSilent() => ClickSeam.OnSta(sink =>
    {
        var a = new Button { Content = "a" };
        var b = new Button { Content = "b" };
        var window = ShownOffScreen(new StackPanel { Children = { a, b } });
        try
        {
            a.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            Assert.True(a.Focus());
            Assert.True(b.Focus());
            ClickSeam.Pump();
        }
        finally
        {
            window.Close();
        }

        Assert.Empty(sink.Clicks);
    });

    [Fact]
    public void ADisabledButtonAndARepeatButtonAreSilent() => ClickSeam.OnSta(sink =>
    {
        var disabled = new Button { IsEnabled = false };
        var repeat = new RepeatButton();
        var raised = 0;
        disabled.Click += (_, _) => raised++;
        repeat.Click += (_, _) => raised++;

        ClickSeam.Click(disabled);
        ClickSeam.Click(repeat);

        Assert.Equal(2, raised); // both clicked
        Assert.Empty(sink.Clicks);
    });

    // ---- The scopes ---------------------------------------------------------------------------

    [Fact]
    public void TheGameScopeIsSilentAndTheGamesScopeReachesTheSinkBothInherited() => ClickSeam.OnSta(sink =>
    {
        var inGame = new Button();
        var game = new Border { Child = new StackPanel { Children = { inGame } } };
        UiClicks.SetScope(game, ClickScope.Game);
        var inGames = new Button();
        var games = new Border { Child = new StackPanel { Children = { inGames } } };
        UiClicks.SetScope(games, ClickScope.Games);

        ClickSeam.Click(inGame);
        ClickSeam.Click(inGames);

        Assert.Equal(ClickScope.Game, UiClicks.GetScope(inGame));
        Assert.Equal(new[] { (ClickCue.Press, ClickScope.Games) }, sink.Clicks);
        Assert.Equal(ClickScope.Overlay, UiClicks.GetScope(new Button()));
    });

    // ---- Armed --------------------------------------------------------------------------------

    [Fact]
    public void AnArmedButtonIsArmWhenItsHandlerArmsItAndPressWhenItActs() => ClickSeam.OnSta(sink =>
    {
        var button = new Button();
        UiClicks.SetCue(button, ClickMark.Arm);
        var armed = false;
        var acted = 0;
        button.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                UiClicks.Armed(button);
                return;
            }
            armed = false;
            acted++;
        };

        ClickSeam.Release(button, () => ClickSeam.OnClick(button));
        Assert.Empty(sink.Clicks); // decided after the handler, not before it
        ClickSeam.Pump();
        Assert.Equal(new[] { Overlay(ClickCue.Arm) }, sink.Clicks);

        ClickSeam.Click(button);
        Assert.Equal(new[] { Overlay(ClickCue.Arm), Overlay(ClickCue.Press) }, sink.Clicks);
        Assert.Equal(1, acted);
    });

    [Fact]
    public void AnArmedCallFromAnotherClickIsNotThisOnes() => ClickSeam.OnSta(sink =>
    {
        var a = new Button();
        var b = new Button();
        UiClicks.SetCue(b, ClickMark.Arm);
        a.Click += (_, _) => UiClicks.Armed(b); // a stray call, outside b's own click

        ClickSeam.Click(a);
        ClickSeam.Click(b);

        Assert.Equal(new[] { Overlay(ClickCue.Press), Overlay(ClickCue.Press) }, sink.Clicks);
    });

    // ---- The bracket itself -------------------------------------------------------------------------

    [Fact]
    public void ARightReleaseAndAReleaseOutsideTheButtonAreSilent() => ClickSeam.OnSta(sink =>
    {
        var button = new Button();
        var panel = new StackPanel { Children = { button } };

        ClickSeam.Release(button, () => ClickSeam.OnClick(button), MouseButton.Right);
        ClickSeam.Release(panel, () => ClickSeam.OnClick(button)); // released on its parent, not on it
        ClickSeam.Pump();

        Assert.Empty(sink.Clicks);
    });

    [Fact]
    public void AfterTheReleaseTheSameButtonsCodeClickIsSilent() => ClickSeam.OnSta(sink =>
    {
        var button = new Button();

        ClickSeam.Click(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        ClickSeam.OnClick(button);

        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks);
        Assert.Equal(0, UiClicks.OpenFrames);
    });

    /// <summary>A handler clicking another button, at once or inside a dispatcher frame of its own (a dialog's loop): not the mouse's click.</summary>
    [Fact]
    public void AnotherButtonClickedFromInsideAReleaseIsSilent() => ClickSeam.OnSta(sink =>
    {
        var a = new Button();
        var other = new Button();
        var later = new Button();
        var raised = 0;
        other.Click += (_, _) => raised++;
        later.Click += (_, _) => raised++;
        a.Click += (_, _) =>
        {
            other.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => ClickSeam.OnClick(later), DispatcherPriority.Background);
        };

        ClickSeam.Click(a);

        Assert.Equal(2, raised);
        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks); // a's own, and only it
    });

    /// <summary>
    /// A key handled inside a release (a dialog's message loop): its click is
    /// the key's, the innermost item, never the release's under it, even on
    /// the very button the release is aimed at.
    /// </summary>
    [Fact]
    public void AKeyHandledInsideAReleaseIsSilentAndTheReleaseStillClicks() => ClickSeam.OnSta(sink =>
    {
        var a = new Button { Content = "a" };
        var b = new Button { Content = "b" };
        var raisedA = 0;
        var raisedB = 0;
        a.Click += (_, _) => raisedA++;
        b.Click += (_, _) => raisedB++;
        var window = ShownOffScreen(new StackPanel { Children = { a, b } });
        try
        {
            ClickSeam.Release(a, () =>
            {
                Assert.True(a.Focus());
                Key(a, System.Windows.Input.Key.Space, Keyboard.KeyDownEvent); // the release's own button, by a key inside it
                Key(a, System.Windows.Input.Key.Space, Keyboard.KeyUpEvent);
                Assert.True(b.Focus());
                Key(b, System.Windows.Input.Key.Space, Keyboard.KeyDownEvent);
                Key(b, System.Windows.Input.Key.Space, Keyboard.KeyUpEvent);
                ClickSeam.OnClick(a);
            });
            ClickSeam.Pump();
        }
        finally
        {
            window.Close();
        }

        Assert.Equal((2, 1), (raisedA, raisedB)); // both keys clicked
        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks); // only the release's own click sounded
        Assert.Equal(0, UiClicks.OpenFrames);
    });

    /// <summary>
    /// An item cancelled inside a release (WPF's own devices cancel raw reports
    /// nested in a key press: probed) gets no PostProcessInput; its frame must
    /// not stay on top of the release, or every real click after a cancelled
    /// nested report would be silent. The canceller is registered before the
    /// layer, so it runs AFTER it, as WPF's own handlers do.
    /// </summary>
    [Fact]
    public void AnItemCancelledInsideAReleaseDoesNotHideIt() => TheThemeOnScreen.OnSta(() =>
    {
        InputEventArgs? cancel = null;
        var cancelled = 0;
        var notified = 0;
        var input = InputManager.Current;
        input.PreProcessInput += (_, e) =>
        {
            if (!ReferenceEquals(e.StagingItem.Input, cancel)) return;
            e.Cancel();
            cancelled++;
        };
        input.PreNotifyInput += (_, e) =>
        {
            if (ReferenceEquals(e.StagingItem.Input, cancel)) notified++;
        };
        var sink = ClickSeam.Listen();
        var a = new Button();
        var elsewhere = new Button();

        ClickSeam.Release(a, () =>
        {
            cancel = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent, Source = elsewhere };
            input.ProcessInput(cancel);
            ClickSeam.OnClick(a);
        });
        for (var i = 0; i < 100; i++)
        {
            cancel = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent, Source = a };
            input.ProcessInput(cancel); // a cancelled left release: never a bracket, and never kept
        }
        ClickSeam.OnClick(a);
        ClickSeam.Pump();

        Assert.Equal((101, 0), (cancelled, notified)); // each was cancelled, and none was raised
        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks);
        Assert.Equal(0, UiClicks.OpenFrames);
    });

    /// <summary>The same with the canceller registered after the layer (it runs first): the layer's frame is dead from the start.</summary>
    [Fact]
    public void AnItemCancelledBeforeTheLayerSeesItIsNoBracket() => ClickSeam.OnSta(sink =>
    {
        var a = new Button();
        InputEventArgs? cancel = null;
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (ReferenceEquals(e.StagingItem.Input, cancel)) e.Cancel();
        };

        cancel = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent, Source = a };
        InputManager.Current.ProcessInput(cancel);
        ClickSeam.OnClick(a);
        ClickSeam.Release(a, () => ClickSeam.OnClick(a));

        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks); // only the release that was raised
        Assert.Equal(0, UiClicks.OpenFrames);
    });

    [Fact]
    public void AttachTwiceIsAttachOnce() => ClickSeam.OnSta(sink =>
    {
        UiClicks.Attach();
        UiClicks.Attach();
        var button = new Button();

        ClickSeam.Click(button);

        Assert.Equal(new[] { Overlay(ClickCue.Press) }, sink.Clicks);
    });

    [Fact]
    public void WithNoSinkOrNoAttachNothingSoundsAndNothingThrows()
    {
        TheThemeOnScreen.OnSta(() =>
        {
            UiClicks.Attach();
            var button = new Button();
            ClickSeam.Click(button); // no sink on this thread
            UiClicks.Clicked(button, ClickCue.Select);
            Assert.Null(UiClicks.Sink);
        });
        TheThemeOnScreen.OnSta(() =>
        {
            UiClicks.Attach(); // the class handlers exist for the whole process from here on
        });
        TheThemeOnScreen.OnSta(() =>
        {
            var sink = new FakeClickSink();
            UiClicks.Sink = sink; // a sink, but this thread never attached: no bracket, so no mouse click
            var button = new Button();
            ClickSeam.Click(button);
            Assert.Empty(sink.Clicks);
            Assert.Equal(0, UiClicks.OpenFrames);
        });
    }

    [Fact]
    public void ASinkIsThisThreadsOnly()
    {
        FakeClickSink? mine = null;
        TheThemeOnScreen.OnSta(() => mine = ClickSeam.Listen());
        TheThemeOnScreen.OnSta(() => Assert.Null(UiClicks.Sink));
        Assert.NotNull(mine);
    }

    // ---- Sliders --------------------------------------------------------------------------------

    /// <summary>A slider whose own press moves it, as IsMoveToPointEnabled does on a click on the track.</summary>
    private sealed class JumpingSlider : Slider
    {
        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Value = Maximum;
            base.OnPreviewMouseLeftButtonDown(e);
        }
    }

    [Fact]
    public void ASliderSoundsOnceAtAReleaseThatMovedIt() => ClickSeam.OnSta(sink =>
    {
        var slider = new Slider { Minimum = 0, Maximum = 10, Value = 2 };

        ClickSeam.Press(slider);
        slider.Value = 3; // a drag: every step a ValueChanged
        slider.Value = 4;
        slider.Value = 5;
        Assert.Empty(sink.Clicks); // nothing while dragging
        ClickSeam.Lift(slider);

        Assert.Equal(new[] { Overlay(ClickCue.Select) }, sink.Clicks);
    });

    [Fact]
    public void ASliderMovedWithoutAPressOrPressedWithoutAMoveIsSilent() => ClickSeam.OnSta(sink =>
    {
        var slider = new Slider { Minimum = 0, Maximum = 10, Value = 2 };
        var other = new Slider { Minimum = 0, Maximum = 10, Value = 2 };

        slider.Value = 7; // a key, or code: ValueChanged alone
        ClickSeam.Lift(slider); // a release with no press on it
        ClickSeam.Press(slider);
        ClickSeam.Lift(slider); // unmoved
        ClickSeam.Press(slider);
        slider.Value = 8;
        ClickSeam.Lift(other); // released over another slider

        Assert.Empty(sink.Clicks);
    });

    /// <summary>A slider's class handler runs before its own press handling, so a click on the track that moves it is a move.</summary>
    [Fact]
    public void APressThatTheSliderItselfMovesCounts() => ClickSeam.OnSta(sink =>
    {
        var slider = new JumpingSlider { Minimum = 0, Maximum = 10, Value = 2 };

        ClickSeam.Press(slider);
        Assert.Equal(10, slider.Value);
        ClickSeam.Lift(slider);

        Assert.Equal(new[] { Overlay(ClickCue.Select) }, sink.Clicks);
    });

    [Fact]
    public void ASlidersMarkAndScopeAreItsOwn() => ClickSeam.OnSta(sink =>
    {
        var press = new Slider { Minimum = 0, Maximum = 10 };
        UiClicks.SetCue(press, ClickMark.Press);
        var none = new Slider { Minimum = 0, Maximum = 10 };
        UiClicks.SetCue(none, ClickMark.None);
        var inGame = new Slider { Minimum = 0, Maximum = 10 };
        UiClicks.SetScope(new Border { Child = inGame }, ClickScope.Game);
        var inGames = new Slider { Minimum = 0, Maximum = 10 };
        var games = new Border { Child = inGames };
        UiClicks.SetScope(games, ClickScope.Games);

        foreach (var slider in new[] { press, none, inGame, inGames })
        {
            ClickSeam.Press(slider);
            slider.Value = 6;
            ClickSeam.Lift(slider);
        }

        Assert.Equal(new[] { Overlay(ClickCue.Press), (ClickCue.Select, ClickScope.Games) }, sink.Clicks);
    });

    // ---- Not buttons ----------------------------------------------------------------------------

    [Fact]
    public void ClickedPlaysNowInTheElementsScope() => ClickSeam.OnSta(sink =>
    {
        var well = new Border();
        var tile = new Border();
        UiClicks.SetScope(new StackPanel { Children = { tile } }, ClickScope.Games);
        var card = new Border();
        UiClicks.SetScope(new StackPanel { Children = { card } }, ClickScope.Game);

        UiClicks.Clicked(well, ClickCue.Press);
        UiClicks.Clicked(tile, ClickCue.Select);
        UiClicks.Clicked(card, ClickCue.Select); // a game's: the Engine's

        Assert.Equal(new[] { Overlay(ClickCue.Press), (ClickCue.Select, ClickScope.Games) }, sink.Clicks);
    });
}

/// <summary>The menu tick's style carries the Toggle mark (Controls.xaml): the same in every theme, never a token.</summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class UiClicksStyleTests
{
    [Fact]
    public void TheMenuTickIsAToggleInEveryTheme() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var sink = ClickSeam.Listen();
        try
        {
            foreach (var entry in Theme.All)
            {
                Theme.Apply(entry.Id);
                var tick = new ToggleButton { Style = (Style)Application.Current.FindResource("MenuTick") };
                Assert.Equal(ClickMark.Toggle, UiClicks.GetCue(tick));
                ClickSeam.Click(tick);
                ClickSeam.Click(tick);
            }
        }
        finally
        {
            Theme.Apply(Theme.DefaultId);
        }

        Assert.Equal(Theme.All.SelectMany(_ => new[] { (ClickCue.ToggleOn, ClickScope.Overlay), (ClickCue.ToggleOff, ClickScope.Overlay) }), sink.Clicks);
    });
}
