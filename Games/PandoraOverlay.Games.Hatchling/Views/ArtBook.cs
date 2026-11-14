using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Every piece of the game's art, found by what it shows: a subject, an action and
/// a frame (<c>&lt;subject&gt;/&lt;action&gt;/&lt;index&gt;</c>, the art tool's names, A28),
/// whichever atlas holds it. The painter and the tile ask it and nothing else, and
/// a piece it does not have is answered null: the painter then draws that piece's
/// stand-in (H30), so the game is playable, and its frames testable, before its art
/// exists, and an atlas that arrives later changes the pictures and no code.
///
/// <para><b>What falls back to what.</b> A size of your dino (H21) that is not drawn
/// is the next larger one that is: <c>Dryosaurus-s</c>, then <c>-m</c>, <c>-l</c>,
/// then the full-size <c>Dryosaurus</c>. An action that is not drawn is the subject's
/// <c>stand/0</c>. Anything else missing is null: a number's digit, a terrain's
/// tile, a mark. The size fallback is worked out once at load, as aliases, so a
/// scene may name the size it wants and never needs the book.</para>
///
/// <para><b>Cost.</b> Everything is laid out at load into read-only tables: a
/// lookup is two dictionary reads by the strings the scene already holds, so a
/// frame painted 30 times a second allocates nothing here, and one book can be
/// shared by every view and every test thread.</para>
///
/// <para>No WPF: the atlases are the engine's pixels (A17). One book is loaded per
/// game and kept (the game's view and its tile share it).</para>
/// </summary>
internal sealed class ArtBook
{
    /// <summary>The suffixes of your dino's sizes, smallest first; the full size has none (H21).</summary>
    private static readonly string[] SizeSuffixes = { "-s", "-m", "-l" };

    /// <summary>One action of one subject: its frames by index (a gap is null), and its animation when the table has one.</summary>
    private sealed class Entry
    {
        public Sprite?[] Frames = Array.Empty<Sprite?>();
        public SpriteAnimation? Animation;

        /// <summary>How many frames from 0 on are there with no gap: a tile's variants.</summary>
        public int Run
        {
            get
            {
                var n = 0;
                while (n < Frames.Length && Frames[n] is not null) n++;
                return n;
            }
        }
    }

    /// <summary>Subject, then action. Holds the size aliases too: an alias's value is the drawn size's own table.</summary>
    private readonly Dictionary<string, Dictionary<string, Entry>> _subjects;

    /// <summary>For each subject name the book answers (aliases included), the subject actually drawn.</summary>
    private readonly Dictionary<string, string> _drawnAs;

    private ArtBook(IReadOnlyList<string> atlases, Dictionary<string, Dictionary<string, Entry>> subjects,
        Dictionary<string, string> drawnAs)
    {
        Atlases = atlases;
        _subjects = subjects;
        _drawnAs = drawnAs;
    }

    /// <summary>No art at all: every lookup is null, and the painter draws its stand-ins only (H30). The golden frames are painted with it, so they never change when art does.</summary>
    public static ArtBook Empty { get; } = From(Array.Empty<(string, Atlas)>());

    /// <summary>The names of the atlases in the book, in the order they were read.</summary>
    public IReadOnlyList<string> Atlases { get; }

