using System.Windows;
using System.Windows.Input;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

// The Run page: what a click does (H5). Every handler goes through
// the view's Guard (A10): a paused or disposed view takes no input, and an exception
// is the game's fault, never the overlay's. The events call the internal methods
// below and nothing else, so a test calls exactly what a click calls.
internal sealed partial class RunPage
{
    /// <summary>A test's switch: run before every call on the game, so a test can make the game throw (A10's path through the guard).</summary>
    internal Action? BeforeAct { get; set; }

    /// <summary>Leave is armed: its next click leaves the run ("armed", H20).</summary>
    internal bool LeaveArmed { get; private set; }

    /// <summary>The input the page takes now: no playback, no layout state, a run that awaits your move (H5: none while a turn plays, the offer's own cards and the summary's button aside).</summary>
    internal bool TakesInput => _sample is null && _play is null && State.Run is { } run && AwaitsMove(run);

    private void Wire()
    {
        _board.PointerPressed += p => _view.Guarded(() => BoardPressed(p));
        _board.PointerMoved += p => _view.Guarded(() => BoardMoved(p));
        _board.PointerLeft += () => _view.Guarded(BoardLeft);

        WalkPace.Click += (_, _) => Clicked(() => PaceClicked(Pace.Walk));
        SneakPace.Click += (_, _) => Clicked(() => PaceClicked(Pace.Sneak));
        SprintPace.Click += (_, _) => Clicked(() => PaceClicked(Pace.Sprint));
        EatButton.Click += (_, _) => Clicked(EatClicked);
        DrinkButton.Click += (_, _) => Clicked(DrinkClicked);
        RestButton.Click += (_, _) => Clicked(RestClicked);
        LeaveButton.Click += (_, _) => Clicked(LeaveClicked);
        LeaveButton.MouseLeave += (_, _) => _view.Guarded(LeaveLeft);
        FastBox.Click += (_, _) => Clicked(() => FastClicked(FastBox.IsChecked == true));
        RedrawButton.Click += (_, _) => Clicked(RedrawClicked);
        BackButton.Click += (_, _) => Clicked(BackClicked);

        // A click anywhere else disarms Leave (H20: an armed control never acts on a stray click).
        PreviewMouseLeftButtonDown += (_, e) => _view.Guarded(() =>
        {
            if (e.OriginalSource is not DependencyObject source || !IsInside(source, LeaveButton)) Disarm();
        });
    }

    /// <summary>A control's click: the handler through the guard, then the controls put back as the state has them (a radio or a box clicked while nothing was taken shows what is true).</summary>
    private void Clicked(Action handler)
    {
        _view.Guarded(handler);
        _view.Settled(SyncControls);
    }

    // ---- the board ---------------------------------------------------------------------------

    /// <summary>
    /// A press on the board. During a playback it finishes the playback and does
    /// NOTHING else: a move chosen on a board the player has not seen settle would be a
    /// move by accident (H5). Idle, the left button on a creature you can attack
    /// attacks it; on a lit cell moves there at the run's pace; anything else, and the
    /// right button, does nothing (H27).
    /// </summary>
    internal void BoardPressed(PixelPointer pointer)
    {
        Disarm();
        if (pointer.Button != PointerButton.Left) return;
        if (_play is not null)
        {
            Skip();
            return;
        }
        if (!TakesInput || State.Run is not { } run) return;
        var cell = BoardLayout.Hexes.CellAt(pointer.X, pointer.Y);
        if (cell < 0) return;
        var stats = Stats.Of(run);
        if (Island.CreatureAt(run, cell) is { } creature && Sight.Knows(run, stats, creature)
            && Moves.Attackable(run, stats).Any(c => c.Id == creature.Id))
        {
            Act(new PlayerAction.Attack(creature.Id));
            return;
        }
        if (Moves.Targets(run, stats).Contains(cell)) Act(new PlayerAction.Move(cell));
    }

    /// <summary>The pointer over the board: the hover cell and the context line; the board repainted only when the cell changed.</summary>
    internal void BoardMoved(PixelPointer pointer) => Hover(BoardLayout.Hexes.CellAt(pointer.X, pointer.Y));

    /// <summary>The pointer left the board: no hover.</summary>
    internal void BoardLeft() => Hover(-1);

    private void Hover(int cell)
    {
        if (cell == _hover) return;
        _hover = cell;
        if (_play is not null) return;   // the line says a click skips; the rest after it reads the hover
        if (State.Run is { } run && AwaitsMove(run))
        {
            // A new scene at rest whenever the hover changes (H53: never a cell set by hand).
            var frame = _scene.AmbientFrame;
            _scene = Scene.Of(run, Stats.Of(run), yourTurn: true, _hover);
            Face(_scene);
            _scene.AmbientFrame = frame;
            Paint();
        }
        Context.Text = ContextLine(State);
    }

