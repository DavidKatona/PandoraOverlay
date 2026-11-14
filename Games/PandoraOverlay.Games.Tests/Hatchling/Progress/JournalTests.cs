using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The field journal's draws and its swap (H12): three entries of three species,
/// never the two most played, never one already open, the lean to what was grown
/// least, and the swap.
/// </summary>
public class JournalTests
{
    private readonly ITestOutputHelper _output;

    public JournalTests(ITestOutputHelper output) => _output = output;

    /// <summary>A fresh save at <see cref="ProgressSamples.Moment"/> with an empty journal whose generator is <paramref name="rng"/>.</summary>
    private static GameState Empty(uint rng)
    {
        var state = GameState.Fresh(Moment);
        state.Journal.Open.Clear();
        state.Journal.Rng = rng;
        return state;
    }

    private static readonly HashSet<string> Never = new(SpeciesTable.JournalNever, StringComparer.Ordinal);

    // ---- A fresh save's journal ---------------------------------------------------------------

    [Fact]
    public void AFreshSaveHasThreeEntriesOfThreeSpeciesNeverTheTwo()
    {
        for (var i = 0; i < 300; i++)
        {
            var state = GameState.Fresh(Moment.AddMinutes(i * 37));
            var open = state.Journal.Open;
            Assert.Equal(3, open.Count);
            Assert.Equal(3, open.Select(e => e.Species).Distinct().Count());
            Assert.All(open, e =>
            {
                Assert.DoesNotContain(e.Species, Never);
                Assert.True(SpeciesTable.TryGet(e.Species, out var info));
                Assert.Equal(info.Name, e.Species);   // the table's spelling (H36)
                Assert.Equal(0, e.Seen);
            });
            Assert.True(state.IsFresh);   // an untouched journal is not progress
        }
    }

    [Fact]
    public void AFreshSavesJournalIsDrawnFromItsOwnStream()
    {
        // The generator a fresh save sets (H25) is the one the three were drawn with,
        // three rolls of it, and the run's seed is untouched by them.
        var state = GameState.Fresh(Moment);
        var start = Rng.Mix(GameState.Seed0(Moment), GameState.JournalSalt);
        var redrawn = Empty(start);
        Journal.Fill(redrawn);
        Assert.Equal(state.Journal.Open.Select(e => e.Species), redrawn.Journal.Open.Select(e => e.Species));

        var rolls = new Rng(start);
        for (var i = 0; i < 3; i++) rolls.NextDouble();
        Assert.Equal(rolls.State, state.Journal.Rng);
        Assert.Equal(Rng.Mix(GameState.Seed0(Moment), GameState.RunSalt), state.NextSeed);
    }

    [Fact]
    public void FillingAFullJournalDrawsNothing()
    {
        var state = GameState.Fresh(Moment);
        var before = Fingerprint(state);
        Journal.Fill(state);
        Assert.Equal(before, Fingerprint(state));
    }

    [Fact]
    public void ADrawTakesOneRollOfTheJournalsGenerator()
    {
        var state = Empty(12345);
        Journal.Draw(state);
        var rolls = new Rng(12345);
        rolls.NextDouble();
        Assert.Equal(rolls.State, state.Journal.Rng);
        Assert.Empty(state.Journal.Open);   // a draw is not added: its caller puts it in place
    }

    // ---- The draw -----------------------------------------------------------------------------

    [Fact]
    public void ADrawIsNeverTheTwoNorOneAlreadyOpenNorTheOneLeftOut()
    {
        for (uint seed = 0; seed < 2000; seed++)
        {
            var state = Empty(seed * 2654435761u);
            Journal.Fill(state);
            state.Journal.Open.RemoveAt((int)(seed % 3));
            var leftOut = SpeciesTable.All[(int)(seed % 21)].Name;
            var drawn = Journal.Draw(state, leftOut);
            Assert.DoesNotContain(drawn.Species, Never);
            Assert.DoesNotContain(drawn.Species, state.Journal.Open.Select(e => e.Species));
            Assert.NotEqual(leftOut, drawn.Species);
        }
    }