    /// <summary>
    /// Every atlas embedded in <paramref name="assembly"/> as <c>Art/&lt;name&gt;.png</c>
    /// with its <c>Art/&lt;name&gt;.json</c> (A28), whichever there are: <c>creatures</c>,
    /// <c>terrain</c>, <c>things</c>, <c>portraits</c>. One that is not embedded is
    /// simply absent; one embedded half (a sheet without its table) is a build defect
    /// the art tool's own test already fails, and is skipped here. One embedded but
    /// broken throws <see cref="InvalidDataException"/> from the engine's load, as
    /// does a piece of art two atlases both hold. The game passes its own assembly
    /// (A29: art is read from the assembly, never from a file).
    /// </summary>
    public static ArtBook Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        const string prefix = "Art/";
        var resources = new HashSet<string>(assembly.GetManifestResourceNames(), StringComparer.Ordinal);
        var names = resources
            .Where(r => r.StartsWith(prefix, StringComparison.Ordinal) && r.EndsWith(".png", StringComparison.Ordinal))
            .Select(r => r[prefix.Length..^".png".Length])
            .Where(name => resources.Contains($"{prefix}{name}.json"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        return From(names.Select(name => (name, Atlas.Load(assembly, name))).ToList());
    }

    /// <summary>A book of these atlases, named: what <see cref="Load"/> builds, and what a test builds from atlases it made.</summary>
    internal static ArtBook From(IReadOnlyList<(string Name, Atlas Atlas)> atlases)
    {
        ArgumentNullException.ThrowIfNull(atlases);
        var subjects = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (atlasName, atlas) in atlases)
        {
            foreach (var name in atlas.Names)
            {
                if (owner.TryGetValue(name, out var first))
                    throw new InvalidDataException($"The art '{name}' is in both atlas '{first}' and atlas '{atlasName}': a piece of art has one home.");
                owner[name] = atlasName;
                var (subject, action, index) = Parse(name, atlasName);
                var entry = EntryOf(subjects, subject, action);
                if (entry.Frames.Length <= index)
                    Array.Resize(ref entry.Frames, index + 1);
                entry.Frames[index] = atlas[name];
            }
            foreach (var name in atlas.Animations)
            {
                var slash = name.IndexOf('/');
                if (slash <= 0 || slash == name.Length - 1 || name.IndexOf('/', slash + 1) >= 0)
                    throw new InvalidDataException($"Atlas '{atlasName}' has an animation '{name}': an animation is named <subject>/<action>.");
                EntryOf(subjects, name[..slash], name[(slash + 1)..]).Animation = atlas.Animation(name);
            }
        }

        // Every subject drawn is drawn as itself...
        var drawnAs = subjects.Keys.ToDictionary(s => s, s => s, StringComparer.Ordinal);
        // ...and a size of a species not drawn is the next larger that is (H21).
        var bases = subjects.Keys.Select(BaseOf).Distinct(StringComparer.Ordinal).ToList();
        foreach (var species in bases)
        {
            for (var size = 0; size < SizeSuffixes.Length; size++)
            {
                var wanted = species + SizeSuffixes[size];
                if (subjects.ContainsKey(wanted)) continue;
                string? larger = null;
                for (var next = size + 1; next <= SizeSuffixes.Length && larger is null; next++)
                {
                    var candidate = next < SizeSuffixes.Length ? species + SizeSuffixes[next] : species;
                    if (subjects.ContainsKey(candidate)) larger = candidate;
                }
                if (larger is null) continue;
                drawnAs[wanted] = larger;
            }
        }
        // The aliases point at the drawn size's own table (after the loop: an alias is never resolved through another).
        foreach (var (asked, drawn) in drawnAs.ToList())
            if (asked != drawn) subjects[asked] = subjects[drawn];

        return new ArtBook(new ReadOnlyCollection<string>(atlases.Select(a => a.Name).ToArray()), subjects, drawnAs);
    }

    /// <summary>
    /// The picture of <paramref name="subject"/> doing <paramref name="action"/> at
    /// <paramref name="tick"/> of it: the animation's frame when the table has an
    /// animation for it (it loops or holds as the table says), else its frame 0. An
    /// action not drawn is the subject's <c>stand/0</c>; a subject not drawn, null.
    /// A size not drawn is the next larger (see the summary). Allocates nothing.
    /// </summary>
    public Sprite? Find(string subject, string action, int tick)
    {
        if (!_subjects.TryGetValue(subject, out var actions)) return null;
        if (actions.TryGetValue(action, out var entry))
            return entry.Animation?.At(tick) ?? FirstOf(entry);
        return actions.TryGetValue(ArtNames.Stand, out var stand) ? FirstOf(stand) : null;
    }

    /// <summary>
    /// Exactly frame <paramref name="index"/> of the action, or null: no fallback to
    /// another frame or action, because a digit 7 drawn as a 0 would say a wrong
    /// number. The painter asks a two-frame mark's ambient frame this way and frame 0
    /// when that one is missing. A size not drawn is the next larger.
    /// </summary>
    public Sprite? Frame(string subject, string action, int index)
    {
        if (index < 0 || !_subjects.TryGetValue(subject, out var actions) || !actions.TryGetValue(action, out var entry))
            return null;
        return index < entry.Frames.Length ? entry.Frames[index] : null;
    }

    /// <summary>How many frames from 0 on the action has with no gap (a terrain's tile variants); 0 when it is not drawn.</summary>
    public int Variants(string subject, string action) =>
        _subjects.TryGetValue(subject, out var actions) && actions.TryGetValue(action, out var entry) ? entry.Run : 0;

