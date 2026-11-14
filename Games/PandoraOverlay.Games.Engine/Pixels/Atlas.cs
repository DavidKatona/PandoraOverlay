using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A sheet of pictures and its frame table, as the art tool writes them (A28):
/// one PNG and one JSON beside it, checked in under a game's <c>Art/</c>
/// folder and embedded in the game's own assembly, so art reaches a build with
/// no tool run and a game reads it with no file access (A29).
///
/// <para><b>The frame table</b> (written by <c>tools/games-art/</c>):</para>
/// <code>
/// { "image": "creatures.png",
///   "frames": { "Dryosaurus/stand/0": { "x": 0, "y": 0, "w": 36, "h": 25, "px": 18, "py": 24 } },
///   "animations": { "Dryosaurus/walk": { "frames": ["Dryosaurus/walk/0", "Dryosaurus/walk/1"], "ticks": [4, 4], "loops": true } } }
/// </code>
/// <para>A frame is named <c>&lt;subject&gt;/&lt;action&gt;/&lt;index&gt;</c>, an animation
/// <c>&lt;subject&gt;/&lt;action&gt;</c>; <c>px, py</c> is the frame's pivot
/// (<see cref="Sprite"/>); <c>ticks</c> are the engine's, 30 a second.
/// <c>animations</c> may be left out. Names are case-sensitive, as a reading
/// spells a species. <c>image</c> names the sheet; loaded from two streams the
/// atlas goes by it in its errors.</para>
///
/// <para><b>Checked at load, all of it.</b> A frame outside the image, a pivot
/// outside its frame (0 to w, 0 to h: a pivot may sit on the frame's far
/// edge), an animation naming a frame that isn't there, tick counts that don't
/// match the frames, a name listed twice: each is an
/// <see cref="InvalidDataException"/> naming the atlas and the entry. These are
/// build defects, and a game's tests load every atlas it ships, so they never
/// reach a player. A sprite asked for by a name the table lacks throws too,
/// naming both (<see cref="this[string]"/>); <see cref="TryGet"/> is for a
/// game that has a plan for missing art.</para>
///
/// <para><b>Immutable.</b> Once loaded nothing in an atlas changes: its tables
/// are read-only and its sheet is frozen (drawing ONTO it throws; every sprite
/// of the atlas draws FROM it). It holds no WPF object, so it is safe to keep
/// for the life of the process and to share between views.</para>
/// </summary>
public sealed partial class Atlas
{
    private readonly string _label;
    private readonly Dictionary<string, Sprite> _sprites;
    private readonly Dictionary<string, SpriteAnimation> _animations;

    private Atlas(string label, IReadOnlyList<Sprite> sprites, IReadOnlyList<SpriteAnimation> animations)
    {
        _label = label;
        _sprites = sprites.ToDictionary(s => s.Name, StringComparer.Ordinal);
        _animations = animations.ToDictionary(a => a.Name, StringComparer.Ordinal);
        Names = new ReadOnlyCollection<string>(sprites.Select(s => s.Name).ToArray());
        Animations = new ReadOnlyCollection<string>(animations.Select(a => a.Name).ToArray());
    }

    /// <summary>
    /// An atlas from a PNG and its frame table. The streams are read to their
    /// end and left open: they are the caller's. A PNG that can't be decoded,
    /// a table that isn't JSON or breaks a rule: <see cref="InvalidDataException"/>.
    /// </summary>
    public static Atlas Load(Stream png, Stream json)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(json);
        return Read(png, json, label: null);
    }

    /// <summary>
    /// The atlas embedded in a game's assembly as <c>Art/&lt;name&gt;.png</c> and
    /// <c>Art/&lt;name&gt;.json</c> (the one line in the game's csproj, A28). A
    /// resource that isn't there: <see cref="InvalidOperationException"/> naming
    /// it and the assembly.
    /// </summary>
    public static Atlas Load(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(name);
        string owner = assembly.GetName().Name ?? "an unnamed assembly";
        using var png = Open(assembly, owner, $"Art/{name}.png");
        using var json = Open(assembly, owner, $"Art/{name}.json");
        return Read(png, json, $"'{name}' in {owner}");
    }

    /// <summary>The sprite of this name. A name the table lacks throws <see cref="KeyNotFoundException"/>, naming it and the atlas.</summary>
    public Sprite this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return _sprites.TryGetValue(name, out var sprite)
                ? sprite
                : throw new KeyNotFoundException($"Atlas {_label} has no sprite '{name}'.");
        }
    }

    /// <summary>The sprite of this name, or false when the table lacks it.</summary>
    public bool TryGet(string name, [MaybeNullWhen(false)] out Sprite sprite)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _sprites.TryGetValue(name, out sprite);
    }

    /// <summary>The animation of this name. A name the table lacks throws <see cref="KeyNotFoundException"/>, naming it and the atlas.</summary>
    public SpriteAnimation Animation(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _animations.TryGetValue(name, out var animation)
            ? animation
            : throw new KeyNotFoundException($"Atlas {_label} has no animation '{name}'.");
    }

    /// <summary>Every sprite's name, in the order of the frame table.</summary>
    public IReadOnlyCollection<string> Names { get; }

    /// <summary>Every animation's name, in the order of the frame table.</summary>
    public IReadOnlyCollection<string> Animations { get; }

    /// <inheritdoc/>
    public override string ToString() => $"Atlas {_label}";

    private static Stream Open(Assembly assembly, string owner, string resource) =>
        assembly.GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException(
            $"The resource '{resource}' is not embedded in {owner}. A game's csproj embeds its Art folder as Art/<file>.");

    /// <summary>The table first (it names the atlas for every later error), then the sheet, then the two checked against each other.</summary>
    private static Atlas Read(Stream png, Stream json, string? label)
    {
        var table = Table.Parse(json, label);
        PixelBuffer sheet;
        try
        {
            sheet = PngPixels.Read(png);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Atlas {table.Label}: its image can't be read as a PNG ({e.GetType().Name}).", e);
        }
        var atlas = table.Build(sheet);
        sheet.Freeze();
        return atlas;
    }
}
