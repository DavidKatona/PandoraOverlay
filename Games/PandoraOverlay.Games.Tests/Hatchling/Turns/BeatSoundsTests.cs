using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// When a beat sounds (H96, H97, H98): <c>PLAN-SOUND.md</c> 3.2's table beat kind by
/// beat kind on hand-built turns, every cue's tick under its beat's ticks for the 200
/// seeded turns and the longest turn, the silent beats silent, a hunter out of sight
/// unheard, and the skip's one cue by its priority.
/// </summary>
public class BeatSoundsTests
{
    private readonly ITestOutputHelper _out;

    public BeatSoundsTests(ITestOutputHelper output) => _out = output;

    // You on row 5 at column 2; a Troodon beside you, "Carnotaurus 2" two cells away,
    // small game three away: all known. An Allosaurus eight away: not known.
    private const int TroodonId = 1, CarnoId = 2, AlloId = 4, GameId = 5;
    private static readonly int Here = Row5(2);

    private static RunState Island() => Run(Here,
        Predator(TroodonId, PredatorType.Troodon, Row5(3)),
        Predator(CarnoId, PredatorType.Carnotaurus, Row5(4), ordinal: 2),
        Predator(AlloId, PredatorType.Allosaurus, Row5(10)),
        Game(GameId, Row5(5)));

    /// <summary>A turn in which you rest, then <paramref name="middle"/>, then time passes; nothing moves.</summary>
    private static TurnScript Rested(params TurnEvent[] middle)
    {
        var island = Island();
        return Build(island, island, new TurnEvent[] { Rest() }.Concat(middle).Append(Time()).ToArray());
    }

    private static IReadOnlyList<BeatSound> Of(Beat beat, bool apex = false) => BeatSounds.Of(beat, apex);

    private static BeatSound[] Pairs(params (int Tick, Cue Cue)[] pairs) => pairs.Select(p => new BeatSound(p.Tick, p.Cue)).ToArray();

    // ---- your beats -------------------------------------------------------------------

    [Fact]
    public void YourWalkSoundsEveryCellInItsPace()
    {
        var empty = Run(Here);
        var path = new[] { Row5(3), Row5(4) };
        var walk = One(Build(empty, YouAt(empty, Row5(3)), Walk(Here, Row5(3)), Time()), BeatKind.YouWalk);
        Assert.Equal(Pairs((0, Cue.Step)), Of(walk));
        var sneak = One(Build(empty, YouAt(empty, Row5(3)), Sneak(Here, Row5(3)), Time()), BeatKind.YouWalk);
        Assert.Equal(Pairs((0, Cue.StepSneak)), Of(sneak));
        var sprint = One(Build(empty, YouAt(empty, Row5(4)), Sprint(Here, path), Time()), BeatKind.YouWalk);
        Assert.Equal(Pairs((0, Cue.StepSprint), (BeatTicks.SprintPerCell, Cue.StepSprint)), Of(sprint));
        // Each cell at c times the pace's ticks a cell (10, 12, 6): where the board starts it.
        var walked = One(Build(empty, YouAt(empty, Row5(5)), Walk(Here, Row5(3), Row5(4), Row5(5)), Time()), BeatKind.YouWalk);
        Assert.Equal(Pairs((0, Cue.Step), (10, Cue.Step), (20, Cue.Step)), Of(walked));
        var sneaked = One(Build(empty, YouAt(empty, Row5(4)), Sneak(Here, path), Time()), BeatKind.YouWalk);
        Assert.Equal(Pairs((0, Cue.StepSneak), (12, Cue.StepSneak)), Of(sneaked));
    }

    [Fact]
    public void YouEatDrinkAndRestOnTheirFirstTick()
    {
        var island = Island();
        Assert.Equal(Pairs((0, Cue.Rest)), Of(One(Rested(), BeatKind.YouRest)));
        Assert.Equal(Pairs((0, Cue.Eat)), Of(One(Build(island, island, new Ate(Food.Plants, 30, 2), Time()), BeatKind.YouEat)));
        Assert.Equal(Pairs((0, Cue.Drink)), Of(One(Build(island, island, new Drank(40, 2), Time()), BeatKind.YouDrink)));
    }

