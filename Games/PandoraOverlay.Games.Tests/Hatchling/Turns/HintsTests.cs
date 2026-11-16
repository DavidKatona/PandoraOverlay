using PandoraOverlay.Games.Hatchling;
using Xunit;
using static PandoraOverlay.Games.Tests.Scenes;

namespace PandoraOverlay.Games.Tests;

/// <summary>The hints of the first runs (H23): each row of the table, in its priority.</summary>
public class HintsTests
{
    private static readonly int Here = Row5(2);

    /// <summary>A run past turn 0, fed and watered, with these creatures.</summary>
    private static RunState Fed(params Creature[] creatures) => Run(Here, creatures);

    private static HintCode For(RunState run, bool hides = true, bool eatsPlants = true, int health = 100) =>
        Hints.For(run, hides, eatsPlants, health, Sees);

    [Fact]
    public void APredatorBesideYouComesFirst()
    {
        // Beside you and hunting, with you hurt, thirsty and hungry: beside wins.
        var run = Fed(Predator(1, PredatorType.Troodon, Row5(3), alert: 4));
        run.You.Water = 10;
        run.You.Food = 10;
        Assert.Equal(HintCode.Beside, For(run, health: 20));
        Assert.Equal("It is beside you. Click it to fight, or get away.", Hints.Text(HintCode.Beside));
    }

    [Fact]
    public void APredatorHuntingYouIsNext_ByWhetherYouHide()
    {
        var run = Fed(Predator(1, PredatorType.Troodon, Row5(5), alert: 4));
        run.You.Water = 10;
        Assert.Equal(HintCode.HuntedHide, For(run, hides: true, health: 20));
        Assert.Equal(HintCode.Hunted, For(run, hides: false));
        Assert.Equal("It hunts you. Break its line of sight in forest, or outrun it.", Hints.Text(HintCode.HuntedHide));
        Assert.Equal("It hunts you. Keep your distance, or fight.", Hints.Text(HintCode.Hunted));
    }

    [Fact]
    public void OnlyAPredatorYouKnowCounts()
    {
        // Hunting you from out of sight: the hint does not give it away.
        var run = Fed(Predator(1, PredatorType.Troodon, Grid.Index(10, 0), alert: 4));
        Assert.Equal(HintCode.Grow, For(run));
        // A Known that says no for one beside you: not named either.
        var beside = Fed(Predator(1, PredatorType.Troodon, Row5(3), alert: 4));
        Assert.Equal(HintCode.Grow, Hints.For(beside, true, true, 100, (_, _) => false));
    }

    [Fact]
    public void HurtWithNoPredatorKnownMeansRest()
    {
        var run = Fed();
        run.You.Water = 10;
        Assert.Equal(HintCode.Rest, For(run, health: 49));
        Assert.Equal(HintCode.Thirst, For(run, health: 50));
        Assert.Equal("Rest to heal.", Hints.Text(HintCode.Rest));

        // A predator known, not hunting and not beside: no rest hint.
        var watched = Fed(Predator(1, PredatorType.Troodon, Row5(5), alert: 0));
        Assert.Equal(HintCode.Grow, For(watched, health: 20));
    }

    [Fact]
    public void ThirstBeforeHunger()
    {
        var run = Fed();
        run.You.Water = 39;
        run.You.Food = 10;
        Assert.Equal(HintCode.Thirst, For(run));
        run.You.Water = 40;
        Assert.Equal(HintCode.HungerPlants, For(run));
        Assert.Equal("Thirsty: stand on water or beside it, and Drink.", Hints.Text(HintCode.Thirst));
    }

    [Fact]
    public void HungerByWhatYouEat()
    {
        var run = Fed();
        run.You.Food = 39;
        Assert.Equal(HintCode.HungerPlants, For(run, eatsPlants: true));
        Assert.Equal(HintCode.HungerMeat, For(run, eatsPlants: false));
        run.You.Food = 40;
        Assert.Equal(HintCode.Grow, For(run));
        Assert.Equal("Hungry: stand on plants and Eat.", Hints.Text(HintCode.HungerPlants));
        Assert.Equal("Hungry: Eat at a carcass, or catch small game.", Hints.Text(HintCode.HungerMeat));
    }

    [Fact]
    public void TheFirstTurnThenTheGuideThenGrowing()
    {
        var run = Fed();
        run.Turn = 0;
        Assert.Equal(HintCode.First, For(run));
        run.Turn = 1;
        Assert.Equal(HintCode.Guide, For(run));   // once a run, the turn after the first move (H94)
        run.Turn = 2;
        Assert.Equal(HintCode.Grow, For(run));
        Assert.Equal("Click a lit hex to move. Predators hear noise: Sneak is quiet, Sprint is loud.", Hints.Text(HintCode.First));
        Assert.Equal("Fed and watered, you grow every turn.", Hints.Text(HintCode.Grow));
        Assert.Equal("The Guide tab explains every rule and every term.", Hints.Text(HintCode.Guide));
    }

    [Fact]
    public void NoHintOnceTheRunIsOver()
    {
        var run = Fed();
        run.Over = new RunOver { Reason = EndReason.Hunger };
        Assert.Equal(HintCode.None, For(run));
        Assert.Equal("", Hints.Text(HintCode.None));
    }

    [Fact]
    public void EveryHintHasWords()
    {
        foreach (var code in Enum.GetValues<HintCode>().Where(c => c != HintCode.None))
            Assert.False(string.IsNullOrEmpty(Hints.Text(code)), code.ToString());
    }
}
