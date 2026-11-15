namespace PandoraOverlay;

/// <summary>
/// The app's speaker's mix (A80, A81, A101): the voices sounding now, summed
/// into each buffer the device is handed. Pure and single-threaded: only the
/// speaker's thread calls it, between two buffers, so every rule of the sound
/// itself is a unit test here, with no device and no thread.
/// <para>
/// <b>A voice</b> is one sound from its first sample: its samples (the gate's
/// own copy, never changed after) times its gain, which is its gate's level
/// at its play (the Games window's step, A82); it goes at its last sample. It
/// carries its channel (<see cref="SpeakerChannel"/>, A101): the gate it came
/// from. At most <see cref="MaxVoices"/> sound at once, whatever their
/// channels: a ninth is dropped, never one already sounding (A81: a second
/// guard behind the gates, the games' 6 with the preview among them and the
/// click gate's one at a time, so a burst that slips past a gate costs a
/// little CPU, never the overlay).
/// </para>
/// <para>
/// <b>A stop</b> (<see cref="StopAll"/>) is one channel's: it fades each of
/// that channel's voices linearly from where it is to nothing over
/// <see cref="FadeSamples"/>, then drops it, and leaves the other channel's
/// voices whole (A101: a game's stop never fades a click played just before
/// it). A cut in the middle of a waveform is a click, a 5 ms ramp is not
/// (A79). A voice already fading keeps its fade, so a second stop never
/// lengthens one or lifts it back up.
/// </para>
/// <para>
/// <b>The sum</b> is in floats, each sample rounded (half away from zero)
/// and held to 16 bits, so two loud sounds at once clip at full scale
/// instead of wrapping round to the other sign, which is a crack.
/// </para>
/// </summary>
internal sealed class SpeakerMix
{
    /// <summary>The most voices at once, both channels together (A81, A101): a second guard behind the games' gate's 6 (<see cref="GamesSound.MaxVoices"/>) and the click gate's one at a time.</summary>
    public const int MaxVoices = 8;

    /// <summary>A stop's fade: 220 samples, 5 ms at 44,100 Hz (A79, A80): long enough not to click, short enough to read as "stopped".</summary>
    public const int FadeSamples = 220;

    private readonly List<Voice> _voices = new(MaxVoices);

    /// <summary>The sum of one buffer before it is rounded: grown once to the buffer's length, then reused, so a fill allocates nothing.</summary>
    private float[] _sum = Array.Empty<float>();

    /// <summary>No voice sounds, on either channel.</summary>
    public bool Idle => _voices.Count == 0;

    /// <summary>How many voices sound now, on both channels, fading ones included.</summary>
    public int Voices => _voices.Count;

    /// <summary>
    /// A voice on this channel from its first sample at the next
    /// <see cref="Fill"/>; dropped when <see cref="MaxVoices"/> already sound,
    /// whatever their channels. Nothing to play (no samples, or a gain that is
    /// not above 0) adds nothing; a gain above 1 is played at 1, as
    /// <see cref="ISpeaker.Play"/> promises no more.
    /// </summary>
    public void Add(short[]? samples, float gain, SpeakerChannel channel)
    {
        if (samples is null || samples.Length == 0 || !(gain > 0)) return; // !(> 0) is NaN's refusal too
        if (_voices.Count >= MaxVoices) return;
        _voices.Add(new Voice(samples, Math.Min(gain, 1f), channel));
    }

    /// <summary>Every voice of this channel faded from where it is to nothing over <see cref="FadeSamples"/>, then dropped; one already fading keeps its fade; the other channel's voices play on whole (A101).</summary>
    public void StopAll(SpeakerChannel channel)
    {
        foreach (var voice in _voices)
        {
            if (voice.Channel == channel) voice.Fade();
        }
    }

    /// <summary>
    /// The next buffer: each sample the sum of every voice's sample times its
    /// gain times its fade, rounded and held to −32,768..32,767; a voice that
    /// reaches its end in it, or the end of its fade, goes; silence where
    /// nothing sounds. True when any voice sounded in it.
    /// </summary>
    public bool Fill(Span<short> buffer)
    {
        if (_voices.Count == 0)
        {
            buffer.Clear();
            return false;
        }
        if (_sum.Length < buffer.Length) _sum = new float[buffer.Length];
        var sum = _sum.AsSpan(0, buffer.Length);
        sum.Clear();
        for (var v = _voices.Count - 1; v >= 0; v--)
        {
            if (!_voices[v].MixInto(sum)) _voices.RemoveAt(v);
        }
        for (var i = 0; i < buffer.Length; i++) buffer[i] = ToSample(sum[i]);
        return true;
    }

    /// <summary>A summed value as a 16-bit sample: rounded half away from zero, then held to the 16 bits.</summary>
    internal static short ToSample(float value) =>
        (short)Math.Clamp(MathF.Round(value, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);

    /// <summary>One sound in the mix: its channel, where it is, and its fade once stopped.</summary>
    private sealed class Voice
    {
        private readonly short[] _samples;
        private readonly float _gain;
        private int _position;

        /// <summary>The fade's samples still to play once stopped; -1 while not fading.</summary>
        private int _fadeLeft = -1;

        public Voice(short[] samples, float gain, SpeakerChannel channel)
        {
            _samples = samples;
            _gain = gain;
            Channel = channel;
        }

        /// <summary>The gate this voice came from: only a stop of this channel fades it (A101).</summary>
        public SpeakerChannel Channel { get; }

        public void Fade()
        {
            if (_fadeLeft < 0) _fadeLeft = FadeSamples;
        }

        /// <summary>This voice's next samples added into the sum; false when it has ended (its last sample, or its fade's).</summary>
        public bool MixInto(Span<float> sum)
        {
            var count = Math.Min(sum.Length, _samples.Length - _position);
            if (_fadeLeft < 0)
            {
                for (var k = 0; k < count; k++) sum[k] += _samples[_position + k] * _gain;
                _position += count;
                return _position < _samples.Length;
            }
            // The k-th sample of the fade plays at (FadeSamples - 1 - k) / FadeSamples
            // of the voice: just under where it was at the stop (never a step up),
            // falling by the same amount each sample, to 0 at the fade's last.
            count = Math.Min(count, _fadeLeft);
            for (var k = 0; k < count; k++)
            {
                _fadeLeft--;
                sum[k] += _samples[_position + k] * _gain * (_fadeLeft / (float)FadeSamples);
            }
            _position += count;
            return _fadeLeft > 0 && _position < _samples.Length;
        }
    }
}
