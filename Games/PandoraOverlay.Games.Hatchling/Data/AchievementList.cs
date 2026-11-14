using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Hatchling's 24 achievements (H26), as the engine's definitions, in the plan's
/// table's order: the order Records lists them in and the order a settled run
/// grants its own in.
///
/// <para><b>What they pay (G22).</b> A little of the game's currency, into its one
/// wallet (G3), or nothing: never power. The engine's reward type has no kind that
/// could be power, and a test holds every reward here to <c>Currency</c> or
/// <c>None</c>, and their sum to <see cref="TotalPay"/>, 8% of the lineage's price,
/// so they speed the climb a little and never replace it.</para>
///
/// <para><b>Ids are stable for ever.</b> The save keeps them; a renamed id would
/// take the achievement from every player who had it. The words are
/// <see cref="HatchlingText"/>'s, read by id (A24), and a test holds this list's
/// ids equal to <see cref="HatchlingText.AchievementIds"/>, in order.</para>
///
/// <para>Who grants which: a settled run its own (<c>Settle.Run</c>), a purchase in
/// the lineage <see cref="Roots"/> and <see cref="Blood"/>, an unlock
/// <see cref="Six"/> and <see cref="Island"/>, server play the journal's, the
/// Adults' and the stars' (task H06). Each counts what its text says and no
/// more; the thresholds are named here, beside the achievement they belong to.</para>
/// </summary>
internal static class AchievementList
{
    // ---- The ids, in the table's order ------------------------------------------------

    public const string First = "first";
    public const string Juvenile = "juvenile";
    public const string Adult = "adult";
    public const string Elder = "elder";
    public const string Win = "win";
    public const string Slayer = "slayer";
    public const string Hunter = "hunter";
    public const string Underdog = "underdog";
    public const string Heavy = "heavy";
    public const string Shapes = "shapes";
    public const string Danger3 = "danger3";
    public const string Danger5 = "danger5";
    public const string Unseen = "unseen";
    public const string Gentle = "gentle";
    public const string Six = "six";
    public const string Island = "island";
    public const string Roots = "roots";
    public const string Blood = "blood";
    public const string Researcher = "researcher";
    public const string Naturalist = "naturalist";
    public const string Diverse = "diverse";
    public const string Star21 = "star21";
    public const string Mastery = "mastery";
    public const string All21 = "all";

    // ---- The thresholds -----------------------------------------------------------------

    /// <summary><see cref="Hunter"/>: predators defeated over all runs.</summary>
    public const int HunterKills = 25;

    /// <summary><see cref="Danger3"/>: a win at this danger or above.</summary>
    public const int IntoDanger = 3;

    /// <summary><see cref="Six"/>: species unlocked.</summary>
    public const int WideLineage = 6;

    /// <summary><see cref="Roots"/>: ranks of traits bought.</summary>
    public const int RootsRanks = 10;

    /// <summary><see cref="Researcher"/>: journal entries finished.</summary>
    public const int ResearcherEntries = 3;

    /// <summary><see cref="Naturalist"/>: journal entries finished.</summary>
    public const int NaturalistEntries = 15;

    /// <summary><see cref="Diverse"/>: species seen crossing Adult on the server.</summary>
    public const int DiverseSpecies = 5;

    /// <summary>What all 24 pay together (H26): 8% of the lineage's 42,180.</summary>
    public const int TotalPay = 3490;

    /// <summary>
    /// The 24, in the table's order. <see cref="Danger5"/>, <see cref="Island"/>,
    /// <see cref="Shapes"/>, <see cref="Star21"/>, <see cref="Mastery"/> and
    /// <see cref="All21"/> count the tables themselves (the highest danger, the 21
    /// species, the nine builds), so their texts and their rules can't part.
    /// </summary>
    public static IReadOnlyList<AchievementDef> All { get; } = new[]
    {
        Def(First, 20),
        Def(Juvenile, 20),
        Def(Adult, 40),
        Def(Elder, 60),
        Def(Win, 100),
        Def(Slayer, 150),
        Def(Hunter, 50),
        Def(Underdog, 100),
        Def(Heavy, 100),
        Def(Shapes, 300),
        Def(Danger3, 150),
        Def(Danger5, 400),
        Def(Unseen, 100),
        Def(Gentle, 100),
        Def(Six, 60),
        Def(Island, 300),
        Def(Roots, 60),
        Def(Blood, 0),
        Def(Researcher, 80),
        Def(Naturalist, 250),
        Def(Diverse, 150),
        Def(Star21, 400),
        Def(Mastery, 0),
        Def(All21, 500),
    };

    /// <summary>A definition with its words read by id, paying <paramref name="pay"/> or, at 0, nothing.</summary>
    private static AchievementDef Def(string id, int pay) =>
        new(id, HatchlingText.AchievementName(id), HatchlingText.AchievementText(id),
            pay > 0 ? new AchievementReward.Currency(pay) : new AchievementReward.None());
}
