using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The stock steps one by one: when each finishes, what a tween applies and
/// when, how the two groups call their children. That each leaves the same
/// picture however it ends is <see cref="SkipEqualsRunTests"/>. A06.
/// </summary>
public class StepsTests
{
    /// <summary>Advances a begun step until it answers true and counts the advances.</summary>
    private static int AdvancesToFinish(IStep step)
    {
        step.Begin();
        for (var n = 1; n <= 10_000; n++)
        {
            if (step.Advance())
                return n;
        }
        throw new Xunit.Sdk.XunitException("the step never finished");
    }

    // ---- Wait -------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(7, 7)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void AWaitFinishesOnItsLastTick(int ticks, int expected) =>
        Assert.Equal(expected, AdvancesToFinish(Steps.Wait(ticks)));

    [Fact]
    public void AFinishedWaitStaysFinished()
    {
        var wait = Steps.Wait(5);
        wait.Finish();

        Assert.True(wait.Advance());
    }

    // ---- Call -------------------------------------------------------------------------

    /// <summary>Run in Begin, never again, and finished on the first advance.</summary>
    [Fact]
    public void ACallRunsOnceInBeginAndFinishesOnTheFirstAdvance()
    {
        var runs = 0;
        var call = Steps.Call(() => runs++);

        call.Begin();
        Assert.Equal(1, runs);
        Assert.True(call.Advance());
        call.Finish();
        call.Begin();
        Assert.Equal(1, runs);
    }

    /// <summary>Skipped before it began: run once, in Finish, and never again.</summary>
    [Fact]
    public void ACallFinishedBeforeItBeganRunsOnceInFinish()
    {
        var runs = 0;
        var call = Steps.Call(() => runs++);

        call.Finish();
        Assert.Equal(1, runs);
        call.Begin();
        Assert.True(call.Advance());
        call.Finish();
        Assert.Equal(1, runs);
    }

    /// <summary>Advanced with no Begin (a game's own composite that slipped): still run, once.</summary>
    [Fact]
    public void ACallAdvancedWithNoBeginStillRunsOnce()
    {
        var runs = 0;
        var call = Steps.Call(() => runs++);

        Assert.True(call.Advance());
        call.Finish();
        Assert.Equal(1, runs);
    }

    /// <summary>
    /// "Once" holds even when the action throws: a step finished after its
    /// action threw does not run it again.
    /// </summary>
    [Fact]
    public void ACallWhoseActionThrewDoesNotRunItAgain()
    {
        var runs = 0;
        var call = Steps.Call(() =>
        {
            runs++;
            throw new InvalidOperationException();
        });

        Assert.Throws<InvalidOperationException>(call.Begin);
        call.Finish();
        Assert.Equal(1, runs);
    }

    // ---- Tween ------------------------------------------------------------------------

    /// <summary>
    /// A four-tick tween's every value, worked out by hand: apply(0) at Begin,
    /// then the curve at 1/4, 2/4 and 3/4, then exactly 1 at the fourth
    /// advance, which finishes it; nothing after.
    /// </summary>
    [Theory]
    [InlineData(Easing.Linear, 0.25, 0.5, 0.75)]
    [InlineData(Easing.InOut, 0.15625, 0.5, 0.84375)]
    [InlineData(Easing.Out, 0.4375, 0.75, 0.9375)]
    public void ATweenAppliesTheCurveAtEachTick(Easing easing, double quarter, double half, double threeQuarters)
    {
        var applied = new List<double>();
        var tween = Steps.Tween(4, applied.Add, easing);

        tween.Begin();
        var finished = new List<bool>();
        for (var i = 0; i < 4; i++)
            finished.Add(tween.Advance());
        tween.Finish();

        Assert.Equal(new[] { 0, quarter, half, threeQuarters, 1 }, applied);
        Assert.Equal(new[] { false, false, false, true }, finished);
    }

    /// <summary>The curves at their two ends and in the middle, each end exact.</summary>
    [Theory]
    [InlineData(Easing.Linear, 0.5)]
    [InlineData(Easing.InOut, 0.5)]
    [InlineData(Easing.Out, 0.75)]
    public void EveryCurveStartsAtZeroAndEndsAtOne(Easing easing, double middle)
    {
        Assert.Equal(0, Steps.Ease(easing, 0));
        Assert.Equal(middle, Steps.Ease(easing, 0.5));
        Assert.Equal(1, Steps.Ease(easing, 1));
    }

