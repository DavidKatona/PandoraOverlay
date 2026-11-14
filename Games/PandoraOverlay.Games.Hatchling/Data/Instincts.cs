namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The twelve instincts: one of three (four with Sharp instincts) is chosen at
/// Juvenile, Subadult and Adult, for the run, each at most once a run (H14). They
/// make every run ask a different question; the lineage only makes runs longer.
/// </summary>
internal enum Instinct { Eyes, Feet, Stride, Skin, Ambush, Appetite, Healer, Quick, Nose, Fierce, Wary, Hardy }

/// <summary>The instincts' table: their order and their ids.</summary>
internal static class Instincts
{
    /// <summary>The twelve in the table's order: an offer shuffles them in this order, so it is part of every seeded run.</summary>
    public static IReadOnlyList<Instinct> All { get; } = new[]
    {
        Instinct.Eyes, Instinct.Feet, Instinct.Stride, Instinct.Skin, Instinct.Ambush, Instinct.Appetite,
        Instinct.Healer, Instinct.Quick, Instinct.Nose, Instinct.Fierce, Instinct.Wary, Instinct.Hardy,
    };

    // The ids go into saves: stable for ever, whatever the enum's names become.
    private static readonly string[] Ids =
        { "eyes", "feet", "stride", "skin", "ambush", "appetite", "healer", "quick", "nose", "fierce", "wary", "hardy" };

    /// <summary>The id a save writes for <paramref name="instinct"/>. Throws for a value that is not one: a defect.</summary>
    public static string IdOf(Instinct instinct) =>
        (int)instinct >= 0 && (int)instinct < Ids.Length
            ? Ids[(int)instinct]
            : throw new ArgumentOutOfRangeException(nameof(instinct), instinct, "not an instinct");

    /// <summary>The instinct a save's <paramref name="id"/> names, exactly; an unknown id is false (a save holding one is refused).</summary>
    public static bool TryParse(string? id, out Instinct instinct)
    {
        var at = id is null ? -1 : Array.IndexOf(Ids, id);
        instinct = at < 0 ? default : (Instinct)at;
        return at >= 0;
    }
}
