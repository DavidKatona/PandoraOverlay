using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The turn script as a whole (H1 to H3, H6, G9): the order of the beats, one actor
/// at a time with a Gap between, the rows and the beats' rows, what is and is not
/// shown of creatures the player does not know, the bounds on every beat (A35),
/// and that it is pure.
/// </summary>
public class TurnScriptTests
{
    // ---- the turn of the brief: you, two known predators, small game, time ---------

    // You walk from column 1 to column 2 of row 5. A Herrerasaurus (1) three cells
    // away at column 5 spots you, steps twice and bites; a Troodon (2) two away at
    // column 0 waits; small game (3) at column 5 of row 6, three away once you have
    // walked, flees one cell further (four away: it is known as its action began);
    // then time passes and you grow.
    private const int HerreraId = 1, TroodonId = 2, GameId = 3;
    private static readonly int GameFrom = Grid.Index(5, 6), GameTo = Grid.Index(6, 6);

    private static readonly Walked YourWalk = Walk(Row5(1), Row5(2));
    private static readonly Noticed Spots = new(HerreraId, Notice.Spotted);
    private static readonly Stepped Hunts = Step(HerreraId, Gait.Hunt, Row5(5), Row5(4), Row5(3));
    private static readonly Bit Bites = new(HerreraId, 12, false, false);
    private static readonly Stayed Waits = new(TroodonId, Idle.Waits);
    private static readonly Stepped Flees = Step(GameId, Gait.Flee, GameFrom, GameTo);
    private static readonly TimePassed Passes = Time();
    private static readonly Grew Grows = new(10, 11.4);

    private static (RunState Before, RunState After) Brief()
    {
        var before = Run(Row5(1),
            Predator(HerreraId, PredatorType.Herrerasaurus, Row5(5)),
            Predator(TroodonId, PredatorType.Troodon, Row5(0)),
            Game(GameId, GameFrom));
        var after = YouAt(before, Row5(2));
        after = With(after, HerreraId, Row5(3), alert: 4);
        after = With(after, GameId, GameTo);
        return (before, after);
    }

    private static TurnResult BriefTurn()
    {
        var (before, after) = Brief();
        return Turn(before, after, YourWalk, Spots, Hunts, Bites, Waits, Flees, Passes, Grows);
    }

    private static TurnScript BriefScript() => TurnScripts.Build(BriefTurn(), Sees);

    [Fact]
    public void TheBriefsSceneIsAsItsCommentSays()
    {
        Assert.Equal(3, Grid.Distance(Row5(2), Row5(5)));
        Assert.Equal(2, Grid.Distance(Row5(2), Row5(0)));
        Assert.Equal(3, Grid.Distance(Row5(2), GameFrom));
        Assert.Equal(4, Grid.Distance(Row5(2), GameTo));
    }

    [Fact]
    public void TheBriefsTurnPlaysInTheRulesOrderWithAGapBetweenEveryTwoActors()
    {
        var script = BriefScript();
        var expected = new (BeatKind Kind, BeatActor Actor, int Creature, int Ticks, string Banner, int Row)[]
        {
            (BeatKind.YouWalk, BeatActor.You, 0, 5, "You walk", 0),
            (BeatKind.Gap, BeatActor.World, 0, 3, "You walk", -1),
            (BeatKind.Focus, BeatActor.Creature, HerreraId, 3, "Herrerasaurus spots you", 1),
            (BeatKind.Notices, BeatActor.Creature, HerreraId, 5, "Herrerasaurus spots you", 1),
            (BeatKind.Steps, BeatActor.Creature, HerreraId, 8, "Herrerasaurus hunts you", 1),
            (BeatKind.Bites, BeatActor.Creature, HerreraId, 10, "Herrerasaurus bites you: 12", 1),
            (BeatKind.Gap, BeatActor.World, 0, 3, "Herrerasaurus bites you: 12", -1),
            (BeatKind.Focus, BeatActor.Creature, TroodonId, 3, "Troodon waits", 2),
            (BeatKind.Stays, BeatActor.Creature, TroodonId, 2, "Troodon waits", 2),
            (BeatKind.Gap, BeatActor.World, 0, 3, "Troodon waits", -1),
            (BeatKind.SmallGame, BeatActor.SmallGame, 0, 5, "Small game scatters", 3),
            (BeatKind.Gap, BeatActor.World, 0, 3, "Small game scatters", -1),
            (BeatKind.Unseen, BeatActor.Unseen, 0, 3, "Out of sight: quiet", 4),
            (BeatKind.TimePasses, BeatActor.World, 0, 4, "Time passes", -1),
        };
        Assert.Equal(expected, script.Beats.Select(b => (b.Kind, b.Actor, b.Creature, b.Ticks, b.Banner, b.RowIndex)).ToArray());
        Assert.Equal(expected.Sum(e => e.Ticks), script.TotalTicks);
        Assert.Equal(script.Beats.Sum(b => b.Ticks), script.TotalTicks);

        // Each beat shows its own events, in order.
        Assert.Equal(new TurnEvent[] { YourWalk }, script.Beats[0].Events);
        Assert.Equal(new TurnEvent[] { Spots }, script.Beats[3].Events);
        Assert.Equal(new TurnEvent[] { Hunts }, script.Beats[4].Events);
        Assert.Equal(new TurnEvent[] { Bites }, script.Beats[5].Events);
        Assert.Equal(new TurnEvent[] { Waits }, script.Beats[8].Events);
        Assert.Equal(new TurnEvent[] { Flees }, script.Beats[10].Events);
        Assert.Equal(new TurnEvent[] { Passes, Grows }, script.Beats[13].Events);
    }

