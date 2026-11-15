using System.Windows;
using System.Windows.Controls;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The page a run is played on (H27): the board, the turn bar, the side
/// panel with the turn list, and the playback that shows a turn one actor at a time.
/// This is where the owner's one requirement is met or missed (G9): "it wasn't clear
/// who was moving and when". So a turn is WATCHED: your dino acts, then each hunter in
/// order with its name in the banner and its row lit, a gap of nothing moving between
/// two of them, and none of it costs the player a click they did not mean (H5).
///
/// <para><b>Partial parts by topic</b> (the repo's style): this file is the page at
/// rest and its frame; <c>RunPage.Input.cs</c> what a click does;
/// <c>RunPage.Playback.cs</c> how a turn is played; <c>RunPage.Panel.cs</c> the side
/// panel, the turn list, the offer and the summary.</para>
///
/// <para><b>The page never changes the game's state</b> (A20, H70): every change is a
/// call on <see cref="HatchlingGame"/> (<c>Act</c>, <c>SetFast</c>, <c>BackToCamp</c>),
/// which commits and saves before it returns; the page plays back from that call's
/// result and then shows <c>game.State</c>, never a copy of its own.</para>
///
/// <para><b>Nothing runs at rest but the quiet cue</b> (A19): the loop is
/// <see cref="LoopRate.Full"/> while a turn plays, <see cref="LoopRate.Ambient"/> while
/// a run awaits your move with this page shown, and <see cref="LoopRate.Off"/>
/// everywhere else: on an offer, on a summary, on another page, in a layout state.</para>
/// </summary>
internal sealed partial class RunPage : Grid, IHatchlingPage
{
    private readonly HatchlingView _view;
    private readonly HatchlingGame _game;
    private readonly ArtBook _art;
    private readonly Playback _playback;
    private readonly GameLoop _loop;
    private readonly PixelSurface _board;

    /// <summary>What the board shows now: the scene at rest, or the one a playback is moving.</summary>
    private Scene _scene = new();

    /// <summary>The turn being played, from the first beat enqueued to <c>Playback.Drained</c>; null at rest.</summary>
    private TurnPlay? _play;

    /// <summary>The cell under the mouse, or -1.</summary>
    private int _hover = -1;

    /// <summary>This page is the one the view shows.</summary>
    private bool _shown;

    /// <summary>A sample state shown by a layout state (never the save); null for the game's own.</summary>
    private GameState? _sample;

    /// <summary>Which way each creature faces: the view's, never saved (H53), carried from a playback into the rest after it.</summary>
    private readonly Dictionary<int, bool> _facing = new();

    /// <summary>The ambient looks since the rest began: the quiet cue flips every second one (A19).</summary>
    private int _ambientLooks;

    /// <summary>The board needs painting at the next draw.</summary>
    private bool _dirty = true;

    public RunPage(HatchlingView view, HatchlingGame game, ArtBook art, Playback playback, GameLoop loop)
    {
        _view = view;
        _game = game;
        _art = art;
        _playback = playback;
        _loop = loop;
        InitializeComponent();
        _board = new PixelSurface(BoardLayout.Width, BoardLayout.Height);
        BoardHost.Children.Insert(0, _board);
        BuildRows();
        Wire();
    }

    /// <inheritdoc />
    public FrameworkElement Root => this;

    /// <summary>The state the page shows: a layout state's sample, else the game's own.</summary>
    internal GameState State => _sample ?? _game.State;

    /// <summary>A turn is being played back.</summary>
    internal bool Playing => _play is not null;

    /// <summary>The board's surface: the tests read its buffer and the pictures for the planner are saved from it.</summary>
    internal PixelSurface Board => _board;

    /// <summary>The scene the board shows now.</summary>
    internal Scene CurrentScene => _scene;

    /// <summary>What who says what is known asks: what the player may know (H3, H6), the one Known the game and the script are given.</summary>
    internal static readonly Known SightKnows = (run, creature) => Sight.Knows(run, Stats.Of(run), creature);

    // ===== IHatchlingPage ==================================================================

    /// <inheritdoc />
    public void Show()
    {
        _shown = true;
        if (_play is null) Rest();
        else _loop.Rate = LoopRate.Full;
    }

    /// <inheritdoc />
    public void Refresh()
    {
        // During a playback the page waits for it to drain: the rest after it reads the state.
        if (_play is null) Rest();
    }

    /// <inheritdoc />
    public void Hide()
    {
        _shown = false;
        // Leaving the page skips what plays (a playback never runs where nobody sees it).
        if (_play is not null && !_playback.Idle) _playback.SkipAll();
        _loop.Rate = LoopRate.Off;
        Disarm();
        _hover = -1;
    }

    // ===== The page at rest ================================================================

