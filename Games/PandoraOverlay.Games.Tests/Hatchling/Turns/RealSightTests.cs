using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The turn script with the game's own <see cref="Sight.Knows"/> handed in, as the
/// view hands it in: Wary's sense two cells beyond sight, and the apex known
/// anywhere (H3, H6). Every other test uses the stand-in <c>Scenes.Sees</c>.
/// </summary>
public class RealSightTests
{
    private static readonly Known Real = (run, c) => Sight.Knows(run, Stats.Of(run), c);

    /// <summary>A fresh Dryosaurus run with no creature, all grass; Wary if asked.</summary>
    private static RunState Plain(bool wary)
    {
        var run = Runs.Fresh("Dryosaurus", 11).Clear().Paved(Terrain.Grass);
        if (wary) run.Instincts.Add(Instinct.Wary);
        return run;
    }

    /// <summary>The lowest cell exactly <paramref name="distance"/> from you.</summary>
    private static int CellAt(RunState run, int distance) =>
        Enumerable.Range(0, Tuning.Cells).First(c => Island.Grid.Distance(run.You.At, c) == distance);

    private static TurnScript Waits(RunState before, params TurnEvent[] action)
    {
        var events = new TurnEvent[] { new Rested(8, 0) }.Concat(action).Append(new TimePassed(0, 0, 0)).ToArray();
        return TurnScripts.Build(new TurnResult(before, before, events), Real);
    }

    [Fact]
    public void WaryKnowsAPredatorTwoBeyondSight()
    {
        var plain = Plain(wary: true);
        var beyond = Stats.Of(plain).Sight + Tuning.WarySense;
        var run = plain.Put(PredatorType.Carnotaurus, CellAt(plain, beyond));
        var id = run.Creatures[^1].Id;
        var script = Waits(run, new Noticed(id, Notice.Heard), new Stayed(id, Idle.Waits));
        Assert.Contains(script.Beats, b => b.Kind == BeatKind.Focus && b.Creature == id);
        Assert.Single(script.Rows, r => r.Actor == RowActor.Predator);
        Assert.Equal(HatchlingText.UnseenQuiet, Assert.Single(script.Beats, b => b.Kind == BeatKind.Unseen).Banner);
    }

    [Fact]
    public void WithoutWaryItIsOutOfSightAndOnlyHeard()
    {
        var plain = Plain(wary: false);
        var beyond = Stats.Of(plain).Sight + Tuning.WarySense;
        var run = plain.Put(PredatorType.Carnotaurus, CellAt(plain, beyond));
        var id = run.Creatures[^1].Id;
        var script = Waits(run, new Noticed(id, Notice.Heard), new Stayed(id, Idle.Waits));
        Assert.DoesNotContain(script.Beats, b => b.Creature == id);
        Assert.DoesNotContain(script.Rows, r => r.Actor == RowActor.Predator);
        Assert.Equal(HatchlingText.UnseenHeard, Assert.Single(script.Beats, b => b.Kind == BeatKind.Unseen).Banner);
    }

    [Fact]
    public void TheApexAnywhereHasBeats()
    {
        var plain = Plain(wary: false);
        var farthest = Enumerable.Range(0, Tuning.Cells).Max(c => Island.Grid.Distance(plain.You.At, c));
        var run = plain.Put(PredatorType.Tyrannosaurus, CellAt(plain, farthest), alert: Tuning.AlertSeen);
        var id = run.Creatures[^1].Id;
        var script = Waits(run, new Stayed(id, Idle.Waits));
        Assert.Contains(script.Beats, b => b.Kind == BeatKind.Stays && b.Creature == id);
        Assert.Equal(PredatorType.Tyrannosaurus, Assert.Single(script.Rows, r => r.Actor == RowActor.Predator).Type);
    }
}
