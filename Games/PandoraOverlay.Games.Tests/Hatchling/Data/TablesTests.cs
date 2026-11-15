using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The game's tables, pinned to the design: the builds' numbers, the 21 species
/// and their perks (H13), the predators and who hunts whom, the five dangers
/// (H15), the twelve instincts (H14) and the lineage (H11). Every later rule reads
/// these; a number that moves here moves every run, so each one is pinned.
/// </summary>
public class TablesTests
{
    // ---- Builds --------------------------------------------------------------------

    [Theory]
    //          build            hp   atk def eats                       noise drain range hides  swims  flies  sight hunger size               big    threat
    [InlineData(Build.Runner,     70,  7, 1, Diet.Plants,                -1,   1.5,  2,    true,  false, false, 0,    1.0,   SizeClass.Small,   false, 0.84)]
    [InlineData(Build.Grazer,    110, 11, 3, Diet.Plants,                 0,   2.0,  2,    true,  false, false, 0,    1.0,   SizeClass.Medium,  false, 1.05)]
    [InlineData(Build.Tank,      170, 16, 6, Diet.Plants,                 1,   2.5,  2,    false, false, false, 0,    1.0,   SizeClass.Large,   true,  1.16)]
    [InlineData(Build.Hunter,     80, 12, 1, Diet.Meat,                   0,   1.5,  2,    true,  false, false, 0,    1.0,   SizeClass.Small,   false, 0.98)]
    [InlineData(Build.Stalker,   130, 18, 3, Diet.Meat,                   0,   2.0,  2,    true,  false, false, 0,    1.0,   SizeClass.Medium,  true,  1.30)]
    [InlineData(Build.Apex,      210, 28, 5, Diet.Meat,                   2,   2.0,  2,    false, false, false, 0,    1.3,   SizeClass.Large,   true,  1.43)]
    [InlineData(Build.Swimmer,   160, 24, 4, Diet.Meat | Diet.Fish,       0,   2.0,  2,    false, true,  false, 0,    1.0,   SizeClass.Large,   true,  1.85)]
    [InlineData(Build.Flyer,      55,  8, 0, Diet.Meat | Diet.Fish,       0,   1.5,  3,    false, false, true,  1,    1.0,   SizeClass.Small,   false, 0.98)]
    [InlineData(Build.Forager,    75,  8, 1, Diet.Plants | Diet.Meat,    -1,   1.5,  2,    true,  false, false, 0,    1.0,   SizeClass.Small,   false, 0.94)]
    public void EachBuildHasThePlansNumbers(object build, int health, double attack, int defense, object eats, int noise,
        double drain, int range, bool hides, bool swims, bool flies, int sight, double hunger, object size, bool big, double threat)
    {
        // The game's enums are internal, so a public test method takes them as object.
        Assert.Equal(new BuildInfo((Build)build, health, attack, defense, (Diet)eats, noise, drain, range, hides, swims, flies, sight,
            hunger, (SizeClass)size, big, threat), Builds.Of((Build)build));
    }

    [Fact]
    public void TheBuildsAreInTheEnumsOrderAndOfThrowsForANonBuild()
    {
        Assert.Equal(Enum.GetValues<Build>(), Builds.All.Select(b => b.Build));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builds.Of((Build)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builds.Of((Build)(-1)));
    }

    // ---- Species -------------------------------------------------------------------

    [Fact]
    public void ThereAre21SpeciesEachWithItsOwnPerk()
    {
        Assert.Equal(21, SpeciesTable.All.Count);
        Assert.Equal(21, SpeciesTable.All.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());
        // Each perk used exactly once (H13), in the perk enum's order.
        Assert.Equal(Enum.GetValues<Perk>(), SpeciesTable.All.Select(s => s.Perk));
    }

