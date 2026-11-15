using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What the side panel shows of your dino while a turn plays (G9: the panel follows
/// the beats, not the end of the turn): health as a fraction of your maximum, food and
/// water 0 to 100, growth a percent.
/// </summary>
internal readonly record struct PanelValues(double Health, double Food, double Water, double Growth)
{
    /// <summary>The values of a run as it stands.</summary>
    public static PanelValues Of(RunState run) => new(run.You.Health, run.You.Food, run.You.Water, run.You.Growth);
}

/// <summary>
/// One turn being played back: the scene the beats move, and what the turn bar, the
/// turn list and the panel read at this moment of it. It is DATA the beats change
/// (A20: a step changes only what is drawn), and the Run page reflects it on screen
/// each frame, so the picture a beat leaves can be compared, field by field, with the
/// one a skip leaves (A20's "skip equals run", A20).
/// </summary>
internal sealed class TurnPlay
{
    public TurnPlay(TurnResult result, TurnScript script, Scene scene, ArtBook art)
    {
        Result = result;
        Script = script;
        Scene = scene;
        Art = art;
        Panel = PanelValues.Of(result.Before);
        RowTexts = new string?[script.Rows.Count];
        Species = result.Before.Species;
    }

    /// <summary>The turn: the state before, the state after (already saved, A20), the events.</summary>
    public TurnResult Result { get; }

    /// <summary>The beats and the rows, from the events by the one function (H2).</summary>
    public TurnScript Script { get; }

    /// <summary>The board as it stands at this moment of the playback.</summary>
    public Scene Scene { get; }

    /// <summary>The game's art: a beat asks it whether an action is drawn, for the stand-ins (H30).</summary>
    public ArtBook Art { get; }

    /// <summary>The species played: your sprite's size is named from it (H21).</summary>
    public string Species { get; }

    /// <summary>What the banner reads now (G9: whose turn it is, and what is happening).</summary>
    public string Banner { get; set; } = "";

    /// <summary>The row of the turn list that has the marker: the one acting; -1 for none.</summary>
    public int Marker { get; set; } = -1;

    /// <summary>Each row's words as written so far: null for a row still to act (H28: nothing in its third column).</summary>
    public string?[] RowTexts { get; }

    /// <summary>What the side panel shows now.</summary>
    public PanelValues Panel { get; set; }

    /// <summary>
    /// The playback's own clock: one per beat tick played. The scene's ambient frame
    /// follows it, every 8 ticks, so the acting ring pulses (H55). Counted by the beats
    /// themselves, so it never reads a clock (the step's rule, A19).
    /// </summary>
    public int Ticks { get; set; }

    /// <summary>The scene's ambient frame flips every this many playback ticks (H55).</summary>
    public const int RingPulseTicks = 8;
}

/// <summary>
/// The sprites a playback needs that the scene at the turn's start does not hold: a
/// creature that walks into what you know, one that arrives, the apex. Made by the
/// same rule as <c>Scene.Of</c>'s own (its ring by what it is, H22; its marks by its
/// alert and afflictions, H52): a test holds this rule equal to the scene's for every
/// creature of many states, so the two cannot drift apart.
/// </summary>
internal static class PlaybackSprites
{
    /// <summary>A sprite for <paramref name="creature"/> with its feet on <paramref name="cell"/>, standing, facing right, fully there.</summary>
    public static SceneSprite Of(Creature creature, int cell)
    {
        ArgumentNullException.ThrowIfNull(creature);
        var sprite = creature.Kind == CreatureKind.SmallGame
            ? new SceneSprite { Creature = creature.Id, Kind = SpriteKind.SmallGame, Subject = ArtNames.SmallGame, Ring = Ring.None }
            : new SceneSprite
            {
                Creature = creature.Id,
                Kind = SpriteKind.Predator,
                Subject = ArtNames.PredatorOf(creature.Type),
                Ring = RingOf(creature),
                Marks = MarksOf(creature),
                Ordinal = creature.Ordinal,
            };
        Place(sprite, cell);
        return sprite;
    }

    /// <summary>A creature's ring at rest: the apex's heavier red, any other predator's red, none for small game (H22).</summary>
    public static Ring RingOf(Creature creature) =>
        creature.Kind == CreatureKind.SmallGame ? Ring.None
        : creature.Type == Tuning.Apex ? Ring.Apex
        : Ring.Hostile;

    /// <summary>A predator's marks: hunting at alert 4 or for the apex, searching at 1 to 3; its afflictions (H52). None for small game.</summary>
    public static Marks MarksOf(Creature creature)
    {
        if (creature.Kind == CreatureKind.SmallGame) return Marks.None;
        var marks = Marks.None;
        if (creature.Alert >= Tuning.AlertSeen || creature.Type == Tuning.Apex) marks |= Marks.Hunt;
        else if (creature.Alert > 0) marks |= Marks.Search;
        if (creature.Dazed) marks |= Marks.Dazed;
        if (creature.Slowed > 0) marks |= Marks.Slow;
        if (creature.Bleed > 0) marks |= Marks.Bleed;
        return marks;
    }

    /// <summary>Feet on the cell's feet point, and the row it stands in (the drawing order).</summary>
    public static void Place(SceneSprite sprite, int cell)
    {
        (sprite.X, sprite.Y) = BoardLayout.Feet(cell);
        sprite.Row = Island.Grid.Row(cell);
    }
}

/// <summary>
/// One beat of a turn, as one step of the playback's queue (A35: each beat enqueued by
/// itself, never inside a Sequence, so the engine's per-step limit is per beat and a
/// turn lasts exactly its script's <see cref="TurnScript.TotalTicks"/>). It is where
/// the owner's one requirement is met or missed (G9): your dino acts alone, then each
/// hunter in its turn with its ring lit and its act named, a gap of nothing moving
/// between two of them.
///
/// <para><b>What a beat does</b> (G9, beat by beat). <see cref="Begin"/>
/// sets the banner and puts the marker on the beat's row; each <see cref="Advance"/>
/// moves the scene one tick; at its last tick, or at <see cref="Finish"/> whenever it
/// comes (before <see cref="Begin"/> too: a skip finishes beats never begun), it puts
/// everything it touches at its end, and when it is the last beat of its row
/// (<see cref="TurnScript.IsLastOfRow"/>) it writes that row: a row says what its actor
/// did once it has done it all, never at its Focus.</para>
///
/// <para><b>Why every picture is worked out ahead.</b> The beats are made in one pass
/// over the script (<see cref="Make"/>), which follows the island as the beats move it:
/// where each sprite stands when its beat begins, what the panel reads, which marks a
/// creature wears. So a beat's every frame is a function of its own tick and of
/// nothing a beat before it may or may not have finished: running to the end, a skip
/// at tick 0 and a skip at any later tick leave the same picture (IStep's rule, A20),
/// and the test holds it for 200 turns.</para>
///
/// <para><b>Never the game's state</b> (A20). A beat changes the <see cref="TurnPlay"/>
/// and its scene, nothing else; it never enqueues. The state was saved before the
/// first beat; the next rest builds the scene from the state again.</para>
///
/// <para><b>Missing art (H30)</b>, asked of the book per action: a strike with no
/// <c>strike</c> frames is the standing frame moved 4 pixels toward its target and
/// back; a hurt with no <c>hurt</c> frame is the sprite drawn at opacity 120 twice, 2
/// ticks each; a walk with no <c>walk</c> frames slides the standing one (the book
/// answers it). The numbers are drawn by the painter only when their digits exist;
/// the banner carries them anyway.</para>
/// </summary>
internal sealed class BeatStep : IStep
{
    // The art's action names for acting (task H11's walk, H12's strike, hurt and eat).
    // The painter's own names (ArtNames) stop at "stand": these are asked only here.
    internal const string Walk = "walk", Strike = "strike", Hurt = "hurt", Eat = "eat";

    /// <summary>A strike's frames, toward its target, before the hurt (the beat table: 3).</summary>
    internal const int StrikeTicks = 3;

    /// <summary>How far a strike with no art leans toward its target, tick by tick: 4 pixels and back (H30).</summary>
    private static readonly int[] StrikeLean = { 2, 4, 2 };

    /// <summary>A hurt with no art: opacity 120 on these ticks of the hurt, twice for 2 ticks each (H30).</summary>
    internal const byte HurtBlink = 120;

    /// <summary>A dodge: your dino hops this far aside and back, tick by tick, after the strike's frames.</summary>
    private static readonly int[] DodgeHop = { 1, 2, 2, 1, 0 };

    /// <summary>Second wind: your ring flashes white on these ticks, three times (the beat's 12).</summary>
    private static bool SecondWindFlash(int tick) => tick % 4 < 2 && tick < 12;

    /// <summary>A stage: your dino blinks out on these ticks, three times, and is its new size from the middle.</summary>
    private static bool StageBlink(int tick) => tick is 2 or 3 or 8 or 9 or 14 or 15;

    /// <summary>The tick of a stage beat from which your dino is drawn at its new size.</summary>
    internal const int StageSwitchTick = 10;

    /// <summary>A rising number starts this far above the feet of the one it concerns, to its top row (H54)...</summary>
    internal const int NumberStartAbove = 22;

    /// <summary>...and rises this much more over its time.</summary>
    internal const int NumberRise = 10;

    private readonly TurnPlay _play;
    private readonly Beat _beat;
    private readonly int _index;
    private readonly bool _lastOfRow;
    private int _advances;
    private bool _begun;
    private bool _done;

    // ---- what the one pass worked out: the beat's sprites, their places, the values ----

    private SceneSprite? _actor;          // who moves or strikes
    private SceneSprite? _other;          // who is struck, bitten or hurt (null when unknown)
    private int _actorX;                  // the actor's feet when the beat begins (x: a strike leans from it)
    private int _otherX;                  // the one struck or bitten (x: a dodge hops from it)
    private int[] _cells = Array.Empty<int>();   // a walk's cells, the first its start
    private bool _flipFrom, _flipEnd;     // the actor's facing when the beat begins, and at its end
    private (SceneSprite Sprite, int[] Cells, bool FlipFrom, bool FlipEnd, byte OpacityFrom)[] _group = Array.Empty<(SceneSprite, int[], bool, bool, byte)>();
    private bool _setActorMarks;              // a creature's action: its marks at the end are _marksTo
    private PanelValues _panelFrom, _panelTo;
    private Ring _ringDuring, _ringEnd;
    private Marks _marksFrom, _marksTo;
    private byte _opacityFrom = 255, _opacityTo = 255;
    private string _subjectFrom = "", _subjectTo = "";
    private Marks _youMarksEnd;
    private bool _setYouMarks;
    private SceneNumber? _number;
    private SceneSprite? _numberOn;
    private int _numberFrom, _numberLength;   // the ticks of the beat the number shows on
    private bool _fadesOut;                   // the one struck or hurt is defeated
    private bool _fogEnd;                     // a walk: the fog and the food as seen from where you stand
    private bool[] _seenEnd = Array.Empty<bool>(), _inSightEnd = Array.Empty<bool>(), _plantsEnd = Array.Empty<bool>(), _foodShownEnd = Array.Empty<bool>();
    private int[] _meatEnd = Array.Empty<int>();
    private (int Cell, bool Plant, int Meat)[] _foodChanges = Array.Empty<(int, bool, int)>();
    private SceneSprite[] _reveals = Array.Empty<SceneSprite>();   // the quiet ones this beat's end brings into view

    private BeatStep(TurnPlay play, int index)
    {
        _play = play;
        _index = index;
        _beat = play.Script.Beats[index];
        _lastOfRow = play.Script.IsLastOfRow(index);
    }

    /// <summary>The beat this step plays.</summary>
    public Beat Beat => _beat;

    /// <summary>Its index in the script.</summary>
    public int Index => _index;

    /// <summary>
    /// One step for each beat of the play's script, in order, with every picture worked
    /// out by one pass over the island as the beats move it (see the summary). The
    /// play's scene is the scene at the turn's start (<c>Scene.Of</c> of the state
    /// before, not your turn); the sprites a beat needs that it lacks (a creature that
    /// walks into what you know, an arrival, the apex) are added to it here, at
    /// opacity 0, where they will first be seen, so nothing is added or removed while
    /// the turn plays: what fades in, fades in; what is defeated fades to 0 (H52).
    /// </summary>
    public static IReadOnlyList<BeatStep> Make(TurnPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);
        return new Planner(play).Make();
    }

    // ===== IStep ======================================================================

    /// <inheritdoc />
    public void Begin()
    {
        if (_begun || _done) return;
        _begun = true;
        Announce();
        Show(0);
    }

    /// <inheritdoc />
    public bool Advance()
    {
        if (_done) return true;
        if (!_begun) Begin();
        _play.Ticks++;
        _play.Scene.AmbientFrame = _play.Ticks / TurnPlay.RingPulseTicks % 2;
        _advances++;
        if (_advances >= _beat.Ticks)
        {
            End();
            return true;
        }
        Show(_advances);
        return false;
    }

    /// <inheritdoc />
    public void Finish()
    {
        if (_done) return;
        End();
    }

    // ===== The beat's picture ===========================================================

    /// <summary>The banner and the marker (G9): set when the beat begins, and again at its end, so a skipped beat leaves them as a played one does.</summary>
    private void Announce()
    {
        _play.Banner = _beat.Banner;
        if (_beat.Kind == BeatKind.Gap) return;   // a Gap belongs to no row: the marker stays where it was
        _play.Marker = _beat.RowIndex;
    }

    /// <summary>Everything the beat touches at its end; the row written when the beat is its row's last.</summary>
    private void End()
    {
        _done = true;
        Announce();
        Show(_beat.Ticks);
        if (_number is not null) _play.Scene.Numbers.Remove(_number);
        foreach (var sprite in _reveals) sprite.Opacity = 255;
        if (_lastOfRow && _beat.RowIndex >= 0 && _beat.RowIndex < _play.RowTexts.Length)
            _play.RowTexts[_beat.RowIndex] = HatchlingText.RowText(_play.Script.Rows[_beat.RowIndex]);
    }

    /// <summary>
    /// The scene at tick <paramref name="k"/> of the beat: 0 when it begins,
    /// <c>Ticks</c> at its end. A pure function of the tick and of what the one pass
    /// worked out, so any order of calls ends in the same picture.
    /// </summary>
    private void Show(int k)
    {
        var end = k >= _beat.Ticks;
        var t = _beat.Ticks;
        switch (_beat.Kind)
        {
            case BeatKind.YouWalk:
            case BeatKind.Steps:
                if (_actor is not null)
                {
                    WalkAlong(_actor, _cells, _flipFrom, _flipEnd, k, t);
                    if (_beat.Kind == BeatKind.Steps) _actor.Ring = end ? _ringEnd : _ringDuring;
                }
                if (end && _fogEnd) ShowFog();
                break;

            case BeatKind.YouEat:
            case BeatKind.YouDrink:
                if (_actor is not null)
                {
                    _actor.Action = end ? ArtNames.Stand : Eat;
                    _actor.Tick = end ? 0 : k;
                }
                Slide(k, t);
                Rise(k);
                if (end) ShowFoodChanges();
                break;

            case BeatKind.YouRest:
                Slide(k, t);
                Rise(k);
                break;

            case BeatKind.YouStrike:
            case BeatKind.AlsoStruck:
                ShowStrike(k, end, strikes: _beat.Kind == BeatKind.YouStrike);
                break;

            case BeatKind.Focus:
                if (_actor is not null)
                {
                    _actor.Ring = end ? _ringEnd : _ringDuring;
                    _actor.Opacity = Fade(_opacityFrom, _opacityTo, k, t);
                }
                break;

            case BeatKind.Notices:
            case BeatKind.Stays:
                if (_actor is not null) _actor.Ring = end ? _ringEnd : _ringDuring;
                break;

            case BeatKind.Bites:
            case BeatKind.Dodged:
                ShowBite(k, end, dodged: _beat.Kind == BeatKind.Dodged);
                break;

            case BeatKind.IsHurt:
                if (_actor is not null)
                {
                    _actor.Ring = end ? _ringEnd : _ringDuring;
                    HurtAt(_actor, k, t, end, _fadesOut);
                }
                Rise(k);
                if (end) ShowFoodChanges();
                break;

            case BeatKind.SecondWind:
                if (_other is not null) _other.Ring = !end && SecondWindFlash(k) ? Ring.Acting : Ring.You;
                if (_actor is not null) _actor.Ring = end ? _ringEnd : _ringDuring;
                Slide(k, 1);   // health is set at once: the blow left you at a quarter
                break;

            case BeatKind.SmallGame:
                foreach (var (sprite, cells, from, to, opacity) in _group)
                {
                    WalkAlong(sprite, cells, from, to, k, t);
                    sprite.Opacity = Fade(opacity, 255, k, t);
                }
                break;

            case BeatKind.Arrives:
            case BeatKind.Apex:
                if (_actor is not null)
                {
                    _actor.Opacity = Fade(_opacityFrom, _opacityTo, k, t);
                    _actor.Ring = end ? _ringEnd : _ringDuring;
                }
                break;

            case BeatKind.TimePasses:
                Slide(k, t);
                break;

            case BeatKind.TimeHurts:
                if (_other is not null) HurtAt(_other, k, t, end, fadesOut: false);
                Slide(k, t);
                Rise(k);
                break;

            case BeatKind.Stage:
                if (_other is not null)
                {
                    _other.Subject = k >= StageSwitchTick ? _subjectTo : _subjectFrom;
                    _other.Opacity = !end && StageBlink(k) ? (byte)0 : (byte)255;
                }
                break;

            // Gap, Unseen, End: nothing moves (G9: a gap is the pause between two actors).
        }

        // A creature's marks: a notice changes them as the banner says it, from its
        // first tick; anything else (a daze spent, a slow worn off, the marks the turn
        // left it with at its last beat) at the end.
        if (_setActorMarks && _actor is not null)
            _actor.Marks = _beat.Kind == BeatKind.Notices || end ? _marksTo : _marksFrom;

        if (end && _setYouMarks && _play.Scene.Sprites.Find(s => s.Kind == SpriteKind.You) is { } you)
            you.Marks = _youMarksEnd;
    }

    /// <summary>Your hit (or Thagomizer's sweep): your strike frames toward it, then its hurt frame and the number; a defeat fades it out and its meat appears.</summary>
    private void ShowStrike(int k, bool end, bool strikes)
    {
        var hurtFrom = strikes ? StrikeTicks : 0;
        if (_actor is not null && strikes) StrikeToward(_actor, _actorX, _flipEnd, k, end);
        if (_other is not null)
        {
            var hurtLength = strikes ? (_fadesOut ? BeatTicks.Strike - StrikeTicks : _beat.Ticks - StrikeTicks) : _beat.Ticks;
            if (end)
            {
                HurtAt(_other, hurtLength, hurtLength, end: true, _fadesOut);
            }
            else if (k >= hurtFrom)
            {
                var hk = k - hurtFrom;
                // A swept one that falls fades in its hurt's last third; your main hit's
                // target fades after its hurt, over the table's 6 more.
                if (hk < hurtLength) HurtAt(_other, hk, hurtLength, end: false, fadesOut: !strikes && _fadesOut);
                else
                {
                    // A hit that defeats: it fades over the rest of the beat (the table's 6 more).
                    _other.Action = ArtNames.Stand;
                    _other.Tick = 0;
                    _other.Opacity = Fade(255, 0, hk - hurtLength, _beat.Ticks - hurtFrom - hurtLength);
                }
            }
            else
            {
                _other.Action = ArtNames.Stand;
                _other.Tick = 0;
                _other.Opacity = 255;
            }
            // Its affliction's mark comes with the hit that caused it (G9).
            _other.Marks = k >= hurtFrom || end ? _marksTo : _marksFrom;
        }
        Rise(k);
        if (end) ShowFoodChanges();
    }

    /// <summary>A predator's bite on you, or the miss you dodged: its strike frames, then your hurt and the number, or your hop aside.</summary>
    private void ShowBite(int k, bool end, bool dodged)
    {
        if (_actor is not null)
        {
            StrikeToward(_actor, _actorX, _flipEnd, k, end);
            _actor.Ring = end ? _ringEnd : _ringDuring;
        }
        if (_other is not null)
        {
            var after = k - StrikeTicks;
            if (dodged)
            {
                var hop = !end && after >= 0 && after < DodgeHop.Length ? DodgeHop[after] : 0;
                _other.X = _otherX + (_actorX <= _otherX ? hop : -hop);
            }
            else
            {
                var length = _beat.Ticks - StrikeTicks;
                if (end) HurtAt(_other, length, length, end: true, fadesOut: false);
                else if (after >= 0) HurtAt(_other, after, length, end: false, fadesOut: false);
            }
        }
        if (!dodged)
        {
            var span = Math.Max(1, _beat.Ticks - StrikeTicks);
            Slide(Math.Max(0, k - StrikeTicks), span, end);
            Rise(k);
        }
    }

    /// <summary>
    /// A walk along <paramref name="cells"/> at tick <paramref name="k"/> of
    /// <paramref name="t"/>: cell by cell, the feet in a straight line from one cell's
    /// feet to the next's, the <c>walk</c> frames asked with the tick of the WHOLE walk
    /// (H60: never from 0 again at a cell), facing the way it steps; at the end it
    /// stands on the last cell. Its row is the one it walks into, and the scene is
    /// sorted when that changes.
    /// </summary>
    private void WalkAlong(SceneSprite sprite, int[] cells, bool flipFrom, bool flipEnd, int k, int t)
    {
        var segments = cells.Length - 1;
        if (segments <= 0) return;
        int row;
        if (k >= t)
        {
            PlaybackSprites.Place(sprite, cells[^1]);
            row = sprite.Row;
            sprite.Action = ArtNames.Stand;
            sprite.Tick = 0;
            sprite.Flip = flipEnd;
        }
        else
        {
            var perCell = Math.Max(1, t / segments);
            var segment = Math.Min(k / perCell, segments - 1);
            var within = Math.Min(k - segment * perCell, perCell);
            var (ax, ay) = BoardLayout.Feet(cells[segment]);
            var (bx, by) = BoardLayout.Feet(cells[segment + 1]);
            sprite.X = ax + (bx - ax) * within / perCell;
            sprite.Y = ay + (by - ay) * within / perCell;
            row = Island.Grid.Row(cells[segment + 1]);
            sprite.Action = Walk;
            sprite.Tick = k;
            sprite.Flip = FacingAlong(cells, segment, flipFrom);
        }
        if (sprite.Row != row)
        {
            sprite.Row = row;
            _play.Scene.Sort();
        }
        else if (k >= t)
        {
            _play.Scene.Sort();
        }
    }

    /// <summary>The facing on segment <paramref name="segment"/> of a walk: toward smaller x is flipped; a step straight up or down keeps the facing before it.</summary>
    internal static bool FacingAlong(int[] cells, int segment, bool flipFrom)
    {
        var flip = flipFrom;
        for (var s = 0; s <= segment && s + 1 < cells.Length; s++)
        {
            var ax = BoardLayout.Feet(cells[s]).X;
            var bx = BoardLayout.Feet(cells[s + 1]).X;
            if (bx < ax) flip = true;
            else if (bx > ax) flip = false;
        }
        return flip;
    }

    /// <summary>A strike's 3 ticks: the art's <c>strike</c> frames, or the standing frame leaning 4 pixels toward the target and back (H30); then it stands, facing its target.</summary>
    private void StrikeToward(SceneSprite sprite, int baseX, bool flip, int k, bool end)
    {
        sprite.Flip = flip;
        if (end || k >= StrikeTicks)
        {
            sprite.Action = ArtNames.Stand;
            sprite.Tick = 0;
            sprite.X = baseX;
            return;
        }
        if (Drawn(sprite.Subject, Strike))
        {
            sprite.Action = Strike;
            sprite.Tick = k;
            sprite.X = baseX;
        }
        else
        {
            sprite.Action = ArtNames.Stand;
            sprite.Tick = 0;
            sprite.X = baseX + (flip ? -StrikeLean[k] : StrikeLean[k]);
        }
    }

    /// <summary>A hurt over <paramref name="length"/> ticks: the art's <c>hurt</c> frame, or two blinks at opacity 120 (H30); a defeat fades out over its last third.</summary>
    private void HurtAt(SceneSprite sprite, int k, int length, bool end, bool fadesOut)
    {
        if (end)
        {
            sprite.Action = ArtNames.Stand;
            sprite.Tick = 0;
            sprite.Opacity = fadesOut ? (byte)0 : (byte)255;
            return;
        }
        var drawn = Drawn(sprite.Subject, Hurt);
        sprite.Action = drawn ? Hurt : ArtNames.Stand;
        sprite.Tick = drawn ? k : 0;
        byte opacity = 255;
        if (!drawn && (k is 0 or 1 || k == 4 || k == 5)) opacity = HurtBlink;
        if (fadesOut)
        {
            var fadeFrom = length - length / 3;
            if (k >= fadeFrom) opacity = (byte)Math.Min(opacity, Fade(255, 0, k - fadeFrom, length - fadeFrom));
        }
        sprite.Opacity = opacity;
    }

    /// <summary>The rising number (H54): from 22 above the feet of the one it concerns to 32, fading in its last third; gone at the beat's end.</summary>
    private void Rise(int k)
    {
        if (_number is null || _numberOn is null) return;
        var scene = _play.Scene;
        var at = k - _numberFrom;
        if (k >= _beat.Ticks || at < 0 || at >= _numberLength)
        {
            scene.Numbers.Remove(_number);
            return;
        }
        if (!scene.Numbers.Contains(_number)) scene.Numbers.Add(_number);
        _number.X = _numberOn.X;
        _number.Y = _numberOn.Y - NumberStartAbove - NumberRise * at / _numberLength;
        var fadeFrom = _numberLength - _numberLength / 3;
        _number.Opacity = at < fadeFrom ? (byte)255 : Fade(255, 0, at - fadeFrom + 1, _numberLength - fadeFrom + 1);
    }

    /// <summary>The panel from its start to its end value over the beat: the bars move with the beat that changed them (G9).</summary>
    private void Slide(int k, int t, bool end = false)
    {
        if (end || k >= t || t <= 0)
        {
            _play.Panel = _panelTo;
            return;
        }
        double At(double a, double b) => a + (b - a) * k / t;
        _play.Panel = new PanelValues(At(_panelFrom.Health, _panelTo.Health), At(_panelFrom.Food, _panelTo.Food),
            At(_panelFrom.Water, _panelTo.Water), At(_panelFrom.Growth, _panelTo.Growth));
    }

    /// <summary>A walk's end: the fog and the food as they are seen from where you now stand.</summary>
    private void ShowFog()
    {
        var scene = _play.Scene;
        Array.Copy(_seenEnd, scene.Seen, _seenEnd.Length);
        Array.Copy(_inSightEnd, scene.InSight, _inSightEnd.Length);
        Array.Copy(_plantsEnd, scene.Plants, _plantsEnd.Length);
        Array.Copy(_meatEnd, scene.Meat, _meatEnd.Length);
        Array.Copy(_foodShownEnd, scene.FoodShown, _foodShownEnd.Length);
    }

    /// <summary>A meal's end (the plant gone, the carcass a meal less) or a defeat's (its meat on its cell).</summary>
    private void ShowFoodChanges()
    {
        var scene = _play.Scene;
        foreach (var (cell, plant, meat) in _foodChanges)
        {
            scene.Plants[cell] = plant;
            scene.Meat[cell] = meat;
        }
    }

    /// <summary>An opacity from <paramref name="from"/> to <paramref name="to"/> over <paramref name="t"/> ticks, at tick <paramref name="k"/>.</summary>
    private static byte Fade(byte from, byte to, int k, int t)
    {
        if (t <= 0 || k >= t) return to;
        if (k <= 0) return from;
        return (byte)(from + (to - from) * k / t);
    }

    /// <summary>The art draws <paramref name="action"/> for <paramref name="subject"/>: otherwise the stand-in (H30).</summary>
    private bool Drawn(string subject, string action) => _play.Art.Variants(subject, action) > 0;

    // ===== The one pass ===================================================================

    /// <summary>
    /// Walks the script once, following the island as the beats move it, and gives each
    /// beat its start and its end: where its sprites stand and face, its marks, its
    /// ring, its opacity, the panel's values. Every value a beat needs at any of its
    /// ticks is decided here, so the beats themselves only interpolate.
    /// </summary>
    private sealed class Planner
    {
        private readonly TurnPlay _play;
        private readonly RunState _before;
        private readonly RunState _after;
        private readonly Stats _statsBefore;
        private readonly Stats _statsAfter;
        private readonly Dictionary<int, SceneSprite> _sprites = new();
        private readonly Dictionary<int, int> _cell = new();
        private readonly Dictionary<int, bool> _flip = new();
        private readonly Dictionary<int, Marks> _marks = new();
        private readonly Dictionary<int, byte> _opacity = new();
        private readonly Dictionary<int, int> _lastBeat = new();
        private readonly SceneSprite _you;
        private PanelValues _panel;
        private string _subject;

        public Planner(TurnPlay play)
        {
            _play = play;
            _before = play.Result.Before;
            _after = play.Result.After;
            _statsBefore = Stats.Of(_before);
            _statsAfter = Stats.Of(_after);
            _panel = play.Panel;

            foreach (var sprite in play.Scene.Sprites)
            {
                _sprites[sprite.Creature] = sprite;
                _flip[sprite.Creature] = sprite.Flip;
                _marks[sprite.Creature] = sprite.Marks;
                _opacity[sprite.Creature] = sprite.Opacity;
            }
            _you = play.Scene.Sprites.Single(s => s.Kind == SpriteKind.You);
            _subject = _you.Subject;
            _cell[0] = _before.You.At;
            foreach (var c in _before.Creatures) _cell[c.Id] = c.At;
            AddNewcomers();
            AddQuietOnes();
            play.Scene.Sort();

            // Each predator's last beat of the turn: where its ring goes back to its own
            // and its marks become the turn's. A Second wind inside its action (its
            // bite called for it) is a beat of that action too.
            var beats = play.Script.Beats;
            for (var i = 0; i < beats.Count; i++)
            {
                if (beats[i].Actor == BeatActor.Creature && beats[i].Creature > 0) _lastBeat[beats[i].Creature] = i;
                else if (beats[i].Kind == BeatKind.SecondWind && beats[i].RowIndex >= 0 && ActingCreature(i) is > 0 and var id) _lastBeat[id] = i;
            }
        }

        public IReadOnlyList<BeatStep> Make()
        {
            var beats = _play.Script.Beats;
            var steps = new BeatStep[beats.Count];
            for (var i = 0; i < beats.Count; i++)
            {
                var step = new BeatStep(_play, i) { _panelFrom = _panel, _panelTo = _panel };
                Plan(step, beats[i], i);
                _panel = step._panelTo;
                steps[i] = step;
            }

            // The quiet ones come into view with no beat of their own: what your walk
            // brought into sight at the walk's end, the rest (small game that arrived,
            // H40) when time passes, or at the turn's last beat when it has none.
            var walk = steps.FirstOrDefault(s => s._beat.Kind == BeatKind.YouWalk);
            var world = steps.FirstOrDefault(s => s._beat.Kind == BeatKind.TimePasses) ?? steps.LastOrDefault();
            foreach (var (sprite, byYourWalk) in _quiet)
            {
                var at = byYourWalk && walk is not null ? walk : world;
                if (at is null) continue;
                at._reveals = at._reveals.Append(sprite).ToArray();
            }
            return steps;
        }

        private readonly List<(SceneSprite Sprite, bool ByYourWalk)> _quiet = new();

        /// <summary>
        /// The creatures the rest after the turn will draw that no beat names: small game
        /// that stood still while your walk brought it into sight, small game that
        /// arrived where you see it (H40: no beat), a predator you walked up to that never
        /// got to act because the run ended. Each is added where it stands at the turn's
        /// end, at opacity 0, and shown at the end of the beat that brought it into view,
        /// so the playback's last picture is the rest's (A20: skip equals run, and both
        /// equal the state).
        /// </summary>
        private void AddQuietOnes()
        {
            var mid = _before.Clone();
            if (_play.Script.Beats.SelectMany(b => b.Events).OfType<Walked>().FirstOrDefault() is { Path.Count: > 0 } walked)
                mid.You.At = walked.Path[^1];
            foreach (var creature in _after.Creatures)
            {
                if (_sprites.ContainsKey(creature.Id) || !Sight.Knows(_after, _statsAfter, creature)) continue;
                var sprite = PlaybackSprites.Of(creature, creature.At);
                sprite.Opacity = 0;
                _play.Scene.Sprites.Add(sprite);
                _sprites[creature.Id] = sprite;
                _cell[creature.Id] = creature.At;
                _flip[creature.Id] = false;
                _marks[creature.Id] = sprite.Marks;
                _opacity[creature.Id] = 0;
                var before = _before.Creatures.Find(c => c.Id == creature.Id);
                _quiet.Add((sprite, before is not null && before.At == creature.At && Sight.Knows(mid, _statsBefore, before)));
            }
        }

        /// <summary>
        /// The sprites a beat names that the scene at the turn's start lacks, at opacity 0
        /// where they are first seen: a predator at the cell its action begins on (it has
        /// not moved before its own action), an arrival on its arrival's cell, small game
        /// where it stands. A creature the turn does not hold is left out: no beat of it
        /// can be drawn, and its banner still says what it did.
        /// </summary>
        private void AddNewcomers()
        {
            foreach (var beat in _play.Script.Beats)
            {
                foreach (var (id, at) in Named(beat))
                {
                    if (_sprites.ContainsKey(id)) continue;
                    var creature = _before.Creatures.Find(c => c.Id == id) ?? _after.Creatures.Find(c => c.Id == id);
                    if (creature is null) continue;
                    var cell = at ?? creature.At;
                    var sprite = PlaybackSprites.Of(creature, cell);
                    sprite.Opacity = 0;
                    _play.Scene.Sprites.Add(sprite);
                    _sprites[id] = sprite;
                    _cell[id] = cell;
                    _flip[id] = false;
                    _marks[id] = sprite.Marks;
                    _opacity[id] = 0;
                }
            }
        }

        /// <summary>The creatures a beat draws, with the cell it is first seen on when that is not its cell before the turn.</summary>
        private static IEnumerable<(int Id, int? At)> Named(Beat beat)
        {
            switch (beat.Kind)
            {
                case BeatKind.SmallGame:
                    foreach (var e in beat.Events)
                        if (e is Stepped s) yield return (s.Creature, s.From);
                    break;
                case BeatKind.Arrives:
                    foreach (var e in beat.Events)
                        if (e is Arrived a) yield return (a.Creature, a.At);
                    break;
                case BeatKind.Apex:
                    foreach (var e in beat.Events)
                        if (e is ApexArrived x) yield return (x.Creature, x.At);
                    break;
                default:
                    if (beat.Creature > 0 && beat.Kind != BeatKind.End) yield return (beat.Creature, null);
                    break;
            }
        }

        private void Plan(BeatStep step, Beat beat, int index)
        {
            switch (beat.Kind)
            {
                case BeatKind.YouWalk:
                    PlanYourWalk(step, beat);
                    break;
                case BeatKind.YouEat:
                case BeatKind.YouDrink:
                case BeatKind.YouRest:
                    PlanYourMeal(step, beat);
                    break;
                case BeatKind.YouStrike:
                case BeatKind.AlsoStruck:
                    PlanYourHit(step, beat);
                    break;
                case BeatKind.Focus:
                case BeatKind.Notices:
                case BeatKind.Stays:
                case BeatKind.Steps:
                case BeatKind.Bites:
                case BeatKind.Dodged:
                case BeatKind.IsHurt:
                    PlanItsAction(step, beat, index);
                    break;
                case BeatKind.SecondWind:
                    step._other = _you;
                    step._panelTo = _panel with { Health = Tuning.SecondWindHealth };
                    if (beat.RowIndex >= 0 && ActingCreature(index) is > 0 and var biter)
                    {
                        PlanActingRing(step, biter, index);
                        PlanLastMarks(step, biter, index);
                    }
                    break;
                case BeatKind.SmallGame:
                    PlanSmallGame(step, beat);
                    break;
                case BeatKind.Arrives:
                case BeatKind.Apex:
                    PlanArrival(step, beat, index);
                    break;
                case BeatKind.TimePasses:
                    PlanTime(step, beat, index);
                    break;
                case BeatKind.TimeHurts:
                    PlanHunger(step, beat, index);
                    break;
                case BeatKind.Stage:
                    PlanStage(step, beat);
                    break;
            }
        }

        private void PlanYourWalk(BeatStep step, Beat beat)
        {
            if (beat.Events.OfType<Walked>().FirstOrDefault() is not { } walked) return;
            step._actor = _you;
            step._cells = new[] { walked.From }.Concat(walked.Path).ToArray();
            step._flipFrom = _flip[0];
            step._flipEnd = FacingAlong(step._cells, step._cells.Length - 2, step._flipFrom);
            if (step._cells.Length > 1)
            {
                _cell[0] = step._cells[^1];
                _flip[0] = step._flipEnd;
            }
            PlanYourEnd(step);

            // The fog and the food as seen from where you stand once you have walked:
            // the cells seen as the turn saved them, the food as it was before the
            // world's turn (what you eat, and what is defeated, changes it later).
            var cells = _after.Tiles.Length;
            var mid = _before.Clone();
            mid.You.At = _cell[0];
            mid.Seen = (bool[])_after.Seen.Clone();
            step._fogEnd = true;
            step._seenEnd = (bool[])_after.Seen.Clone();
            step._inSightEnd = new bool[cells];
            step._plantsEnd = new bool[cells];
            step._meatEnd = new int[cells];
            step._foodShownEnd = new bool[cells];
            for (var cell = 0; cell < cells; cell++)
            {
                step._inSightEnd[cell] = Sight.InSight(mid, _statsBefore, cell);
                step._plantsEnd[cell] = Sight.ShowsFood(mid, _statsBefore, cell, Food.Plants);
                step._meatEnd[cell] = Sight.ShowsFood(mid, _statsBefore, cell, Food.Meat) ? mid.Meat[cell] : 0;
                step._foodShownEnd[cell] = !mid.Seen[cell] && (step._plantsEnd[cell] || step._meatEnd[cell] > 0);
            }
        }

        private void PlanYourMeal(BeatStep step, Beat beat)
        {
            step._actor = _you;
            var e = beat.Events.FirstOrDefault();
            var (gain, to) = e switch
            {
                Ate a => (a.Gain, _panel with { Food = Math.Min(Tuning.FoodMost, _panel.Food + a.Gain) }),
                Drank d => (d.Gain, _panel with { Water = Math.Min(Tuning.WaterMost, _panel.Water + d.Gain) }),
                Rested r => (r.Healed, _panel with { Health = Math.Min(1, _panel.Health + r.Healed / 100.0) }),
                _ => (0, _panel),
            };
            step._panelTo = to;
            Number(step, _you, gain, good: true, from: 0, length: beat.Ticks);
            if (e is Ate)
            {
                var at = _cell[0];
                step._foodChanges = new[]
                {
                    (at, Sight.ShowsFood(_after, _statsAfter, at, Food.Plants),
                        Sight.ShowsFood(_after, _statsAfter, at, Food.Meat) ? _after.Meat[at] : 0),
                };
            }
            PlanYourEnd(step);
        }

        private void PlanYourHit(BeatStep step, Beat beat)
        {
            if (beat.Events.OfType<Struck>().FirstOrDefault() is not { } struck) return;
            var strikes = beat.Kind == BeatKind.YouStrike;
            step._actor = _you;
            step._actorX = BoardLayout.Feet(_cell[0]).X;
            if (_sprites.TryGetValue(struck.Creature, out var target))
            {
                step._other = target;
                var targetX = BoardLayout.Feet(_cell.GetValueOrDefault(struck.Creature, struck.At)).X;
                // You face what you strike; facing straight up or down keeps the last.
                step._flipEnd = targetX < step._actorX || (targetX == step._actorX && _flip[0]);
                if (strikes) _flip[0] = step._flipEnd;
                step._marksFrom = _marks[struck.Creature];
                var marks = step._marksFrom;
                foreach (var f in beat.Events.OfType<Afflicted>())
                    marks |= f.Kind switch { Affliction.Dazed => Marks.Dazed, Affliction.Slowed => Marks.Slow, _ => Marks.Bleed };
                step._marksTo = marks;
                _marks[struck.Creature] = marks;
                step._fadesOut = struck.Defeated;
                if (struck.Defeated) _opacity[struck.Creature] = 0;
                Number(step, target, struck.Damage, good: false,
                    from: strikes ? StrikeTicks : 0, length: strikes ? BeatTicks.Strike - StrikeTicks : beat.Ticks);
            }
            else
            {
                step._flipEnd = _flip[0];
            }
            if (struck.Defeated)
                step._foodChanges = new[]
                {
                    (struck.At, Sight.ShowsFood(_after, _statsAfter, struck.At, Food.Plants),
                        Sight.ShowsFood(_after, _statsAfter, struck.At, Food.Meat) ? _after.Meat[struck.At] : 0),
                };
            PlanYourEnd(step);
        }

        /// <summary>Your marks once your move is done: hidden as the turn left you (cover, or Freeze), Elder as you stand.</summary>
        private void PlanYourEnd(BeatStep step)
        {
            var marks = _you.Marks & ~(Marks.Hidden | Marks.You);
            if (Sight.Hidden(_after, _statsAfter)) marks |= Marks.Hidden;
            step._setYouMarks = true;
            step._youMarksEnd = marks;
        }

        private void PlanItsAction(BeatStep step, Beat beat, int index)
        {
            var id = beat.Creature;
            if (!_sprites.TryGetValue(id, out var sprite)) return;
            step._actor = sprite;
            PlanActingRing(step, id, index);
            step._marksFrom = _marks[id];
            step._marksTo = _marks[id];
            var cell = _cell.GetValueOrDefault(id);
            step._actorX = BoardLayout.Feet(cell).X;
            step._flipFrom = _flip.GetValueOrDefault(id);
            step._flipEnd = step._flipFrom;

            switch (beat.Kind)
            {
                case BeatKind.Focus:
                    step._opacityFrom = _opacity.GetValueOrDefault(id);
                    step._opacityTo = 255;
                    _opacity[id] = 255;
                    break;

                case BeatKind.Notices when beat.Events.OfType<Noticed>().FirstOrDefault() is { } noticed:
                    var marks = _marks[id] & ~(Marks.Hunt | Marks.Search);
                    var apex = sprite.Ring == Ring.Apex || PlaybackSprites.RingOf(CreatureOf(id)) == Ring.Apex;
                    marks |= noticed.Kind switch
                    {
                        Notice.Spotted => Marks.Hunt,
                        Notice.Heard => Marks.Search,
                        _ => apex ? Marks.Hunt : Marks.None,
                    };
                    step._marksTo = marks;
                    _marks[id] = marks;
                    break;

                case BeatKind.Stays when beat.Events.OfType<Stayed>().FirstOrDefault() is { Why: Idle.Dazed }:
                    step._marksTo = _marks[id] & ~Marks.Dazed;
                    _marks[id] = step._marksTo;
                    break;

                case BeatKind.Steps when beat.Events.OfType<Stepped>().FirstOrDefault() is { } stepped:
                    step._cells = new[] { stepped.From }.Concat(stepped.Path).ToArray();
                    step._flipEnd = FacingAlong(step._cells, step._cells.Length - 2, step._flipFrom);
                    if (step._cells.Length > 1)
                    {
                        _cell[id] = step._cells[^1];
                        _flip[id] = step._flipEnd;
                    }
                    break;

                case BeatKind.Bites:
                case BeatKind.Dodged:
                    if (beat.Events.OfType<Bit>().FirstOrDefault() is not { } bit) break;
                    step._other = _you;
                    var youX = BoardLayout.Feet(_cell[0]).X;
                    step._otherX = youX;
                    step._flipEnd = youX < step._actorX || (youX == step._actorX && step._flipFrom);
                    _flip[id] = step._flipEnd;
                    if (!bit.Dodged)
                    {
                        step._panelTo = _panel with { Health = Math.Max(0, _panel.Health - (double)bit.Damage / _statsBefore.MaxHealth) };
                        Number(step, _you, bit.Damage, good: false, from: StrikeTicks, length: beat.Ticks - StrikeTicks);
                    }
                    break;

                case BeatKind.IsHurt when beat.Events.OfType<Hurt>().FirstOrDefault() is { } hurt:
                    Number(step, sprite, hurt.Damage, good: false, from: 0, length: beat.Ticks);
                    step._fadesOut = hurt.Defeated;
                    if (hurt.Defeated)
                    {
                        _opacity[id] = 0;
                        step._foodChanges = new[]
                        {
                            (hurt.At, Sight.ShowsFood(_after, _statsAfter, hurt.At, Food.Plants),
                                Sight.ShowsFood(_after, _statsAfter, hurt.At, Food.Meat) ? _after.Meat[hurt.At] : 0),
                        };
                    }
                    break;
            }

            step._setActorMarks = true;
            PlanLastMarks(step, id, index);
        }

        /// <summary>A creature's last beat of the turn: its marks as the turn left it (its alert, a slow worn off), as the next rest will draw them.</summary>
        private void PlanLastMarks(BeatStep step, int id, int index)
        {
            if (_lastBeat.GetValueOrDefault(id, -1) != index || !_marks.ContainsKey(id)) return;
            step._setActorMarks = true;
            if (step._marksTo == default && step._marksFrom == default) step._marksFrom = step._marksTo = _marks[id];
            if (_after.Creatures.Find(c => c.Id == id) is { } left)
            {
                step._marksTo = PlaybackSprites.MarksOf(left);
                _marks[id] = step._marksTo;
            }
        }

        /// <summary>The ring of the one acting (H22): white while its action lasts, its own again at the end of its last beat.</summary>
        private void PlanActingRing(BeatStep step, int id, int index)
        {
            if (id <= 0 || !_sprites.TryGetValue(id, out var sprite)) return;
            if (step._actor is null) step._actor = sprite;
            var own = PlaybackSprites.RingOf(CreatureOf(id));
            step._ringDuring = Ring.Acting;
            step._ringEnd = _lastBeat.GetValueOrDefault(id, -1) == index ? own : Ring.Acting;
        }

        /// <summary>The predator whose action a beat sits in (a Second wind inside a bite): the creature of the last beat of a creature before it.</summary>
        private int ActingCreature(int index)
        {
            var beats = _play.Script.Beats;
            for (var i = index - 1; i >= 0; i--)
            {
                if (beats[i].Kind == BeatKind.Gap) return 0;
                if (beats[i].Actor == BeatActor.Creature) return beats[i].Creature;
            }
            return 0;
        }

        private void PlanSmallGame(BeatStep step, Beat beat)
        {
            var group = new List<(SceneSprite, int[], bool, bool, byte)>();
            foreach (var stepped in beat.Events.OfType<Stepped>())
            {
                if (!_sprites.TryGetValue(stepped.Creature, out var sprite)) continue;
                var cells = new[] { stepped.From }.Concat(stepped.Path).ToArray();
                var from = _flip.GetValueOrDefault(stepped.Creature);
                var to = FacingAlong(cells, cells.Length - 2, from);
                // Small game that walks into what you know fades in as it comes (H4: one
                // beat for all of them); one already seen stays whole.
                group.Add((sprite, cells, from, to, _opacity.GetValueOrDefault(stepped.Creature)));
                if (cells.Length > 1)
                {
                    _cell[stepped.Creature] = cells[^1];
                    _flip[stepped.Creature] = to;
                }
                _opacity[stepped.Creature] = 255;
            }
            step._group = group.ToArray();
        }

        private void PlanArrival(BeatStep step, Beat beat, int index)
        {
            var id = beat.Creature;
            if (!_sprites.TryGetValue(id, out var sprite)) return;
            step._actor = sprite;
            step._opacityFrom = _opacity.GetValueOrDefault(id);
            step._opacityTo = 255;
            _opacity[id] = 255;
            var own = PlaybackSprites.RingOf(CreatureOf(id));
            step._ringDuring = own;
            step._ringEnd = own;
            if (_lastBeat.GetValueOrDefault(id, -1) == index && _after.Creatures.Find(c => c.Id == id) is { } left)
                sprite.Marks = PlaybackSprites.MarksOf(left);
        }

        private void PlanTime(BeatStep step, Beat beat, int index)
        {
            var time = beat.Events.OfType<TimePassed>().FirstOrDefault();
            var grew = beat.Events.OfType<Grew>().FirstOrDefault();
            var hurtsNext = index + 1 < _play.Script.Beats.Count && _play.Script.Beats[index + 1].Kind == BeatKind.TimeHurts;
            // Food, water and growth go to what the turn saved; health takes what time
            // gave back, and lands on the saved value here unless hunger or thirst take
            // their share in the beat that follows.
            var health = hurtsNext
                ? Math.Clamp(_panel.Health + (time?.Regained ?? 0) / 100.0, 0, 1)
                : _after.You.Health;
            step._panelTo = new PanelValues(health,
                time is not null ? _after.You.Food : _panel.Food,
                time is not null ? _after.You.Water : _panel.Water,
                grew?.To ?? (time is not null ? _after.You.Growth : _panel.Growth));
        }

        private void PlanHunger(BeatStep step, Beat beat, int index)
        {
            step._other = _you;
            var time = beat.Events.OfType<TimePassed>().FirstOrDefault();
            var loss = (time?.Hunger ?? 0) + (time?.Thirst ?? 0);
            var windLater = _play.Script.Beats.Skip(index + 1).Any(b => b.Kind == BeatKind.SecondWind);
            step._panelTo = _panel with
            {
                Health = windLater ? Math.Max(0, _panel.Health - loss / 100.0) : _after.You.Health,
            };
            Number(step, _you, loss, good: false, from: 0, length: beat.Ticks);
        }

        private void PlanStage(BeatStep step, Beat beat)
        {
            if (beat.Events.OfType<StageReached>().FirstOrDefault() is not { } reached) return;
            step._other = _you;
            step._subjectFrom = _subject;
            step._subjectTo = ArtBook.StageSubject(_play.Species, Math.Clamp(reached.Stage, Stages.Hatchling, Stages.Elder));
            _subject = step._subjectTo;
            var marks = _you.Marks & ~Marks.You;
            if (Sight.Hidden(_after, _statsAfter)) marks |= Marks.Hidden;
            else marks &= ~Marks.Hidden;
            if (reached.Stage >= Stages.Elder) marks |= Marks.Elder;
            step._setYouMarks = true;
            step._youMarksEnd = marks;
        }

        /// <summary>A rising number on <paramref name="on"/>: <paramref name="value"/> without its sign, a gain or a hurt, over the ticks given.</summary>
        private static void Number(BeatStep step, SceneSprite on, int value, bool good, int from, int length)
        {
            step._number = new SceneNumber { Value = Math.Abs(value), Good = good, Sign = true };
            step._numberOn = on;
            step._numberFrom = from;
            step._numberLength = Math.Max(1, length);
        }

        private Creature CreatureOf(int id) =>
            _after.Creatures.Find(c => c.Id == id) ?? _before.Creatures.Find(c => c.Id == id)
            ?? new Creature { Id = id, Kind = CreatureKind.Predator };
    }
}