    [Fact]
    public void BigBuildsAskFiftyForFiveHundredAndTheOthersSeventyFiveForFourHundred()
    {
        foreach (var species in SpeciesTable.All)
        {
            var entry = Journal.EntryFor(species);
            var big = species.Build is Build.Tank or Build.Apex or Build.Stalker or Build.Swimmer;
            Assert.Equal(big ? 50 : 75, entry.Target);
            Assert.Equal(big ? 500 : 400, entry.Reward);
            Assert.Equal(species.Name, entry.Species);
            Assert.Equal(0, entry.Seen);
        }
    }

    [Fact]
    public void EveryDrawableSpeciesComesUpAndTheTwoNever()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var state = Empty(7);
        for (var i = 0; i < 3000; i++)
        {
            var entry = Journal.Draw(state);
            counts[entry.Species] = counts.GetValueOrDefault(entry.Species) + 1;
        }
        Assert.Equal(SpeciesTable.All.Count - Never.Count, counts.Count);
        Assert.DoesNotContain(counts.Keys, Never.Contains);
    }

    [Fact]
    public void TheJournalLeansToWhatWasGrownLeast()
    {
        // The prototype's check: after five species were grown 200% each on the
        // server, they are under 12% of 400 fills' draws (each weighs a fifth).
        var five = new[] { "Dryosaurus", "Hypsilophodon", "Kentrosaurus", "Pachycephalosaurus", "Tenontosaurus" };
        var grown = Server.Quiet();
        foreach (var name in five)
            for (var life = 0; life < 2; life++)
                grown = Server.Grow(grown, name, 0, 1000, Moment, stepPerMille: 10);
        Assert.All(five, n => Assert.Equal(200, grown.Record(n).Grown, 6));

        var draws = new List<string>();
        for (uint k = 0; k < 400; k++)
        {
            var state = grown.Clone();
            state.Journal.Open.Clear();
            state.Journal.Rng = k * 977 + 3;
            Journal.Fill(state);
            draws.AddRange(state.Journal.Open.Select(e => e.Species));
        }
        var share = draws.Count(five.Contains) / (double)draws.Count;
        _output.WriteLine($"The five grown 200% each: {share:P1} of {draws.Count} draws.");
        Assert.True(share < 0.12, $"grown species came up {share:P1}");
    }

    // ---- The swap -----------------------------------------------------------------------------

    [Fact]
    public void ASwapCostsFiftyAndPutsANewEntryInThePlace()
    {
        var state = WithPay(120);
        state.Journal.Open[1].Seen = 30;
        var next = Unchanged(state, s => Journal.Swap(s, 1))!;

        Assert.Equal(70, next.Wallet.Balance);
        Assert.Equal(120, next.Wallet.Earned);   // spending never lowers what was earned
        Assert.Equal(3, next.Journal.Open.Count);
        Assert.NotEqual(state.Journal.Open[1].Species, next.Journal.Open[1].Species);
        Assert.Equal(0, next.Journal.Open[1].Seen);
        Assert.Equal(state.Journal.Open[0].Species, next.Journal.Open[0].Species);
        Assert.Equal(state.Journal.Open[2].Species, next.Journal.Open[2].Species);
        Assert.Equal(3, next.Journal.Open.Select(e => e.Species).Distinct().Count());
        Assert.Equal(state.Journal.Done, next.Journal.Done);   // a swap is not a finished entry
    }

    [Fact]
    public void ASwapIsRefusedWhenShortOrAtABadIndex()
    {
        var poor = WithPay(49);
        Assert.Null(Unchanged(poor, s => Journal.Swap(s, 0)));

        var rich = WithPay(1000);
        foreach (var index in new[] { -1, 3, 99, int.MinValue })
            Assert.Null(Unchanged(rich, s => Journal.Swap(s, index)));

        Assert.NotNull(Journal.Swap(WithPay(50), 2));
    }

    [Fact]
    public void ASwapNeverReturnsTheSpeciesSwappedAway()
    {
        for (uint seed = 0; seed < 1000; seed++)
        {
            var state = WithPay(50);
            state.Journal.Rng = seed * 40503u;
            var index = (int)(seed % 3);
            var next = Journal.Swap(state, index)!;
            Assert.NotEqual(state.Journal.Open[index].Species, next.Journal.Open[index].Species);
            Assert.Equal(3, next.Journal.Open.Select(e => e.Species).Distinct().Count());
            Assert.DoesNotContain(next.Journal.Open[index].Species, Never);
        }
    }
}
