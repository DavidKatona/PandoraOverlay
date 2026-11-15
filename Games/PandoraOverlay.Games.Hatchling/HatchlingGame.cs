using System.Windows;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Hatchling as the Games window sees it (G7, A4): the one object the window talks
/// to. It reads the save at <see cref="Attach"/>, takes every reading of the real
/// dino (<see cref="OnReading"/>), and it is the ONLY place where a player's action
/// becomes a new state on disk.
///
/// <para><b>Committed and saved before anything is shown (A20).</b> Every action a
/// view asks for (<see cref="Act"/>, <see cref="StartRun"/>, a purchase, a choice at
/// Camp) runs its pure rule, puts the new state in place and writes it with
/// <c>Now</c> BEFORE it returns, so the view only ever plays back what is already on
/// disk, and closing the window mid-playback, a click on the game, a crash, lose
/// nothing (G17). Losing a player's progress is the worst bug this feature can have,
/// and that promise is kept or broken in this one class, in <see cref="Commit"/>.</para>
///
/// <para><b>A save that could not be read back is never written.</b> Commit checks
/// the save's text against the game's own check (<see cref="HatchlingSave.Valid"/>,
/// the one the next launch's load will ask) before it replaces anything: a state the
/// check would refuse is a defect of the game, and it throws there, with the last good
/// save still on disk and the state in memory as it was, instead of writing a save
/// that would be held as unreadable at the next launch (A7).</para>
///
/// <para><b>When (A8).</b> <c>Now</c> for everything the player did (a turn, a pick,
/// a redraw, a pace, a start, leaving, back to camp, a purchase, an unlock, a choice
/// at Camp, Fast, a swap, a reset) and for a server step that did more than pay (a
/// star, an unlock, a journal entry, an achievement); <c>Soon</c> for a step that only
/// paid, which the host coalesces to one write a minute (a crash loses at most a
/// minute of that, and nothing else).</para>
///
/// <para><b>Only what the overlay saw (A6).</b> The engine's <see cref="SeenGrowth"/>
/// decides which growth was seen; it lives in memory only and is made new at every
/// Attach and Reset, so nothing bridges a restart and no growth is ever counted for
/// time the overlay was closed. The game never reads a reading's growth itself.</para>
///
/// <para><b>Fields only in the constructor</b> (A's rule for every game): the host
/// builds every listed game at start-up, before any save is read, so no state, no
/// art and no IO happen until Attach and the first picture.</para>
///
/// <para>Every member is called on the UI thread, through the host's guard or a
/// view's (A10): a throw here stops the game, never the overlay. The log is facts and
/// counts only, never a species or anything of the player's (constraint 4).</para>
/// </summary>
public sealed class HatchlingGame : IGame
{
    /// <summary>What the player may know of a creature, as the view and the turn script ask it (H3, H6).</summary>
    private static readonly Known SightKnows = (run, creature) => Sight.Knows(run, Stats.Of(run), creature);

    private IGameHost? _host;
    private GameState? _state;
    private SeenGrowth? _seen;
    private ArtBook? _art;
    private GameTile? _toldTile;
    private DinoReading _lastReading = DinoReading.NotInGame;
    private bool _hasReading;

    /// <summary>Whether the log has said, for this instance, that a reading named a species the table does not know (H89).</summary>
    private bool _toldUnknownSpecies;

    /// <summary>
    /// The one fact H89 logs: a fact, never the species' name (constraint 4: the log
    /// holds no server text). Whether the API spells all 21 species as the table does
    /// was never checked; with this line the owner's log shows a mismatch the first
    /// time he spawns as such a species.
    /// </summary>
    private const string UnknownSpeciesFact = "a reading named a species not in the species table: its growth pays nothing (A23)";

    /// <summary>Stable for ever: the save folder's name (A37).</summary>
    public string Id => "hatchling";

    public string Title => HatchlingText.Title;

    public string Pitch => HatchlingText.Pitch;

    // ===== For the view ====================================================================

    /// <summary>
    /// The state as it is on disk (or about to be, for a server step written Soon).
    /// Read only for the view: nothing outside this class may change it; every change
    /// goes through one of the methods below. Throws before Attach, and after an
    /// Attach that could not read its save until a Reset: the host shows no view then.
    /// </summary>
    internal GameState State =>
        _state ?? throw new InvalidOperationException("Hatchling has no state: it is not attached, or its save could not be read.");

