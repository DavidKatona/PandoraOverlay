namespace PandoraOverlay;

/// <summary>Plays the Games window's sounds on the PC's default audio device (A80).
/// Called on the UI thread only; returns at once, never waits for the device, never throws.</summary>
/// <remarks>
/// Every rule above the device (which instance may sound, the mute, the
/// steps, the limits) is <see cref="GamesSound"/>'s, tested with a fake
/// speaker; the device is only the speaker's. The real one (one stream, our
/// own mix, a thread of the host's own that exists only while something
/// plays) replaces <see cref="NullSpeaker"/> in a later step. Whatever it is,
/// it never touches the game process, makes no request and starts no timer
/// that leads to one (A86), and it never sets the overlay's own volume in
/// Windows' mixer: that level is the player's (A80).
/// </remarks>
internal interface ISpeaker : IDisposable
{
    /// <summary>One sound, from now: mono 16-bit samples at GameSound.SampleRate, scaled by
    /// gain (0 &lt; gain &lt;= 1). The array is the caller's own copy and never changes after:
    /// the speaker reads it on its own thread.</summary>
    void Play(short[] samples, float gain);

    /// <summary>Every sound playing faded to silence within 5 ms, and dropped.</summary>
    void StopAll();
}

/// <summary>
/// The speaker that plays nothing: what the Games window's sound goes
/// through until the real speaker is wired (A80), and what a host made
/// without one gets (the tests, the render harness). No device, no thread,
/// nothing kept.
/// </summary>
internal sealed class NullSpeaker : ISpeaker
{
    public void Play(short[] samples, float gain)
    {
    }

    public void StopAll()
    {
    }

    public void Dispose()
    {
    }
}
