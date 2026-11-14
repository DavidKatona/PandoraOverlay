namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// What the player may know, asked by the script: whether <paramref name="creature"/>,
/// standing where it stands in <paramref name="run"/>, can be seen or sensed from
/// where you stand there. The view and the tests hand in <c>Sight.Knows</c> over a
/// state (plan 1.3: in sight, within Wary's sense for a predator, the apex always,
/// H6); the script never decides it for itself, so the playback can show the
/// player no creature the board would not.
/// </summary>
internal delegate bool Known(RunState run, Creature creature);

/// <summary>
/// A turn as the player is shown it: the beats the board plays, in order, and the
/// rows of the turn list as they read once the beats have played. Both come from
/// the one list of events through <see cref="TurnScripts.Build"/>, so the playback
/// and the list cannot disagree (H2), and there is no other log.
/// </summary>
/// <param name="Beats">In playing order: the events' own order (H1), with a Gap between two actors (G9).</param>
/// <param name="Rows">You first, each predator in id order, small game, out of sight last (plan 3.4); as codes, never text (H28).</param>
internal sealed record TurnScript(IReadOnlyList<Beat> Beats, IReadOnlyList<TurnRow> Rows)
{
    /// <summary>
    /// No beat lasts longer: two seconds at speed 1. The engine finishes for it
    /// any queued step still running after 300 ticks (<c>Playback.StepLimitTicks</c>)
    /// and the rest of it would jump (A35); 60 keeps every beat far under that, and
    /// a turn's longest beat (a stage, 20) under this.
    /// </summary>
    public const int MaxBeatTicks = 60;

    /// <summary>The sum of the beats' ticks: what the playback lasts at speed 1, since one beat is one queued step and the queue plays them back to back (A35).</summary>
    public int TotalTicks => Beats.Sum(b => b.Ticks);

    /// <summary>
    /// Beat <paramref name="beat"/> is the last of its row: when it finishes the
    /// view writes that row's words, so a row says what its actor did once it has
    /// done it all ("spotted you · bit you: 12" appears after the bite, not at the
    /// Focus). False for a beat of no row.
    /// </summary>
    public bool IsLastOfRow(int beat)
    {
        var row = Beats[beat].RowIndex;
        if (row < 0) return false;
        for (var later = beat + 1; later < Beats.Count; later++)
            if (Beats[later].RowIndex == row) return false;
        return true;
    }

    // Two scripts of the same turn are equal beat for beat and row for row (a
    // record would compare the two lists by reference).
    public bool Equals(TurnScript? other) =>
        other is not null && Beats.SequenceEqual(other.Beats) && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Beats.Count, Rows.Count, TotalTicks);
}

/// <summary>
/// Turns a turn's events into its <see cref="TurnScript"/>: pure and total (H2).
///
/// <para><b>The order (H1)</b> is the events' order, which is the rules' own: your
/// move; each predator in id order; small game; out of sight; time and arrivals.
/// The script never reorders, so what is shown is what happened. One liberty: a
/// <see cref="Grew"/> rides in the Time passes beat that its <see cref="TimePassed"/>
/// began (the beat table's "From: TimePassed, Grew"), and the beat for health lost
/// to hunger and thirst follows that beat at once.</para>
///
/// <para><b>Who acts, one at a time (G9).</b> Your beats come first and alone. A
/// known predator's action is a Focus (its ring lit, its row marked, the banner
/// already naming what it does), then a beat for each of its events: what it
/// noticed, its step or why it stayed, its bite or your dodge, its hurts, and a
/// Second wind its bite caused. A Gap of nothing moving follows your beats, each
/// predator's and the small game's, whenever another beat comes after; and two
/// creatures' beats are never next to each other without one (two arrivals in a
/// turn).</para>
///
/// <para><b>What is known (H3, H6).</b> A creature makes beats and a row only when
/// the <see cref="Known"/> handed in says so for it as its action began (the island
/// replayed from the state before: your move done, the creatures before it moved)
/// or as the turn ended (the state after; for one defeated during its action,
/// where it fell). Any other creature's action makes no beat, no row, no banner and
/// no event in any beat; together they are the one Out of sight beat, which says
/// "Something heard you" when one of those predators heard or spotted you, and
/// "Out of sight: quiet" otherwise: never which, how many or where. The apex is
/// known everywhere by that rule (Sight.Knows), so it has beats from afar.</para>
///
/// <para><b>Ends.</b> A run left has no beat at all: the summary shows at once, and
/// the list stays as last shown. A run that ended before the world's turn (you
/// defeated the apex, or a predator caught you) has no Out of sight beat: nothing
/// out of sight acted, and its row and the small game's say nothing.</para>
/// </summary>
internal static class TurnScripts
{
    /// <summary>The script of <paramref name="turn"/>. Reads the two states, never changes them.</summary>
    public static TurnScript Build(TurnResult turn, Known known) => new Builder(turn, known).Build();

