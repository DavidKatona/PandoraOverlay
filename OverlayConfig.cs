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

    // ---- Controls ---------------------------------------------------------

    /// <summary>
    /// Global edit-mode hotkey: modifiers (Ctrl/Alt/Shift/Win) plus one key,
    /// separated by "+", e.g. "Ctrl+F8" or "Ctrl+Shift+M". Editable in the
    /// settings window; unparseable values fall back to Ctrl+F8.
    /// </summary>
    public string Hotkey { get; set; } = "Ctrl+F8";

    /// <summary>
    /// Hide/show the whole overlay (screenshots, cutscenes) — same format as
    /// Hotkey. Hidden state never persists: the app always starts visible.
    /// </summary>
    public string HotkeyHideAll { get; set; } = "Ctrl+F9";

    /// <summary>Toggle the minimap island/centered view from gameplay — same format as Hotkey.</summary>
    public string HotkeyMinimapView { get; set; } = "Ctrl+F7";

    // ---- Minimap (v1.1) ---------------------------------------------------

    /// <summary>Show the stats panel (toggled from the control panel or the tray menu).</summary>
    public bool StatsEnabled { get; set; } = true;

    /// <summary>Show the minimap window (toggled from the control panel or the tray menu).</summary>
    public bool MinimapEnabled { get; set; } = true;

    public double MinimapX { get; set; } = 300;
    public double MinimapY { get; set; } = 40;

    /// <summary>Edge length of the square minimap, in DIPs. Slider in Settings (160–400).</summary>
    public double MinimapSize { get; set; } = 230;

    /// <summary>
    /// Minimap view: "island" (whole map, the arrow moves) or "centered"
    /// (north-up, the map pans under an arrow fixed at the centre).
    /// The VIEW button on the minimap's edit banner toggles this.
    /// </summary>
    public string MinimapMode { get; set; } = "island";

    /// <summary>
    /// Centered-mode magnification: the map is rendered at MinimapSize × zoom.
    /// Clamped to 1.25–6 at runtime (the source image is 1000 px, so the top
    /// of the range upscales slightly). Mouse wheel over the minimap adjusts
    /// it while in edit mode.
    /// </summary>
    public double MinimapZoom { get; set; } = 5;

    /// <summary>
    /// Degrees added to the raw yaw before rotating the player arrow — corrects
    /// for map-image orientation. Tune here if the arrow points sideways.
    /// </summary>
    public double MinimapYawOffsetDegrees { get; set; } = 90;

    /// <summary>Cached /api/map/calibration values; refreshed once per launch.</summary>
    public MapCalibration? Calibration { get; set; }

    /// <summary>
    /// Minimap waypoint in world coordinates (cm); null = none. Right-click
    /// the minimap in edit mode to place/move it, right-click the marker to
    /// clear it. Persists across restarts.
    /// </summary>
    public double? WaypointX { get; set; }

    public double? WaypointY { get; set; }

    /// <summary>Edit-mode control panel position; null until first moved (defaults to bottom-center).</summary>
    public double? ControlPanelX { get; set; }

    public double? ControlPanelY { get; set; }

    // ---- Appearance -------------------------------------------------------

    /// <summary>
    /// Stats panel (and control panel) scale, as a layout transform. Clamped
    /// 0.75–1.5. The minimap is sized natively via MinimapSize instead.
    /// </summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>Opacity of the dark glass behind the panels (text stays crisp). Clamped 0.3–1.</summary>
    public double BackgroundOpacity { get; set; } = 0.8;

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
