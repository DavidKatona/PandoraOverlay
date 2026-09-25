namespace PandoraOverlay;

/// <summary>Eight-point compass letters for a screen heading (0 = up = north, clockwise).</summary>
public static class Compass
{
    private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static string Letter(double degrees)
    {
        var normalized = (degrees % 360 + 360) % 360;
        return Points[(int)Math.Floor((normalized + 22.5) / 45) % 8];
    }
}
