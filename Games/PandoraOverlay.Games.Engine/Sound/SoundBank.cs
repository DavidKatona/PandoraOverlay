using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A game's sounds, read once from its own assembly (A78), as an
/// <see cref="Atlas"/> is its art: every WAV under the game's <c>Sound/</c>
/// folder, embedded by one line of its csproj
/// (<c>&lt;EmbeddedResource Include="Sound\*.wav" LogicalName="Sound/%(Filename)%(Extension)" /&gt;</c>),
/// becomes a <see cref="GameSound"/> named after its file. So sounds reach a
/// build with no tool run, a game reads them with no file access (the
/// forbidden list, A29: streams and the assembly's own manifest resources
/// only, A60), and the host, which plays them, never reads a game's assembly
/// or parses a file (A78).
///
/// <para><b>The one reader.</b> <see cref="Read"/> is the only place a WAV is
/// parsed, here or in the app, and it holds the format every game must keep,
/// the Contract's (<see cref="GameSound"/>): RIFF, WAVE, PCM, one channel,
/// 44,100 samples a second, 16 bits, 1 to <see cref="GameSound.MaxSamples"/>
/// samples. Anything else is an <see cref="InvalidDataException"/> naming the
/// sound and the field (<c>Sound 'x': 2 channels, not 1</c>): a build defect,
/// which a game's tests catch by loading its bank, so it never reaches a
/// player. The engine's by A21: a second game uses it unchanged, and it guards
/// a rule every game keeps.</para>
///
/// <para><b>Immutable.</b> Once loaded nothing in a bank changes, and it holds
/// no WPF object: safe to keep for the life of the process and to share
/// between views. A game makes one bank per instance, or one for good; each
/// sound is one object, which matters to the host: it copies a sound's
/// samples the first time it plays that object, and tells "the same sound
/// twice within a moment" by the object (A81).</para>
/// </summary>
public sealed partial class SoundBank
{
    /// <summary>Where a game's sounds sit among its manifest resources: <c>Sound/&lt;name&gt;.wav</c>.</summary>
    internal const string Folder = "Sound/";

    /// <summary>A sound's resource ends in this, written in lower case as the generator writes it.</summary>
    internal const string Extension = ".wav";

    private readonly string _owner;
    private readonly Dictionary<string, GameSound> _sounds;

    private SoundBank(string owner, IReadOnlyList<GameSound> sounds)
    {
        _owner = owner;
        _sounds = sounds.ToDictionary(s => s.Name, StringComparer.Ordinal);
        Names = new ReadOnlyCollection<string>(sounds.Select(s => s.Name).ToArray());
    }

    /// <summary>
    /// Every sound embedded in <paramref name="assembly"/> as
    /// <c>Sound/&lt;name&gt;.wav</c>, read once, named without the folder and
    /// the extension, in name order. An assembly with none gives an empty bank:
    /// a game with no sounds plays none. A sound that breaks the format:
    /// <see cref="InvalidDataException"/>, naming it (<see cref="Read"/>).
    /// </summary>
    public static SoundBank Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string owner = assembly.GetName().Name ?? "an unnamed assembly";
        var sounds = new List<GameSound>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(IsSound).OrderBy(r => r, StringComparer.Ordinal))
        {
            using var wav = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The resource '{resource}' is listed by {owner} but can't be opened.");
            sounds.Add(Read(wav, NameOf(resource)));
        }
        return new SoundBank(owner, sounds);
    }

    /// <summary>
    /// One WAV into a <see cref="GameSound"/> named <paramref name="name"/>:
    /// the one reader (the class's summary). The stream is read from where it
    /// is up to the end of its <c>data</c> chunk and left open: it is the
    /// caller's. Anything but the Contract's format:
    /// <see cref="InvalidDataException"/> whose message names the sound and
    /// the field.
    /// </summary>
    public static GameSound Read(Stream wav, string name)
    {
        ArgumentNullException.ThrowIfNull(wav);
        ArgumentNullException.ThrowIfNull(name);
        return new GameSound(name, Wav.Read(wav, name));
    }

    /// <summary>The sound of this name. A name the bank lacks throws <see cref="KeyNotFoundException"/>, naming it and the assembly: a build defect the tests catch, as the atlas's.</summary>
    public GameSound this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return _sounds.TryGetValue(name, out var sound)
                ? sound
                : throw new KeyNotFoundException($"The sounds of {_owner} have no '{name}'. A game embeds its Sound folder as {Folder}<name>{Extension}.");
        }
    }

    /// <summary>The sound of this name, or false when the bank lacks it.</summary>
    public bool TryGet(string name, [MaybeNullWhen(false)] out GameSound sound)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _sounds.TryGetValue(name, out sound);
    }

    /// <summary>Every sound's name, in name order (ordinal).</summary>
    public IReadOnlyCollection<string> Names { get; }

    /// <inheritdoc/>
    public override string ToString() => $"Sounds of {_owner} ({Names.Count})";

    /// <summary>A manifest resource that is a sound: <c>Sound/</c>, a name of at least one character, <c>.wav</c>.</summary>
    internal static bool IsSound(string resource) =>
        resource.Length > Folder.Length + Extension.Length
        && resource.StartsWith(Folder, StringComparison.Ordinal)
        && resource.EndsWith(Extension, StringComparison.Ordinal);

    /// <summary>A sound resource's name: without the folder and the extension.</summary>
    internal static string NameOf(string resource) => resource[Folder.Length..^Extension.Length];
}
