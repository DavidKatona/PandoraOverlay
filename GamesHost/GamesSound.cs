using System.Runtime.CompilerServices;
using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The games' sound gate (G26; A79, A81, A82): one per <see cref="GamesHost"/>,
/// the app's, on the UI thread. Every sound a game hands its host
/// (<see cref="IGameHost.Sound"/>, through its own <see cref="GameHost"/>)
/// arrives here, and only what passes every rule below reaches the speaker
/// (<see cref="ISpeaker"/>, which this owns and disposes). The window's mute and
/// volume (its header's sound group) are this class's other half.
/// <para>
/// <b>Never while closed, paused or muted (A79).</b> The window tells the gate
/// what it shows at every change (<see cref="Stage"/>: a game shown, the grid,
/// the health warning up, "Keep playing", the window closed). A sound plays
/// only when ALL hold: the instance that asks is the CURRENT instance of the
/// game on screen (a detached one, after "Start it again", reaches nothing,
/// as its storage doesn't), the warning is not up, the player has not muted,
/// and the sound is valid. Everything else is dropped, silently: the game
/// never learns which (the Contract's <see cref="IGameSound"/>). So a game's
/// bug that plays from a reading with the window closed, or from a page not on
/// screen, is silence, whatever the game does. This is the second of A79's
/// three layers: the engine's paused view plays nothing (the first), and every
/// change that leaves the game unheard stops what plays (the third, below).
/// </para>
/// <para>
/// <b>A stop at every change.</b> A <see cref="Stage"/> at which nobody is
/// heard (the grid, the warning up, the window closed), or at which another
/// instance is heard, calls the speaker's <c>StopAll</c> (faded out in 5 ms,
/// never cut with a click); a mute does too. Only a stage that keeps the same
/// instance heard leaves its sounds playing. A stop with nothing playing costs
/// nothing (the speaker has nothing to fade), and never fades a later sound: a
/// <c>Play</c> after it is heard whole (A88, written on <c>ISpeaker.StopAll</c>).
/// </para>
/// <para>
/// <b>Valid (A78).</b> 1 to <see cref="GameSound.MaxSamples"/> samples, checked
/// the first time an object would play, and copied then, ONCE, into an array
/// of the gate's own (kept per object, <see cref="ConditionalWeakTable{TKey, TValue}"/>,
/// so it goes with the object), so what the speaker reads on its own thread is
/// never memory a game still holds. A refused sound is logged once, a Warning
/// in the games' log naming it (cut to <see cref="MaxLoggedName"/> characters),
/// and never again for that object.
/// </para>
/// <para>
/// <b>The limits (A81).</b> At most <see cref="MaxVoices"/> sounding at once,
/// each counted from its play to its play plus its length on the host's clock;
/// a play over the count is dropped (none is cut short). The same object is
/// not started again within <see cref="SameSoundGap"/>: the engine's loop
/// makes up to five ticks after a stall (A55), so two steps' cues can land in
/// one look. A game that plays every tick by mistake costs a little CPU, never
/// the overlay (the speaker's mix holds its own cap behind this one).
/// </para>
/// <para>
/// <b>The level (A82).</b> Five steps 6 dB apart, 1 = −24 dB up to 5 = 0 dB, the
/// third by default, sound on; the step and the mute live in the config
/// (<c>GamesSoundVolume</c>, <c>GamesSoundMuted</c>). A value out of range is
/// READ as the nearest step and never written back by reading: only a click
/// writes. A sound is passed at its step's gain at its play; what is playing
/// finishes at the level it started with (under a second). The steps' one home
/// is <see cref="SoundSteps"/> since A103 (the overlay's clicks share them):
/// <see cref="GainOf"/>, <see cref="Steps"/>, <see cref="DefaultStep"/> and
/// the rest here forward to it.
/// </para>
/// <para>
/// <b>A game's clicks (G27, A105).</b> A game's own controls click by the
/// Engine's rule, which hands each click its host (<see cref="IGameSound.Click"/>,
/// through its <see cref="GameHost"/>'s door) and arrives at <see cref="Click"/>:
/// passed on to the click gate (<see cref="ClickSound"/>, handed in by
/// MainWindow) in the <see cref="ClickScope.Games"/> scope only for the
/// instance heard, by the same <see cref="Heard"/> as a sound (A79's second
/// layer: the current instance of the game on screen, the window open, the
/// warning not up), so a detached instance's click, another game's, one from
/// the grid or under the warning is silence. The rest is the click gate's and
/// is not repeated here (A99, A103): the Settings switch, the window's mute
/// and step, the 80 ms gap; and a click plays on the speaker's Clicks
/// channel, so it is never one of this gate's <see cref="MaxVoices"/> and a
/// stage's stop never fades it. With no click gate (the tests, the render
/// harness: the parameter left out) no click goes anywhere.
/// </para>
/// <para>
/// <b>The preview (A82).</b> The gate's own sound, made in code once: a 0.08 s
/// triangle blip at 660 Hz, so a player who changes the level hears it, on
/// the grid too, instead of setting it blind until the next hunter moves.
/// Played at the new step after every change but a mute, whatever the stage
/// (the click is on the open window), never while muted, under the same
/// limits.
/// </para>
/// <para>
/// Made with no IO, no device and no thread (MainWindow builds it in its
/// constructor, and the tests and the render harness build MainWindow
/// unshown). It names no request, no cookie and no process, and starts no
/// timer (A86): the clock it counts with is the host's, read at each play.
/// The overlay's own chimes (General → Sounds) are not this class's and are
/// untouched by its mute (A85).
/// </para>
/// </summary>
internal sealed class GamesSound : IDisposable
{
    /// <summary>The most sounds sounding at once (A81); a seventh is dropped, none is cut.</summary>
    public const int MaxVoices = 6;