    [Theory]
    [InlineData("Dryosaurus", Build.Runner, Perk.Tireless, 1.10)]
    [InlineData("Hypsilophodon", Build.Runner, Perk.Freeze, 1.09)]
    [InlineData("Kentrosaurus", Build.Grazer, Perk.Spikes, 1.10)]
    [InlineData("Pachycephalosaurus", Build.Grazer, Perk.Headbutt, 1.06)]
    [InlineData("Tenontosaurus", Build.Grazer, Perk.Braced, 1.11)]
    [InlineData("Diabloceratops", Build.Grazer, Perk.Charge, 1.05)]
    [InlineData("Maiasaura", Build.Grazer, Perk.BigBites, 0.98)]
    [InlineData("Stegosaurus", Build.Tank, Perk.Thagomizer, 1.05)]
    [InlineData("Triceratops", Build.Tank, Perk.Frill, 1.05)]
    [InlineData("Pteranodon", Build.Flyer, Perk.HighView, 1.35)]
    [InlineData("Troodon", Build.Hunter, Perk.Venom, 1.07)]
    [InlineData("Herrerasaurus", Build.Hunter, Perk.Pounce, 1.09)]
    [InlineData("Omniraptor", Build.Hunter, Perk.Latch, 1.11)]
    [InlineData("Carnotaurus", Build.Stalker, Perk.Fleet, 1.15)]
    [InlineData("Ceratosaurus", Build.Stalker, Perk.Scavenger, 1.08)]
    [InlineData("Dilophosaurus", Build.Hunter, Perk.Spit, 1.10)]
    [InlineData("Deinosuchus", Build.Swimmer, Perk.Lunge, 1.10)]
    [InlineData("Allosaurus", Build.Stalker, Perk.Bleeder, 1.15)]
    [InlineData("Tyrannosaurus", Build.Apex, Perk.CrushingBite, 1.04)]
    [InlineData("Beipiaosaurus", Build.Forager, Perk.Wader, 1.21)]
    [InlineData("Gallimimus", Build.Forager, Perk.Evasive, 1.20)]
    public void EachSpeciesHasThePlansBuildAndPerk(string name, object build, object perk, double threat)
    {
        Assert.True(SpeciesTable.TryGet(name, out var species));
        Assert.Equal(new SpeciesInfo(name, (Build)build, (Perk)perk, threat), species);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Dryosaurus ")]
    [InlineData("Spinosaurus")]     // a species the game does not know is ignored, never an error
    public void AnUnknownSpeciesIsNotFound(string? name)
    {
        Assert.False(SpeciesTable.TryGet(name, out _));
    }

    /// <summary>H36: a reading that differs from the table only in case still finds its species, in the table's spelling.</summary>
    [Theory]
    [InlineData("Tyrannosaurus")]
    [InlineData("tyrannosaurus")]
    [InlineData("TYRANNOSAURUS")]
    public void ASpeciesIsFoundIgnoringCaseAndCarriesTheTablesSpelling(string name)
    {
        Assert.True(SpeciesTable.TryGet(name, out var species));
        Assert.Equal("Tyrannosaurus", species.Name);
    }

    [Fact]
    public void TheStartersAndTheNeverAskedAreInTheTable()
    {
        Assert.Equal(new[] { "Dryosaurus", "Herrerasaurus" }, SpeciesTable.Starters);
        Assert.Equal(new[] { "Tyrannosaurus", "Deinosuchus" }, SpeciesTable.JournalNever);
        Assert.All(SpeciesTable.Starters.Concat(SpeciesTable.JournalNever), n => Assert.True(SpeciesTable.TryGet(n, out _)));
    }

    // ---- Predators -----------------------------------------------------------------

    [Theory]
    [InlineData(PredatorType.Troodon, 18, 5, 0, true, 2, 1)]
    [InlineData(PredatorType.Herrerasaurus, 40, 9, 1, true, 2, 2)]
    [InlineData(PredatorType.Carnotaurus, 75, 15, 2, true, 3, 2)]
    [InlineData(PredatorType.Allosaurus, 115, 20, 3, false, 3, 3)]
    [InlineData(PredatorType.Tyrannosaurus, 230, 30, 5, false, 3, 4)]
    public void EachPredatorHasThePlansNumbers(object type, int health, int attack, int defense, bool fast, int sight, int meat)
    {
        Assert.Equal(new PredatorInfo((PredatorType)type, health, attack, defense, fast, sight, meat), Predators.Of((PredatorType)type));
    }

    [Fact]
    public void EveryPredatorIsNamedAsTheSpeciesTableSpellsIt()
    {
        foreach (var type in Enum.GetValues<PredatorType>())
            Assert.True(SpeciesTable.TryGet(Predators.NameOf(type), out _), type.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => Predators.NameOf((PredatorType)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Predators.Of((PredatorType)5));
    }

    /// <summary>The fifteen cells of "who hunts whom", in their order (an arrival is a pick of the list, so the order is part of a seeded run).</summary>
    [Theory]
    [InlineData(SizeClass.Small, 0, "Troodon")]
    [InlineData(SizeClass.Small, 1, "Troodon,Herrerasaurus")]
    [InlineData(SizeClass.Small, 2, "Herrerasaurus,Carnotaurus")]
    [InlineData(SizeClass.Small, 3, "Carnotaurus,Allosaurus")]
    [InlineData(SizeClass.Small, 4, "Allosaurus")]
    [InlineData(SizeClass.Medium, 0, "Troodon,Herrerasaurus")]
    [InlineData(SizeClass.Medium, 1, "Herrerasaurus")]
    [InlineData(SizeClass.Medium, 2, "Herrerasaurus,Carnotaurus")]
    [InlineData(SizeClass.Medium, 3, "Carnotaurus,Allosaurus")]
    [InlineData(SizeClass.Medium, 4, "Allosaurus")]
    [InlineData(SizeClass.Large, 0, "Herrerasaurus")]
    [InlineData(SizeClass.Large, 1, "Herrerasaurus,Carnotaurus")]
    [InlineData(SizeClass.Large, 2, "Carnotaurus,Allosaurus")]
    [InlineData(SizeClass.Large, 3, "Allosaurus")]
    [InlineData(SizeClass.Large, 4, "Allosaurus")]
    public void ThePoolsAreThePlans(object size, int stage, string pool)
    {
        Assert.Equal(pool, string.Join(",", Predators.Pool((SizeClass)size, stage)));
    }

    [Fact]
    public void TheApexIsInNoPoolAndAStageOutsideTheFiveThrows()
    {
        foreach (var size in Enum.GetValues<SizeClass>())
            for (var stage = 0; stage <= 4; stage++)
                Assert.DoesNotContain(PredatorType.Tyrannosaurus, Predators.Pool(size, stage));
        Assert.Throws<ArgumentOutOfRangeException>(() => Predators.Pool(SizeClass.Small, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Predators.Pool(SizeClass.Small, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Predators.Pool((SizeClass)3, 0));
    }

    [Fact]
    public void EachSizeClassIsTheBuildsThePlanNames()
    {
        Assert.Equal(new[] { Build.Runner, Build.Hunter, Build.Flyer, Build.Forager },
            Builds.All.Where(b => b.Size == SizeClass.Small).Select(b => b.Build));
        Assert.Equal(new[] { Build.Grazer, Build.Stalker }, Builds.All.Where(b => b.Size == SizeClass.Medium).Select(b => b.Build));
        Assert.Equal(new[] { Build.Tank, Build.Apex, Build.Swimmer }, Builds.All.Where(b => b.Size == SizeClass.Large).Select(b => b.Build));
        Assert.Equal(new[] { Build.Tank, Build.Stalker, Build.Apex, Build.Swimmer }, Builds.All.Where(b => b.Big).Select(b => b.Build));
    }

    // ---- Dangers -------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1.00, 7, 2, 5, 15, 1.00)]
    [InlineData(2, 1.30, 6, 2, 5, 15, 1.25)]
    [InlineData(3, 1.40, 6, 3, 6, 17, 1.50)]
    [InlineData(4, 1.60, 5, 3, 6, 19, 1.75)]
    [InlineData(5, 2.00, 5, 4, 7, 25, 2.00)]
    public void EachDangerHasThePlansNumbers(int level, double threat, int every, int mostBase, int mostCap, int outlast, double pay)
    {
        Assert.Equal(new DangerInfo(level, threat, every, mostBase, mostCap, outlast, pay), Dangers.Of(level));
    }

    [Theory]
    [InlineData(1, "2,3,4,5,5")]
    [InlineData(2, "2,3,4,5,5")]
    [InlineData(3, "3,4,5,6,6")]
    [InlineData(4, "3,4,5,6,6")]
    [InlineData(5, "4,5,6,7,7")]
    public void TheMostPredatorsByStageIsBaseAndStageUpToTheCap(int level, string byStage)
    {
        Assert.Equal(byStage, string.Join(",", Enumerable.Range(0, 5).Select(Dangers.Of(level).Most)));
    }

    [Fact]
    public void ThereAreFiveDangersAndNoOther()
    {
        Assert.Equal(5, Dangers.Highest);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Dangers.All.Select(d => d.Level));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dangers.Of(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dangers.Of(6));
    }

    // ---- Instincts -----------------------------------------------------------------

    [Fact]
    public void TwelveInstinctsWithTheirIdsInTheTablesOrder()
    {
        Assert.Equal(12, Instincts.All.Count);
        Assert.Equal(Enum.GetValues<Instinct>(), Instincts.All);
        Assert.Equal(
            new[] { "eyes", "feet", "stride", "skin", "ambush", "appetite", "healer", "quick", "nose", "fierce", "wary", "hardy" },
            Instincts.All.Select(Instincts.IdOf));
    }

    [Fact]
    public void AnInstinctsIdRoundTripsAndAnUnknownOneDoesNot()
    {
        foreach (var instinct in Instincts.All)
        {
            Assert.True(Instincts.TryParse(Instincts.IdOf(instinct), out var back));
            Assert.Equal(instinct, back);
        }
        Assert.False(Instincts.TryParse("Eyes", out _));
        Assert.False(Instincts.TryParse("", out _));
        Assert.False(Instincts.TryParse(null, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => Instincts.IdOf((Instinct)12));
    }

    // ---- The lineage (H11) ---------------------------------------------------------

    [Theory]
    [InlineData(Trait.DeepStomach, "stomach", 0, 1, 5, 60)]
    [InlineData(Trait.ThickHide, "hide", 0, 2, 4, 200)]
    [InlineData(Trait.DrySpells, "dry", 0, 3, 3, 800)]
    [InlineData(Trait.SecondWind, "wind", 0, 4, 1, 4000)]
    [InlineData(Trait.KeenNose, "nose", 1, 1, 2, 60)]
    [InlineData(Trait.QuickReflexes, "reflexes", 1, 2, 4, 200)]
    [InlineData(Trait.LightStep, "step", 1, 3, 2, 800)]
    [InlineData(Trait.EscapeArtist, "escape", 1, 4, 1, 4000)]
    [InlineData(Trait.FastMetabolism, "metabolism", 2, 1, 5, 60)]
    [InlineData(Trait.StrongStart, "start", 2, 2, 3, 200)]
    [InlineData(Trait.Legacy, "legacy", 2, 3, 3, 800)]
    [InlineData(Trait.EldersPatience, "patience", 2, 4, 1, 4000)]
    [InlineData(Trait.SharpInstincts, "sharp", 3, 1, 1, 500)]
    [InlineData(Trait.SecondThoughts, "thoughts", 3, 2, 1, 1500)]
    [InlineData(Trait.LateBloomer, "bloomer", 3, 3, 1, 3000)]
    [InlineData(Trait.BornReady, "ready", 3, 4, 1, 6000)]
    public void EachTraitHasThePlansPlaceRanksAndPrice(object trait, string id, int branch, int tier, int maxRank, int price)
    {
        Assert.Equal(new TraitInfo((Trait)trait, id, branch, tier, maxRank, price), Lineage.Of((Trait)trait));
    }

    [Fact]
    public void SixteenTraitsBranchByBranchTierByTier()
    {
        Assert.Equal(16, Lineage.All.Count);
        Assert.Equal(Enum.GetValues<Trait>(), Lineage.All.Select(t => t.Trait));
        for (var i = 0; i < Lineage.All.Count; i++)
        {
            Assert.Equal(i / Lineage.Tiers, Lineage.All[i].Branch);
            Assert.Equal(i % Lineage.Tiers + 1, Lineage.All[i].Tier);
        }
    }

    [Fact]
    public void TheLineageHas38RanksAndCostsItsWholePrice()
    {
        Assert.Equal(38, Lineage.All.Sum(t => t.MaxRank));
        // Rank r costs Price * r: a trait to its last rank is Price * (1 + 2 + ... + MaxRank).
        var sum = Lineage.All.Sum(t => Enumerable.Range(1, t.MaxRank).Sum(r => t.Price * r));
        Assert.Equal(42180, sum);
        Assert.Equal(Lineage.WholePrice, sum);
    }

    [Fact]
    public void ATraitsIdsAreUniqueAndRoundTrip()
    {
        Assert.Equal(16, Lineage.All.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var info in Lineage.All)
        {
            Assert.True(Lineage.TryParse(info.Id, out var back));
            Assert.Equal(info.Trait, back);
        }
        Assert.False(Lineage.TryParse("Stomach", out _));
        Assert.False(Lineage.TryParse("DeepStomach", out _));
        Assert.False(Lineage.TryParse(null, out _));
    }

    [Fact]
    public void EveryTraitButAFirstTierNeedsTheOneATierAboveInItsBranch()
    {
        foreach (var info in Lineage.All)
        {
            var above = Lineage.Above(info.Trait);
            if (info.Tier == 1)
            {
                Assert.Null(above);
                continue;
            }
            Assert.NotNull(above);
            Assert.Equal(info.Branch, above!.Branch);
            Assert.Equal(info.Tier - 1, above.Tier);
        }
        Assert.Equal(Trait.DrySpells, Lineage.Above(Trait.SecondWind)!.Trait);
        Assert.Equal(Trait.SharpInstincts, Lineage.Above(Trait.SecondThoughts)!.Trait);
        Assert.Throws<ArgumentOutOfRangeException>(() => Lineage.Of((Trait)16));
    }
}