    [Fact]
    public void TheBriefsTurnListsItsRowsInOrderWithTheirCodes()
    {
        var rows = BriefScript().Rows;
        Assert.Equal(new[] { RowActor.You, RowActor.Predator, RowActor.Predator, RowActor.SmallGame, RowActor.Unseen },
            rows.Select(r => r.Actor));
        Assert.Equal(new[] { "You", "Herrerasaurus", "Troodon", "Small game", "Out of sight" }, rows.Select(HatchlingText.RowName));
        Assert.Equal(new[] { "walked · noise 3", "spotted you · bit you: 12", "waited", "scattered", "quiet" }, rows.Select(HatchlingText.RowText));

        Assert.Equal((RowCode.Walked, RowCode.None, 3), (rows[0].First, rows[0].Second, rows[0].A));
        Assert.Equal((RowCode.Spotted, RowCode.Bit, 12), (rows[1].First, rows[1].Second, rows[1].A));
        // The alert is the predator's at the end: the list colours its name by it.
        Assert.Equal(4, rows[1].Alert);
        Assert.Equal(0, rows[2].Alert);
        Assert.Equal((RowCode.None, RowCode.Waited), (rows[2].First, rows[2].Second));
        Assert.Equal(RowCode.Scattered, rows[3].First);
        Assert.Equal(RowCode.Quiet, rows[4].First);
    }

    [Fact]
    public void EveryBeatsRowIsItsActorsRow()
    {
        var script = BriefScript();
        foreach (var beat in script.Beats)
        {
            switch (beat.Actor)
            {
                case BeatActor.You:
                    Assert.Equal(0, beat.RowIndex);
                    break;
                case BeatActor.Creature:
                    var row = script.Rows[beat.RowIndex];
                    Assert.Equal(RowActor.Predator, row.Actor);
                    var type = beat.Creature == HerreraId ? PredatorType.Herrerasaurus : PredatorType.Troodon;
                    Assert.Equal(type, row.Type);
                    break;
                case BeatActor.SmallGame:
                    Assert.Equal(RowActor.SmallGame, script.Rows[beat.RowIndex].Actor);
                    break;
                case BeatActor.Unseen:
                    Assert.Equal(script.Rows.Count - 1, beat.RowIndex);
                    Assert.Equal(RowActor.Unseen, script.Rows[beat.RowIndex].Actor);
                    break;
                default:
                    Assert.Equal(-1, beat.RowIndex);
                    break;
            }
        }
    }

    [Fact]
    public void ARowIsWrittenWhenItsLastBeatFinishes()
    {
        var script = BriefScript();
        // The Herrerasaurus' row: its Focus, notice and step are not its last; its bite is.
        Assert.False(script.IsLastOfRow(2));
        Assert.False(script.IsLastOfRow(4));
        Assert.True(script.IsLastOfRow(5));
        Assert.True(script.IsLastOfRow(0));
        Assert.False(script.IsLastOfRow(1));    // a Gap is no row's
        Assert.False(script.IsLastOfRow(13));   // nor is time
    }

    // ---- G9's requirements, held on the brief's turn -----------------------------------

    [Fact]
    public void ItIsAlwaysPlainWhoseTurnItIs()
    {
        // Every beat has a banner; every creature's beat names it; its Focus marks its row.
        var script = BriefScript();
        Assert.All(script.Beats, b => Assert.False(string.IsNullOrEmpty(b.Banner)));
        foreach (var beat in script.Beats.Where(b => b.Actor == BeatActor.Creature))
            Assert.Contains(HatchlingText.RowName(script.Rows[beat.RowIndex]), beat.Banner);
    }

