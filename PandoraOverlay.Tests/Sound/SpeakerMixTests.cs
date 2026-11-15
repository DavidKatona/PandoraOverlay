using PandoraOverlay.Games;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The speaker's mix (<see cref="SpeakerMix"/>; A80, A81): every rule of the
/// sound itself, with no device and no thread. A voice from its first
/// sample, scaled by its gain; voices summed in floats, rounded and held to
/// 16 bits; a voice that ends goes; a stop fades each voice from where it is
/// to nothing over 220 samples, linearly; at most 8 voices; silence when
/// nothing sounds.
/// </summary>
public class SpeakerMixTests
{
    private static short[] Filled(SpeakerMix mix, int length, out bool sounded)
    {
        var buffer = new short[length];
        sounded = mix.Fill(buffer);
        return buffer;
    }

    private static short[] Filled(SpeakerMix mix, int length) => Filled(mix, length, out _);

    private static short[] Constant(short value, int length) => Enumerable.Repeat(value, length).ToArray();

    [Fact]
    public void OneVoiceAtGainOneGivesItsSamplesBack()
    {
        var samples = new short[] { 0, 1, -1, 1234, -1234, short.MaxValue, short.MinValue, 7 };
        var mix = new SpeakerMix();
        mix.Add(samples, 1f, SpeakerChannel.Games);

        var buffer = Filled(mix, samples.Length, out var sounded);

        Assert.Equal(samples, buffer);
        Assert.True(sounded);
    }

    [Fact]
    public void AtHalfGainEachSampleIsHalvedAndRoundedHalfAwayFromZero()
    {
        var mix = new SpeakerMix();
        mix.Add(new short[] { 1, 3, -3, 5, 1001, -1001, 2000, short.MinValue }, 0.5f, SpeakerChannel.Games);

        Assert.Equal(new short[] { 1, 2, -2, 3, 501, -501, 1000, -16384 }, Filled(mix, 8));
    }

    [Fact]
    public void TwoVoicesSumSampleBySample()
    {
        var mix = new SpeakerMix();
        mix.Add(new short[] { 100, 200, 300, 400 }, 1f, SpeakerChannel.Games);
        mix.Add(new short[] { 10, -20, 30 }, 0.5f, SpeakerChannel.Games);

        Assert.Equal(new short[] { 105, 190, 315, 400, 0 }, Filled(mix, 5));
    }

    [Fact]
    public void TheSumIsHeldToSixteenBitsAtBothEnds()
    {
        var loud = new SpeakerMix();
        loud.Add(Constant(30_000, 4), 1f, SpeakerChannel.Games);
        loud.Add(Constant(30_000, 4), 1f, SpeakerChannel.Games);
        var low = new SpeakerMix();
        low.Add(Constant(-30_000, 4), 1f, SpeakerChannel.Games);
        low.Add(Constant(-30_000, 4), 1f, SpeakerChannel.Games);

        Assert.Equal(Constant(short.MaxValue, 4), Filled(loud, 4)); // never wrapped round to the other sign
        Assert.Equal(Constant(short.MinValue, 4), Filled(low, 4));
    }

    [Fact]
    public void AVoiceContinuesExactlyAcrossTwoBuffersAndGoesAtItsEnd()
    {
        var samples = Enumerable.Range(1, 700).Select(i => (short)i).ToArray();
        var mix = new SpeakerMix();
        mix.Add(samples, 1f, SpeakerChannel.Games);

        var first = Filled(mix, 441);
        Assert.False(mix.Idle);
        var second = Filled(mix, 441, out var sounded);

        Assert.Equal(samples[..441], first);
        Assert.Equal(samples[441..], second[..259]);
        Assert.All(second[259..], s => Assert.Equal(0, s));
        Assert.True(sounded);
        Assert.True(mix.Idle);
        Assert.False(mix.Fill(new short[441]));
    }

    [Fact]
    public void AStopFadesFromTheCurrentLevelToNothingIn220SamplesLinearly()
    {
        var mix = new SpeakerMix();
        mix.Add(Constant(22_000, 10_000), 1f, SpeakerChannel.Games);
        Assert.All(Filled(mix, 100), s => Assert.Equal(22_000, s));

        mix.StopAll(SpeakerChannel.Games);
        var fade = Filled(mix, 300);

        Assert.Equal(220, SpeakerMix.FadeSamples);
        for (var k = 0; k < SpeakerMix.FadeSamples; k++)
            Assert.InRange(fade[k], 22_000 * (219 - k) / 220 - 1, 22_000 * (219 - k) / 220 + 1); // 100 down each sample
        Assert.Equal(0, fade[SpeakerMix.FadeSamples - 1]); // nothing at the fade's last sample
        Assert.All(fade[SpeakerMix.FadeSamples..], s => Assert.Equal(0, s));
        Assert.True(fade[0] < 22_000); // the first sample after the stop is already lower: never a step up
        for (var i = 1; i < fade.Length; i++) Assert.True(fade[i] <= fade[i - 1], $"a step up at {i}");
        Assert.True(mix.Idle);
    }

