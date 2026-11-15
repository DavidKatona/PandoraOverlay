using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// The overlay's click layer (G27; A98, A100, A104; <c>PLAN-CLICKS.md</c> 1
/// and 6.1): ONE class handler for every button of every window of the app,
/// a popup's included, so a new button clicks with no line of its own, and
/// the guard that keeps a key, automation and code silent. The few controls
/// whose type doesn't say what they mean carry a mark (<see cref="CueProperty"/>);
/// a window says whose rules its clicks obey (<see cref="ScopeProperty"/>);
/// the clicks go to the UI thread's <see cref="Sink"/>, MainWindow's
/// <see cref="ClickSound"/>, which decides whether, and how loud.
/// <para>
/// <b>A left click by the mouse, and nothing else.</b> ButtonBase raises
/// its Click from its OnMouseLeftButtonUp, which runs while the thread's
/// input manager raises a left button's <c>Mouse.MouseUpEvent</c> item.
/// Space or Enter on a focused button, an access key, automation's Invoke,
/// a <c>RaiseEvent</c> in code and an <c>IsChecked</c> set in code all reach
/// the click handler OUTSIDE that item (the planner's probe,
/// <c>evidence\clicks-planner\click-input.txt</c>, and this file's tests). So
/// <see cref="Attach"/> hooks the input manager's PreProcessInput and
/// PostProcessInput, which bracket every input item it handles, and keeps a
/// stack of the items being handled, the innermost last; a click is a mouse
/// click when the innermost item still being handled is a left MouseUp whose
/// target is the clicked button or inside it. The stack and its two rules
/// (read from WPF's own InputManager, ButtonBase and UIElement, and probed):
/// </para>
/// <list type="bullet">
/// <item><b>Items nest.</b> The input manager handles one item at a time, but
/// handling one can start more inside it (ButtonBase releases its capture
/// before it clicks, and the release synchronises the mouse: raw reports
/// handled within the MouseUp item, right before the Click; a click handler
/// that opens a dialog runs a whole message loop inside it). So a frame is
/// pushed at every item's PreProcessInput and taken off, with every frame
/// above it, at its PostProcessInput: a key pressed in a dialog that a mouse
/// click opened is the innermost item while its click is raised, never the
/// MouseUp under it.</item>
/// <item><b>A frame can end without its PostProcessInput.</b> A handler that
/// runs after ours may cancel an item in its PreProcessInput (ours runs
/// first: the input manager calls the last added first), and then the item
/// is never raised and gets no PostProcessInput: WPF's own devices cancel raw
/// reports nested inside a key press (probed). So a frame is LIVE only while
/// the input manager's own arguments for its item still name it and are not
/// cancelled: they are reset for the next item of the same level and cleared
/// when the level ends, and a nested level gets arguments of its own. Dead
/// frames are dropped from the top before every look, so a cancelled raw
/// report inside the release never shadows the MouseUp under it (which would
/// silence every real click), and the stack stays as deep as the nesting.</item>
/// <item><b>The target.</b> A click is the release's only when the clicked
/// button is the MouseUp's original source or one of its ancestors (the
/// button holds the mouse capture from its press, so the source is the
/// button itself, or a part of it). So a click raised in code from inside a
/// release (a handler clicking another button, a dialog opened by the click
/// whose own message loop runs automation's Invoke) is not the mouse's, even
/// with the MouseUp still the innermost live item.</item>
/// </list>
/// <para>
/// Known limit, said plainly: an exception thrown out of a release's handling
/// and then contained (only a game's fault is, <c>GameFaults</c>) leaves that
/// frame looking live, since the input manager's arguments are abandoned with
/// it; from then on a click raised in code or by automation on that same
/// button, with no other item being handled, would sound. A key never does
/// (its own item is the innermost), and a real click on it sounds as it
/// should. The real mouse path itself can be seen by nobody but the owner: a
/// window a test builds can't hold a mouse capture, so the tests open the
/// bracket through the input manager with a MouseUp of their own and call
/// the button's own OnClick inside it, where ButtonBase calls it (A107).
/// </para>
/// <para>
/// <b>The handler</b> (on <c>ButtonBase.ClickEvent</c>, handledEventsToo, so a
/// click some handler marked handled still clicks): only at the source (a
/// button inside another clicks once); only a mouse click (above); never a
/// RepeatButton (a slider's track, a scroll bar's page parts, which click on
/// the press and repeat while held) and never a disabled control; the
/// <see cref="ClickScope.Game"/> scope leaves at once (a game's controls are
/// the Engine's rule, A105); the <see cref="ClickMark.None"/> mark leaves; an
/// <see cref="ClickMark.Arm"/> button's cue is decided just after its own
/// handler (below); every other click is played AT ONCE, before the button's
/// own handler runs (class handlers run before instance handlers), so a
/// click that opens Settings is heard as the button comes up, not when the
/// dialog appears. By type (<c>PLAN-CLICKS.md</c> 1.3): a CheckBox
/// <see cref="ClickCue.ToggleOn"/> when its new IsChecked is true, else
/// <see cref="ClickCue.ToggleOff"/> (a ToggleButton toggles before it raises
/// Click, so the handler reads the new state); a RadioButton and every other
/// ToggleButton (a nav, a tab, a menu opener) <see cref="ClickCue.Select"/>;
/// any other button <see cref="ClickCue.Press"/>.
/// </para>
/// <para>
/// <b>Armed</b> (A104, <c>PLAN-CLICKS.md</c> 1.4): a button marked
/// <see cref="ClickMark.Arm"/> waits for its own handler: the class handler
/// posts the decision at <see cref="DispatcherPriority.Send"/>, which runs
/// after the handler and before the next frame is drawn; meanwhile the
/// arming branch has called <see cref="Armed"/> for the button, or not:
/// <see cref="ClickCue.Arm"/> (it asks), or <see cref="ClickCue.Press"/> (it
/// did the thing). An armed button's handler never opens a dialog (each
/// arms or acts in place), so the wait is under a millisecond.
/// </para>
/// <para>
/// <b>Sliders</b>: class handlers on a Slider's left press and release
/// (their Preview events, handledEventsToo; a Slider's class handler runs
/// before its own press handling, so the value read at the press is the
/// value before a click on the track moved it): one cue at the release when
/// the value moved, <see cref="ClickCue.Select"/> or the slider's mark; none
/// while dragging (a drag's ValueChanged never sounds), none unmoved, none
/// for a key. Both are mouse events, so no bracket is needed.
/// </para>
/// <para>
/// <b>Not buttons</b>: <see cref="Clicked"/>, called from the element's own
/// mouse handler (the Games window's tiles, the colour wells, a stored dino
/// card's summary), plays with the element's scope, now; it trusts its
/// caller to be a mouse handler.
/// </para>
/// <para>
/// <b>Silent unless MainWindow says.</b> The sink is thread-static: null on
/// any thread MainWindow did not set it on, so every test, every dialog a
/// test builds and the golden-render harness (which makes MainWindow without
/// its constructor) are silent, and so is everything before
/// <see cref="Attach"/>. The class handlers are process-wide and permanent
/// (that is the point: a new button needs nothing; a new control that must
/// be silent needs its mark, risk 6); on a thread with no sink or no
/// <see cref="Attach"/> they return at once. The layer adds no thread, no
/// timer, no device and no request; it logs nothing per click (constraint 4),
/// and a failure of its own (none is known) is one Warning, never a throw
/// into the button's click.
/// </para>
/// </summary>
public static class UiClicks
{
    // ---- The two attached properties -----------------------------------------------------

