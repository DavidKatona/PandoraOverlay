# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server). Shows the
player's own dino stats in an always-on-top panel, plus a minimap, tray icon,
and settings window. **v1.18.0 is built, working, and approved by the server's
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
   own page refetches every 10 s per open tab), and the prime pair
   `POST /api/prime/check` + `POST /api/prime/cooldown` (approved Sep 19
   2026, both confirmed covered by the owner; cookie-authed and
   USER-TRIGGERED ONLY — a control panel click or the Check Prime hotkey
   press, never a timer. The
   cooldown is mirrored client-side, so a cooling-down or not-in-game click
   sends no prime request — a STALE not-in-game state is first refreshed by
   one regular `mylocation` poll, since idle pacing can trail a spawn by up
   to a minute; `prime/cooldown` is called exactly once after each
   SUCCESSFUL check, because the success response carries no cooldown and
   its length varies per account — supporter ranks shorten it, so assuming
   the website button's hardcoded 5 min over-blocks ranked players). The
   `friends` endpoint and
   the zone overlays are NOT cleared for use (see Permissions). The
   launch-time update check calls the GitHub releases API — not an
   islapandora endpoint, so it sits outside this constraint.
3. **Poll interval >= 2 s** (default 3 s, matching the website's own cadence).
   Server-side rate limit is 300/window. Never add endpoints or frequency without
   the owner's explicit okay — the dev specifically praised the polling restraint.
   The cadence is adaptive, DOWNWARDS ONLY: the configured interval is the
   in-game pace; not in-game (or 5 straight failures) idles at 15 s, then
   60 s after 10 min. Off-schedule polls (user nudges) are floored at the
   configured interval and re-arm the timer — nothing may ever poll faster.
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
- Approved Sep 19 2026 (relayed by the owner): the **Prime tracker** —
  rebuilding the live-map page's "Prime Check" box as an overlay widget.
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
`left% = (offsetX + x·scaleX + pinOffset.x)/mapSize·100`,
`top%  = (1 − (offsetY + y·scaleY + pinOffset.y)/mapSize)·100`.
`pinOffset` is part of the site's coordinate mapping for EVERY marker — the
frontend anchors markers centered (translate(-50%,-50%)), player arrow
included (verified in the bundle, Sep 16 2026). v1.13.0 and earlier skipped
it on the wrong assumption it compensated the pin-icon anchor, drawing
~0.6%/1% off the website.
`PandoraClient.FindCalibration` scans the JSON for the first object carrying
the five fields (+ the optional nested pinOffset — absent reads as 0), so
wrapping changes won't break it. Map image: site asset
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
  stream (this is what preserves constraint #3). Adaptive pacing (Sep 2026
  — a fixed 3 s poll sent ~28,800 requests/day with the game closed):
  `ApplyPacing` runs after every poll, BEFORE the event is raised, and sets
  the timer from the pure, tested `NextInterval` — configured cadence while
  live, 15 s when not in-game or after `FailuresBeforeIdle` (5) straight
  failures, 60 s once that has lasted 10 min; a slower configured cadence is
  never sped up. The interval is assigned only on a change (assigning
  re-arms a running DispatcherTimer), so the in-game rhythm is untouched.
  `Nudge()` (MainWindow: entering edit mode, un-hiding) and a Check Prime
  click on a stale not-in-game state poll right away via `PollNowAsync`,
  which re-arms the timer and is floored at the configured interval — the
  spawn-detection lag is the feature's price, these are its relief valves.
  `IsIdling`/`Interval` feed the status line. Fetches calibration once per
  launch (retried after a credential swap) and caches it into config. A
  second 60 s timer (`RefreshHeatmapAsync` — also hot-triggered on minimap
  re-show and the heatmap hotkey / control-panel toggle) raises
  `HeatmapChanged(byte[]?)`, gated on `HeatmapEnabled` + `MinimapEnabled`;
  null hides the layer. `CheckPrimeAsync` is the on-demand prime check —
  NO timer may ever call it. All gating lives here: busy guard, the
  cooldown mirror (`PrimeCooldownUntilUtc`) and the last poll's in-game
  state answer locally with no prime request (a stale not-in-game state
  gets one regular poll first). The cooldown is the SERVER's word,
  never an assumption (it varies with supporter rank): after an Ok check
  `prime/cooldown` is asked once (5 min only as the fallback when that
  fails), a rejected check supplies `remainingMs`, and the result is
  persisted as `config.PrimeCooldownUntilUtc` so restarts don't re-assume;
  a 15 s floor/retry guard means the check can never be mashed. Raises
  `PrimeCheckStarted` / `PrimeChecked(PrimeCheckResult)` — the latter only
  after the cooldown is known, so the widget's countdown starts right —
  and caches an Ok snapshot into `config.Prime`.
- **OverlayWindowBase.cs** — shared Win32 interop (click-through / no-activate /
  toolwindow styles via SetWindowLongPtr, x64), `EditMode` state, shared
  edit-border brushes, `ApplyAppearance` (UI scale as a LayoutTransform +
  panel-glass alpha), the attention fade, and the edit-mode drag: manual (no DragMove) so it snaps
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
  hotkeys are registered once, in MainWindow. Attention fade (Sep 2026,
  `FadeEnabled` + `FadeIdleOpacity`, default off): windows opting in via
  `Fades => true` (stats panel, Prime tracker — NOT the minimap: nothing
  on it can wake it, and it is consulted rather than watched) call
  `SetAttention(bool)`; `UpdateFade` eases the whole window's `Opacity`
  (text, bars and glass alike — `BackgroundOpacity` only touches the
  glass, so the fade can only ever make a panel fainter, never brighter)
  to 1 or the idle value over 300 ms, and edit mode overrides to 1. The
  idle value is read in `ApplyAppearance`, so a Settings save hot-applies
  through the existing Appearance flag. Each window decides its own
  verdict: the stats panel via the pure `StatsAttention`, the Prime
  tracker via its own events (see PrimeWindow). `PulseBriefly(element,
  total)` is the shared one-element blink (opacity 1→0.3 autoreverse,
  FillBehavior.Stop so the element ends exactly as it was) for the growth
  header at a milestone and the Prime footer at cooldown end.
- **SnapResolver.cs** — pure, tested snapping math: screen edges + 16px
  inset + peer edges, 12px threshold (threshold < inset on purpose, so the
  two magnets read as distinct stops), axes independent; leading- and
  trailing-edge candidates per target give align-and-abut for free. Returns
  a `SnapResult` (position + per-axis `SnapGuide` naming the engaged target
  and whether it was a peer) so the caller can draw guides.
- **ControlPanelWindow.xaml(.cs)** — the edit-mode control panel (v1.9):
  appears with edit mode, hides on lock; ONE row grouped by widget (owner's
  call, Sep 2026 — a two-row layout was tried and rejected): Settings |
  STATS: Show/hide | MINIMAP: Show/hide, Map view, Heatmap | PRIME:
  Show/hide, Check | Lock / Exit — small captions over each group keep
  labels short, and a new widget adds a group, not loose buttons; + the hint
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
  header on either. `CheckPrimeAsync` is the cookie-authed, empty-bodied
  prime POST; like the frontend it parses the JSON body regardless of HTTP
  status (cooldown / not_in_game arrive as error bodies). `ParsePrime` is
  pure + tested: status under isPrimeElder/isPrime and
  isEligiblePrime/isEligible, condition flags keyed "1".."10" or
  "c1".."c10"; server error strings are mapped, never echoed.
  `FetchPrimeCooldownAsync` / `ParsePrimeCooldown` read the account's
  remaining cooldown (success without a positive `remainingMs` = none
  running, like the frontend; anything unusable = null → caller falls back).
- **GrowthTracker.cs** — pure class fed from the snapshot stream: 15-min
  sliding window of (time, growth) samples → slope → in-game ETA to full
  growth. Needs a ≥5-min baseline before showing anything; a full baseline
  with no measurable delta → Paused (amber header). Resets on death/dino
  swap/not-in-game (a wall-clock gap would flatten the slope). Session-only.
- **DrainTracker.cs** — GrowthTracker's sibling for a draining stat (two
  instances in MainWindow: hunger, thirst): 10-min window, ≥3-min baseline,
  endpoint slope → `TimeLeft` to zero. The drain RATE survives a refill (a
  rise > 0.002 between polls restarts the window but keeps the rate — eating
  moves the level, not the metabolism), so the estimate is back on the next
  poll; a full flat baseline clears it. `Label` ("~40m") is what the UI
  shows: only under 1 h left (owner's call — 3 h was tried and read as
  clutter), with a 60/65 min show/hide gap so it can't blink at the edge.
  Resets on death/dino swap/not-in-game. Rendered as a bar-chart data
  label (`HungerLeft`/`ThirstLeft`, `MainWindow.SetTimeLeft`): it rides
  just past the fill's tip in the bar's own lightened colour, flipping
  inside the fill's end (dark text) when the track has no room left; the
  TextBlock overlays the track's grid cell rather than living in it, so
  the 12 px bar can't clip it. The panel keeps its exact size — widening
  the 44 px percent column was rejected (an update must never resize a
  panel), and a first version (white 9 px text in a dark pill at the
  track's right end) was rejected on sight: the track is near-invisible,
  so it floated next to the percent like a second unrelated number.
  Whether drain varies with activity (sprinting) is UNVERIFIED — don't
  claim it in user-facing text. `StatTimeLeftEnabled`
  (Settings checkbox, default on) only gates rendering; the trackers always
  run, so ticking the box shows the estimate at once.
- **StatsAttention.cs** — pure, tested wake/calm rule for the stats
  panel's fade: wake on health/hunger/thirst < 50%, any fracture, damage
  (health down > 0.005 between polls, held 10 s — one poll's drop is
  momentary) or a drain estimate under 15 min; calm only above 55% / 20
  min with none of the rest, so a stat at the line can't blink. Resets
  its damage baseline on death/dino swap (identity or growth decrease,
  like the trackers) and on not-in-game. `NoteEvent` holds it lit 10 s for
  a one-off moment (a growth milestone). MainWindow also lights the panel
  by hand for not-set-up, connecting and disconnected, and calms it for
  not-in-game — nothing to watch there.
- **LowStatAlert.cs** — pure, tested chime rule for hunger/thirst (v1.19,
  `LowStatChimeEnabled`, default off): each stat chimes once on dropping
  under 20%, repeats every 5 min while it stays there (the AFK grower who
  missed the first), and re-arms only above 30% so a stat at the line
  can't chime per poll; a new life starts armed. The sound is the Windows
  "Exclamation" scheme sound (`MainWindow.Chime`, no bundled audio —
  owner's call, a custom sound only if users ask).
- **GrowthMilestones.cs** — pure, tested: reports the stage line crossed
  by a sample (25 juvenile / 50 subadult / 75 adult / 100 elder, the last
  at GrowthTracker's 0.9995 full line); the first sample of a life is only
  a baseline, a gap over two lines reports the higher, death/swap resets.
  MainWindow always `PulseBriefly`s the growth header + `NoteEvent`s the
  fade for it; the chime is gated by `GrowthChimeEnabled` (default off).
- **OverlayConfig.cs** — config.json persistence + DPAPI vault. Plaintext `Cookie`
  field is a paste-inbox only: `Load()` encrypts it into `CookieProtected`
  (`DataProtectionScope.CurrentUser`) and blanks it. `GetCookie()` returns "" on
  any failure; nothing in this class ever throws.
- **MainWindow.xaml(.cs)** — orchestrator: owns the config, the PollService,
  and the minimap + prime windows' lifetimes (both follow edit mode and
  hide-all; `CheckPrime` un-hides and shows the prime widget first, then
  fires the one user-triggered check). Five global hotkeys (RegisterHotKey +
  WM_HOTKEY in WndProc; control-panel/tray labels follow config): edit mode
  (`Hotkey`, Ctrl+F7) toggling every window, hide/show overlay
  (`HotkeyHideAll`, Ctrl+F4 — exits edit mode first; hidden never persists;
  the edit hotkey un-hides first), minimap view toggle
  (`HotkeyMinimapView`, Ctrl+F5 → `MinimapWindow.ToggleView`), and the
  heatmap toggle (`HotkeyHeatmap`, Ctrl+F6 → `ToggleHeatmap`, a no-op while
  the minimap is hidden; it replaced the tray entry and the Settings
  checkbox), and Check Prime (`HotkeyPrimeCheck`, Ctrl+F8 → `CheckPrime`;
  v1.19, replacing the tray's "Check Prime status" line). The heatmap and
  Prime keys arrived after users had customized the earlier ones, so
  `ResolveLateHotkey` swaps a colliding default for the first free
  candidate (heatmap F6/F8/F9/F11, prime F8/F9/F11/F12/F6 — always one
  more candidate than takers) instead of letting an own-app duplicate
  fail to register and read as "in use by another app". Defaults sit
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
  respects the flag). Not-in-game auto-hide (`HideWhenNotInGame`, default
  off, Sep 2026): `UpdateAutoHide` runs per snapshot — after
  `AutoHideGrace` (30 s, a constant: it only has to outlast one spurious
  inGame:false, since the spawn menu and restarts are fine to hide over
  and the first in-game poll brings everything back) it calls the shared
  `HideWindows`; the first in-game poll calls `ShowWindows`. Two
  precedence rules, both in that method: never while editing, and never
  over a manual hide (`_overlayHidden` is the user's word and a spawn
  must not undo it, so the two flags are never both set). A user reveal
  (`RevealAutoHidden` — hotkey, tray, edit mode, Check Prime all route
  through `ToggleOverlayVisibility`, which reveals instead of toggling
  while auto-hidden) and locking edit mode both just restart the grace
  clock. A "reveal keeps it visible until the next spawn" rule was tried
  and REJECTED in testing (Sep 24 2026): unlock + lock left the overlay
  refusing to hide, which read as a bug — don't reintroduce it. Tray
  tooltip reads "hidden until you spawn" meanwhile. Runtime-only, like
  the manual hide. Minimap is sized natively (`MinimapSize`, Settings
  slider 160–400, `AppearanceScale` override 1.0), the prime tracker by
  `PrimeScale`, and `UiScale` scales only the stats + control panels. `UpdateUi` is a 3-state machine:
  not-set-up / not-in-game / live (health bar recolors at <50% amber, <25% red;
  fracture badges toggle; health/hunger/thirst fills pulse below 25% — stamina
  deliberately excluded, it drains by design). Fires one `UpdateChecker` call
  on Loaded, feeding the status line + tray.
- **TrayIcon.cs** — WinForms NotifyIcon wrapper owned by MainWindow: the only
  always-visible affordance (windows are click-through, no taskbar/Alt-Tab).
  Right-click menu, deliberately short = edit mode / hide-show overlay |
  settings / exit (hotkey labels follow config) — no per-widget toggles,
  and no Check Prime since v1.19 (it earned Ctrl+F8), see the surface
  rules under Conventions;
  double-click = edit mode. Hover tooltip shows live stats (`SetStatus`,
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
  in-game, Sep 2026). Shown/hidden via the control panel
  (`MinimapEnabled`). Waypoint: right-click in edit
  mode places/moves it (stored as world cm in config — persists), right-click
  on the marker clears it; blue diamond, edge-clamped in the centered view,
  distance appended to the footer (◆ 830m / ◆ 1.2km). Optional heatmap
  layer (`HeatmapEnabled`): the site's pre-rendered heatmap PNG (opaque —
  grayscale map, blobs and a player-count caption baked in) as a second
  Image sharing the map image's size and translate transform, blended at
  the site's own 55%; null/undecodable bytes collapse it. Breadcrumb trail
  (`MinimapTrailMinutes`, Settings radios Off/10/30/60, default 30): three
  Polylines in the MAP canvas, in map-pixel space and sharing
  `_mapTranslate`, so they pan and glide with the map for free —
  `RenderTrail` rebuilds them per snapshot and in `ApplyViewMode` (the
  rendered size changed). Three age bands at 0.85/0.55/0.3 opacity, since
  one polyline can't fade along its length; each band starts on the
  previous band's last point. Solid on purpose — a dash pattern is anchored
  at the first point and would crawl along the whole trail whenever the
  tail trims. Hidden with the arrow while not in-game. Scale bar
  (`MinimapScaleBarEnabled`, default on): bottom-left, because the
  heatmap's baked caption owns the top-left; `UpdateScaleBar` runs from
  `ApplyViewMode` and `OnCalibrationChanged`, budget = 30% of the map
  width capped at 80 px. `ToFraction` is the one world→map-fraction
  transform (arrow, waypoint, trail).
- **BreadcrumbTrail.cs** — pure, tested path store in world cm: a point
  per poll once moved ≥ 5 m, expiry by age, reset on death/dino swap
  (identity / growth decrease, like the trackers) and on a jump no dino
  could travel (> 60 m/s, or > 500 m across any poll gap — the path is
  unknown there anyway), which would otherwise draw a line across the map.
  Deliberately NOT reset by not-in-game: after a relog/restart you stand
  where you stood. Lives in MinimapWindow, so closing the minimap widget
  (not hide-all, which only hides) starts it over.
- **ScaleBar.cs** — pure, tested: largest 1-2-5 × 10ⁿ metres fitting a
  pixel budget, + the "500 m" / "2 km" label.
- **PrimeWindow.xaml(.cs)** — the Prime tracker widget: status header +
  ten ✓/✗ condition rows (texts baked in — the site bakes them into its
  frontend too, the API only returns flags) + a two-line footer (what the
  rows reflect: time and dino, amber once the live dino differs; then
  checking / sticky notice / live cooldown countdown — its 1 s timer only
  runs while cooling down). Display-only and click-through when locked;
  the check is triggered from the control panel or the Ctrl+F8 hotkey via
  `MainWindow.CheckPrime` → `PollService.CheckPrimeAsync`. Changed-since-
  last-check highlight (v1.19): `OnCheckStarted` snapshots `_config.Prime`
  as `_before` (PollService swaps in the new result before `PrimeChecked`
  fires), an Ok result diffs the flags into `_changed`, and
  `RenderSnapshot` paints a newly met row bright/semibold and a lost one
  amber until the next check replaces the comparison; session-only. The
  cooldown reaching zero also `PulseBriefly`s the footer for those
  without the fade. Fixed 250 px
  content width and a reserved footer keep it one size in every state.
  Derives OverlayWindowBase (drag/snap/clamp; sized by its own `PrimeScale`
  via the `AppearanceScale` override — seeded from `UiScale` in
  `OverlayConfig.Load` for configs that predate it); first
  show docks to the left screen edge (16 px inset), vertically centered;
  position persists (`PrimeX/Y`, nullable), visibility via `PrimeEnabled`
  (default on — a visible widget costs zero requests until clicked).
  Takes part in the attention fade (`Fades => true`): `Wake(hold)` lights
  it — a check in flight holds until its result, while a result, the
  countdown reaching zero (caught where `RenderNotice` stops the 1 s
  timer) and the stale-dino cue first appearing (`RenderInfo` returns
  stale) each hold `AttentionHold` (30 s) via a one-shot timer, then it
  fades again. Deliberately not lit for as long as the cue stays amber:
  a permanently lit widget would defeat the fade. Calm at launch — the
  cached result is old news.
- **SettingsWindow.xaml(.cs)** — sectioned settings dialog: Account /
  Controls / General (APP-WIDE ONLY) / Stats panel / Minimap / Prime
  tracker — widget sections in the control panel's order, each holding
  that widget's Scale-or-Size slider and its own options; a new widget
  adds a section (regrouped Sep 24 2026, "option A", after the dialog hit
  ~850 px with General as a grab bag; a left-nav "option B" is the plan
  if it outgrows a screen again). Single column, no tabs — deliberate,
  avoids theming stock TabControl chrome. Title and buttons are docked
  outside a ScrollViewer and `MaxHeight` = 92% of the work area, so a
  small screen scrolls the sections instead of losing the buttons. Slider
  rows share a 140 px label width (`SliderLabel`) so every slider starts
  on the same x; a checkbox that owns a slider sits on the slider's row
  ("Fade idle panels to [slider]", slider IsEnabled bound to the box).
  Cookie box is a replace-inbox: empty = keep the
  current cookie; once a cookie is stored the box is folded behind a
  "▸ Replace cookie…" link (`CookiePanel`; folding it clears the box so a
  hidden paste can't be saved) and the hint line hides while empty; first
  run shows the box + walkthrough up front and gates Save on a valid paste (`Clean()` strips
  `cookie:` prefix, quotes, newlines, trailing `;`; live validation needs
  `connect.sid`, warns if `cf_clearance` missing). Five hotkey capture boxes
  (edit / hide-overlay / minimap-view / heatmap / Check Prime) share the capture UX: combos are
  availability-tested via a throwaway RegisterHotKey on the dialog's hwnd
  and cross-duplicates rejected. MainWindow suspends its five
  registrations for the dialog's lifetime (WM_HOTKEY is system-level and
  would fire behind the modal dialog; suspension also lets the boxes see
  and reassign our own combos) and restores them in a finally on close.
  The Minimap section holds size, view mode, centered zoom, the trail
  length (radio buttons, not a ComboBox — stock ComboBox chrome is light
  and ignores Background) and the scale-bar checkbox — NOT the
  heatmap on/off (removed Sep 2026: something you flip is not a preference,
  see the surface rules under Conventions).
  Save writes config + Run key and sets Cookie/Hotkey/Minimap/Appearance
  Changed flags; MainWindow hot-applies each (RebuildClient / re-register
  with fallback / minimap ApplySettings / ApplyAppearance)
  — no restart, ever. General = Start with Windows, the not-in-game
  auto-hide checkbox (no flag: MainWindow reads it live), background
  opacity (30–100%) and the fade row (20–80%); Stats panel = scale
  (75–150%), the hunger/thirst time-left checkbox and the two chime
  checkboxes (low stat / growth stages — no flag, MainWindow reads them
  live); Prime tracker = scale.
  All scales, opacities, the time-left and fade settings ride the one
  Appearance flag.
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
software — verified in-game), v1.13.0 (optional activity heatmap layer
on the minimap — the site's pre-rendered image blended at its own 55%,
toggled from Settings, the control panel or the tray; approved by the
site dev Sep 15 — verified in-game), v1.13.1 (calibration pinOffset
applied to the arrow/waypoint transforms — matches the website exactly,
verified in-game), v1.14.0 (Prime tracker widget — user-triggered checks,
server-sourced rank-aware cooldown, approved Sep 19; heatmap hotkey
Ctrl+F6 replacing its tray entry and Settings checkbox; tray cut to the
lifelines; control panel grouped by widget in one row — verified in-game),
v1.15.0 (prime tracker's own size slider, `PrimeScale` seeded from
`UiScale` — every widget now sizes independently; verified in-game),
v1.16.0 (adaptive polling — 15 s, then 60 s while not in-game or
persistently failing, with user-activity nudges; time left on the hunger
and thirst bars as a fill-tip data label under 1 h, Settings checkbox —
verified in-game), v1.17.0 (minimap breadcrumb trail — age-banded,
Off/10/30/60 min, survives a relog, cleared by death/swap/teleport — and
scale bar, both computed locally; verified in-game), v1.18.0 (optional
not-in-game auto-hide with a 30 s grace; optional attention fade for
the stats panel + Prime tracker; Settings regrouped by widget with the
cookie box folded and a scroll safety net — all verified in-game).

Later/maybe: friends markers (needs permission first), zone overlays
(needs permission; the live-map bundles them as static PNGs — patrols,
sanctuaries, migrations, salt rocks), official token auth (the nudge went
out with the heatmap ask ~Sep 14 2026; the heatmap got its yes Sep 15,
tokens unanswered), Segoe Fluent Icons for stat glyphs. Velopack auto-update installer (decided Sep
2026) — TRIGGER: only if the overlay goes community-wide beyond the friend
group (e.g. posted publicly after a heatmap yes); then skip Inno entirely
and go straight to Velopack: `vpk pack` replaces the zip step in CI,
GitHub Releases stays the update feed, UpdateChecker retires.
Code-signing gets CONSIDERED at the same trigger but may well be
skipped — it is optional, not part of the decision (Sep 15 2026, after
an unknown-publisher report — unsigned releases reset SmartScreen/Smart
App Control trust on every update; until/unless signing happens, the
README's unblock-the-zip note is the answer). Ship BOTH
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
- Where a control goes (owner's rules, Sep 2026 — apply them to every new
  feature, and don't put one thing on all three surfaces by reflex):
  **Settings** = a preference: it has a value (number, text, key combo,
  choice) or is an on/off decided once; changed rarely; may need
  explanation, validation or Save/Cancel. Test: "set once and forget?"
  **Control panel** = arranging the HUD: changes what is on screen right
  now, belongs to one widget (goes in that widget's captioned group), fine
  to be unreachable while locked. Test: "part of composing my layout?"
  **Tray** = only (1) lifelines that must work while locked, hidden or with
  dead hotkeys — edit mode, hide/show overlay, settings, exit; (2) alerts
  needing a persistent home — update, hotkey conflict; (3) mid-game actions
  with no hotkey — currently none (Check Prime sat here until it earned
  Ctrl+F8 in v1.19). The tray must never grow with the number
  of widgets: no per-widget show/hide. A mid-game action used often earns a
  **hotkey** instead of a tray line (the heatmap and Check Prime did).
- Every distinct widget gets its OWN size slider in Settings (owner's rule,
  Sep 2026): stats panel `UiScale`, minimap `MinimapSize`, prime tracker
  `PrimeScale` — a new widget ships with one, seeded so an update never
  resizes anything (override `AppearanceScale`). Sliders are independent:
  no global scale multiplier on top (considered and dropped — Windows
  display scaling already does it, two multiplying sliders confuse, and
  scaling everything at once breaks docked/snapped layouts).
- Fail soft: config/crypto/HTTP errors degrade to a UI state, never crash.
- Keep files well under ~500 lines; current style is regions + XML doc comments.
- Versioning: SemVer. The csproj `<Version>` is the single source of truth;
  bump it each release and tag the commit `vX.Y.Z` (annotated). Features bump
  minor, fixes bump patch. Current: 1.18.0.
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
