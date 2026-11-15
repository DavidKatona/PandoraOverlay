using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

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
internal sealed partial class BeatStep : IStep
{
    // The art's action names for acting (task H11's walk, H12's strike, hurt and eat).
    // The painter's own names (ArtNames) stop at "stand": these are asked only here.
    internal const string Walk = "walk", Strike = "strike", Hurt = "hurt", Eat = "eat";

    /// <summary>
    /// A strike's wind-up (<c>strike/0</c>, task H12's art): the ticks of a strike
    /// before its blow lands. The hit's hurt and its number begin on this tick, never
    /// during the wind-up (H75; the beat table's "(3)").
    /// </summary>
    internal const int WindUpTicks = 3;

    /// <summary>
    /// A whole strike as the art draws it (H75): the wind-up's 3 and the blow's 6
    /// (<c>strike/1</c>). The art does not loop it and would hold the blow for ever,
    /// so the view puts the striker back on <c>stand</c> once these are over, also
    /// where the beat goes on ("It bites", 10). A dodge (8) ends inside it.
    /// </summary>
    internal const int StrikeTicks = 9;

    /// <summary>A hurt as the art draws it (H75): one frame for 4 ticks; then the view puts it back on <c>stand</c>.</summary>
    internal const int HurtTicks = 4;

    /// <summary>
    /// How far the blow moves the striker toward its target's feet while
    /// <c>strike/1</c> shows (H75): 3 pixels along its own row; for a target in the
    /// row below or above, this many down or up and <see cref="BlowAcross"/> across.
    /// The art cannot lunge by itself (its frames never leave their feet), so this
    /// is what makes the blow land. The knob the plan's section 10 names if a bite
    /// reads as a flicker in the owner's hand.
    /// </summary>
    internal const int BlowReach = 3;

    /// <summary>The blow's across part toward a target in another row (H75): the slant of a hex neighbour, 2 across for <see cref="BlowReach"/>'s 3 down or up. Turn both together.</summary>
    internal const int BlowAcross = 2;

    /// <summary>
    /// A creature the player no longer knows fades out, whole, over this many last
    /// ticks of its own last beat of the turn (H77; the whole beat if it is shorter),
    /// so it is seen leaving and nothing stays on the board that the rest will not draw.
    /// </summary>
    internal const int LeaveTicks = 3;

    /// <summary>How far a strike with no art leans toward its target, tick by tick over the wind-up: 4 pixels and back (H30).</summary>
    private static readonly int[] StrikeLean = { 2, 4, 2 };

    /// <summary>A hurt with no art: opacity 120 on these ticks of the hurt, twice for 2 ticks each (H30).</summary>
    internal const byte HurtBlink = 120;

    /// <summary>A dodge: your dino hops this far away from the biter and back, tick by tick, from the blow's first tick.</summary>
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
    private (int X, int Y) _actorFeet;    // the actor's feet when the beat begins: a strike leans or blows from them, and ends on them
    private (int X, int Y) _targetFeet;   // the feet of what it strikes or bites: the blow moves toward them
    private int _otherX;                  // the one struck or bitten (x: a dodge hops from it)
    private SceneSprite[] _leaving = Array.Empty<SceneSprite>();   // this beat is their last, and the rest will not draw them: they fade out (H77)
    private SceneSprite[] _leftBehind = Array.Empty<SceneSprite>(); // known before your move, not after, with no beat of their own: gone at this beat's end (H77)
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
        // What your move left behind goes with the fog, where the quiet ones appear (H77).
        foreach (var sprite in _leftBehind) sprite.Opacity = 0;
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

