namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Your dino's numbers at one moment of a run: what every rule, the bot, the board
/// and Camp's cards read. A pure function of the species, the lineage's ranks and
/// the species' stars as they were when the run began (H19), the instincts picked
/// and the growth, so nothing is cached: it is cheap, and a cached copy could only
/// ever be stale.
///
/// <para>The formulas are the design's table, one by one, with the build's adult
/// numbers scaled by growth. The prototype's <c>hxStats</c> is their ancestor, but
/// not their source: its flat species bonuses became one perk each (H13), its trait
/// steps were repriced (H11), and the instincts are new (H14).</para>
/// </summary>
/// <param name="MaxHealth">Full health in points; your health is a fraction of it, so a change here keeps the fraction.</param>
/// <param name="Attack">Your blow before its swing and the target's armour, to one decimal.</param>
/// <param name="Defense">Points of armour against a bite.</param>
/// <param name="Sight">Cells you see, counted as steps from you.</param>
/// <param name="Sense">With Wary, predators are known this far away, out of sight; 0 without it.</param>
/// <param name="NoiseMod">Added to every move's noise.</param>
/// <param name="SprintRange">Cells a sprint can cover.</param>
/// <param name="SprintDrain">A sprint costs this many times a turn's food and water.</param>
/// <param name="FoodDrain">Food a turn costs.</param>
/// <param name="WaterDrain">Water a turn costs.</param>
/// <param name="GrowthRate">Growth a turn, in percent, while fed and watered.</param>
/// <param name="Dodge">The chance to dodge a bite, capped at <see cref="Tuning.DodgeMost"/>.</param>
/// <param name="RestHeal">A rest heals this share of full health.</param>
/// <param name="Eats">The foods you can eat: the build's, plus fish for a Wader.</param>
/// <param name="Hides">Forest is cover for you (the build's).</param>
/// <param name="Swims">Water is cover for you, you sprint only in water, and you never wade (the build's).</param>
/// <param name="Flies">Rock and water are open to you, and you never wade (the build's).</param>
/// <param name="Species">The species' row: its perk is asked by the rules that read one.</param>
/// <param name="Build">The build's row.</param>
internal sealed record Stats(int MaxHealth, double Attack, int Defense, int Sight, int Sense, int NoiseMod,
    int SprintRange, double SprintDrain, double FoodDrain, double WaterDrain, double GrowthRate, double Dodge,
    double RestHeal, Diet Eats, bool Hides, bool Swims, bool Flies, SpeciesInfo Species, BuildInfo Build)
{
    /// <summary>
    /// Big appetite is picked: the one instinct <see cref="Meal"/> needs that no
    /// other number carries. Kept beside the table's numbers rather than in them,
    /// because the meal's factor depends on the food, which only the meal knows.
    /// </summary>
    public bool BigAppetite { get; init; }

    /// <summary>
    /// What a meal of <paramref name="kind"/> is multiplied by: half again for Big
    /// appetite (H14), half again for Big bites, and for Scavenger half again on
    /// meat only (H13). They multiply: a Maiasaura with Big appetite eats 2.25 times
    /// a plain meal, as the design's table writes it.
    /// </summary>
    public double Meal(Food kind) =>
        (BigAppetite ? Tuning.BigAppetiteMeal : 1)
        * (Species.Perk == Perk.BigBites ? Tuning.BigBitesMeal : 1)
        * (Species.Perk == Perk.Scavenger && kind == Food.Meat ? Tuning.ScavengerMeal : 1);

    /// <summary>
    /// The numbers of the run's dino now: its species, its snapshot of the lineage
    /// and the stars (H19), its instincts and its growth. A species the table does
    /// not know throws: a run is only ever made from the table, and a save holding
    /// another is refused when it loads, so meeting one here is a defect.
    /// </summary>
    public static Stats Of(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!SpeciesTable.TryGet(run.Species, out var species))
            throw new ArgumentException("The run's species is not in the table.", nameof(run));
        return Of(species, run.Ranks, run.Stars, run.Instincts, run.You.Growth);
    }

    /// <summary>
    /// The numbers of <paramref name="species"/> at <paramref name="growth"/> (a
    /// percent) with the lineage's <paramref name="ranks"/> (a missing trait is rank
    /// 0), its <paramref name="stars"/> and the <paramref name="instincts"/> picked.
    /// Camp shows a species' card from this at 75 with no instinct; a run asks it
    /// through <see cref="Of(RunState)"/>.
    /// </summary>
    public static Stats Of(SpeciesInfo species, IReadOnlyDictionary<Trait, int> ranks, int stars,
        IReadOnlyCollection<Instinct> instincts, double growth)
    {
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(ranks);
        ArgumentNullException.ThrowIfNull(instincts);

        var build = Builds.Of(species.Build);
        var perk = species.Perk;
        int Rank(Trait trait) => ranks.TryGetValue(trait, out var rank) ? rank : 0;
        bool Has(Instinct instinct) => instincts.Contains(instinct);

        var scale = Scale(growth);
        var starFactor = 1 + Tuning.StarBonus * stars;

        var maxHealth = (int)Math.Max(1, Tuning.Round(
            build.Health * scale * starFactor * (Has(Instinct.Hardy) ? Tuning.HardyHealth : 1)));
        var attack = Tuning.Round1(
            build.Attack * scale * starFactor * (Has(Instinct.Fierce) ? Tuning.FierceAttack : 1));
        var defense = build.Defense
            + Tuning.ThickHideStep * Rank(Trait.ThickHide)
            + (Has(Instinct.Skin) ? Tuning.ThickSkinDefense : 0);
        var sight = Tuning.BaseSight + build.Sight
            + Tuning.KeenNoseStep * Rank(Trait.KeenNose)
            + (Has(Instinct.Eyes) ? Tuning.KeenEyesSight : 0)
            + (perk == Perk.HighView ? Tuning.HighViewSight : 0);
        // Wary reaches beyond sight; without it there is no sense at all, not "sense = sight".
        var sense = Has(Instinct.Wary) ? sight + Tuning.WarySense : 0;
        var noise = build.Noise
            - Tuning.LightStepStep * Rank(Trait.LightStep)
            - (Has(Instinct.Feet) ? Tuning.SoftFeetNoise : 0);
        var sprintRange = build.SprintRange
            + Tuning.EscapeArtistStep * Rank(Trait.EscapeArtist)
            + (Has(Instinct.Stride) ? Tuning.LongStrideRange : 0)
            + (perk == Perk.Fleet ? Tuning.FleetRange : 0);
        // Tireless replaces the drain outright (a sprint costs a walk's), Fleet shrinks the build's.
        var sprintDrain = perk == Perk.Tireless
            ? Tuning.TirelessDrain
            : build.SprintDrain * (perk == Perk.Fleet ? Tuning.FleetDrain : 1);
        var foodDrain = Tuning.FoodPerTurn * build.Hunger * (1 - Tuning.DeepStomachStep * Rank(Trait.DeepStomach));
        var waterDrain = Tuning.WaterPerTurn * (1 - Tuning.DrySpellsStep * Rank(Trait.DrySpells));
        var growthRate = Tuning.GrowthPerTurn * (1 + Tuning.FastMetabolismStep * Rank(Trait.FastMetabolism));
        var dodge = Math.Min(Tuning.DodgeMost,
            Tuning.QuickReflexesStep * Rank(Trait.QuickReflexes)
            + (Has(Instinct.Quick) ? Tuning.QuickDodge : 0)
            + (perk == Perk.Evasive ? Tuning.EvasiveDodge : 0));
        var restHeal = Has(Instinct.Healer) ? Tuning.FastHealerHeal : Tuning.RestHeal;
        var eats = build.Eats | (perk == Perk.Wader ? Diet.Fish : Diet.None);

        return new Stats(maxHealth, attack, defense, sight, sense, noise, sprintRange, sprintDrain, foodDrain,
            waterDrain, growthRate, dodge, restHeal, eats, build.Hides, build.Swims, build.Flies, species, build)
        {
            BigAppetite = Has(Instinct.Appetite),
        };
    }

    /// <summary>
    /// A dino's size by growth: from a hatchling's share at 0 up to the adult's
    /// numbers (1) at 75, then on to the Elder's gain at 100. Health and attack are
    /// the build's adult numbers times this.
    /// </summary>
    public static double Scale(double growth) =>
        growth < Tuning.ScaleAdultAt
            ? Tuning.ScaleHatchling + (1 - Tuning.ScaleHatchling) * growth / Tuning.ScaleAdultAt
            : 1 + Tuning.ScaleElderGain * (growth - Tuning.ScaleAdultAt) / (Tuning.GrowthMost - Tuning.ScaleAdultAt);
}