    [Fact]
    public void YourStrikeSoundsOnTheBlowAndADefeatWhereItsFadeBegins()
    {
        var island = Island();
        var hit = One(Build(island, island, new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4), Time()), BeatKind.YouStrike);
        Assert.Equal(Pairs((BeatStep.WindUpTicks, Cue.Hit)), Of(hit));
        Assert.Equal(6, BeatStep.WindUpTicks);

        var defeated = One(Build(island, Without(island, TroodonId), new Struck(TroodonId, Row5(3), 30, false, false, false, true, 4), Time()), BeatKind.YouStrike);
        Assert.Equal(Pairs((6, Cue.Hit), (BeatStep.StrikeFadeFrom, Cue.Defeat)), Of(defeated));
        Assert.Equal(18, BeatStep.StrikeFadeFrom);
        Assert.Equal(BeatTicks.Strike, BeatStep.StrikeFadeFrom);
    }

    [Fact]
    public void EachSweptOneSoundsAtOnceAndFallsInItsLastThird()
    {
        var island = Island();
        var script = Build(island, Without(island, CarnoId),
            new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4),
            new Struck(CarnoId, Row5(4), 7, false, false, true, true, 4), Time());
        var swept = One(script, BeatKind.AlsoStruck);
        Assert.Equal(Pairs((0, Cue.Hit), (BeatStep.FadeFrom(swept.Ticks), Cue.Defeat)), Of(swept));
        Assert.Equal(8, BeatStep.FadeFrom(BeatTicks.AlsoStruck));

        var standing = One(Build(island, island,
            new Struck(TroodonId, Row5(3), 7, false, false, false, false, 4),
            new Struck(CarnoId, Row5(4), 7, false, false, true, false, 4), Time()), BeatKind.AlsoStruck);
        Assert.Equal(Pairs((0, Cue.Hit)), Of(standing));
    }

    // ---- a hunter's beats ---------------------------------------------------------------

    [Fact]
    public void AFocusPingsAndTheApexsIsItsOwn()
    {
        var focus = Rested(new Stayed(CarnoId, Idle.Waits)).Beats.First(b => b.Kind == BeatKind.Focus);
        Assert.Equal(Pairs((0, Cue.Focus)), Of(focus, apex: false));
        Assert.Equal(Pairs((0, Cue.FocusApex)), Of(focus, apex: true));
    }

    [Theory]
    [InlineData((int)Notice.Spotted, (int)Cue.Spotted)]
    [InlineData((int)Notice.Heard, (int)Cue.Heard)]
    [InlineData((int)Notice.LostTrail, (int)Cue.LostTrail)]
    public void ANoticeSoundsItsKind(int kind, int cue)
    {
        var script = Rested(new Noticed(CarnoId, (Notice)kind), new Stayed(CarnoId, Idle.Waits));
        Assert.Equal(Pairs((0, (Cue)cue)), Of(One(script, BeatKind.Notices)));
    }

    [Fact]
    public void AHuntersStepSoundsEveryCellAtEightTicks()
    {
        var one = One(Rested(Step(CarnoId, Gait.Hunt, Row5(4), Row5(5))), BeatKind.Steps);
        Assert.Equal(Pairs((0, Cue.HunterStep)), Of(one));
        var two = One(Rested(Step(CarnoId, Gait.Search, Row5(4), Row5(5), Row5(6))), BeatKind.Steps);
        Assert.Equal(Pairs((0, Cue.HunterStep), (BeatTicks.StepPerCell, Cue.HunterStep)), Of(two));
        Assert.Equal(8, BeatTicks.StepPerCell);
    }

    [Fact]
    public void ABiteAndADodgeSoundOnTheBlow()
    {
        var bite = One(Rested(new Stayed(TroodonId, Idle.Waits), new Bit(TroodonId, 12, false, false)), BeatKind.Bites);
        Assert.Equal(Pairs((BeatStep.WindUpTicks, Cue.Bite)), Of(bite));
        var dodge = One(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 0, true, false)), BeatKind.Dodged);
        Assert.Equal(Pairs((BeatStep.WindUpTicks, Cue.Dodge)), Of(dodge));
    }

    [Fact]
    public void AHurtStingsAndAFallSoundsWhereItsFadeBegins()
    {
        var spikes = One(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 12, false, false), new Hurt(CarnoId, Row5(4), 4, HurtBy.Spikes, false)), BeatKind.IsHurt);
        Assert.Equal(Pairs((0, Cue.Sting)), Of(spikes));
        var island = Island();
        var bled = One(Build(island, Without(island, CarnoId), Rest(),
            new Stayed(CarnoId, Idle.Waits), new Hurt(CarnoId, Row5(4), 5, HurtBy.Bleed, true), Time()), BeatKind.IsHurt);
        Assert.Equal(Pairs((0, Cue.Sting), (BeatStep.FadeFrom(bled.Ticks), Cue.Defeat)), Of(bled));
        Assert.Equal(8, BeatStep.FadeFrom(BeatTicks.Hurt));
    }

    [Fact]
    public void SecondWindArrivalsAndTheWorldSoundOnTheirFirstTick()
    {
        var wind = Rested(new Stayed(TroodonId, Idle.Waits), new Bit(TroodonId, 40, false, false), new SecondWind());
        Assert.Equal(Pairs((0, Cue.SecondWind)), Of(One(wind, BeatKind.SecondWind)));

        var island = Island();
        var arrived = island.Clone();
        arrived.Creatures.Add(Predator(7, PredatorType.Carnotaurus, Row5(5), ordinal: 3));
        Assert.Equal(Pairs((0, Cue.Arrive)), Of(One(Build(island, arrived, Rest(), Time(), new Arrived(7, Row5(5), false)), BeatKind.Arrives)));

        Assert.Equal(Pairs((0, Cue.Starve)), Of(One(Build(island, island, Rest(), new TimePassed(5, 0, 0)), BeatKind.TimeHurts)));
        Assert.Equal(Pairs((0, Cue.Stage)), Of(One(Build(island, island, Rest(), Time(), new Grew(24, 25.4), new StageReached(1)), BeatKind.Stage)));

        var apex = island.Clone();
        apex.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4));
        Assert.Equal(Pairs((0, Cue.Apex)), Of(One(Build(island, apex, Rest(), Time(), new ApexArrived(9, Grid.Index(10, 0))), BeatKind.Apex)));
    }

    [Fact]
    public void TheEndIsWonOrLost()
    {
        var island = Island();
        Assert.Equal(Pairs((0, Cue.Lost)), Of(One(Build(island, island, Rest(), new TimePassed(5, 0, 0), new RunEnded(false, EndReason.Hunger, 0)), BeatKind.End)));
        Assert.Equal(Pairs((0, Cue.Lost)), Of(One(Build(island, island, Rest(), new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 99, false, false),
            new RunEnded(false, EndReason.Caught, CarnoId)), BeatKind.End)));
        Assert.Equal(Pairs((0, Cue.Win)), Of(One(Build(island, island, Rest(), Time(), new RunEnded(true, EndReason.Outlasted, 0)), BeatKind.End)));
    }

    [Fact]
    public void OutOfSightIsHeardOnlyWhenSomethingHeardYouAndToldByItsTicks()
    {
        // The two lengths differ, or the rule could not tell them apart (H3: the beat carries no event).
        Assert.NotEqual(BeatTicks.UnseenHeard, BeatTicks.UnseenQuiet);
        var quiet = One(Rested(), BeatKind.Unseen);
        Assert.Empty(quiet.Events);
        Assert.Empty(Of(quiet));
        var heard = One(Rested(new Noticed(AlloId, Notice.Heard), Step(AlloId, Gait.Search, Row5(10), Row5(9))), BeatKind.Unseen);
        Assert.Empty(heard.Events);
        Assert.Equal(Pairs((0, Cue.Heard)), Of(heard));
    }

    [Fact]
    public void TheSilentBeatsAreSilent()
    {
        var island = Run(Here, Predator(CarnoId, PredatorType.Carnotaurus, Row5(4), ordinal: 2), Game(5, Row5(4)), Game(6, Row5(5)));
        var script = Build(island, With(With(island, 5, Row5(3)), 6, Row5(4)), Rest(),
            new Stayed(CarnoId, Idle.Dazed),
            Step(5, Gait.Roam, Row5(4), Row5(3)), Step(6, Gait.Flee, Row5(5), Row5(4)), Time(), new Grew(10, 11.4));
        var silent = new[] { BeatKind.Gap, BeatKind.Stays, BeatKind.SmallGame, BeatKind.TimePasses };
        foreach (var kind in silent) Assert.Contains(script.Beats, b => b.Kind == kind);
        foreach (var beat in script.Beats.Where(b => silent.Contains(b.Kind)))
            Assert.Empty(Of(beat, apex: true));
        foreach (var why in new[] { Idle.Waits, Idle.Held })
            Assert.Empty(Of(One(Rested(new Stayed(CarnoId, why)), BeatKind.Stays)));
    }

    [Fact]
    public void AHunterOutOfSightHasNoCue()
    {
        // The Allosaurus, eight cells off, hunts and steps: no beat of it, so no Focus,
        // no step and no notice it could own; out of sight says only that something heard you.
        var island = Island();
        var script = Build(island, With(island, AlloId, Row5(9)), Rest(),
            new Noticed(AlloId, Notice.Spotted), Step(AlloId, Gait.Hunt, Row5(10), Row5(9)), Time());
        Assert.DoesNotContain(script.Beats, b => b.Creature == AlloId);
        var cues = script.Beats.SelectMany(b => Of(b)).Select(p => p.Cue).ToList();
        Assert.Equal(new[] { Cue.Rest, Cue.Heard }, cues);
        Assert.DoesNotContain(Cue.HunterStep, cues);
        Assert.DoesNotContain(Cue.Focus, cues);
        Assert.DoesNotContain(Cue.Spotted, cues);
    }

    // ---- under the beats' ticks --------------------------------------------------------

    [Fact]
    public void EveryCueIsUnderItsBeatsTicksForTheSeededTurnsAndTheLongest()
    {
        var pairs = 0;
        var kinds = new HashSet<Cue>();
        foreach (var script in TurnPlaybackTests.Seeded(200).Select(t => t.Script).Append(Longest()))
            foreach (var beat in script.Beats)
                foreach (var apex in new[] { false, true })
                {
                    var sounds = Of(beat, apex);
                    Assert.All(sounds, s => Assert.InRange(s.Tick, 0, beat.Ticks - 1));
                    Assert.Equal(sounds.OrderBy(s => s.Tick), sounds);   // in tick order
                    pairs += sounds.Count;
                    kinds.UnionWith(sounds.Select(s => s.Cue));
                }
        Assert.True(pairs > 1000, $"only {pairs} pairs");
        _out.WriteLine($"{pairs} pairs over the 200 seeded turns and the longest, each under its beat's ticks; cues seen: {string.Join(", ", kinds.OrderBy(c => c))}");
    }

    /// <summary>
    /// The longest turn the rules can produce (PLAN-HATCHLING 3.8): danger 5, the apex
    /// and seven predators known, each noticing, stepping twice, biting, hurt by spikes
    /// and bleeding; a sprint of five cells; a stage reached; the run ending.
    /// </summary>
    internal static TurnScript Longest()
    {
        var you = Grid.Index(5, 5);
        var five = new[] { Grid.Index(5, 4), Grid.Index(5, 3), Grid.Index(5, 2), Grid.Index(5, 1), Grid.Index(5, 0) };
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
        return TurnScripts.Build(Turn(before, YouAt(before, five[^1]), events.ToArray()), (_, _) => true);
    }

    // ---- the plan's two turns (H97) -----------------------------------------------------

    /// <summary>You walk a cell; a Troodon and Carnotaurus 2 each spot you, step two cells and bite; time passes (BeatTableTests' turn).</summary>
    internal static TurnScript TwoHunters()
    {
        var to = Row5(3);
        int troodonFrom = Row5(6), carnoFrom = Grid.Index(3, 8);
        var island = Run(Here,
            Predator(1, PredatorType.Troodon, troodonFrom),
            Predator(2, PredatorType.Carnotaurus, carnoFrom, ordinal: 2));
        var after = With(With(YouAt(island, to), 1, Row5(4), alert: 4), 2, Grid.Index(3, 6), alert: 4);
        return Build(island, after, Walk(Here, to),
            new Noticed(1, Notice.Spotted), Step(1, Gait.Hunt, troodonFrom, Row5(5), Row5(4)), new Bit(1, 9, false, false),
            new Noticed(2, Notice.Spotted), Step(2, Gait.Hunt, carnoFrom, Grid.Index(3, 7), Grid.Index(3, 6)), new Bit(2, 9, false, false),
            Time());
    }

    /// <summary>The cues of a script on the turn's own clock: each pair at its beat's start plus its tick.</summary>
    internal static List<(int Tick, Cue Cue)> Track(TurnScript script)
    {
        var track = new List<(int, Cue)>();
        var start = 0;
        foreach (var beat in script.Beats)
        {
            track.AddRange(BeatSounds.Of(beat, apex: false).Select(s => (start + s.Tick, s.Cue)));
            start += beat.Ticks;
        }
        return track;
    }

    [Fact]
    public void AQuietTurnIsOneSoftStepAndTwoHuntersAreElevenCuesInSixAndAHalfSeconds()
    {
        var empty = Run(Here);
        var quiet = Build(empty, YouAt(empty, Row5(1)), Walk(Here, Row5(1)), Time());
        Assert.Equal(new[] { (0, Cue.Step) }, Track(quiet));
        Assert.Equal(39, quiet.TotalTicks);

        var two = TwoHunters();
        var track = Track(two);
        Assert.Equal(195, two.TotalTicks);
        Assert.Equal(11, track.Count);
        // A step, then for each hunter: a ping, a chirp, two thuds, a crunch (PLAN-SOUND 3.3).
        Assert.Equal(new[] { Cue.Step, Cue.Focus, Cue.Spotted, Cue.HunterStep, Cue.HunterStep, Cue.Bite, Cue.Focus, Cue.Spotted, Cue.HunterStep, Cue.HunterStep, Cue.Bite },
            track.Select(t => t.Cue));
        Assert.Equal(new[] { 0, 25, 40, 52, 60, 74, 103, 118, 130, 138, 152 }, track.Select(t => t.Tick));   // the S01 board's demo
    }

    // ---- the skip (H98) -----------------------------------------------------------------

    [Fact]
    public void ASkipPlaysTheEndElseTheApexElseAStageElseNothing()
    {
        var island = Island();
        var apex = island.Clone();
        apex.Creatures.Add(Predator(9, PredatorType.Tyrannosaurus, Grid.Index(10, 0), alert: 4));
        var all = Build(island, apex, Rest(), Time(), new Grew(24, 25.4), new StageReached(1),
            new ApexArrived(9, Grid.Index(10, 0)), new RunEnded(true, EndReason.Outlasted, 0)).Beats;
        Assert.Equal(Cue.Win, BeatSounds.OnSkip(all, 0));

        var lost = Build(island, island, Rest(), new TimePassed(5, 0, 0), new Grew(24, 25.4), new StageReached(1), new RunEnded(false, EndReason.Hunger, 0)).Beats;
        Assert.Equal(Cue.Lost, BeatSounds.OnSkip(lost, 0));

        var noEnd = Build(island, apex, Rest(), Time(), new Grew(24, 25.4), new StageReached(1), new ApexArrived(9, Grid.Index(10, 0))).Beats;
        Assert.Equal(Cue.Apex, BeatSounds.OnSkip(noEnd, 0));

        var stage = Build(island, island, Rest(), Time(), new Grew(24, 25.4), new StageReached(1)).Beats;
        Assert.Equal(Cue.Stage, BeatSounds.OnSkip(stage, 0));

        Assert.Null(BeatSounds.OnSkip(Rested(new Stayed(CarnoId, Idle.Waits), new Bit(CarnoId, 12, false, false)).Beats, 0));

        // Only the beats from the first not begun: an end already begun has played its own cue.
        var endAt = lost.ToList().FindIndex(b => b.Kind == BeatKind.End);
        Assert.Equal(Cue.Lost, BeatSounds.OnSkip(lost, endAt));
        Assert.Null(BeatSounds.OnSkip(lost, endAt + 1));
        var stageAt = stage.ToList().FindIndex(b => b.Kind == BeatKind.Stage);
        Assert.Null(BeatSounds.OnSkip(stage, stageAt + 1));
        Assert.Null(BeatSounds.OnSkip(Array.Empty<Beat>(), 0));
    }
}
