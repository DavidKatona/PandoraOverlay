using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// Owns the HTTP client and the poll timer so every window consumes ONE shared
/// request stream — adding views (stats panel, minimap, …) never adds requests
/// (hard constraint: a single mylocation poll every >= 2 s). The cadence is
/// adaptive, and only ever downwards: the configured interval applies while
/// in-game, and the poll idles while nobody is spawned (menus, restarts, the
/// game closed) or the connection keeps failing. A second, slower timer
/// refetches the minimap's heatmap layer while it is enabled. Runs entirely
/// on the UI thread via DispatcherTimer, so subscribers may touch UI directly.
/// </summary>
public sealed class PollService : IDisposable
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

    private readonly OverlayConfig _config;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _heatmapTimer;
    private readonly TimeSpan _activeInterval;
    private PandoraClient _client;
    private bool _busy;
    private bool _heatmapBusy;
    private bool _primeBusy;
    private bool _calibrationRequested;
    private bool _inGame;
    private string? _dino;
    private int _failStreak;
    private DateTime _lastPollUtc;
    private DateTime _lastLiveUtc = DateTime.UtcNow;

    /// <summary>Current gap between polls: the configured one in-game, longer while idling.</summary>
    public TimeSpan Interval => _timer.Interval;

    /// <summary>True while the poll runs slower than configured (not in-game, or persistently failing).</summary>
    public bool IsIdling => _timer.Interval > _activeInterval;

    /// <summary>Latest cookie, including any rolled connect.sid (persist on exit).</summary>
    public string CurrentCookie => _client.CurrentCookie;

    /// <summary>Last known map calibration: fetched this launch, else the config-cached copy.</summary>
    public MapCalibration? Calibration { get; private set; }

    public event Action<MyLocationResponse>? SnapshotReceived;
    public event Action<Exception>? PollFailed;
    public event Action<MapCalibration>? CalibrationChanged;

    /// <summary>Fresh heatmap PNG bytes, or null (disabled / off / fetch failed) → hide the layer.</summary>
    public event Action<byte[]?>? HeatmapChanged;

    /// <summary>Raised right before a prime check request actually goes out (not for locally answered ones).</summary>
    public event Action? PrimeCheckStarted;

    /// <summary>Outcome of a user-triggered prime check, including the no-request cooldown / not-in-game answers.</summary>
    public event Action<PrimeCheckResult>? PrimeChecked;

    /// <summary>When the next prime check is allowed (UTC); in the past means available now.</summary>
    public DateTime PrimeCooldownUntilUtc { get; private set; }

    public PollService(OverlayConfig config)
    {
        _config = config;
        Calibration = config.Calibration;
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
        _timer.Tick += async (_, _) => await PollOnceAsync();

        _heatmapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(HeatmapIntervalSeconds)
        };
        _heatmapTimer.Tick += async (_, _) => await RefreshHeatmapAsync();
    }

    public void Start()
    {
        if (!_timer.IsEnabled) _timer.Start();
        if (!_heatmapTimer.IsEnabled) _heatmapTimer.Start();
        _ = PollOnceAsync();
        _ = FetchCalibrationOnceAsync();
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
        _calibrationRequested = false; // retry with the fresh session
        _failStreak = 0;               // a fresh session starts the idle clock over
        _lastLiveUtc = DateTime.UtcNow;
        Start();
    }

    public async Task PollOnceAsync()
    {
        if (_busy) return;
        _busy = true;
        _lastPollUtc = DateTime.UtcNow;
        try
        {
            var result = await _client.FetchAsync();
            _inGame = result.InGame && result.Player is not null;
            _dino = result.Player?.Dino;
            _failStreak = 0;
            ApplyPacing(); // before the event, so subscribers read the cadence this snapshot set
            SnapshotReceived?.Invoke(result);
        }
        catch (Exception ex)
        {
            _failStreak++;
            ApplyPacing();
            PollFailed?.Invoke(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// User activity (edit mode, un-hiding the overlay) hints that the game
    /// may be up again: while idling, poll now rather than up to a minute
    /// late. Never faster than the configured cadence, however often called.
    /// </summary>
    public void Nudge()
    {
        if (_timer.IsEnabled && IsIdling && PollIsStale) _ = PollNowAsync();
    }

    /// <summary>The pacing rule, pure for the tests: configured cadence while live, else idle, then deep idle.</summary>
    internal static TimeSpan NextInterval(TimeSpan active, bool live, TimeSpan sinceLive)
    {
        if (live) return active;
        var idle = sinceLive < DeepIdleAfter ? IdleInterval : DeepIdleInterval;
        return idle > active ? idle : active; // a slower configured cadence is never sped up
    }

    private bool PollIsStale => DateTime.UtcNow - _lastPollUtc >= _activeInterval;

    private void ApplyPacing()
    {
        var now = DateTime.UtcNow;
        var live = _inGame && _failStreak < FailuresBeforeIdle;
        if (live) _lastLiveUtc = now;

        var next = NextInterval(_activeInterval, live, now - _lastLiveUtc);
        // Assigning Interval re-arms a running DispatcherTimer from now, so
        // only touch it on a change — the in-game tick rhythm stays as it was.
        if (_timer.Interval != next) _timer.Interval = next;
    }

    /// <summary>An off-schedule poll: re-arms the timer first, so the next tick can't land right behind it.</summary>
    private Task PollNowAsync()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _timer.Start();
        }
        return PollOnceAsync();
    }

    /// <summary>
    /// One heatmap fetch — the slow timer and the hot-apply paths (settings
    /// save, minimap re-show) all land here. Gated on the config toggles, so
    /// a disabled layer costs zero requests; raises null (hide the layer)
    /// when off, server-disabled, or the fetch fails — the next tick retries.
    /// </summary>
    public async Task RefreshHeatmapAsync()
    {
        if (_heatmapBusy) return;
        if (!_config.HeatmapEnabled || !_config.MinimapEnabled)
        {
            HeatmapChanged?.Invoke(null);
            return;
        }
        _heatmapBusy = true;
        try
        {
            HeatmapChanged?.Invoke(await _client.FetchHeatmapAsync());
        }
        catch
        {
            HeatmapChanged?.Invoke(null);
        }
        finally
        {
            _heatmapBusy = false;
        }
    }

    /// <summary>
    /// One USER-TRIGGERED prime check (control panel / hotkey) — never call this
    /// from a timer; the endpoint is approved on exactly that condition. All
    /// gating lives here so windows stay pure consumers: a running cooldown
    /// or a not-in-game poll state answers locally with no prime request
    /// sent (a stale not-in-game state is refreshed by one regular poll
    /// first — the idle cadence may not have seen a fresh spawn yet). A
    /// successful snapshot is cached into config (persisted with the next
    /// Save), and the server is then asked once for this account's actual
    /// cooldown — its length varies with supporter rank.
    /// </summary>
    public async Task CheckPrimeAsync()
    {
        if (_primeBusy) return;

        var now = DateTime.UtcNow;
        if (now < PrimeCooldownUntilUtc)
        {
            PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.Cooldown, Remaining: PrimeCooldownUntilUtc - now));
            return;
        }

        _primeBusy = true;
        try
        {
            // Idle pacing can trail a fresh spawn by up to a minute: refresh
            // the in-game state with one regular poll before answering "not
            // in game" from a stale one. Still no prime request unless spawned.
            if (!_inGame && PollIsStale) await PollNowAsync();
            if (!_inGame)
            {
                PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.NotInGame));
                return;
            }

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
            PrimeChecked?.Invoke(result);
        }
        catch (Exception ex)
        {
            PrimeCooldownUntilUtc = DateTime.UtcNow + PrimeRetryGuard;
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
            return await _client.FetchPrimeCooldownAsync() ?? DefaultPrimeCooldown;
        }
        catch
        {
            return DefaultPrimeCooldown;
        }
    }

    /// <summary>
    /// One calibration fetch per launch (or per credential swap) — it is static
    /// site config. Failure is soft: cached values (if any) stay in effect.
    /// </summary>
    private async Task FetchCalibrationOnceAsync()
    {
        if (_calibrationRequested) return;
        _calibrationRequested = true;
        try
        {
            if (await _client.FetchCalibrationAsync() is { } fresh && fresh != Calibration)
            {
                Calibration = fresh;
                _config.Calibration = fresh; // persisted with the next Save()
                CalibrationChanged?.Invoke(fresh);
            }
        }
        catch
        {
            // Keep cached/null calibration; the minimap shows its waiting state.
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _heatmapTimer.Stop();
        _client.Dispose();
    }
}
