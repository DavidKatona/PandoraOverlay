using System.Globalization;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// One Patreon skin as the website's /patreon page lists it: a colour set
/// with a name, the tier that unlocks it and a picture. The server decides
/// Locked — the overlay only ever shows it. The id is kept with its JSON
/// type, because applying sends back exactly what the list delivered.
/// </summary>
public sealed record PatreonSkin(
    string Id,
    bool IdIsNumber,
    string Name,
    string? Description,
    string? Image,
    string? Thumbnail,
    string? RequiredRole,
    string? RequiredRoleId,
    bool Locked,
    IReadOnlyList<string> Colours)
{
    /// <summary>The id as it goes into the apply request: a bare number or a quoted string, as the list had it.</summary>
    public string IdJson => PatreonSkins.IdJson(Id, IdIsNumber);

    /// <summary>
    /// Where a tile's picture may come from, in the order to try: the
    /// thumbnail, then the full image (the site shows "thumbnail || image" and
    /// falls back between the two when one fails to load). Empty = the skin
    /// has no picture at all.
    /// </summary>
    public IReadOnlyList<string> Pictures =>
        new[] { Thumbnail, Image }.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim()).Distinct().ToList();
}

/// <summary>One tile picture as fetched: its bytes, or a short reason why not ("HTTP 404", "too large (6 MB)", an exception type). Never server text.</summary>
public sealed record SkinPicture(byte[]? Bytes, string? Problem = null);

/// <summary>The skin and pattern last applied for one species, so the Skins page can offer it again (kept in config).</summary>
public sealed record SkinChoice(string SkinId, bool IdIsNumber, string Name, int Pattern);

public enum SkinApplyOutcome { Ok, NotInGame, TooSoon, Refused, Failed }

/// <summary>
/// Outcome of one user-triggered apply. NotInGame and TooSoon are answered
/// locally with no request sent; Refused carries the server's own (cleaned)
/// words, Failed a short reason — an exception type at most.
/// </summary>
public sealed record SkinApplyResult(SkinApplyOutcome Outcome, string SkinName, int Pattern, string? Message = null);

/// <summary>
/// The pure half of the Patreon skins feature (v1.28): parsing the site's
/// list and apply responses, the colour maths and the picture-URL rule.
/// Tested; no WPF, no HTTP. The requests live in PandoraClient.Skins, the
/// gating in PollService.Skins, the tiles in SettingsWindow.Skins.
/// </summary>
public static class PatreonSkins
{
    public const int PatternCount = 6; // the site's A–F buttons: patternIndex 0–5
    public const int MaxMessageLength = 140;

    private const string SiteRoot = "https://islapandora.eu/";

    /// <summary>The colour slots in the order the site draws its dots.</summary>
    private static readonly string[] ColourKeys = { "md", "m", "b", "f", "u", "d1", "e" };

    /// <summary>The site's "By Role" order: its four Patreon tiers, cheapest first; anything else sorts last.</summary>
    private static readonly Dictionary<string, int> TierRank = new()
    {
        ["1403680490732523550"] = 1, // Patreon | Bronze
        ["1403681132398116935"] = 2, // Patreon | Silver
        ["1403681275142995998"] = 3, // Pandorian Titan
        ["1403681314757935166"] = 4, // Pandorian Fanatic
    };

    public static int RankOf(string? roleId) => roleId is not null && TierRank.TryGetValue(roleId, out var rank) ? rank : 99;

    /// <summary>Pattern index → the letter on the site's button.</summary>
    public static char PatternLetter(int index) => (char)('A' + Math.Clamp(index, 0, PatternCount - 1));

    public static string IdJson(string id, bool isNumber) => isNumber ? id : JsonSerializer.Serialize(id);

    /// <summary>
    /// {success, skins:[...]} → the list; entries without an id or a name are
    /// dropped, a missing list reads as empty, anything else as null (a miss).
    /// </summary>
    public static IReadOnlyList<PatreonSkin>? ParseList(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsTruthy(root, "success")) return null;

