# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server). Shows the
player's own dino stats in an always-on-top panel, plus a minimap, tray icon,
and settings window. **v1.12.0 is built, working, and approved by the server's
web dev.**

## Hard constraints (never violate)

1. **Fully external, always.** Never read game memory, inject, hook, enumerate or
   touch the game process in any way. The Isle runs Easy Anti-Cheat. The ONLY data
   source is the islapandora.eu web API. If a feature seems to need game-side data,
   the answer is no.
2. **Approved endpoints only.** `POST /api/map/mylocation` (the poll),
   `POST /api/map/calibration` (once per launch — static map-transform constants
   for the approved minimap; the live-map page itself loads it on every visit;
   added at the owner's direction, Sep 2026), and the heatmap pair
   `GET /map/api/heatmap-status` + `GET /map/heatmap-live.png` (approved by
   the site dev Sep 15 2026; public and fetched WITHOUT a cookie, every 60 s
   and only while the heatmap layer is on and the minimap shown — the site's
   own page refetches every 10 s per open tab). The `friends` endpoint and
   the zone overlays are NOT cleared for use (see Permissions). The
   launch-time update check calls the GitHub releases API — not an
   islapandora endpoint, so it sits outside this constraint.
3. **Poll interval >= 2 s** (default 3 s, matching the website's own cadence).
   Server-side rate limit is 300/window. Never add endpoints or frequency without
   the owner's explicit okay — the dev specifically praised the polling restraint.
4. **The cookie is a credential.** Never log it, print it, put it in exceptions,
   window text, or commit it. Error paths surface exception *type* only.
5. `config.json` is runtime state (holds the DPAPI blob) — stays in `.gitignore`.

## Permissions status (from Discord ticket, Sep 2026)

- Approved by **instantnameofficial** (site dev, via admin Emilyana): use of the
  overlay, explicitly including a future **minimap** from the same endpoint.
  Distributing the app / public source is fine — it's a simple tracker and the
  server team has no problem with such tools.
- Approved Sep 15 2026 (same dev): the **heatmap layer** — mirroring the
  live-map page's pre-rendered `/map/heatmap-live.png` (+ its status
  endpoint) on the minimap.
- **NOT approved:** using the `friends` endpoint or the zone overlay images
  (the live-map bundles patrols / sanctuaries / migrations / salt rocks as
  static PNGs) — ask him first. A read-only API
  token feature was pitched to him; if it ships, auth migration happens inside
  `OverlayConfig.GetCookie/SetCookie` + `PandoraClient` header — nothing else changes.

## API contract

`POST https://islapandora.eu/api/map/mylocation` — empty body (Content-Length: 0).
Headers: `Cookie` (needs `connect.sid=...` and usually `cf_clearance=...`),
`User-Agent` (browser-like, from config), `Origin: https://islapandora.eu`,
`Referer: https://islapandora.eu/live-map`, `Accept: */*`.

Responses (JSON):
- Offline: `{"inGame":false}`
- In-game:
  `{"inGame":true,"player":{"steamId":"7656...","name":"Dave94Punk","dino":"Deinosuchus","gender":"Male","growth":0.416,"health":0.995,"stamina":1,"hunger":0.322,"thirst":0.77,"yaw":-140.394,"headFractured":false,"bodyFractured":false,"legsFractured":false,"x":6167.09,"y":-316782.07,"z":20754.4}}`
- Stats are 0–1 floats. x/y/z are Unreal world coords (cm); yaw is facing (deg).
- Every response carries `Set-Cookie` renewing `connect.sid` (rolling ~1-month
  session). `PandoraClient.UpdateRollingCookie` splices it in; persisted on exit.
- Behind Cloudflare, but plain HttpClient passes (verified with curl) — no TLS
  impersonation or WebView2 needed. Backend is Express; auth is session cookie only.

`POST /api/map/calibration` — empty body, same headers; called once per launch.
POST-only (GET 404s); works even unauthenticated. Verified response (Sep 2026):
`{"success":true,"scaleX":0.002001...,"scaleY":-0.002000...,"offsetX":1160.92...,
"offsetY":1223.28...,"mapSize":2500,"pinOffset":{"x":-15,"y":25}}`
Transform (mirrors the frontend; note negative scaleY + the flip):
`left% = (offsetX + x·scaleX)/mapSize·100`,
`top%  = (1 − (offsetY + y·scaleY)/mapSize)·100`.
`pinOffset` is deliberately skipped — it compensates the website's pin-icon
anchor, not the player position; our arrow geometry is origin-centred.
`PandoraClient.FindCalibration` scans the JSON for the first object carrying
the five fields, so wrapping changes won't break it. Map image: site asset
`/assets/map-<hash>.png` (1000×1000); the hash changes per deploy, so a copy
is bundled as `Assets/map.png` (WPF Resource).

## Stack & build

- .NET 8, WPF, x64. One NuGet dep: `System.Security.Cryptography.ProtectedData`.
  WinForms interop (`UseWindowsForms`) enabled solely for the tray NotifyIcon.
  App icon `Assets/app.ico` (generated: dark rounded square + orange arrow).
- `dotnet build -c Release` → `bin/Release/net8.0-windows/PandoraOverlay.exe`.
  `PandoraOverlay.sln` at the root also carries **PandoraOverlay.Tests**
  (xUnit; `dotnet test`; CI runs it on every push). The app csproj globs the
  repo root, so the test subtree is `Compile Remove`d from it.
- `config.json` is created next to the exe on first run.

## Architecture

Dependency rule: `MainWindow` → { `PollService`, `OverlayConfig` } and
`PollService` → `PandoraClient`. `PandoraClient`/`OverlayConfig` have zero WPF
references — keep it that way. Every overlay window derives from
`OverlayWindowBase`.

- **PollService.cs** — owns the shared client + `DispatcherTimer` + `_busy`
  reentrancy guard; raises `SnapshotReceived`/`PollFailed`/`CalibrationChanged`
  on the UI thread. Windows are pure consumers: N windows, still ONE request
  stream (this is what preserves constraint #3). Fetches calibration once per
  launch (retried after a credential swap) and caches it into config. A
  second 60 s timer (`RefreshHeatmapAsync` — also hot-triggered on settings
  save, minimap re-show and the control-panel/tray heatmap toggle) raises
  `HeatmapChanged(byte[]?)`, gated on `HeatmapEnabled` + `MinimapEnabled`;
  null hides the layer.
- **OverlayWindowBase.cs** — shared Win32 interop (click-through / no-activate /
  toolwindow styles via SetWindowLongPtr, x64), `EditMode` state, shared
  edit-border brushes, `ApplyAppearance` (UI scale as a LayoutTransform +
  panel-glass alpha), and the edit-mode drag: manual (no DragMove) so it snaps
  live via SnapResolver against the current monitor's FULL bounds (not the
  work area — the game covers the taskbar; per-monitor through WinForms
  `Screen`, DIP-converted) and the other overlay window
  (static instance registry; `IsSnapTarget` false excludes transient chrome
  like the control panel from being snapped AGAINST); holding Alt bypasses.
  Windows are static-size in both modes (no banners since v1.9), so position
  fidelity is inherent. Locking edit mode and the Loaded event both run
  `ClampIntoScreen` (via `SnapResolver.ClampIntoRect`), so a locked panel is
  always fully on-screen — dragging stays free for cross-monitor moves;
  stale-monitor/resolution positions self-heal at startup. The global
  hotkeys are registered once, in MainWindow.
- **SnapResolver.cs** — pure, tested snapping math: screen edges + 16px
  inset + peer edges, 12px threshold (threshold < inset on purpose, so the
  two magnets read as distinct stops), axes independent; leading- and
  trailing-edge candidates per target give align-and-abut for free. Returns
  a `SnapResult` (position + per-axis `SnapGuide` naming the engaged target
  and whether it was a peer) so the caller can draw guides.
- **ControlPanelWindow.xaml(.cs)** — the edit-mode control panel (v1.9):
  appears with edit mode, hides on lock; labeled buttons Settings /
  Show-hide stats / Show-hide minimap / Map view / Heatmap | Lock / Exit + the hint
  line (hotkey label follows config). Derives OverlayWindowBase (drag/snap/clamp inherited),
  permanently interactive while visible, `IsSnapTarget` false, first show
  bottom-center, position persisted (`ControlPanelX/Y`, nullable). Activated
  on show (hotkey press grants foreground rights) so the game loses focus
  and releases its mouse capture — cursor visible immediately; only OUR
  window is activated, the game process is never touched. Buttons use a
  glow-overlay template (default chrome's hover highlight was unreadable).
- **SnapGuideWindow.cs** — full-virtual-screen, click-through, no-activate
  window drawing the guide lines mid-drag (orange = screen targets, blue =
  peer targets, matching arrow/waypoint colours). One lazily created
  instance shared app-wide (static in OverlayWindowBase); shown only while
  a snap is engaged, hidden on release/Alt.

- **PandoraClient.cs** — HTTP layer + `PlayerState`/`MyLocationResponse` records
  (case-insensitive JSON). One long-lived HttpClient, `UseCookies=false` (manual
  Cookie header so cf_clearance is sent verbatim), 8 s timeout, rolling-cookie
  capture *before* status check. `ConfigureAwait(false)` inside; UI hops back only
  at the outermost await. `FetchHeatmapAsync` mirrors the live-map page:
  checks the public `/map/api/heatmap-status` kill-switch (the site fails
  open, we fail closed), then GETs the cache-busted heatmap PNG — no Cookie
  header on either.
- **GrowthTracker.cs** — pure class fed from the snapshot stream: 15-min
  sliding window of (time, growth) samples → slope → in-game ETA to full
  growth. Needs a ≥5-min baseline before showing anything; a full baseline
  with no measurable delta → Paused (amber header). Resets on death/dino
  swap/not-in-game (a wall-clock gap would flatten the slope). Session-only.
- **OverlayConfig.cs** — config.json persistence + DPAPI vault. Plaintext `Cookie`
  field is a paste-inbox only: `Load()` encrypts it into `CookieProtected`
  (`DataProtectionScope.CurrentUser`) and blanks it. `GetCookie()` returns "" on
  any failure; nothing in this class ever throws.
- **MainWindow.xaml(.cs)** — orchestrator: owns the config, the PollService,
  and the minimap window's lifetime. Three global hotkeys (RegisterHotKey +
  WM_HOTKEY in WndProc; control-panel/tray labels follow config): edit mode
  (`Hotkey`, Ctrl+F7) toggling every window, hide/show overlay
  (`HotkeyHideAll`, Ctrl+F4 — exits edit mode first; hidden never persists;
  the edit hotkey un-hides first), and minimap view toggle
  (`HotkeyMinimapView`, Ctrl+F5 → `MinimapWindow.ToggleView`). Defaults sit
  in F4–F7 (v1.12 rebase — Ctrl+F3 proved globally held by third-party
  software in the wild; the mid-game toggles take the nearest keys, the
  occasional edit toggle the farthest), clear of the game's F2 recording
  and F10 hide-HUD keys — raw-input games can react to the bare F-key
  despite Ctrl;
  `OverlayConfig.Load` migrates configs still holding an exact past
  default trio (F3/F4/F5 or F7/F8/F9) and leaves customized sets alone. Edit mode:
  borders recolor, drag-with-snapping, and MainWindow shows the
  ControlPanelWindow (hidden again on lock); leaving edit mode
  persists all window positions + the re-encrypted rolled cookie.
  `ToggleStats` (v1.10) hides/shows the stats panel itself (`StatsEnabled`;
  hwnd stays alive so hotkeys/tray/polling continue; hide-all unhide
  respects the flag). Minimap is sized natively (`MinimapSize`, Settings
  slider 160–400, `AppearanceScale` override 1.0) while `UiScale` scales
  only the stats + control panels. `UpdateUi` is a 3-state machine:
  not-set-up / not-in-game / live (health bar recolors at <50% amber, <25% red;
  fracture badges toggle; health/hunger/thirst fills pulse below 25% — stamina
  deliberately excluded, it drains by design). Fires one `UpdateChecker` call
  on Loaded, feeding the status line + tray.
- **TrayIcon.cs** — WinForms NotifyIcon wrapper owned by MainWindow: the only
  always-visible affordance (windows are click-through, no taskbar/Alt-Tab).
  Right-click menu = edit mode / hide-show overlay / stats / minimap /
  heatmap / settings / exit (hotkey labels follow config); double-click = edit mode. Hover tooltip shows live stats (`SetStatus`,
  127-char NotifyIcon cap); the Edit mode entry's hotkey label follows config.
  `ShowUpdateAvailable` reveals a hidden menu entry (opens the Releases page)
  and appends the tag to the tooltip; `ShowHotkeyConflict`/`ClearHotkeyConflict`
  do the same for combos another app holds (entry opens Settings) — the
  status-line warning alone is overwritten by the next poll. Disposed on
  shutdown.
- **UpdateChecker.cs** — one fail-soft GET to the GitHub releases API at
  launch (the only non-islapandora network call); a newer tag surfaces via
  the status line (once) and the tray (for the session). Never re-checks,
  never pops anything up; offline/errors read as "no update".
- **MinimapWindow.xaml(.cs)** — bundled island map + player arrow. World→pixel
  per `MapCalibration` (with the Y flip); movement animates between polls
  (shortest-arc yaw; first fix / mode switch snaps). Two north-up views
  (`MinimapMode`): "island" (arrow translates over the fitted map) and
  "centered" (arrow pinned at centre, the map — rendered at size×`MinimapZoom`,
  clamped 1.25–6, default 5 — translates instead; no pan clamping, coasts show the map's
  own ocean border). The control panel's Map view button or Ctrl+F5 toggles
  views (`ToggleView`), wheel zooms in edit mode; a footer under the map
  always shows the active view (+ zoom when centered).
  `MinimapYawOffsetDegrees` corrects arrow orientation (default 90 — verified
  in-game, Sep 2026). Shown/hidden via the control panel or tray
  (`MinimapEnabled`). Waypoint: right-click in edit
  mode places/moves it (stored as world cm in config — persists), right-click
  on the marker clears it; blue diamond, edge-clamped in the centered view,
  distance appended to the footer (◆ 830m / ◆ 1.2km). Optional heatmap
  layer (`HeatmapEnabled`): the site's pre-rendered heatmap PNG (opaque —
  grayscale map, blobs and a player-count caption baked in) as a second
  Image sharing the map image's size and translate transform, blended at
  the site's own 55%; null/undecodable bytes collapse it.