    /// <summary>
    /// The turn list of a run that has had no turn yet (turn 0): "hatched", each
    /// predator known with nothing said, small game with nothing said when one is
    /// known, and "quiet" out of sight.
    /// </summary>
    public static IReadOnlyList<TurnRow> RowsAtStart(RunState run, Known known)
    {
        var rows = new List<TurnRow> { YouRow(RowCode.Hatched) };
        foreach (var c in run.Creatures.Where(c => c.Kind == CreatureKind.Predator && known(run, c)).OrderBy(c => c.Id))
            rows.Add(PredatorRow(c, c.Alert, RowCode.None, RowCode.None, 0));
        if (run.Creatures.Any(c => c.Kind == CreatureKind.SmallGame && known(run, c)))
            rows.Add(PlainRow(RowActor.SmallGame, RowCode.None));
        rows.Add(PlainRow(RowActor.Unseen, RowCode.Quiet));
        return rows;
    }

    /// <summary>
    /// Every kind of event the script reads. An event of any other kind is skipped
    /// (the script stays total), and a test fails for it: every concrete
    /// <see cref="TurnEvent"/> of the game must be here.
    /// </summary>
    internal static IReadOnlyCollection<Type> Reads { get; } = new[]
    {
        typeof(Walked), typeof(Ate), typeof(Drank), typeof(Rested), typeof(Struck), typeof(Afflicted),
        typeof(Noticed), typeof(Stepped), typeof(Stayed), typeof(Bit), typeof(Hurt), typeof(SecondWind),
        typeof(TimePassed), typeof(Grew), typeof(StageReached), typeof(Arrived), typeof(PlantsGrew),
        typeof(ApexArrived), typeof(ApexWaits), typeof(InstinctsOffered), typeof(RunEnded),
    };

    // ---- rows -----------------------------------------------------------------------

    private static TurnRow YouRow(RowCode code, int a = 0, int b = 0, Food food = default,
        PredatorType targetType = default, int targetOrdinal = 0, bool targetIsSmallGame = false) =>
        new(RowActor.You, default, 0, 0, code, RowCode.None, a, b, food, targetType, targetOrdinal, targetIsSmallGame);

    private static TurnRow PredatorRow(Creature who, int alert, RowCode first, RowCode second, int a) =>
        new(RowActor.Predator, who.Type, who.Ordinal, alert, first, second, a, 0, default, default, 0, false);

    private static TurnRow PlainRow(RowActor actor, RowCode code) =>
        new(actor, default, 0, 0, code, RowCode.None, 0, 0, default, default, 0, false);

    // ---- the builder ----------------------------------------------------------------

    /// <summary>Which row a beat belongs to, before the rows are numbered.</summary>
    private enum RowOf { None, You, Predator, SmallGame, Unseen }

    /// <summary>A beat while it is being made: its banner and events can still be added to.</summary>
    private sealed class Draft
    {
        public BeatKind Kind;
        public BeatActor Actor;
        public int Creature;
        public int Ticks;
        public string Banner = "";
        public List<TurnEvent> Events = new();
        public RowOf Row;
        public int RowCreature;   // the predator's id, for RowOf.Predator
    }

