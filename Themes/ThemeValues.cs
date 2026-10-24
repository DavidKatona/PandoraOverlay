using System.Globalization;

namespace PandoraOverlay;

/// <summary>
/// The value types a theme token can hold, WPF-free so the tests read theme
/// files without WPF (PLAN.md section 1). Theme.Apply turns each into the
/// WPF object a window reads: a colour into a frozen brush (and a Color), a
/// radius into a CornerRadius, a weight into a FontWeight, a font into a
/// FontFamily. In a theme file every one of them is a plain JSON value:
/// <c>"#7FE3F0"</c>, <c>0</c>, <c>"SemiBold"</c>, <c>"Assets/Fonts/Montserrat/#Montserrat"</c>.
/// </summary>
/// <param name="A">Alpha, 0 transparent to 255 opaque.</param>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    /// <summary>Fully transparent: what Default holds for a part it never draws (a glow, a lit corner).</summary>
    public static readonly ThemeColor None = new(0, 0, 0, 0);

    /// <summary>
    /// "#RRGGBB" (opaque) or "#AARRGGBB", the forms the XAML has always used,
    /// so Default reads like the literals it replaced. Anything else is a
    /// FormatException: a theme file with a bad colour fails its test, it
    /// never draws black.
    /// </summary>
    public static ThemeColor Parse(string text)
    {
        if (TryParse(text, out var colour)) return colour;
        throw new FormatException($"\"{text}\" is not a colour (#RRGGBB or #AARRGGBB)");
    }

    public static bool TryParse(string? text, out ThemeColor colour)
    {
        colour = default;
        if (text is null || text.Length is not (7 or 9) || text[0] != '#') return false;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return false;
        if (text.Length == 7) v |= 0xFF000000;
        colour = new ThemeColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>The same colour at another alpha (0–1): "the accent at 30%".</summary>
    public ThemeColor At(double opacity) => this with { A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255) };

    /// <summary>Back to the file's form: "#RRGGBB" when opaque, else "#AARRGGBB".</summary>
    public override string ToString() =>
        A == 0xFF ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// A corner radius, the same on all four corners (every rounded corner the
/// overlay has is uniform). Its own type, not a double, so Theme.Apply knows
/// to hand the windows a CornerRadius. 0 = square.
/// </summary>
public readonly record struct ThemeRadius(double Value)
{
    public static implicit operator ThemeRadius(double value) => new(value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A font, as a theme file names it: an installed family by its name
/// ("Segoe UI"), or a bundled one by its folder under the app, then # and
/// the family's own name ("Assets/Fonts/Montserrat/#Montserrat").
/// Theme.FontFamilyOf turns it into the WPF FontFamily.
/// </summary>
public readonly record struct ThemeFont(string Address)
{
    /// <summary>True for a family bundled with the app (a path with a #), false for an installed one.</summary>
    public bool Bundled => Address.Contains('#') && Address.Contains('/');

    public override string ToString() => Address;
}

/// <summary>
/// A font weight by its OpenType name. The bundled Montserrat has Regular
/// (Normal), Medium, SemiBold and Bold; a theme that wants another weight
/// brings that file too (PLAN.md section 5).
/// </summary>
public enum ThemeWeight
{
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    Normal = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    ExtraBold = 800,
    Black = 900,
}