- **SettingsWindow.xaml(.cs)** — sectioned settings dialog (Account / Controls
  / General / Minimap; single column, no tabs — deliberate, avoids theming
  stock TabControl chrome). Cookie box is a replace-inbox: empty = keep the
  current cookie; first run gates Save on a valid paste (`Clean()` strips
  `cookie:` prefix, quotes, newlines, trailing `;`; live validation needs
  `connect.sid`, warns if `cf_clearance` missing). Three hotkey capture boxes
  (edit / hide-overlay / minimap-view) share the capture UX: combos are
  availability-tested via a throwaway RegisterHotKey on the dialog's hwnd
  and cross-duplicates rejected. MainWindow suspends its three
  registrations for the dialog's lifetime (WM_HOTKEY is system-level and
  would fire behind the modal dialog; suspension also lets the boxes see
  and reassign our own combos) and restores them in a finally on close.
  The Minimap section holds view mode, centered zoom, map size and the
  heatmap toggle (folded into the Minimap flag).
  Save writes config + Run key and sets Cookie/Hotkey/Minimap/Appearance
  Changed flags; MainWindow hot-applies each (RebuildClient / re-register
  with fallback / minimap ApplySettings + a heatmap refresh / ApplyAppearance)
  — no restart, ever. General also holds the UI scale (75–150%) and background opacity
  (30–100%) sliders.
