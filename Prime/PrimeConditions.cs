namespace PandoraOverlay;

/// <summary>
/// The ten Prime conditions as the site words them. Baked in because the
/// site bakes them into its frontend too — the API only returns flags.
/// Shared by the Prime tracker's rows and the Activity feed's "now met /
/// lost" lines.
/// </summary>
public static class PrimeConditions
{
    public const int NeededForPrime = 5;

    public static readonly string[] Texts =
    {
        "Visit a Sanctuary as a juvenile",
        "Get nested in",
        "Get perfect diet (1% of each)",
        "Visit Mass Migration zone",
        "Visit 2 Migration zones",
        "Visit 4 Patrol zones",
        "Never be Infertile",
        "Never get Muscle spasms",
        "Raise children to Subadult",
        "Be a Hypsi, Troodon, Beipi, Dryo or Deino"
    };
}