    /// <summary>What a predator's row will say: what it noticed, and what it did.</summary>
    private sealed class PredatorSays
    {
        public RowCode First;
        public RowCode Second;
        public int A;
    }

    private sealed class Builder
    {
        private readonly TurnResult _turn;
        private readonly Known _known;
        // The island as it stands at the moment being read: the state before,
        // replayed event by event (your cell, each creature's cell, who is gone or
        // has come), so a creature is asked about where it stood as its action
        // began. A copy: the turn's states are never touched.
        private readonly RunState _scene;
        // Every creature the turn names, by id, for its kind and its name: the
        // state before's, then the arrivals of the state after.
        private readonly Dictionary<int, Creature> _roster = new();
        // The creatures for which an ApexArrived is in the events: their beat is
        // the Apex beat, not an Arrives one.
        private readonly HashSet<int> _apexes;

        private readonly List<Draft> _drafts = new();
        private readonly Dictionary<int, PredatorSays> _predators = new();
        private TurnRow? _you;
        private Draft? _lastStrike;
        private Draft? _time;
        private bool _struck;
        private bool _yourBlockOpen;
        private bool _gapOwed;
        private bool _played;
        private bool _worldBegan;
        private bool _endedEarly;
        private bool _unseenShown;
        private bool _unseenHeard;
        private bool _gameMoved;
        private bool _gameFled;

        public Builder(TurnResult turn, Known known)
        {
            _turn = turn;
            _known = known;
            _scene = turn.Before.Clone();
            foreach (var c in turn.Before.Creatures) _roster[c.Id] = c;
            foreach (var c in turn.After.Creatures) _roster.TryAdd(c.Id, c);
            _apexes = turn.Events.OfType<ApexArrived>().Select(a => a.Creature).ToHashSet();
        }

        public TurnScript Build()
        {
            var events = _turn.Events;
            var i = 0;
            while (i < events.Count)
            {
                var e = events[i];
                if (IsYourMove(e))
                {
                    YourMove(e);
                    i++;
                    continue;
                }

                // Anything else closes your beats: a Gap will follow them.
                if (_yourBlockOpen)
                {
                    _gapOwed = true;
                    _yourBlockOpen = false;
                }

                if (ActionOf(e) is int id)
                {
                    if (!_roster.TryGetValue(id, out var who))
                    {
                        i++;   // a creature the turn does not hold: nothing to show
                        continue;
                    }
                    var j = i + 1;
                    if (who.Kind == CreatureKind.SmallGame)
                    {
                        // Every small game's move, one after another: one beat (H4).
                        while (j < events.Count && ActionOf(events[j]) is int next
                               && _roster.TryGetValue(next, out var other) && other.Kind == CreatureKind.SmallGame) j++;
                        SmallGameMoves(events.Skip(i).Take(j - i).ToList());
                    }
                    else
                    {
                        // The predator's whole action: its events in a row, and a
                        // Second wind its bite caused, which sits among them.
                        while (j < events.Count && (ActionOf(events[j]) == id || events[j] is SecondWind)) j++;
                        PredatorActs(id, events.Skip(i).Take(j - i).ToList());
                    }
                    i = j;
                    continue;
                }

                World(e);
                i++;
            }

            // A turn the world finished with no world event still had its out of
            // sight; one that ended first did not.
            if (_played && !_unseenShown && !_endedEarly) ShowUnseen();

            // Nothing was played (a run left, a choice that takes no turn): the
            // list stays as last shown.
            if (_drafts.Count == 0) return new TurnScript(Array.Empty<Beat>(), _turn.Before.LastTurn.ToArray());

            return Finish();
        }

        // ---- your move --------------------------------------------------------------

        private static bool IsYourMove(TurnEvent e) => e is Walked or Ate or Drank or Rested or Struck or Afflicted;

