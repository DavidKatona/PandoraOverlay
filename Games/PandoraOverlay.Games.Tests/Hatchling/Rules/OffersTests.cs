using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>The offers of instincts (H14): what is offered, a pick, the redraws and their prices, the picks owed at the start.</summary>
public class OffersTests
{
    /// <summary>A run that starts with one offer open (Born ready), and the lineage's other <paramref name="more"/> ranks.</summary>
    private static RunState Offered(uint seed = 1, params (Trait, int)[] more) =>
        Runs.Fresh("Dryosaurus", seed, Runs.Ranks(more.Append((Trait.BornReady, 1)).ToArray()), stars: 0);

    /// <summary>A run with no offer open, the given instincts owned, and one pick owed.</summary>
    private static RunState Owning(IEnumerable<Instinct> owned, params (Trait, int)[] ranks)
    {
        var run = Runs.Fresh("Dryosaurus", 5, Runs.Ranks(ranks), stars: 0);
        run.Offer = null;
        run.PicksOwed = 0;
        run.Instincts.AddRange(owned);
        return run;
    }

    // ---- What an offer is -----------------------------------------------------------

    [Fact]
    public void AnOfferIsThreeDistinctInstinctsNoneOwned()
    {
        for (uint seed = 1; seed <= 100; seed++)
        {
            var run = Offered(seed);
            Assert.NotNull(run.Offer);
            Assert.Equal(Tuning.OfferSize, run.Offer!.Count);
            Assert.Equal(3, run.Offer.Distinct().Count());
            Assert.DoesNotContain(run.Offer, run.Has);
        }
    }

    [Fact]
    public void NoInstinctOwnedIsEverOffered()
    {
        var owned = new[] { Instinct.Eyes, Instinct.Wary, Instinct.Hardy, Instinct.Skin, Instinct.Quick };
        for (uint seed = 1; seed <= 100; seed++)
        {
            var run = Owning(owned);
            Offers.Owe(run, 1);
            var rng = new Rng(seed);
            Offers.OpenIfOwed(run, rng);
            Assert.Equal(3, run.Offer!.Count);
            Assert.Empty(run.Offer.Intersect(owned));
        }
    }

    [Fact]
    public void SharpInstinctsOffersFour()
    {
        var run = Offered(1, (Trait.SharpInstincts, 1));
        Assert.Equal(Tuning.SharpOfferSize, run.Offer!.Count);
        Assert.Equal(4, run.Offer.Distinct().Count());
    }

    /// <summary>The not-owned in the table's order, shuffled by the run's generator: the offer is the seed's, for ever.</summary>
    [Fact]
    public void AnOfferIsTheTableShuffledByTheRunsGenerator()
    {
        var run = Owning(Array.Empty<Instinct>());
        Offers.Owe(run, 1);
        var rng = new Rng(2024);
        Offers.OpenIfOwed(run, rng);

        var expected = Instincts.All.ToList();
        var copy = new Rng(2024);
        copy.Shuffle(expected);
        Assert.Equal(expected.Take(3), run.Offer);
        Assert.Equal(copy.State, rng.State);   // eleven rolls, for twelve instincts
    }

    [Fact]
    public void WhenElevenAreOwnedTheOfferHoldsTheLastOne()
    {
        var run = Owning(Instincts.All.Where(i => i != Instinct.Nose));
        Offers.Owe(run, 1);
        Offers.OpenIfOwed(run, new Rng(1));
        Assert.Equal(new[] { Instinct.Nose }, run.Offer);
    }

    [Fact]
    public void WhenAllTwelveAreOwnedNoOfferOpensAndNothingStaysOwed()
    {
        var run = Owning(Instincts.All);
        Offers.Owe(run, 2);
        Offers.OpenIfOwed(run, new Rng(1));
        Assert.Null(run.Offer);
        Assert.Equal(0, run.PicksOwed);
    }

    // ---- Owing and opening --------------------------------------------------------------

    [Fact]
    public void OweAddsToThePicksOwedAndNothingForZeroOrLess()
    {
        var run = Owning(Array.Empty<Instinct>());
        Offers.Owe(run, 2);
        Offers.Owe(run, 0);
        Offers.Owe(run, -3);
        Offers.Owe(run, 1);
        Assert.Equal(3, run.PicksOwed);
        Assert.Null(run.Offer);   // owing opens nothing by itself
    }