    /// <summary>
    /// Finish is apply(1) exactly, and apply(1) is always the last call, made
    /// once: finished mid-way, finished before it began, finished again,
    /// begun or advanced after it finished.
    /// </summary>
    [Fact]
    public void ATweensLastCallIsAlwaysApplyOne()
    {
        var midway = new List<double>();
        var a = Steps.Tween(10, midway.Add, Easing.InOut);
        a.Begin();
        a.Advance();
        a.Advance();
        a.Finish();
        a.Finish();
        a.Begin();
        Assert.True(a.Advance());
        Assert.Equal(4, midway.Count);
        Assert.Equal(1, midway[^1]);
        Assert.Single(midway, v => v == 1);

        var early = new List<double>();
        var b = Steps.Tween(10, early.Add);
        b.Finish();
        b.Begin();
        Assert.True(b.Advance());
        Assert.Equal(new double[] { 1 }, early);

        var ran = new List<double>();
        var c = Steps.Tween(3, ran.Add, Easing.Out);
        Assert.Equal(3, AdvancesToFinish(c));
        c.Finish();
        Assert.True(c.Advance());
        Assert.Equal(1, ran[^1]);
        Assert.Single(ran, v => v == 1);
    }

    /// <summary>0 ticks or less is one tick, as for a wait: apply(0) at Begin, apply(1) on the first advance.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ATweenOfNoTicksTakesOne(int ticks)
    {
        var applied = new List<double>();
        var tween = Steps.Tween(ticks, applied.Add);

        Assert.Equal(1, AdvancesToFinish(tween));
        Assert.Equal(new double[] { 0, 1 }, applied);
    }

    /// <summary>Advanced with no Begin: it begins first, so the values are a full run's.</summary>
    [Fact]
    public void ATweenAdvancedWithNoBeginBeginsFirst()
    {
        var applied = new List<double>();
        var tween = Steps.Tween(2, applied.Add);

        tween.Advance();
        tween.Advance();

        Assert.Equal(new[] { 0, 0.5, 1 }, applied);
    }

