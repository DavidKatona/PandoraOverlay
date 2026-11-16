using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// Your dino's numbers (<see cref="Stats"/>) and the stages (<see cref="Stages"/>):
/// the design's table row by row, worked by hand, and each trait, instinct, perk and
/// star changing exactly its own numbers and nothing else.
/// </summary>
public class StatsTests
{
    private static readonly IReadOnlyCollection<Instinct> None = Array.Empty<Instinct>();

    private static SpeciesInfo Species(string name) =>
        SpeciesTable.TryGet(name, out var s) ? s : throw new ArgumentException(name);

    private static Stats At(string species, double growth, IReadOnlyDictionary<Trait, int>? ranks = null,
        int stars = 0, params Instinct[] instincts) =>
        Stats.Of(Species(species), ranks ?? Runs.NoRanks, stars, instincts, growth);

    // ---- Stages --------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(24.999, 0)]
    [InlineData(25, 1)]       // a value on a line is past it
    [InlineData(49.9, 1)]
    [InlineData(50, 2)]
    [InlineData(74.99, 2)]
    [InlineData(75, 3)]
    [InlineData(99.99, 3)]
    [InlineData(100, 4)]
    [InlineData(double.NaN, 0)]
    public void AStageIsTheLinesItIsOnOrPast(double growth, int stage)
    {
        Assert.Equal(stage, Stages.Of(growth));
    }

    // ---- The table, row by row, for one build ----------------------------------------

    /// <summary>
    /// The Tyrannosaurus (Apex: 210 health, 28 attack, 5 defense, noise +2, hunger
    /// x1.3; Crushing bite changes no number) at four growths. The scale: 0.35 at 0;
    /// 0.35 + 0.65 x 37.5 / 75 = 0.675; 1 at 75; 1.15 at 100. Health: 210 x 0.35 =
    /// 73.5, a half, up to 74; 210 x 0.675 = 141.75 -> 142; 210; 210 x 1.15 =
    /// 241.4999... in doubles (as in JavaScript) -> 241. Attack: 9.8, 18.9, 28, 32.2.
    /// </summary>
    [Theory]
    [InlineData(0, 74, 9.8)]
    [InlineData(37.5, 142, 18.9)]
    [InlineData(75, 210, 28)]
    [InlineData(100, 241, 32.2)]
    public void EveryRowOfTheTableForTheApex(double growth, int health, double attack)
    {
        var s = At("Tyrannosaurus", growth);
        Assert.Equal(health, s.MaxHealth);
        Assert.Equal(attack, s.Attack, 9);
        Assert.Equal(5, s.Defense);
        Assert.Equal(3, s.Sight);              // BaseSight 3, no build sight
        Assert.Equal(0, s.Sense);              // no Wary
        Assert.Equal(2, s.NoiseMod);
        Assert.Equal(2, s.SprintRange);
        Assert.Equal(2, s.SprintDrain, 9);
        Assert.Equal(2.08, s.FoodDrain, 9);    // 1.6 x 1.3
        Assert.Equal(2.0, s.WaterDrain, 9);
        Assert.Equal(1.4, s.GrowthRate, 9);
        Assert.Equal(0, s.Dodge, 9);
        Assert.Equal(0.08, s.RestHeal, 9);
        Assert.Equal(1, s.Meal(Food.Meat), 9);
        Assert.Equal(Diet.Meat, s.Eats);
        Assert.False(s.Hides);
        Assert.False(s.Swims);
        Assert.False(s.Flies);
        Assert.Equal(Build.Apex, s.Build.Build);
        Assert.Equal("Tyrannosaurus", s.Species.Name);
    }

    /// <summary>
    /// Halves go up (H24): a Dryosaurus hatchling has 70 x 0.35 = 24.5 health, which is
    /// 25 (.NET's own rounding would make it 24); an Elder 70 x 1.15 = 80.5 -> 81. The
    /// attack 7 x 0.35 is 2.4499... in doubles, so 2.4, as the prototype computes it.
    /// </summary>
    [Theory]
    [InlineData(0, 25, 2.4)]
    [InlineData(37.5, 47, 4.7)]
    [InlineData(75, 70, 7)]
    [InlineData(100, 81, 8)]
    public void HalvesGoUpInTheRunnersHealth(double growth, int health, double attack)
    {
        var s = At("Dryosaurus", growth);
        Assert.Equal(health, s.MaxHealth);
        Assert.Equal(attack, s.Attack, 9);
    }

