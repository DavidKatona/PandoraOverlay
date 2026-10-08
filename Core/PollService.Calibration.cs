using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// The map calibration (Oct 8 2026). Static site config, fetched once per
/// launch (and per credential swap) — but that one fetch used to be the only
/// one: a failure at launch (Start with Windows before the network is up, a
/// blip) left a fresh install's minimap "waiting for map calibration" for the
/// whole session, while the heatmap beside it recovered on its own timer.
/// Now (1) a dated seed (Assets/calibration.json) stands in until the first
/// successful fetch, so the arrow is drawn at once, and (2) a failed fetch is
/// retried right after a successful mylocation poll — never on a timer of
/// its own, and at most once per CalibrationRetryAfter — until one succeeds.
/// </summary>
public sealed partial class PollService
{
    /// <summary>Floor between calibration attempts while retrying: a broken endpoint must not ride every poll.</summary>
    private static readonly TimeSpan CalibrationRetryAfter = TimeSpan.FromSeconds(60);

    private const string CalibrationSeedResource = "calibration.json";

    private bool _calibrationFetched; // the site's answer arrived this launch / for this session
    private bool _calibrationBusy;
    private DateTime _lastCalibrationAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// The retry rule, pure for the tests: only until the site's answer has
    /// arrived, never while one is in flight, and not again within the floor.
    /// </summary>
    internal static bool CalibrationDue(bool fetched, bool inFlight, DateTime lastAttemptUtc, DateTime nowUtc) =>
        !fetched && !inFlight && nowUtc - lastAttemptUtc >= CalibrationRetryAfter;

    /// <summary>The bundled seed, read through the same parser as the live answer; null if it is missing or unusable.</summary>
    internal static MapCalibration? BundledCalibration()
    {
        try
        {
            using var stream = typeof(PollService).Assembly.GetManifestResourceStream(CalibrationSeedResource);
            if (stream is null) return null;
            using var doc = JsonDocument.Parse(stream);
            return PandoraClient.FindCalibration(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the calibration if it is due. Called by Start (launch, new
    /// session) and after every successful poll; the gate makes all but the
    /// first a no-op once the answer is in. Failure is soft: the cached or
    /// seed values stay in effect.
    /// </summary>
    private async Task FetchCalibrationIfDueAsync()
    {
        var now = DateTime.UtcNow;
        if (!CalibrationDue(_calibrationFetched, _calibrationBusy, _lastCalibrationAttemptUtc, now)) return;
        _calibrationBusy = true;
        _lastCalibrationAttemptUtc = now;
        try
        {
            if (await _client.FetchCalibrationAsync() is { } fresh)
            {
                _calibrationFetched = true;
                if (fresh != Calibration)
                {
                    Calibration = fresh;
                    _config.Calibration = fresh; // persisted with the next Save()
                    CalibrationChanged?.Invoke(fresh);
                }
            }
        }
        catch
        {
            // Keep the cached / seed calibration; the next successful poll retries.
        }
        finally
        {
            _calibrationBusy = false;
        }
    }

    /// <summary>Forget that this session has the site's answer, so a new session asks again (RebuildClient).</summary>
    private void ResetCalibrationFetch()
    {
        _calibrationFetched = false;
        _lastCalibrationAttemptUtc = DateTime.MinValue;
    }
}