    [Fact]
    public void AStopFadesEveryVoiceAndAVoiceAddedAfterItIsWhole()
    {
        var mix = new SpeakerMix();
        mix.Add(Constant(1000, 5000), 1f, SpeakerChannel.Games);
        mix.Add(Constant(2000, 5000), 1f, SpeakerChannel.Games);
        mix.StopAll(SpeakerChannel.Games);
        mix.Add(Constant(500, 400), 1f, SpeakerChannel.Games); // after the stop: not faded

        var buffer = Filled(mix, 300);

        Assert.InRange(buffer[0], 3000 * 219 / 220 + 500 - 1, 3000 * 219 / 220 + 500 + 1);
        Assert.Equal(500, buffer[SpeakerMix.FadeSamples]);
        Assert.Equal(500, buffer[299]);
        Assert.Equal(1, mix.Voices);
    }

    [Fact]
    public void AVoiceAlreadyFadingKeepsItsFade()
    {
        var once = new SpeakerMix();
        var twice = new SpeakerMix();
        once.Add(Constant(10_000, 1000), 1f, SpeakerChannel.Games);
        twice.Add(Constant(10_000, 1000), 1f, SpeakerChannel.Games);
        once.StopAll(SpeakerChannel.Games);
        twice.StopAll(SpeakerChannel.Games);

        var a = Filled(once, 100).Concat(Filled(once, 200));
        var first = Filled(twice, 100);
        twice.StopAll(SpeakerChannel.Games); // a second stop: neither restarted nor lengthened
        var b = first.Concat(Filled(twice, 200));

        Assert.Equal(a, b);
        Assert.True(twice.Idle);
    }

    [Fact]
    public void ANinthVoiceIsDropped()
    {
        var mix = new SpeakerMix();
        for (var v = 0; v < SpeakerMix.MaxVoices + 1; v++) mix.Add(Constant(1000, 10), 1f, SpeakerChannel.Games);

        Assert.Equal(8, SpeakerMix.MaxVoices);
        Assert.Equal(SpeakerMix.MaxVoices, mix.Voices);
        Assert.All(Filled(mix, 10), s => Assert.Equal(8000, s));
    }

    [Fact]
    public void TheMixHoldsMoreVoicesThanTheGateLetsThrough()
    {
        // A81: the mix is a second guard behind the gate's 6, never the first.
        Assert.True(SpeakerMix.MaxVoices > GamesSound.MaxVoices);
    }

    [Fact]
    public void AnEmptyMixFillsSilenceAndAnswersFalse()
    {
        var mix = new SpeakerMix();
        var buffer = Constant(1234, 441); // whatever the buffer held before

        Assert.False(mix.Fill(buffer));
        Assert.All(buffer, s => Assert.Equal(0, s));
        Assert.True(mix.Idle);
    }

    [Fact]
    public void AVoiceOfSilenceStillSounds()
    {
        var mix = new SpeakerMix();
        mix.Add(new short[10], 1f, SpeakerChannel.Games);

        Assert.True(mix.Fill(new short[4]));
        Assert.False(mix.Idle);
    }

    [Fact]
    public void NothingToPlayAddsNoVoice()
    {
        var mix = new SpeakerMix();
        mix.Add(null, 1f, SpeakerChannel.Games);
        mix.Add(Array.Empty<short>(), 1f, SpeakerChannel.Games);
        mix.Add(Constant(1000, 4), 0f, SpeakerChannel.Games);
        mix.Add(Constant(1000, 4), -0.5f, SpeakerChannel.Games);
        mix.Add(Constant(1000, 4), float.NaN, SpeakerChannel.Games);

        Assert.True(mix.Idle);
    }

    [Fact]
    public void AGainAboveOneIsPlayedAtOne()
    {
        var mix = new SpeakerMix();
        mix.Add(Constant(1000, 4), 4f, SpeakerChannel.Games);

        Assert.All(Filled(mix, 4), s => Assert.Equal(1000, s));
    }

    [Fact]
    public void AStopOnAnEmptyMixDoesNothing()
    {
        var mix = new SpeakerMix();
        mix.StopAll(SpeakerChannel.Games);
        mix.Add(Constant(1000, 300), 1f, SpeakerChannel.Games);

        Assert.All(Filled(mix, 300), s => Assert.Equal(1000, s)); // A88: a play after a stop is whole
    }

