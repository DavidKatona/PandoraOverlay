using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// What every game's view is built on: the loop, the playback, the guard and
/// the lifecycle, so that no game writes its own and none gets them wrong.
///
/// <para><b>The loop and the playback.</b> The constructor makes a
/// <see cref="Playback"/> and a <see cref="GameLoop"/> over the host's clock.
/// Each loop tick advances the playback once per engine tick, then calls the
/// subclass's <see cref="Step"/>, then its <see cref="Draw"/>; anything either
/// throws goes to the host's <c>Fault</c> (A10).</para>
///
/// <para><b>The subclass sets <see cref="Loop"/>'s rate</b>:
/// <see cref="LoopRate.Full"/> when it enqueues a playback,
/// <see cref="LoopRate.Off"/> (or <see cref="LoopRate.Ambient"/>, for a quiet
/// cue) on <see cref="Engine.Playback.Drained"/>. The base does not, because
/// only the game knows whether something on its board still breathes; and it
/// starts Off, because nothing moves until a game says so: nothing moves,
/// nothing runs (A19). A view waiting for the player's click costs nothing
/// while The Isle runs underneath.</para>
///
/// <para><b>The guard.</b> Every handler a view subscribes (a button's click,
/// a surface's pointer events, a playback's <c>Drained</c>) goes through
/// <see cref="Guard"/>: a disposed or paused view takes no input, and an
/// exception becomes the game's fault, never the overlay's (A10). A skip is a
/// handler like any other: <see cref="Engine.Playback.SkipAll"/> throws on an
/// endless chain (A35), and through the guard that is one fault.</para>
///
/// <para><b>Disposal loses nothing.</b> The game's state is final at every
/// moment (A20): what the playback shows moving is a cosmetic replay of a
/// state already committed and saved. So <see cref="Dispose"/> only stops
/// the loop and drops the playback; it never touches the game's state, and
/// the next view paints the final state. It never throws, and it may be
/// called from anywhere, from inside a step's own code included: a step that
/// reports a fault (through <see cref="Guard"/> or the host) makes the window
/// dispose the view while the playback's walk is still on the stack, where
/// <see cref="Engine.Playback.Clear"/> refuses to run (A06). The view is then
/// marked disposed first, its loop disposed, and the refused clear left to
/// the garbage collector with the rest of the object; from that moment the
/// view's step and draw return at once, so nothing of a disposed view is
/// stepped or painted.</para>
///
/// <para><b>Overruns.</b> A playback step still running at its limit is
/// finished for it (<see cref="Engine.Playback.StepOverran"/>) and logged
/// here once, as a Warning: a bug in a game's step, never a fault, since the
/// picture left is still right.</para>
///
/// <para><b>The click rule</b> (G27, A105, <c>PLAN-CLICKS.md</c> 1.6 and 6.2).
/// A game's own controls click with the host's click sounds, by one rule kept
/// here so that every game has it without writing a line: a left click on a
/// button of the view (or on a card the view names by <see cref="Clicked"/>)
/// sounds once, as its kind (<see cref="GameClicks.ByType"/>: the same table as
/// the overlay's windows), through <see cref="IGameSound.Click"/>, unless a turn
/// plays back, the click began one, or the view played a cue of its own for it.
/// The host is given the view's root only through <see cref="IGameView.Root"/>,
/// and that is where the rule is wired, the first time each root is asked for;
/// see the section "The click rule" below for each condition and why.</para>
/// </summary>
public abstract class GameViewBase : IGameView
{
    /// <summary>The log's line for an overrun: a fact, the same for every game.</summary>
    internal const string OverranWarning = "a playback step ran past its limit and was finished";

    private bool _disposed;

