using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// BeatStep, its planner part (H90): the same class, split for reading; see BeatStep.cs.
/// </summary>
internal sealed partial class BeatStep
{
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
            PlanLeaving(steps);
            return steps;
        }

        /// <summary>
        /// The board never keeps a creature the player no longer knows (H77): a sprite
        /// the rest after the turn will not draw (its creature not known in the turn's
        /// result, or not there), that no defeat has already faded, leaves in its own
        /// last beat of the turn (a predator that walks out of what you know, small game
        /// in the small game beat), fading out over that beat's last
        /// <see cref="LeaveTicks"/>; one with no beat of its own (your walk left it
        /// behind) is gone at the end of the turn's first beat, yours, with the fog. So
        /// after the last beat nothing is on the board that the rest will not draw, and
        /// a skip at any tick leaves the same (each beat's end sets it).
        /// </summary>
        private void PlanLeaving(BeatStep[] steps)
        {
            if (steps.Length == 0) return;
            var lastOwn = new Dictionary<int, int>();
            for (var i = 0; i < steps.Length; i++)
            {
                var beat = steps[i]._beat;
                if (beat.Actor == BeatActor.Creature && beat.Creature > 0) lastOwn[beat.Creature] = i;
                if (beat.Kind == BeatKind.SmallGame)
                    foreach (var stepped in beat.Events.OfType<Stepped>()) lastOwn[stepped.Creature] = i;
            }
            foreach (var sprite in _play.Scene.Sprites)
            {
                if (sprite.Kind == SpriteKind.You || _opacity.GetValueOrDefault(sprite.Creature) == 0) continue;
                if (_after.Creatures.Find(c => c.Id == sprite.Creature) is { } left && Sight.Knows(_after, _statsAfter, left)) continue;
                if (lastOwn.TryGetValue(sprite.Creature, out var at))
                    steps[at]._leaving = steps[at]._leaving.Append(sprite).ToArray();
                else
                    steps[0]._leftBehind = steps[0]._leftBehind.Append(sprite).ToArray();
                _opacity[sprite.Creature] = 0;
            }
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
            step._actorFeet = BoardLayout.Feet(_cell[0]);
            step._targetFeet = step._actorFeet;
            if (_sprites.TryGetValue(struck.Creature, out var target))
            {
                step._other = target;
                step._targetFeet = BoardLayout.Feet(_cell.GetValueOrDefault(struck.Creature, struck.At));
                var targetX = step._targetFeet.X;
                // You face what you strike; facing straight up or down keeps the last.
                step._flipEnd = targetX < step._actorFeet.X || (targetX == step._actorFeet.X && _flip[0]);
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
                    from: strikes ? WindUpTicks : 0, length: strikes ? BeatTicks.Strike - WindUpTicks : beat.Ticks);
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
            step._actorFeet = BoardLayout.Feet(cell);
            step._targetFeet = step._actorFeet;
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
                    step._targetFeet = BoardLayout.Feet(_cell[0]);
                    var youX = step._targetFeet.X;
                    step._otherX = youX;
                    step._flipEnd = youX < step._actorFeet.X || (youX == step._actorFeet.X && step._flipFrom);
                    _flip[id] = step._flipEnd;
                    if (!bit.Dodged)
                    {
                        step._panelTo = _panel with { Health = Math.Max(0, _panel.Health - (double)bit.Damage / _statsBefore.MaxHealth) };
                        Number(step, _you, bit.Damage, good: false, from: WindUpTicks, length: beat.Ticks - WindUpTicks);
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