        private void YourMove(TurnEvent e)
        {
            switch (e)
            {
                case Walked w:
                {
                    var cells = w.Path.Count;
                    var (perCell, banner, code) = w.Pace switch
                    {
                        Pace.Sneak => (BeatTicks.SneakPerCell, HatchlingText.YouSneak, RowCode.Sneaked),
                        Pace.Sprint => (BeatTicks.SprintPerCell, HatchlingText.YouSprint, RowCode.Sprinted),
                        _ => (BeatTicks.WalkPerCell, HatchlingText.YouWalk, RowCode.Walked),
                    };
                    if (cells > 0) _scene.You.At = w.Path[^1];
                    Mine(BeatKind.YouWalk, 0, perCell * cells, banner, w);
                    // A sprint says how far it went ("sprinted 2 · noise 5"); a walk
                    // and a sneak are always one cell.
                    _you ??= code == RowCode.Sprinted ? YouRow(code, cells, w.Noise) : YouRow(code, w.Noise);
                    break;
                }
                case Ate a:
                    Mine(BeatKind.YouEat, 0, BeatTicks.Eat, HatchlingText.YouEat, a);
                    _you ??= YouRow(RowCode.Ate, a.Gain, food: a.Kind);
                    break;
                case Drank d:
                    Mine(BeatKind.YouDrink, 0, BeatTicks.Drink, HatchlingText.YouDrink, d);
                    _you ??= YouRow(RowCode.Drank, d.Gain);
                    break;
                case Rested r:
                    Mine(BeatKind.YouRest, 0, BeatTicks.Rest, HatchlingText.YouRest, r);
                    _you ??= YouRow(RowCode.Rested, r.Healed);
                    break;
                case Struck s:
                    Strike(s);
                    break;
                case Afflicted f:
                    // No beat of its own: its mark appears with the hit that caused it.
                    _lastStrike?.Events.Add(f);
                    break;
            }
        }

        private void Strike(Struck s)
        {
            _played = true;
            if (!_roster.TryGetValue(s.Creature, out var target)) return;
            var game = target.Kind == CreatureKind.SmallGame;
            if (!s.Swept && !_struck)
            {
                _struck = true;
                var banner = game
                    ? (s.Defeated ? HatchlingText.YouCaughtGame : HatchlingText.YouHitGame(s.Damage))
                    : (s.Defeated ? HatchlingText.YouDefeated(target.Type, target.Ordinal) : HatchlingText.YouHit(target.Type, target.Ordinal, s.Damage));
                var ticks = BeatTicks.Strike + (s.Defeated ? BeatTicks.StrikeDefeatFade : 0);
                _lastStrike = Mine(BeatKind.YouStrike, s.Creature, ticks, banner, s);
                _you ??= YouRow(s.Defeated ? RowCode.Defeated : RowCode.Hit, s.Damage,
                    targetType: target.Type, targetOrdinal: target.Ordinal, targetIsSmallGame: game);
            }
            else
            {
                // Thagomizer's sweep: the same blow on another creature beside you.
                var banner = game ? HatchlingText.AndHitGame(s.Damage) : HatchlingText.AndHit(target.Type, target.Ordinal, s.Damage);
                _lastStrike = Mine(BeatKind.AlsoStruck, s.Creature, BeatTicks.AlsoStruck, banner, s);
            }
            if (s.Defeated) _scene.Creatures.RemoveAll(c => c.Id == s.Creature);
        }

        private Draft Mine(BeatKind kind, int creature, int ticks, string banner, TurnEvent e)
        {
            _played = true;
            _yourBlockOpen = true;
            return Add(kind, BeatActor.You, creature, ticks, banner, RowOf.You, 0, e);
        }

        // ---- a creature's action ----------------------------------------------------

        /// <summary>The creature an event of a creature's action names, or null for any other event.</summary>
        private static int? ActionOf(TurnEvent e) => e switch
        {
            Noticed n => n.Creature,
            Stepped s => s.Creature,
            Stayed s => s.Creature,
            Bit b => b.Creature,
            Hurt h => h.Creature,
            _ => null,
        };