    [Fact]
    public void YourActionComesFirstAndAlone()
    {
        var script = BriefScript();
        var firstGap = script.Beats.ToList().FindIndex(b => b.Kind == BeatKind.Gap);
        Assert.True(firstGap > 0);
        Assert.All(script.Beats.Take(firstGap), b => Assert.Equal(BeatActor.You, b.Actor));
        Assert.DoesNotContain(script.Beats.Skip(firstGap), b => b.Kind is BeatKind.YouWalk or BeatKind.YouStrike);
    }

    [Fact]
    public void TwoActorsAreNeverBackToBack()
    {
        foreach (var script in new[] { BriefScript(), Worst().Script })
        {
            var beats = script.Beats;
            // Whose action a beat is part of is its row (you, a predator, small game,
            // out of sight). A beat of one row right after a beat of another, with
            // no Gap between, would be two actors back to back. Time's beats and
            // the end belong to no row.
            for (var i = 1; i < beats.Count; i++)
            {
                var (a, b) = (beats[i - 1].RowIndex, beats[i].RowIndex);
                Assert.False(a >= 0 && b >= 0 && a != b, $"beats {i - 1} and {i}: rows {a} then {b} with no Gap");
            }
            Assert.NotEqual(BeatKind.Gap, beats[0].Kind);
            Assert.NotEqual(BeatKind.Gap, beats[^1].Kind);
            for (var i = 1; i < beats.Count; i++)
                Assert.False(beats[i - 1].Kind == BeatKind.Gap && beats[i].Kind == BeatKind.Gap, $"two Gaps at {i}");
        }
    }

    [Fact]
    public void TheListIsTheOrder()
    {
        // Rows above the marker have acted, rows below have not: until the world's
        // turn, the rows the beats mark only go down the list.
        var script = BriefScript();
        var marked = script.Beats.TakeWhile(b => b.Kind != BeatKind.TimePasses).Where(b => b.RowIndex >= 0).Select(b => b.RowIndex).ToList();
        Assert.Equal(marked.OrderBy(r => r), marked);
        Assert.Equal(Enumerable.Range(0, script.Rows.Count), marked.Distinct());
    }

    [Fact]
    public void ThePredatorsActInIdOrderWhateverTheirDistance()
    {
        // The far one has the lower id: it still acts first (H1, nearest-first was turned down).
        var before = Run(Row5(2),
            Predator(1, PredatorType.Carnotaurus, Row5(5)),
            Predator(2, PredatorType.Troodon, Row5(3)));
        var script = Build(before, before, Rest(), new Stayed(1, Idle.Waits), new Stayed(2, Idle.Waits), Time());
        var order = script.Beats.Where(b => b.Kind == BeatKind.Focus).Select(b => b.Creature).ToArray();
        Assert.Equal(new[] { 1, 2 }, order);
        Assert.Equal(new[] { "Carnotaurus", "Troodon" }, script.Rows.Where(r => r.Actor == RowActor.Predator).Select(HatchlingText.RowName));
    }

    // ---- what is not known (H3) ---------------------------------------------------------

    private static (RunState Before, RunState After, TurnEvent[] Events) OneUnknownHeard(bool two)
    {
        var before = Run(Row5(2),
            Predator(1, PredatorType.Troodon, Row5(4)),
            Predator(4, PredatorType.Allosaurus, Grid.Index(10, 0)));
        if (two) before.Creatures.Add(Predator(6, PredatorType.Allosaurus, Grid.Index(0, 0), ordinal: 2));
        var after = With(before, 4, Grid.Index(9, 0), alert: 3);
        if (two) after = With(after, 6, Grid.Index(1, 0), alert: 3);
        var events = new List<TurnEvent> { Shared.Rest, Shared.TroodonWaits, new Noticed(4, Notice.Heard), Step(4, Gait.Search, Grid.Index(10, 0), Grid.Index(9, 0)) };
        if (two) events.AddRange(new TurnEvent[] { new Noticed(6, Notice.Heard), Step(6, Gait.Search, Grid.Index(0, 0), Grid.Index(1, 0)) });
        events.Add(Shared.Time);
        return (before, after, events.ToArray());
    }

    // The events both scripts share are the same objects, as one turn's would be.
    private static class Shared
    {
        public static readonly Rested Rest = Scenes.Rest();
        public static readonly Stayed TroodonWaits = new(1, Idle.Waits);
        public static readonly TimePassed Time = Scenes.Time();
    }