    /// <summary>
    /// The subject your dino of <paramref name="species"/> is drawn as at
    /// <paramref name="stage"/> (0 Hatchling to 4 Elder, H21): <c>&lt;Species&gt;-s</c>,
    /// <c>-m</c>, <c>-l</c>, or the full size for an Adult and an Elder, falling back
    /// to the next larger that exists; the full size when none of them does (its
    /// stand-in then stands for it). A stage outside 0 to 4 throws: a defect.
    /// </summary>
    public string SubjectFor(string species, int stage)
    {
        var wanted = StageSubject(species, stage);
        return _drawnAs.TryGetValue(wanted, out var drawn) ? drawn : species;
    }

    /// <summary>
    /// The subject of a size, with no art asked: what a scene names your sprite by
    /// (the book then answers it with the size actually drawn, so the scene never
    /// needs the book). Hatchling <c>-s</c>, Juvenile <c>-m</c>, Subadult <c>-l</c>,
    /// Adult and Elder the full size (an Elder is told by its mark, H21).
    /// </summary>
    internal static string StageSubject(string species, int stage)
    {
        ArgumentNullException.ThrowIfNull(species);
        return stage switch
        {
            Stages.Hatchling => species + SizeSuffixes[0],
            Stages.Juvenile => species + SizeSuffixes[1],
            Stages.Subadult => species + SizeSuffixes[2],
            Stages.Adult or Stages.Elder => species,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "A stage is 0 to 4."),
        };
    }

    private static Sprite? FirstOf(Entry entry) => entry.Frames.Length > 0 ? entry.Frames[0] : null;

    private static Entry EntryOf(Dictionary<string, Dictionary<string, Entry>> subjects, string subject, string action)
    {
        if (!subjects.TryGetValue(subject, out var actions))
            subjects[subject] = actions = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (!actions.TryGetValue(action, out var entry))
            actions[action] = entry = new Entry();
        return entry;
    }

    /// <summary>A subject without its size suffix: <c>Dryosaurus-s</c> is <c>Dryosaurus</c>; <c>num-hurt</c> stays itself.</summary>
    private static string BaseOf(string subject)
    {
        foreach (var suffix in SizeSuffixes)
            if (subject.Length > suffix.Length && subject.EndsWith(suffix, StringComparison.Ordinal))
                return subject[..^suffix.Length];
        return subject;
    }

    /// <summary>A frame's name as its three parts; anything else is a build defect, named.</summary>
    private static (string Subject, string Action, int Index) Parse(string name, string atlasName)
    {
        var parts = name.Split('/');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index))
            throw new InvalidDataException($"Atlas '{atlasName}' has a frame '{name}': a frame is named <subject>/<action>/<index>.");
        return (parts[0], parts[1], index);
    }
}

/// <summary>
/// The art's names, in one home: the subjects and actions the painter and the tile
/// ask the book for (A28's <c>&lt;subject&gt;/&lt;action&gt;/&lt;index&gt;</c>). The art tool
/// writes the same names; a rename is one edit here and one there.
/// </summary>
internal static class ArtNames
{
    // Terrain (atlas terrain).
    public const string Grass = "grass", Forest = "forest", Water = "water", Rock = "rock", Tile = "tile";
    public const string Fog = "fog", Unseen = "unseen", Dim = "dim";

    // Marks: on a cell (terrain) and above a head (things).
    public const string Mark = "mark";
    public const string Move = "move", Attack = "attack", Hover = "hover";
    public const string Hunt = "hunt", Search = "search", Dazed = "dazed", Slow = "slow", Bleed = "bleed";
    public const string You = "you", Hidden = "hidden", Elder = "elder";

    // Rings (things).
    public const string Ring = "ring", Hostile = "hostile", Apex = "apex", Acting = "acting";

    // Items and small game (things).
    public const string Plant = "plant", Meat = "meat", Pile = "pile", SmallGame = "smallgame";

    // Digits (things).
    public const string Ordinal = "ordinal", NumberHurt = "num-hurt", NumberGood = "num-good", Digit = "digit", Sign = "sign";

    // Every creature's.
    public const string Stand = "stand";

    /// <summary>A terrain's subject, by the terrain.</summary>
    public static string TerrainOf(Terrain ground) => ground switch
    {
        Terrain.Grass => Grass,
        Terrain.Forest => Forest,
        Terrain.Water => Water,
        Terrain.Rock => Rock,
        _ => throw new ArgumentOutOfRangeException(nameof(ground), ground, "Not a terrain."),
    };

    /// <summary>A predator's subject: its species, drawn at full size.</summary>
    public static string PredatorOf(PredatorType type) => PredatorSubjects[(int)type];

    private static readonly string[] PredatorSubjects = Enum.GetValues<PredatorType>().Select(t => t.ToString()).ToArray();
}
