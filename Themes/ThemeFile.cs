using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PandoraOverlay;

/// <summary>
/// A theme file: every theme but Default is a SPARSE override, an embedded
/// <c>Assets/Themes/&lt;id&gt;.json</c> that names only what differs from
/// Default and inherits the rest (PLAN.md section 1, REVIEW.md objection 2).
/// <code>
/// {
///   "Name": "Isle",            // what players read on Settings → General
///   "Order": 1,                // its place in that row, after Default
///   "Tokens": {                // only what differs, by the token's path
///     "Text": { "Title": "#B8E6EC" },
///     "Shapes": { "Radio": "hexagon" },
///     "Bars": { "Health": { "Below25": { "Fill": "#F06A5A" } } }
///   }
/// }
/// </code>
/// A key Default does not have, or a value that does not parse, is a
/// FAILURE (<see cref="ThemeFileException"/>), never a silent inheritance: a
/// typo must not quietly keep Default's colour. There is no run-time
/// fallback, because an embedded file cannot go missing or change on a
/// player's PC: a broken file is a build defect, and ThemeFileTests catch
/// it (fail-soft loading belongs to the day players have files of their own,
/// the theme editor's release). Comments and trailing commas are allowed.
/// WPF-free, so the tests read the files without WPF.
/// </summary>
public static class ThemeFile
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>How tokens read and write: every value type by its strict converter, no member left over or missing.</summary>
    internal static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new ColourConverter(),
            new RadiusConverter(),
            new FontConverter(),
            new ShapeConverter(),
            new WeightConverter(),
        },
    };

    private static readonly string[] FileKeys = { "Name", "Order", "Tokens" };

    /// <summary>
    /// Reads a theme file and lays it over <paramref name="over"/> (Default).
    /// <paramref name="id"/> is the file's name without ".json".
    /// </summary>
    public static ThemeEntry Parse(string id, string json, ThemeTokens over)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ThemeFileException(id, $"is not JSON ({ex.Message})");
        }
        if (root is not JsonObject file) throw new ThemeFileException(id, "is not a JSON object");

        foreach (var (key, _) in file)
            if (!FileKeys.Contains(key, StringComparer.Ordinal))
                throw new ThemeFileException(id, $"\"{key}\" is not one of {string.Join(", ", FileKeys)}");

        var name = file["Name"] is JsonValue n && n.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
            ? s.Trim()
            : throw new ThemeFileException(id, "has no Name");
        var order = file["Order"] is JsonValue o && o.TryGetValue<int>(out var i)
            ? i
            : throw new ThemeFileException(id, "has no Order (a whole number)");
        var tokens = file["Tokens"] as JsonObject ?? throw new ThemeFileException(id, "has no Tokens object");

        return new ThemeEntry(id, name, order, Overlay(id, tokens, over));
    }

    /// <summary>
    /// Lays a sparse set of tokens over a complete one. Objects merge key by
    /// key, a value replaces the one under it; a key the record does not have,
    /// an object where a value belongs (or the reverse), a null, or a value
    /// its type can't read fails with the token's path. The shape picks are
    /// checked against their slots (<see cref="ShapeCatalogue.Check"/>).
    /// </summary>
    public static ThemeTokens Overlay(string id, JsonObject overrides, ThemeTokens over)
    {
        var merged = JsonSerializer.SerializeToNode(over, Options) as JsonObject
                     ?? throw new InvalidOperationException("the base tokens did not serialise to an object");
        Merge(id, merged, overrides, "Tokens");

        ThemeTokens tokens;
        try
        {
            tokens = merged.Deserialize<ThemeTokens>(Options) ?? throw new ThemeFileException(id, "Tokens read as nothing");
        }
        catch (JsonException ex)
        {
            throw new ThemeFileException(id, $"{Path(ex.Path)}: {ex.InnerException?.Message ?? ex.Message}");
        }
        try
        {
            ShapeCatalogue.Check(tokens.Shapes);
        }
        catch (FormatException ex)
        {
            throw new ThemeFileException(id, $"Tokens.{ex.Message}");
        }
        return tokens;
    }

    private static void Merge(string id, JsonObject target, JsonObject source, string path)
    {
        foreach (var (key, value) in source)
        {
            var here = $"{path}.{key}";
            // Exact case: "title" is not Title, and must not quietly inherit Default's.
            if (!target.TryGetPropertyValue(key, out var under))
                throw new ThemeFileException(id, $"{here} is not a token");
            if (value is null)
                throw new ThemeFileException(id, $"{here} is null");

            if (under is JsonObject group)
            {
                if (value is not JsonObject inner) throw new ThemeFileException(id, $"{here} is a group of tokens, not a value");
                Merge(id, group, inner, here);
            }
            else
            {
                if (value is JsonObject or JsonArray) throw new ThemeFileException(id, $"{here} is a value, not a group");
                target[key] = value.DeepClone();
            }
        }
    }

    // "$.Text.Title" → "Tokens.Text.Title", the path as the file writes it.
    private static string Path(string? jsonPath) =>
        string.IsNullOrEmpty(jsonPath) ? "Tokens" : "Tokens" + jsonPath.TrimStart('$');

    #region Converters (strict: a wrong kind of value is a failure, never a default)

    private sealed class ColourConverter : JsonConverter<ThemeColor>
    {
        public override bool HandleNull => true;

        public override ThemeColor Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("a colour is a string, \"#RRGGBB\" or \"#AARRGGBB\"");
            return ThemeColor.TryParse(reader.GetString(), out var c) ? c
                : throw new JsonException($"\"{reader.GetString()}\" is not a colour (#RRGGBB or #AARRGGBB)");
        }

        public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class RadiusConverter : JsonConverter<ThemeRadius>
    {
        public override bool HandleNull => true;

        public override ThemeRadius Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException("a corner radius is a number");
            var v = reader.GetDouble();
            return v >= 0 ? new ThemeRadius(v) : throw new JsonException("a corner radius can't be negative");
        }

        public override void Write(Utf8JsonWriter writer, ThemeRadius value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    private sealed class FontConverter : JsonConverter<ThemeFont>
    {
        public override bool HandleNull => true;

        public override ThemeFont Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var s = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return !string.IsNullOrWhiteSpace(s) ? new ThemeFont(s.Trim())
                : throw new JsonException("a font is a family's name, or a bundled family's \"Assets/Fonts/<folder>/#<Family>\"");
        }

        public override void Write(Utf8JsonWriter writer, ThemeFont value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Address);
    }

    private sealed class ShapeConverter : JsonConverter<ShapeKind>
    {
        public override bool HandleNull => true;

        public override ShapeKind Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("a shape is a name from the catalogue");
            try
            {
                return ShapeCatalogue.Parse(reader.GetString()!);
            }
            catch (FormatException ex)
            {
                throw new JsonException(ex.Message);
            }
        }

        public override void Write(Utf8JsonWriter writer, ShapeKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ShapeCatalogue.NameOf(value));
    }

    private sealed class WeightConverter : JsonConverter<ThemeWeight>
    {
        public override bool HandleNull => true;

        public override ThemeWeight Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var s = reader.TokenType == JsonTokenType.String ? reader.GetString()?.Trim() : null;
            // Names only: Enum.TryParse would take "600" as a number.
            if (!string.IsNullOrEmpty(s) && s.All(char.IsLetter) && Enum.TryParse<ThemeWeight>(s, ignoreCase: true, out var w))
                return w;
            throw new JsonException($"a weight is one of {string.Join(", ", Enum.GetNames<ThemeWeight>())}");
        }

        public override void Write(Utf8JsonWriter writer, ThemeWeight value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    #endregion
}

/// <summary>A theme file that can't be read, named with the token at fault: a build defect, caught by ThemeFileTests.</summary>
public sealed class ThemeFileException : Exception
{
    public ThemeFileException(string id, string problem) : base($"{id}.json: {problem}") => Id = id;

    /// <summary>The theme file's id (its name without ".json").</summary>
    public string Id { get; }
}
