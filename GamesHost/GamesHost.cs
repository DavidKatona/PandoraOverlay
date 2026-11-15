using PandoraOverlay.Games;

namespace PandoraOverlay;

/// <summary>
/// The app's side of the Contract: the listed games, attached, fed, guarded
/// and saved. Everything a game may do to the overlay passes through here: a
/// reading in, a save out, and a fault that must never take the overlay down
/// or cost a request.
/// <para>
/// <b>Fed, never asking.</b> MainWindow hands it every successful poll's
/// answer (<see cref="Feed"/>, the last line of its snapshot handler, on the UI
/// thread), and a game learns only <see cref="DinoReading"/>: in game or not,
/// the species, the gender, the growth (A5). A failed poll feeds nothing, so a
/// game sees a gap as a gap. Nothing here names a request, the cookie or a
/// process (a source scan holds it), so the games add no request and no
/// request trigger, and opening Games nudges no poll (A13).
/// </para>
/// <para>
/// <b>Guarded (A10).</b> Every call into a game goes through
/// <see cref="GameGuard"/>; a game that throws is stopped
/// (<see cref="GameCondition.Faulted"/>, <see cref="Stop"/>), fed nothing, and
/// can be started again from its last save (<see cref="StartAgain"/>); the
/// others play on. A game's own handlers report through
/// <see cref="IGameHost.Fault"/>, and what slips past both is caught by
/// <see cref="GameFaults"/> in App's handler.
/// </para>
/// <para>
/// <b>Saved (A7, A8).</b> Each game writes through its own
/// <see cref="GameStorage"/> over the one <see cref="GameSaveStore"/>. A save
/// a game can't take is held, and only the player's armed "Start fresh" sets
/// it aside (<see cref="StartFresh"/>). The tile's two buttons that replace a
/// game's state SAVE FIRST: a Reset writes what is pending and keeps the save
/// it replaces before it forgets anything, and resets nothing when it can't
/// (A38); "Start it again" writes what the stopped instance had pending before
/// it makes a new one, and starts nothing when it can't (A49). Each then
/// answers false, the tile says why, and the player can click again: a refusal
/// costs a click while the disk is failing, where going ahead would lose what
/// the player would miss without a word on screen.
/// </para>
/// <para>
/// <b>No IO until <see cref="AttachAll"/>.</b> The constructor only makes the
/// instances: MainWindow makes the host in its constructor and attaches the
/// games on Loaded, which a window never shown never reaches, so the tests and
/// the render harness never read or write a real save.
/// </para>
/// Everything here runs on the UI thread, where both events are raised.
/// </summary>
internal sealed class GamesHost
{
    private readonly GameSaveStore _store;
    private readonly IGameClock _clock;
    private readonly Func<string, IGameLog> _logFor;
    private readonly HostedGame[] _games;
    private bool _attached;

    /// <param name="sound">The games' sound gate (A79), MainWindow's over its config and this clock; none (the tests, the render harness): one over <see cref="NullSpeaker"/> and a fresh config.</param>
    public GamesHost(GameSaveStore store, IReadOnlyList<GameEntry> catalog, IGameClock clock, GamesSound? sound = null)
        : this(store, catalog, clock, id => new GameLog(id), sound)
    {
    }

    /// <param name="logFor">Each game's log (a test's collects the lines; the app's writes them to <see cref="AppLog.Games"/>).</param>
    /// <param name="sound">As the public constructor's.</param>
    internal GamesHost(GameSaveStore store, IReadOnlyList<GameEntry> catalog, IGameClock clock, Func<string, IGameLog> logFor, GamesSound? sound = null)
    {
        _store = store;
        _clock = clock;
        _logFor = logFor;
        Sound = sound ?? new GamesSound(new NullSpeaker(), new OverlayConfig(), clock);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        _games = new HostedGame[catalog.Count];
        for (var i = 0; i < catalog.Count; i++)
        {
            var entry = catalog[i];
            var game = new HostedGame(this, entry);
            _games[i] = game;
            game.Defect = DefectOf(entry, seen);
            if (game.Defect is { } defect)
            {
                game.Condition = GameCondition.Faulted;
                AppLog.Games.Error($"A listed game can't play: {defect}");
                continue;
            }
            Make(game);
        }
        Games = Array.AsReadOnly(_games);
    }

    /// <summary>The listed games, in the catalog's order (G1).</summary>
    public IReadOnlyList<HostedGame> Games { get; }

    /// <summary>
    /// The games' sound gate (G26, A79): every instance's <see cref="IGameHost.Sound"/>
    /// asks it, the window tells it what it shows and works its mute and
    /// volume through it. MainWindow disposes it on the way out.
    /// </summary>
    public GamesSound Sound { get; }

