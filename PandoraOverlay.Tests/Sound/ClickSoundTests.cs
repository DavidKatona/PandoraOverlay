using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The click gate (<see cref="ClickSound"/>; A99, A103, A104) over a speaker
/// that records (<see cref="FakeSpeaker"/>), a clock moved by hand and a bank
/// of the tests' own bytes: clicks off, nothing; on, at each step's gain;
/// the Games scope muted, nothing, else at the Games window's step; the Game
/// scope refused; the 80 ms gap, across cues and scopes; a missing cue's
/// file; nothing at construction.
/// </summary>
public sealed class ClickSoundTests
{
    /// <summary>A gate, its speaker, its clock and its bank's bench, each cue a file of its own few samples.</summary>
    private sealed class Gate
    {
        public Gate(OverlayConfig? config = null, Func<string, byte[]?>? files = null)
        {
            Config = config ?? new OverlayConfig();
            Bench = new ClickBankTests.Bench(files ?? (resource => ClickBankTests.Wav(SamplesOf(resource))));
            Sound = new ClickSound(Speaker, Config, () => Now, Bench.Bank);
        }

        public OverlayConfig Config { get; }

        public FakeSpeaker Speaker { get; } = new();

        public ClickBankTests.Bench Bench { get; }

        public ClickSound Sound { get; }

        public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Moves the clock on and plays.</summary>
        public void PlayAfter(double milliseconds, ClickCue cue, ClickScope scope = ClickScope.Overlay)
        {
            Now += TimeSpan.FromMilliseconds(milliseconds);
            Sound.Play(cue, scope);
        }
    }

    /// <summary>Each cue's stand-in file: its name's length in samples, so a test can tell which file was played.</summary>
    private static short[] SamplesOf(string resource) => Enumerable.Repeat((short)100, resource.Length).ToArray();

    private static int LengthOf(ClickCue cue) => ClickBank.ResourceOf(cue).Length;

    [Fact]
    public void NothingAtConstruction()
    {
        var gate = new Gate();

        Assert.Empty(gate.Bench.Opened); // no file read
        Assert.Empty(gate.Speaker.Calls); // nothing played, stopped or disposed
    }

    [Fact]
    public void AClickPlaysItsCuesSamplesOnTheSpeaker()
    {
        var gate = new Gate();

        gate.PlayAfter(0, ClickCue.ToggleOn);

        var (samples, gain) = Assert.Single(gate.Speaker.Played);
        Assert.Equal(LengthOf(ClickCue.ToggleOn), samples.Length);
        Assert.Equal(SoundSteps.GainOf(SoundSteps.Default), gain);
        Assert.Equal(new[] { "play" }, gate.Speaker.Calls); // never a stop, never a dispose
    }