        private void PredatorActs(int id, List<TurnEvent> action)
        {
            var me = _scene.Creatures.Find(c => c.Id == id);
            if (me is null) return;   // already gone from the island: it cannot act

            var knownAtStart = _known(_scene, me);
            var defeated = false;
            foreach (var e in action)
            {
                if (e is Stepped { Path.Count: > 0 } s) me.At = s.Path[^1];
                if (e is Hurt { Defeated: true }) defeated = true;
            }
            var knownAtEnd = KnownAtEnd(me);
            if (defeated) _scene.Creatures.Remove(me);

            if (!knownAtStart && !knownAtEnd)
            {
                // Out of sight: it makes no beat and no row, and counts only
                // towards the one line that says something heard you (H3).
                if (action.Any(e => e is Noticed { Kind: Notice.Heard or Notice.Spotted })) _unseenHeard = true;
                return;
            }

            var who = _roster[id];
            var says = Says(id);
            var focus = Add(BeatKind.Focus, BeatActor.Creature, id, BeatTicks.Focus, "", RowOf.Predator, id, null);
            var bit = false;
            var stayed = RowCode.None;
            var stepped = RowCode.None;
            foreach (var e in action)
            {
                switch (e)
                {
                    case Noticed n:
                        Its(BeatKind.Notices, id, BeatTicks.Notices, NoticeBanner(who, n.Kind), n);
                        if (says.First == RowCode.None)
                            says.First = n.Kind switch { Notice.Spotted => RowCode.Spotted, Notice.Heard => RowCode.Heard, _ => RowCode.Lost };
                        break;
                    case Stepped s:
                        Its(BeatKind.Steps, id, BeatTicks.StepPerCell * s.Path.Count, StepBanner(who, s.Gait), s);
                        stepped = s.Gait switch { Gait.Hunt => RowCode.Hunting, Gait.Search => RowCode.Searching, _ => RowCode.Wandered };
                        break;
                    case Stayed st:
                        var (ticks, banner, code) = st.Why switch
                        {
                            Idle.Dazed => (BeatTicks.Stuck, HatchlingText.IsDazed(who.Type, who.Ordinal), RowCode.Dazed),
                            Idle.Held => (BeatTicks.Stuck, HatchlingText.IsHeld(who.Type, who.Ordinal), RowCode.Held),
                            _ => (BeatTicks.Waits, HatchlingText.Waits(who.Type, who.Ordinal), RowCode.Waited),
                        };
                        Its(BeatKind.Stays, id, ticks, banner, st);
                        stayed = code;
                        break;
                    case Bit b when b.Dodged:
                        Its(BeatKind.Dodged, id, BeatTicks.Dodge, HatchlingText.YouDodge(who.Type, who.Ordinal), b);
                        if (!bit) { bit = true; says.Second = RowCode.Missed; }
                        break;
                    case Bit b:
                        Its(BeatKind.Bites, id, BeatTicks.Bite, HatchlingText.Bites(who.Type, who.Ordinal, b.Damage), b);
                        if (!bit) { bit = true; says.Second = RowCode.Bit; says.A = b.Damage; }
                        break;
                    case Hurt h:
                        Its(BeatKind.IsHurt, id, BeatTicks.Hurt, h.By == HurtBy.Spikes
                            ? HatchlingText.SpikesHurt(who.Type, who.Ordinal, h.Damage, h.Defeated)
                            : HatchlingText.Bleeds(who.Type, who.Ordinal, h.Damage, h.Defeated), h);
                        break;
                    case SecondWind w:
                        // Your ring flashes, inside its action: its row keeps the marker.
                        Add(BeatKind.SecondWind, BeatActor.You, 0, BeatTicks.SecondWind, HatchlingText.SecondWind, RowOf.Predator, id, w);
                        break;
                }
            }

            // The Focus already names what it does: the text of its next beat.
            var next = _drafts.IndexOf(focus) + 1;
            focus.Banner = next < _drafts.Count ? _drafts[next].Banner : HatchlingText.Name(who.Type, who.Ordinal);

            // Its row says one thing it did (plan 3.4): defeated, else its bite,
            // else why it stayed, else how it moved; what it noticed in front.
            if (defeated) says.Second = RowCode.WasDefeated;
            else if (!bit) says.Second = stayed != RowCode.None ? stayed : stepped;

            _gapOwed = true;
        }

