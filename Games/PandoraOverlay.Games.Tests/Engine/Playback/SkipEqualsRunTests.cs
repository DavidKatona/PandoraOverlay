using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// IStep's rule, held for every stock step and for nested groups: running a
/// playback to its end, and skipping it at any tick (before the first, at
/// every tick in between, after the end), leave exactly the same picture;
/// and so does finishing a step directly at any point, before it began or
/// after it ended, with nothing drawn after the end. This is what lets a
/// player skip, a broken step be cut short and the speed change, at no cost
/// to what the board shows (A20). A06.
/// </summary>
public class SkipEqualsRunTests
{
    /// <summary>
    /// Each makes fresh steps over a fresh scene (a step is played once).
    /// Within a <see cref="Steps.Together"/> the steps draw different things,
    /// as that group asks; one after another they may draw the same.
    /// </summary>
    private static readonly Dictionary<string, Func<Scene, IStep[]>> Scenarios = new(StringComparer.Ordinal)
    {
        ["wait"] = s => new[] { Steps.Wait(5) },
        ["call"] = s => new[] { Steps.Call(() => s.Bump("c")) },
        ["tween linear"] = s => new[] { Steps.Tween(6, v => s.Set("x", 10 + 20 * v)) },
        ["tween in-out"] = s => new[] { Steps.Tween(6, v => s.Set("x", 10 + 20 * v), Easing.InOut) },
        ["tween out"] = s => new[] { Steps.Tween(6, v => s.Set("x", 10 + 20 * v), Easing.Out) },
        ["sequence"] = s => new[]
        {
            Steps.Sequence(
                Steps.Tween(4, v => s.Set("x", 3 * v)),
                Steps.Call(() => s.Bump("hit")),
                Steps.Wait(3),
                Steps.Tween(5, v => s.Set("x", 3 + v), Easing.InOut)),
        },
        ["together"] = s => new[]
        {
            Steps.Together(
                Steps.Tween(3, v => s.Set("a", v)),
                Steps.Tween(7, v => s.Set("b", -v), Easing.Out),
                Steps.Call(() => s.Bump("c")),
                Steps.Wait(2)),
        },
        ["nested"] = s => new[]
        {
            Steps.Sequence(
                Steps.Together(
                    Steps.Tween(4, v => s.Set("a", v)),
                    Steps.Sequence(
                        Steps.Call(() => s.Bump("b")),
                        Steps.Tween(3, v => s.Set("c", 2 * v), Easing.InOut))),
                Steps.Wait(2),
                Steps.Together(
                    Steps.Sequence(),
                    Steps.Together(),
                    Steps.Tween(5, v => s.Set("a", 1 + v), Easing.Out)),
                Steps.Sequence(
                    Steps.Sequence(Steps.Call(() => s.Bump("d"))),
                    Steps.Together(Steps.Tween(2, v => s.Set("e", v))))),
        },
        ["a queue of beats"] = s => new IStep[]
        {
            Steps.Tween(3, v => s.Set("x", v)),
            Steps.Call(() => s.Bump("one")),
            Steps.Wait(4),
            Steps.Together(
                Steps.Tween(5, v => s.Set("y", v), Easing.InOut),
                Steps.Tween(2, v => s.Set("z", v))),
            Steps.Sequence(
                Steps.Call(() => s.Bump("two")),
                Steps.Tween(3, v => s.Set("x", 1 - v), Easing.Out)),
            Steps.Call(() => s.Bump("three")),
        },
    };

    public static TheoryData<string> Names
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var name in Scenarios.Keys)
                names.Add(name);
            return names;
        }
    }

    /// <summary>
    /// Plays a scenario through a playback: to its end, or for
    /// <paramref name="skipAfter"/> ticks (advancing an idle playback does
    /// nothing, so a count past the end is "after the end") and then a skip.
    /// </summary>
    private static (Scene Scene, int Ticks) Play(string name, int? skipAfter = null, int speed = 1)
    {
        var scene = new Scene();
        var playback = new Playback { Speed = speed };
        foreach (var step in Scenarios[name](scene))
            playback.Enqueue(step);

        var ticks = 0;
        if (skipAfter is int k)
        {
            for (var i = 0; i < k; i++)
                playback.Advance();
            playback.SkipAll();
        }
        else
        {
            ticks = PlaybackRun.TicksToEnd(playback);
        }

        Assert.True(playback.Idle);
        return (scene, ticks);
    }

    /// <summary>
    /// The skip at every moment leaves the full run's picture, and in both
    /// every call ran exactly once.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void SkippingAtAnyTickLeavesTheFullRunsPicture(string name)
    {
        var (full, ticks) = Play(name);
        Assert.All(full.CallRuns, runs => Assert.Equal(1, runs));
        Assert.True(ticks > 0);

        var differences = new List<string>();
        for (var k = 0; k <= ticks + 1; k++)
        {
            var (skipped, _) = Play(name, skipAfter: k);
            if (skipped.Picture != full.Picture)
                differences.Add("skipped after " + k + " ticks: " + skipped.Picture);
        }

        Assert.Empty(differences);
    }

    /// <summary>The speed changes the pace, never the picture.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheSpeedNeverChangesThePicture(string name)
    {
        var (reference, ticks) = Play(name);

        foreach (var speed in new[] { 2, 3, 5, 1000 })
        {
            var (faster, fasterTicks) = Play(name, speed: speed);
            Assert.Equal(reference.Picture, faster.Picture);
            Assert.Equal((ticks + speed - 1) / speed, fasterTicks);
        }
    }

    /// <summary>
    /// The rule on the step itself, with no playback: finished before it
    /// began, right after Begin, between any two advances, and after it
    /// ended, it leaves the full run's picture; and once it has ended (by
    /// itself or by Finish) nothing more is drawn, whatever is called on it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void FinishingTheStepAtAnyPointLeavesTheFullRunsPicture(string name)
    {
        // The full run, and then every call again: nothing more drawn.
        var full = new Scene();
        var step = TheStep(name, full);
        step.Begin();
        var advances = 1;
        while (!step.Advance())
            advances++;
        AssertNothingMoreIsDrawn(step, full);

        // Before Begin.
        var early = new Scene();
        var skippedEarly = TheStep(name, early);
        skippedEarly.Finish();
        Assert.Equal(full.Picture, early.Picture);
        AssertNothingMoreIsDrawn(skippedEarly, early);

        // After Begin and k advances, short of the end.
        for (var k = 0; k < advances; k++)
        {
            var scene = new Scene();
            var midway = TheStep(name, scene);
            midway.Begin();
            for (var i = 0; i < k; i++)
                Assert.False(midway.Advance());
            midway.Finish();
            Assert.Equal(full.Picture, scene.Picture);
            AssertNothingMoreIsDrawn(midway, scene);
        }
    }

    /// <summary>The scenario as one step: itself when it is one, its steps in a sequence otherwise.</summary>
    private static IStep TheStep(string name, Scene scene)
    {
        var steps = Scenarios[name](scene);
        return steps.Length == 1 ? steps[0] : Steps.Sequence(steps);
    }

    /// <summary>Every call on an ended step leaves the picture as it is and draws nothing at all.</summary>
    private static void AssertNothingMoreIsDrawn(IStep ended, Scene scene)
    {
        var picture = scene.Picture;
        var writes = scene.Writes;

        ended.Finish();
        ended.Begin();
        Assert.True(ended.Advance());
        ended.Finish();

        Assert.Equal(picture, scene.Picture);
        Assert.Equal(writes, scene.Writes);
    }
}