    /// <summary>The last reading of the real dino, in memory only (A5); <see cref="DinoReading.NotInGame"/> before any.</summary>
    internal DinoReading LastReading => _lastReading;

    /// <summary>
    /// A reading has arrived since this instance was attached: what tells the Journal's
    /// last line "not in game" (a reading said so) from "waiting for the next reading"
    /// (none yet), which <see cref="LastReading"/> alone can't.
    /// </summary>
    internal bool HasReading => _hasReading;

    /// <summary>
    /// The state changed from outside a view's own call: a reading paid something, a
    /// reading itself changed (the Journal's last line), or a Reset. A view refreshes
    /// on it (after its playback, if one runs). Never raised by a view's own action.
    /// </summary>
    internal event Action? Changed;

    /// <summary>The host's calendar, for what a page shows (freshness, H18). Never for measuring.</summary>
    internal DateTime UtcNow => Host.Clock.UtcNow;

    // ===== The Contract ====================================================================

    /// <summary>
    /// Reads the save, once, before anything else (A4, A7, A34). Never throws for a
    /// bad save; it says so:
    /// <list type="bullet">
    /// <item>No save: a fresh state (H25), nothing written, Ready.</item>
    /// <item>A save (or the backup, which the host already warned of): through the
    /// schema. Ok is the state, written back once with <c>Now</c> only if it was
    /// upgraded (A9); Newer answers SaveFromNewerVersion and Corrupt SaveUnreadable,
    /// and the state stays unset: the host holds the file untouched.</item>
    /// <item>A save the host could not read: SaveUnreadable, and the schema is never
    /// asked, whatever version or text the read may carry (A34).</item>
    /// </list>
    /// A loaded journal with fewer open entries than <see cref="Tuning.JournalEntries"/>
    /// is filled (server play assumes three and never tops it up), in memory: it is
    /// written with the next save. A new <see cref="SeenGrowth"/> starts here.
    /// </summary>
    public AttachResult Attach(IGameHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _state = null;
        _seen = new SeenGrowth();
        _toldTile = null;

        var read = host.Storage.Read();
        switch (read.State)
        {
            case SaveReadState.None:
                Begin(GameState.Fresh(host.Clock.UtcNow));
                Log("attached: fresh");
                return AttachResult.Ready;

            case SaveReadState.Ok:
            case SaveReadState.FromBackup:
                return Load(read);

            default:
                // Unreadable, or a state this build does not know: when in doubt, hold.
                Log("attached: the save could not be read");
                return AttachResult.SaveUnreadable;
        }
    }

