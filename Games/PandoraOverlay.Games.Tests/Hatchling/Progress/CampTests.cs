using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>Camp's choices (H15, H20): any species can be chosen, only an unlocked one started, a danger up to the highest unlocked.</summary>
public class CampTests
{
    [Fact]
    public void ALockedSpeciesCanBeChosenButNotStarted()
    {
        var state = GameState.Fresh(Moment);
        Assert.True(Camp.CanStart(state));

        var chosen = Unchanged(state, s => Camp.Select(s, "triceratops"));
        Assert.Equal("Triceratops", chosen.Selected);   // the table's spelling (H36)
        Assert.False(Camp.CanStart(chosen));

        Unlocks.Grant(chosen, "Triceratops");
        Assert.True(Camp.CanStart(chosen));
    }

    [Fact]
    public void AnUnknownSpeciesChangesNothing()
    {
        var state = GameState.Fresh(Moment);
        var next = Unchanged(state, s => Camp.Select(s, "Velociraptor"));
        Assert.Equal(Fingerprint(state), Fingerprint(next));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(3, 2, true)]
    [InlineData(3, 4, false)]
    [InlineData(5, 5, true)]
    [InlineData(5, 0, false)]
    [InlineData(5, 6, false)]
    [InlineData(5, -1, false)]
    public void ADangerAboveTheHighestUnlockedOrOutsideOneToFiveIsRefused(int maxDanger, int danger, bool allowed)
    {
        var state = GameState.Fresh(Moment);
        state.MaxDanger = maxDanger;
        var next = Unchanged(state, s => Camp.SetDanger(s, danger));
        if (allowed) Assert.Equal(danger, next!.Danger);
        else Assert.Null(next);
    }

    [Fact]
    public void ThePaceIsKept()
    {
        var state = GameState.Fresh(Moment);
        Assert.True(Unchanged(state, s => Camp.SetFast(s, true)).Fast);
        Assert.False(Camp.SetFast(Camp.SetFast(state, true), false).Fast);
    }

    [Fact]
    public void NoRunStartsWhileOneExists()
    {
        var state = GameState.Fresh(Moment);
        state.Run = new RunState { Species = "Dryosaurus" };
        Assert.False(Camp.CanStart(state));

        state.Run = EndedRun();   // an ended run is left through its summary first
        Assert.False(Camp.CanStart(state));
    }

    [Fact]
    public void ADangerAboveTheHighestUnlockedCanNotStartEvenWhenASaveHoldsIt()
    {
        var state = GameState.Fresh(Moment);
        state.Danger = 2;
        Assert.False(Camp.CanStart(state));
    }
}