    /// <summary>The same sound object is not started again within this (A81).</summary>
    public static readonly TimeSpan SameSoundGap = TimeSpan.FromMilliseconds(40);

    /// <summary>The volume's steps (A82): <see cref="SoundSteps.Count"/>, the steps' one home since A103.</summary>
    public const int Steps = SoundSteps.Count;

    /// <summary>The volume a fresh config holds (A82): <see cref="SoundSteps.Default"/>, the steps' one home since A103.</summary>
    public const int DefaultStep = SoundSteps.Default;

    /// <summary>Between two steps (A82): <see cref="SoundSteps.DecibelsPerStep"/>, the steps' one home since A103.</summary>
    public const double DecibelsPerStep = SoundSteps.DecibelsPerStep;

    /// <summary>A refused sound's name in its log line is cut to this many characters.</summary>
    internal const int MaxLoggedName = 32;

    /// <summary>The preview's pitch: 660 Hz, an E5, in the first board's palette of C, E and G (A82).</summary>
    internal const double PreviewHz = 660;

    /// <summary>The preview's length: 0.08 s.</summary>
    internal const double PreviewSeconds = 0.08;

    /// <summary>The preview's linear attack, from silence: 2 ms, so it starts with no click.</summary>
    internal const double PreviewAttackSeconds = 0.002;

    /// <summary>The level the preview decays to, exponentially, at its end: the board's envelopes' own floor.</summary>
    internal const double PreviewFloor = 0.0001;

    /// <summary>The preview's peak before the step's gain: −14 dBFS, the middle of the games' cues (−24 to −5).</summary>
    internal const double PreviewPeakDbfs = -14;

    /// <summary>16-bit full scale, the reference of a level in dBFS.</summary>
    internal const double FullScale = short.MaxValue;

    /// <summary>The preview, the gate's own sound, made once (no file).</summary>
    internal static readonly GameSound Preview = new("preview", MakePreview());

    private readonly ISpeaker _speaker;
    private readonly OverlayConfig _config;
    private readonly IGameClock _clock;

    /// <summary>Where a heard instance's clicks go (A105): MainWindow's click gate, or none (silence).</summary>
    private readonly IClickSink? _clicks;

