namespace PandoraOverlay;

/// <summary>Plays a gate's sounds on the PC's default audio device (A80), through one channel of the app's one speaker (A101).
/// Called on the UI thread only; returns at once, never waits for the device, never throws.</summary>
/// <remarks>
/// Every rule above the device (which instance may sound, the mute, the
/// steps, the limits) is the gate's: the games' gate, <see cref="GamesSound"/>,
/// tested with a fake speaker; the device is only the speaker's. The real one
/// is a channel of <see cref="WaveOutSpeaker"/> (one stream, our own mix, a
/// thread of the app's own that exists only while something plays), which
/// MainWindow makes once and hands each gate as a view of its channel
/// (<see cref="WaveOutSpeaker.Channel"/>, A101); <see cref="NullSpeaker"/> is
/// what a host made without a gate of its own plays through. Whatever it is,
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

    /// <summary>Every sound playing faded to silence within 5 ms, and dropped: on this
    /// speaker's channel only, so another gate's sounds play on (A101). It acts on
    /// what plays at the call only: with nothing playing it does nothing (no thread, no
    /// device, nothing kept for later), and a <see cref="Play"/> after it is heard whole,
    /// since the gate calls it at every stage at which nobody, or another instance, is
    /// heard (A88).</summary>
    void StopAll();
}

/// <summary>
/// The speaker that plays nothing: what a <see cref="GamesHost"/> made
/// without a sound gate of its own plays through (the tests, the render
/// harness). No device, no thread, nothing kept.
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