    /// <summary>
    /// The page as the state stands, with no playback: the scene from the state alone
    /// (<c>Scene.Of</c>, the one way in, H53) with the hover and each creature's
    /// facing; the banner ("Your turn", the offer's line, or the summary's title, G9);
    /// the turn list as last shown (H28) with the marker on You; the panel,
    /// the moves, the offer or the summary; the context line; the loop at the quiet cue
    /// while a run awaits your move here, else off.
    /// </summary>
    internal void Rest()
    {
        var state = State;
        if (state.Run is not { } run) return;
        var stats = Stats.Of(run);
        var awaiting = AwaitsMove(run);

        _scene = Scene.Of(run, stats, yourTurn: true, awaiting ? _hover : -1);
        Face(_scene);
        _scene.AmbientFrame = 0;
        _ambientLooks = 0;

        // The run's end first: a run left with an offer open (1.9) keeps that offer in
        // its state, and the summary, not the offer, is what shows over the board.
        Banner.Text = run.Over is { } over ? HatchlingText.OverTitle(over)
            : run.Offer is not null ? HatchlingText.ChooseInstinct
            : HatchlingText.YourTurn;
        ShowRows(run.LastTurn, texts: null, marker: run.Over is null ? 0 : -1, playing: false);
        TurnNumber.Text = HatchlingText.TurnNumber(run.Turn);
        ShowYourDino(run, PanelValues.Of(run));
        ShowMoves(run, stats, open: awaiting);
        ShowOffer(state);
        ShowSummary(state);
        Context.Text = ContextLine(state);
        SyncControls();

        _loop.Rate = _sample is null && _shown && awaiting ? LoopRate.Ambient : LoopRate.Off;
        Paint();
    }

    /// <summary>A run awaits your move: not over and no offer open. Only then is the board a click, and only then does the quiet cue run.</summary>
    private static bool AwaitsMove(RunState run) => run.Over is null && run.Offer is null;

    /// <summary>Each creature's facing as the last playback left it (facing is the view's, H53).</summary>
    private void Face(Scene scene)
    {
        foreach (var sprite in scene.Sprites)
            if (_facing.TryGetValue(sprite.Creature, out var flip)) sprite.Flip = flip;
    }

    // ===== The loop's tick and draw ==========================================================

    /// <summary>
    /// The view's step: at rest on the ambient beat, the quiet "your turn" cue, the
    /// scene's one ambient frame flipped every second look (H55, A19).
    /// During a playback the beats keep that frame themselves (every 8 ticks).
    /// </summary>
    internal void Step(int ticks)
    {
        if (_play is not null || _loop.Rate != LoopRate.Ambient) return;
        _ambientLooks++;
        if (_ambientLooks % 2 != 0) return;
        _scene.AmbientFrame ^= 1;
        _dirty = true;
    }

    /// <summary>The view's draw: what a playback changed, on screen; the board painted when it moved.</summary>
    internal void Draw()
    {
        if (_play is not null)
        {
            ShowPlay(_play);
            _dirty = true;
        }
        if (_dirty) Paint();
    }

    /// <summary>The board: the scene painted by the pure painter into the surface's buffer, then presented (A17).</summary>
    private void Paint()
    {
        BoardPainter.Paint(_board.Buffer, _scene, _art);
        _board.Present();
        _dirty = false;
    }

    // ===== A layout state (A26) ===================================================

    /// <summary>
    /// Shows a sample state as a still picture: no loop, no input, no storage (A11's
    /// rules for a layout state). A playback still queued is dropped first.
    /// </summary>
    internal void ShowSample(GameState sample)
    {
        DropPlayback();
        _sample = sample;
        _hover = -1;
        _facing.Clear();
        Rest();
        _loop.Rate = LoopRate.Off;
    }

    /// <summary>
    /// Shows a sample turn frozen at tick <paramref name="tick"/> of beat
    /// <paramref name="beat"/>: the beats before it finished (as a skip finishes them),
    /// that one begun and advanced. Nothing is enqueued, the loop stays off, so the
    /// window's test that no loop holds a timer after every state passes (A11).
    /// </summary>
    internal void ShowFrozen(GameState sample, TurnResult turn, int beat, int tick)
    {
        ShowSample(sample);
        var script = TurnScripts.Build(turn, SightKnows);
        var play = Begin(turn, script);
        var steps = BeatStep.Make(play);
        for (var i = 0; i < beat && i < steps.Count; i++) steps[i].Finish();
        if (beat < steps.Count)
        {
            steps[beat].Begin();
            for (var k = 0; k < tick; k++) steps[beat].Advance();
        }
        ShowPlay(play);
        Paint();
        _loop.Rate = LoopRate.Off;
    }

    /// <summary>The page leaves a layout state: the game's own state again.</summary>
    internal void ClearSample()
    {
        if (_sample is null) return;
        _sample = null;
        _play = null;
    }
}
