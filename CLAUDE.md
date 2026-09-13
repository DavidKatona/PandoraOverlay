# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server). Shows the
player's own dino stats in an always-on-top panel, plus a minimap, tray icon,
and settings window. **v1.6.0 is built, working, and approved by the server's
web dev.**

## Hard constraints (never violate)

1. **Fully external, always.** Never read game memory, inject, hook, enumerate or
   touch the game process in any way. The Isle runs Easy Anti-Cheat. The ONLY data
   source is the islapandora.eu web API. If a feature seems to need game-side data,
   the answer is no.
2. **Two endpoints only.** `POST /api/map/mylocation` (the poll) and
   `POST /api/map/calibration` (once per launch — static map-transform constants
   for the approved minimap; the live-map page itself loads it on every visit;
   added at the owner's direction, Sep 2026). The `friends` and heatmap/zone
   endpoints are NOT cleared for use (see Permissions). The launch-time update
   check calls the GitHub releases API — not an islapandora endpoint, so it
   sits outside this constraint.
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
- **NOT approved:** using the `friends` endpoint — ask him first. A read-only API
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
  launch (retried after a credential swap) and caches it into config.
- **OverlayWindowBase.cs** — shared Win32 interop (click-through / no-activate /
  toolwindow styles via SetWindowLongPtr, x64), `EditMode` state, shared
  edit-border brushes, `ApplyAppearance` (UI scale as a LayoutTransform +
  panel-glass alpha), and the edit-mode drag: manual (no DragMove) so it snaps
  live via SnapResolver against the current monitor's work area (per-monitor
  through WinForms `Screen`, DIP-converted) and the other overlay window
  (static instance registry); holding Alt bypasses. Banner compensation:
  entering edit mode shifts `Top` up by the (scale-aware) banner height so
  the CONTENT stays put between modes; snapping runs in content space, and
  the shift clamps at the work-area top. The global hotkeys are registered
  once, in MainWindow.
- **SnapResolver.cs** — pure, tested snapping math: work-area edges + 12px
  inset + peer edges, 14px threshold, axes independent; leading- and
  trailing-edge candidates per target give align-and-abut for free.

- **PandoraClient.cs** — HTTP layer + `PlayerState`/`MyLocationResponse` records
  (case-insensitive JSON). One long-lived HttpClient, `UseCookies=false` (manual
  Cookie header so cf_clearance is sent verbatim), 8 s timeout, rolling-cookie
  capture *before* status check. `ConfigureAwait(false)` inside; UI hops back only
  at the outermost await.
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
  WM_HOTKEY in WndProc; banner/tray labels follow config): edit mode
  (`Hotkey`, Ctrl+F8) toggling every window, hide/show overlay
  (`HotkeyHideAll`, Ctrl+F9 — exits edit mode first; hidden never persists;
  the edit hotkey un-hides first), and minimap view toggle
  (`HotkeyMinimapView`, Ctrl+F7 → `MinimapWindow.ToggleView`). Edit mode:
  drag-with-snapping, ⚙ settings, MAP minimap toggle, ✕ close; leaving it
  persists all window positions + the re-encrypted rolled cookie. `UpdateUi` is a 3-state machine:
  not-set-up / not-in-game / live (health bar recolors at <50% amber, <25% red;
  fracture badges toggle; health/hunger/thirst fills pulse below 25% — stamina
  deliberately excluded, it drains by design). Fires one `UpdateChecker` call
  on Loaded, feeding the status line + tray.
- **TrayIcon.cs** — WinForms NotifyIcon wrapper owned by MainWindow: the only
  always-visible affordance (windows are click-through, no taskbar/Alt-Tab).
  Right-click menu = edit mode / hide-show overlay / minimap toggle /
  settings / exit (hotkey labels follow config); double-click = edit mode. Hover tooltip shows live stats (`SetStatus`,
  127-char NotifyIcon cap); the Edit mode entry's hotkey label follows config.
  `ShowUpdateAvailable` reveals a hidden menu entry (opens the Releases page)
  and appends the tag to the tooltip. Disposed on shutdown.
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
  own ocean border). VIEW button toggles, wheel zooms (edit mode only); a
  footer under the map always shows the active view (+ zoom when centered).
  `MinimapYawOffsetDegrees` corrects arrow orientation (default 90 — verified
  in-game, Sep 2026). ✕ on its banner hides it (`MinimapEnabled=false`); the
  MAP button on the stats panel brings it back. Waypoint: right-click in edit
  mode places/moves it (stored as world cm in config — persists), right-click
  on the marker clears it; blue diamond, edge-clamped in the centered view,
  distance appended to the footer (◆ 830m / ◆ 1.2km).
- **SettingsWindow.xaml(.cs)** — sectioned settings dialog (Account / Controls
  / General / Minimap; single column, no tabs — deliberate, avoids theming
  stock TabControl chrome). Cookie box is a replace-inbox: empty = keep the
  current cookie; first run gates Save on a valid paste (`Clean()` strips
  `cookie:` prefix, quotes, newlines, trailing `;`; live validation needs
  `connect.sid`, warns if `cf_clearance` missing). Three hotkey capture boxes
  (edit / hide-overlay / minimap-view) share the capture UX: combos are
  availability-tested via a throwaway RegisterHotKey on the dialog's hwnd
  (skipped for combos our app already holds) and cross-duplicates rejected.
  Save writes config + Run key and sets Cookie/Hotkey/Minimap/Appearance
  Changed flags; MainWindow hot-applies each (RebuildClient / re-register
  with fallback / minimap ApplySettings / ApplyAppearance) — no restart,
  ever. General also holds the UI scale (75–150%) and background opacity
  (30–100%) sliders.
- **HotkeySpec.cs** — record converting the config string ("Ctrl+F8") ⇄ the
  RegisterHotKey pair (ModifierKeys flags == Win32 MOD_* values); hosts the
  shared Register/Unregister p/invokes. Modifier-less hotkeys are rejected
  (a bare global key would be swallowed from the game).
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
scale + background-opacity sliders, xUnit test suite in CI).

Later/maybe: friends markers (needs permission first), zone overlays (needs
permission), official token auth (if the dev builds it), Segoe Fluent Icons
for stat glyphs. Rejected: rotating (facing-up) minimap mode — owner decided
it isn't useful enough (Sep 2026); don't re-propose.

## Conventions

- Code-behind over MVVM — deliberate at this size; don't introduce frameworks.
- Fail soft: config/crypto/HTTP errors degrade to a UI state, never crash.
- Keep files well under ~500 lines; current style is regions + XML doc comments.
- Versioning: SemVer. The csproj `<Version>` is the single source of truth;
  bump it each release and tag the commit `vX.Y.Z` (annotated). Features bump
  minor, fixes bump patch. Current: 1.6.0.
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