    /// <summary>Per sound object: the gate's own copy of its samples (null when refused) and when it last started.</summary>
    private readonly ConditionalWeakTable<GameSound, Kept> _kept = new();

    /// <summary>When each sound started and not yet stopped ends, on the host's clock.</summary>
    private readonly List<TimeSpan> _sounding = new();

    private HostedGame? _onScreen;
    private bool _paused;

    /// <summary>The instance heard at the last stage: the game on screen, playing, not paused. Null when nobody is.</summary>
    private GameHost? _heard;

    private bool _disposed;

    /// <param name="speaker">What plays (the real speaker, or <see cref="NullSpeaker"/>); owned and disposed by the gate.</param>
    /// <param name="config">Where the step and the mute live (A82): read at every play, written by the controls.</param>
    /// <param name="clock">The host's clock, for the limits' counts.</param>
    /// <param name="clicks">The click gate a heard instance's clicks are handed to (MainWindow's <see cref="ClickSound"/>, A105), or a test's fake sink; left out, no game's click goes anywhere. Never disposed here: it is MainWindow's, and owns nothing to dispose.</param>
    public GamesSound(ISpeaker speaker, OverlayConfig config, IGameClock clock, IClickSink? clicks = null)
    {
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _clicks = clicks;
        _kept.Add(Preview, new Kept(Preview.Samples.ToArray()));
    }

    /// <summary>The step the player chose (1 to <see cref="Steps"/>): the config's value, read as the nearest step when it is out of range (and never written back).</summary>
    public int Step => StepOf(_config.GamesSoundVolume);

    /// <summary>The player muted the games' sounds (A82).</summary>
    public bool Muted => _config.GamesSoundMuted;

    /// <summary>A value of the config as a step: <see cref="SoundSteps.StepOf"/> (A103), held to 1..<see cref="Steps"/>.</summary>
    public static int StepOf(int value) => SoundSteps.StepOf(value);

    /// <summary>
    /// The gain a step plays at: <see cref="SoundSteps.GainOf"/>, the steps' one
    /// home since A103 (the Games window and the overlay's clicks share it),
    /// <c>10^(dB/20)</c> with 1 = −24 dB, 2 = −18, 3 = −12, 4 = −6, 5 = 0. A step
    /// out of range is read as the nearest.
    /// </summary>
    public static float GainOf(int step) => SoundSteps.GainOf(step);

    /// <summary>A step's level in dB: <see cref="SoundSteps.DecibelsOf"/> (A103), 0 at the top step, <see cref="DecibelsPerStep"/> less for each below.</summary>
    public static double DecibelsOf(int step) => SoundSteps.DecibelsOf(step);

    // ---- The stage (A79) ----------------------------------------------------------------

    /// <summary>
    /// What the window shows now, told at every change: the game on screen
    /// (null on the grid and once closed) and whether the health warning covers
    /// it. A stage at which nobody is heard, or another instance is, stops
    /// every sound; only the same instance heard again leaves its sounds playing.
    /// </summary>
    public void Stage(HostedGame? onScreen, bool paused)
    {
        if (_disposed) return;
        _onScreen = onScreen;
        _paused = paused;
        var heard = !paused && onScreen is { Plays: true } shown ? shown.Host : null;
        if (heard is null || !ReferenceEquals(heard, _heard)) StopAll();
        _heard = heard;
    }

    // ---- A game's sound (A79, A81) --------------------------------------------------------

    /// <summary>
    /// A sound a game's instance handed its host (its <see cref="GameHost"/>'s
    /// door): played when that instance is heard and the sound is valid, under
    /// the limits; else dropped, silently. Never throws.
    /// </summary>
    internal void Play(GameHost asking, GameSound? sound)
    {
        if (_disposed || sound is null || Muted || !Heard(asking)) return;
        var kept = KeptOf(sound, asking.Log);
        if (kept.Samples is null) return;
        Start(kept);
    }

    // ---- A game's click (A105) ---------------------------------------------------------------

