namespace PandoraOverlay;

/// <summary>
/// The click gate (G27; A99, A103, A104): every click the player makes, in
/// the overlay's windows (through <see cref="UiClicks"/>'s class handler) and
/// in the Games window and its games (through the games' gate, which hands
/// a game's click on in the Games scope, A105), arrives here, and only what
/// passes every rule below reaches the speaker's Clicks channel. MainWindow
/// makes ONE (<c>MainWindow.Sound.cs</c>) and makes it the UI thread's
/// <see cref="UiClicks.Sink"/>.
/// <para>
/// <b>The rules, in order</b> (<c>PLAN-CLICKS.md</c> 1.1 and 2):
/// nothing while the player has turned clicks off (Settings › General ›
/// Sounds, <c>ClickSoundEnabled</c>: one switch for every click, the games'
/// too); in the <see cref="ClickScope.Games"/> scope nothing while the Games
/// window is muted (a player who mutes it expects silence in it), else the
/// gain of the window's own step (<c>GamesSoundVolume</c>), so a click keeps
/// its place under the games' cues at any step; in the
/// <see cref="ClickScope.Overlay"/> scope the gain of the overlay's own step
/// (<c>ClickSoundVolume</c>); the <see cref="ClickScope.Game"/> scope never
/// reaches the gate (the app's handler leaves a game's controls to the
/// Engine, A105) and is refused if it does; nothing when the last click
/// started less than <see cref="MinGap"/> ago, whatever its cue or its
/// scope; then the cue's samples from the bank (none: nothing), and the
/// speaker. A dropped click is dropped, never queued, and does not count as
/// started. The config is read at each click: Settings writes the two keys
/// and names no <c>SettingsChange</c>, and a change is heard at the next
/// click.
/// </para>
/// <para>
/// <b>At most one at a time.</b> No click is longer than <see cref="MinGap"/>
/// (R8, held by <c>ClickBankTests</c>), and none starts within it of the
/// last, so two clicks never sound together and the speaker's mix holds one
/// click voice at most beside the games' (A101).
/// </para>
/// <para>
/// Made with no IO, no device and no thread (the bank reads a file at the
/// first click that needs it, the speaker opens the device at its first
/// sound), on the UI thread, where every call comes. It starts no timer: its
/// clock is the host's, read at each click. It logs nothing per click: a
/// click is no fact worth a line (constraint 4 keeps the log to facts); the
/// one line it can cause is the bank's, a file that can't be read. It never
/// throws: the config's reads, the clock, the bank and the speaker don't.
/// </para>
/// </summary>
internal sealed class ClickSound : IClickSink
{
    /// <summary>
    /// No click starts within this of the last click's start (A99): 80 ms.
    /// A mouse's bounce (two clicks tens of ms apart) is one sound, a machine
    /// gun at most 12 a second, and a deliberate double click (100 to 200 ms
    /// apart) still two. No click is longer than this, so at most one sounds
    /// at any moment.
    /// </summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(80);

    private readonly ISpeaker _speaker;
    private readonly OverlayConfig _config;
    private readonly Func<TimeSpan> _elapsed;
    private readonly ClickBank _bank;

    /// <summary>When the last click that sounded started, on <see cref="_elapsed"/>; null before the first.</summary>
    private TimeSpan? _lastStart;

    /// <param name="speaker">The speaker's Clicks channel (<c>WaveOutSpeaker.Channel(SpeakerChannel.Clicks)</c>), or a fake: played through, never disposed here (the speaker is MainWindow's).</param>
    /// <param name="config">Where the switch and the two steps live, read at each click.</param>
    /// <param name="elapsed">The host's monotonic clock (the games' <c>GameClock</c>'s Elapsed), for the gap.</param>
    /// <param name="bank">The clicks' samples, read at the first click that needs each.</param>
    public ClickSound(ISpeaker speaker, OverlayConfig config, Func<TimeSpan> elapsed, ClickBank bank)
    {
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _elapsed = elapsed ?? throw new ArgumentNullException(nameof(elapsed));
        _bank = bank ?? throw new ArgumentNullException(nameof(bank));
    }

    /// <summary>One click, in its scope: played on the Clicks channel at its scope's gain, or dropped by the rules above. On the UI thread; never throws.</summary>
    public void Play(ClickCue cue, ClickScope scope)
    {
        if (GainOf(scope, _config) is not { } gain) return;
        var now = _elapsed();
        if (!Spaced(_lastStart, now)) return;
        if (_bank.Samples(cue) is not { } samples) return;
        _lastStart = now;
        _speaker.Play(samples, gain);
    }

    /// <summary>The layer's sink is the gate (A104).</summary>
    void IClickSink.Click(ClickCue cue, ClickScope scope) => Play(cue, scope);

    /// <summary>
    /// The gain a click plays at in its scope, or null for silence (A103):
    /// clicks off, nothing; the Games scope muted, nothing, else the Games
    /// window's step; the Overlay scope the overlay's step; the Game scope
    /// nothing (the Engine's, A105). A step out of range is read as the
    /// nearest (<see cref="SoundSteps.StepOf"/>) and never written back.
    /// </summary>
    internal static float? GainOf(ClickScope scope, OverlayConfig config)
    {
        if (!config.ClickSoundEnabled) return null;
        return scope switch
        {
            ClickScope.Overlay => SoundSteps.GainOf(config.ClickSoundVolume),
            ClickScope.Games => config.GamesSoundMuted ? null : SoundSteps.GainOf(config.GamesSoundVolume),
            _ => null,
        };
    }

    /// <summary>A click starting now is clear of the last one's start by <see cref="MinGap"/> or more, or is the first (A99).</summary>
    internal static bool Spaced(TimeSpan? lastStart, TimeSpan now) => lastStart is not { } last || now - last >= MinGap;
}