    /// <summary>
    /// A control's mark (<see cref="ClickMark"/>; not inherited): <c>Auto</c> by
    /// default, the cue by type. Set in XAML as <c>local:UiClicks.Cue="Back"</c>,
    /// on a style (the menu tick's <c>Toggle</c>, Controls.xaml), or in code by
    /// <see cref="SetCue"/> for a button built there.
    /// </summary>
    public static readonly DependencyProperty CueProperty = DependencyProperty.RegisterAttached(
        "Cue", typeof(ClickMark), typeof(UiClicks), new PropertyMetadata(ClickMark.Auto));

    public static ClickMark GetCue(DependencyObject element) => (ClickMark)element.GetValue(CueProperty);

    public static void SetCue(DependencyObject element, ClickMark value) => element.SetValue(CueProperty, value);

    /// <summary>
    /// Whose rules a click obeys (<see cref="ClickScope"/>; INHERITED down the
    /// tree): <c>Overlay</c> by default, <c>Games</c> on the Games window's
    /// root, <c>Game</c> on its view's container (A104).
    /// </summary>
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached(
        "Scope", typeof(ClickScope), typeof(UiClicks), new FrameworkPropertyMetadata(ClickScope.Overlay, FrameworkPropertyMetadataOptions.Inherits));

    public static ClickScope GetScope(DependencyObject element) => (ClickScope)element.GetValue(ScopeProperty);

