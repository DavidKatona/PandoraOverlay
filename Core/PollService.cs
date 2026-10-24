using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// Owns the HTTP client and the poll timer so every window consumes ONE shared
/// request stream — adding views (stats panel, minimap, …) never adds requests
/// (hard constraint: a single mylocation poll every >= 2 s). The cadence is
/// adaptive, and only ever downwards: the configured interval applies while
/// in-game, and the poll idles while nobody is spawned (menus, restarts, the
/// game closed) or the connection keeps failing. A second, slower timer
/// refetches the minimap's heatmap layer while it is enabled, and the friends
/// roster rides every second in-game poll while a friends surface is shown.
/// Runs entirely on the UI thread via DispatcherTimer, so subscribers may
/// touch UI directly. The Patreon skins requests (v1.28, click-driven only)
/// live in PollService.Skins.cs, the Dino storage requests (1.33, likewise)
/// in PollService.Storage.cs, the map calibration (its seed and retry) in
/// PollService.Calibration.cs.
/// The heatmap's one pacing rule (never two fetches less than a minute
/// apart, whatever asks for one) and the big map's wider gates live in
/// PollService.Maps.cs.
/// </summary>
public sealed partial class PollService : IDisposable
{
    // ---- Idle pacing --------------------------------------------------------
    // An overlay left running with the game closed used to send ~28,800
    // pointless polls a day at 3 s. Not in-game → IdleInterval; still not
    // in-game after DeepIdleAfter → DeepIdleInterval (~1,440/day). The price
    // is noticing a spawn that much later, so user activity (Nudge) and a
    // Check Prime click poll right away instead of waiting the interval out.
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DeepIdleInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeepIdleAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Consecutive failed polls before a broken connection (expired cookie,
    /// site down) is paced like not-in-game. A blip or two must not slow the
    /// minimap down mid-game.
    /// </summary>
    private const int FailuresBeforeIdle = 5;

    /// <summary>
    /// Seconds between heatmap refetches while the layer is on — the live-map
    /// page itself refetches every 10 s per open tab, so this stays well
    /// under the site's own footprint. Deliberately not configurable.
    /// </summary>
    private const int HeatmapIntervalSeconds = 60;

    /// <summary>
    /// Fallback prime-check cooldown, used only when the server's own answer
    /// is unavailable. The real length differs per account (supporter ranks
    /// shorten it), so after each successful check the server is asked once.
    /// </summary>
    private static readonly TimeSpan DefaultPrimeCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Floor under every cooldown, and the breather after a failed check: the
    /// check can never be button-mashed, whatever the server would allow.
    /// </summary>
    private static readonly TimeSpan PrimeRetryGuard = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The friends roster rides every Nth in-game poll (every 2nd at the
    /// default 3 s = 6 s, under the live-map page's own 5 s) and every idle
    /// poll (15 s / 60 s — friends matter while you sit in the menu). It is
    /// fetched right after a successful mylocation poll — the one exception
    /// being RefreshFriendsAsync's hot path between polls, which needs the
    /// last poll to have succeeded and the last roster to be at least one
    /// poll interval old — so it can never outrun constraint #3, and only
    /// while a friends surface is shown (see FriendsWanted). A poll's own
    /// fetch also waits while the last roster is under one interval old
    /// (FriendsDue): before Oct 2026 a hot fetch just before an idle tick was
    /// followed by the tick's own under a second later.
    /// </summary>
    private const int FriendsEveryNthPoll = 2;

    /// <summary>Consecutive friends misses before the layers are told to clear — one blip keeps the last roster.</summary>
    private const int FriendMissesBeforeClear = 2;

    /// <summary>
    /// Consecutive polls the site refused as "no session" (v1.31) before the
    /// session is declared over: a dead cookie answers 403 forever, so
    /// polling stops and a new sign-in is asked for instead of a retry every
    /// minute for good. Two, not one, so a single odd answer is not it.
    /// </summary>
    private const int RefusalsBeforeSignedOut = 2;

    private readonly OverlayConfig _config;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _heatmapTimer;
    private readonly TimeSpan _activeInterval;
    private PandoraClient _client;
    // One request of the poll stream at a time: the poll with its friends and
    // calibration follow-ups, or a hot friends fetch (RefreshFriendsAsync).
    private bool _busy;
    private bool _heatmapBusy;
    private bool _primeBusy;
    private bool _inGame;
    private string? _dino;
    private string? _steamId; // yours, once seen; the friends roster may include you
    private int _failStreak;
    private int _refusals; // consecutive "no session" answers, see RefusalsBeforeSignedOut
    private int _pollsSinceFriends;
    private int _friendMisses;
    private bool _friendsDue;   // a hot trigger arrived while busy or too soon: fetch on the next poll
    private readonly List<string> _friendsDueFor = new(); // LogFriends: every hot trigger waiting for that poll
    private DateTime _lastPollUtc;
    private DateTime _lastFriendsUtc;
    private DateTime _lastLiveUtc = DateTime.UtcNow;
    private bool _answered;       // LogPoll: this session's first answer has been noted
    private string? _lastFailure; // LogPoll: the cause of the last failed poll, so a run of the same one is one Warning

    /// <summary>Current gap between polls: the configured one in-game, longer while idling.</summary>
    public TimeSpan Interval => _timer.Interval;

    /// <summary>True while the poll runs slower than configured (not in-game, or persistently failing).</summary>
    public bool IsIdling => _timer.Interval > _activeInterval;

    /// <summary>Latest cookie, including any rolled connect.sid (persist on exit).</summary>
    public string CurrentCookie => _client.CurrentCookie;

    /// <summary>Last known map calibration: fetched this launch, else the config-cached copy, else the bundled seed.</summary>
    public MapCalibration? Calibration { get; private set; }

    /// <summary>You, as the last successful poll saw you in game; null while not in game (the Dino storage page's distance and arrow).</summary>
    public PlayerState? LastPlayer { get; private set; }

    public event Action<MyLocationResponse>? SnapshotReceived;
    public event Action<Exception>? PollFailed;
    public event Action<MapCalibration>? CalibrationChanged;

    /// <summary>
    /// The website session is over (v1.31): the site refused the cookie
    /// RefusalsBeforeSignedOut polls in a row. Polling has stopped by the
    /// time this fires; a new sign-in (RebuildClient) starts it again.
    /// </summary>
    public event Action? SignedOut;

    /// <summary>True from SignedOut until the next RebuildClient.</summary>
    public bool IsSignedOut { get; private set; }

    /// <summary>
    /// No session to send: never signed in, signed out, or ended by the
    /// website. A Check Prime or a skin apply then answers locally (1.38's
    /// constraint review): before, the last poll's "in game" outlived a Sign
    /// out or a refused session, and the skin key or Check sent its request
    /// with no cookie or a dead one.
    /// </summary>
    private bool HasNoSession => NoSession(IsSignedOut, _client.CurrentCookie);

    /// <summary>The rule behind HasNoSession, pure for the tests.</summary>
    internal static bool NoSession(bool signedOut, string? cookie) => signedOut || string.IsNullOrWhiteSpace(cookie);

    /// <summary>Fresh heatmap PNG bytes, or null (disabled / off / fetch failed) → hide the layer.</summary>
    public event Action<byte[]?>? HeatmapChanged;

    /// <summary>
    /// The friends roster after each fetch (you filtered out), or null when
    /// no friends surface is shown or the fetch keeps failing → clear the
    /// layers. Raised after SnapshotReceived, so consumers know your own
    /// position first.
    /// </summary>
    public event Action<IReadOnlyList<FriendState>?>? FriendsChanged;

    /// <summary>The last roster delivered (null = none / cleared), so a window created mid-session can render at once.</summary>
    public IReadOnlyList<FriendState>? Friends { get; private set; }

    /// <summary>Raised right before a prime check request actually goes out (not for locally answered ones).</summary>
    public event Action? PrimeCheckStarted;

    /// <summary>Outcome of a user-triggered prime check, including the no-request cooldown / not-in-game answers.</summary>
    public event Action<PrimeCheckResult>? PrimeChecked;

    /// <summary>When the next prime check is allowed (UTC); in the past means available now.</summary>
    public DateTime PrimeCooldownUntilUtc { get; private set; }

    public PollService(OverlayConfig config)
    {
        _config = config;
        Calibration = config.Calibration ?? BundledCalibration();
        AppLog.Maps.Log(config.Calibration is not null ? "Calibration: the saved copy until the website's answer"
                        : Calibration is not null ? "Calibration: the bundled seed until the website's answer"
                        : "Calibration: none until the website's answer");
        // The server-reported cooldown end survives restarts; configs written
        // before it existed fall back to "last check + default". A value far
        // in the future can only be a clock jump — ignore it rather than lock
        // the check out.
        var until = config.PrimeCooldownUntilUtc
                    ?? (config.Prime is { } lastPrime ? lastPrime.CheckedAtUtc + DefaultPrimeCooldown : default);
        if (until <= DateTime.UtcNow + TimeSpan.FromHours(1)) PrimeCooldownUntilUtc = until;
        _client = new PandoraClient(config.GetCookie(), config.UserAgent);

        _activeInterval = TimeSpan.FromSeconds(Math.Max(2, config.PollIntervalSeconds));
        _timer = new DispatcherTimer { Interval = _activeInterval };
        _timer.Tick += async (_, _) => await PollOnceAsync("timer");

        _heatmapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(HeatmapIntervalSeconds)
        };
        _heatmapTimer.Tick += async (_, _) => await RefreshHeatmapAsync("timer");
    }

    /// <summary>
    /// Starts the timers and polls — for launch and a new session
    /// (RebuildClient) only; an on-demand poll goes through PollNowAsync.
    /// The timer is re-armed on the configured interval (the poll's
    /// ApplyPacing sets the idle cadence again where due), so its next tick
    /// can't land right behind this poll, and a new session never waits out
    /// an idle minute. The poll goes out at once unless the last one (the old
    /// session's) is under one interval old; then the first tick is the new
    /// session's first poll. (Before Oct 2026 a sign-in from Settings while
    /// polling polled at once on the running timer, and two polls could land
    /// under a second apart.)
    /// </summary>
    public void Start()
    {
        if (!_heatmapTimer.IsEnabled) _heatmapTimer.Start();
        _timer.Stop();
        _timer.Interval = _activeInterval;
        _timer.Start();
        if (PollIsStale) _ = PollOnceAsync("launch or new session");
        _ = FetchCalibrationIfDueAsync();
    }

    public void Stop()
    {
        _timer.Stop();
        _heatmapTimer.Stop();
    }

    /// <summary>Hot-swaps credentials after the settings dialog saves — no restart.</summary>
    public void RebuildClient()
    {
        _client.Dispose();
        _client = new PandoraClient(_config.GetCookie(), _config.UserAgent);
        ResetCalibrationFetch();       // ask again with the fresh session
        _failStreak = 0;               // a fresh session starts the idle clock over
        _refusals = 0;
        _answered = false;
        IsSignedOut = false;
        _lastLiveUtc = DateTime.UtcNow;
        ForgetSkins();                 // another login may see other skins
        ForgetStorage();               // and has another storage
        Start();
    }

    /// <param name="why">What asked for the poll ("timer", "edit mode", "Check Prime" …), for LogPoll's Verbose line: a pacing check reads the reason instead of working it out from the timing.</param>
    public async Task PollOnceAsync(string why)
    {
        if (_busy)
        {
            AppLog.Poll.VeryVerbose($"No poll for {why}: one is in flight");
            return;
        }
        _busy = true;
        _lastPollUtc = DateTime.UtcNow;
        AppLog.Poll.Verbose($"Poll: {why}");
        try
        {
            var result = await _client.FetchAsync();
            var wasInGame = _inGame;
            var failedBefore = _failStreak;
            _inGame = result.InGame && result.Player is not null;
            LastPlayer = _inGame ? result.Player : null;
            _dino = result.Player?.Dino;
            _steamId = result.Player?.SteamId ?? _steamId;
            _failStreak = 0;
            _refusals = 0;
            LogAnswer(wasInGame, failedBefore);
            ApplyPacing(); // before the event, so subscribers read the cadence this snapshot set
            SnapshotReceived?.Invoke(result);
            await MaybeFetchFriendsAsync();
            await FetchCalibrationIfDueAsync(); // a failed launch-time fetch retries once the connection is known to work
        }
        catch (Exception ex)
        {
            _failStreak++;
            _refusals = ex is SessionEndedException ? _refusals + 1 : 0;
            LogFailure(ex);
            ApplyPacing();
            PollFailed?.Invoke(ex);
            if (SignedOutAfter(_refusals) && !IsSignedOut)
            {
                IsSignedOut = true;
                Stop(); // final for this cookie: the next sign-in restarts the timers
                AppLog.Poll.Warning($"The website refused the session {_refusals} times in a row: polling stopped until a new sign-in");
                SignedOut?.Invoke();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The sign-out rule, pure for the tests: a run of refusals, not one.</summary>
    internal static bool SignedOutAfter(int refusals) => refusals >= RefusalsBeforeSignedOut;

    /// <summary>LogPoll for an answer: the session's first, the first after failures, and every change between in game and not.</summary>
    private void LogAnswer(bool wasInGame, int failedBefore)
    {
        if (!_answered)
        {
            _answered = true;
            AppLog.Poll.Log(_inGame ? "Connected, in game" : "Connected, not in game");
            return;
        }
        if (failedBefore > 0) AppLog.Poll.Log($"Connected again after {failedBefore} failed poll{(failedBefore == 1 ? "" : "s")}");
        if (_inGame != wasInGame) AppLog.Poll.Log(_inGame ? "In game" : "Not in game");
    }

    /// <summary>
    /// LogPoll for a failure: the first of a run, or a new cause, as a Warning;
    /// the same cause again as Verbose. A site that is down for an hour is then
    /// one line, not one a minute (LogNet still has each request).
    /// </summary>
    private void LogFailure(Exception ex)
    {
        var cause = AppLog.Short(ex);
        if (_failStreak == 1 || cause != _lastFailure) AppLog.Poll.Warning($"Poll failed: {cause}");
        else AppLog.Poll.Verbose($"Poll failed ({_failStreak} in a row): {cause}");
        _lastFailure = cause;
    }

    // ---- Account (v1.31) ---------------------------------------------------------

    /// <summary>One "who am I" for the Account page's first look per dialog — never on a timer.</summary>
    public Task<AccountInfo> FetchAccountAsync() => _client.FetchAccountAsync();

    /// <summary>
    /// The Account card's avatar picture (Oct 8 2026): the skin tiles' own path —
    /// the bare picture client (no cookie, no site headers, a size cap), one
    /// download per address at a time, a failure remembered for the session
    /// so a page reopened offline asks nothing. The page saves a small copy
    /// and asks again only when that is gone; never on a timer.
    /// </summary>
    public Task<SkinPicture> GetAvatarAsync(Uri address) => GetSkinPictureAsync(address);

    /// <summary>
    /// After a sign-out: a client with no session, the timers stopped, the
    /// roster and the skins forgotten — nothing of the old account stays, and
    /// CurrentCookie is empty so the exit save keeps nothing either.
    /// </summary>
    public void ForgetSession()
    {
        Stop();
        _client.Dispose();
        _client = new PandoraClient("", _config.UserAgent);
        _failStreak = 0;
        _refusals = 0;
        _answered = false;
        IsSignedOut = false;
        LastPlayer = null;
        _inGame = false; // nobody is in game for a client with no session (the skin key read the old "in game")
        _dino = null;
        ForgetSkins();
        ForgetStorage();
        ClearFriends("signed out");
    }

    /// <summary>
    /// A Sign out click: polling stops FIRST (the cookie is about to die, and
    /// a dead cookie's refusals must not read as "session ended"), then the
    /// site is told once. Fail soft — the caller forgets the session either
    /// way. True when the site took the logout.
    /// </summary>
    public async Task<bool> SignOutAsync()
    {
        Stop();
        try
        {
            var took = await _client.SignOutAsync();
            AppLog.Account.Log(took ? "The website took the sign-out" : "The website did not confirm the sign-out");
            return took;
        }
        catch (Exception ex)
        {
            AppLog.Account.Warning($"Sign-out request failed: {AppLog.Short(ex)}");
            return false;
        }
    }

    // ---- Friends ---------------------------------------------------------------

    /// <summary>A friends surface is on screen: the Activity widget with friends' events included, the minimap with its friend arrows, or the open big map with its Friends layer (PollService.Maps.cs).</summary>
    private bool FriendsWanted => FriendsWantedFor(_config, BigMapOpen);

    /// <summary>The cadence rule, pure for the tests: every idle poll, else every Nth — and never within one poll interval of the last roster.</summary>
    internal static bool FriendsDue(int pollsSinceFriends, bool idling, bool hotTrigger, TimeSpan sinceLastRoster, TimeSpan interval) =>
        sinceLastRoster >= interval && (hotTrigger || idling || pollsSinceFriends >= FriendsEveryNthPoll);

    /// <summary>Runs after each successful poll, inside the busy guard: fetches the roster when it is due.</summary>
    private async Task MaybeFetchFriendsAsync()
    {
        if (!FriendsWanted)
        {
            ClearFriends("no friends surface on screen");
            return;
        }
        _pollsSinceFriends++;
        // Too soon after a hot fetch: the flag, if set, waits for the next poll.
        if (!FriendsDue(_pollsSinceFriends, IsIdling, _friendsDue, DateTime.UtcNow - _lastFriendsUtc, _activeInterval)) return;
        await FetchFriendsAsync(_friendsDue ? $"{string.Join(", ", _friendsDueFor)}; held for this poll"
                                : IsIdling ? "idle poll"
                                : $"every {FriendsEveryNthPoll} in-game polls");
    }

    /// <summary>
    /// The hot-apply path for a surface that APPEARED (widget shown, the map
    /// opened or its Friends layer ticked, settings saved): fetch now if the
    /// last roster is older than one poll interval, else mark it due for the
    /// next poll — showing and hiding the widget in a hurry must not turn into
    /// a request per click. Off → clears at once. A surface that went away
    /// takes <see cref="FriendsSurfaceHidden"/> instead.
    /// </summary>
    /// <param name="why">What asked ("map opened", "Activity widget shown" …), for LogFriends.</param>
    public async Task RefreshFriendsAsync(string why)
    {
        if (!FriendsWanted)
        {
            ClearFriends($"no friends surface on screen ({why})");
            return;
        }
        _friendsDue = true;
        if (!_friendsDueFor.Contains(why)) _friendsDueFor.Add(why);
        // Not yet polling (startup, no cookie), mid-poll, too soon, or the
        // connection is failing: the next successful poll picks the flag up.
        if (!_timer.IsEnabled || _busy || DateTime.UtcNow - _lastFriendsUtc < _activeInterval || _failStreak > 0)
        {
            var wait = !_timer.IsEnabled ? "polling is stopped"
                     : _busy ? "a request is in flight"
                     : _failStreak > 0 ? "the last poll failed"
                     : "the last roster is under one interval old";
            AppLog.Friends.VeryVerbose($"No roster now for {why}: {wait}; held for the next poll");
            return;
        }
        // The one friends fetch that does not ride a poll. It takes the poll's
        // busy guard, so a tick landing meanwhile is skipped, not stacked on it.
        _busy = true;
        try
        {
            await FetchFriendsAsync(why);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// A friends surface went away (the minimap or the Activity widget hidden,
    /// the map's Friends layer unticked): the roster is cleared when nothing
    /// else wants it, and NO request is sent — a surface still on screen keeps
    /// the roster it has, and the polls keep it fresh. Before 1.36 these took
    /// RefreshFriendsAsync, which fetched at once whenever another surface
    /// remained: an extra request for nothing, outside the endpoint table's
    /// triggers (a surface shown, a Settings save). The log caught it, Oct 21 2026.
    /// </summary>
    /// <param name="why">What went away, for LogFriends.</param>
    public void FriendsSurfaceHidden(string why)
    {
        if (!FriendsWanted) ClearFriends($"no friends surface on screen ({why})");
        else AppLog.Friends.VeryVerbose($"No roster for {why}: another friends surface still shows the last one");
    }

    /// <param name="why">Why the roster is asked for now, written into its LogFriends line.</param>
    private async Task FetchFriendsAsync(string why)
    {
        _pollsSinceFriends = 0;
        _friendsDue = false;
        _friendsDueFor.Clear();
        _lastFriendsUtc = DateTime.UtcNow;
        try
        {
            var roster = await _client.FetchFriendsAsync();
            if (roster is null) throw new InvalidOperationException("server declined");
            if (_friendMisses > 0) AppLog.Friends.Log($"Roster back after {_friendMisses} miss{(_friendMisses == 1 ? "" : "es")}");
            _friendMisses = 0;
            Friends = _steamId is null ? roster : roster.Where(f => f.SteamId != _steamId).ToList();
            AppLog.Friends.Verbose($"Roster ({why}): {Friends.Count} friends, {Friends.Count(f => f.InGame)} in game");
            FriendsChanged?.Invoke(Friends);
        }
        catch (Exception ex)
        {
            // Our own InvalidOperationException above is the website's success:false.
            AppLog.Friends.Warning($"Roster missed ({why}): {(ex is InvalidOperationException ? "the website declined" : AppLog.Short(ex))}");
            // One miss keeps the last roster on screen; a run of them clears it
            // so nobody navigates by a stale arrow. The next due poll retries.
            if (++_friendMisses >= FriendMissesBeforeClear) ClearFriends($"{_friendMisses} misses in a row");
        }
    }

    /// <param name="why">For LogFriends, when a roster was on screen.</param>
    private void ClearFriends(string why)
    {
        _pollsSinceFriends = 0;
        if (Friends is null) return;
        AppLog.Friends.Log($"Roster cleared: {why}");
        Friends = null;
        FriendsChanged?.Invoke(null);
    }

    /// <summary>
    /// User activity (edit mode, un-hiding the overlay) hints that the game
    /// may be up again: while idling, poll now rather than up to a minute
    /// late. Never faster than the configured cadence, however often called.
    /// The Skins page calls it on opening too: an apply wants a fresh in-game
    /// state.
    /// </summary>
    /// <param name="why">What nudged ("edit mode", "overlay shown" …), for LogPoll.</param>
    public void Nudge(string why)
    {
        if (_timer.IsEnabled && IsIdling && PollIsStale)
        {
            _ = PollNowAsync(why);
            return;
        }
        AppLog.Poll.VeryVerbose($"No poll for {why}: {(!_timer.IsEnabled ? "polling is stopped" : !IsIdling ? "not idling" : "the last poll is under one interval old")}");
    }

    /// <summary>The pacing rule, pure for the tests: configured cadence while live, else idle, then deep idle.</summary>
    internal static TimeSpan NextInterval(TimeSpan active, bool live, TimeSpan sinceLive)
    {
        if (live) return active;
        var idle = sinceLive < DeepIdleAfter ? IdleInterval : DeepIdleInterval;
        return idle > active ? idle : active; // a slower configured cadence is never sped up
    }

    private bool PollIsStale => IsStale(_lastPollUtc, DateTime.UtcNow, _activeInterval);

    /// <summary>The floor under every off-schedule poll, pure for the tests: at least one configured interval since the last poll began.</summary>
    internal static bool IsStale(DateTime lastPollUtc, DateTime nowUtc, TimeSpan interval) => nowUtc - lastPollUtc >= interval;

    private void ApplyPacing()
    {
        var now = DateTime.UtcNow;
        var live = _inGame && _failStreak < FailuresBeforeIdle;
        if (live) _lastLiveUtc = now;

        var next = NextInterval(_activeInterval, live, now - _lastLiveUtc);
        // Assigning Interval re-arms a running DispatcherTimer from now, so
        // only touch it on a change — the in-game tick rhythm stays as it was.
        if (_timer.Interval != next)
        {
            AppLog.Poll.Log($"Polling every {next.TotalSeconds:0} s (was {_timer.Interval.TotalSeconds:0} s)");
            _timer.Interval = next;
        }
    }

    /// <summary>An off-schedule poll: re-arms the timer first, so the next tick can't land right behind it.</summary>
    private Task PollNowAsync(string why)
    {
        // Every caller checks PollIsStale first (Nudge, the Check Prime and
        // skin-apply clicks), so an off-schedule poll is floored at the
        // configured interval: nothing polls faster, however often asked.
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _timer.Start();
        }
        return PollOnceAsync(why);
    }

    /// <summary>
    /// One USER-TRIGGERED prime check (control panel / hotkey) — never call this
    /// from a timer; the endpoint is approved on exactly that condition. All
    /// gating lives here so windows stay pure consumers: no session, a
    /// running cooldown or a not-in-game poll state answers locally with no
    /// prime request sent (a stale not-in-game state is refreshed by one
    /// regular poll first — the idle cadence may not have seen a fresh spawn
    /// yet; with no session not even that poll goes out). A
    /// successful snapshot is cached into config (persisted with the next
    /// Save), and the server is then asked once for this account's actual
    /// cooldown — its length varies with supporter rank.
    /// </summary>
    public async Task CheckPrimeAsync()
    {
        if (_primeBusy) return;

        if (HasNoSession)
        {
            AppLog.Prime.Log("Check answered here: not signed in");
            PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.Failed, Reason: "not signed in"));
            return;
        }

        var now = DateTime.UtcNow;
        if (now < PrimeCooldownUntilUtc)
        {
            AppLog.Prime.Log($"Check answered here: cooling down, {(PrimeCooldownUntilUtc - now).TotalSeconds:0} s left");
            PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.Cooldown, Remaining: PrimeCooldownUntilUtc - now));
            return;
        }

        _primeBusy = true;
        try
        {
            // Idle pacing can trail a fresh spawn by up to a minute: refresh
            // the in-game state with one regular poll before answering "not
            // in game" from a stale one. Still no prime request unless spawned.
            if (!_inGame && PollIsStale) await PollNowAsync("Check Prime, to refresh a not-in-game state");
            if (!_inGame)
            {
                AppLog.Prime.Log("Check answered here: not in game");
                PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.NotInGame));
                return;
            }

            // config.Prime still holds the previous result here — consumers
            // snapshot it to diff against — and is replaced before PrimeChecked.
            PrimeCheckStarted?.Invoke();
            var result = await _client.CheckPrimeAsync(_dino);
            switch (result.Outcome)
            {
                case PrimeCheckOutcome.Ok:
                    _config.Prime = result.Snapshot;
                    // Ask before announcing the result, so the widget's
                    // countdown is right from its first tick.
                    SetPrimeCooldown(await FetchPrimeCooldownOrDefaultAsync());
                    break;
                case PrimeCheckOutcome.Cooldown:
                    SetPrimeCooldown(result.Remaining);
                    break;
                case PrimeCheckOutcome.Failed:
                    PrimeCooldownUntilUtc = DateTime.UtcNow + PrimeRetryGuard;
                    break;
            }
            // Reason is our own mapped words ("server declined"), never the website's.
            AppLog.Prime.Log($"Check answered by the website: {result.Outcome}{(result.Reason is { } why ? $" ({why})" : "")}; next check in {(PrimeCooldownUntilUtc - DateTime.UtcNow).TotalSeconds:0} s");
            PrimeChecked?.Invoke(result);
        }
        catch (Exception ex)
        {
            AppLog.Prime.Warning($"Check failed: {AppLog.Short(ex)}");
            PrimeCooldownUntilUtc = DateTime.UtcNow + PrimeRetryGuard;
            // The type's name only, never the message: the reason reaches the
            // widget, and error paths surface nothing that could carry request details.
            PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.Failed, Reason: ex.GetType().Name));
        }
        finally
        {
            _primeBusy = false;
        }
    }

    /// <summary>The server's word on the cooldown (remembered across restarts), never shorter than the mash guard.</summary>
    private void SetPrimeCooldown(TimeSpan remaining)
    {
        if (remaining < PrimeRetryGuard) remaining = PrimeRetryGuard;
        PrimeCooldownUntilUtc = DateTime.UtcNow + remaining;
        _config.PrimeCooldownUntilUtc = PrimeCooldownUntilUtc; // persisted with the next Save()
    }

    /// <summary>One request, right after a successful check; any failure falls back to the default length.</summary>
    private async Task<TimeSpan> FetchPrimeCooldownOrDefaultAsync()
    {
        try
        {
            if (await _client.FetchPrimeCooldownAsync() is { } cooldown) return cooldown;
            AppLog.Prime.Log("No cooldown in the website's answer: the default 5 min");
            return DefaultPrimeCooldown;
        }
        catch (Exception ex)
        {
            AppLog.Prime.Warning($"Cooldown request failed: {AppLog.Short(ex)}; the default 5 min");
            return DefaultPrimeCooldown;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _heatmapTimer.Stop();
        _client.Dispose();
    }
}