    [Fact]
    public void TheFadeIsFiveMilliseconds()
    {
        Assert.Equal((int)(GameSound.SampleRate * 0.005), SpeakerMix.FadeSamples);
    }

    // ---- Two channels (A101): a stop is one channel's; Idle, Voices and the cap count both --

    /// <summary>A long voice on the stopped channel and a short one on the other, then a stop: the first faded, the second whole.</summary>
    private static void AssertAStopFadesOnlyItsChannel(SpeakerChannel stopped, SpeakerChannel other)
    {
        var mix = new SpeakerMix();
        mix.Add(Constant(10_000, 5000), 1f, stopped);
        mix.Add(Constant(1000, 400), 1f, other);
        mix.StopAll(stopped);

        var buffer = Filled(mix, 500);

        for (var k = 0; k < SpeakerMix.FadeSamples; k++)
            Assert.InRange(buffer[k], 10_000 * (219 - k) / 220.0 + 999, 10_000 * (219 - k) / 220.0 + 1001);
        Assert.All(buffer[SpeakerMix.FadeSamples..400], s => Assert.Equal(1000, s)); // the other channel's to its last sample
        Assert.All(buffer[400..], s => Assert.Equal(0, s));
        Assert.True(mix.Idle);
    }

    [Fact]
    public void AStopOfGamesFadesItsVoiceAndLeavesAClickWhole() =>
        AssertAStopFadesOnlyItsChannel(SpeakerChannel.Games, SpeakerChannel.Clicks);

    [Fact]
    public void AStopOfClicksFadesItsVoiceAndLeavesAGamesSoundWhole() =>
        AssertAStopFadesOnlyItsChannel(SpeakerChannel.Clicks, SpeakerChannel.Games);

    [Fact]
    public void TwoStopsOfOneChannelAreOneAndTheOtherPlaysOnWhole()
    {
        var once = new SpeakerMix();
        var twice = new SpeakerMix();
        foreach (var mix in new[] { once, twice })
        {
            mix.Add(Constant(10_000, 1000), 1f, SpeakerChannel.Games);
            mix.Add(Constant(1000, 1000), 1f, SpeakerChannel.Clicks);
            mix.StopAll(SpeakerChannel.Games);
        }

        var a = Filled(once, 100).Concat(Filled(once, 200)).ToArray();
        var first = Filled(twice, 100);
        twice.StopAll(SpeakerChannel.Games); // the fade neither restarted nor lengthened
        var b = first.Concat(Filled(twice, 200)).ToArray();

        Assert.Equal(a, b);
        Assert.All(b[SpeakerMix.FadeSamples..], s => Assert.Equal(1000, s)); // the click whole
        Assert.Equal(1, twice.Voices);
    }

    [Fact]
    public void IdleCountsEitherChannelAndAStopOfTheOtherChangesNothing()
    {
        var mix = new SpeakerMix();
        mix.Add(Constant(1000, 300), 1f, SpeakerChannel.Clicks);
        Assert.False(mix.Idle);
        Assert.Equal(1, mix.Voices);
        mix.StopAll(SpeakerChannel.Games);
        Assert.All(Filled(mix, 300), s => Assert.Equal(1000, s));
        Assert.True(mix.Idle);

        mix.Add(Constant(2000, 300), 1f, SpeakerChannel.Games);
        Assert.False(mix.Idle);
        mix.StopAll(SpeakerChannel.Clicks);
        Assert.All(Filled(mix, 300), s => Assert.Equal(2000, s));
        Assert.True(mix.Idle);
    }

    [Fact]
    public void TheEightVoicesAreSharedByBothChannels()
    {
        var mix = new SpeakerMix();
        for (var v = 0; v < 5; v++) mix.Add(Constant(1000, 10), 1f, SpeakerChannel.Games);
        for (var v = 0; v < 3; v++) mix.Add(Constant(100, 10), 1f, SpeakerChannel.Clicks);
        mix.Add(Constant(7, 10), 1f, SpeakerChannel.Clicks); // a ninth, of either channel: dropped
        mix.Add(Constant(7, 10), 1f, SpeakerChannel.Games);

        Assert.Equal(SpeakerMix.MaxVoices, mix.Voices);
        Assert.All(Filled(mix, 10), s => Assert.Equal(5300, s));
        Assert.True(GamesSound.MaxVoices + 1 <= SpeakerMix.MaxVoices); // behind both gates: the games' 6 and one click (A99)
    }
}