    [Fact]
    public void TheScaleRunsFromAHatchlingsShareToTheEldersGain()
    {
        Assert.Equal(0.35, Stats.Scale(0), 12);
        Assert.Equal(0.675, Stats.Scale(37.5), 12);
        Assert.Equal(1, Stats.Scale(75), 12);
        Assert.Equal(1.075, Stats.Scale(87.5), 12);
        Assert.Equal(1.15, Stats.Scale(100), 12);
    }

    [Fact]
    public void HealthIsNeverUnderOne()
    {
        // No real build reaches 0, but the floor is the table's: a tiny dino still has a point.
        Assert.True(At("Pteranodon", 0).MaxHealth >= 1);
    }

    [Fact]
    public void OfARunReadsItsSnapshotItsInstinctsAndItsGrowth()
    {
        var run = Runs.Fresh("Herrerasaurus", 1).With(Runs.Ranks((Trait.ThickHide, 2)), stars: 1);
        run.Instincts.Add(Instinct.Eyes);
        run.You.Growth = 37.5;
        Assert.Equal(At("Herrerasaurus", 37.5, Runs.Ranks((Trait.ThickHide, 2)), 1, Instinct.Eyes), Stats.Of(run));
    }

    [Fact]
    public void ARunOfAnUnknownSpeciesIsADefect()
    {
        var run = Runs.Fresh("Herrerasaurus", 1);
        run.Species = "Velociraptor";
        Assert.Throws<ArgumentException>(() => Stats.Of(run));
    }

    // ---- Each trait's rank changes exactly its own numbers ---------------------------

    /// <summary>The Herrerasaurus at 75: Hunter's numbers, and Pounce changes none of them.</summary>
    private static Stats Base => At("Herrerasaurus", 75);

    private static Stats WithRank(Trait trait, int rank) => At("Herrerasaurus", 75, Runs.Ranks((trait, rank)));

    [Fact]
    public void DeepStomachSlowsTheFoodDrainEightPercentARank()
    {
        Assert.Equal(Base with { FoodDrain = 1.6 * (1 - 0.08 * 3) }, WithRank(Trait.DeepStomach, 3));
    }

    [Fact]
    public void ThickHideAddsADefenseARank()
    {
        Assert.Equal(Base with { Defense = 1 + 4 }, WithRank(Trait.ThickHide, 4));
    }

    [Fact]
    public void DrySpellsSlowsTheWaterDrainTenPercentARank()
    {
        Assert.Equal(Base with { WaterDrain = 2.0 * (1 - 0.10 * 3) }, WithRank(Trait.DrySpells, 3));
    }

    [Fact]
    public void KeenNoseAddsSightARank()
    {
        Assert.Equal(Base with { Sight = 3 + 2 }, WithRank(Trait.KeenNose, 2));
    }

    [Fact]
    public void QuickReflexesAddsSixPercentDodgeARank()
    {
        Assert.Equal(Base with { Dodge = 0.06 * 4 }, WithRank(Trait.QuickReflexes, 4));
    }

    [Fact]
    public void LightStepTakesANoiseARank()
    {
        Assert.Equal(Base with { NoiseMod = 0 - 2 }, WithRank(Trait.LightStep, 2));
    }

    [Fact]
    public void EscapeArtistAddsASprintCell()
    {
        Assert.Equal(Base with { SprintRange = 2 + 1 }, WithRank(Trait.EscapeArtist, 1));
    }

    [Fact]
    public void FastMetabolismGrowsYouSixPercentFasterARank()
    {
        Assert.Equal(Base with { GrowthRate = 1.4 * (1 + 0.06 * 5) }, WithRank(Trait.FastMetabolism, 5));
    }

    /// <summary>The other traits act on the start, the pay, the apex or the offers, never on a number here.</summary>
    [Theory]
    [InlineData(Trait.SecondWind)]
    [InlineData(Trait.StrongStart)]
    [InlineData(Trait.Legacy)]
    [InlineData(Trait.EldersPatience)]
    [InlineData(Trait.SharpInstincts)]
    [InlineData(Trait.SecondThoughts)]
    [InlineData(Trait.LateBloomer)]
    [InlineData(Trait.BornReady)]
    public void TheOtherTraitsChangeNoNumber(object trait)
    {
        Assert.Equal(Base, WithRank((Trait)trait, Lineage.Of((Trait)trait).MaxRank));
    }