        private void SmallGameMoves(List<TurnEvent> action)
        {
            var shown = new List<TurnEvent>();
            var fled = false;
            foreach (var e in action)
            {
                if (e is not Stepped s) continue;
                var me = _scene.Creatures.Find(c => c.Id == s.Creature);
                if (me is null) continue;
                var knownAtStart = _known(_scene, me);
                if (s.Path.Count > 0) me.At = s.Path[^1];
                if (!knownAtStart && !KnownAtEnd(me)) continue;   // out of sight: not shown, not counted
                shown.Add(s);
                if (s.Gait == Gait.Flee) fled = true;
            }
            if (shown.Count == 0) return;

            // All of them in one beat (H4): they are not hunters.
            var beat = Add(BeatKind.SmallGame, BeatActor.SmallGame, 0, BeatTicks.SmallGame,
                fled ? HatchlingText.GameScatters : HatchlingText.GameMoves, RowOf.SmallGame, 0, null);
            beat.Events.AddRange(shown);
            _gameMoved = true;
            _gameFled |= fled;
            _gapOwed = true;
        }

        /// <summary>Known as the turn ended: in the state after, or, for one gone from it this turn, where it stood last.</summary>
        private bool KnownAtEnd(Creature me)
        {
            var after = _turn.After.Creatures.Find(c => c.Id == me.Id);
            return after is not null ? _known(_turn.After, after) : _known(_scene, me);
        }

        private void Its(BeatKind kind, int id, int ticks, string banner, TurnEvent e) =>
            Add(kind, BeatActor.Creature, id, ticks, banner, RowOf.Predator, id, e);

        private static string NoticeBanner(Creature who, Notice kind) => kind switch
        {
            Notice.Spotted => HatchlingText.Spots(who.Type, who.Ordinal),
            Notice.Heard => HatchlingText.Hears(who.Type, who.Ordinal),
            _ => HatchlingText.Loses(who.Type, who.Ordinal),
        };

        private static string StepBanner(Creature who, Gait gait) => gait switch
        {
            Gait.Hunt => HatchlingText.Hunts(who.Type, who.Ordinal),
            Gait.Search => HatchlingText.Searches(who.Type, who.Ordinal),
            _ => HatchlingText.Wanders(who.Type, who.Ordinal),
        };

        private PredatorSays Says(int id)
        {
            if (!_predators.TryGetValue(id, out var says)) _predators[id] = says = new PredatorSays();
            return says;
        }

        // ---- out of sight, time, arrivals, the end ---------------------------------

        private void ShowUnseen()
        {
            _unseenShown = true;
            Add(BeatKind.Unseen, BeatActor.Unseen, 0,
                _unseenHeard ? BeatTicks.UnseenHeard : BeatTicks.UnseenQuiet,
                _unseenHeard ? HatchlingText.UnseenHeard : HatchlingText.UnseenQuiet,
                RowOf.Unseen, 0, null);
        }

        /// <summary>The world's turn has begun: out of sight comes first, once, after every creature (H3).</summary>
        private void WorldBegins()
        {
            if (_worldBegan) return;
            _worldBegan = true;
            if (_played && !_unseenShown) ShowUnseen();
        }

