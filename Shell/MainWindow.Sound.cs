namespace PandoraOverlay;

/// <summary>
/// MainWindow, Sound part (A101): the app's one speaker and the gates that
/// play through it. The speaker (<see cref="WaveOutSpeaker"/>, in Sound/) is
/// made ONCE, in the constructor, and each gate is handed a view of its own
/// channel (<see cref="WaveOutSpeaker.Channel"/>): the games' gate
/// (<see cref="GamesSound"/>, made here and handed to the games host) the
/// Games channel, the click gate (<see cref="ClickSound"/>, G27) the Clicks
/// channel. One speaker, not one per gate: one stream to the one device, so
/// one open, one thread, one idle close, one failure line and one retry
/// clock, and a click while a turn plays rides the stream already open. Two
/// channels, so a game's stop never fades a click and a click never touches
/// a game's sound.
/// <para>
/// <b>The clicks</b> (A104). The click gate is made in the constructor,
/// before the games' gate (which hands it a game's clicks), over the Clicks
/// channel, the config (Settings' switch and step, the Games window's mute
/// and step, read at each click), the games host's one clock (the gap) and a
/// bank that reads each click's file at its first click. Then the click
/// layer is attached to this, the UI thread (<see cref="UiClicks.Attach"/>:
/// its class handlers and the input manager's bracket) and the gate becomes
/// the thread's sink. The sink is thread-static: MainWindow is the only
/// place that sets it, so a test, a dialog a test builds and the render
/// harness (which makes MainWindow without running its constructor) never
/// sound.
/// </para>
/// <para>
/// <b>Nothing at construction.</b> The speaker is lazy: making it opens no
/// device and starts no thread (the first sound does), and a gate made over a
/// view opens nothing either, nor does the click bank read a file, so a
/// MainWindow built and never shown plays nothing and holds nothing. (No test
/// makes MainWindow, so none has a speaker at all.)
/// </para>
/// <para>
/// <b>The exit.</b> <see cref="CloseSound"/> disposes the speaker, from
/// <c>Closed</c>, AFTER every gate: the games' gate is disposed first (its
/// stop queued, its view's Dispose doing nothing), the click layer's sink is
/// cleared (<see cref="DetachClicks"/>: no click reaches the speaker from
/// then on), then the speaker, whose Dispose joins its thread for at most
/// 200 ms and does not fade (A90): the order and the budget of the exit as
/// they were with the speaker the games'. A gate never disposes the speaker
/// (its view's Dispose does nothing), so one gate's end can't close the
/// stream the other plays on.
/// </para>
/// <para>
/// A new gate is one more field here, made over
/// <c>_speaker.Channel(...)</c> of its own channel, and disposed before
/// <see cref="CloseSound"/>'s last line. Nothing here names a request, the
/// cookie or a process, and nothing here declares a native call (a scan holds
/// this file with GamesHost/ and Sound/: A102).
/// </para>
/// Same class as MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MainWindow
{
    /// <summary>The app's one speaker (A101): made in the constructor, lazy, disposed by <see cref="CloseSound"/> after every gate.</summary>
    private readonly WaveOutSpeaker _speaker;

    /// <summary>The click gate (G27, A104): made in the constructor by <see cref="AttachClicks"/>, before the games' gate; the UI thread's click sink until <see cref="DetachClicks"/>.</summary>
    private readonly ClickSound _clicks;

    /// <summary>
    /// The games' gate (A79, A82) over the speaker's Games channel, the
    /// config (the Games window's mute and step), the games host's one
    /// clock and the click gate, to which it hands a heard game's clicks
    /// (A105; <see cref="_clicks"/> is made first, by <see cref="AttachClicks"/>):
    /// made in the constructor for the host, which owns and disposes it
    /// (its Dispose stops the Games channel and disposes the view, which does
    /// nothing; the click gate is not its to dispose). Opens nothing, starts
    /// nothing.
    /// </summary>
    private GamesSound MakeGamesSound(GameClock clock) =>
        new(_speaker.Channel(SpeakerChannel.Games), _config, clock, _clicks);

    /// <summary>
    /// The click gate over the speaker's Clicks channel, the config, the
    /// games host's clock and a lazy bank; then the click layer attached to
    /// the UI thread and the gate made its sink (A104). Opens nothing, reads
    /// nothing, starts nothing.
    /// </summary>
    private ClickSound AttachClicks(GameClock clock)
    {
        var clicks = new ClickSound(_speaker.Channel(SpeakerChannel.Clicks), _config, () => clock.Elapsed, new ClickBank());
        UiClicks.Attach();
        UiClicks.Sink = clicks;
        return clicks;
    }

    /// <summary>The exit (<c>Closed</c>, before <see cref="CloseSound"/>): this thread's clicks go nowhere from now on.</summary>
    private void DetachClicks()
    {
        if (ReferenceEquals(UiClicks.Sink, _clicks)) UiClicks.Sink = null;
    }

    /// <summary>
    /// The exit's last sound step (<c>Closed</c>, right after the games'
    /// gate's Dispose and the clicks' detach): the speaker disposed, so its
    /// thread leaves and its device closes, joined for at most 200 ms (A80,
    /// A90). Never throws (the speaker's own promise).
    /// </summary>
    private void CloseSound()
    {
        _speaker.Dispose();
    }
}
