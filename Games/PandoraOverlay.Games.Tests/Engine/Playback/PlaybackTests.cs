using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The playback's queue: the order of every Begin, Advance and Finish, the
/// speed, the skip, the drain, the overrun, and what a step's own code may
/// and may not do. A06; A19, A20.
/// </summary>
public class PlaybackTests
{
    // ---- The order of the calls ------------------------------------------------

    /// <summary>
    /// The queue used by the order tests: A finishes on its 2nd advance, B on
    /// its 1st, C on its 3rd. Six advances in all.
    /// </summary>
    private static (Playback Playback, List<string> Log, ProbeStep[] Steps) ThreeSteps()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var steps = new[]
        {
            new ProbeStep("A", log, finishOn: 2),
            new ProbeStep("B", log, finishOn: 1),
            new ProbeStep("C", log, finishOn: 3),
        };
        foreach (var step in steps)
            playback.Enqueue(step);
        return (playback, log, steps);
    }

    /// <summary>
    /// Run to the end at speed 1: one advance a tick, a step begun in the
    /// tick of its first advance, the next one only on the tick after a step
    /// finished, nothing finished that finished by itself, and the drain once
    /// at the end.
    /// </summary>
    [Fact]
    public void AQueueOfThreeRunsInOrderOneAdvanceATick()
    {
        var (playback, log, _) = ThreeSteps();

        var ticks = PlaybackRun.ToEnd(playback, log);

        Assert.Equal(new[]
        {
            "A.Begin A.Advance",
            "A.Advance",
            "B.Begin B.Advance",
            "C.Begin C.Advance",
            "C.Advance",
            "C.Advance Drained",
        }, ticks);
    }

    /// <summary>Speed 2: half the ticks, and exactly the same calls in the same order.</summary>
    [Fact]
    public void SpeedTwoTakesHalfTheTicksWithTheSameCalls()
    {
        var (playback, log, _) = ThreeSteps();
        playback.Speed = 2;

        var ticks = PlaybackRun.ToEnd(playback, log);

        Assert.Equal(new[]
        {
            "A.Begin A.Advance A.Advance",
            "B.Begin B.Advance C.Begin C.Advance",
            "C.Advance C.Advance Drained",
        }, ticks);
    }

    /// <summary>
    /// The speed changes the pace, never the outcome: at every speed the
    /// calls are the ones speed 1 makes, in its order, in as many ticks as
    /// the six advances take at that pace.
    /// </summary>
    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(6, 1)]
    [InlineData(50, 1)]
    public void TheSpeedNeverChangesTheCalls(int speed, int expectedTicks)
    {
        var (reference, referenceLog, _) = ThreeSteps();
        PlaybackRun.ToEnd(reference, referenceLog);

        var (playback, log, _) = ThreeSteps();
        playback.Speed = speed;
        var ticks = PlaybackRun.ToEnd(playback, log);

        Assert.Equal(referenceLog, log);
        Assert.Equal(expectedTicks, ticks.Count);
    }

    /// <summary>
    /// The skip at every moment of the three-step queue: what ran before it
    /// is exactly the first ticks of a full run; then the current step (begun)
    /// and every queued one (never begun) are finished, in order, and the
    /// drain comes once. A step that finished by itself is never finished
    /// again, and after the end the skip does nothing at all.
    /// </summary>
    [Theory]
    [InlineData(0, "A.Finish B.Finish C.Finish Drained")]
    [InlineData(1, "A.Finish B.Finish C.Finish Drained")]
    [InlineData(2, "B.Finish C.Finish Drained")]
    [InlineData(3, "C.Finish Drained")]
    [InlineData(4, "C.Finish Drained")]
    [InlineData(5, "C.Finish Drained")]
    [InlineData(6, "")]
    [InlineData(7, "")]
    public void SkipAllAtEveryMomentFinishesWhatIsLeftInOrder(int ticksFirst, string expectedSkip)
    {
        var (reference, referenceLog, _) = ThreeSteps();
        var fullRun = PlaybackRun.ToEnd(reference, referenceLog);

        var (playback, log, steps) = ThreeSteps();
        var ran = new List<string>();
        for (var i = 0; i < ticksFirst; i++)
            ran.Add(PlaybackRun.OneTick(playback, log));
        var before = log.Count;
        playback.SkipAll();
        var skip = string.Join(" ", log.Skip(before));

        Assert.Equal(fullRun.Take(ticksFirst).Concat(Enumerable.Repeat("", Math.Max(0, ticksFirst - fullRun.Count))), ran);
        Assert.Equal(expectedSkip, skip);
        Assert.True(playback.Idle);
        Assert.All(steps, s => Assert.True(s.Begins <= 1 && s.Finishes <= 1, s.Name + " was begun or finished twice"));
        Assert.Single(log, e => e == "Drained");
    }

    /// <summary>A skip mid-step at speed 2 finishes the same steps as at speed 1.</summary>
    [Fact]
    public void SkipAllAtSpeedTwoFinishesTheSameSteps()
    {
        var (playback, log, _) = ThreeSteps();
        playback.Speed = 2;
        playback.Advance(); // A done
        var before = log.Count;

        playback.SkipAll();

        Assert.Equal("B.Finish C.Finish Drained", string.Join(" ", log.Skip(before)));
    }

    // ---- Idle, Enqueue, Speed ---------------------------------------------------

    [Fact]
    public void IdleMeansNoCurrentStepAndNothingQueued()
    {
        var log = new List<string>();
        var playback = new Playback();
        Assert.True(playback.Idle);

        playback.Enqueue(new ProbeStep("A", log, finishOn: 2));
        Assert.False(playback.Idle); // queued

        playback.Advance();
        Assert.False(playback.Idle); // current, nothing queued

        playback.Advance();
        Assert.True(playback.Idle);
    }

    [Fact]
    public void AnIdlePlaybackDoesNothingWhenAdvancedOrSkipped()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);

        playback.Advance();
        playback.SkipAll();
        playback.Clear();

        Assert.Empty(log);
        Assert.True(playback.Idle);
    }

    [Fact]
    public void EnqueuingNullThrows()
    {
        var playback = new Playback();

        Assert.Throws<ArgumentNullException>(() => playback.Enqueue(null!));
        Assert.True(playback.Idle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ASpeedUnderOneThrowsAndKeepsTheOldSpeed(int speed)
    {
        var playback = new Playback { Speed = 3 };

        Assert.Throws<ArgumentOutOfRangeException>(() => playback.Speed = speed);
        Assert.Equal(3, playback.Speed);
    }

    [Fact]
    public void TheSpeedIsOneByDefault() => Assert.Equal(1, new Playback().Speed);

    /// <summary>
    /// A step that changes the speed changes the next tick's pace, not the
    /// one it is in: the speed is read once at the start of each tick.
    /// </summary>
    [Fact]
    public void ASpeedChangedByAStepTakesEffectOnTheNextTick()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var a = new ProbeStep("A", log, finishOn: 1);
        playback.Enqueue(a);
        playback.Enqueue(new ProbeStep("B", log, finishOn: 1));
        playback.Enqueue(new ProbeStep("C", log, finishOn: 1));
        a.OnAdvance = () => playback.Speed = 2;

        var ticks = PlaybackRun.ToEnd(playback, log);

        Assert.Equal(new[] { "A.Begin A.Advance", "B.Begin B.Advance C.Begin C.Advance Drained" }, ticks);
    }

    // ---- Drained ------------------------------------------------------------------

    /// <summary>
    /// Once per emptying: once when a run ends, never again while it stays
    /// idle, once more for the next playback.
    /// </summary>
    [Fact]
    public void DrainedComesOncePerEmptying()
    {
        var drained = 0;
        var playback = new Playback();
        playback.Drained += () => drained++;

        playback.Enqueue(Steps.Wait(2));
        for (var i = 0; i < 5; i++)
            playback.Advance();
        Assert.Equal(1, drained);

        playback.Enqueue(Steps.Wait(1));
        playback.Enqueue(Steps.Wait(1));
        for (var i = 0; i < 5; i++)
            playback.Advance();
        Assert.Equal(2, drained);

        playback.Enqueue(Steps.Wait(4));
        playback.SkipAll();
        playback.SkipAll();
        Assert.Equal(3, drained);
    }

    /// <summary>
    /// A clear drops everything for a view going away: no step is finished,
    /// nothing drains, and nothing of what was queued ever plays.
    /// </summary>
    [Fact]
    public void ClearDropsEverythingWithNoFinishAndNoDrain()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        playback.Enqueue(new ProbeStep("A", log, finishOn: 3));
        playback.Enqueue(new ProbeStep("B", log));
        playback.Advance();

        playback.Clear();
        playback.Advance();
        playback.SkipAll();

        Assert.Equal(new[] { "A.Begin", "A.Advance" }, log);
        Assert.True(playback.Idle);
    }

    /// <summary>
    /// The drain is raised after the playback is done with its steps, so its
    /// handler may use the playback freely: here it starts the next playback,
    /// which plays and drains in its turn.
    /// </summary>
    [Fact]
    public void ADrainedHandlerMayStartTheNextPlayback()
    {
        var log = new List<string>();
        var playback = new Playback();
        var drained = 0;
        playback.Drained += () =>
        {
            drained++;
            log.Add("Drained");
            if (drained == 1)
            {
                playback.Enqueue(new ProbeStep("B", log));
                playback.Advance(); // allowed here: no step's code is running
            }
        };
        playback.Enqueue(new ProbeStep("A", log));

        playback.Advance();

        Assert.Equal(new[] { "A.Begin", "A.Advance", "Drained", "B.Begin", "B.Advance", "Drained" }, log);
        Assert.True(playback.Idle);
    }

    // ---- Steps that enqueue -------------------------------------------------------

    /// <summary>A step's code may enqueue: the new step plays after everything already queued, and the drain waits for it.</summary>
    [Fact]
    public void AStepMayEnqueueAndItsStepGoesToTheBack()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var a = new ProbeStep("A", log);
        a.OnAdvance = () => playback.Enqueue(new ProbeStep("D", log));
        playback.Enqueue(a);
        playback.Enqueue(new ProbeStep("B", log));

        var ticks = PlaybackRun.ToEnd(playback, log);

        Assert.Equal(new[] { "A.Begin A.Advance", "B.Begin B.Advance", "D.Begin D.Advance Drained" }, ticks);
    }

    /// <summary>
    /// What a finishing step enqueues during a skip is finished too: running
    /// to the end would have played it, so the skip must leave its picture.
    /// </summary>
    [Fact]
    public void SkipAllFinishesWhatAFinishingStepEnqueues()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var a = new ProbeStep("A", log, finishOn: 5);
        a.OnFinish = () => playback.Enqueue(new ProbeStep("D", log));
        playback.Enqueue(a);
        playback.Enqueue(new ProbeStep("B", log));
        playback.Advance();

        playback.SkipAll();

        Assert.Equal(new[] { "A.Begin", "A.Advance", "A.Finish", "B.Finish", "D.Finish", "Drained" }, log);
        Assert.True(playback.Idle);
    }

    // ---- The overrun ----------------------------------------------------------------

    /// <summary>
    /// A step that never finishes is finished for it on its 300th advance,
    /// reported once, and dropped; the next step plays on the next tick.
    /// </summary>
    [Fact]
    public void ANeverEndingStepIsFinishedAtTheLimitAndReported()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var broken = ProbeStep.Never("X", log);
        var next = new ProbeStep("B", log);
        playback.Enqueue(broken);
        playback.Enqueue(next);

        for (var i = 1; i < Playback.StepLimitTicks; i++)
            playback.Advance();
        Assert.Equal(0, broken.Finishes);
        Assert.DoesNotContain("Overran", log);

        var limitTick = PlaybackRun.OneTick(playback, log);
        Assert.Equal("X.Advance X.Finish Overran", limitTick);
        Assert.Equal(Playback.StepLimitTicks, broken.Advances);

        var after = PlaybackRun.OneTick(playback, log);
        Assert.Equal("B.Begin B.Advance Drained", after);
        Assert.Equal(1, broken.Finishes);
        Assert.Single(log, e => e == "Overran");
    }

    [Fact]
    public void TheLimitIsTenSecondsOfEngineTicks() =>
        Assert.Equal(10 * EngineTime.TicksPerSecond, Playback.StepLimitTicks);

    /// <summary>
    /// The limit counts the step's own advances, not loop ticks, so the
    /// speed never changes whether a step overruns: at speed 2 the same 300
    /// advances take 150 ticks.
    /// </summary>
    [Fact]
    public void TheLimitCountsTheStepsAdvancesAtAnySpeed()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var broken = ProbeStep.Never("X", log);
        playback.Enqueue(broken);
        playback.Speed = 2;

        var ticks = PlaybackRun.TicksToEnd(playback);

        Assert.Equal(Playback.StepLimitTicks / 2, ticks);
        Assert.Equal(Playback.StepLimitTicks, broken.Advances);
        Assert.Equal(1, broken.Finishes);
        Assert.Equal(new[] { "X.Finish", "Overran", "Drained" }, log.Where(e => e != "X.Advance" && e != "X.Begin"));
    }

    /// <summary>
    /// A step that finishes on exactly its 300th advance finished by itself:
    /// no Finish, no report.
    /// </summary>
    [Fact]
    public void AStepFinishingOnItsLastAllowedAdvanceIsNotAnOverrun()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var step = new ProbeStep("A", log, finishOn: Playback.StepLimitTicks);
        playback.Enqueue(step);

        PlaybackRun.ToEnd(playback, log);

        Assert.Equal(0, step.Finishes);
        Assert.DoesNotContain("Overran", log);
        Assert.Single(log, e => e == "Drained");
    }

    /// <summary>
    /// A sequence is one step to the queue: one that runs past ten seconds is
    /// cut short, and still leaves its end state, as IStep's rule promises.
    /// </summary>
    [Fact]
    public void ALongSequenceIsCutShortButLeavesItsEndState()
    {
        var scene = new Scene();
        var overran = 0;
        var playback = new Playback();
        playback.StepOverran += () => overran++;
        playback.Enqueue(Steps.Sequence(
            Steps.Tween(200, v => scene.Set("a", v)),
            Steps.Tween(200, v => scene.Set("b", v))));

        var ticks = PlaybackRun.TicksToEnd(playback);

        Assert.Equal(Playback.StepLimitTicks, ticks);
        Assert.Equal(1, overran);
        Assert.Equal(1, scene.Get("a"));
        Assert.Equal(1, scene.Get("b"));
    }

    // ---- Re-entrancy ------------------------------------------------------------------

    public enum Call { Advance, SkipAll, Clear }

    public enum Where { Begin, Advance, Finish }

    /// <summary>
    /// A step's own code may not advance, skip or clear its playback, from
    /// any of its three calls: it throws InvalidOperationException, which
    /// passes out to the caller, and leaves the playback cleared and usable.
    /// (Finish is reached through a skip.)
    /// </summary>
    [Theory]
    [InlineData(Call.Advance, Where.Begin)]
    [InlineData(Call.Advance, Where.Advance)]
    [InlineData(Call.Advance, Where.Finish)]
    [InlineData(Call.SkipAll, Where.Begin)]
    [InlineData(Call.SkipAll, Where.Advance)]
    [InlineData(Call.SkipAll, Where.Finish)]
    [InlineData(Call.Clear, Where.Begin)]
    [InlineData(Call.Clear, Where.Advance)]
    [InlineData(Call.Clear, Where.Finish)]
    public void AStepMayNotAdvanceSkipOrClearItsPlayback(Call call, Where where)
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var step = new ProbeStep("A", log, finishOn: 3);
        Action reenter = call switch
        {
            Call.Advance => playback.Advance,
            Call.SkipAll => playback.SkipAll,
            _ => playback.Clear,
        };
        switch (where)
        {
            case Where.Begin: step.OnBegin = reenter; break;
            case Where.Advance: step.OnAdvance = reenter; break;
            default: step.OnFinish = reenter; break;
        }
        playback.Enqueue(step);
        playback.Enqueue(new ProbeStep("B", log));

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (where == Where.Finish)
            {
                playback.Advance(); // begun and advanced once
                playback.SkipAll();
            }
            else
            {
                playback.Advance();
            }
        });

        Assert.True(playback.Idle);
        Assert.DoesNotContain("Drained", log);
        StillWorks(playback);
    }

    // ---- A step that throws -------------------------------------------------------------

    private sealed class Boom : Exception
    {
    }

    /// <summary>
    /// A step that throws, in any of its calls: the very exception passes out
    /// (the loop reports it as the game's fault, A10), nothing drains, the
    /// steps after it are dropped, and the playback is idle and usable.
    /// </summary>
    [Theory]
    [InlineData(Where.Begin)]
    [InlineData(Where.Advance)]
    [InlineData(Where.Finish)]
    public void AThrowingStepLeavesAnIdleUsablePlayback(Where where)
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var boom = new Boom();
        var step = new ProbeStep("A", log, finishOn: 3);
        switch (where)
        {
            case Where.Begin: step.OnBegin = () => throw boom; break;
            case Where.Advance: step.OnAdvance = () => throw boom; break;
            default: step.OnFinish = () => throw boom; break;
        }
        var after = new ProbeStep("B", log);
        playback.Enqueue(step);
        playback.Enqueue(after);

        var thrown = Assert.Throws<Boom>(() =>
        {
            if (where == Where.Finish)
            {
                playback.Advance();
                playback.SkipAll();
            }
            else
            {
                playback.Advance();
            }
        });

        Assert.Same(boom, thrown);
        Assert.True(playback.Idle);
        Assert.DoesNotContain("Drained", log);
        Assert.Equal(0, after.Begins + after.Advances + after.Finishes);
        StillWorks(playback);
    }

    /// <summary>A step that throws when the overrun finishes it: the same, the playback cleared and usable.</summary>
    [Fact]
    public void AStepThrowingWhenFinishedForItsOverrunLeavesAnIdleUsablePlayback()
    {
        var log = new List<string>();
        var playback = PlaybackRun.Logged(log);
        var broken = ProbeStep.Never("X", log);
        broken.OnFinish = () => throw new Boom();
        playback.Enqueue(broken);
        for (var i = 1; i < Playback.StepLimitTicks; i++)
            playback.Advance();

        Assert.Throws<Boom>(playback.Advance);

        Assert.True(playback.Idle);
        Assert.DoesNotContain("Overran", log);
        StillWorks(playback);
    }

    /// <summary>A fresh step plays, drains and reports on a playback that was just refused or faulted.</summary>
    private static void StillWorks(Playback playback)
    {
        var log = new List<string>();
        var drained = 0;
        playback.Drained += () => drained++;
        playback.Enqueue(new ProbeStep("Z", log, finishOn: 2));

        var ticks = PlaybackRun.ToEnd(playback, log);

        Assert.Equal(new[] { "Z.Begin Z.Advance", "Z.Advance" }, ticks);
        Assert.Equal(1, drained);
    }
}