    // ---- Each instinct changes exactly its own numbers -------------------------------

    private static Stats WithInstinct(Instinct instinct) => At("Herrerasaurus", 75, null, 0, instinct);

    [Fact]
    public void KeenEyesSeesOneFurther() => Assert.Equal(Base with { Sight = 4 }, WithInstinct(Instinct.Eyes));

    [Fact]
    public void SoftFeetMakesOneLessNoise() => Assert.Equal(Base with { NoiseMod = -1 }, WithInstinct(Instinct.Feet));

    [Fact]
    public void LongStrideSprintsOneFurther() => Assert.Equal(Base with { SprintRange = 3 }, WithInstinct(Instinct.Stride));

    [Fact]
    public void ThickSkinAddsTwoDefense() => Assert.Equal(Base with { Defense = 3 }, WithInstinct(Instinct.Skin));

    [Fact]
    public void FastHealerRestsForSixteenPercent() => Assert.Equal(Base with { RestHeal = 0.16 }, WithInstinct(Instinct.Healer));

    [Fact]
    public void QuickAddsFifteenPercentDodge() => Assert.Equal(Base with { Dodge = 0.15 }, WithInstinct(Instinct.Quick));

    [Fact]
    public void FierceAddsTwentyPercentAttack() => Assert.Equal(Base with { Attack = 14.4 }, WithInstinct(Instinct.Fierce));

    [Fact]
    public void HardyAddsFifteenPercentHealth() => Assert.Equal(Base with { MaxHealth = 92 }, WithInstinct(Instinct.Hardy));

    [Fact]
    public void WarySensesTwoBeyondSight()
    {
        Assert.Equal(Base with { Sense = 3 + 2 }, WithInstinct(Instinct.Wary));
        // ...and beyond the sight it has, Keen eyes and Keen nose included.
        var s = At("Herrerasaurus", 75, Runs.Ranks((Trait.KeenNose, 1)), 0, Instinct.Wary, Instinct.Eyes);
        Assert.Equal(5, s.Sight);
        Assert.Equal(7, s.Sense);
    }

    [Fact]
    public void BigAppetiteFeedsHalfAgainOfEveryMealAndChangesNothingElse()
    {
        var s = WithInstinct(Instinct.Appetite);
        Assert.Equal(Base with { BigAppetite = true }, s);
        Assert.Equal(1.5, s.Meal(Food.Plants), 9);
        Assert.Equal(1.5, s.Meal(Food.Meat), 9);
        Assert.Equal(1.5, s.Meal(Food.Fish), 9);
    }

    /// <summary>Ambusher acts on a hit, Good nose on what shows through the fog: neither is a number here.</summary>
    [Theory]
    [InlineData(Instinct.Ambush)]
    [InlineData(Instinct.Nose)]
    public void TheInstinctsOfTheMomentChangeNoNumber(object instinct)
    {
        Assert.Equal(Base, WithInstinct((Instinct)instinct));
    }

    // ---- Each stats perk (H13) changes exactly its own numbers ------------------------
    //
    // Each is held against a species of the same build whose perk changes no number,
    // so the only differences are the perk's (and the species row itself).

    [Fact]
    public void TirelessSprintsForAWalksDrain()
    {
        var dryosaurus = At("Dryosaurus", 75);
        var hypsilophodon = At("Hypsilophodon", 75);   // Freeze: no number
        Assert.Equal(1.0, dryosaurus.SprintDrain, 9);
        Assert.Equal(1.5, hypsilophodon.SprintDrain, 9);
        Assert.Equal(hypsilophodon with { Species = dryosaurus.Species, SprintDrain = 1.0 }, dryosaurus);
    }

    [Fact]
    public void HighViewSeesOneFurtherOnTopOfTheFlyersOne()
    {
        var s = At("Pteranodon", 75);
        Assert.Equal(3 + 1 + 1, s.Sight);
        Assert.Equal(3, s.SprintRange);   // the Flyer's own
        Assert.True(s.Flies);
        Assert.Equal(Diet.Meat | Diet.Fish, s.Eats);
    }

