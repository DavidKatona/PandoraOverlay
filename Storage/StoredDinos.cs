using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PandoraOverlay;

/// <summary>One vital of a stored dino as the website shows it: the value, its maximum, and how full it is (0–1).</summary>
public readonly record struct StoredVital(double Value, double Max, double Fraction)
{
    /// <summary>"225 / 225", the website's whole numbers; the value alone when there is no maximum.</summary>
    public string Text => Max > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{Value:0} / {Max:0}")
        : Value.ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>
/// One dino in the account's storage, as the website's /extras Dino Storage
/// page lists it. The id is kept WITH its JSON type, like a Patreon skin's,
/// because rename and delete send back exactly what the list delivered.
/// Name is null for the site's "Unnamed"; X/Y are world cm, null when the
/// website has no position for it.
/// </summary>
public sealed record StoredDino(
    string Id,
    bool IdIsNumber,
    string Species,
    string? Name,
    string? Description,
    string? Gender,
    double Growth,
    int ElderStacks,
    bool Prime,
    bool Compensated,
    DateTime? StoredAtUtc,
    StoredVital Health,
    StoredVital Stamina,
    StoredVital Hunger,
    StoredVital Thirst,
    StoredVital Blood,
    IReadOnlyList<string> Mutations,
    IReadOnlyList<string> ParentMutations,
    IReadOnlyList<string> ElderMutations,
    int MutationCount,
    double? X,
    double? Y)
{
    /// <summary>The id as it goes into a request body: a bare number or a quoted string, as the list had it.</summary>
    public string IdJson => IdIsNumber ? Id : JsonSerializer.Serialize(Id);

    /// <summary>The name you gave it, else its species.</summary>
    public string DisplayName => Name ?? Species;
}

/// <summary>The account's storage: the dinos in the website's order, and how many slots the account has.</summary>
public sealed record StoredDinoList(IReadOnlyList<StoredDino> Dinos, int Limit)
{
    public int Free => Math.Max(0, Limit - Dinos.Count);

    /// <summary>The list with one dino replaced by its renamed copy (matched by id).</summary>
    public StoredDinoList With(StoredDino changed) =>
        this with { Dinos = Dinos.Select(d => d.Id == changed.Id ? changed : d).ToList() };

    /// <summary>The list without one dino (a delete the website confirmed).</summary>
    public StoredDinoList Without(string id) => this with { Dinos = Dinos.Where(d => d.Id != id).ToList() };
}

/// <summary>What the list request brought: the list, or the website's own (cleaned) words for declining it.</summary>
public sealed record StoredDinoFetch(StoredDinoList? List, string? Declined = null);

public enum StorageEditOutcome { Ok, Refused, TooSoon, SignedOut, Failed }

/// <summary>
/// Outcome of one rename or delete click. TooSoon and SignedOut are answered
/// locally with no request sent (or the site refused the session); Refused
/// carries the website's own cleaned words, Failed a short reason — an
/// exception type at most. A successful rename carries the renamed dino.
/// </summary>
public sealed record StorageEditResult(StorageEditOutcome Outcome, string? Message = null, StoredDino? Dino = null);

/// <summary>
/// The pure half of the Dino storage page (1.33): the website's
/// /api/user/dinos answer read the way its own page reads it, the bodies of
/// the two writes, and the page's labels. Tested; no WPF, no HTTP. The
/// requests live in PandoraClient.Storage, the gating in
/// PollService.Storage, the cards in SettingsWindow.Storage.
/// </summary>
public static class StoredDinos
{
    public const int MaxNameLength = 40;          // the website's own input limits
    public const int MaxDescriptionLength = 200;
    public const int MaxMessageLength = 140;
    private const int MaxMutationLength = 60;

    // ---- The list ---------------------------------------------------------------

    /// <summary>
    /// {success, dinos:[...], storageLimit} → the list. Entries without an id
    /// are dropped (nothing could be done with them), a missing list reads as
    /// empty, a missing or zero limit as one slot (the website's own
    /// fallback); anything else is null — the website declined.
    /// </summary>
    public static StoredDinoList? ParseList(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsTruthy(root, "success")) return null;

        var dinos = new List<StoredDino>();
        if (root.TryGetProperty("dinos", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (ParseDino(item) is { } dino) dinos.Add(dino);
            }
        }
        var limit = Number(root, "storageLimit");
        return new StoredDinoList(dinos, limit >= 1 ? (int)Math.Min(Math.Round(limit), 999) : 1);
    }

    /// <summary>The website's words for a refusal ("error", else "message"), cleaned; null when it said nothing.</summary>
    public static string? ServerWords(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
            ? PatreonSkins.Clean(Text(root, "error") ?? Text(root, "message"), MaxMessageLength)
            : null;

    private static StoredDino? ParseDino(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("_id", out var idElement)) return null;
        var isNumber = idElement.ValueKind == JsonValueKind.Number;
        var id = isNumber ? idElement.GetRawText() : idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return null;

        var mutations = item.TryGetProperty("mutations", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        var (x, y) = Position(item);
        return new StoredDino(
            id, isNumber,
            SpeciesOf(Text(item, "dinoClass")),
            NameOrNull(Text(item, "name")),
            PatreonSkins.Clean(Text(item, "description"), MaxDescriptionLength),
            GenderOf(Text(item, "gender")),
            Math.Clamp(Number(item, "growth"), 0, 1),
            (int)Math.Clamp(Number(item, "elderStacks"), 0, 99),
            IsTruthy(item, "primeStatus"),
            IsTruthy(item, "isCompensated"),
            StoredAt(item),
            Vital(Number(item, "health"), Number(item, "maxHealth")),
            Vital(Number(item, "stamina"), Number(item, "maxStamina")),
            Vital(Number(item, "hunger"), Number(item, "maxHunger")),
            Vital(Number(item, "thirst"), Number(item, "maxThirst")),
            Vital(Number(item, "blood"), Number(item, "maxBlood")),
            Slots(mutations, i => $"MutationSlot{i}"),
            Slots(mutations, i => $"ParentMutationSlot{i}"),
            ElderSlots(mutations),
            CountMutations(mutations),
            x, y);
    }

    /// <summary>
    /// The species from the game's class name, the website's own rule:
    /// "BP_Herrerasaurus_C" (or a path ending in one) → "Herrerasaurus"; a
    /// name in another shape loses its "BP_" and "_C…" and gets spaces for
    /// underscores; nothing at all is "Unknown".
    /// </summary>
    public static string SpeciesOf(string? dinoClass)
    {
        if (string.IsNullOrWhiteSpace(dinoClass)) return "Unknown";
        var last = dinoClass.Split('/')[^1];
        if (last.Length == 0) last = dinoClass;
        var match = Regex.Match(last, "BP_([A-Za-z]+)_C");
        var name = match.Success
            ? match.Groups[1].Value
            : Regex.Replace(Regex.Replace(Regex.Replace(last, "^BP_", ""), "_C.*$", ""), "[_-]+", " ");
        if (name.Length == 0) name = dinoClass;
        return PatreonSkins.Clean(name, MaxNameLength) ?? "Unknown";
    }

    /// <summary>
    /// One vital, the website's way. The storage holds two kinds of dino (its
    /// changelog: the old and the new storage system): a value at most 1.5
    /// beside a maximum above 1.5 is a FRACTION of that maximum, anything
    /// else is the value itself. How full it is never goes past 1.
    /// </summary>
    public static StoredVital Vital(double current, double max)
    {
        if (!double.IsFinite(current) || current < 0) current = 0;
        if (!double.IsFinite(max) || max < 0) max = 0;
        var isFraction = current <= 1.5 && max > 1.5;
        var value = isFraction ? current * max : current;
        var full = max == 0 ? 0 : isFraction ? Math.Min(1, current) : Math.Min(1, current / max);
        return new StoredVital(value, max, full);
    }

    /// <summary>The website shows "Unnamed" for a dino nobody named; here that is no name at all.</summary>
    internal static string? NameOrNull(string? raw)
    {
        var name = PatreonSkins.Clean(raw, MaxNameLength);
        return name is null || name.Equals("Unnamed", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    private static string? GenderOf(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "male" => "Male",
            "female" => "Female",
            _ => PatreonSkins.Clean(raw, 20)
        };

    /// <summary>
    /// Where it was stored, in world cm. None at all, or exactly 0/0 (what
    /// the website's page falls back to when there is none), or off the
    /// island: no position.
    /// </summary>
    private static (double? X, double? Y) Position(JsonElement item)
    {
        if (!item.TryGetProperty("position", out var p) || p.ValueKind != JsonValueKind.Object) return (null, null);
        if (!TryNumber(p, "x", out var x) || !TryNumber(p, "y", out var y)) return (null, null);
        if ((x == 0 && y == 0) || !WaypointLibrary.InBounds(x, y)) return (null, null);
        return (x, y);
    }

    /// <summary>An ISO 8601 time (the website's) or milliseconds since 1970, as UTC.</summary>
    private static DateTime? StoredAt(JsonElement item)
    {
        if (!item.TryGetProperty("storedAt", out var v)) return null;
        if (v.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture,
                              DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
        {
            return DateTime.SpecifyKind(when, DateTimeKind.Utc);
        }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms) && ms is > 0 and < 32_503_680_000_000)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }
        return null;
    }

    private static readonly int[] SlotNumbers = { 1, 2, 3, 4 };

    /// <summary>The filled slots of one group, in slot order; "None" is an empty slot.</summary>
    private static IReadOnlyList<string> Slots(JsonElement mutations, Func<int, string> key) =>
        SlotNumbers.Select(i => Slot(mutations, key(i))).OfType<string>().ToList();

    /// <summary>An elder slot holds a pair (A and B): "A + B" when both are filled, else the one that is.</summary>
    private static IReadOnlyList<string> ElderSlots(JsonElement mutations) =>
        SlotNumbers.Select(i => (Slot(mutations, $"ElderMutationSlot{i}A"), Slot(mutations, $"ElderMutationSlot{i}B")) switch
            {
                ({ } a, { } b) => $"{a} + {b}",
                ({ } a, null) => a,
                (null, { } b) => b,
                _ => null
            })
            .OfType<string>().ToList();

    private static string? Slot(JsonElement mutations, string key)
    {
        if (mutations.ValueKind != JsonValueKind.Object) return null;
        var text = PatreonSkins.Clean(Text(mutations, key), MaxMutationLength);
        return text is null || IsNone(text) ? null : text;
    }

    /// <summary>The website's badge count: every filled value in the mutations object, whatever its slot.</summary>
    private static int CountMutations(JsonElement mutations) =>
        mutations.ValueKind != JsonValueKind.Object
            ? 0
            : mutations.EnumerateObject().Count(p =>
                p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()) && !IsNone(p.Value.GetString()!));

    private static bool IsNone(string text) => text.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);

    // ---- The two writes -----------------------------------------------------------

    /// <summary>What a name box sends: one line, no control characters, at most the website's 40. Empty is allowed, as on the website.</summary>
    public static string CleanName(string? raw) => CleanInput(raw, MaxNameLength);

    /// <summary>What a description box sends: one line, no control characters, at most the website's 200.</summary>
    public static string CleanDescription(string? raw) => CleanInput(raw, MaxDescriptionLength);

    private static string CleanInput(string? raw, int max)
    {
        var chars = (raw ?? "").Select(c => char.IsWhiteSpace(c) ? ' ' : c).Where(c => !char.IsControl(c)).ToArray();
        var text = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length > max ? text[..max].TrimEnd() : text;
    }

    /// <summary>The website's rename body: {dinoId, name, description}.</summary>
    public static string RenameBody(StoredDino dino, string name, string description) =>
        Body(w =>
        {
            w.WritePropertyName("dinoId");
            w.WriteRawValue(dino.IdJson);
            w.WriteString("name", name);
            w.WriteString("description", description);
        });

    /// <summary>The website's delete body: {dinoId}.</summary>
    public static string DeleteBody(StoredDino dino) =>
        Body(w =>
        {
            w.WritePropertyName("dinoId");
            w.WriteRawValue(dino.IdJson);
        });

    private static string Body(Action<Utf8JsonWriter> fields)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            fields(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The rename answer: success carries the name and description the
    /// website kept (what was sent stands in for a missing one), else its own
    /// words for why not.
    /// </summary>
    public static StorageEditResult ParseRename(JsonElement root, StoredDino dino, string sentName, string sentDescription)
    {
        if (root.ValueKind != JsonValueKind.Object) return new StorageEditResult(StorageEditOutcome.Failed, "unexpected response");
        if (!IsTruthy(root, "success")) return new StorageEditResult(StorageEditOutcome.Refused, ServerWords(root));

        var name = root.TryGetProperty("name", out var n) && n.ValueKind is JsonValueKind.String or JsonValueKind.Null
            ? (n.ValueKind == JsonValueKind.String ? n.GetString() : null)
            : sentName;
        var description = root.TryGetProperty("description", out var d) && d.ValueKind is JsonValueKind.String or JsonValueKind.Null
            ? (d.ValueKind == JsonValueKind.String ? d.GetString() : null)
            : sentDescription;
        var renamed = dino with
        {
            Name = NameOrNull(name),
            Description = PatreonSkins.Clean(description, MaxDescriptionLength)
        };
        return new StorageEditResult(StorageEditOutcome.Ok, Dino: renamed);
    }

    /// <summary>The delete answer: success, or the website's own words for why not.</summary>
    public static StorageEditResult ParseDelete(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return new StorageEditResult(StorageEditOutcome.Failed, "unexpected response");
        return IsTruthy(root, "success")
            ? new StorageEditResult(StorageEditOutcome.Ok)
            : new StorageEditResult(StorageEditOutcome.Refused, ServerWords(root));
    }

    // ---- Labels -------------------------------------------------------------------

    /// <summary>
    /// "58% · subadult", "100% · fully grown": the percent rounded down, so
    /// 99.6% never reads as full — with a hair of slack, because 0.58 × 100
    /// is 57.999… in floating point.
    /// </summary>
    public static string GrowthText(double growth)
    {
        var percent = growth >= GrowthMilestones.FullyGrown ? 100 : (int)Math.Floor(Math.Clamp(growth, 0, 1) * 100 + 1e-9);
        return $"{percent}% · {GrowthMilestones.StageAt(growth)}";
    }

    /// <summary>"23 Sep · 11:42" this year, "23 Sep 2025" before; English, whatever the PC's locale, like the rest of the overlay.</summary>
    public static string StoredText(DateTime local, int currentYear) =>
        local.Year == currentYear
            ? local.ToString("d MMM · HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    // ---- JSON helpers -------------------------------------------------------------

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Number(JsonElement obj, string name) => TryNumber(obj, name, out var value) ? value : 0;

    private static bool TryNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out value)) return false;
        return double.IsFinite(value);
    }

    private static bool IsTruthy(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));
}