    [Fact]
    public void APredatorNotKnownMakesNoBeatNoRowAndNoWord()
    {
        var (before, after, events) = OneUnknownHeard(two: false);
        var script = Build(before, after, events);
        Assert.DoesNotContain(script.Beats, b => b.Creature == 4);
        Assert.DoesNotContain(script.Beats, b => b.Events.OfType<Noticed>().Any(n => n.Creature == 4) || b.Events.OfType<Stepped>().Any(s => s.Creature == 4));
        Assert.DoesNotContain(script.Rows, r => r.Actor == RowActor.Predator && r.Type == PredatorType.Allosaurus);
        Assert.DoesNotContain(AllWords(script), w => w.Contains("Allosaurus"));

        // Its hearing you is the one Out of sight line, and nothing more.
        Assert.Equal("Something heard you", One(script, BeatKind.Unseen).Banner);
        Assert.Empty(One(script, BeatKind.Unseen).Events);
        Assert.Equal(RowCode.HeardYou, script.Rows[^1].First);
        Assert.Equal("something heard you", HatchlingText.RowText(script.Rows[^1]));
    }

    [Fact]
    public void OneUnknownPredatorHearingYouReadsExactlyAsTwo()
    {
        var (b1, a1, e1) = OneUnknownHeard(two: false);
        var (b2, a2, e2) = OneUnknownHeard(two: true);
        Assert.Equal(Build(b1, a1, e1), Build(b2, a2, e2));
    }

    [Fact]
    public void AnUnknownPredatorThatSpottedYouCountsAsHearingYou()
    {
        var before = Run(Row5(2), Predator(4, PredatorType.Allosaurus, Grid.Index(10, 0)));
        var script = Build(before, before, Rest(), new Noticed(4, Notice.Spotted), new Stayed(4, Idle.Waits), Time());
        Assert.Equal("Something heard you", One(script, BeatKind.Unseen).Banner);
    }

    [Fact]
    public void UnknownSmallGameMakesNoBeat()
    {
        var before = Run(Row5(2), Game(5, Grid.Index(10, 0)));
        var script = Build(before, With(before, 5, Grid.Index(9, 0)), Rest(), Step(5, Gait.Roam, Grid.Index(10, 0), Grid.Index(9, 0)), Time());
        Assert.DoesNotContain(script.Beats, b => b.Kind == BeatKind.SmallGame);
        Assert.DoesNotContain(script.Rows, r => r.Actor == RowActor.SmallGame);
        Assert.Equal("Out of sight: quiet", One(script, BeatKind.Unseen).Banner);
    }

    [Fact]
    public void KnownSmallGameThatStaysStillHasARowAndNoBeat()
    {
        var before = Run(Row5(2), Game(5, Row5(4)));
        var script = Build(before, before, Rest(), Time());
        Assert.DoesNotContain(script.Beats, b => b.Kind == BeatKind.SmallGame);
        Assert.Equal("still", HatchlingText.RowText(Assert.Single(script.Rows, r => r.Actor == RowActor.SmallGame)));
    }

