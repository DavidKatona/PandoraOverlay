using System.Globalization;
using System.Text.RegularExpressions;

namespace PandoraOverlay;

/// <summary>
/// The share-a-spot code: a world position, optionally named, as short text
/// for the clipboard, so "meet me here" can go over voice or chat and come
/// back as a waypoint. Metres, not cm — nobody needs the decimals and the
/// code stays short. Parsing is forgiving: the prefix is optional and the
/// code may sit inside a longer message; anything after the numbers on the
/// same line is the name. Never touches the site; a snapshot, not tracking.
/// </summary>
public static class ShareCode
{
    private const string Prefix = "pandora:";
    private const double MaxMeters = 50_000; // the island spans ~12.5 km; anything wilder is not a position

    private static readonly Regex Pattern =
        new(@"(-?\d{1,6})\s*,\s*(-?\d{1,6})[ \t]*([^\r\n]*)", RegexOptions.Compiled);

    public static string Format(double xCm, double yCm, string? name = null)
    {
        var code = Prefix +
                   Math.Round(xCm / 100).ToString("0", CultureInfo.InvariantCulture) + "," +
                   Math.Round(yCm / 100).ToString("0", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(name) ? code : code + " " + name.Trim();
    }

    /// <summary>World cm (and a name, possibly empty) from a code or any text containing one; false when there is none.</summary>
    public static bool TryParse(string? text, out double xCm, out double yCm, out string name)
    {
        xCm = yCm = 0;
        name = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = Pattern.Match(text);
        if (!match.Success) return false;

        var x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (Math.Abs(x) > MaxMeters || Math.Abs(y) > MaxMeters) return false;

        xCm = x * 100;
        yCm = y * 100;
        name = match.Groups[3].Value.Trim();
        return true;
    }

    public static bool TryParse(string? text, out double xCm, out double yCm) => TryParse(text, out xCm, out yCm, out _);
}