    /// <summary>The loop over the host's clock and the playback, both Off and empty to begin with.</summary>
    protected GameViewBase(IGameHost host)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Playback = new Playback();
        Playback.StepOverran += OnStepOverran;
        Loop = new GameLoop(host.Clock, OnLoopStep, OnLoopDraw, host.Fault);
    }

    /// <summary>The host the game was attached to: its clock, storage, log and fault.</summary>
    protected IGameHost Host { get; }

    /// <summary>The view's loop: the subclass sets its rate (Full to play, Off or Ambient once drained).</summary>
    protected GameLoop Loop { get; }

    /// <summary>The view's playback: advanced once per engine tick by the loop, before <see cref="Step"/>.</summary>
    protected Playback Playback { get; }

    /// <summary>
    /// The game's own element, laid out in <see cref="GameFrame.ViewWidth"/> x
    /// <see cref="GameFrame.ViewHeight"/> (<see cref="IGameView.Root"/>'s rule).
    /// The host does not read this: it reads <see cref="IGameView.Root"/>, which
    /// returns this same element with the click rule wired on it (A105).
    /// </summary>
    public abstract FrameworkElement Root { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> LayoutStates { get; }

    /// <inheritdoc />
    public abstract void ShowLayoutState(string state);

    /// <summary>One loop tick of <paramref name="ticks"/> engine ticks, after the playback has advanced as many times.</summary>
    protected abstract void Step(int ticks);

    /// <summary>Paint the surfaces (compose their buffers and present them).</summary>
    protected abstract void Draw();

    /// <summary>
    /// Runs <paramref name="action"/> as the view's own handler: nothing when
    /// the view is disposed or paused (a covered view takes no input),
    /// otherwise the action, with any exception handed to the host's
    /// <c>Fault</c> (A10), never thrown on.
    /// </summary>
    protected void Guard(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_disposed || Loop.Paused)
            return;
        try
        {
            action();
        }
        catch (Exception error)
        {
            Host.Fault(error);
        }
    }

    /// <summary>
    /// One of the game's sounds, handed to the host (A77): nothing once the
    /// view is disposed or while it is paused, otherwise the host's
    /// <see cref="IGameHost.Sound"/>, which plays it or drops it.
    /// <para>
    /// The first of A79's three layers that keep a game silent off screen:
    /// the engine's rule that a paused view does nothing (A19, A56) holds for
    /// its sounds as for its input, so a step or a handler that plays while
    /// the health warning covers the view, or after the window closed it,
    /// makes no sound here. The host's gate is the second layer (it plays only
    /// for the instance on screen, not paused, not muted), and a stop at every
    /// change of what the window shows the third; the host holds those
    /// whatever a game does, this one keeps the engine's own word. A paused
    /// loop advances no playback either, so no beat reaches its cue.
    /// </para>
    /// </summary>
    protected void Play(GameSound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (_disposed || Loop.Paused)
            return;
        _handedOver++;   // the click rule's "a cue of its own" (A105): counted as handed, played or not
        Host.Sound.Play(sound);
    }

    /// <summary>The window covered the view (G18): the loop paused, and <see cref="Guard"/> takes no input. Idempotent.</summary>
    public virtual void Pause() => Loop.Paused = true;

    /// <summary>The warning was acknowledged: the loop and the input carry on where they were. Idempotent.</summary>
    public virtual void Resume() => Loop.Paused = false;

    /// <summary>
    /// The loop gone and the playback dropped; the game's state untouched
    /// (A20). Idempotent, and it never throws (see the class's summary for
    /// the call from inside a step). A subclass that overrides it calls this
    /// too.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Loop.Dispose();
        try
        {
            Playback.Clear();
        }
        catch (InvalidOperationException)
        {
            // Called from inside a step's own code, where the playback refuses
            // to be cleared under its own walk (A06). The steps go with the
            // object: the loop is gone and this view is never stepped again.
        }
    }

    // ===== The click rule (G27, A105; PLAN-CLICKS.md 1.6, 6.2) =====================================
    //
    // A game's own controls click by this one rule, so every game has it for nothing
    // (A21) and none writes it wrong. A click is a LEFT RELEASE on the view: it opens at
    // the root's PreviewMouseLeftButtonUp (the release tunnels down through the root
    // before any element of the game sees it), takes note of what was clicked while it
    // is open (a button's Click, seen at the root as it bubbles up, after the game's own
    // handler on the button; a card the game names by Clicked; an arming by Arming),
    // and closes at the root's MouseLeftButtonUp, which WPF raises on the root after
    // every element below it had the release (a button marks it handled, so the three
    // handlers are added with handledEventsToo), along the route WPF built before any
    // handler ran: a handler that takes its own button's page out of the view is still
    // decided at the root. The decision is made there, after every handler of the game
    // ran, and is ONE Click or none:
    //
    // - nothing when the view is disposed or paused: the guard's rule, as Play's (A79's
    //   first layer; a covered view takes no input and makes no sound);
    // - nothing when the playback was running at the release, or is running after it.
    //   A click during a turn is a skip (A20: a playback can always be skipped), whose
    //   one sound is the game's own skip cue or none, and a tick on every impatient skip
    //   would make the commonest gesture of a long turn noise; a click that began a turn
    //   has the turn's beats for its sound;
    // - nothing when the view handed the host any sound during the release: that sound
    //   is the click's (its own cue says the press did something), and two at once would
    //   be the double the owner's "do not make these annoying" rules out (A99);
    // - nothing when nothing was noted (a release on the board, a text, the background:
    //   no button, no card) or the noted button is marked None;
    // - else Arm when the game called Arming (the click asked, it did not do the thing,
    //   A104), else the card's kind, else the button's mark or type (GameClicks.ByType).
    //
    // A key, automation or code reaches a button's Click with no release open around it
    // (and the games' controls take no keyboard focus anyway, A33), so none of them
    // clicks; Clicked and Arming outside a release do nothing. A RepeatButton (a
    // slider's track, a scrollbar's page parts) clicks on its press and repeats while
    // held: never noted. The host decides the rest (the window, the mute, the player's
    // switch for clicks; A103), and the game never learns whether it sounded.

    /// <summary>The roots the rule is wired on: each once, the same element every time it is asked for.</summary>
    private readonly List<FrameworkElement> _wiredRoots = new();

    /// <summary>How many sounds the view has handed the host (counted in <see cref="Play"/>): a release compares the count at its end with its start.</summary>
    private int _handedOver;

    /// <summary>A left release is open on the root: between its preview and its bubble.</summary>
    private bool _releasing;

    /// <summary>The playback was idle when the release opened.</summary>
    private bool _idleAtRelease;

    /// <summary><see cref="_handedOver"/> when the release opened.</summary>
    private int _handedAtRelease;

    /// <summary>The first button whose Click the release saw, or null.</summary>
    private ButtonBase? _releasedButton;

    /// <summary>The kind of the first card the game named during the release, or null.</summary>
    private GameClick? _releasedCard;

    /// <summary>The game called <see cref="Arming"/> during the release.</summary>
    private bool _releaseArmed;

    /// <summary>
    /// The root the host lays out (A3), with the click rule wired on it (A105): the
    /// subclass's <see cref="Root"/>, the same element every time, wired the first
    /// time it is handed over. A game writes nothing for it: the host reaches a view's
    /// root only through here. A root the subclass changes to later is wired too, the
    /// first time it is handed over; a root handed over again is never wired twice.
    /// <para>
    /// Here, and not in the constructor, because the subclass builds its root after
    /// the base constructor has run (a virtual call there would see none); and not on
    /// the root's <c>Loaded</c>, which would need the root first anyway. The rule's
    /// handlers are on the root, so a game's handlers on the elements below it always
    /// run first, and a game's own handlers on the root itself run first when the game
    /// added them before the host asked for the root (in its constructor, as games do).
    /// </para>
    /// </summary>
    FrameworkElement IGameView.Root
    {
        get
        {
            var root = Root;
            if (root is not null && !_wiredRoots.Exists(wired => ReferenceEquals(wired, root)))
            {
                root.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnReleaseOpens), handledEventsToo: true);
                root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClicked), handledEventsToo: true);
                root.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnReleaseCloses), handledEventsToo: true);
                _wiredRoots.Add(root);
            }
            // A game's null root (its bug) is handed on as it was before the rule: the
            // window's guard meets it there, and nothing here throws for it.
            return root!;
        }
    }

    /// <summary>How many roots the click rule is wired on: for the tests that hold "once each".</summary>
    internal int RootsWired => _wiredRoots.Count;

    /// <summary>
    /// The click being released armed (A104, A105): its handler's arming branch says
    /// so, and the click sounds <see cref="GameClick.Arm"/> instead of its button's
    /// kind, since it did NOT do the thing, it asks. Called from the branch that arms
    /// (the first click of a two-click action); outside a left release it does
    /// nothing, so a layout state or a test that arms in code makes no sound.
    /// </summary>
    protected void Arming()
    {
        if (_releasing)
            _releaseArmed = true;
    }

    /// <summary>
    /// A card or another clickable that is not a button was released on (A105): the
    /// view's mouse handler names its kind (a card picked is
    /// <see cref="GameClick.Select"/>), at the moment it acts on the release, so a cue
    /// its action plays claims the click as any button's does. The first card named in
    /// a release counts. Outside a left release it does nothing: it trusts its caller
    /// to be the mouse handler, and a call from code is silent.
    /// </summary>
    protected void Clicked(GameClick click)
    {
        if (_releasing && _releasedCard is null)
            _releasedCard = click;
    }

    /// <summary>A left release reaches the root, before anything below it sees it: a release opens, with the playback's state and the sounds handed over so far.</summary>
    private void OnReleaseOpens(object sender, MouseButtonEventArgs e)
    {
        // Opened whatever the view's state (field writes only): a stale release, whose
        // end never reached the root, is forgotten here, and the end decides by the
        // guard's rules.
        _releasing = true;
        _idleAtRelease = Playback.Idle;
        _handedAtRelease = _handedOver;
        _releasedButton = null;
        _releasedCard = null;
        _releaseArmed = false;
    }

    /// <summary>A button's Click bubbles through the root during a release: the first one's source is the click's button, unless it is a RepeatButton.</summary>
    private void OnButtonClicked(object sender, RoutedEventArgs e)
    {
        if (!_releasing || _releasedButton is not null)
            return;
        // The source, never the sender (the root): a button inside another clicks once, as the one under the mouse.
        if (e.OriginalSource is ButtonBase button and not RepeatButton)
            _releasedButton = button;
    }

    /// <summary>The release reaches the root last: it closes, and decides through the guard (nothing paused or disposed, nothing thrown into the game).</summary>
    private void OnReleaseCloses(object sender, MouseButtonEventArgs e)
    {
        if (!_releasing)
            return;
        var idleAtRelease = _idleAtRelease;
        var handed = _handedOver != _handedAtRelease;
        var button = _releasedButton;
        var card = _releasedCard;
        var armed = _releaseArmed;
        _releasing = false;
        _releasedButton = null;
        _releasedCard = null;
        _releaseArmed = false;

        Guard(() =>
        {
            if (ClickOf(idleAtRelease && Playback.Idle, handed, button, card, armed) is { } click)
                Host.Sound.Click(click);
        });
    }

    /// <summary>
    /// The click a closed release sounds, or null, by the section above: nothing when
    /// the playback was or is running (<paramref name="idle"/> false), when the view
    /// handed the host a sound during it, when nothing was noted, or when the noted
    /// button is marked None; else Arm, else the card's kind, else the button's mark or
    /// type. Pure: what the view's state was is passed in (the guard has already said
    /// the view is neither paused nor disposed).
    /// </summary>
    internal static GameClick? ClickOf(bool idle, bool handedASound, ButtonBase? button, GameClick? card, bool armed)
    {
        if (!idle || handedASound)
            return null;
        if (button is null && card is null && !armed)
            return null;
        if (button is not null && GameClicks.GetCue(button) == GameClickMark.None)
            return null;
        if (armed)
            return GameClick.Arm;
        if (card is { } named)
            return named;
        return GameClicks.ByType(button!);
    }

    private void OnLoopStep(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            if (_disposed)
                return;
            Playback.Advance();
        }
        if (_disposed)
            return;
        Step(ticks);
    }

    private void OnLoopDraw()
    {
        if (_disposed)
            return;
        Draw();
    }

    private void OnStepOverran() => Host.Log.Warning(OverranWarning);
}