        var skins = new List<PatreonSkin>();
        if (!root.TryGetProperty("skins", out var list) || list.ValueKind != JsonValueKind.Array) return skins;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("id", out var id)) continue;
            var isNumber = id.ValueKind == JsonValueKind.Number;
            var idText = isNumber ? id.GetRawText() : id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            var name = Text(item, "name");
            if (string.IsNullOrWhiteSpace(idText) || string.IsNullOrWhiteSpace(name)) continue;

            skins.Add(new PatreonSkin(
                idText, isNumber, Clean(name, 60)!,
                Clean(Text(item, "description"), 200),
                Text(item, "image"), Text(item, "thumbnail"),
                Clean(Text(item, "requiredRole"), 40), RoleId(item),
                IsTruthy(item, "locked"),
                ReadColours(item)));
        }
        return skins;
    }

    /// <summary>The apply response: success, or the server's own words for why not (message, else error), cleaned.</summary>
    public static SkinApplyResult ParseApply(JsonElement root, string skinName, int pattern)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new SkinApplyResult(SkinApplyOutcome.Failed, skinName, pattern, "unexpected response");
        }
        if (IsTruthy(root, "success")) return new SkinApplyResult(SkinApplyOutcome.Ok, skinName, pattern);

        var said = Clean(Text(root, "message") ?? Text(root, "error"), MaxMessageLength);
        return new SkinApplyResult(SkinApplyOutcome.Refused, skinName, pattern, said);
    }

    /// <summary>
    /// A server string made safe to show: one line, no control characters,
    /// capped. Anything that looks like it carries a session cookie is
    /// dropped whole — the cookie is a credential and never reaches a window.
    /// </summary>
    public static string? Clean(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Contains("connect.sid", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase)) return null;

        var chars = raw.Select(c => char.IsWhiteSpace(c) ? ' ' : c).Where(c => !char.IsControl(c)).ToArray();
        var text = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > maxLength) text = text[..(maxLength - 1)].TrimEnd() + "…";
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The site's own conversion of a skin colour (linear 0–1 floats) to a
    /// display colour: each channel raised to 1/1.8, then scaled to a byte.
    /// </summary>
    public static string ToHex(double r, double g, double b)
    {
        // AwayFromZero: JavaScript's Math.round takes a half up, .NET's default takes it to the even number.
        static int Channel(double v) =>
            (int)Math.Clamp(Math.Round(Math.Pow(Math.Max(0, v), 1 / 1.8) * 255, MidpointRounding.AwayFromZero), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{Channel(r):x2}{Channel(g):x2}{Channel(b):x2}");
    }

    /// <summary>
    /// Where a tile's picture is fetched from: an absolute https address as it
    /// is, a site-relative one under islapandora.eu; anything else (http,
    /// data:, garbage) is no picture. Pictures are always fetched WITHOUT the
    /// cookie, whatever the host.
    /// </summary>
    public static Uri? ResolvePicture(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (text.StartsWith("//", StringComparison.Ordinal)) text = "https:" + text;
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme == Uri.UriSchemeHttps ? absolute : null; // http, data:, file: and the rest: no picture
        }
        return Uri.TryCreate(new Uri(SiteRoot), text, out var relative) && relative.Scheme == Uri.UriSchemeHttps ? relative : null;
    }

    /// <summary>The order the page shows: what you can apply first, then by tier, then by name.</summary>
    public static IReadOnlyList<PatreonSkin> Sorted(IEnumerable<PatreonSkin> skins) =>
        skins.OrderBy(s => s.Locked)
             .ThenBy(s => RankOf(s.RequiredRoleId))
             .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
             .ToList();

    /// <summary>The page's search box: name, description or tier contains the text.</summary>
    public static bool Matches(PatreonSkin skin, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var needle = search.Trim();
        return skin.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               (skin.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (skin.RequiredRole?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static IReadOnlyList<string> ReadColours(JsonElement item)
    {
        var colours = new string[ColourKeys.Length];
        var has = item.TryGetProperty("colors", out var set) && set.ValueKind == JsonValueKind.Object;
        for (var i = 0; i < ColourKeys.Length; i++)
        {
            colours[i] = has && set.TryGetProperty(ColourKeys[i], out var c) && c.ValueKind == JsonValueKind.Object
                ? ToHex(Number(c, "R"), Number(c, "G"), Number(c, "B"))
                : "#808080"; // the site's own stand-in for a missing colour
        }
        return colours;
    }

    private static string? RoleId(JsonElement item) =>
        item.TryGetProperty("requiredRoleId", out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null
            : null;

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Number(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static bool IsTruthy(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));
}
