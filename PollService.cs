using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// Owns the HTTP client and the poll timer so every window consumes ONE shared
/// request stream — adding views (stats panel, minimap, …) never adds requests
/// (hard constraint: a single mylocation poll every >= 2 s). Runs entirely on
/// the UI thread via DispatcherTimer, so subscribers may touch UI directly.
/// </summary>
public sealed class PollService : IDisposable
{
    private readonly OverlayConfig _config;
    private readonly DispatcherTimer _timer;
    private PandoraClient _client;
    private bool _busy;
    private bool _calibrationRequested;

    /// <summary>Latest cookie, including any rolled connect.sid (persist on exit).</summary>
    public string CurrentCookie => _client.CurrentCookie;

    /// <summary>Last known map calibration: fetched this launch, else the config-cached copy.</summary>
    public MapCalibration? Calibration { get; private set; }

    public event Action<MyLocationResponse>? SnapshotReceived;
    public event Action<Exception>? PollFailed;
    public event Action<MapCalibration>? CalibrationChanged;

    public PollService(OverlayConfig config)
    {
        _config = config;
        Calibration = config.Calibration;
        _client = new PandoraClient(config.GetCookie(), config.UserAgent);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(2, config.PollIntervalSeconds))
        };
        _timer.Tick += async (_, _) => await PollOnceAsync();
    }

    public void Start()
    {
        if (!_timer.IsEnabled) _timer.Start();
        _ = PollOnceAsync();
        _ = FetchCalibrationOnceAsync();
    }

    public void Stop() => _timer.Stop();

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
        _client.Dispose();
    }
}