        // Leaving what the player knows (H77): over the beat's last LeaveTicks, whole
        // (its ring, marks and number follow its opacity), to 0 at the beat's end. On
        // top of whatever the beat itself does with it (a walk out, small game's step);
        // never above the beat's own opacity.
        if (_leaving.Length > 0)
        {
            var from = Math.Max(0, _beat.Ticks - LeaveTicks);
            if (k >= from)
            {
                var leave = Fade(255, 0, k - from, _beat.Ticks - from);
                foreach (var sprite in _leaving) sprite.Opacity = Math.Min(sprite.Opacity, leave);
            }
        }
    }

    /// <summary>
    /// Your hit (or Thagomizer's sweep): your <c>strike</c> toward it on ticks 0 to 8,
    /// the blow moving you toward it from tick 3 (H75); its <c>hurt</c> on the 4 ticks
    /// from the one the blow lands (3; a swept one's from 0) and its number from that
    /// tick; a defeat fades it out (after its hurt's 6 for your main hit, in its last
    /// third for a swept one) and its meat appears.
    /// </summary>
    private void ShowStrike(int k, bool end, bool strikes)
    {
        var hurtFrom = strikes ? WindUpTicks : 0;
        if (_actor is not null && strikes) StrikeToward(_actor, k, end);
        if (_other is not null)
        {
            var hurtLength = strikes ? (_fadesOut ? BeatTicks.Strike - WindUpTicks : _beat.Ticks - WindUpTicks) : _beat.Ticks;
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

    /// <summary>
    /// A predator's bite on you, or the miss you dodged (H75): its <c>strike</c> toward
    /// you on ticks 0 to 8 (to the beat's end in a dodge), the blow moving it toward
    /// you from tick 3; then your <c>hurt</c> on ticks 3 to 6, the number and the
    /// health bar from tick 3; or, dodged, your hop away from it on the blow's ticks.
    /// </summary>
    private void ShowBite(int k, bool end, bool dodged)
    {
        if (_actor is not null)
        {
            StrikeToward(_actor, k, end);
            _actor.Ring = end ? _ringEnd : _ringDuring;
        }
        if (_other is not null)
        {
            var after = k - WindUpTicks;
            if (dodged)
            {
                var hop = !end && after >= 0 && after < DodgeHop.Length ? DodgeHop[after] : 0;
                _other.X = _otherX + (_actorFeet.X <= _otherX ? hop : -hop);
            }
            else
            {
                var length = _beat.Ticks - WindUpTicks;
                if (end) HurtAt(_other, length, length, end: true, fadesOut: false);
                else if (after >= 0) HurtAt(_other, after, length, end: false, fadesOut: false);
                else
                {
                    _other.Action = ArtNames.Stand;
                    _other.Tick = 0;
                }
            }
        }
        if (!dodged)
        {
            var span = Math.Max(1, _beat.Ticks - WindUpTicks);
            Slide(Math.Max(0, k - WindUpTicks), span, end);
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

    /// <summary>
    /// The striker at tick <paramref name="k"/> of its beat, facing its target from
    /// tick 0 (H75). With the art: <c>strike</c> with the tick since it began, for
    /// <see cref="StrikeTicks"/> or to the beat's end if that comes first (a dodge);
    /// while the blow shows (from <see cref="WindUpTicks"/>) it stands
    /// <see cref="Blow"/> away from its feet toward its target, its ring, marks and
    /// ordinal with it (the painter draws them from <c>X</c>, <c>Y</c>). Without it, the
    /// stand-in (H30): the standing frame leaning 4 pixels toward the target and back
    /// over the wind-up. Before and after, and at the beat's end: standing on its feet.
    /// </summary>
    private void StrikeToward(SceneSprite sprite, int k, bool end)
    {
        sprite.Flip = _flipEnd;
        (sprite.X, sprite.Y) = _actorFeet;
        sprite.Action = ArtNames.Stand;
        sprite.Tick = 0;
        if (end) return;
        if (Drawn(sprite.Subject, Strike))
        {
            if (k >= Math.Min(StrikeTicks, _beat.Ticks)) return;
            sprite.Action = Strike;
            sprite.Tick = k;
            if (k >= WindUpTicks)
            {
                var (dx, dy) = Blow(_actorFeet, _targetFeet, _flipEnd);
                sprite.X += dx;
                sprite.Y += dy;
            }
        }
        else if (k < WindUpTicks)
        {
            sprite.X += _flipEnd ? -StrikeLean[k] : StrikeLean[k];
        }
    }

    /// <summary>
    /// Where the blow puts a striker standing on <paramref name="feet"/>, from them
    /// (H75): <see cref="BlowReach"/> toward a target in its own row, (±3, 0); for one
    /// in the row below or above, <see cref="BlowAcross"/> across and
    /// <see cref="BlowReach"/> down or up, (±2, ±3). Across is toward the target, or,
    /// straight above or below it, the way it faces.
    /// </summary>
    internal static (int Dx, int Dy) Blow((int X, int Y) feet, (int X, int Y) target, bool flip)
    {
        var across = target.X > feet.X ? 1 : target.X < feet.X ? -1 : flip ? -1 : 1;
        if (target.Y == feet.Y) return (across * BlowReach, 0);
        return (across * BlowAcross, (target.Y > feet.Y ? 1 : -1) * BlowReach);
    }

    /// <summary>
    /// A hurt at tick <paramref name="k"/> of the <paramref name="length"/> ticks it is
    /// given: the art's <c>hurt</c> frame on its first <see cref="HurtTicks"/>, then
    /// standing (H75: the art holds its frame for ever, so the view ends it); or, with
    /// no <c>hurt</c> frame, two blinks at opacity 120 (H30). A defeat fades out over
    /// the length's last third.
    /// </summary>
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
        var hurts = drawn && k < HurtTicks;
        sprite.Action = hurts ? Hurt : ArtNames.Stand;
        sprite.Tick = hurts ? k : 0;
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
}