        private void World(TurnEvent e)
        {
            switch (e)
            {
                case TimePassed t:
                    WorldBegins();
                    _time = Add(BeatKind.TimePasses, BeatActor.World, 0, BeatTicks.TimePasses, HatchlingText.TimePasses, RowOf.None, 0, t);
                    if (t.Hunger > 0 || t.Thirst > 0)
                    {
                        var hurts = t.Hunger > 0 && t.Thirst > 0 ? HatchlingText.StarvingAndParched
                            : t.Hunger > 0 ? HatchlingText.Starving : HatchlingText.Parched;
                        Add(BeatKind.TimeHurts, BeatActor.You, 0, BeatTicks.TimeHurts, hurts, RowOf.None, 0, t);
                    }
                    break;
                case Grew g:
                    WorldBegins();
                    if (_time is not null) _time.Events.Add(g);
                    else _time = Add(BeatKind.TimePasses, BeatActor.World, 0, BeatTicks.TimePasses, HatchlingText.TimePasses, RowOf.None, 0, g);
                    break;
                case StageReached st:
                    WorldBegins();
                    if (st.Stage >= 1 && st.Stage <= Tuning.StageLines.Count)
                        Add(BeatKind.Stage, BeatActor.You, 0, BeatTicks.Stage, HatchlingText.GrewTo(st.Stage), RowOf.None, 0, st);
                    break;
                case Arrived a:
                    WorldBegins();
                    Arrival(a);
                    break;
                case ApexArrived x:
                    WorldBegins();
                    ApexComes(x);
                    break;
                case PlantsGrew or ApexWaits or InstinctsOffered:
                    // Never a beat: plants grow out of sight by rule, the apex's turns
                    // are in the status line, and an offer opens once the playback
                    // has drained.
                    WorldBegins();
                    break;
                case SecondWind w:
                    // Hunger or thirst would have ended the run.
                    Add(BeatKind.SecondWind, BeatActor.You, 0, BeatTicks.SecondWind, HatchlingText.SecondWind, RowOf.None, 0, w);
                    break;
                case RunEnded r:
                    if (!_worldBegan) _endedEarly = true;
                    if (r.Reason == EndReason.Left) break;   // no beat: the summary shows at once
                    // Caught names the one that caught you; one the turn does not hold
                    // (never, from the rules) falls back to the summary's title.
                    var by = _roster.GetValueOrDefault(r.By);
                    var endBanner = r.Reason == EndReason.Caught && by is null
                        ? HatchlingText.OverLost
                        : HatchlingText.EndBanner(r.Reason, by?.Type ?? default, by?.Ordinal ?? 0);
                    Add(BeatKind.End, BeatActor.World, r.By, BeatTicks.End, endBanner, RowOf.None, 0, r);
                    break;
            }
        }

        private void Arrival(Arrived a)
        {
            var who = _turn.After.Creatures.Find(c => c.Id == a.Creature);
            if (who is null) return;
            if (_scene.Creatures.TrueForAll(c => c.Id != who.Id))
            {
                var copy = who.Clone();
                copy.At = a.At;
                _scene.Creatures.Add(copy);
            }
            // Small game arriving makes no beat: it is simply there once the
            // playback has drained. The apex's arrival is its own beat.
            if (who.Kind != CreatureKind.Predator || _apexes.Contains(who.Id)) return;
            // New arrivals out of sight are not announced (plan 3.5).
            if (!_known(_turn.After, who)) return;
            Its(BeatKind.Arrives, who.Id, BeatTicks.Arrives, HatchlingText.Arrives(who.Type, who.Ordinal), a);
            Says(who.Id).Second = RowCode.Arrived;
        }

        private void ApexComes(ApexArrived x)
        {
            var who = _turn.After.Creatures.Find(c => c.Id == x.Creature);
            if (who is not null && _scene.Creatures.TrueForAll(c => c.Id != who.Id))
            {
                var copy = who.Clone();
                copy.At = x.At;
                _scene.Creatures.Add(copy);
            }
            // Always shown: from now on it is known wherever it is (H6).
            if (who is null)
            {
                Add(BeatKind.Apex, BeatActor.Creature, x.Creature, BeatTicks.Apex, HatchlingText.ApexScent, RowOf.None, 0, x);
                return;
            }
            Its(BeatKind.Apex, who.Id, BeatTicks.Apex, HatchlingText.ApexScent, x);
            Says(who.Id).Second = RowCode.Arrived;
        }

        // ---- making beats ------------------------------------------------------------