    [Fact]
    public void ATweenNeedsSomethingToApplyAndAKnownCurve()
    {
        Assert.Throws<ArgumentNullException>(() => Steps.Tween(3, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Steps.Tween(3, _ => { }, (Easing)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Steps.Ease((Easing)99, 0.5));
    }

    [Fact]
    public void ACallNeedsAnAction() =>
        Assert.Throws<ArgumentNullException>(() => Steps.Call(null!));

    // ---- Sequence ---------------------------------------------------------------------

    /// <summary>
    /// One after another, and the next one begins on the advance after the
    /// one before finished: A takes two advances, B one, C two.
    /// </summary>
    [Fact]
    public void ASequencePlaysItsStepsOneAfterAnother()
    {
        var log = new List<string>();
        var sequence = Steps.Sequence(
            new ProbeStep("A", log, finishOn: 2),
            new ProbeStep("B", log, finishOn: 1),
            new ProbeStep("C", log, finishOn: 2));

        sequence.Begin();
        Assert.Equal(new[] { "A.Begin" }, log);
        var ticks = new List<string>();
        var finished = new List<bool>();
        for (var i = 0; i < 5; i++)
        {
            var before = log.Count;
            finished.Add(sequence.Advance());
            ticks.Add(string.Join(" ", log.Skip(before)));
        }

        Assert.Equal(new[] { "A.Advance", "A.Advance", "B.Begin B.Advance", "C.Begin C.Advance", "C.Advance" }, ticks);
        Assert.Equal(new[] { false, false, false, false, true }, finished);
    }

    /// <summary>
    /// A sequence plays exactly as its steps enqueued one by one would, tick
    /// for tick: grouping never changes the timing.
    /// </summary>
    [Fact]
    public void ASequencePlaysAsItsStepsEnqueuedWould()
    {
        var queuedLog = new List<string>();
        var queued = new Playback();
        queued.Enqueue(new ProbeStep("A", queuedLog, finishOn: 2));
        queued.Enqueue(new ProbeStep("B", queuedLog, finishOn: 1));
        queued.Enqueue(new ProbeStep("C", queuedLog, finishOn: 3));
        var queuedTicks = PlaybackRun.ToEnd(queued, queuedLog);

        var groupedLog = new List<string>();
        var grouped = new Playback();
        grouped.Enqueue(Steps.Sequence(
            new ProbeStep("A", groupedLog, finishOn: 2),
            new ProbeStep("B", groupedLog, finishOn: 1),
            new ProbeStep("C", groupedLog, finishOn: 3)));
        var groupedTicks = PlaybackRun.ToEnd(grouped, groupedLog);

        Assert.Equal(queuedTicks, groupedTicks);
    }

    /// <summary>Finished mid-way: the current step and every later one, in order, the later ones never begun.</summary>
    [Fact]
    public void FinishingASequenceFinishesTheCurrentAndEveryLaterOne()
    {
        var log = new List<string>();
        var sequence = Steps.Sequence(
            new ProbeStep("A", log, finishOn: 1),
            new ProbeStep("B", log, finishOn: 3),
            new ProbeStep("C", log, finishOn: 1));
        sequence.Begin();
        sequence.Advance(); // A done
        sequence.Advance(); // B begun
        var before = log.Count;

        sequence.Finish();
        sequence.Finish();
        Assert.True(sequence.Advance());

        Assert.Equal(new[] { "B.Finish", "C.Finish" }, log.Skip(before));
    }

    /// <summary>Finished before it began: every step finished, none begun.</summary>
    [Fact]
    public void ASequenceFinishedBeforeItBeganFinishesEveryStep()
    {
        var log = new List<string>();
        var sequence = Steps.Sequence(new ProbeStep("A", log), new ProbeStep("B", log));

        sequence.Finish();
        sequence.Begin();
        Assert.True(sequence.Advance());

        Assert.Equal(new[] { "A.Finish", "B.Finish" }, log);
    }

    // ---- Together ---------------------------------------------------------------------

    /// <summary>
    /// All begin together, each is advanced until it finishes and then left
    /// alone, and the group finishes when the last one does.
    /// </summary>
    [Fact]
    public void TogetherBeginsAllAndFinishesWithTheLast()
    {
        var log = new List<string>();
        var together = Steps.Together(
            new ProbeStep("A", log, finishOn: 3),
            new ProbeStep("B", log, finishOn: 1),
            new ProbeStep("C", log, finishOn: 2));

        together.Begin();
        Assert.Equal(new[] { "A.Begin", "B.Begin", "C.Begin" }, log);
        var ticks = new List<string>();
        var finished = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            var before = log.Count;
            finished.Add(together.Advance());
            ticks.Add(string.Join(" ", log.Skip(before)));
        }

        Assert.Equal(new[] { "A.Advance B.Advance C.Advance", "A.Advance C.Advance", "A.Advance" }, ticks);
        Assert.Equal(new[] { false, false, true }, finished);
    }

    /// <summary>Finished mid-way: every step not yet finished, in the order given, and nothing after.</summary>
    [Fact]
    public void FinishingTogetherFinishesEveryUnfinishedStepInOrder()
    {
        var log = new List<string>();
        var together = Steps.Together(
            new ProbeStep("A", log, finishOn: 3),
            new ProbeStep("B", log, finishOn: 1),
            new ProbeStep("C", log, finishOn: 2));
        together.Begin();
        together.Advance(); // B done
        var before = log.Count;

        together.Finish();
        together.Finish();
        together.Begin();
        Assert.True(together.Advance());

        Assert.Equal(new[] { "A.Finish", "C.Finish" }, log.Skip(before));
    }

    /// <summary>Finished before it began: every step finished, none begun.</summary>
    [Fact]
    public void TogetherFinishedBeforeItBeganFinishesEveryStep()
    {
        var log = new List<string>();
        var together = Steps.Together(new ProbeStep("A", log), new ProbeStep("B", log));

        together.Finish();
        together.Begin();
        Assert.True(together.Advance());

        Assert.Equal(new[] { "A.Finish", "B.Finish" }, log);
    }

    // ---- Both groups ------------------------------------------------------------------

    [Fact]
    public void AnEmptyGroupFinishesOnItsFirstAdvance()
    {
        Assert.Equal(1, AdvancesToFinish(Steps.Sequence()));
        Assert.Equal(1, AdvancesToFinish(Steps.Together()));
    }

    [Fact]
    public void AGroupRefusesAMissingStep()
    {
        Assert.Throws<ArgumentNullException>(() => Steps.Sequence(null!));
        Assert.Throws<ArgumentNullException>(() => Steps.Together(null!));
        Assert.Throws<ArgumentException>(() => Steps.Sequence(Steps.Wait(1), null!));
        Assert.Throws<ArgumentException>(() => Steps.Together(null!, Steps.Wait(1)));
    }

    /// <summary>A group keeps its own copy: changing the caller's array afterwards changes nothing.</summary>
    [Fact]
    public void AGroupKeepsItsOwnCopyOfTheSteps()
    {
        var log = new List<string>();
        var forSequence = new IStep[] { new ProbeStep("A", log), new ProbeStep("B", log) };
        var forTogether = new IStep[] { new ProbeStep("C", log), new ProbeStep("D", log) };
        var sequence = Steps.Sequence(forSequence);
        var together = Steps.Together(forTogether);
        forSequence[1] = new ProbeStep("X", log);
        forTogether[0] = new ProbeStep("Y", log);

        sequence.Finish();
        together.Finish();

        Assert.Equal(new[] { "A.Finish", "B.Finish", "C.Finish", "D.Finish" }, log);
    }
}
