using System.Reflection;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The game's sounds, one <see cref="GameSound"/> per <see cref="Cue"/> (H95, A78): read
/// once from the game's own assembly through the engine's one reader
/// (<see cref="SoundBank.Load"/>), as the <see cref="ArtBook"/> is its art, and kept by
/// the game for the life of its instance (<c>HatchlingGame</c>, at its first view). The
/// same object every time a cue plays, which the host needs: it copies a sound's
/// samples the first time it plays that object, and tells "the same sound twice within
/// a moment" by the object (A81).
///
/// <para><b>Every cue has its sound, or the load throws</b>, naming the cue and its
/// file: a build defect (a WAV missing from <c>Sound/</c>, or the csproj's line gone),
/// which the game's tests catch by loading the book, so it never reaches a player.
/// Nothing else of the sound is here: its level and recipe are the sound tool's
/// (<c>tools/games-sound/hatchling/cues.js</c>), its playing the host's.</para>
/// </summary>
internal sealed class SoundBook
{
    private readonly GameSound[] _sounds;

    private SoundBook(GameSound[] sounds) => _sounds = sounds;

    /// <summary>
    /// The sounds of every cue, from the WAVs embedded in <paramref name="assembly"/> as
    /// <c>Sound/&lt;file&gt;.wav</c> (<see cref="Cues.All"/>). A cue whose file is not
    /// there: <see cref="InvalidOperationException"/> naming it; a file that breaks the
    /// format: the reader's <c>InvalidDataException</c>, naming it.
    /// </summary>
    public static SoundBook Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var bank = SoundBank.Load(assembly);
        var count = Enum.GetValues<Cue>().Length;
        var sounds = new GameSound[count];
        foreach (var (cue, file) in Cues.All)
        {
            if (!bank.TryGet(file, out var sound))
                throw new InvalidOperationException(
                    $"Hatchling's cue {cue} has no sound: Sound/{file}.wav is not embedded in {assembly.GetName().Name} (H95, A78).");
            sounds[(int)cue] = sound;
        }
        for (var i = 0; i < count; i++)
            if (sounds[i] is null)
                throw new InvalidOperationException($"Hatchling's cue {(Cue)i} is not in Cues.All (H95).");
        return new SoundBook(sounds);
    }

    /// <summary>The sound of <paramref name="cue"/>: the same object every time.</summary>
    public GameSound this[Cue cue] => _sounds[(int)cue];
}
