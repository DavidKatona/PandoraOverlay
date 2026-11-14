using System.IO;
using System.Text.Json;

namespace PandoraOverlay.Games.Engine;

public sealed partial class Atlas
{
    /// <summary>How an atlas read from two streams is named when its table names no image (or can't be read at all).</summary>
    private const string FromStreams = "(loaded from streams)";

    /// <summary>
    /// The frame table, read and checked in two halves: its own shape first
    /// (every field there and of its kind, no name twice), with no sheet yet,
    /// so a broken table is reported as such even when the PNG is broken too;
    /// then <see cref="Build"/>, against the sheet's size. Every failure is an
    /// <see cref="InvalidDataException"/> naming the atlas and the entry.
    /// </summary>
    private sealed class Table
    {
        private readonly List<FrameEntry> _frames = new();
        private readonly List<AnimationEntry> _animations = new();

        private Table(string label) => Label = label;

        /// <summary>How the errors name the atlas: <c>'creatures' in PandoraOverlay.Games.X</c>, or <c>'creatures.png'</c> from streams.</summary>
        public string Label { get; private set; }

        public static Table Parse(Stream json, string? label)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException(
                    $"Atlas {label ?? FromStreams}: its frame table is not valid JSON (line {e.LineNumber + 1}).", e);
            }

            using (document)
            {
                var table = new Table(label ?? FromStreams);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw table.Bad("its frame table is not a JSON object");

                if (root.TryGetProperty("image", out var image))
                {
                    if (image.ValueKind != JsonValueKind.String)
                        throw table.Bad("'image' is not a string");
                    if (label is null && image.GetString() is { Length: > 0 } named)
                        table.Label = $"'{named}'";
                }

                if (!root.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Object)
                    throw table.Bad("'frames' is missing or not an object");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in frames.EnumerateObject())
                {
                    string where = table.Named("frame", entry.Name, seen);
                    var value = entry.Value;
                    if (value.ValueKind != JsonValueKind.Object)
                        throw table.Bad($"{where} is not an object");
                    table._frames.Add(new FrameEntry(
                        entry.Name,
                        table.Int(value, "x", where), table.Int(value, "y", where),
                        table.Int(value, "w", where), table.Int(value, "h", where),
                        table.Int(value, "px", where), table.Int(value, "py", where)));
                }

                // Left out is fine: a sheet of still pictures has no animation.
                if (root.TryGetProperty("animations", out var animations))
                {
                    if (animations.ValueKind != JsonValueKind.Object)
                        throw table.Bad("'animations' is not an object");
                    seen.Clear();
                    foreach (var entry in animations.EnumerateObject())
                    {
                        string where = table.Named("animation", entry.Name, seen);
                        var value = entry.Value;
                        if (value.ValueKind != JsonValueKind.Object)
                            throw table.Bad($"{where} is not an object");
                        var names = table.Array(value, "frames", where)
                            .Select(f => f.ValueKind == JsonValueKind.String ? f.GetString()! : throw table.Bad($"{where}: 'frames' holds something that is not a frame's name"))
                            .ToArray();
                        var ticks = table.Array(value, "ticks", where)
                            .Select(t => t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out int n) ? n : throw table.Bad($"{where}: 'ticks' holds something that is not a whole number"))
                            .ToArray();
                        if (!value.TryGetProperty("loops", out var loops) || loops.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw table.Bad($"{where}: 'loops' is missing or not true or false");
                        table._animations.Add(new AnimationEntry(entry.Name, names, ticks, loops.GetBoolean()));
                    }
                }
                return table;
            }
        }

        /// <summary>The sprites on the sheet and the animations over them, every entry checked against the sheet.</summary>
        public Atlas Build(PixelBuffer sheet)
        {
            var sprites = new List<Sprite>(_frames.Count);
            var byName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var f in _frames)
            {
                string where = $"frame '{f.Name}'";
                if (f.W < 1 || f.H < 1)
                    throw Bad($"{where} is {f.W} x {f.H}: each side is at least 1");
                if (f.X < 0 || f.Y < 0 || (long)f.X + f.W > sheet.Width || (long)f.Y + f.H > sheet.Height)
                    throw Bad($"{where} ({f.X}, {f.Y}, {f.W} x {f.H}) lies outside the {sheet.Width} x {sheet.Height} image");
                if (f.Px < 0 || f.Px > f.W || f.Py < 0 || f.Py > f.H)
                    throw Bad($"{where}: its pivot ({f.Px}, {f.Py}) lies outside its {f.W} x {f.H} frame");
                var sprite = new Sprite(f.Name, sheet, new PixelRect(f.X, f.Y, f.W, f.H), f.Px, f.Py);
                sprites.Add(sprite);
                byName.Add(f.Name, sprite);
            }

            var animations = new List<SpriteAnimation>(_animations.Count);
            foreach (var a in _animations)
            {
                string where = $"animation '{a.Name}'";
                if (a.Frames.Length == 0)
                    throw Bad($"{where} has no frame");
                if (a.Ticks.Length != a.Frames.Length)
                    throw Bad($"{where} has {a.Frames.Length} frames and {a.Ticks.Length} tick counts: one count per frame");
                var frames = new Sprite[a.Frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    if (!byName.TryGetValue(a.Frames[i], out var frame))
                        throw Bad($"{where} names the frame '{a.Frames[i]}', which is not in the table");
                    if (a.Ticks[i] < 1)
                        throw Bad($"{where} shows '{a.Frames[i]}' for {a.Ticks[i]} ticks: every frame shows for at least 1");
                    frames[i] = frame;
                }
                try
                {
                    animations.Add(new SpriteAnimation(a.Name, frames, a.Ticks, a.Loops));
                }
                catch (ArgumentException e)
                {
                    throw Bad($"{where}: {e.Message}"); // what the checks above don't cover: a total past an int
                }
            }
            return new Atlas(Label, sprites, animations);
        }

        /// <summary>An entry's name checked (not empty, not listed twice) and turned into how the errors name it.</summary>
        private string Named(string kind, string name, HashSet<string> seen)
        {
            string where = $"{kind} '{name}'";
            if (name.Length == 0)
                throw Bad($"a {kind} has an empty name");
            if (!seen.Add(name))
                throw Bad($"{where} is listed twice");
            return where;
        }

        private int Int(JsonElement entry, string property, string where) =>
            entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n)
                ? n
                : throw Bad($"{where}: '{property}' is missing or not a whole number");

        private JsonElement.ArrayEnumerator Array(JsonElement entry, string property, string where) =>
            entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : throw Bad($"{where}: '{property}' is missing or not a list");

        private InvalidDataException Bad(string what) => new($"Atlas {Label}, {what}.");
    }

    private sealed record FrameEntry(string Name, int X, int Y, int W, int H, int Px, int Py);

    private sealed record AnimationEntry(string Name, string[] Frames, int[] Ticks, bool Loops);
}
