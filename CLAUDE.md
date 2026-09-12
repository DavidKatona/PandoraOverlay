# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server). Shows the
player's own dino stats in an always-on-top panel. **v1.0.0 is built, working, and
approved by the server's web dev.**

## Hard constraints (never violate)

1. **Fully external, always.** Never read game memory, inject, hook, enumerate or
   touch the game process in any way. The Isle runs Easy Anti-Cheat. The ONLY data
   source is the islapandora.eu web API. If a feature seems to need game-side data,
   the answer is no.
2. **One endpoint.** Only `POST /api/map/mylocation` is called. The `friends` and
   heatmap/zone endpoints are NOT cleared for use (see Permissions).
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

## Stack & build

- .NET 8, WPF, x64. One NuGet dep: `System.Security.Cryptography.ProtectedData`.
- `dotnet build -c Release` → `bin/Release/net8.0-windows/PandoraOverlay.exe`.
- No tests yet. `config.json` is created next to the exe on first run.

## Architecture

Dependency rule: `MainWindow` → { `PandoraClient`, `OverlayConfig` }. The two
non-UI classes have zero WPF references — keep it that way (reusable for future
tray app / minimap window).

- **PandoraClient.cs** — HTTP layer + `PlayerState`/`MyLocationResponse` records
  (case-insensitive JSON). One long-lived HttpClient, `UseCookies=false` (manual
  Cookie header so cf_clearance is sent verbatim), 8 s timeout, rolling-cookie
  capture *before* status check. `ConfigureAwait(false)` inside; UI hops back only
  at the outermost await.
- **OverlayConfig.cs** — config.json persistence + DPAPI vault. Plaintext `Cookie`
  field is a paste-inbox only: `Load()` encrypts it into `CookieProtected`
  (`DataProtectionScope.CurrentUser`) and blanks it. `GetCookie()` returns "" on
  any failure; nothing in this class ever throws.
- **MainWindow.xaml(.cs)** — orchestrator. Win32 interop: `WS_EX_TRANSPARENT |
  WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` via SetWindowLongPtr (x64 assumption);
  global hotkey **Ctrl+F8** (RegisterHotKey + WM_HOTKEY in WndProc hook) toggles
  edit mode (drag via DragMove, ⚙ settings, ✕ close; leaving edit mode persists
  position + re-encrypted cookie). `DispatcherTimer` poll loop with `_busy`
  reentrancy guard; everything runs on the UI thread — no locks/Invoke needed.
  `UpdateUi` is a 3-state machine: not-set-up / not-in-game / live (health bar
  recolors at <50% amber, <25% red; fracture badges toggle).
- **SettingsWindow.xaml(.cs)** — cookie paste dialog. `Clean()` strips `cookie:`
  prefix, quotes, newlines, trailing `;`. Live validation (needs `connect.sid`;
  warns if `cf_clearance` missing). Auto-opens on first run; save hot-swaps the
  client via `MainWindow.RebuildClient()` — no restart.
- Icons are font glyphs (✕ ♂ ♀, text badges) — no image assets in the project.

## Runtime expectations

- Game must run **borderless windowed** (overlay can't beat exclusive fullscreen).
- `inGame:false` during server restarts/menus is normal — UI shows "Not in-game"
  and self-recovers. `getplayerdata` upstream only includes spawned players.
- Status line shows last-update timestamp; "Disconnected · retrying (TypeName)"
  on errors. Persistent 401/403 → user pastes a fresh cookie via ⚙.

## Roadmap — next: v1.1 minimap (approved)

Same endpoint already provides x/y/z/yaw. Plan:
1. Map asset: grab the island image URL from DevTools (Img filter) on the live
   map; cache locally. **Not yet captured.**
2. World→pixel transform: affine, no rotation expected —
   `px = (x - minX)/(maxX - minX) * imgW` (watch for Y sign flip). Get min/max by
   reading their frontend JS (search bundle for bounds/scale consts), from
   community Evrima map tools, or 2-point empirical calibration (3rd point
   verifies no rotation). **Constants not yet known.**
3. Render: Canvas + clipped Image + arrow Path; TransformGroup
   (Translate/Scale/Rotate) for north-up or player-centered modes. Freeze bitmap,
   DecodePixelWidth to display size.
4. Smoothing: animate position between polls (~poll interval duration);
   shortest-arc interpolation for yaw.
5. Refactor first: extract polling into a `PollService` (owns client + timer,
   raises SnapshotReceived) so bars + minimap share ONE request stream (this
   preserves constraint #3). Minimap = separate draggable window; consider a
   shared base class for the click-through/hotkey/edit-mode interop.

Later/maybe: friends markers (needs permission first), zone overlays (needs
permission), official token auth (if the dev builds it), Segoe Fluent Icons for
stat glyphs, app icon in csproj.

## Conventions

- Code-behind over MVVM — deliberate at this size; don't introduce frameworks.
- Fail soft: config/crypto/HTTP errors degrade to a UI state, never crash.
- Keep files well under ~500 lines; current style is regions + XML doc comments.
- Versioning: SemVer. The csproj `<Version>` is the single source of truth;
  bump it each release and tag the commit `vX.Y.Z` (annotated). Features bump
  minor, fixes bump patch. Current: 1.0.0.
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