    [Fact]
    public void FleetSprintsOneFurtherForThirtyPercentLess()
    {
        var carnotaurus = At("Carnotaurus", 75);
        var allosaurus = At("Allosaurus", 75);   // Bleeder: no number
        Assert.Equal(allosaurus with { Species = carnotaurus.Species, SprintRange = 3, SprintDrain = 2 * 0.7 }, carnotaurus);
    }

    [Fact]
    public void ScavengerFeedsHalfAgainOnMeatOnly()
    {
        var ceratosaurus = At("Ceratosaurus", 75);
        Assert.Equal(At("Allosaurus", 75) with { Species = ceratosaurus.Species }, ceratosaurus);
        Assert.Equal(1.5, ceratosaurus.Meal(Food.Meat), 9);
        Assert.Equal(1, ceratosaurus.Meal(Food.Plants), 9);
        Assert.Equal(1, ceratosaurus.Meal(Food.Fish), 9);
    }

    [Fact]
    public void WaderEatsFish()
    {
        var beipiaosaurus = At("Beipiaosaurus", 75);
        var gallimimus = At("Gallimimus", 75);
        Assert.Equal(Diet.Plants | Diet.Meat | Diet.Fish, beipiaosaurus.Eats);
        Assert.Equal(gallimimus with { Species = beipiaosaurus.Species, Eats = beipiaosaurus.Eats, Dodge = 0 }, beipiaosaurus);
    }

    [Fact]
    public void EvasiveDodgesTwentyPercent()
    {
        var gallimimus = At("Gallimimus", 75);
        Assert.Equal(0.20, gallimimus.Dodge, 9);
        Assert.Equal(Diet.Plants | Diet.Meat, gallimimus.Eats);
    }

    [Fact]
    public void BigBitesFeedsHalfAgainOfEveryMeal()
    {
        var maiasaura = At("Maiasaura", 75);
        Assert.Equal(At("Pachycephalosaurus", 75) with { Species = maiasaura.Species }, maiasaura);   // Headbutt: no number
        Assert.Equal(1.5, maiasaura.Meal(Food.Plants), 9);
        // With Big appetite the two multiply.
        Assert.Equal(2.25, At("Maiasaura", 75, null, 0, Instinct.Appetite).Meal(Food.Plants), 9);
    }

    [Fact]
    public void TheBuildsDecideCoverAndWater()
    {
        Assert.True(At("Deinosuchus", 75).Swims);
        Assert.False(At("Deinosuchus", 75).Hides);
        Assert.True(At("Dryosaurus", 75).Hides);
        Assert.False(At("Triceratops", 75).Hides);
    }

    // ---- Stars and the dodge's cap ----------------------------------------------------

    [Fact]
    public void EachStarAddsFivePercentHealthAndAttackAndNothingElse()
    {
        // 80 x 1.15 = 92; 12 x 1.15 = 13.8.
        Assert.Equal(Base with { MaxHealth = 92, Attack = 13.8 }, At("Herrerasaurus", 75, null, 3));
        // The stars multiply with Hardy and Fierce: 80 x 1.05 x 1.15 = 96.6 -> 97; 12 x 1.05 x 1.2 = 15.12 -> 15.1.
        var s = At("Herrerasaurus", 75, null, 1, Instinct.Hardy, Instinct.Fierce);
        Assert.Equal(97, s.MaxHealth);
        Assert.Equal(15.1, s.Attack, 9);
    }

    [Fact]
    public void TheDodgeIsCappedAtSixtyPercent()
    {
        // Evasive 0.20 + Quick 0.15 + four ranks 0.24 = 0.59: under the cap.
        Assert.Equal(0.59, At("Gallimimus", 75, Runs.Ranks((Trait.QuickReflexes, 4)), 0, Instinct.Quick).Dodge, 9);
        // A fifth rank (beyond the lineage's, to reach past the cap) would be 0.65: held at 0.6.
        Assert.Equal(Tuning.DodgeMost, At("Gallimimus", 75, Runs.Ranks((Trait.QuickReflexes, 5)), 0, Instinct.Quick).Dodge, 9);
    }
}