- **HotkeySpec.cs** — record converting the config string ("Ctrl+F7") ⇄ the
  RegisterHotKey pair (ModifierKeys flags == Win32 MOD_* values); hosts the
  shared Register/Unregister p/invokes. Registration always adds
  MOD_NOREPEAT (without it, holding the combo past the key-repeat delay
  fires twice — a toggle turns on and instantly off, reading as a dead
  keypress). Modifier-less hotkeys are rejected (a bare global key would be
  swallowed from the game). Startup registration failures are surfaced in
  the status line by MainWindow, not swallowed.
- **StartupRegistration.cs** — Start-with-Windows via HKCU Run; the registry
  entry IS the state (deliberately no config field to drift). Fail-soft.
- **App.xaml.cs** — single-instance mutex: a second launch shows a notice and
  exits (protects constraint #3 from silently doubled polling).
- In-UI icons are font glyphs (✕ ♂ ♀, text badges); the only image assets are
  `Assets/map.png` and `Assets/app.ico`.

## Runtime expectations

- Game must run **borderless windowed** (overlay can't beat exclusive fullscreen).
- `inGame:false` during server restarts/menus is normal — UI shows "Not in-game"
  and self-recovers. `getplayerdata` upstream only includes spawned players.
- Growth (owner's server knowledge, Sep 2026): Isla Pandora runs a **1.3×**
  growth multiplier vs official; total grow time differs per species; growth
  does NOT appear to pause when starving/dehydrated. GrowthTracker measures
  the effective rate, so none of this needs configuring.
- Status line shows last-update timestamp; "Disconnected · retrying (TypeName)"
  on errors. Persistent 401/403 → user pastes a fresh cookie via ⚙.

## Roadmap

Shipped Sep 2026, all verified in-game: v1.1.0 (minimap — arrow position
matches the website's live map, yaw offset 90 correct), v1.2.0
(player-centered north-up view — panning accurate), v1.3.0 (view-mode
footer, 5× default zoom, tray icon + app icon), v1.3.1 (setup-dialog
disabled-Save fix), v1.4.0 (sectioned settings window, rebindable hotkey,
Start with Windows, tray stats tooltip, single-instance guard), v1.5.0
(growth ETA in the stats header — estimate confirmed accurate in-game),
v1.6.0 (update notifier, minimap waypoint, critical-stat pulses, UI
scale + background-opacity sliders, xUnit test suite in CI), v1.7.0
(hide-all + minimap-view hotkeys, drag snapping, edit-mode position
fidelity, on-screen clamping), v1.8.0 (snap guide lines while dragging),
v1.9.0 (banner-less widgets + edit-mode control panel, focus grab so the
game releases the cursor, hover-readable buttons), v1.10.0 (hide-stats
toggle, per-widget sizing: native Map size slider + stats-only scale),
v1.11.0 (hotkey defaults rebased to Ctrl+F3/F4/F5 with auto-migration —
verified: migration fired, new combos work in-game), v1.12.0 (hotkey
reliability: MOD_NOREPEAT, startup conflicts surfaced persistently in
the tray, registrations suspended during the settings dialog; edit
default moved to Ctrl+F7 after Ctrl+F3 proved squatted by third-party
software — verified in-game).

Later/maybe: friends markers (needs permission first), zone overlays
(needs permission; the live-map bundles them as static PNGs — patrols,
sanctuaries, migrations, salt rocks), official token auth (the nudge went
out with the heatmap ask ~Sep 14 2026; the heatmap got its yes Sep 15,
tokens unanswered), Segoe Fluent Icons for stat glyphs. Velopack auto-update installer (decided Sep
2026) — TRIGGER: only if the overlay goes community-wide beyond the friend
group (e.g. posted publicly after a heatmap yes); then skip Inno entirely
and go straight to Velopack: `vpk pack` replaces the zip step in CI,
GitHub Releases stays the update feed, UpdateChecker retires. Ship BOTH
human-facing artifacts per release — Setup.exe and a portable zip
(portable stays fully manual: choosing portable is choosing manual
control) — plus the nupkg/manifest feed files, with release notes
pointing people at the right two. Update
policy (decided Sep 2026): a single checkbox, **"Check for updates at
launch"**, default ON — notify only; a user click triggers the download +
apply; unchecked = no GitHub call at all. The app can NEVER modify itself
without a click — "Install automatically" was considered and dropped
(silent apply must defer to next launch anyway to avoid restarting the
overlay mid-game, so auto saves exactly one click per release while
weakening the trust story and adding a background pipeline). No silent
pre-download either: negligible gain for tiny deltas, more state, and
notify mode fetches nothing beyond the version check without consent.
Prerequisite refactor: config.json must move from
next-to-exe into %AppData%\PandoraOverlay (Velopack uses versioned
app-X.Y.Z folders — the current location would reset settings every
update) with a one-time migration. Until the trigger: zip + notifier is
the right size, don't build an installer. Rejected: rotating (facing-up)
minimap mode — owner decided it isn't useful enough (Sep 2026); don't
re-propose.

## Conventions

- Code-behind over MVVM — deliberate at this size; don't introduce frameworks.
- Fail soft: config/crypto/HTTP errors degrade to a UI state, never crash.
- Keep files well under ~500 lines; current style is regions + XML doc comments.
- Versioning: SemVer. The csproj `<Version>` is the single source of truth;
  bump it each release and tag the commit `vX.Y.Z` (annotated). Features bump
  minor, fixes bump patch. Current: 1.12.0.
- Release model: main moves freely between releases; tags mark the stable
  points. Anyone wanting "a version" uses a tag or its GitHub Release (pushing
  a `vX.Y.Z` tag triggers the workflow that builds and attaches the zip) —
  never a random commit. No standing release/version branches.
- Never move or re-tag an existing tag. If a release ships broken, fix forward
  and tag the next patch version.
- Hotfixing an old release while main holds unreleased work:
  `git switch -c fix vX.Y.Z` → fix → bump patch in csproj + CHANGELOG →
  tag `vX.Y.(Z+1)` → push the tag (release builds automatically) →
  merge/cherry-pick the fix back to main → delete the branch.