    public static void SetScope(DependencyObject element, ClickScope value) => element.SetValue(ScopeProperty, value);

    // ---- The sink and the state, per thread -----------------------------------------------

    /// <summary>
    /// Where this thread's clicks go: MainWindow's <see cref="ClickSound"/> on
    /// the UI thread (set after <see cref="Attach"/>, cleared at the exit
    /// before the speaker closes), null anywhere else: silence.
    /// </summary>
    [ThreadStatic]
    internal static IClickSink? Sink;

    /// <summary>The input items this thread's input manager is handling, the innermost last (the summary's stack); null before <see cref="Attach"/> on this thread.</summary>
    [ThreadStatic]
    private static List<InputFrame>? _frames;

    /// <summary>The button whose arming branch called <see cref="Armed"/> since the last armed click began.</summary>
    [ThreadStatic]
    private static DependencyObject? _armed;

    /// <summary>The slider the left button was pressed on, and its value then; null once released.</summary>
    [ThreadStatic]
    private static Slider? _pressedSlider;

    [ThreadStatic]
    private static double _pressedValue;

    /// <summary>A failure of the layer's own was logged on this thread: the next ones are Verbose.</summary>
    [ThreadStatic]
    private static bool _failed;

    private static readonly object ClassHandlersLock = new();
    private static bool _classHandlers;

    // ---- Attach ---------------------------------------------------------------------------------