    /// <summary>
    /// A click a game's instance handed its host (its <see cref="GameHost"/>'s
    /// door, from the Engine's rule): passed to the click gate as the
    /// <see cref="ClickCue"/> of the same name, in the
    /// <see cref="ClickScope.Games"/> scope, when that instance is heard (the
    /// same <see cref="Heard"/> as <see cref="Play"/>); else dropped, silently.
    /// Nothing once disposed or with no click gate, and nothing for a value
    /// outside <see cref="GameClick"/>'s six. The mute is NOT read here: the
    /// click gate reads it for the Games scope, with the Settings switch, the
    /// window's step and the gap (A103, A99), so no rule is written twice.
    /// Never throws (the click gate's own promise).
    /// </summary>
    internal void Click(GameHost asking, GameClick click)
    {
        if (_disposed || _clicks is null || !Heard(asking) || CueOf(click) is not { } cue) return;
        _clicks.Click(cue, ClickScope.Games);
    }

    /// <summary>
    /// A game's click as the overlay's cue (A100, A105): the Contract's
    /// <see cref="GameClick"/> and the app's <see cref="ClickCue"/> are the
    /// same six by name (the Contract can't name the app's enum, A2); null for
    /// a value that is none of them (a game's cast), which plays nothing.
    /// </summary>
    internal static ClickCue? CueOf(GameClick click) => click switch
    {
        GameClick.Press => ClickCue.Press,
        GameClick.Select => ClickCue.Select,
        GameClick.ToggleOn => ClickCue.ToggleOn,
        GameClick.ToggleOff => ClickCue.ToggleOff,
        GameClick.Arm => ClickCue.Arm,
        GameClick.Back => ClickCue.Back,
        _ => null,
    };

    /// <summary>The asking instance is the current instance of the game on screen, which plays, and the warning is not up.</summary>
    private bool Heard(GameHost asking) =>
        !_paused && asking.Current && _onScreen is { Plays: true } shown && ReferenceEquals(shown.Host, asking);

    /// <summary>The gate's own record of this object: made the first time it would play, its samples copied once, or refused once with one Warning.</summary>
    private Kept KeptOf(GameSound sound, IGameLog log)
    {
        if (_kept.TryGetValue(sound, out var kept)) return kept;
        var samples = sound.Samples;
        if (samples.Length is >= 1 and <= GameSound.MaxSamples)
        {
            kept = new Kept(samples.ToArray());
        }
        else
        {
            kept = new Kept(null);
            log.Warning($"sound refused: '{Cut(sound.Name)}' is empty or longer than {GameSound.MaxSamples / GameSound.SampleRate} s");
        }
        _kept.Add(sound, kept);
        return kept;
    }

    /// <summary>A name for the log: its first line, at most <see cref="MaxLoggedName"/> characters, never half a surrogate pair.</summary>
    internal static string Cut(string? name)
    {
        var text = name ?? "";
        var end = text.IndexOfAny(new[] { '\r', '\n' });
        if (end >= 0) text = text[..end];
        if (text.Length <= MaxLoggedName) return text;
        return text[..(char.IsHighSurrogate(text[MaxLoggedName - 1]) ? MaxLoggedName - 1 : MaxLoggedName)];
    }

    /// <summary>The limits (A81), then the speaker at the step's gain: the same object not again within the gap, at most the voices.</summary>
    private void Start(Kept kept)
    {
        var now = _clock.Elapsed;
        if (kept.LastStart is { } last && now - last < SameSoundGap) return;
        _sounding.RemoveAll(end => end <= now);
        if (_sounding.Count >= MaxVoices) return;
        kept.LastStart = now;
        _sounding.Add(now + LengthOf(kept.Samples!.Length));
        _speaker.Play(kept.Samples, GainOf(Step));
    }

    /// <summary>A sound's length on the clock, to the tick.</summary>
    internal static TimeSpan LengthOf(int samples) => TimeSpan.FromTicks(samples * TimeSpan.TicksPerSecond / GameSound.SampleRate);

    private void StopAll()
    {
        _sounding.Clear();
        _speaker.StopAll();
    }

    // ---- The window's controls (A82) ---------------------------------------------------------

