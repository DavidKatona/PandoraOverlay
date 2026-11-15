using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// What one instance of a game is given at <see cref="IGame.Attach"/> (the
/// Contract's <see cref="IGameHost"/>): its whole world. The host's clock, its
/// own storage over the store, its own log lines, its own door to the window's
/// speaker (the host's gate, <see cref="GamesSound"/>, A77), and two ways back to
/// <see cref="GamesHost"/>: its tile changed, or it caught an exception of its
/// own (the second layer of containment, A10: a view's handler, the engine's
/// loop).
/// <para>
/// One per instance. When <see cref="GamesHost.StartAgain"/> replaces a
/// stopped instance, this one is detached: whatever the old instance still
/// does with it (a write, a sound, a fault, a tile change) reaches nothing, so it can
/// never touch the instance that replaced it.
/// </para>
/// <para>
/// It names no request, no cookie and no process (A02's scan holds every file
/// here to that): the games are fed what the overlay already reads, and
/// nothing a game is given can ask the website for anything.
/// </para>
/// </summary>
internal sealed class GameHost : IGameHost
{
    private readonly GamesHost _owner;
    private readonly GameStorage _storage;

    private readonly SoundDoor _sound;

    public GameHost(GamesHost owner, HostedGame game, IGameClock clock, GameStorage storage, IGameLog log)
    {
        _owner = owner;
        Game = game;
        Clock = clock;
        _storage = storage;
        Log = log;
        _sound = new SoundDoor(this);
    }

    public IGameClock Clock { get; }

    public IGameStorage Storage => _storage;

    public IGameLog Log { get; }

    /// <summary>This instance's door to the window's speaker (A77): its Play asks the host's gate (<see cref="GamesSound"/>) with this instance, and reaches nothing once the instance is detached.</summary>
    public IGameSound Sound => _sound;

    /// <summary>The game this instance plays.</summary>
    internal HostedGame Game { get; }

    /// <summary>The storage, as the host flushes, clears and stops it.</summary>
    internal GameStorage OwnStorage => _storage;

    /// <summary>False once a new instance replaced this one.</summary>
    internal bool Current { get; private set; } = true;

    public void TileChanged() => _owner.OnTileChanged(this);

    public void Fault(Exception error) => _owner.OnFault(this, error);

    /// <summary>A new instance takes over: nothing this one does reaches the host or the disk any more.</summary>
    internal void Detach()
    {
        Current = false;
        _storage.Stop();
        _sound.Close();
    }

    /// <summary>
    /// One instance's <see cref="IGameSound"/> (A77, A79): what it is handed goes
    /// to the gate with the instance that handed it, so the gate can tell the
    /// instance on screen from any other. Closed by <see cref="Detach"/>, as the
    /// storage is stopped: a replaced instance's sound reaches nothing (the gate
    /// would drop it too, by <see cref="Current"/>; two locks on one door).
    /// Never throws, as the Contract promises: the gate drops what it doesn't
    /// play.
    /// </summary>
    private sealed class SoundDoor : IGameSound
    {
        private readonly GameHost _host;
        private bool _closed;

        public SoundDoor(GameHost host) => _host = host;

        public void Play(GameSound sound)
        {
            if (_closed) return;
            _host._owner.Sound.Play(_host, sound);
        }

        /// <summary>
        /// A game's click (G27, A105): dropped for now. C06 hands it to the games'
        /// gate, <c>GamesSound.Click(_host, click)</c>, which plays it only for the
        /// instance heard (A79's second layer) through the click gate in the Games
        /// scope (the Settings switch, the window's mute and step, the gap); a closed
        /// door will reach nothing, as its <see cref="Play"/> does. Until then every
        /// game's click is silence, which the Contract allows: the host decides, the
        /// game never learns which, and nothing here throws.
        /// </summary>
        public void Click(GameClick click)
        {
        }

        public void Close() => _closed = true;
    }
}

/// <summary>
/// A game's lines in the overlay's log (the Contract's <see cref="IGameLog"/>),
/// under the games' category, <see cref="AppLog.Games"/>, each as
/// "&lt;id&gt;: &lt;fact&gt;". A fact is cut to its first line and to
/// <see cref="MaxFact"/> characters: one game can't fill the log, break its
/// one-line-per-entry shape, or forge a line that looks like another
/// category's. Facts and counts only (constraint 4): the game's promise, which
/// the log's own scrub backs for a cookie's name.
/// </summary>
internal sealed class GameLog : IGameLog
{
    /// <summary>The longest fact a game's line keeps.</summary>
    internal const int MaxFact = 200;

    /// <summary>Every character .NET's own line reading ends a line at: CR, LF, NEL, the line and the paragraph separators (written as numbers: a separator inside a string literal would end the source line).</summary>
    private static readonly char[] LineBreaks = { '\r', '\n', (char)0x85, (char)0x2028, (char)0x2029 };

    private readonly string _id;

    public GameLog(string id) => _id = id;

    public void Log(string fact) => AppLog.Games.Log(Line(_id, fact));

    public void Warning(string fact) => AppLog.Games.Warning(Line(_id, fact));

    /// <summary>"&lt;id&gt;: &lt;fact&gt;", the fact cut at its first line break and to <see cref="MaxFact"/> characters (never inside a surrogate pair).</summary>
    internal static string Line(string id, string? fact)
    {
        var text = fact ?? "";
        var end = text.AsSpan().IndexOfAny(LineBreaks);
        if (end >= 0) text = text[..end];
        if (text.Length > MaxFact)
        {
            var cut = char.IsHighSurrogate(text[MaxFact - 1]) ? MaxFact - 1 : MaxFact;
            text = text[..cut];
        }
        return $"{id}: {text}";
    }
}