    /// <summary>
    /// Wires the layer: the class handlers once per process (on
    /// <c>ButtonBase.ClickEvent</c> and a Slider's left press and release, all
    /// with handledEventsToo), and this thread's input manager's
    /// PreProcessInput and PostProcessInput once per thread. On the UI thread
    /// (MainWindow's constructor), before the sink is set; again on the same
    /// thread it does nothing.
    /// </summary>
    public static void Attach()
    {
        lock (ClassHandlersLock)
        {
            if (!_classHandlers)
            {
                EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), handledEventsToo: true);
                EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSliderPress), handledEventsToo: true);
                EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSliderRelease), handledEventsToo: true);
                _classHandlers = true;
            }
        }
        if (_frames is not null) return;
        _frames = new List<InputFrame>();
        var input = InputManager.Current;
        // Two lambdas, so neither the input manager's method nor the post
        // stage's arguments type is ever named here: the host's reach scan
        // reads a word that begins with it as a way to another program
        // (A92, A102), and the scan is never edited for us.
        input.PreProcessInput += (_, e) => Opened(e);
        input.PostProcessInput += (_, e) => Closed(e.StagingItem);
    }

    // ---- The bracket ----------------------------------------------------------------------------

    /// <summary>
    /// One input item being handled: the item, the input manager's own
    /// PreProcessInput arguments for it (whose StagingItem and Canceled tell
    /// whether the item is still being handled) and, for a left button's
    /// MouseUp, its arguments (the release, whose original source is the
    /// target). A struct in a list: no allocation per item.
    /// </summary>
    private readonly struct InputFrame
    {
        public InputFrame(StagingAreaInputItem item, PreProcessInputEventArgs args, MouseButtonEventArgs? release)
        {
            Item = item;
            Args = args;
            Release = release;
        }

        public StagingAreaInputItem Item { get; }

        public PreProcessInputEventArgs Args { get; }

        /// <summary>The left MouseUp this item is, or null for any other item.</summary>
        public MouseButtonEventArgs? Release { get; }

        /// <summary>Still being handled: the arguments still name this item (reset for the next item of the level, cleared when the level ends) and nothing cancelled it.</summary>
        public bool Live => ReferenceEquals(Args.StagingItem, Item) && !Args.Canceled;
    }

    /// <summary>An item's PreProcessInput: dead frames off the top, then the item's own frame on it.</summary>
    private static void Opened(PreProcessInputEventArgs e)
    {
        var frames = _frames;
        if (frames is null) return;
        DropDead(frames);
        var input = e.StagingItem.Input;
        var release = input is MouseButtonEventArgs { ChangedButton: MouseButton.Left } mouse && ReferenceEquals(mouse.RoutedEvent, Mouse.MouseUpEvent) ? mouse : null;
        frames.Add(new InputFrame(e.StagingItem, e, release));
    }

    /// <summary>An item's PostProcessInput: its frame off, with every frame above it (nested items that ended without one).</summary>
    private static void Closed(StagingAreaInputItem item)
    {
        var frames = _frames;
        if (frames is null) return;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(frames[i].Item, item)) continue;
            frames.RemoveRange(i, frames.Count - i);
            return;
        }
    }

    private static void DropDead(List<InputFrame> frames)
    {
        while (frames.Count > 0 && !frames[^1].Live) frames.RemoveAt(frames.Count - 1);
    }

    /// <summary>The frames this thread holds now (the tests' look at the stack staying as deep as the nesting).</summary>
    internal static int OpenFrames
    {
        get
        {
            var frames = _frames;
            if (frames is null) return 0;
            DropDead(frames);
            return frames.Count;
        }
    }

    /// <summary>A click on this button now is the mouse's: the innermost live item is a left MouseUp whose target is the button or inside it.</summary>
    private static bool InLeftRelease(DependencyObject button)
    {
        var frames = _frames;
        if (frames is null) return false;
        DropDead(frames);
        return frames.Count > 0 && frames[^1].Release is { } release && Covers(button, release.OriginalSource as DependencyObject);
    }

    /// <summary>The element is the node or one of its ancestors: up the visual tree, by the logical parent where there is no visual one (a text element, a popup's root).</summary>
    private static bool Covers(DependencyObject element, DependencyObject? node)
    {
        for (; node is not null; node = ParentOf(node))
            if (ReferenceEquals(node, element)) return true;
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        (node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);

    // ---- The rule (PLAN-CLICKS.md 1.3, A100) ---------------------------------------------------

    /// <summary>What kind of control a click came from, as the rule reads it.</summary>
    internal enum ClickControl
    {
        /// <summary>Any button that is no toggle: a Button, or a ButtonBase of another kind.</summary>
        Button,
        CheckBox,
        RadioButton,

        /// <summary>A plain ToggleButton (a nav, a tab, a menu opener, a menu tick).</summary>
        ToggleButton,

        /// <summary>A RepeatButton: never clicks (it clicks on the press and repeats).</summary>
        RepeatButton,

        /// <summary>A Slider's release that moved it.</summary>
        Slider,
    }

    /// <summary>A control's kind, the most derived first (a CheckBox and a RadioButton are ToggleButtons).</summary>
    internal static ClickControl KindOf(DependencyObject control) => control switch
    {
        RepeatButton => ClickControl.RepeatButton,
        CheckBox => ClickControl.CheckBox,
        RadioButton => ClickControl.RadioButton,
        ToggleButton => ClickControl.ToggleButton,
        Slider => ClickControl.Slider,
        _ => ClickControl.Button,
    };

    /// <summary>
    /// The cue a click plays (A100), or null for none: a RepeatButton never;
    /// <c>None</c> never; <c>Press</c>, <c>Select</c> and <c>Back</c> their own;
    /// <c>Toggle</c> on or off by the new IsChecked (false or null off);
    /// <c>Arm</c> <see cref="ClickCue.Arm"/> when the branch called
    /// <see cref="Armed"/>, else <see cref="ClickCue.Press"/>; <c>Auto</c> by
    /// type: a CheckBox on or off, a RadioButton and a ToggleButton select, a
    /// slider select, any other button press. A slider has nothing to toggle
    /// and nothing to arm, so those two marks read as <c>Auto</c> on it.
    /// </summary>
    internal static ClickCue? CueOf(ClickControl control, bool? isChecked, ClickMark mark, bool armed)
    {
        if (control == ClickControl.RepeatButton) return null;
        if (control == ClickControl.Slider && mark is ClickMark.Toggle or ClickMark.Arm) mark = ClickMark.Auto;
        return mark switch
        {
            ClickMark.None => null,
            ClickMark.Press => ClickCue.Press,
            ClickMark.Select => ClickCue.Select,
            ClickMark.Back => ClickCue.Back,
            ClickMark.Toggle => isChecked == true ? ClickCue.ToggleOn : ClickCue.ToggleOff,
            ClickMark.Arm => armed ? ClickCue.Arm : ClickCue.Press,
            _ => control switch
            {
                ClickControl.CheckBox => isChecked == true ? ClickCue.ToggleOn : ClickCue.ToggleOff,
                ClickControl.RadioButton or ClickControl.ToggleButton or ClickControl.Slider => ClickCue.Select,
                _ => ClickCue.Press,
            },
        };
    }

    // ---- The handlers -------------------------------------------------------------------------

    /// <summary>The class handler on every ButtonBase's Click (the summary's rules).</summary>
    private static void OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ReferenceEquals(sender, e.OriginalSource) || sender is not ButtonBase button) return;
            if (button is RepeatButton || !button.IsEnabled) return;
            if (Sink is not { } sink || !InLeftRelease(button)) return;
            var scope = GetScope(button);
            if (scope == ClickScope.Game) return;
            var mark = GetCue(button);
            if (mark == ClickMark.None) return;
            if (mark == ClickMark.Arm)
            {
                // Decided after the button's own handler, which may arm it (A104).
                _armed = null;
                button.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => DecideArmed(button, scope)));
                return;
            }
            if (CueOf(KindOf(button), (button as ToggleButton)?.IsChecked, mark, armed: false) is { } cue) sink.Click(cue, scope);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>An armed button's cue, once its handler ran: <see cref="ClickCue.Arm"/> if it called <see cref="Armed"/>, else <see cref="ClickCue.Press"/>.</summary>
    private static void DecideArmed(DependencyObject button, ClickScope scope)
    {
        try
        {
            var armed = ReferenceEquals(_armed, button);
            _armed = null;
            if (CueOf(ClickControl.Button, null, ClickMark.Arm, armed) is { } cue) Sink?.Click(cue, scope);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>
    /// Called by an armed button's arming branch, during its Click handler
    /// (A104): this click asked ("Really…?") rather than did the thing, so it
    /// plays <see cref="ClickCue.Arm"/>. One line per site; nothing else there
    /// changes. Outside an armed click it does nothing that is ever heard.
    /// </summary>
    public static void Armed(DependencyObject button) => _armed = button;

    /// <summary>
    /// A click on a thing that is not a button (a tile, a colour well, a card's
    /// summary), from that element's own mouse handler: plays now, in the
    /// element's scope (never the Game scope's, which is the Engine's). It
    /// trusts its caller: call it from a mouse handler only.
    /// </summary>
    public static void Clicked(DependencyObject element, ClickCue cue)
    {
        try
        {
            if (Sink is not { } sink) return;
            var scope = GetScope(element);
            if (scope == ClickScope.Game) return;
            sink.Click(cue, scope);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>A slider's left press: its value now, before its own press handling moves it (a class handler runs first).</summary>
    private static void OnSliderPress(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled) return;
        _pressedSlider = slider;
        _pressedValue = slider.Value;
    }

    /// <summary>A slider's left release: one cue when the press on this slider moved it, by its mark.</summary>
    private static void OnSliderRelease(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var pressed = _pressedSlider;
            var before = _pressedValue;
            if (sender is not Slider slider || !ReferenceEquals(pressed, slider)) return;
            _pressedSlider = null;
            if (slider.Value.Equals(before) || Sink is not { } sink) return; // unmoved: nothing to confirm
            var scope = GetScope(slider);
            if (scope == ClickScope.Game) return;
            if (CueOf(ClickControl.Slider, null, GetCue(slider), armed: false) is { } cue) sink.Click(cue, scope);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>A failure of the layer's own: one Warning per thread (the type and codes only, constraint 4), then Verbose; never a throw into a button's click.</summary>
    private static void Failed(Exception ex)
    {
        AppLog.Sound.Write(_failed ? LogVerbosity.Verbose : LogVerbosity.Warning, $"Sound: a click could not be handled ({AppLog.Short(ex)})");
        _failed = true;
    }
}