    // ---- the panel's controls ----------------------------------------------------------------

    /// <summary>A pace (a choice that takes no turn, kept in the run): during a playback only a skip.</summary>
    internal void PaceClicked(Pace pace)
    {
        Disarm();
        if (_play is not null) { Skip(); return; }
        if (!TakesInput || State.Run is not { } run || run.Pace == pace) return;
        Act(new PlayerAction.SetPace(pace));
    }

    /// <summary>Eat: during a playback only a skip.</summary>
    internal void EatClicked() => Move(new PlayerAction.Eat());

    /// <summary>Drink: during a playback only a skip.</summary>
    internal void DrinkClicked() => Move(new PlayerAction.Drink());

    /// <summary>Rest: during a playback only a skip.</summary>
    internal void RestClicked() => Move(new PlayerAction.Rest());

    private void Move(PlayerAction action)
    {
        Disarm();
        if (_play is not null) { Skip(); return; }
        if (!TakesInput) return;
        Act(action);
    }

    /// <summary>
    /// Leave, armed (H20): the first click changes the label and the look and does
    /// nothing else; the second leaves the run, which pays what was reached and shows
    /// the summary. During a playback the click only skips.
    /// </summary>
    internal void LeaveClicked()
    {
        if (_play is not null) { Disarm(); Skip(); return; }
        if (!TakesInput) { Disarm(); return; }
        if (!LeaveArmed)
        {
            LeaveArmed = true;
            LeaveButton.Content = HatchlingText.LeaveArmed;
            LeaveButton.SetResourceReference(StyleProperty, "DangerButtonStyle");
            return;
        }
        Disarm();
        Act(new PlayerAction.Leave());
    }

    /// <summary>The mouse left Leave: it disarms.</summary>
    internal void LeaveLeft() => Disarm();

    /// <summary>Leave back to its first look: nothing moves between the two clicks, and nothing but the mouse leaving or another click undoes the first.</summary>
    internal void Disarm()
    {
        if (!LeaveArmed) return;
        LeaveArmed = false;
        LeaveButton.Content = HatchlingText.Leave;
        LeaveButton.SetResourceReference(StyleProperty, "NeutralButtonStyle");
    }

    /// <summary>Fast (H5): the playback's pace, kept in the save, at once, during a playback too.</summary>
    internal void FastClicked(bool fast)
    {
        Disarm();
        if (_sample is not null) return;
        if (State.Fast != fast) _game.SetFast(fast);
        _playback.Speed = fast ? 2 : 1;
    }

    // ---- the offer and the summary -------------------------------------------------------------

    /// <summary>A click on an offer's card: that instinct is picked (one click, H27).</summary>
    internal void PickClicked(int index)
    {
        Disarm();
        if (_sample is not null || _play is not null || State.Run is not { Over: null, Offer: { } offer }) return;
        if (index < 0 || index >= offer.Count) return;
        Act(new PlayerAction.Pick(index));
    }

    /// <summary>Redraw: a new offer, its price paid from the wallet by the game.</summary>
    internal void RedrawClicked()
    {
        Disarm();
        if (_sample is not null || _play is not null || State.Run is not { Over: null, Offer: not null }) return;
        Act(new PlayerAction.Redraw());
    }

    /// <summary>"Back to camp": the ended run forgotten (H20), and Camp shown.</summary>
    internal void BackClicked()
    {
        Disarm();
        if (_sample is not null || _play is not null || State.Run?.Over is null) return;
        BeforeAct?.Invoke();
        if (_game.BackToCamp()) _view.AfterBackToCamp();
    }

    // ---- the one way into the game -------------------------------------------------------------

    /// <summary>
    /// The game acts (A20): it commits and saves before it returns; null is a refusal
    /// with nothing written. A turn is then played back from the result; anything else
    /// rests the page on the state. The wallet is told either way (a redraw spends, a
    /// run's end banks its pay).
    /// </summary>
    private void Act(PlayerAction action)
    {
        BeforeAct?.Invoke();
        var result = _game.Act(action);
        _view.ShowWallet();
        if (result is null) Rest();
        else Play(result);
    }

    /// <summary>The pace radios and the Fast box as the state has them.</summary>
    private void SyncControls()
    {
        var state = State;
        FastBox.IsChecked = state.Fast;
        var pace = state.Run?.Pace ?? Pace.Walk;
        WalkPace.IsChecked = pace == Pace.Walk;
        SneakPace.IsChecked = pace == Pace.Sneak;
        SprintPace.IsChecked = pace == Pace.Sprint;
    }

    private static bool IsInside(DependencyObject node, DependencyObject container)
    {
        for (var d = node; d is not null;
             d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, container)) return true;
        return false;
    }
}
