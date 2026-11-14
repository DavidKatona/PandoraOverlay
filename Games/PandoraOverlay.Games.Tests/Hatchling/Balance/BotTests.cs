using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.Sweeps;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The bot knows only what the player may know (H32): what it cannot know changes
/// no choice, and what the player's board shows through the fog (H39) does. If the
/// bot read more than the player, every balance number would be the number of a
/// game nobody plays.
/// </summary>
public class BotTests
{
    /// <summary>
    /// Every state the bot meets in runs of every species, fresh and full, each
    /// handed to <paramref name="check"/>: a few thousand states with the island's
    /// creatures and food where play left them.
    /// </summary>
    private static void EveryState(Action<RunState> check, int seeds = 3)
    {
        foreach (var s in SpeciesTable.All)
        foreach (var ranks in new[] { Fresh, Full })
        for (var k = 0; k < seeds; k++)
        {
            var run = Island.NewRun(new RunSetup(s.Name, 1, ranks, 0), SeedOf(k));
            while (run.Over is null && run.Turn < Bot.TurnLimit)
            {
                check(run);
                var choice = Bot.Choose(run);
                if (choice.Pace is { } pace && pace != run.Pace)
                    run = TurnRules.Apply(run, new PlayerAction.SetPace(pace))!.After;
                run = (TurnRules.Apply(run, choice.Action) ?? TurnRules.Apply(run, new PlayerAction.Rest())!).After;
            }
        }
    }

    [Fact]
    public void HidingAPredatorThePlayerDoesNotKnowChangesNoChoice()
    {
        var states = 0;
        var hidden = 0;
        EveryState(run =>
        {
            var stats = Stats.Of(run);
            var unknown = run.Creatures.Where(c => c.Kind == CreatureKind.Predator && !Sight.Knows(run, stats, c)).ToList();
            states++;
            if (unknown.Count == 0) return;
            hidden++;
            var without = run.Clone();
            without.Creatures.RemoveAll(c => unknown.Any(u => u.Id == c.Id));
            Assert.Equal(Bot.Choose(run), Bot.Choose(without));
        });
        Assert.True(hidden > states / 4, $"only {hidden} of {states} states had a predator to hide");
    }

    [Fact]
    public void FoodOnACellNeverSeenChangesNoChoiceWithoutANose()
    {
        var changed = 0;
        EveryState(run =>
        {
            if (run.Has(Instinct.Nose) || Stats.Of(run).Species.Perk == Perk.Scavenger) return;
            var fed = run.Clone();
            for (var cell = 0; cell < Tuning.Cells; cell++)
            {
                if (fed.Seen[cell]) continue;
                fed.Plants[cell] = true;
                fed.Meat[cell] = Tuning.CarcassMeals;
                changed++;
            }
            Assert.Equal(Bot.Choose(run), Bot.Choose(fed));
        }, seeds: 2);
        Assert.True(changed > 0);
    }

    /// <summary>
    /// A bare board: no creature, no food anywhere, you hungry and well watered, so
    /// the bot can only explore, toward the nearest cell not yet seen. Then food you
    /// eat on a cell never seen, further on another way: with a nose the bot goes for
    /// it, without one it does not see it.
    /// </summary>
    private static (RunState Run, int Cell, PlayerAction Explore) Hungry(string species)
    {
        for (var k = 0; k < 50; k++)
        {
            var run = Runs.Fresh(species, SeedOf(k)).Clear();
            Array.Fill(run.Plants, false);
            Array.Fill(run.Meat, 0);
            run.You.Food = 30;
            run.You.Water = 95;
            var explore = Bot.Choose(run).Action;
            if (explore is not PlayerAction.Move move) continue;
            var you = run.You.At;
            // An unseen land cell the exploring step does not bring nearer.
            for (var cell = 0; cell < Tuning.Cells; cell++)
            {
                if (run.Seen[cell] || !Island.IsLand(run, cell)) continue;
                if (Island.Grid.Distance(move.To, cell) >= Island.Grid.Distance(you, cell))
                    return (run, cell, explore);
            }
        }
        throw new InvalidOperationException("no island had such a cell");
    }

    private static void AssertGoesFor(RunState run, int cell)
    {
        var move = Assert.IsType<PlayerAction.Move>(Bot.Choose(run).Action);
        Assert.True(Island.Grid.Distance(move.To, cell) < Island.Grid.Distance(run.You.At, cell),
            $"the bot stepped to {move.To}, no nearer to the food at {cell}");
    }

    [Fact]
    public void APlantNeverSeenIsAGoalWithGoodNoseOnly()
    {
        var (run, cell, explore) = Hungry("Dryosaurus");
        run.Plants[cell] = true;
        Assert.Equal(explore, Bot.Choose(run).Action);   // not on the board: nothing changes

        run.Instincts.Add(Instinct.Nose);
        Assert.True(Sight.ShowsFood(run, Stats.Of(run), cell, Food.Plants));
        AssertGoesFor(run, cell);
    }

    [Fact]
    public void ACarcassNeverSeenIsAGoalWithScavengerOrGoodNose()
    {
        var (herrera, cell, explore) = Hungry("Herrerasaurus");
        herrera.Meat[cell] = Tuning.CarcassMeals;
        Assert.Equal(explore, Bot.Choose(herrera).Action);
        herrera.Instincts.Add(Instinct.Nose);
        AssertGoesFor(herrera, cell);

        var (cerato, carcass, _) = Hungry("Ceratosaurus");
        cerato.Meat[carcass] = Tuning.CarcassMeals;
        AssertGoesFor(cerato, carcass);
    }

    [Fact]
    public void ChoosingChangesNothing()
    {
        EveryState(run =>
        {
            var before = Runs.Fingerprint(run);
            Bot.Choose(run);
            Assert.Equal(before, Runs.Fingerprint(run));
        }, seeds: 1);
    }

    [Fact]
    public void AnOpenOfferTakesTheFirstChoice()
    {
        var run = Runs.Fresh("Dryosaurus", SeedOf(0), Runs.Ranks((Trait.SharpInstincts, 1), (Trait.SecondThoughts, 1),
            (Trait.LateBloomer, 1), (Trait.BornReady, 1)), 0);
        Assert.NotNull(run.Offer);
        Assert.Equal(new Bot.Choice(null, new PlayerAction.Pick(0)), Bot.Choose(run));
    }

    [Fact]
    public void ACareerBuysTheCheapestRankAfterEachRun()
    {
        var start = GameState.Fresh(ProgressSamples.Moment);
        var career = Bot.Career(start, Careers.Starters, 1, most: 3);
        Assert.Equal(3, career.Runs.Count);
        Assert.Equal(new[] { "Dryosaurus", "Herrerasaurus", "Dryosaurus" }, career.Runs.Select(r => r.Species));
        // Nothing is left that the wallet affords: the bot bought until it could not.
        Assert.DoesNotContain(Lineage.All, t => LineageRules.CanBuy(career.Final, t.Trait));
        Assert.Equal(career.Final.Wallet.Earned - career.Final.Wallet.Balance, career.Runs[^1].Spent);
        Assert.Equal(3, career.Final.Totals.Runs);
        Assert.Null(career.Final.Run);
    }
}
