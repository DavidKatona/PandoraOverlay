namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The lineage's sixteen traits: what is kept between runs, in four branches of
/// four tiers, 38 ranks (H11). Branch by branch (Survival, Senses, Growth,
/// Instinct), tier by tier.
/// </summary>
internal enum Trait
{
    DeepStomach, ThickHide, DrySpells, SecondWind,
    KeenNose, QuickReflexes, LightStep, EscapeArtist,
    FastMetabolism, StrongStart, Legacy, EldersPatience,
    SharpInstincts, SecondThoughts, LateBloomer, BornReady,
}

/// <summary>A trait's place and price.</summary>
/// <param name="Id">What a save writes: stable for ever.</param>
/// <param name="Branch">0 Survival, 1 Senses, 2 Growth, 3 Instinct (<c>HatchlingText.BranchName</c>).</param>
/// <param name="Tier">1 to 4, as the design counts them: a trait needs the one a tier above it at rank 1.</param>
/// <param name="MaxRank">Ranks it can be bought to.</param>
/// <param name="Price">The tier's base: rank r costs Price * r.</param>
internal sealed record TraitInfo(Trait Trait, string Id, int Branch, int Tier, int MaxRank, int Price);

/// <summary>The lineage's table (H11): ranks and prices, priced for weeks of play, not an evening.</summary>
internal static class Lineage
{
    /// <summary>Branches in the lineage.</summary>
    public const int Branches = 4;

    /// <summary>Tiers in a branch.</summary>
    public const int Tiers = 4;

    /// <summary>The branch whose traits change the instincts; its prices are its own (<see cref="Tuning.InstinctTierPrice"/>).</summary>
    public const int InstinctBranch = 3;

    /// <summary>What the whole lineage costs, every rank of every trait: a test holds it equal to the table's sum (H11).</summary>
    public const int WholePrice = 42180;

    /// <summary>The sixteen, branch by branch, tier by tier: in <see cref="Trait"/>'s order, so <c>All[(int)trait]</c> is its row.</summary>
    public static IReadOnlyList<TraitInfo> All { get; } = new[]
    {
        Row(Trait.DeepStomach, "stomach", 0, 1, 5),
        Row(Trait.ThickHide, "hide", 0, 2, 4),
        Row(Trait.DrySpells, "dry", 0, 3, 3),
        Row(Trait.SecondWind, "wind", 0, 4, 1),

        Row(Trait.KeenNose, "nose", 1, 1, 2),
        Row(Trait.QuickReflexes, "reflexes", 1, 2, 4),
        Row(Trait.LightStep, "step", 1, 3, 2),
        Row(Trait.EscapeArtist, "escape", 1, 4, 1),

        Row(Trait.FastMetabolism, "metabolism", 2, 1, 5),
        Row(Trait.StrongStart, "start", 2, 2, 3),
        Row(Trait.Legacy, "legacy", 2, 3, 3),
        Row(Trait.EldersPatience, "patience", 2, 4, 1),

        Row(Trait.SharpInstincts, "sharp", 3, 1, 1),
        Row(Trait.SecondThoughts, "thoughts", 3, 2, 1),
        Row(Trait.LateBloomer, "bloomer", 3, 3, 1),
        Row(Trait.BornReady, "ready", 3, 4, 1),
    };

    // The price comes from Tuning by the branch and the tier, so a price has one home.
    private static TraitInfo Row(Trait trait, string id, int branch, int tier, int maxRank) =>
        new(trait, id, branch, tier, maxRank,
            (branch == InstinctBranch ? Tuning.InstinctTierPrice : Tuning.TierPrice)[tier - 1]);

    /// <summary>The row of <paramref name="trait"/>. Throws for a value that is not a trait: a defect.</summary>
    public static TraitInfo Of(Trait trait) =>
        (int)trait >= 0 && (int)trait < All.Count
            ? All[(int)trait]
            : throw new ArgumentOutOfRangeException(nameof(trait), trait, "not a trait");

    /// <summary>The trait a tier above <paramref name="trait"/> in its branch, which it needs at rank 1; null for a first tier.</summary>
    public static TraitInfo? Above(Trait trait)
    {
        var info = Of(trait);
        return info.Tier == 1 ? null : All.Single(t => t.Branch == info.Branch && t.Tier == info.Tier - 1);
    }

    /// <summary>The trait a save's <paramref name="id"/> names, exactly; an unknown id is false (a save holding one is refused).</summary>
    public static bool TryParse(string? id, out Trait trait)
    {
        foreach (var info in All)
        {
            if (string.Equals(info.Id, id, StringComparison.Ordinal))
            {
                trait = info.Trait;
                return true;
            }
        }
        trait = default;
        return false;
    }
}