    [Fact]
    public void APredatorKnownBeforeAndNotAfterIsSeenLeaving()
    {
        // Three away as its action begins, four once it has wandered.
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(5)));
        var script = Build(before, With(before, 1, Row5(6)), Rest(), Step(1, Gait.Wander, Row5(5), Row5(6)), Time());
        Assert.Equal("Troodon wanders", One(script, BeatKind.Steps).Banner);
        var row = Assert.Single(script.Rows, r => r.Actor == RowActor.Predator);
        Assert.Equal("wandered", HatchlingText.RowText(row));
    }

    [Fact]
    public void APredatorKnownOnlyAfterIsSeenComing()
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(6)));
        var script = Build(before, With(before, 1, Row5(5), alert: 2), Rest(), Step(1, Gait.Search, Row5(6), Row5(5)), Time());
        Assert.Equal("Troodon searches", One(script, BeatKind.Steps).Banner);
        Assert.Single(script.Rows, r => r.Actor == RowActor.Predator);
    }

    [Fact]
    public void WhatIsKnownIsAskedWhereYouStandAfterYourMove()
    {
        // Known before your move (three away), not after it (four), and it does not move.
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(5)));
        var script = Build(before, YouAt(before, Row5(1)), Walk(Row5(2), Row5(1)), new Stayed(1, Idle.Waits), Time());
        Assert.DoesNotContain(script.Beats, b => b.Creature == 1);
        Assert.DoesNotContain(script.Rows, r => r.Actor == RowActor.Predator);
    }

    [Fact]
    public void WhatIsKnownIsAskedWhereItStoodAsItsActionBegan()
    {
        // Two predators: the first steps away first; the second is asked about with
        // the first already moved (the scene the Known sees is the replayed one).
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(5)), Predator(2, PredatorType.Troodon, Row5(9), ordinal: 2));
        var seen = new List<(int Id, int At, int YouAt)>();
        Known spy = (run, c) =>
        {
            seen.Add((c.Id, c.At, run.You.At));
            return Sees(run, c);
        };
        var after = With(With(YouAt(before, Row5(3)), 1, Row5(6)), 2, Row5(8));
        TurnScripts.Build(Turn(before, after, Walk(Row5(2), Row5(3)), Step(1, Gait.Wander, Row5(5), Row5(6)), Step(2, Gait.Wander, Row5(9), Row5(8)), Time()), spy);
        // As each action began: you at column 3, the first at column 5, the second at 9.
        Assert.Contains((1, Row5(5), Row5(3)), seen);
        Assert.Contains((2, Row5(9), Row5(3)), seen);
        // The two states themselves are untouched.
        Assert.Equal(Row5(5), before.Creatures[0].At);
        Assert.Equal(Row5(2), before.You.At);
    }

    // ---- the apex (H6) ------------------------------------------------------------------

    [Fact]
    public void TheApexHasBeatsFromAfar()
    {
        var far = Grid.Index(10, 0);
        var before = Run(Row5(2), Predator(9, PredatorType.Tyrannosaurus, far, alert: 4));
        var step = Grid.Index(9, 1);
        var script = Build(before, With(before, 9, step), Rest(), Step(9, Gait.Hunt, far, step), Time());
        Assert.Equal("Tyrannosaurus hunts you", One(script, BeatKind.Steps).Banner);
        var row = Assert.Single(script.Rows, r => r.Actor == RowActor.Predator);
        Assert.Equal((PredatorType.Tyrannosaurus, 4), (row.Type, row.Alert));
    }

    [Fact]
    public void TheApexArrivingIsOneBeatAndARowThatSaysArrived()
    {
        var far = Grid.Index(10, 0);
        var before = Run(Row5(2));
        var after = before.Clone();
        after.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, far, alert: 4));
        // If the rules also list it as an arrival, it is still the one Apex beat.
        var script = Build(before, after, Rest(), Time(), new Arrived(9, far, true), new ApexArrived(9, far), new ApexWaits(30));
        Assert.Single(script.Beats, b => b.Creature == 9);
        Assert.DoesNotContain(script.Beats, b => b.Kind == BeatKind.Arrives);
        Assert.Equal("arrived", HatchlingText.RowText(Assert.Single(script.Rows, r => r.Actor == RowActor.Predator)));
    }

    [Fact]
    public void ArrivalsOutOfSightAreNotAnnounced()
    {
        var before = Run(Row5(2));
        var after = before.Clone();
        after.Creatures.Add(Predator(7, PredatorType.Carnotaurus, Grid.Index(10, 0)));
        after.Creatures.Add(Game(8, Row5(4)));
        var script = Build(before, after, Rest(), Time(), new Arrived(7, Grid.Index(10, 0), false), new Arrived(8, Row5(4), false));
        Assert.DoesNotContain(script.Beats, b => b.Kind == BeatKind.Arrives);
        Assert.DoesNotContain(script.Rows, r => r.Actor == RowActor.Predator);
        // The small game is simply there: known at the end, it has its row, still.
        Assert.Equal("still", HatchlingText.RowText(Assert.Single(script.Rows, r => r.Actor == RowActor.SmallGame)));
    }

    [Fact]
    public void TwoArrivalsInATurnHaveAGapBetween()
    {
        var before = Run(Row5(2));
        var after = before.Clone();
        after.Creatures.Add(Predator(7, PredatorType.Carnotaurus, Row5(5)));
        after.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4));
        var script = Build(before, after, Rest(), Time(), new Arrived(7, Row5(5), false), new ApexArrived(9, Grid.Index(10, 0)));
        var kinds = script.Beats.Select(b => b.Kind).ToList();
        var arrives = kinds.IndexOf(BeatKind.Arrives);
        Assert.Equal(BeatKind.Gap, kinds[arrives + 1]);
        Assert.Equal(BeatKind.Apex, kinds[arrives + 2]);
    }

    // ---- the rows' rules (plan 3.4) -----------------------------------------------------

    private static TurnRow PredatorRowOf(params TurnEvent[] action)
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Carnotaurus, Row5(4), ordinal: 2));
        var script = Build(before, before, new TurnEvent[] { Rest() }.Concat(action).Append(Time()).ToArray());
        return Assert.Single(script.Rows, r => r.Actor == RowActor.Predator);
    }

    [Fact]
    public void ARowSaysWhatItNoticedInFrontWhenBothAreWorthSaying()
    {
        Assert.Equal("spotted you · bit you: 12", HatchlingText.RowText(PredatorRowOf(
            new Noticed(1, Notice.Spotted), Step(1, Gait.Hunt, Row5(4), Row5(3)), new Bit(1, 12, false, false))));
        Assert.Equal("spotted you · missed you", HatchlingText.RowText(PredatorRowOf(
            new Noticed(1, Notice.Spotted), Step(1, Gait.Hunt, Row5(4), Row5(3)), new Bit(1, 0, true, false))));
        Assert.Equal("heard you · waited", HatchlingText.RowText(PredatorRowOf(
            new Noticed(1, Notice.Heard), new Stayed(1, Idle.Waits))));
    }

    [Fact]
    public void SpottedThenHuntingIsSaidAsSpotted()
    {
        Assert.Equal("spotted you", HatchlingText.RowText(PredatorRowOf(
            new Noticed(1, Notice.Spotted), Step(1, Gait.Hunt, Row5(4), Row5(5)))));
    }

    [Fact]
    public void HeardThenSearchingIsSaidAsHeard()
    {
        Assert.Equal("heard you", HatchlingText.RowText(PredatorRowOf(
            new Noticed(1, Notice.Heard), Step(1, Gait.Search, Row5(4), Row5(5)))));
    }

    [Fact]
    public void LostYourTrailStandsAlone()
    {
        Assert.Equal("lost your trail", HatchlingText.RowText(PredatorRowOf(
            Step(1, Gait.Search, Row5(4), Row5(5)), new Noticed(1, Notice.LostTrail))));
    }

    [Fact]
    public void ARowSaysOneThingItDid()
    {
        Assert.Equal("hunting you", HatchlingText.RowText(PredatorRowOf(Step(1, Gait.Hunt, Row5(4), Row5(3)))));
        Assert.Equal("dazed", HatchlingText.RowText(PredatorRowOf(new Stayed(1, Idle.Dazed))));
        Assert.Equal("held", HatchlingText.RowText(PredatorRowOf(new Stayed(1, Idle.Held))));
        Assert.Equal("bit you: 7", HatchlingText.RowText(PredatorRowOf(new Stayed(1, Idle.Waits), new Bit(1, 7, false, false))));

        // Defeated by your spikes or its bleeding: that is what is said of it.
        var before = Run(Row5(2), Predator(1, PredatorType.Carnotaurus, Row5(3), ordinal: 2));
        var script = Build(before, Without(before, 1), Rest(),
            new Stayed(1, Idle.Waits), new Bit(1, 7, false, false), new Hurt(1, Row5(3), 3, HurtBy.Spikes, true), Time());
        Assert.Equal("defeated", HatchlingText.RowText(Assert.Single(script.Rows, r => r.Actor == RowActor.Predator)));
    }

    [Fact]
    public void YourRowSaysYourMove()
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Carnotaurus, Row5(3), ordinal: 2), Game(5, Grid.Index(2, 4)));
        string You(RunState after, params TurnEvent[] move) =>
            HatchlingText.RowText(Build(before, after, move.Append(Time()).ToArray()).Rows[0]);

        Assert.Equal("walked · noise 3", You(YouAt(before, Row5(1)), Walk(Row5(2), Row5(1))));
        Assert.Equal("sneaked · noise 1", You(YouAt(before, Row5(1)), Sneak(Row5(2), Row5(1))));
        Assert.Equal("sprinted 2 · noise 5", You(YouAt(before, Row5(0)), Sprint(Row5(2), Row5(1), Row5(0))));
        Assert.Equal("ate meat · +45", You(before, new Ate(Food.Meat, 45, 1)));
        Assert.Equal("drank · +40", You(before, new Drank(40, 1)));
        Assert.Equal("rested · +8%", You(before, Rest()));
        Assert.Equal("hit the Carnotaurus 2: 7", You(before, new Struck(1, Row5(3), 7, false, false, false, false, 4)));
        Assert.Equal("defeated the Carnotaurus 2", You(Without(before, 1), new Struck(1, Row5(3), 70, false, false, false, true, 4)));
        Assert.Equal("caught small game", You(Without(before, 5), new Struck(5, Grid.Index(2, 4), 9, false, false, false, true, 4)));
    }

    [Fact]
    public void APredatorYouDefeatedLeavesTheList()
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(3)));
        var script = Build(before, Without(before, 1), new Struck(1, Row5(3), 30, false, false, false, true, 4), Time());
        Assert.DoesNotContain(script.Rows, r => r.Actor == RowActor.Predator);
        Assert.Equal("defeated the Troodon", HatchlingText.RowText(script.Rows[0]));
    }

    // ---- ends ---------------------------------------------------------------------------

    [Fact]
    public void ARunCaughtEndsInTheActionThatCaughtYou()
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Carnotaurus, Row5(3), ordinal: 2), Predator(2, PredatorType.Troodon, Row5(4)), Game(5, Row5(5)));
        var script = Build(before, before, Rest(), new Stayed(1, Idle.Waits), new Bit(1, 99, false, false), new RunEnded(false, EndReason.Caught, 1));
        Assert.Equal(new[] { BeatKind.YouRest, BeatKind.Gap, BeatKind.Focus, BeatKind.Stays, BeatKind.Bites, BeatKind.Gap, BeatKind.End },
            script.Beats.Select(b => b.Kind));
        // Nothing out of sight acted: no such beat, and that row, the small game's
        // and the Troodon's, who never got to act, say nothing.
        Assert.Equal("", HatchlingText.RowText(script.Rows[^1]));
        Assert.Equal("", HatchlingText.RowText(Assert.Single(script.Rows, r => r.Actor == RowActor.SmallGame)));
        Assert.Equal("", HatchlingText.RowText(Assert.Single(script.Rows, r => r.Type == PredatorType.Troodon && r.Actor == RowActor.Predator)));
    }

    [Fact]
    public void SecondWindInABiteBelongsToTheBitersRowAndInTimeToNone()
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(3)));
        var bite = Build(before, before, Rest(), new Stayed(1, Idle.Waits), new Bit(1, 40, false, false), new SecondWind(), Time());
        Assert.Equal(1, One(bite, BeatKind.SecondWind).RowIndex);
        var hunger = Build(before, before, Rest(), new Stayed(1, Idle.Waits), new TimePassed(5, 0, 0), new SecondWind());
        Assert.Equal(-1, One(hunger, BeatKind.SecondWind).RowIndex);
    }

    // ---- bounds (A35) -------------------------------------------------------------------

    [Fact]
    public void NoBeatIsZeroTicksOrLongerThanTheMost()
    {
        var before = Run(Row5(2), Predator(1, PredatorType.Carnotaurus, Row5(4)));
        // A walk with no cell and a step of thirty cells: not what the rules make,
        // but the script is total, and the bounds hold anyway.
        var odd = Build(before, before, new Walked(Pace.Walk, Row5(2), Array.Empty<int>(), false, 3),
            Step(1, Gait.Hunt, Row5(4), Enumerable.Repeat(Row5(4), 30).ToArray()), Time());
        Assert.Equal(1, One(odd, BeatKind.YouWalk).Ticks);
        Assert.Equal(TurnScript.MaxBeatTicks, One(odd, BeatKind.Steps).Ticks);
        AssertBounded(odd);
        AssertBounded(BriefScript());
    }

    private static void AssertBounded(TurnScript script)
    {
        Assert.All(script.Beats, b => Assert.InRange(b.Ticks, 1, TurnScript.MaxBeatTicks));
        Assert.Equal(script.Beats.Sum(b => b.Ticks), script.TotalTicks);
    }

    /// <summary>
    /// The longest turn the rules can produce (plan 3.8): danger 5, the apex and
    /// seven predators known, each noticing, stepping twice, biting, hurt by spikes
    /// and bleeding; a sprint of five cells; a stage reached; the run ending.
    /// </summary>
    private static (TurnScript Script, int Predators) Worst()
    {
        var you = Grid.Index(5, 5);
        var five = new[] { Grid.Index(5, 4), Grid.Index(5, 3), Grid.Index(5, 2), Grid.Index(5, 1), Grid.Index(5, 0) };
        var end = five[^1];
        var creatures = new List<Creature>();
        var events = new List<TurnEvent> { Sprint(you, five) };
        for (var id = 1; id <= 8; id++)
        {
            var type = id == 8 ? PredatorType.Tyrannosaurus : PredatorType.Carnotaurus;
            var from = Grid.Index(id, 2);
            creatures.Add(Predator(id, type, from, ordinal: id == 8 ? 1 : id, alert: 4));
            events.Add(new Noticed(id, Notice.Spotted));
            events.Add(Step(id, Gait.Hunt, from, Grid.Index(id, 1), Grid.Index(id - 1, 1)));
            events.Add(new Bit(id, 9, false, false));
            events.Add(new Hurt(id, Grid.Index(id - 1, 1), 3, HurtBy.Spikes, false));
            events.Add(new Hurt(id, Grid.Index(id - 1, 1), 2, HurtBy.Bleed, false));
        }
        events.Add(new TimePassed(5, 5, 0));
        events.Add(new Grew(98.8, 100));
        events.Add(new StageReached(4));
        events.Add(new RunEnded(false, EndReason.Hunger, 0));
        var before = Run(you, creatures.ToArray());
        before.Danger = 5;
        var after = YouAt(before, end);
        // Everyone is known throughout: a Known that says so, as Wary and the apex can.
        return (TurnScripts.Build(Turn(before, after, events.ToArray()), (_, _) => true), 8);
    }

    [Fact]
    public void TheWorstTurnStaysUnderTheMostBeatByBeat()
    {
        var (script, predators) = Worst();
        AssertBounded(script);
        Assert.Equal(15, One(script, BeatKind.YouWalk).Ticks);
        Assert.Equal(predators, script.Beats.Count(b => b.Kind == BeatKind.Focus));
        Assert.All(script.Beats.Where(b => b.Kind == BeatKind.Steps), b => Assert.Equal(8, b.Ticks));
        // Far under the engine's limit for one queued step, beat by beat.
        Assert.True(script.Beats.Max(b => b.Ticks) < 300);
        Assert.Equal(new[] { BeatKind.Stage, BeatKind.End }, script.Beats.TakeLast(2).Select(b => b.Kind));
    }

    // ---- pure and total ------------------------------------------------------------------

    [Fact]
    public void BuildTwiceGivesEqualScripts()
    {
        var turn = BriefTurn();
        var (before, after) = (turn.Before, turn.After);
        var first = TurnScripts.Build(turn, Sees);
        var second = TurnScripts.Build(turn, Sees);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, Build(before, after, YourWalk, Passes));

        // The states are as they were.
        Assert.Equal(Row5(1), before.You.At);
        Assert.Equal(Row5(5), before.Creatures.Single(c => c.Id == HerreraId).At);
        Assert.Equal(Row5(3), after.Creatures.Single(c => c.Id == HerreraId).At);
    }

    [Fact]
    public void EveryEventOfTheGameIsReadByTheScript()
    {
        var kinds = typeof(TurnEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TurnEvent)) && !t.IsAbstract)
            .ToList();
        Assert.NotEmpty(kinds);
        Assert.All(kinds, k => Assert.Contains(k, TurnScripts.Reads));
    }

    [Fact]
    public void AnEventOfEveryKindInOneTurnGivesAScript()
    {
        // Every kind at least once, in the rules' order, with creatures the turn holds
        // and one it does not (id 99): nothing throws, and the bounds hold.
        var before = Run(Row5(2), Predator(1, PredatorType.Troodon, Row5(3)), Game(5, Row5(4)));
        var after = YouAt(before, Row5(1));
        after.Creatures.Add(Predator(7, PredatorType.Herrerasaurus, Row5(5)));
        after.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4));
        var script = Build(before, after,
            Walk(Row5(2), Row5(1)), new Ate(Food.Fish, 25, 2), new Drank(40, 1), new Rested(8, 0),
            new Struck(1, Row5(3), 3, true, true, false, false, 4), new Afflicted(1, Affliction.Bleeding),
            new Struck(99, Row5(3), 3, false, false, true, false, 4),
            new Noticed(1, Notice.Heard), Step(1, Gait.Search, Row5(3), Row5(2)), new Stayed(1, Idle.Held),
            new Bit(1, 5, false, true), new Hurt(1, Row5(2), 1, HurtBy.Spikes, false), new SecondWind(),
            new Hurt(1, Row5(2), 1, HurtBy.Bleed, false), new Noticed(99, Notice.Heard),
            Step(5, Gait.Flee, Row5(4), Row5(5)),
            new TimePassed(1, 1, 0), new SecondWind(), new Grew(24, 25.4), new StageReached(1), new StageReached(7),
            new Arrived(7, Row5(5), true), new PlantsGrew(new[] { 0, 1 }), new ApexArrived(9, Grid.Index(10, 0)),
            new ApexWaits(30), new InstinctsOffered(new[] { Instinct.Eyes }), new RunEnded(true, EndReason.Outlasted, 0));
        AssertBounded(script);
        Assert.Contains(script.Beats, b => b.Kind == BeatKind.End);
        Assert.All(script.Beats, b => Assert.True(b.RowIndex < script.Rows.Count));
    }

    // ---- turn 0 --------------------------------------------------------------------------

    [Fact]
    public void RowsAtStartSayHatchedTheKnownPredatorsWithNothingAndQuiet()
    {
        var run = Run(Row5(2),
            Predator(1, PredatorType.Troodon, Row5(5), alert: 0),
            Predator(2, PredatorType.Herrerasaurus, Grid.Index(10, 0)),
            Game(3, Row5(4)));
        run.Turn = 0;
        var rows = TurnScripts.RowsAtStart(run, Sees);
        Assert.Equal(new[] { "You", "Troodon", "Small game", "Out of sight" }, rows.Select(HatchlingText.RowName));
        Assert.Equal(new[] { "hatched", "", "", "quiet" }, rows.Select(HatchlingText.RowText));

        var alone = Run(Row5(2));
        Assert.Equal(new[] { RowActor.You, RowActor.Unseen }, TurnScripts.RowsAtStart(alone, Sees).Select(r => r.Actor));
    }
}