    [Fact]
    public void ClicksOffPlayNothingInAnyScope()
    {
        var gate = new Gate(new OverlayConfig { ClickSoundEnabled = false });

        gate.PlayAfter(0, ClickCue.Press);
        gate.PlayAfter(200, ClickCue.Select, ClickScope.Games);
        gate.PlayAfter(200, ClickCue.Back, ClickScope.Game);

        Assert.Empty(gate.Speaker.Played);
        Assert.Empty(gate.Bench.Opened); // not even read
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheOverlayScopePlaysAtTheOverlaysStep(int step)
    {
        var gate = new Gate(new OverlayConfig { ClickSoundVolume = step, GamesSoundVolume = 6 - step });

        gate.PlayAfter(0, ClickCue.Press);

        Assert.Equal(SoundSteps.GainOf(step), Assert.Single(gate.Speaker.Played).Gain);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void TheGamesScopePlaysAtTheGamesWindowsStepAndNotWhenMuted(int step)
    {
        var gate = new Gate(new OverlayConfig { GamesSoundVolume = step, ClickSoundVolume = 6 - step });

        gate.PlayAfter(0, ClickCue.Select, ClickScope.Games);
        gate.Config.GamesSoundMuted = true;
        gate.PlayAfter(200, ClickCue.Select, ClickScope.Games);
        gate.PlayAfter(200, ClickCue.Press); // the overlay's clicks: not the window's mute

        Assert.Equal(new[] { SoundSteps.GainOf(step), SoundSteps.GainOf(6 - step) }, gate.Speaker.Played.Select(p => p.Gain));
    }

    [Fact]
    public void TheGameScopeIsRefused()
    {
        var gate = new Gate();

        gate.PlayAfter(0, ClickCue.Press, ClickScope.Game);
        gate.PlayAfter(0, ClickCue.Press); // and it started nothing: no gap to wait out

        Assert.Single(gate.Speaker.Played);
    }

    /// <summary>The gap (A99): 79 ms after a start is dropped, 80 played, whatever the cue or the scope.</summary>
    [Fact]
    public void AClickWithinEightyMillisecondsOfTheLastStartIsDroppedAcrossCuesAndScopes()
    {
        var gate = new Gate();

        gate.PlayAfter(0, ClickCue.Press);                       // 0: played
        gate.PlayAfter(79, ClickCue.Select, ClickScope.Games);   // 79: dropped
        gate.PlayAfter(1, ClickCue.Back);                        // 80: played
        gate.PlayAfter(40, ClickCue.Back);                       // 120: dropped (the same cue too)
        gate.PlayAfter(40, ClickCue.Arm, ClickScope.Games);      // 160: played

        Assert.Equal(new[] { LengthOf(ClickCue.Press), LengthOf(ClickCue.Back), LengthOf(ClickCue.Arm) }, gate.Speaker.Played.Select(p => p.Samples.Length));
    }

    /// <summary>Dropped, never queued, and a dropped click does not move the gap: twenty a second sound ten a second.</summary>
    [Fact]
    public void ADroppedClickIsNeverQueuedAndStartsNothing()
    {
        var gate = new Gate();

        for (var i = 0; i < 12; i++) gate.PlayAfter(i == 0 ? 0 : 50, ClickCue.Press);
        gate.Now += TimeSpan.FromSeconds(5); // nothing kept for later

        Assert.Equal(6, gate.Speaker.Played.Count); // 0, 100, 200 ... 500 ms
    }

    [Fact]
    public void AClickMutedOrOffStartsNoGap()
    {
        var gate = new Gate();

        gate.Config.GamesSoundMuted = true;
        gate.PlayAfter(0, ClickCue.Select, ClickScope.Games); // muted: not started
        gate.PlayAfter(10, ClickCue.Press);                   // so this plays

        Assert.Single(gate.Speaker.Played);
    }

    /// <summary>A cue whose file is missing: silence for it, one Warning, and no gap taken; the others play.</summary>
    [Fact]
    public void AMissingCuesFileIsSilenceAndTheOthersPlay()
    {
        var gate = new Gate(files: resource => resource == ClickBank.ResourceOf(ClickCue.Back) ? null : ClickBankTests.Wav(SamplesOf(resource)));

        gate.PlayAfter(0, ClickCue.Back);
        gate.PlayAfter(10, ClickCue.Press); // the silent Back started nothing
        gate.PlayAfter(200, ClickCue.Back);

        Assert.Equal(new[] { LengthOf(ClickCue.Press) }, gate.Speaker.Played.Select(p => p.Samples.Length));
        Assert.Single(gate.Bench.Lines);
        Assert.Equal(LogVerbosity.Warning, gate.Bench.Lines[0].Level);
    }

    /// <summary>Settings writes the config and names no SettingsChange: the gate reads it at each click.</summary>
    [Fact]
    public void TheConfigIsReadAtEachClick()
    {
        var gate = new Gate();

        gate.PlayAfter(0, ClickCue.Press);
        gate.Config.ClickSoundVolume = 5;
        gate.PlayAfter(100, ClickCue.Press);
        gate.Config.ClickSoundEnabled = false;
        gate.PlayAfter(100, ClickCue.Press);
        gate.Config.ClickSoundEnabled = true;
        gate.PlayAfter(100, ClickCue.Press);

        Assert.Equal(new[] { SoundSteps.GainOf(3), SoundSteps.GainOf(5), SoundSteps.GainOf(5) }, gate.Speaker.Played.Select(p => p.Gain));
    }

    /// <summary>The gate is the layer's sink (A104): a click handed to it as a sink is a Play.</summary>
    [Fact]
    public void AsTheLayersSinkItPlays()
    {
        var gate = new Gate();

        ((IClickSink)gate.Sound).Click(ClickCue.Select, ClickScope.Overlay);

        Assert.Single(gate.Speaker.Played);
    }

    [Fact]
    public void TheRealFilesPlayFromTheRealBank()
    {
        var speaker = new FakeSpeaker();
        var now = TimeSpan.Zero;
        var sound = new ClickSound(speaker, new OverlayConfig(), () => now, new ClickBank(null, (_, line) => throw new Xunit.Sdk.XunitException(line)));

        foreach (var cue in Enum.GetValues<ClickCue>())
        {
            sound.Play(cue, ClickScope.Overlay);
            now += ClickSound.MinGap;
        }

        Assert.Equal(Enum.GetValues<ClickCue>().Length, speaker.Played.Count);
    }
}