    [Fact]
    public void NothingOpensWhenNothingIsOwedOrAnOfferIsOpen()
    {
        var run = Owning(Array.Empty<Instinct>());
        var rng = new Rng(8);
        Offers.OpenIfOwed(run, rng);
        Assert.Null(run.Offer);
        Assert.Equal(8u, rng.State);   // no roll

        var open = Offered();
        var shown = open.Offer!.ToList();
        open.PicksOwed = 2;
        Offers.OpenIfOwed(open, rng);
        Assert.Equal(shown, open.Offer);
        Assert.Equal(8u, rng.State);
    }

    // ---- The picks owed at the start (Strong start, Born ready) ---------------------

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(2, false, 0)]   // 20%: under Juvenile
    [InlineData(3, false, 1)]   // 30%: past Juvenile
    [InlineData(0, true, 1)]
    [InlineData(3, true, 2)]
    public void TheStartOwesAPickForEachLineAlreadyPastAndOneForBornReady(int strongStart, bool bornReady, int owed)
    {
        var ranks = new List<(Trait, int)> { (Trait.StrongStart, strongStart) };
        if (bornReady) ranks.Add((Trait.BornReady, 1));
        var run = Runs.Fresh("Dryosaurus", 1, Runs.Ranks(ranks.ToArray()), stars: 0);
        Assert.Equal(owed, run.PicksOwed);
        Assert.Equal(owed > 0, run.Offer is not null);   // the first is drawn at once
    }

    /// <summary>An offer drawn at the start rolls from the run's generator: the island's pinned rolls come first, the offer's after.</summary>
    [Fact]
    public void AnOfferAtTheStartDoesNotMoveTheIsland()
    {
        var plain = Runs.Fresh("Dryosaurus", 7919);
        var offered = Offered(7919);
        Assert.Equal(plain.Tiles, offered.Tiles);
        Assert.Equal(plain.Creatures.Select(c => c.At), offered.Creatures.Select(c => c.At));
        Assert.NotEqual(plain.Rng, offered.Rng);
    }

    // ---- Pick -----------------------------------------------------------------------------

    [Fact]
    public void APickAddsTheInstinctClosesTheOfferAndLeavesTheRunGivenAsItWas()
    {
        var run = Offered();
        var before = Runs.Fingerprint(run);
        var chosen = run.Offer![1];

        var next = Offers.Pick(run, 1)!;

        Assert.Equal(before, Runs.Fingerprint(run));
        Assert.Equal(new[] { chosen }, next.Instincts);
        Assert.Null(next.Offer);
        Assert.Equal(0, next.PicksOwed);
        Assert.Equal(run.Turn, next.Turn);   // no turn passes
        Assert.Equal(run.Rng, next.Rng);     // nothing more owed: nothing rolled
    }

    [Fact]
    public void APickOpensTheNextOfferWhenOneIsOwed()
    {
        var run = Offered(3, (Trait.StrongStart, 3));   // two owed: Juvenile already past, and Born ready
        Assert.Equal(2, run.PicksOwed);
        var chosen = run.Offer![0];

        var next = Offers.Pick(run, 0)!;

        Assert.Equal(1, next.PicksOwed);
        Assert.NotNull(next.Offer);
        Assert.Equal(3, next.Offer!.Count);
        Assert.DoesNotContain(chosen, next.Offer);
        Assert.NotEqual(run.Rng, next.Rng);   // the next offer's rolls came from the run's generator

        var last = Offers.Pick(next, 2)!;
        Assert.Equal(2, last.Instincts.Count);
        Assert.Null(last.Offer);
        Assert.Equal(0, last.PicksOwed);
    }

    [Fact]
    public void HardyKeepsTheShareOfHealth()
    {
        var run = Owning(Array.Empty<Instinct>());
        run.You.Health = 0.5;
        run.Offer = new List<Instinct> { Instinct.Hardy, Instinct.Eyes, Instinct.Feet };
        run.PicksOwed = 1;
        var next = Offers.Pick(run, 0)!;
        Assert.Equal(0.5, next.You.Health);
        Assert.True(Stats.Of(next).MaxHealth > Stats.Of(run).MaxHealth);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void APickOutsideTheOfferIsRefused(int index)
    {
        Assert.Null(Offers.Pick(Offered(), index));
    }

    [Fact]
    public void APickWithNoOfferIsRefused()
    {
        Assert.Null(Offers.Pick(Runs.Fresh("Dryosaurus", 1), 0));
    }

    // ---- Redraw and its price -------------------------------------------------------

    [Fact]
    public void TheRedrawsCost25ThenDouble()
    {
        var run = Offered();
        var prices = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            prices.Add(Offers.RedrawPrice(run));
            run = Offers.Redraw(run)!;
        }
        Assert.Equal(new[] { 25, 50, 100, 200 }, prices);
        Assert.Equal(4, run.Redraws);
        Assert.False(run.FreeRedrawUsed);
    }

    [Fact]
    public void SecondThoughtsMakesTheFirstRedrawFreeAndItDoesNotCount()
    {
        var run = Offered(1, (Trait.SecondThoughts, 1));
        var prices = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            prices.Add(Offers.RedrawPrice(run));
            run = Offers.Redraw(run)!;
        }
        Assert.Equal(new[] { 0, 25, 50, 100 }, prices);
        Assert.True(run.FreeRedrawUsed);
        Assert.Equal(3, run.Redraws);
    }

    [Fact]
    public void ThePriceIsHeldAtTheTopRatherThanWrapping()
    {
        var run = Offered();
        run.Redraws = 40;
        Assert.Equal(int.MaxValue, Offers.RedrawPrice(run));
    }

    /// <summary>
    /// With at least an offer's worth of others left, a redraw shows none of the offer
    /// before it. Owned 0 to 6 leaves 12 to 6 not owned, 9 to 3 others besides the offer
    /// shown: always enough for three.
    /// </summary>
    [Fact]
    public void ARedrawShowsNoneOfTheOfferBeforeItWhileEnoughOthersAreLeft()
    {
        for (var owned = 0; owned <= 6; owned++)
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                var run = Owning(Instincts.All.Take(owned));
                Offers.Owe(run, 1);
                var rng = new Rng(seed);
                Offers.OpenIfOwed(run, rng);
                run.Rng = rng.State;
                var shown = run.Offer!.ToList();

                var next = Offers.Redraw(run)!;

                Assert.Equal(3, next.Offer!.Count);
                Assert.Empty(next.Offer.Intersect(shown));
                Assert.Empty(next.Offer.Intersect(run.Instincts));
            }
        }
    }

    [Fact]
    public void WithSharpInstinctsARedrawShowsNoneOfTheFourBeforeItWhileEightAreLeft()
    {
        var run = Owning(Instincts.All.Take(4), (Trait.SharpInstincts, 1));
        Offers.Owe(run, 1);
        Offers.OpenIfOwed(run, new Rng(4));
        var shown = run.Offer!.ToList();
        var next = Offers.Redraw(run)!;
        Assert.Equal(4, next.Offer!.Count);
        Assert.Empty(next.Offer.Intersect(shown));
    }

    /// <summary>Too few others left: the redraw draws from every instinct not owned, the shown ones included.</summary>
    [Fact]
    public void WithTooFewOthersLeftARedrawDrawsFromAllNotOwned()
    {
        var run = Owning(Instincts.All.Take(8));   // four left: the offer of three, one other
        Offers.Owe(run, 1);
        Offers.OpenIfOwed(run, new Rng(6));
        var next = Offers.Redraw(run)!;
        Assert.Equal(3, next.Offer!.Count);
        Assert.Subset(Instincts.All.Skip(8).ToHashSet(), next.Offer.ToHashSet());
    }

    [Fact]
    public void ARedrawLeavesTheRunGivenAsItWasAndPassesNoTurn()
    {
        var run = Offered();
        var before = Runs.Fingerprint(run);
        var next = Offers.Redraw(run)!;
        Assert.Equal(before, Runs.Fingerprint(run));
        Assert.Equal(run.Turn, next.Turn);
        Assert.Equal(run.PicksOwed, next.PicksOwed);   // a redraw pays off no pick
        Assert.Equal(run.Instincts, next.Instincts);
        Assert.NotEqual(run.Rng, next.Rng);
    }

    [Fact]
    public void ARedrawWithNoOfferIsRefused()
    {
        Assert.Null(Offers.Redraw(Runs.Fresh("Dryosaurus", 1)));
    }
}