    // A subscriber of either event is the app's own code (the window,
    // MainWindow): one that throws is the app's own bug, and is not caught here
    // (A50). Raised from the host's own code (a Reset, StartAgain, Stop in a
    // guard's catch), it crashes as any app bug. Raised inside a game's call (a
    // TileChanged from a game's Commit during Feed, a fault a view's handler
    // reported), the throw is on that game's stack, and A10's layers contain it
    // as the game's: nothing is lost, and the log keeps the app's frames (A63).

    /// <summary>A game's tile texts changed (the game said so, or the host reset it): the window redraws that tile in place.</summary>
    public event Action<HostedGame>? TileChanged;

    /// <summary>A game's <see cref="HostedGame.Condition"/> changed: the window rebuilds that tile, and closes the game if it is on screen and stopped.</summary>
    public event Action<HostedGame>? ConditionChanged;

    // ---- Making and attaching ---------------------------------------------------------

    /// <summary>Why an entry can never play, or null: an id the store would refuse (A37), or one an earlier entry has (G3).</summary>
    private static string? DefectOf(GameEntry entry, HashSet<string> seen)
    {
        if (entry.Create is null) return $"the entry \"{entry.Id}\" makes no instance";
        if (!GameIds.IsValid(entry.Id)) return $"\"{entry.Id}\" is not a game id (A37)";
        if (!seen.Add(entry.Id)) return $"\"{entry.Id}\" is listed twice, and two games never share a save (G3)";
        return null;
    }

    /// <summary>
    /// A new instance from the catalog, its title and pitch read once (all
    /// guarded): a constructor that throws leaves the game Faulted, its title
    /// its id. True when there is an instance that has not faulted.
    /// </summary>
    private bool Make(HostedGame game)
    {
        IGame? instance = null;
        if (!GameGuard.Run(game, "its constructor", () => { instance = game.Entry.Create(); })) return false;
        if (instance is null)
        {
            Stop(game, "its constructor (it made no instance)", null);
            return false;
        }
        game.Instance = instance;
        game.Host = null;
        game.Attached = false;
        game.Condition = GameCondition.Ready;
        game.Title = GameGuard.Run(game, "Title", () => instance.Title) is { Length: > 0 } title ? title : game.Id;
        game.Pitch = game.Condition == GameCondition.Ready ? GameGuard.Run(game, "Pitch", () => instance.Pitch) ?? "" : "";
        return game.Condition == GameCondition.Ready;
    }

    /// <summary>
    /// Every game's <see cref="IGame.Attach"/>, once: from MainWindow's
    /// Loaded, never its constructor (no IO until then). A second call does
    /// nothing.
    /// </summary>
    public void AttachAll()
    {
        if (_attached) return;
        _attached = true;
        foreach (var game in _games)
        {
            if (game.Condition == GameCondition.Ready && game.Instance is not null) Attach(game);
        }
    }

    /// <summary>
    /// One instance's Attach, with what it is given made for it alone, and the
    /// answer checked against the storage's own read: a save the host could
    /// not read is SaveUnreadable whatever the game answered (it does not get
    /// to overlook one), and a held answer holds the save in the store, so
    /// nothing replaces it until the player's "Start fresh" (A7). A save read
    /// from the backup is Ready, with a Warning. False: Attach threw (the game
    /// is then Faulted).
    /// </summary>
    private bool Attach(HostedGame game)
    {
        var instance = game.Instance!;
        var log = _logFor(game.Id);
        var storage = new GameStorage(_store, game.Id, _clock, log);
        var host = new GameHost(this, game, _clock, storage, log);
        game.Host = host;
        var answer = AttachResult.Ready;
        if (!GameGuard.Run(game, "Attach", () => { answer = instance.Attach(host); })) return false;
        game.Attached = true;
        // A game that reported a fault of its own while attaching stays stopped.
        if (game.Condition == GameCondition.Faulted) return false;

        // A game that never read its save is checked too: the host reads it.
        var read = storage.AttachRead ?? storage.Read();
        var condition = ConditionOf(answer, read.State);
        game.Condition = condition;
        if (condition is GameCondition.SaveUnreadable or GameCondition.SaveFromNewerVersion) Hold(game);
        else if (read.State == SaveReadState.FromBackup) log.Warning("plays from its backup: the save itself could not be read");
        log.Log($"attached: {condition}");
        return true;
    }

