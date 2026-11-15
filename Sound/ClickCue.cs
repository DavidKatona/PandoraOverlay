namespace PandoraOverlay;

/// <summary>
/// The six click sounds (G27, A100; <c>PLAN-CLICKS.md</c> 3): one family for
/// the overlay and the games, since a click means "your press registered"
/// whatever window it is in. Each is a WAV made by code
/// (<c>tools/games-sound/clicks/</c>, take A of each the owner's, G29) under
/// <c>Assets/Clicks/</c>, embedded as <c>Clicks/&lt;file&gt;.wav</c> and read
/// by <see cref="ClickBank"/>. The names are the plan's (section 3.3), the
/// Contract's <c>GameClick</c> the same six.
/// </summary>
public enum ClickCue
{
    /// <summary><c>press.wav</c>, −18 dBFS: a button that acts; an armed button's confirming click.</summary>
    Press,

    /// <summary><c>select.wav</c>, −20 dBFS, the commonest: a nav, a tab, a radio, a menu opener, a slider's release, a card.</summary>
    Select,

    /// <summary><c>toggle-on.wav</c>, −18 dBFS, rising: a check ticked, a menu tick on.</summary>
    ToggleOn,

    /// <summary><c>toggle-off.wav</c>, −20 dBFS, falling: a check cleared, a menu tick off.</summary>
    ToggleOff,

    /// <summary><c>arm.wav</c>, −16 dBFS, the loudest: an armed button's FIRST click, which did not do the thing and asks (A100, A104).</summary>
    Arm,

    /// <summary><c>back.wav</c>, −21 dBFS, the softest: ✕, Close, Later, Cancel, Keep, "‹ Games", "Back to camp".</summary>
    Back,
}

/// <summary>
/// A control's own word on its click (<see cref="UiClicks.CueProperty"/>,
/// set per control, never inherited; A104). <see cref="Auto"/>, the default,
/// leaves the cue to the control's type (<c>PLAN-CLICKS.md</c> 1.3); the rest
/// are the few marks the appendix gives a control whose meaning its type
/// does not say.
/// </summary>
public enum ClickMark
{
    /// <summary>By type: a CheckBox on or off by its new state, a RadioButton and any other ToggleButton select, any other button press.</summary>
    Auto,

    /// <summary>Silent: a control with a sound of its own (the Games window's 🔊, `−` and `+`) or none at all (the control panel's Exit, A90).</summary>
    None,

    /// <summary>Always <see cref="ClickCue.Press"/> (Settings' click volume slider: the commonest click, heard at the new level).</summary>
    Press,

    /// <summary>Always <see cref="ClickCue.Select"/>.</summary>
    Select,

    /// <summary><see cref="ClickCue.ToggleOn"/> or <see cref="ClickCue.ToggleOff"/> by the control's new IsChecked (a menu's tick, by its style).</summary>
    Toggle,

    /// <summary>An armed button: <see cref="ClickCue.Arm"/> when its handler called <see cref="UiClicks.Armed"/>, else <see cref="ClickCue.Press"/> (its confirming click).</summary>
    Arm,

    /// <summary>Always <see cref="ClickCue.Back"/>: a dismissal.</summary>
    Back,
}

/// <summary>
/// Whose rules a click obeys (<see cref="UiClicks.ScopeProperty"/>, inherited
/// down the tree; A103, A104). <see cref="Overlay"/>, the default, is every
/// window of the overlay: Settings' switch and its step. <see cref="Games"/>
/// is the Games window (set on its root): the switch, and the window's own
/// mute and step. <see cref="Game"/> is a game's view (set on its container):
/// a game's controls are the Engine's rule (A105), which the app's handler
/// leaves at once, so a game's click is never decided twice.
/// </summary>
public enum ClickScope
{
    /// <summary>The overlay's windows: Settings' "Click sounds" and its step.</summary>
    Overlay,

    /// <summary>The Games window's own controls: the switch, then the window's mute and step (A103).</summary>
    Games,

    /// <summary>A game's view: the Engine's rule, never the app's handler (A105).</summary>
    Game,
}

/// <summary>
/// Where the click layer hands a click (A104): MainWindow's
/// <see cref="ClickSound"/> on the UI thread, a fake in the tests. Called on
/// the UI thread only; returns at once and never throws.
/// </summary>
internal interface IClickSink
{
    /// <summary>One click the player made, in its scope: the sink decides whether, and how loud, it sounds.</summary>
    void Click(ClickCue cue, ClickScope scope);
}
