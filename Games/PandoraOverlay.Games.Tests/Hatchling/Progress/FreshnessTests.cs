using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Freshness (H18): it comes back 0.25 a calendar day, to 3.0 at most, worked out
/// when needed; a clock set back gains nothing; what a screen reads changes nothing.
/// </summary>
public class FreshnessTests
{
    /// <summary>A fresh save with Maiasaura worn to <paramref name="fresh"/> and Troodon to 2.0, up to date at <see cref="ProgressSamples.Moment"/>.</summary>
    private static GameState Worn(double fresh = 1.0)
    {
        var state = GameState.Fresh(Moment);
        state.RecordToChange("Maiasaura").Fresh = fresh;
        state.RecordToChange("Troodon").Fresh = 2.0;
        return state;
    }

    /// <summary><see cref="Freshness.Advance"/> on a copy: the copy, and the state given checked unchanged.</summary>
    private static GameState Advanced(GameState state, DateTime moment) => Unchanged(state, s =>
    {
        var copy = s.Clone();
        Freshness.Advance(copy, moment);
        return copy;
    });

    [Fact]
    public void TwoDaysBringBackAHalf()
    {
        var next = Advanced(Worn(), Moment.AddDays(2));
        Assert.Equal(1.5, next.Record("Maiasaura").Fresh, 9);
        Assert.Equal(2.5, next.Record("Troodon").Fresh, 9);
        Assert.Equal(Moment.AddDays(2), next.FreshAsOf);
    }

    [Fact]
    public void PartsOfADayCount()
    {
        var next = Advanced(Worn(), Moment.AddHours(12));
        Assert.Equal(1.125, next.Record("Maiasaura").Fresh, 9);
    }

    [Fact]
    public void FreshnessNeverRisesAboveThree()
    {
        var next = Advanced(Worn(2.9), Moment.AddDays(10));
        Assert.Equal(3.0, next.Record("Maiasaura").Fresh);
        Assert.Equal(3.0, next.Record("Troodon").Fresh);
        Assert.Equal(3.0, Advanced(Worn(), Moment.AddYears(50)).Record("Maiasaura").Fresh);
    }

    [Fact]
    public void AMomentBeforeFreshAsOfChangesNothingAndDoesNotMoveIt()
    {
        var state = Worn();
        foreach (var back in new[] { Moment, Moment.AddTicks(-1), Moment.AddDays(-3), DateTime.MinValue })
        {
            var next = Advanced(state, back);
            Assert.Equal(Fingerprint(state), Fingerprint(next));
        }
    }

    [Fact]
    public void ForwardAgainCountsOnlyFromFreshAsOf()
    {
        // Two days on, then the clock set back a day, then three days on from the start:
        // only the one day after the latest moment seen counts.
        var state = Advanced(Worn(), Moment.AddDays(2));
        state = Advanced(state, Moment.AddDays(1));
        Assert.Equal(1.5, state.Record("Maiasaura").Fresh, 9);
        Assert.Equal(Moment.AddDays(2), state.FreshAsOf);

        state = Advanced(state, Moment.AddDays(3));
        Assert.Equal(1.75, state.Record("Maiasaura").Fresh, 9);
        Assert.Equal(Moment.AddDays(3), state.FreshAsOf);
    }

    [Fact]
    public void TurningTheClockBackAndForthGainsNothing()
    {
        // Each real day counted once, however the calendar wanders.
        var wandering = Worn();
        foreach (var day in new[] { 1.0, -5, 2, 0.5, -1, 4 })
            wandering = Advanced(wandering, Moment.AddDays(day));
        var straight = Advanced(Worn(), Moment.AddDays(4));
        Assert.Equal(straight.Record("Maiasaura").Fresh, wandering.Record("Maiasaura").Fresh, 9);
        Assert.Equal(straight.FreshAsOf, wandering.FreshAsOf);
    }

    [Fact]
    public void ASpeciesWithNoRecordIsAtThreeAndStaysUnstored()
    {
        var state = Worn();
        Assert.Equal(3.0, Freshness.At(state, "Stegosaurus", Moment.AddDays(1)));
        Assert.Equal(3.0, Freshness.At(state, "Velociraptor", Moment.AddDays(1)));
        var next = Advanced(state, Moment.AddDays(1));
        Assert.False(next.Species.ContainsKey("Stegosaurus"));
    }

    [Fact]
    public void AtIsAdvanceWithoutChangingTheState()
    {
        var state = Worn();
        var moments = new[] { Moment.AddDays(-2), Moment, Moment.AddMinutes(7), Moment.AddHours(30), Moment.AddDays(9) };
        foreach (var moment in moments)
            foreach (var species in new[] { "Maiasaura", "troodon", "Stegosaurus", "Velociraptor" })
            {
                var at = Unchanged(state, s => Freshness.At(s, species, moment));
                Assert.Equal(Advanced(state, moment).Record(species).Fresh, at, 12);
            }
    }

    [Fact]
    public void AdvancingTwiceToOneMomentIsAdvancingOnce()
    {
        var once = Advanced(Worn(), Moment.AddDays(1.5));
        var twice = Advanced(once, Moment.AddDays(1.5));
        Assert.Equal(Fingerprint(once), Fingerprint(twice));
    }
}
