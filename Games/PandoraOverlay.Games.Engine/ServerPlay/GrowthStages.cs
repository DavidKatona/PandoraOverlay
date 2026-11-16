namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The growth stages of a dino on the server, by the overlay's own lines: the
/// ones its stats panel counts to and its Activity feed announces
/// (<c>GrowthMilestones</c> in the app). They are copied here, not shared,
/// because the engine may not reference the app (A2); a test in the app's
/// tests (<c>GrowthLinesInStepTests</c>) holds the two copies equal, so
/// neither can move alone and a game never names a stage the overlay itself
/// would not.
///
/// <para>The stages are numbered 0 to 4: 0 before the first line, then one
/// more for each line reached, 4 from <see cref="Elder"/> on. The engine names
/// only the four lines; a game words them (A21).</para>
/// </summary>
public static class GrowthStages
{
    /// <summary>Stage 1 from here.</summary>
    public const double Juvenile = 0.25;

    /// <summary>Stage 2 from here.</summary>
    public const double Subadult = 0.50;

    /// <summary>Stage 3 from here.</summary>
    public const double Adult = 0.75;

    /// <summary>
    /// Stage 4, fully grown, from here: a hair under 1, because the server
    /// reports a grown dino a hair under 1 (the overlay's full-grown line).
    /// </summary>
    public const double Elder = 0.9995;

    /// <summary>The four lines in order; a stage's number is how many of them a growth has reached.</summary>
    private static readonly double[] Lines = { Juvenile, Subadult, Adult, Elder };

    /// <summary>
    /// The stage <paramref name="growth"/> is in: 0 under <see cref="Juvenile"/>,
    /// then 1, 2, 3, and 4 from <see cref="Elder"/> on. A value on a line is past
    /// it (0.75 is stage 3), as in the overlay. Anything not a number is 0 (no
    /// line is reached by it); the website never sends one.
    /// </summary>
    public static int IndexOf(double growth)
    {
        var stage = 0;
        foreach (var line in Lines)
        {
            if (growth >= line) stage++;
            else break;
        }
        return stage;
    }
}
