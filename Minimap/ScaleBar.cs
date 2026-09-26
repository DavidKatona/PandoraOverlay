namespace PandoraOverlay;

/// <summary>Pure math for the minimap's scale bar: a round real-world length that fits a pixel budget.</summary>
public static class ScaleBar
{
    /// <summary>
    /// The largest 1-2-5 × 10ⁿ length (metres) whose bar is at most
    /// <paramref name="maxPixels"/> long at the given map scale; (0, 0) when
    /// the scale is unusable.
    /// </summary>
    public static (double Meters, double Pixels) Pick(double pixelsPerMeter, double maxPixels)
    {
        if (!(pixelsPerMeter > 0) || !(maxPixels > 0) || double.IsInfinity(pixelsPerMeter)) return (0, 0);

        var maxMeters = maxPixels / pixelsPerMeter;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(maxMeters)));
        var lead = maxMeters / magnitude; // 1 ≤ lead < 10
        var meters = (lead >= 5 ? 5 : lead >= 2 ? 2 : 1) * magnitude;
        return (meters, meters * pixelsPerMeter);
    }

    /// <summary>"500 m", "2 km".</summary>
    public static string Format(double meters) =>
        meters >= 1000 ? $"{meters / 1000:0.#} km" : $"{meters:0.#} m";
}
