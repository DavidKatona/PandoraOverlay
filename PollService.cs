using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// Owns the HTTP client and the poll timer so every window consumes ONE shared
/// request stream — adding views (stats panel, minimap, …) never adds requests
/// (hard constraint: a single mylocation poll every >= 2 s). A second, slower
/// timer refetches the minimap's heatmap layer while it is enabled. Runs
/// entirely on the UI thread via DispatcherTimer, so subscribers may touch UI
/// directly.
/// </summary>
public sealed class PollService : IDisposable
{
    /// <summary>
    /// Seconds between heatmap refetches while the layer is on — the live-map
    /// page itself refetches every 10 s per open tab, so this stays well
    /// under the site's own footprint. Deliberately not configurable.
    /// </summary>
    private const int HeatmapIntervalSeconds = 60;

    /// <summary>
    /// Client-side mirror of the server's prime-check cooldown (the live-map
    /// page locks its button for the same 5 min), so a cooling-down check
    /// costs no request at all.
    /// </summary>
    private static readonly TimeSpan PrimeCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Breather after a failed check, so a broken endpoint can't be button-mashed.</summary>
    private static readonly TimeSpan PrimeRetryGuard = TimeSpan.FromSeconds(15);

    private readonly OverlayConfig _config;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _heatmapTimer;
    private PandoraClient _client;
    private bool _busy;
    private bool _heatmapBusy;
    private bool _primeBusy;
    private bool _calibrationRequested;
    private bool _inGame;
    private string? _dino;

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
        if (config.Prime is { } lastPrime) PrimeCooldownUntilUtc = lastPrime.CheckedAtUtc + PrimeCooldown;
        _client = new PandoraClient(config.GetCookie(), config.UserAgent);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(2, config.PollIntervalSeconds))
        };
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
        Start();
    }

    public async Task PollOnceAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var result = await _client.FetchAsync();
            _inGame = result.InGame && result.Player is not null;
            _dino = result.Player?.Dino;
            SnapshotReceived?.Invoke(result);
        }
        catch (Exception ex)
        {
            PollFailed?.Invoke(ex);
        }
        finally
        {
            _busy = false;
        }
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
    /// One USER-TRIGGERED prime check (control panel / tray) — never call this
    /// from a timer; the endpoint is approved on exactly that condition. All
    /// gating lives here so windows stay pure consumers: a running cooldown
    /// or a not-in-game poll state answers locally with no request sent. A
    /// successful snapshot is cached into config (persisted with the next
    /// Save).
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
        if (!_inGame)
        {
            PrimeChecked?.Invoke(new PrimeCheckResult(PrimeCheckOutcome.NotInGame));
            return;
        }

        _primeBusy = true;
        PrimeCheckStarted?.Invoke();
        try
        {
            var result = await _client.CheckPrimeAsync(_dino);
            switch (result.Outcome)
            {
                case PrimeCheckOutcome.Ok:
                    PrimeCooldownUntilUtc = DateTime.UtcNow + PrimeCooldown;
                    _config.Prime = result.Snapshot;
                    break;
                case PrimeCheckOutcome.Cooldown:
                    PrimeCooldownUntilUtc = DateTime.UtcNow + result.Remaining;
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