        /// <summary>
        /// Adds a beat, after a Gap when one is owed (an actor's beats ended) or when
        /// it is one creature's and the beat before it another's (G9: two creatures
        /// never act back to back). A Gap carries the banner before it, since the
        /// banner stays, and belongs to no row.
        /// </summary>
        private Draft Add(BeatKind kind, BeatActor actor, int creature, int ticks, string banner, RowOf row, int rowCreature, TurnEvent? e)
        {
            var last = _drafts.Count > 0 ? _drafts[^1] : null;
            var otherCreature = last is not null && actor == BeatActor.Creature && last.Actor == BeatActor.Creature
                && last.Kind != BeatKind.Gap && last.Creature != creature;
            if (last is not null && (_gapOwed || otherCreature))
                _drafts.Add(new Draft
                {
                    Kind = BeatKind.Gap, Actor = BeatActor.World, Ticks = BeatTicks.Gap, Banner = last.Banner, Row = RowOf.None,
                });
            _gapOwed = false;

            var draft = new Draft
            {
                Kind = kind,
                Actor = actor,
                Creature = creature,
                // At least one tick (a step that finishes at once still takes one),
                // and never long enough to be cut short (A35).
                Ticks = Math.Clamp(ticks, 1, TurnScript.MaxBeatTicks),
                Banner = banner,
                Row = row,
                RowCreature = rowCreature,
            };
            if (e is not null) draft.Events.Add(e);
            _drafts.Add(draft);
            return draft;
        }

        // ---- the rows, and the beats numbered by them --------------------------------

        private TurnScript Finish()
        {
            var before = _turn.Before;
            var after = _turn.After;
            var rows = new List<TurnRow> { _you ?? YouRow(RowCode.None) };

            // A predator has a row when it is known at the end of the turn, or had a
            // beat this turn (it is seen leaving, or fell), in id order (H1).
            var ids = new SortedSet<int>(_predators.Keys);
            foreach (var c in after.Creatures)
                if (c.Kind == CreatureKind.Predator && _known(after, c)) ids.Add(c.Id);
            var predatorRow = new Dictionary<int, int>();
            foreach (var id in ids)
            {
                if (!_roster.TryGetValue(id, out var who)) continue;
                // The alert at the end (the list colours the name by it); for one
                // gone from the island, its last.
                var alert = after.Creatures.Find(c => c.Id == id)?.Alert ?? who.Alert;
                var says = _predators.GetValueOrDefault(id);
                predatorRow[id] = rows.Count;
                rows.Add(PredatorRow(who, alert, says?.First ?? RowCode.None, says?.Second ?? RowCode.None, says?.A ?? 0));
            }

            // Small game has a row when one is known at the start or the end of the
            // turn, or it had a beat.
            var gameRow = -1;
            if (_gameMoved
                || before.Creatures.Any(c => c.Kind == CreatureKind.SmallGame && _known(before, c))
                || after.Creatures.Any(c => c.Kind == CreatureKind.SmallGame && _known(after, c)))
            {
                var code = _gameMoved ? (_gameFled ? RowCode.Scattered : RowCode.Moved)
                    : _endedEarly ? RowCode.None : RowCode.Still;
                gameRow = rows.Count;
                rows.Add(PlainRow(RowActor.SmallGame, code));
            }

            var unseenRow = rows.Count;
            rows.Add(PlainRow(RowActor.Unseen,
                !_unseenShown ? RowCode.None : _unseenHeard ? RowCode.HeardYou : RowCode.Quiet));

            var beats = _drafts.Select(d => new Beat(d.Kind, d.Actor, d.Creature, d.Ticks, d.Banner, d.Events.ToArray(), d.Row switch
            {
                RowOf.You => 0,
                RowOf.Predator => predatorRow.GetValueOrDefault(d.RowCreature, -1),
                RowOf.SmallGame => gameRow,
                RowOf.Unseen => unseenRow,
                _ => -1,
            })).ToArray();
            return new TurnScript(beats, rows);
        }
    }
}