    /// <summary>
    /// The condition an Attach leaves: the storage's Unreadable outranks the
    /// game's answer; a game's own SaveUnreadable or SaveFromNewerVersion
    /// stands; an answer the Contract does not name holds the save too (when
    /// in doubt, hold).
    /// </summary>
    internal static GameCondition ConditionOf(AttachResult answer, SaveReadState read)
    {
        if (read == SaveReadState.Unreadable) return GameCondition.SaveUnreadable;
        return answer switch
        {
            AttachResult.Ready => GameCondition.Ready,
            AttachResult.SaveFromNewerVersion => GameCondition.SaveFromNewerVersion,
            _ => GameCondition.SaveUnreadable,
        };
    }

    private void Hold(HostedGame game)
    {
        try
        {
            _store.Hold(game.Id);
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"{game.Id}: the save could not be held: {AppLog.Short(ex)}");
        }
    }

    // ---- The feed (A5) -----------------------------------------------------------

    /// <summary>
    /// Every successful poll: one reading to every Ready game, then each
    /// game's due save (A8). A game that throws is stopped and the others are
    /// still fed. It never throws for a game, writes no log line of its own
    /// and allocates nothing (the reading is a struct, the guarded call a
    /// static lambda): it runs on every poll of the overlay, every 3 seconds,
    /// window open or not.
    /// </summary>
    public void Feed(MyLocationResponse snapshot)
    {
        if (snapshot is null) return;
        var reading = snapshot.InGame && snapshot.Player is { } p
            ? new DinoReading(true, p.Dino, p.Gender, p.Growth)
            : DinoReading.NotInGame;
        var games = _games;
        for (var i = 0; i < games.Length; i++)
        {
            var game = games[i];
            if (game.Plays) GameGuard.Run(game, "OnReading", (game.Instance!, reading), static s => s.Item1.OnReading(s.Item2));
        }
        var elapsed = _clock.Elapsed;
        for (var i = 0; i < games.Length; i++) games[i].Host?.OwnStorage.FlushIfDue(elapsed);
    }

    /// <summary>
    /// Every pending save, now: the window closing, Windows ending the
    /// session, exit. Faulted games included: what a game handed over before
    /// it failed was a whole state.
    /// </summary>
    public void Flush()
    {
        foreach (var game in _games) game.Host?.OwnStorage.Flush();
    }

    // ---- The tile's buttons ------------------------------------------------------

    /// <summary>
    /// The armed second click on a tile's "Start fresh" (a held save) or
    /// "Reset" (a Ready game). SAVE AND KEEP FIRST, RESET SECOND (A38, A49):
    /// the old state is written and put aside before anything is forgotten,
    /// and when it can't be, nothing is reset and the answer is false; the tile
    /// says so, and the player can click again. A Reset that throws stops the
    /// game as any call does, and the answer is false.
    /// <para>
    /// A held save: its files are set aside (<c>unreadable-&lt;n&gt;.*</c>,
    /// never deleted), which releases the hold; then the game's Reset writes
    /// its fresh state, and the game is Ready. A Ready game: what is pending
    /// is written first, so the copy is its newest state, and when that write
    /// fails nothing is reset and the pending text is kept for the next flush
    /// (A49: what is pending can be a failed Now write, a committed turn); then
    /// that save is copied to a new <c>save.before-reset-&lt;n&gt;.json</c>
    /// (A62), the player's way back from a Reset by mistake, and when the copy
    /// fails nothing is reset; then the Reset. A Faulted game has nothing to
    /// reset ("Start it again" is its button): false.
    /// </para>
    /// </summary>
    public bool StartFresh(HostedGame game)
    {
        if (game.Instance is not { } instance || game.Host is not { } host || !game.Attached) return false;
        switch (game.Condition)
        {
            case GameCondition.SaveUnreadable or GameCondition.SaveFromNewerVersion:
                if (!SetAside(game)) return false;
                host.OwnStorage.Clear();
                if (!GameGuard.Run(game, "Reset", instance.Reset) || game.Condition == GameCondition.Faulted) return false;
                game.Condition = GameCondition.Ready;
                host.Log.Log("started fresh: its old save was set aside");
                ConditionChanged?.Invoke(game);
                return true;

            case GameCondition.Ready:
                host.OwnStorage.Flush();
                if (host.OwnStorage.HasPending)
                {
                    host.Log.Warning("not reset: its last pending save could not be written first");
                    return false;
                }
                if (!KeepBeforeReset(game))
                {
                    host.Log.Warning("not reset: its save could not be kept first");
                    return false;
                }
                host.OwnStorage.Clear();
                if (!GameGuard.Run(game, "Reset", instance.Reset) || game.Condition == GameCondition.Faulted) return false;
                host.Log.Log("reset: its old save was kept as the one before the reset");
                TileChanged?.Invoke(game);
                return true;

            default:
                return false;
        }
    }

    private bool SetAside(HostedGame game)
    {
        try
        {
            return _store.SetAside(game.Id);
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"{game.Id}: the save could not be set aside: {AppLog.Short(ex)}");
            return false;
        }
    }

    private bool KeepBeforeReset(HostedGame game)
    {
        try
        {
            return _store.KeepBeforeReset(game.Id);
        }
        catch (Exception ex)
        {
            AppLog.Games.Warning($"{game.Id}: the save could not be kept before a reset: {AppLog.Short(ex)}");
            return false;
        }
    }

    /// <summary>
    /// The tile's "Start it again", for a Faulted game. SAVE FIRST, START AGAIN
    /// SECOND (A49): what the stopped instance handed over before it failed (a
    /// whole state) is written BEFORE anything is made. Then the old instance
    /// is detached (nothing it still does reaches the host or the disk), and a
    /// new instance from the catalog is attached to that last save, as at
    /// <see cref="AttachAll"/>, raising <see cref="ConditionChanged"/> once
    /// (unless the new instance failed in its constructor: the game is then
    /// still Faulted, as it was, and the log has it; an <c>Attach</c> that
    /// throws stops it through the guard, which raises the event).
    /// <para>
    /// False means one thing only: the pending save could not be written, so
    /// nothing was started and nothing is lost. The text stays pending (the
    /// next flush tries again), the old instance is not detached, the game is
    /// still Faulted, no event is raised; the tile says so and the player can
    /// click again. What is pending is not only a minute of accrual: a Now write
    /// that failed (a committed turn, a purchase) is kept pending too, and a new
    /// instance started from the older save would write over it. True in every
    /// other case: a game that is not Faulted (nothing to do), a catalog defect
    /// (a Warning, nothing to start), and a new instance that failed again (it
    /// is stopped as before).
    /// </para>
    /// </summary>
    public bool StartAgain(HostedGame game)
    {
        if (game.Condition != GameCondition.Faulted) return true;
        if (game.Defect is not null)
        {
            AppLog.Games.Warning($"{game.Id}: can't be started again: {game.Defect}");
            return true;
        }
        if (game.Host is { } old)
        {
            old.OwnStorage.Flush();
            if (old.OwnStorage.HasPending)
            {
                old.Log.Warning("not started again: its last pending save could not be written first");
                return false;
            }
            old.Detach();
        }
        if (!Make(game)) return true; // a constructor that threw again: still Faulted, as it was
        if (_attached && !Attach(game)) return true; // Attach threw: Stop raised ConditionChanged
        game.Host?.Log.Log("started again after an error");
        ConditionChanged?.Invoke(game);
        return true;
    }

    // ---- Faults (A10) ------------------------------------------------------------

    /// <summary>
    /// The game is stopped: Faulted, fed nothing, its storage taking no new
    /// write, <see cref="ConditionChanged"/> raised (the window closes it if
    /// it is on screen). Once: a second fault of a stopped game is only
    /// logged. For a fault that came from outside the guard: a game's own
    /// handler (through its host) or the last net (<see cref="GameFaults"/>).
    /// </summary>
    public void Fault(HostedGame game, Exception error) => Stop(game, "its own code", error);

    /// <summary>
    /// The one place a game is stopped, and its one log line: "&lt;id&gt;
    /// failed in &lt;what&gt;", the exception as its type and stack, never its
    /// message (constraint 4).
    /// </summary>
    internal void Stop(HostedGame game, string what, Exception? error)
    {
        if (error is null) AppLog.Games.Error($"{game.Id} failed in {what}");
        else AppLog.Games.Exception(LogVerbosity.Error, $"{game.Id} failed in {what}", error);
        if (game.Condition == GameCondition.Faulted) return;
        game.Condition = GameCondition.Faulted;
        game.Host?.OwnStorage.Stop();
        ConditionChanged?.Invoke(game);
    }

    /// <summary>A game's host said its tile changed: passed on while that instance plays (a stopped one's tile shows the host's words, not its own).</summary>
    internal void OnTileChanged(GameHost host)
    {
        if (host.Current && host.Game.Host == host && host.Game.Plays) TileChanged?.Invoke(host.Game);
    }

    /// <summary>A game caught an exception of its own and reported it (the second layer, A10). A detached instance's report is only logged.</summary>
    internal void OnFault(GameHost host, Exception error)
    {
        if (host.Current && host.Game.Host == host)
        {
            Stop(host.Game, "its view or its loop", error);
            return;
        }
        if (error is null) AppLog.Games.Error($"{host.Game.Id}: a replaced instance reported a fault");
        else AppLog.Games.Exception(LogVerbosity.Error, $"{host.Game.Id}: a replaced instance reported a fault", error);
    }
}
