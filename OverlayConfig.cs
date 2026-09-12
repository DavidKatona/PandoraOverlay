using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PandoraOverlay;

/// <summary>
/// Persistent settings, stored as config.json next to the executable.
///
/// The session cookie is never stored in plaintext. The "Cookie" field is a
/// paste-here inbox: on the next launch its value is encrypted with Windows
/// DPAPI (scoped to the current Windows user), moved into "CookieProtected",
/// and the plaintext field is blanked. Only your Windows account on this
/// machine can decrypt the blob.
/// </summary>
public sealed class OverlayConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // App-specific additional entropy for DPAPI. This is not a secret (it is
    // in the source); it just prevents other DPAPI-using apps from decrypting
    // the blob by accident. The real protection is the CurrentUser scope.
    private static readonly byte[] Entropy =
        { 0x50, 0x61, 0x6E, 0x64, 0x6F, 0x72, 0x61, 0x4F, 0x76, 0x65, 0x72, 0x6C, 0x61, 0x79, 0x2E, 0x76, 0x31 };

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>
    /// PASTE-HERE FIELD ONLY. Put the full "cookie" request-header value here
    /// (must include connect.sid=... and cf_clearance=...). It is encrypted
    /// and blanked on the next launch.
    /// </summary>
    public string Cookie { get; set; } = "";

    /// <summary>DPAPI-encrypted cookie, base64. Managed by the app — do not edit.</summary>
    public string CookieProtected { get; set; } = "";

    /// <summary>
    /// Sent with every request. Keep this matching your real browser's
    /// User-Agent so the traffic looks like the browser session it belongs to.
    /// </summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36";

    /// <summary>Seconds between polls. The site itself polls every few seconds; do not go below 2.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    public double WindowX { get; set; } = 40;
    public double WindowY { get; set; } = 40;

    // ---- Minimap (v1.1) ---------------------------------------------------

    /// <summary>Show the minimap window (✕ on its edit banner hides it; MAP on the stats panel brings it back).</summary>
    public bool MinimapEnabled { get; set; } = true;

    public double MinimapX { get; set; } = 300;
    public double MinimapY { get; set; } = 40;

    /// <summary>Edge length of the square minimap, in DIPs.</summary>
    public double MinimapSize { get; set; } = 230;

    /// <summary>
    /// Degrees added to the raw yaw before rotating the player arrow — corrects
    /// for map-image orientation. Tune here if the arrow points sideways.
    /// </summary>
    public double MinimapYawOffsetDegrees { get; set; } = 90;

    /// <summary>Cached /api/map/calibration values; refreshed once per launch.</summary>
    public MapCalibration? Calibration { get; set; }

    public static OverlayConfig Load()
    {
        OverlayConfig cfg;
        try
        {
            cfg = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<OverlayConfig>(File.ReadAllText(FilePath)) ?? new OverlayConfig()
                : new OverlayConfig();
        }
        catch
        {
            cfg = new OverlayConfig(); // corrupt config: rewrite defaults
        }

        // Migrate a freshly pasted plaintext cookie into the DPAPI blob.
        if (!string.IsNullOrWhiteSpace(cfg.Cookie))
        {
            cfg.SetCookie(cfg.Cookie.Trim());
            cfg.Cookie = "";
        }

        cfg.Save();
        return cfg;
    }

    /// <summary>Decrypts and returns the stored cookie, or "" if none/undecryptable.</summary>
    public string GetCookie()
    {
        if (string.IsNullOrWhiteSpace(CookieProtected)) return "";
        try
        {
            var blob = Convert.FromBase64String(CookieProtected);
            var plain = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Wrong user/machine, or corrupted blob — treat as "no cookie".
            return "";
        }
    }

    /// <summary>Encrypts and stores the cookie (call Save() afterwards to persist).</summary>
    public void SetCookie(string cookie)
    {
        try
        {
            var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(cookie), Entropy, DataProtectionScope.CurrentUser);
            CookieProtected = Convert.ToBase64String(blob);
        }
        catch
        {
            // DPAPI failure is exotic; keep the previous blob rather than crash.
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // Non-fatal: losing window position / rolled cookie is acceptable.
        }
    }
}
