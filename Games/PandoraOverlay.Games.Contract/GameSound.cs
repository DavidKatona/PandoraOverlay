namespace PandoraOverlay.Games;

/// <summary>
/// One of a game's sounds (A78): a name for the log, and mono 16-bit samples
/// at <see cref="SampleRate"/>, at most <see cref="MaxSamples"/>. The host
/// copies them the first time it plays this object and refuses an empty or
/// longer one, logging it once.
/// <para>
/// The format is the Contract's rule, so every game keeps the same one: a
/// game's sounds are WAVs embedded in its own assembly, read once by the
/// engine's one reader (<c>SoundBank</c>, A78), which refuses any other
/// format. 44,100 a second is the rate the sounds were first auditioned at;
/// mono 16-bit is all a short cue needs, and Windows resamples to the
/// device's own rate as it does for every program.
/// </para>
/// <para>
/// The host never reads a game's assembly and parses no file: it is handed
/// these objects. It takes its own copy of the samples the first time it
/// plays one (keyed by the object), so what plays never reads memory a game
/// still holds; the name is used only in the log and to tell the same sound
/// twice within a moment (A81). Two records with equal fields are still two
/// sounds to the host: it goes by the object.
/// </para>
/// </summary>
/// <param name="Name">The sound's name, for the log only (a fact: the game's own word for its cue).</param>
/// <param name="Samples">Mono 16-bit samples at <see cref="SampleRate"/>, 1 to <see cref="MaxSamples"/> of them.</param>
public sealed record GameSound(string Name, ReadOnlyMemory<short> Samples)
{
    /// <summary>Samples a second: 44,100, the rate the first sound board was rendered and heard at (A78).</summary>
    public const int SampleRate = 44_100;

    /// <summary>The most samples a sound may have: 2 s. A game's cues are under a second; anything longer is a mistake, refused (A78, A81).</summary>
    public const int MaxSamples = 2 * SampleRate;
}