    /// <summary>The speaker button: muting stops every sound at once; unmuting plays the preview at the step.</summary>
    public void ToggleMute()
    {
        if (_disposed) return;
        if (Muted)
        {
            _config.GamesSoundMuted = false;
            LogLevel();
            PlayPreview();
            return;
        }
        _config.GamesSoundMuted = true;
        StopAll();
        AppLog.Games.Log("Games sound: muted");
    }

    /// <summary>`−`: one step down and sound on, then the preview. At step 1 with sound on, nothing changes and nothing is written.</summary>
    public void StepDown() => StepTo(Step - 1);

    /// <summary>`+`: one step up and sound on, then the preview. At the top step with sound on, nothing changes and nothing is written.</summary>
    public void StepUp() => StepTo(Step + 1);

    private void StepTo(int wanted)
    {
        if (_disposed) return;
        var step = StepOf(wanted);
        if (step == Step && !Muted) return; // at an end, nothing to change (a value out of range stays as it is)
        _config.GamesSoundVolume = step;
        _config.GamesSoundMuted = false;
        LogLevel();
        PlayPreview();
    }

    private void LogLevel() => AppLog.Games.Log($"Games sound: on, step {Step} of {Steps}");

    /// <summary>The preview at the step, whatever the stage, never while muted, under the limits.</summary>
    private void PlayPreview()
    {
        if (Muted || !_kept.TryGetValue(Preview, out var kept)) return;
        Start(kept);
    }

    /// <summary>
    /// The preview's samples: a triangle at <see cref="PreviewHz"/> from phase
    /// 0, a linear attack over <see cref="PreviewAttackSeconds"/> from silence,
    /// then an exponential decay to <see cref="PreviewFloor"/> at its end, as
    /// the first board's envelopes; peak-normalised to
    /// <see cref="PreviewPeakDbfs"/> and rounded to 16 bits. Its first sample is
    /// silence (the attack starts at 0), and so is its last, as the board's
    /// envelope is 0 from its end: at the floor a sample would still round to
    /// a step of one.
    /// </summary>
    internal static short[] MakePreview()
    {
        var count = (int)Math.Round(PreviewSeconds * GameSound.SampleRate);
        var end = (count - 1.0) / GameSound.SampleRate;
        var wave = new double[count];
        var peak = 0.0;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / GameSound.SampleRate;
            var envelope = t < PreviewAttackSeconds
                ? t / PreviewAttackSeconds
                : Math.Pow(PreviewFloor, (t - PreviewAttackSeconds) / (end - PreviewAttackSeconds));
            wave[i] = Triangle(PreviewHz * t) * envelope;
            peak = Math.Max(peak, Math.Abs(wave[i]));
        }
        var scale = Math.Pow(10, PreviewPeakDbfs / 20) * FullScale / peak;
        var samples = new short[count];
        for (var i = 1; i < count - 1; i++) samples[i] = (short)Math.Round(wave[i] * scale);
        return samples;
    }

    /// <summary>A triangle of peak 1 at this many cycles, rising from 0 as a sine does.</summary>
    private static double Triangle(double cycles)
    {
        var phase = cycles - Math.Floor(cycles);
        return phase < 0.25 ? 4 * phase
             : phase < 0.75 ? 2 - 4 * phase
             : 4 * phase - 4;
    }

    // ---- The end --------------------------------------------------------------------------

    /// <summary>The overlay is closing: every sound stopped, then the speaker disposed. Idempotent, and it never throws.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The speaker's own promise is never to throw; on the way out the
        // overlay keeps its word even if one breaks it, and says so.
        try
        {
            StopAll();
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"the games' sound could not be stopped: {AppLog.Short(ex)}");
        }
        try
        {
            _speaker.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"the games' speaker could not be closed: {AppLog.Short(ex)}");
        }
    }

    /// <summary>The gate's own copy of a sound's samples (null: refused, and logged once), and when it last started.</summary>
    private sealed class Kept
    {
        public Kept(short[]? samples) => Samples = samples;

        public short[]? Samples { get; }

        public TimeSpan? LastStart { get; set; }
    }
}