    /// <summary>
    /// Every successful poll while Ready, window open or not (A5). The seen step, if
    /// the reading made one, is paid by server play (H10, H18) and saved:
    /// <c>Now</c> when it did more than pay, else <c>Soon</c>. A species the table does
    /// not know is ignored (A23), and the first in-game reading of one (or of none)
    /// per instance logs a single fact without its name (H89). Cheap: a reading that
    /// makes no step allocates nothing and writes nothing, and raises
    /// <see cref="Changed"/> only when it differs from the reading before.
    /// </summary>
    public void OnReading(DinoReading reading)
    {
        if (_host is null || _state is null || _seen is null) return;
        var step = _seen.Update(reading, _host.Clock.Elapsed);
        var changed = !_hasReading || reading != _lastReading;
        _lastReading = reading;
        _hasReading = true;

        // H89: an in-game reading of a species the table does not know (or of none) pays
        // nothing (A23); say so once, as a fact, never the name (constraint 4).
        if (reading.InGame && !_toldUnknownSpecies && !SpeciesTable.TryGet(reading.Species, out _))
        {
            _toldUnknownSpecies = true;
            Log(UnknownSpeciesFact);
        }

        // ServerPlay.Apply answers a new state for an unknown species too (and so is
        // never compared with the old one by reference): asked here first, so a
        // species the game does not know costs neither a copy nor a write.
        if (step is { } seen && SpeciesTable.TryGet(seen.Species, out _))
        {
            var result = ServerPlay.Apply(_state, seen, _host.Clock.UtcNow);
            Commit(result.State, result.Notable ? SaveUrgency.Now : SaveUrgency.Soon);
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// The tile (A4): the tag; a run under way, "Under way: Juvenile Dryosaurus"
    /// (Good); a run ended and not yet left, "Your run has ended"; else the species
    /// unlocked and the wallet. The detail counts the achievements earned (an id this
    /// build does not know is kept in the save but not counted). Reset is offered once
    /// the state differs from a fresh one. No side effects.
    /// </summary>
    public GameTile Tile() => TileOf(State);

    /// <summary>The tile's picture: the board in small, with the chosen species (task H13's <see cref="TileArt"/>).</summary>
    public FrameworkElement CreateTileArt() => TileArt.Create(Art(), State.Selected);

    /// <summary>
    /// The game on screen (task H14's <see cref="HatchlingView"/>): the bar and the
    /// pages, over the host this game was attached to, with the game's one art book
    /// (loaded once, shared with the tile). The view reads <see cref="State"/> and
    /// changes it only through this class's actions (A20): closing it at any moment
    /// loses nothing. Throws before Attach, or after an Attach that could not read its
    /// save: the host shows no view then.
    /// </summary>
    public IGameView CreateView()
    {
        _ = State;   // no state, no view: the same refusal as State's
        return new HatchlingView(this, Host, Art());
    }

    /// <summary>
    /// The window's armed second click (or "Start fresh" on a held save, after the
    /// host set the old files aside, A7, A38): a fresh state (H25), written
    /// <c>Now</c>, and a new <see cref="SeenGrowth"/>. Valid on an instance whose
    /// Attach could not read its save. Before any Attach it throws: a Reset that
    /// could not be written must never look as if it was.
    /// </summary>
    public void Reset()
    {
        var host = Host;
        _seen = new SeenGrowth();
        Commit(GameState.Fresh(host.Clock.UtcNow), SaveUrgency.Now);
        Log("reset: a fresh save");
        Changed?.Invoke();
    }

    // ===== The player's actions (each commits and saves Now before it returns) ==============

    /// <summary>
    /// One action in the run (A20): the rules answer it, or refuse it (null: nothing
    /// rolled, nothing written). Answered, the new run goes into a copy of the state;
    /// a Redraw first pays its price from the wallet (refused when short); a run that
    /// just ended is settled in the same step (its pay banked, its records kept, H20);
    /// a move that passed a turn stores the turn list the script reads from it (H28);
    /// the state is replaced and written <c>Now</c>; THEN the result is returned. Its
    /// <c>After</c> is the run as committed, settled when the run ended, so what the
    /// view plays and shows is what the save holds.
    /// </summary>
    internal TurnResult? Act(PlayerAction action)
    {
        var advanced = Advance(State, action);
        if (advanced is null) return null;
        var (next, result) = advanced.Value;
        Commit(next, SaveUrgency.Now);
        if (next.Run?.Over is { } over && result.Before.Over is null)
            Log($"run ended: {EndWord(over)}, turn {next.Run.Turn}");
        return result;
    }

    /// <summary>
    /// A new run of the chosen species at the chosen danger (H19: with the lineage
    /// and the stars as they are now) on the island of the next seed, its turn list
    /// the start's; saved <c>Now</c>. False, and nothing written, when Camp can't start
    /// one: a run exists (H20), the species is locked, or the danger is not unlocked.
    /// </summary>
    internal bool StartRun()
    {
        if (Started(State) is not { } next) return false;
        Commit(next, SaveUrgency.Now);
        Log($"run started: danger {next.Run!.Danger}");
        return true;
    }

    // ===== The two steps a view's actions are made of, pure (A20) ===========================

    /// <summary>
    /// What <see cref="Act"/> commits, worked out without committing it: the state
    /// after <paramref name="action"/> and the turn's result, or null when it is
    /// refused (no run, the rules refuse it, or a Redraw the wallet can't pay). The
    /// state given is never changed. Pure, so the save's tests play careers through
    /// the very step the game takes.
    /// <list type="number">
    /// <item>A Redraw's price (<see cref="Offers.RedrawPrice"/>, nothing while the free
    /// one lasts) must be in the wallet; it is spent from the copy.</item>
    /// <item>The rules answer (<see cref="TurnRules.Apply"/>); their new run goes into
    /// a copy of the state.</item>
    /// <item>A move that passed a turn stores the turn list the script reads from it
    /// (H28); a pick, a redraw, a pace or leaving keeps the list as last shown.</item>
    /// <item>A run that just ended is settled in the same step (H20): its pay banked
    /// and its records kept before anything shows its summary.</item>
    /// </list>
    /// The result's <c>After</c> is the run as it goes into the state, settled when it
    /// ended, so what a view plays back and shows is what the save holds.
    /// </summary>
    internal static (GameState State, TurnResult Result)? Advance(GameState state, PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        if (state.Run is not { } run) return null;

        var price = action is PlayerAction.Redraw && run.Offer is not null ? Offers.RedrawPrice(run) : 0;
        if (price > state.Wallet.Balance) return null;

        var result = TurnRules.Apply(run, action);
        if (result is null) return null;

        var next = state.Clone();
        if (!next.Wallet.TrySpend(price)) return null;
        var after = result.After;
        if (PassesATurn(action))
            after.LastTurn = new List<TurnRow>(TurnScripts.Build(result, SightKnows).Rows);
        next.Run = after;

        if (after.Over is { Settled: false }) next = Settle.Run(next);
        return (next, result with { After = next.Run! });
    }

    /// <summary>
    /// What <see cref="StartRun"/> commits, worked out without committing it: the
    /// state with a new run of the chosen species at the chosen danger, on the island
    /// of the next seed, its turn list the start's; null when Camp can't start one.
    /// The state given is never changed.
    /// </summary>
    internal static GameState? Started(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Camp.CanStart(state)) return null;
        var run = Island.NewRun(state.SetupFor(state.Selected, state.Danger), state.NextSeed);
        run.LastTurn = new List<TurnRow>(TurnScripts.RowsAtStart(run, SightKnows));
        var next = state.Clone();
        next.Run = run;
        return next;
    }

    /// <summary>Leaves an ended run's summary for Camp (H20); false while a run goes on or none exists.</summary>
    internal bool BackToCamp()
    {
        var state = State;
        if (state.Run?.Over is null) return false;
        return Apply(Settle.BackToCamp(state));
    }

    /// <summary>Chooses a species on Camp, locked or not; false for a name the table does not know.</summary>
    internal bool Select(string species) =>
        SpeciesTable.TryGet(species, out _) && Apply(Camp.Select(State, species));

    /// <summary>Chooses the next run's danger; false above the highest unlocked.</summary>
    internal bool SetDanger(int danger) => Apply(Camp.SetDanger(State, danger));

    /// <summary>The playback's pace (H5), kept in the save.</summary>
    internal bool SetFast(bool fast) => Apply(Camp.SetFast(State, fast));

    /// <summary>Buys the next rank of a trait (H11); a run under way is untouched (H19).</summary>
    internal bool BuyTrait(Trait trait) => Apply(LineageRules.Buy(State, trait));

    /// <summary>Unlocks a species with the wallet (H10).</summary>
    internal bool Unlock(string species) => Apply(Unlocks.Buy(State, species));

    /// <summary>Swaps the journal's entry at <paramref name="entry"/> for a new draw (H12).</summary>
    internal bool Swap(int entry) => Apply(Journal.Swap(State, entry));

    // ===== Inside ==========================================================================

    private IGameHost Host =>
        _host ?? throw new InvalidOperationException("Hatchling is not attached: it has no host to save through.");

    /// <summary>A rule's answer: null, refused and nothing written; else replaced and written <c>Now</c>.</summary>
    private bool Apply(GameState? next)
    {
        if (next is null) return false;
        Commit(next, SaveUrgency.Now);
        return true;
    }

    /// <summary>
    /// THE one way a state becomes the game's (A20): its save text made and checked
    /// first, so a defect throws with nothing replaced and the last good save still on
    /// disk; then the state replaced and the text handed to the host; then the tile
    /// told, if what it says changed. The host's storage never throws; while the save
    /// is held it refuses the write and logs it (A7), and the game plays on.
    /// </summary>
    private void Commit(GameState next, SaveUrgency urgency)
    {
        var host = Host;
        var data = HatchlingSave.From(next);
        if (HatchlingSave.Problem(data) is { } problem)
        {
            // The log first (H70): the app logs an exception's type and stack, never
            // its message, so the one fact that finds the defect (which field, which
            // rule) would otherwise be lost. The check's words name no value.
            host.Log.Warning($"not saved: the game's own check refuses the new state: {problem}");
            throw new InvalidOperationException(
                $"Hatchling would write a save its own check refuses ({problem}): a defect of the game; nothing was replaced or written.");
        }
        var payload = HatchlingSave.Schema.Save(data);

        _state = next;
        host.Storage.Write(HatchlingSave.Version, payload, urgency);

        var tile = TileOf(next);
        if (tile != _toldTile)
        {
            _toldTile = tile;
            host.TileChanged();
        }
    }

    /// <summary>
    /// A save read by the host, through the schema (A9). A refusal says why in the log,
    /// as a fact (H72): the check's words, or that the text is not this version's shape.
    /// </summary>
    private AttachResult Load(SaveRead read)
    {
        var (loaded, why) = HatchlingSave.Read(read.Version, read.Payload ?? "");
        switch (loaded.Outcome)
        {
            case SaveOutcome.Newer:
                Log($"attached: the save is from version {read.Version}, newer than this build's {HatchlingSave.Version}");
                return AttachResult.SaveFromNewerVersion;

            case SaveOutcome.Ok when loaded.State is { } data:
                var state = HatchlingSave.To(data);
                if (state.Journal.Open.Count < Tuning.JournalEntries) Journal.Fill(state);
                Begin(state);
                Log($"attached: version {read.Version}{PhaseOf(state)}{(read.State == SaveReadState.FromBackup ? ", from the backup" : "")}");
                if (loaded.Upgraded)
                {
                    Commit(state, SaveUrgency.Now);
                    Log($"save upgraded: version {read.Version} to {HatchlingSave.Version}");
                }
                return AttachResult.Ready;

            default:
                _host?.Log.Warning($"attached: the save could not be read (version {read.Version}): {why ?? "its text is not this version's shape"}");
                return AttachResult.SaveUnreadable;
        }
    }

    /// <summary>The state an Attach found, in place, and the tile the grid will draw from it as the one told.</summary>
    private void Begin(GameState state)
    {
        _state = state;
        _toldTile = TileOf(state);
    }

    private static GameTile TileOf(GameState state)
    {
        var run = state.Run;
        var (status, tone) = run switch
        {
            { Over: null } => (HatchlingText.TileUnderWay(Stages.Of(run.You.Growth), run.Species), GameTileTone.Good),
            not null => (HatchlingText.TileEnded, GameTileTone.Neutral),
            _ => (HatchlingText.TileIdle(state.Unlocked.Count, state.Wallet.Balance), GameTileTone.Neutral),
        };
        var achievements = state.Achievements;
        var earned = achievements.All.Count(a => achievements.Has(a.Id));
        return new GameTile(status, tone, HatchlingText.TileDetail(earned, achievements.All.Count), HatchlingText.Tag, !state.IsFresh);
    }

    /// <summary>The art, loaded once, at the first picture (task H13's book of the game's own atlases, A28).</summary>
    private ArtBook Art() => _art ??= ArtBook.Load(typeof(HatchlingGame).Assembly);

    /// <summary>Your move or the world's turn: what makes a new turn list (Pick, Redraw, a pace and leaving keep the last one, H28).</summary>
    private static bool PassesATurn(PlayerAction action) =>
        action is PlayerAction.Move or PlayerAction.Eat or PlayerAction.Drink or PlayerAction.Rest or PlayerAction.Attack;

    private static string PhaseOf(GameState state) => state.Run switch
    {
        null => "",
        { Over: null } => ", a run under way",
        _ => ", a run ended",
    };

    /// <summary>How a run ended, for the log: a fact, never who or what played it.</summary>
    private static string EndWord(RunOver over) => over.Won
        ? "won"
        : over.Reason switch
        {
            EndReason.Caught => "caught",
            EndReason.Hunger => "out of food",
            EndReason.Thirst => "out of water",
            EndReason.Left => "left",
            _ => "ended",
        };

    private void Log(string fact) => _host?.Log.Log(fact);
}
