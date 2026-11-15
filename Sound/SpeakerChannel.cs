namespace PandoraOverlay;

/// <summary>
/// Which gate a sound of the app's one speaker belongs to (A101): the games'
/// gate (<c>GamesSound</c>: a game's cues and the Games window's preview) or
/// the click gate (G27, A104: the overlay's clicks and a game's). A voice
/// carries its channel, and a stop is per channel, so a game's stop (every
/// stage at which nobody, or another instance, is heard, A88) never fades a
/// click played just before it, and a click never touches a game's sound.
/// Both play on one stream to the one device (one open, one thread, one idle
/// close, one failure line), and the mix's voices are shared by both.
/// </summary>
internal enum SpeakerChannel
{
    /// <summary>The games' gate: a game's sounds and the Games window's own preview (A77, A82).</summary>
    Games,

    /// <summary>The click gate: every click the player makes, in the overlay and in the games (G27, A98).</summary>
    Clicks,
}

/// <summary>
/// One channel of the app's speaker as a gate sees it (A101): an
/// <see cref="ISpeaker"/> whose <see cref="Play"/> and <see cref="StopAll"/>
/// are the speaker's own on this channel, so a gate plays and stops through
/// it as it would through a speaker of its own and never names a channel
/// (<c>GamesSound</c> and its tests are unchanged by the second channel). Made
/// by <see cref="WaveOutSpeaker.Channel"/>; it holds nothing but the speaker
/// and the channel.
/// <para>
/// <b>Its Dispose does nothing.</b> The speaker is MainWindow's, which
/// disposes it once at the exit, after every gate (A101): a gate disposes
/// what it was handed (the games' gate does, at the exit), and that must not
/// close the stream the other gate plays on. So after a view's Dispose the
/// speaker runs on, and a play through the view, or through the other
/// channel's, is heard.
/// </para>
/// </summary>
internal sealed class SpeakerChannelView : ISpeaker
{
    private readonly WaveOutSpeaker _speaker;

    /// <param name="speaker">The app's speaker, which plays and stops for this view.</param>
    /// <param name="channel">The channel every play and stop through this view takes.</param>
    public SpeakerChannelView(WaveOutSpeaker speaker, SpeakerChannel channel)
    {
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        Channel = channel;
    }

    /// <summary>The channel every play and stop through this view takes.</summary>
    public SpeakerChannel Channel { get; }

    /// <summary>The speaker's <see cref="WaveOutSpeaker.Play"/> on this channel: returns at once, never throws.</summary>
    public void Play(short[] samples, float gain) => _speaker.Play(samples, gain, Channel);

    /// <summary>The speaker's <see cref="WaveOutSpeaker.StopAll"/> on this channel: what this channel plays at the call, faded; the other channel's sounds play on (A88, A101).</summary>
    public void StopAll() => _speaker.StopAll(Channel);

    /// <summary>Nothing: the speaker is its owner's to dispose, after every gate (A101).</summary>
    public void Dispose()
    {
    }
}
