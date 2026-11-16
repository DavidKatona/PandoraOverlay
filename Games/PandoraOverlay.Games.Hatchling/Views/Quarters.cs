namespace PandoraOverlay.Games.Hatchling;

/// <summary>The four states every bar of the run's panel is in: the app's own four (its <c>BarState</c>), by the same lines.</summary>
internal enum Quarter
{
    /// <summary>75% and up.</summary>
    Above75,
    /// <summary>50% up to (not including) 75%.</summary>
    From50To75,
    /// <summary>25% up to (not including) 50%.</summary>
    From25To50,
    /// <summary>Under 25%.</summary>
    Below25,
}

/// <summary>
/// Which quarter a bar's value is in, so the Run page's health, food and water bars
/// take the theme's own colour for it (<c>Theme.Bars.Health.Below25.Fill</c> and the
/// rest), exactly as the stats panel's bars over the game do: a player who knows that
/// red means under a quarter on their real dino reads the game's bars the same way.
///
/// <para>The lines are the app's (<c>Stats/BarState.cs</c>, <c>BarStates.Of</c>): 0.75,
/// 0.50 and 0.25, and a value ON a line belongs to the quarter above it. Written as the
/// same three "under" tests, so a NaN lands where the app's lands (Above75). The game
/// cannot reference the app (A2), so the rule is mirrored here, and a test in the app's
/// tests (task H16) holds the two equal.</para>
/// </summary>
internal static class Quarters
{
    /// <summary>The quarter of a 0 to 1 value.</summary>
    public static Quarter Of(double fraction) =>
        fraction < 0.25 ? Quarter.Below25
        : fraction < 0.50 ? Quarter.From25To50
        : fraction < 0.75 ? Quarter.From50To75
        : Quarter.Above75;

    /// <summary>The health bar's fill key for a quarter: one whole literal each, so the app's key scan reads every one.</summary>
    public static string HealthKey(Quarter quarter) => quarter switch
    {
        Quarter.Above75 => "Theme.Bars.Health.Above75.Fill",
        Quarter.From50To75 => "Theme.Bars.Health.From50To75.Fill",
        Quarter.From25To50 => "Theme.Bars.Health.From25To50.Fill",
        _ => "Theme.Bars.Health.Below25.Fill",
    };

    /// <summary>The food bar's fill key (the stats panel's Hunger bar).</summary>
    public static string FoodKey(Quarter quarter) => quarter switch
    {
        Quarter.Above75 => "Theme.Bars.Hunger.Above75.Fill",
        Quarter.From50To75 => "Theme.Bars.Hunger.From50To75.Fill",
        Quarter.From25To50 => "Theme.Bars.Hunger.From25To50.Fill",
        _ => "Theme.Bars.Hunger.Below25.Fill",
    };

    /// <summary>The water bar's fill key (the stats panel's Thirst bar).</summary>
    public static string WaterKey(Quarter quarter) => quarter switch
    {
        Quarter.Above75 => "Theme.Bars.Thirst.Above75.Fill",
        Quarter.From50To75 => "Theme.Bars.Thirst.From50To75.Fill",
        Quarter.From25To50 => "Theme.Bars.Thirst.From25To50.Fill",
        _ => "Theme.Bars.Thirst.Below25.Fill",
    };
}
