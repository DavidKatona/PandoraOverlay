# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server). Shows the
player's own dino stats in an always-on-top panel, plus a minimap, tray icon,
and settings window. **v1.32.0 is built, working, and approved by the server's
web dev.**

## Hard constraints (never violate)

1. **Fully external, always.** Never read game memory, inject, hook, enumerate or
   touch the game process in any way. The Isle runs Easy Anti-Cheat. The ONLY data
   source is the islapandora.eu web API. If a feature seems to need game-side data,
   the answer is no. (One accepted edge, the owner's reading of Oct 5 2026: a
   third-party installer / updater listing all running programs to find its own
   copies, as Velopack does on a click — see "Updater TRIAL" under Roadmap. The
   overlay's own code never looks at any process.)
2. **Approved endpoints only.** `POST /api/map/mylocation` (the poll),
   `POST /api/map/calibration` (once per launch — static map-transform constants
   for the approved minimap; the live-map page itself loads it on every visit;
   added at the owner's direction, Sep 2026; since Oct 8 2026 a FAILED fetch
   is retried right after a successful `mylocation` poll, at most once a
   minute, until one succeeds — and a bundled seed stands in meanwhile),
   and the heatmap pair
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
   the website button's hardcoded 5 min over-blocks ranked players), and
   the friends roster `POST /api/map/friends` (approved, relayed by the
   owner Sep 27 2026; cookie-authed, empty body, the same call the
   live-map page sends alongside EVERY mylocation poll at 5 s — ours
   rides every SECOND in-game poll (6 s at the default cadence) and every
   idle poll, only right after a successful `mylocation` poll and only
   while a friends surface is shown: the Activity widget or the minimap
   with its friend arrows; both off = zero friends requests. READ-ONLY:
   the friends page's management calls — `/api/friends/data`, requests,
   accept/decline, block, `/api/preferences/*` toggles — are writes or
   roster admin and are NEVER called; friends are managed on the website),
   and the Patreon skins pair `POST /api/skins/patreon-skins` (the list;
   empty body) + `POST /api/skins/apply-patreon` (JSON `{skinId,
   patternIndex}` — the overlay's FIRST WRITE: it changes your dino's skin
   in game), built Oct 1 2026 under the server owner's blanket go-ahead
   (see Permissions). Both are CLICK-DRIVEN ONLY: the list when the
   Settings → Skins page is opened (reused 10 min, 30 s floor), apply for
   a click on a pattern button, never a timer; not-in-game and a 15 s
   mash guard answer locally with no request. Tile pictures are plain GETs
   to whatever address the list names, from a separate client that sends
   NO cookie and none of the site headers. The ACCOUNT pair (v1.31, the
   in-app sign-in, under the blanket go-ahead with the owner's okay, Oct 7
   2026): `POST /api/auth/me` (the "who am I" every site page sends on
   load; ours once right after a sign-in to confirm the session, and once
   when Settings → Account is looked at per dialog — never on a timer) and
   `GET /auth/logout` (the site's own logout link, once for a Sign out
   click). The sign-in itself is the website's own `/auth/discord` page
   flow, browsed by the player in the overlay's WebView2 window
   (SignInWindow): the server redirects to discord.com and back to
   `/auth/discord/callback`, and the window stops the navigation that
   follows, so no site page ever renders in it. The account's AVATAR
   (Oct 8 2026, the owner's okay) is the one picture from outside the
   site: the address auth/me names, on Discord's image server, fetched
   like a skin tile (no cookie, no site headers) only when the Account
   page is looked at and no saved copy exists — see the Account page.
   The DINO STORAGE trio (1.33, a few players' request, the owner's okay
   Oct 8 2026 under the blanket go-ahead): `POST /api/user/dinos` (the
   list, empty body; on the first look at Settings → Dino storage per
   dialog and for Refresh, never twice within 30 s) + `POST
   /api/user/dinos/rename` (JSON `{dinoId, name, description}`) + `POST
   /api/user/dinos/delete` (JSON `{dinoId}`) — two more WRITES,
   CLICK-DRIVEN ONLY (a card's rename Save, the armed Delete), one at a
   time with a 3 s breather, never a timer; see Dino storage.
   The zone overlays are NOT cleared for use (see Permissions). The
   launch-time update check calls the GitHub releases API (and, since
   v1.30, a CLICK on the update entry downloads the update package from
   GitHub) — not islapandora endpoints, so they sit outside this constraint.
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
- Approved Sep 27 2026 (relayed by the owner): **friends** — the
  `/api/map/friends` roster the live-map page polls, for friend arrows on
  the minimap and the Activity widget's feed. Consent stays the server's: a friend
  who enabled "hide my location" arrives flagged `hideLocation` and is never
  drawn. Nicknames/colours are local (the site's nicknames are
  browser-localStorage too, no API).
- Server rules (Sep 29 2026): the site has NO rules endpoint — `/rules`
  is a client-rendered page whose 17 rules and pack-limit table are baked
  into the frontend bundle (which itself says the Discord rules have
  priority "due to the website requiring updates"). The overlay ships a
  DATED COPY of that public page as `Assets/rules.json` and makes no
  request for it; scraping the bundle at runtime was rejected (fragile
  minified names, ~600 KB per launch, uncleared traffic). A `GET
  /api/rules` ask to the dev is PENDING (the owner will ask); when it
  exists it follows the calibration pattern (once per launch, cached,
  the seed only a fresh install's first content).
- **Blanket go-ahead, Oct 1 2026** (the server owner, Tar, relayed by the
  owner; the overlay is endorsed and has its own announcement channel):
  "anything the site can do, we can do too. No need to ask now." So a
  normal player-facing endpoint of islapandora.eu no longer needs a
  per-endpoint ask to the site dev — it needs the OWNER's okay for the
  feature, as always. First use: the Patreon skins page; second (Oct 7
  2026): the in-app sign-in's account pair, `auth/me` + `/auth/logout`;
  third (Oct 8 2026): the Dino storage trio. What still holds
  whatever the endpoint: fully external; the cookie is a credential;
  nothing polls faster than the site; a WRITE happens only for a click,
  never on a timer; the admin / staff endpoints (`/api/admin/*`, the
  skin-store and admin-skin editors) are never called.
- **NOT approved:** the zone overlay images
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

- .NET 8, WPF, x64. Three NuGet deps: `System.Security.Cryptography.ProtectedData`,
  since v1.30 `Velopack` (1.2.161 — the SAME version as the `vpk` packer
  the release workflow installs; bump both together) and, since v1.31,
  `Microsoft.Web.WebView2` (1.0.4258.31, the sign-in window; it needs
  Microsoft's WebView2 Runtime on the PC — part of Windows 11, on nearly
  every Windows 10 through Edge, and `vpk pack --framework
  net8.0-x64-desktop,webview2` makes the installer fetch it where missing;
  the plain zip relies on the one already there).
  WinForms interop (`UseWindowsForms`) enabled solely for the tray NotifyIcon.
  App icon `Assets/app.ico` (generated: dark rounded square + orange arrow).
- `dotnet build -c Release` → `bin/Release/net8.0-windows/PandoraOverlay.exe`.
  `PandoraOverlay.sln` at the root also carries **PandoraOverlay.Tests**
  (xUnit; `dotnet test`; CI runs it on every push). The app csproj globs the
  repo root, so the test subtree is `Compile Remove`d from it.
- `config.json`, `waypoints.json`, `friends.json` and `cache\skins` live in
  `%AppData%\PandoraOverlay` (`DataFolder`, v1.30) — next to the exe before;
  the first start copies them over from there (see DataFolder under Architecture).

## Layout (Sep 26 2026)

Files are grouped BY FEATURE, not by layer — every feature here is a widget
plus its pure helpers plus a Settings page, so that is how the folders cut:

- `Core/` — zero-WPF plumbing: PandoraClient, PollService, OverlayConfig,
  HotkeySpec, StartupRegistration, UpdateChecker, DataFolder (where the
  user's files live, v1.30) and Updater (Velopack, v1.30 — the one Core
  file with a package dependency besides DPAPI).
- `Account/` — the in-app sign-in (v1.31): SignInWindow (the WebView2
  window, the one Shell-side file with a package dependency) and
  SignInPolicy (its pure rules).
- `Shell/` — what every widget stands on: OverlayWindowBase, WidgetFrame,
  DefaultLayout, SnapResolver,
  SnapGuideWindow, ControlPanelWindow, TrayIcon, and MainWindow (the
  orchestrator, which is also the stats panel — splitting the panel out
  into its own window is a possible later refactor).
- `Stats/` — GrowthTracker, DrainTracker, StaminaTracker, DamageTracker,
  StatsAttention, LowStatAlert, GrowthMilestones.
- `Minimap/` — MinimapWindow, BreadcrumbTrail, ScaleBar, SpeedTracker, Compass,
  AreaMap (+ AreaReadout, AreaJournal), AreaBorders and AreaMapAsset (its PNG loader).
- `Waypoints/` — WaypointLibrary, WaypointPacks, ShareCode.
- `Friends/` — FriendBook (+ FriendColour), FriendFeed.
- `Activity/` — ActivityWindow, ActivityLog, SelfActivity (the widget is
  the feed of everyone's events; the friends folder holds the friend half).
- `Prime/` — PrimeWindow, PrimeConditions. `Rules/` — ServerRules (+
  `Assets/rules.json`, the seed). `Skins/` — PatreonSkins (the pure half;
  its requests and gating are the `Core/PandoraClient.Skins.cs` and
  `Core/PollService.Skins.cs` parts). `Storage/` (1.33) — StoredDinos
  (the pure half of Dino storage; its requests and gating are the
  `Core/PandoraClient.Storage.cs` and `Core/PollService.Storage.cs`
  parts). `Settings/` — SettingsWindow.
  `Updates/` (1.32) — UpdateCardWindow (the update card), ReleaseNotes
  and UpdateCardPolicy (its pure halves); the updater itself stays in
  `Core/Updater.cs`.
- `Assets/` unchanged; `App.xaml` (StartupUri now `Shell/MainWindow.xaml`),
  `Program.cs` (the entry point since v1.30: Velopack's start-up call, then
  WPF — `<StartupObject>` in the csproj), csproj, sln and the docs stay at
  the root. `PandoraOverlay.Tests/` mirrors
  the folders.
- `packs/` — DATA, not code (Sep 30 2026): five ready-made waypoint packs
  users download and import (`gateway-areas` 26, `gateway-water` 27,
  `gateway-landmarks` 27 = landmarks + human sites + tunnels; added Oct 1
  on a player's request: `gateway-mud` 18 — one waypoint per pool, the
  centre of each circle / outline in the source, numbered where a record
  holds two — and `gateway-saltrocks` 24 — bare points in the source, so
  each is named "Salt: <nearest named place>") and their
  own README with the import walkthrough (`docs/waypoint-import*.png`).
  Not built into the app and not in the release zip. Converted once from
  VulnonaMAP's Gateway label data (vulnona.com, community-made): world cm
  = its Lat/Long × 1000, Long → X, Lat → Y — the game's own coordinates,
  checked against hand-placed waypoints and a plot on `Assets/map.png`.
  Ids are deterministic (MD5 of "vulnona-gateway/<pack>/<record name>"),
  so a regenerated pack re-imports as duplicates. Owner's call: a credit
  line in each file (`Source`, `CopiedOn` — extra JSON the importer
  ignores) and in the README is enough. A dated snapshot like
  `rules.json`; the overlay never contacts vulnona.com. Left out: fence
  gates, caves, air currents, the other food spawns and the zone shapes.
  These packs are user-imported waypoints from a community map — NOT the
  site's zone-overlay images (salt rocks included), which stay unapproved.
- `tools/area-map/` — NOT part of the app (Oct 2 2026): the PowerShell
  generator of the area map (`make-area-map.ps1`, Windows PowerShell 5.1
  with inline C#) and its input `area-labels.json` (the site's map
  calibration, and one label point per area in world cm with a size hint
  and a sea flag — today VulnonaMAP's Gateway labels, credited). It
  writes `Assets/areas.png` + `Assets/areas.json` and, with
  `-PreviewPath`, the picture for people (`docs/area-map.jpg`). A STARTING POINT, not a
  build step: corrections are painted into `Assets/areas.png`, and a
  re-run overwrites them. THE BUNDLED MAP IS HAND-CORRECTED since Oct 5
  2026 (the owner painted Central Dome larger to the south-east, ~2,000
  px, nearly all from Swamps) — so DON'T re-run the generator over it
  without the owner's word. The picture is drawn by its own script,
  `preview-area-map.ps1` (Oct 5 2026): it only READS `Assets/map.png`,
  `areas.png` and `areas.json` and writes `docs/area-map.jpg`, so it is
  what to run after painting; the generator's `-PreviewPath` calls the
  same script (one copy of the drawing code). `-FullSize`, `-Title` and
  `-Hex` are for a one-off picture such as a Discord post. See AreaMap
  under Architecture.
  **RULE (owner, Oct 2 2026): NOTHING IS WRITTEN FOR ONE NAMED AREA.**
  The generator, the overlay's area code and the tests treat every area
  alike, and everything about a particular map is INPUT — so a new map
  means: replace `Assets/map.png`, replace `area-labels.json`, run the
  script, done. No rule, exception or test may name an area ("Spiky
  Isle", "Highland") or count on this island (26 areas, 1000 px, sea in
  the corner, how much land there is). That is why the calibration lives
  in the input file and is copied into `areas.json` (the tests read it
  from there), distances are in metres and turned into pixels by the
  calibration, colours beyond the 26 hand-picked ones are generated, and
  the bundled-map tests check properties any generated map has: only
  legend colours, every label point inside its own area, borders that
  are lines and not fills. The one number that is about the map
  PICTURE's style, not the island, stays in the generator and is named
  as such: the sea colour tolerance.

The namespace stays ONE flat `PandoraOverlay` on purpose (35 files don't
earn sub-namespaces; `.editorconfig` silences IDE0130). The SDK-style csproj
globs subfolders, so moving a file needs no project edit; pack URIs point at
`Assets/`, which didn't move. Big windows are PARTIAL CLASSES split by
concern, the way WPF already splits them from their generated `.g.cs`:
`MainWindow.xaml.cs` (+ `.Hotkeys.cs`, `.Visibility.cs`, `.Updates.cs`),
`MinimapWindow.xaml.cs` (+ `.Menu.cs`, `.Markers.cs`, `.Friends.cs`, `.Area.cs`),
`SettingsWindow.xaml.cs` (+ `.Waypoints.cs`, `.Friends.cs`, `.Rules.cs`,
`.Skins.cs`, `.SkinPictures.cs`, `.About.cs`, `.Account.cs`, `.Storage.cs`, `.StorageDetail.cs`) and, since v1.28, the two Core classes that had reached the
limit: `PandoraClient.cs` + `.Skins.cs` + `.Account.cs` + `.Storage.cs`, `PollService.cs` + `.Skins.cs` + `.Calibration.cs` + `.Storage.cs`. Same class, same
fields, no behaviour change — a reading aid, not decoupling; the pure
helper classes are the real decoupling. Keep each part under ~500 lines;
when one outgrows that, cut another `Window.Topic.cs`, don't extract a
"manager" class by reflex.

## Architecture

Dependency rule: `MainWindow` → { `PollService`, `OverlayConfig`,
`WaypointLibrary` } and `PollService` → `PandoraClient`.
`PandoraClient`/`OverlayConfig`/`WaypointLibrary` have zero WPF references —
keep it that way. Windows receive the library like the config (pure
consumers; the minimap redraws on its `Changed`, MainWindow saves on it).
Every overlay window derives from `OverlayWindowBase`.

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
  `IsIdling`/`Interval` feed the status line. Calibration
  (`PollService.Calibration.cs`, Oct 8 2026): fetched once per launch (and
  again after a credential swap) and cached into config; until the site's
  answer arrives, `Calibration` is the cached copy, else the bundled seed
  `Assets/calibration.json` (a dated copy of the site's answer in its own
  shape, EmbeddedResource like `rules.json`, read through `FindCalibration`;
  a test holds it equal to `areas.json`'s calibration). A FAILED fetch is
  retried after a successful poll, gated by the pure, tested
  `CalibrationDue` (not yet fetched, none in flight, 60 s since the last
  try). Before, the one launch-time attempt was all: a failure (Start with
  Windows before the network, a blip) left a fresh install's minimap
  "waiting for map calibration…" for the whole session while the heatmap
  beside it recovered — reported by a player Oct 8 2026. A
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
  and caches an Ok snapshot into `config.Prime`. Friends (Sep 27 2026):
  `MaybeFetchFriendsAsync` runs at the END of every successful poll,
  inside the same busy guard, and fetches the roster when the pure, tested
  `FriendsDue(pollsSinceFriends, idling, hotTrigger)` says so — every
  `FriendsEveryNthPoll` (2) live polls, every idle poll, or once after a
  hot trigger; gated on `FriendsWanted` (`ActivityEnabled &&
  ActivityIncludeFriends`, or `MinimapEnabled && FriendsOnMinimap` — a
  solo player with both off sends no friends requests at all). Raises
  `FriendsChanged(IReadOnlyList<FriendState>?)` AFTER `SnapshotReceived`
  (consumers know your position first) with YOU filtered out by steamId;
  `Friends` keeps the last roster so a window created mid-session renders
  at once. One miss keeps the last roster, two clear it (null) so nobody
  navigates by a stale arrow; the gate turning off clears at once.
  `RefreshFriendsAsync` is the hot-apply path (widget shown, minimap
  shown, settings saved): fetches now only if the timer runs, nothing is
  in flight, the connection is healthy and the last roster is older than
  one poll interval — else it just marks the next poll due, so toggling
  the widget can't become a request per click. A failed `mylocation`
  poll never triggers a friends fetch. SIGNED OUT (v1.31):
  `PandoraClient.FetchAsync` turns the site's own "no session" answer
  (401/403 WITH a JSON body — a Cloudflare block is a 403 with HTML and
  stays an ordinary failure; pure `IsRefusal`, verified Oct 7 2026: a dead
  or missing cookie gets `403 {"error":"Forbidden"}`) into
  `SessionEndedException`; two in a row (`SignedOutAfter`, pure) set
  `IsSignedOut`, STOP the timers (a dead cookie answers 403 forever — no
  retry every minute for good) and raise `SignedOut`; the next
  `RebuildClient` starts over. The sign-out path: `FetchAccountAsync`
  (one auth/me for the Account page's first look), `SignOutAsync` (stop
  FIRST, so the dying cookie's refusals don't read as "session ended",
  then the site's logout once) and `ForgetSession` (a client with no
  cookie, timers stopped, roster and skins forgotten, so `CurrentCookie`
  is empty and the exit save keeps nothing).
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
- **WidgetFrame.cs** — the two fixed frame sizes (v1.25, owner's design,
  Sep 29 2026): every widget is `Width` 298 outer; the minimap and the
  Prime tracker share `LargeHeight` 318 (set by the map: a 284 px square
  = 298 − 6 px padding − 1 px border each side, plus its footer), the
  stats panel and the Activity feed share `SmallHeight` 168 (set by the
  stats panel with its fracture row permanent). Each window's root Border
  takes them as its explicit size (`x:Static` in XAML) and the content
  lays out INSIDE with DockPanel + stretching Grid rows — so "a widget
  never changes size" is structural, and twins line up in the two-column
  layout. Per-widget scale sliders multiply the whole frame. THE ONE-TIME
  EXCEPTION to the never-resize rule, accepted by the owner: 1.25
  resizes every existing widget once (Prime wider + taller, Activity
  wider, stats taller by the fracture row, minimap to the nearest
  percent); top-left anchored, the startup clamp handles overflow, tight
  stacks may overlap a few px → the coming Reset positions button. Never
  again.
- **DefaultLayout.cs** — pure, tested: where widgets go with no saved
  position. One preset, `Columns(screen, prime, activity, minimap,
  stats)` → the four top-left points: Prime top-left at the 16 px
  `Inset` with Activity `Gap` 8 below; minimap top-right with the stats
  panel below, right edges aligned; the TOP inset is `TopFraction` 10%
  of the screen height (floor 16 px — 108 px at 1080p) because the game's
  own HUD owns the top strip (version/FPS/recording camera left, ping/FPS
  right, ~85 px at 1080p, scaling with resolution; owner, Sep 29 2026,
  from a screenshot) and vertical centring was rejected as it would run
  into the game's bottom-right stat hexes on short screens; sizes passed in are the windows'
  ACTUAL (scaled) sizes. Written preset-shaped so a second layout is one
  more method (owner, Sep 29 2026: no presets yet). Wired through
  `MainWindow.FillDefaultPositions`: at startup, BEFORE the other windows
  are created, every null position (`WindowX/Y`, `MinimapX/Y`, `PrimeX/Y`,
  `ActivityX/Y` — all nullable since v1.25; configs from before hold
  numbers and are left alone) is filled INTO config from the frames × each
  widget's scale, not from live windows, so a hidden widget leaves its
  gap and the arrangement is the same whichever widgets are shown; the
  windows then read config as always (their old first-show fallbacks
  only guard a hand-edited config). `ResetPositions` (Settings → General
  → "Reset positions", armed in the dialog and applied on Save like
  everything else) nulls all six positions, refills them and moves the
  live windows; the control panel goes back to bottom-centre
  (`PlaceDefault`). Positions ONLY, never scales.
- **SnapResolver.cs** — pure, tested snapping math: screen edges + 16px
  inset + peer edges, 12px threshold (threshold < inset on purpose, so the
  two magnets read as distinct stops), axes independent; leading- and
  trailing-edge candidates per target give align-and-abut for free. Returns
  a `SnapResult` (position + per-axis `SnapGuide` naming the engaged target
  and whether it was a peer) so the caller can draw guides.
- **ControlPanelWindow.xaml(.cs)** — the edit-mode control panel (v1.9):
  appears with edit mode, hides on lock; ONE row grouped by widget (owner's
  call, Sep 2026 — a two-row layout was tried and rejected): Settings |
  STATS: Show/hide, View | MINIMAP: Show/hide, Map view, Heatmap, Areas | PRIME:
  Show/hide, Check | ACTIVITY: Show/hide | Lock / Exit — small captions over each group keep
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
  `FetchFriendsAsync` / `ParseFriends` (pure + tested) read the friends
  roster into `FriendState` records — your own player record's fields plus
  `InGame`, `HideLocation` and NULLABLE `X`/`Y` (the site null-checks them
  too); `OnMap` = in game && !hideLocation && both coordinates, the site's
  own draw rule. Entries without a steamId or unparseable are dropped, a
  missing list is an empty roster, `success:false` is null (a miss).
  `PandoraClient.Account.cs` (v1.31): `FetchAccountAsync` / `ParseAccount`
  (pure + tested — authenticated, username, steamId, the Steam LinkID,
  hasMapAccess, isVerified, into `AccountInfo`; a `user` object alone
  counts as signed in, like the frontend; a refusal reads as
  `AccountInfo.None`; Discord ids and roles are left out) — plus the
  avatar's address through the pure, tested `AvatarAddress` (https on
  Discord's hosts or islapandora.eu only, anything else is null; a
  Discord address without a size asks for `AvatarSize` 128) and
  `SignOutAsync` (the site's logout GET on its own no-redirect client —
  the answer is a redirect to the home page, not worth a download).
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
- **StaminaTracker.cs** — pure, tested (v1.27, a player's request): the
  stamina trend as a time, BOTH ways — "~25s" until empty while it falls
  poll after poll, "full ~40s" until full while it climbs. Recovery time
  is unique to stamina on purpose: it refills by itself at a steady
  pace, where hunger/thirst refill in bites the player controls and have
  no rate to extrapolate. A reading needs two consistent steps (three
  polls, ~6 s into a sprint), follows the last six samples (~15 s), and
  ends on one flat poll (< 0.004), a reversal (the new run starts at the
  previous sample), a gap > 8 s (idle polling) or a new dino; nothing at
  the ends (≤ 0.5% to go) or beyond 5 min. "~Ns" under 100 s, "~Nm"
  above. The number is up to one poll old — the interval floor
  (constraint #3) means that can't improve. Information ONLY: no pulse,
  fade wake or chime (stamina drains by design). Rendered by
  `MainWindow.SetBarLabel` on the stamina bar of both views, gated by the
  same `StatTimeLeftEnabled` checkbox as hunger/thirst.
- **DamageTracker.cs** — pure, tested (v1.27): health lost in the current
  fight, for the combat view's Damage row. Every drop > 0.002 between
  polls adds to `Total` (bleed counts, regeneration is never subtracted,
  so a long fight can pass 100%); 30 s without a hit ends the fight and
  clears it; death/dino swap/not-in-game reset. A readout, not a combat
  log: one reading per poll, so a hit healed within the same poll is
  invisible and WHO hit you is unknowable (no such data in the API) —
  don't claim either in user-facing text. Separate from StatsAttention's
  damage cue (fade) and SelfActivity's damage line (feed) on purpose:
  each has its own threshold for its own job.
- **StatsAttention.cs** — pure, tested wake/calm rule for the stats
  panel's fade, ONE RULESET PER VIEW (v1.27) on the principle "lit means
  something ON this panel is worth a look" — before, low thirst lit a
  combat panel that doesn't show thirst. `Update(p, hungerLeft,
  thirstLeft, combatView, inFight)` branches into two private rules that
  share the identity and the damage baseline (one class, not two, so a
  flip mid-fight loses nothing). A FLIP ALWAYS LIGHTS the panel for 10 s
  (owner's call: it is a change to the panel itself) — `ApplyStatsView`
  tells a flip from a re-apply (`_shownCombat`), calls `NoteViewFlip` and
  lights the panel at once, whichever path flipped it (hotkey, control
  panel, Settings save); `FlipHeld` covers not-in-game, where `Update`
  never runs. After the hold the new view decides AFRESH — a value
  between its lines reads as calm instead of inheriting the hold or the
  other view's verdict. SURVIVAL (unchanged): wake on health/hunger/thirst < 50%, any fracture, damage
  (health down > 0.005 between polls, held 10 s — one poll's drop is
  momentary) or a drain estimate under 15 min; calm only above 55% / 20
  min with none of the rest, so a stat at the line can't blink; stamina
  ignored. COMBAT: wake on health OR stamina < 75%, calm above 80%
  (owner's numbers, Sep 30 2026 — stamina is that view's subject and a
  chase has no hits; the 75% health line means a wounded dino keeps the
  panel lit while it heals, accepted), any fracture, and `inFight` =
  `DamageTracker.InFight`, so the panel is lit exactly as long as the
  Damage row shows something (30 s after the last hit) instead of a
  second timer that could disagree with it; hunger, thirst, their
  estimates and `NoteEvent` (growth readout hidden) do not wake it.
  Always-lit-in-combat was considered and dropped: combat can be the
  default view, and that would silently switch the fade off. Resets
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
  owner's call, a custom sound only if users ask). `HungerFired` /
  `ThirstFired` say which stat the last Update fired for, so the Activity
  feed can name it.
- **GrowthMilestones.cs** — pure, tested: reports the stage line crossed
  by a sample (25 juvenile / 50 subadult / 75 adult / 100 elder, the last
  at GrowthTracker's 0.9995 full line); the first sample of a life is only
  a baseline, a gap over two lines reports the higher, death/swap resets.
  MainWindow always `PulseBriefly`s the growth header + `NoteEvent`s the
  fade for it; the chime is gated by `GrowthChimeEnabled` (default off).
  `StageAt(growth)` (1.33) names the stage a value sits in by the same
  lines — "hatchling" under 25% — for the Dino storage cards;
  `FullyGrown` (0.9995) is GrowthTracker's full-grown line, named.
- **OverlayConfig.cs** — config.json persistence + DPAPI vault. Plaintext `Cookie`
  field is a paste-inbox only: `Load()` encrypts it into `CookieProtected`
  (`DataProtectionScope.CurrentUser`) and blanks it. `GetCookie()` returns "" on
  any failure; nothing in this class ever throws. Since v1.31 the inbox
  is the UNDOCUMENTED EMERGENCY ROUTE — the UI has no paste box — and
  `ApplySignIn` (cookie encrypted, the sign-in browser's UA copied into
  `UserAgent`, `AccountName`, `SignedInUtc`; tested) / `ClearSignIn` are
  what a sign-in's outcome and a Sign out do.
- **MainWindow.xaml(.cs)** — orchestrator: owns the config, the PollService,
  the WaypointLibrary (loaded at startup; `MigrateWaypointSlots` turns
  v1.20's three config slots into Blue/Green/Purple entries once and
  blanks them; saved on every `Changed`, not just on exit — it is user
  content), the FriendBook (loaded at startup; `Sync`ed from every roster
  in `OnFriendsRoster` BEFORE the windows see it, since MainWindow
  subscribes to `FriendsChanged` first; saved on `Changed` and flushed in
  `PersistState` for the silent last-seen facts), the ActivityLog + the
  FriendFeed + SelfActivity (MainWindow, not the widget, owns them so the
  last ten minutes survive the widget being hidden: `OnFriendsRoster`
  posts the feed's diff lines, `OnSnapshot` posts `SelfActivity.Update`'s
  spawn/fracture/damage lines and keeps `_me` for the proximity rule,
  `UpdateUi` posts the growth-stage and low-stat lines beside the blink
  and chime they already had, `OnFriendsRoster` also plays the opt-in
  friend-spawn chime, and `PrimeChecked` posts the Prime diff
  against `_primeBefore` snapshotted on `PrimeCheckStarted`; every own
  line is always posted — the feed is yours; friend lines are posted
  only while `ActivityIncludeFriends` is on, the diff running regardless
  so the baseline is right when they are switched back on), and
  the minimap + prime + activity windows' lifetimes (all follow edit mode
  and hide-all; `CheckPrime` un-hides and shows the prime widget first,
  then fires the one user-triggered check; `ShowActivity` runs after
  `ShowPrime` at startup and hands the new widget a suggested spot right
  under the Prime tracker; every activity show/hide calls
  `RefreshFriendsAsync` so the fetch gate follows the surfaces). Stats
  views (v1.27, a player's request for a PvP focus): `StatsView`
  "survival" (health, stamina, hunger, thirst + growth; anything that is
  not "combat" reads as survival, so the first build's "full" still
  loads) or "combat" — the SAME four rows at the same sizes showing
  different information: health, stamina, Damage (`DamageTracker`: this
  fight's total as a red bar that grows, the percent uncapped) and Speed
  (a plain "N km/h" from a second, short-window `SpeedTracker` — NO bar:
  a dino's top speed changes with growth, so no full mark would be true;
  owner's call); the header's corner swaps the growth readout for
  `ConditionText` — "Wounded" under 50% health (the GAME's keyword and
  line, `WoundedBelow`, the same line the health bar turns amber on; red
  under 25% like the bar) or a quiet grey "Healthy" (OUR word for
  not-wounded, so the slot always answers); the status line kept —
  staleness matters most in a fight. The footer names the view at its
  right end, lowercase like the minimap's ("survival view" / "combat
  view", `ViewText`): every status goes through `SetStatus`, whose
  `FitViewName` hides the name (Hidden, not Collapsed) whenever the
  status needs the room — the first-run hint, hotkey notices and the
  Disconnected line do; Live / Connected / Update lines don't. Never
  trim a status for it. Two StackPanels (`FullView` = Survival /
  `CombatView`) inside the SAME small frame, both kept up to date per
  poll so `ToggleStatsView` (hotkey, control panel "View") is instant and
  moves nothing; `ApplyStatsView` also runs after every Settings save. A
  shorter panel was the literal ask and was turned down for the frame's
  sake; a first build that drew health and stamina LARGE (24 px bars, big
  percents, bigger badges) was REJECTED on sight by the owner — size is
  not how this overlay emphasises anything, don't reintroduce it;
  automatic switching on damage was rejected too (content changing by
  itself is a surprise). Six global hotkeys (RegisterHotKey +
  WM_HOTKEY in WndProc; control-panel/tray labels follow config): edit mode
  (`Hotkey`, Ctrl+F7) toggling every window, hide/show overlay
  (`HotkeyHideAll`, Ctrl+F4 — exits edit mode first; hidden never persists;
  the edit hotkey un-hides first), minimap view toggle
  (`HotkeyMinimapView`, Ctrl+F5 → `MinimapWindow.ToggleView`), and the
  heatmap toggle (`HotkeyHeatmap`, Ctrl+F6 → `ToggleHeatmap`, a no-op while
  the minimap is hidden; it replaced the tray entry and the Settings
  checkbox), Check Prime (`HotkeyPrimeCheck`, Ctrl+F8 → `CheckPrime`;
  v1.19, replacing the tray's "Check Prime status" line) and the stats
  view toggle (`HotkeyStatsView`, Ctrl+F9 → `ToggleStatsView`; v1.27,
  candidates F9/F11/F12/F8/F6/Ctrl+Shift+F9). The heatmap, Prime and
  stats-view keys arrived after users had customized the earlier ones, so
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
  the manual hide. Every widget is scaled in percent through the same
  LayoutTransform: `UiScale` (stats + control panel), `MinimapScale`
  (v1.25 — before that the minimap was the one widget sized in pixels;
  the pixel key `MinimapSize` in old files is ignored since Oct 6 2026,
  see Conventions), `PrimeScale`, `ActivityScale`. The stats panel sits on the SMALL frame
  (WidgetFrame): DockPanel with the header on top, the status line and
  the fracture row docked at the bottom, the bars filling. `UpdateUi` is a 3-state machine:
  not-set-up / not-in-game / live (health bar recolors at <50% amber, <25% red;
  the three fracture badges are ALWAYS drawn — `SetFractures`: dim when
  intact, lit when fractured, like the site's icons, so a fracture can
  never resize the panel (v1.25; before, the row collapsed and the panel
  grew on the first fracture); health/hunger/thirst fills pulse below 25% — stamina
  deliberately excluded, it drains by design). Fires one `UpdateChecker` call
  on Loaded, feeding the status line + tray. THE SESSION (v1.31):
  `ApplySession` runs whenever Settings reports `CookieChanged` — read
  BEFORE the saved/cancelled branch, since a sign-in acts at once — or the
  tray's sign-in finished: a stored session rebuilds the client, none
  calls `ForgetSession`, zeroes the panel and shows the not-signed-in
  state; `OnSignedOut` (PollService gave up on the cookie) says "Session
  ended · sign in again from the tray" and reveals the tray entry.
- **TrayIcon.cs** — WinForms NotifyIcon wrapper owned by MainWindow: the only
  always-visible affordance (windows are click-through, no taskbar/Alt-Tab).
  Right-click menu, deliberately short = edit mode / hide-show overlay |
  settings / server rules / exit (hotkey labels follow config) — no
  per-widget toggles, and no Check Prime since v1.19 (it earned Ctrl+F8);
  "Server rules…" (Sep 29 2026) is the one hotkey-less mid-game action
  and opens Settings on the Rules page, see the surface
  rules under Conventions;
  double-click = edit mode. Hover tooltip shows live stats (`SetStatus`,
  127-char NotifyIcon cap); the Edit mode entry's hotkey label follows config.
  `ShowUpdateAvailable(tag, label, onClick)` reveals a hidden menu entry
  whose text and click are the caller's — "Update to vX and restart"
  installing it (v1.30), or "open download page" where this copy can't
  update itself — and appends the tag to the tooltip; `SetUpdateBusy`
  greys it while the download runs; `ShowHotkeyConflict`/`ClearHotkeyConflict`
  do the same for combos another app holds (entry opens Settings) — the
  status-line warning alone is overwritten by the next poll.
  `ShowSignedOut`/`ClearSignedOut` (v1.31) are the third alert: "Sign in
  again…" opens the sign-in window straight away (`MainWindow.OpenSignIn`),
  no Settings in between. Disposed on shutdown.
- **UpdateChecker.cs** — one fail-soft GET to the GitHub releases API
  (`/releases/latest`, so pre-releases never count); a newer tag surfaces
  via the status line (once) and the tray (for the session). Never
  re-checks, never pops anything up; offline/errors read as "no update".
  Since v1.30 it is the FALLBACK for a copy Velopack didn't put on the PC
  (the plain zip, a build from the IDE) — everything else goes through
  Updater.
- **Updater.cs** (v1.30) — Velopack's `UpdateManager` against this repo's
  GitHub releases (`GithubSource`), for a copy Setup.exe installed
  (`%LocalAppData%\PandoraOverlay\current`) or the self-updating zip
  unpacked (`.portable` marker beside `Update.exe`); `CanUpdate` is
  Velopack's `IsInstalled`, `Flavour` says installed / zip / plain
  folder for the Settings page. `CheckAsync` is one GitHub request (the
  release list, then the `releases.win.json` asset), `DownloadAsync`
  fetches the package (the small delta where one exists) into Velopack's
  `packages` folder, `ApplyOnExit` hands it to `Update.exe`, which waits
  for the process to exit, swaps `current` and starts the overlay again
  — so MainWindow leaves through its NORMAL exit path and saves state
  first (`WaitExitThenApplyUpdates`, not `ApplyUpdatesAndRestart`). A
  pre-release build (SemVer "-rc.1", read off the informational version,
  pure `AcceptsPreReleases`) also sees pre-releases: testers on rc.1 are
  offered rc.2, everyone else only full releases. Fail soft: `Note` holds
  an exception's type name at most. NOTHING HERE RUNS ON A TIMER: the
  launch check is gated by `UpdateCheckEnabled` (default on; off = no
  GitHub request at all), the download and the restart happen only for a
  click (the tray entry, or Settings → About's button) — the update
  policy decided Sep 2026, see Roadmap. `MainWindow.Updates.cs` is the
  glue: `CheckForUpdateAsync` at Loaded, `OfferUpdate` (status line +
  tray), `ApplyUpdateAsync` (busy tray entry, "Downloading the update…",
  then `Shutdown()`; a failure re-offers), `UpdateHooks` for Settings
  (version, flavour, a check and an apply). The entry point is
  `Program.Main`: `VelopackApp.Build().Run()` FIRST — it answers the
  installer's / updater's hook calls into the exe (`--veloapp-install`,
  `-updated`, `-obsolete`, `-uninstall`) and exits for those, so no
  window or poll ever starts during an update — then WPF as the generated
  `App.Main` would. The trial that preceded this and the owner's reading
  of hard constraint 1 for Velopack's process scan are under Roadmap.
  Since 1.32 `PendingNotes` hands over the found release's notes
  (`NotesMarkdown`, from the feed the check already read) and
  `DownloadAsync(progress)` reports 0–100; `ApplyUpdateAsync(progress)`
  keeps ONE task (`_applying`), so the tray, the card and Settings join
  a running download instead of starting another.
- **UpdateCardWindow.xaml(.cs)** (1.32, `Updates/`, the owner's design of
  Oct 8 2026 — sketches and PLAN.md in `Desktop\Pandora Overlay
  Files\pandora-update-card`) — the overlay's own centred card, chosen
  over a tray balloon (Windows can snooze or swallow those). Settings'
  frame and button looks kept local like SignInWindow's; FIXED size
  (460 wide, the notes box scrolls past 200 px — owner's call); topmost,
  not modal, not in the taskbar, not part of edit mode; ✕ and Escape =
  Later. Three modes: AVAILABLE (version, "You have vX", What's new,
  Later / Install and restart — the very same `ApplyUpdateAsync` as the
  tray entry, then download progress until the overlay exits; a failure
  brings the buttons back as "Try again"), UPDATED ("Updated ✓", "you
  were on vY" when known, the notes, OK) and NOTES (Settings → About's
  "What's new": the running version's notes, OK, modal over Settings).
  RULES (`UpdateCardPolicy`, pure, tested; owner, Oct 8 2026): the
  available card comes ONLY from the launch check (off = no card) — at
  launch, a conscious act, so no in-game rule, no grace, no slow-check
  guard (all considered and dropped) — never on a first run (no stored
  session: Settings opens on Account), once per version
  (`config.UpdateCardShownFor`, recorded when shown, so Later is final
  for that version), and only where this copy can install (a plain
  folder — today only an IDE build — keeps the tray notice). The updated
  card compares the running version with `config.LastRunVersion` (a
  VERSION COMPARISON, not Velopack's restart hook: it also catches
  Setup.exe run over an old install and a zip unpacked over an old
  folder); a config from before 1.32 has none, so the first 1.32 start
  shows the card without "you were on"; a first run, the same version
  or a downgrade show nothing. One card per launch: an updated card
  wins and the tray still offers the newer version. `Compare` is a small
  SemVer ordering (pre-releases below their release, numeric labels
  numerically, build metadata ignored). Nothing goes to the Activity
  feed (the owner: it stays free of chores). The tray entry, its
  tooltip and the About page stay the update's permanent home.
- **ReleaseNotes.cs** (1.32, `Updates/`, pure, tested) — the card's
  What's new. `SectionFor(changelog, version)`: the text from
  `## [X.Y.Z]` to the next `## `, and for a pre-release the
  `## [Unreleased]` section (the CHANGELOG stays there until the final)
  — the release workflow's "Release notes" step cuts the same section
  in pwsh for `vpk pack --releaseNotes` and the GitHub release text.
  `Parse`: `### Added` groups, `- ` bullets, indented continuation
  lines, backticks / bold dropped, a markdown link keeps its words;
  tolerant. The updated card and About's What's new read the CHANGELOG
  bundled with the app (EmbeddedResource `CHANGELOG.md`, like
  rules.json): no request, every flavour; a test holds that the bundled
  CHANGELOG has notes for the version being built. CHANGELOG ENTRIES
  ARE PLAYER-FACING TEXT since 1.32: the card shows them.
- **DataFolder.cs** (v1.30) — where the user's files live:
  `%AppData%\PandoraOverlay` (Roaming, as Velopack's docs suggest for
  files that must survive an uninstall; the install itself is under
  `%LocalAppData%`), created on first use. `OverlayConfig.FilePath`,
  `WaypointLibrary.FilePath`, `FriendBook.FilePath` and
  `SkinThumbnails.DefaultFolder` all resolve through it. The MOVE from
  next-to-exe (1.29 and before): `App.OnStartup` calls `MigrateLegacy()`
  before MainWindow loads anything — only while the new folder has NO
  config.json, it COPIES config / waypoints / friends (+ the cache) from
  the nearest of three candidates: next to the exe (the plain zip
  unpacked over the old folder), one level up (the self-updating zip's
  `current\` inside the old folder), the folder of the exe the Run key
  points at (an old copy elsewhere, when Setup.exe put this one under
  AppData). Never a move, never a delete: the old folder keeps working
  as it was, and the user removes it. Settings → About's "Import from
  an older copy…" (`ImportFrom`, an `OpenFolderDialog`) is the safety net
  for a fresh install that found nothing; it REPLACES what is in the
  folder, so the page then disables Save (which would write the stale
  in-memory config straight back) and asks for a restart. Pure IO, zero
  WPF, tested with temp folders (`ImportIfEmpty`, `Import`); messages
  name an exception's type at most. `StartupRegistration.RepointToThisExe`
  (at Loaded) rewrites a Run entry that points at another exe, so an old
  copy's "Start with Windows" doesn't keep starting the old copy.
  `BrowserFolder` (v1.31) is the sign-in window's WebView2 user data
  folder, `%LocalAppData%\PandoraOverlay\WebView2` — local, not roaming (a
  browser cache is nobody's content), and not WebView2's default, which
  would land next to the exe inside the folder an update replaces;
  `ClearBrowserFolder` drops it on a Sign out. `AvatarFolder` (Oct 8
  2026) is the Account card's saved avatar, `cacheccount` beside the
  skin pictures, emptied on a Sign out.
- **SignInWindow.xaml(.cs)** (v1.31, `Account/`) — the in-app sign-in:
  the website's own Discord login inside a WebView2 that browses IN
  PRIVATE (`IsInPrivateModeEnabled`: Discord's login lives in memory and
  dies with the window). The ONE dialog of ours without
  `AllowsTransparency` (WebView2 is an HwndHost: blank inside a layered
  window), so it draws its own title bar through WindowChrome. Flow
  (rehearsed as a spike Oct 7 2026 — `Desktop\Pandora Overlay
  Files\pandora-webview-sign-in\PLAN.md`): navigate to
  `SignInPolicy.StartUrl`; `NavigationStarting` holds every top-level
  navigation to the allowlist (`IsAllowed`: the site's `/auth/` and
  `/cdn-cgi/` paths, discord.com and its subdomains, about:blank — a
  clicked link elsewhere opens in the real browser, a redirect is just
  stopped; popups likewise); the first site navigation outside those
  paths (`IsSignedInLanding` — in practice the server's redirect to `/`
  after the callback) is CANCELLED, so no site page and none of its
  polling ever runs in the window, and the capture begins: the browser's
  own cookies for the site (HttpOnly included — it is the jar, not page
  script) → `CookieHeader` (connect.sid + cf_clearance if any, nothing
  else), the WebView's UA, then one `auth/me` through a throwaway
  `PandoraClient` to confirm the session and read the name (the renewed
  cookie is kept). Result screens replace the browser (HIDDEN, not
  covered — an HwndHost draws over everything): Steam linked = done,
  auto-closes in 10 s (3 s read too short in the owner's test; Done is
  there for the hurried); Steam not linked = signed in but explained, with
  the account's LinkID shown big when the site gave one (the code typed
  in game chat; never logged); failed = reason + Try again; no WebView2
  Runtime = a screen with Microsoft's download link. The address strip is
  read-only and always shows the real host: the password is typed into a
  window that is ours, and that line plus Discord's QR option are what
  make it honest. `Run(owner)` is modal (owned by Settings, or topmost
  from the tray); `Outcome` (cookie, UA, AccountInfo) survives a ✕ after
  success. Nothing here logs, shows or keeps the cookie's value.
- **SignInPolicy.cs** — the window's rules, pure + tested: `IsAllowed`,
  `IsSignedInLanding`, `StepLabel` ("Step 1 of 2 · Discord login" / "Step
  2 of 2 · Allow the website"), `CookieHeader`, `HasSession`.
- **MinimapWindow.xaml(.cs)** — bundled island map + player arrow, on the
  LARGE frame (WidgetFrame, v1.25): a Grid of the 284 px `MapSize` square
  and the footer centred in the rest; sized by `MinimapScale` through the
  same LayoutTransform as the other widgets (`AppearanceScale`), so the
  footer, scale bar and speed pill scale with the map — before v1.25 it
  was the one natively sized widget (`MinimapSize` px, migrated). All the
  map math stays in nominal MapHost coordinates; `GetPosition(MapHost)`
  already accounts for the transform. World→pixel
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
  (`MinimapEnabled`). Waypoints (v1.22, the library): every shown entry
  of `WaypointLibrary` is drawn by `RebuildMarkers` into `MarkerLayer` (a
  Canvas under the arrow) — a 6 px dot in its palette colour, the TRACKED
  one (`config.TrackedWaypointId`) a 10 px diamond with a 16 px ring; each
  marker has its own TranslateTransform so `UpdateWaypointVisual` can
  glide them with the map. Shown set = `ShownWaypoints()` per the
  `WaypointVisibility` policy (all visible / tracked only / nearest 10 —
  under "nearest" the set follows the player, rebuilt per snapshot only
  when it actually changed); the tracked one is always drawn. Centered
  view: the tracked marker edge-clamps as a direction indicator, others
  just leave the panel. `HitTest` (within `SnapRadius`, tracked wins
  ties) serves the menu and the edit-mode hover (`_hover` → the footer
  names that marker). The edit-mode right-click **map menu** (`MapMenu`,
  a WPF Popup declared as the window's Tag so it lives outside the
  layout — a popup is its own HWND, so the minimap never changes size,
  which is the owner's rule for every widget; items are built in code
  with the control panel's glow look, `MenuButton`; opened on the
  right-button RELEASE, not the press — opened on the press,
  StaysOpen=false took the matching release as an outside click and the
  menu had to be held open until the cursor reached it. Right-click
  always means "menu here": with the menu open, a right-click elsewhere
  moves it, like Explorer; left click, Escape (the panel is focused on
  open for that) or an entry closes it. A 400 ms "swallow the dismissing
  click" rule was tried and REJECTED (Sep 25 2026): whether a right-click
  closed or moved the menu depended on how long the button was held —
  don't reintroduce timing rules here. `PopupAnimation="None"` on
  purpose: Fade flickered on open (traced by elimination). KNOWN AND
  ACCEPTED (owner's call): a left click on a widget that closes the menu
  also reaches that widget's `DragIfEditing`, so the snap guides flash
  and a moving hand can nudge the widget a pixel; a swallow-the-press
  flag was designed and declined — don't add it unasked): on a marker,
  "Track/Untrack, Copy, Remove <name>" first; always "Waypoint here"
  (auto-named `NextName`, `NextColour`, auto-tracked; disabled "Library
  full (256)" at the cap), then share-a-spot: "Copy this spot" (the
  clicked point, or the marker under it exactly — "meet here"), "Copy my
  position" ("come to me"), "Paste waypoint" (a new tracked entry named
  from the code; enabled only when the clipboard holds a code). Owner
  chose the library over three coloured slots and over typed names in
  the popup; names are typed on the Settings page. Clipboard calls are
  wrapped: it is a shared resource another app can hold. The footer
  shows the tracked — else nearest visible — waypoint in its colour with
  a 14-char name, the distance, and "~N min" while the closing speed
  toward it is ≥ 0.3 m/s (from `SpeedTracker`); a 3 s `_notice` (added /
  removed / copied / nothing to paste) replaces the line briefly. Heading
  + speed pill (`MinimapSpeedEnabled`, default on): bottom-right, the
  scale bar's twin — `Compass.Letter` of the arrow's screen heading (body
  yaw + `MinimapYawOffsetDegrees`, so relative to the dino's body, not
  the free-look camera) and km/h. Area pill (`MinimapAreaEnabled`,
  default on, v1.29, `MinimapWindow.Area.cs`): TOP-RIGHT, the one free
  corner (owner's choice) — the settled area MainWindow hands it through
  `SetArea` (its `AreaJournal`; the minimap computes none itself). Where
  NO area is named (open sea, off the map's edge) it reads "Uncharted",
  dimmed to 60% so it isn't taken for an area of that name — the owner's
  word (Oct 2 2026): they asked for something mystical and NEUTRAL, one
  that doesn't claim water ("Uncharted waters" was my pick and was
  turned down for that), since a future map could leave land unnamed.
  It never reaches the Activity feed. A CROSSING blinks the pill for 3 s
  (`SetArea` → `PulseBriefly`; Oct 4 2026) — not its first appearance
  after a spawn, not while it shows the cursor's area. Hidden only while there is no
  position (not in game) or the pill is off; in EDIT MODE it names the area
  under the CURSOR instead, in the edit orange, and only that (falling
  back to your own area there would read as "this spot is in my area") —
  the owner's way to look the borders over without walking the island.
  `FractionAt` is the one panel-point → map-fraction inverse (the
  right-click menu uses it too). Area BORDER layer
  (`MinimapAreaBordersEnabled`, default off; the control panel's Areas
  button → `ToggleAreaBorders`): `AreaBordersPath`, scaled and
  translated with the map like the heatmap and above it, at 70% — DARK
  NAVY LINES, NOT the area colours: those are picked to be told apart
  and bury the terrain, and would turn to mud with the heatmap (owner
  agreed, Oct 2 2026). THE RULE IS ONE LINE: a line wherever a pixel's
  neighbour belongs to something else, another area OR none
  (`AreaBorders`), so every area is a closed shape, its coastal water
  and the bays included — exactly what the pill's lookup uses. HOW IT
  GOT THERE, so it isn't walked again (all Oct 2 2026, the owner
  judging each step by eye): (1) the first build drew that same rule in
  LIGHT lines — a white net over the island with scalloped rings round
  the coast; the owner chose "darker, fainter" over the alternatives
  (outline only your own area, a muted tint, names on the map, hiding
  it with the heatmap). (2) I then cut the rule down — only between two
  areas, then on land only with a land mask, then a traced shoreline
  for an area that is an island of its own, with the map picture's dark
  coastal rim trimmed off the mask so that line could be seen — each
  step answering a fair complaint (rings, stubs ending in the sea, an
  island with no outline) with another mechanism. (3) Asked whether
  that was worth it, I said only partly; side-by-side renders of "as
  built" against "every line, in the dark colour" settled it: the owner
  chose EVERY LINE. In dark navy the sea lines are faint and read as
  part of the map; the layer shows what the pill does; and the land
  mask, the rim trim and the island rule all went (less to tend when a
  new map comes). Don't bring the light lines back, and don't
  re-introduce a land mask or per-case outline rules. Also rendered and
  NOT taken: rounder water shapes (the generator spreading over water
  in 16 directions instead of 8, so bays aren't angular) — too subtle
  at actual size to justify changing a map already tried in game; it
  lives only in the generator, so it can be added later if the angular
  bays start to bother. (4) The layer was first a PICTURE of the
  borders (1-bit, map pixels) stretched with the map; the owner found it
  low-res at 5–6× zoom, so it is now VECTOR LINES: `AreaBorders.Trace`
  (pure, tested) walks the pixel edges into chains — junction to
  junction, plus closed loops — and straightens each with
  Douglas–Peucker at 0.9 map px, so a slanted border's staircase becomes
  one line, junctions stay put and lines still meet. `AreaMapAsset.Borders`
  is that, traced once into a frozen StreamGeometry in map pixels;
  `UpdateAreaBorders` scales it through the GEOMETRY's transform (points
  move, the stroke doesn't), so the line is ~1.2 screen px at every zoom
  and MinimapScale and stays sharp — and a future big map could reuse it
  as is. `Assets/areas.png` is untouched by this; the borders are no
  more ACCURATE than its 12.5 m grid, only cleaner. (5) TWO HIGHLIGHTS
  on the layer (Oct 4 2026, `UpdateAreaOutlines`), mirroring the pill's
  two colours: YOUR area's outline in a soft light line (`AreaOwnPath`,
  #ECF2F8 at 60%, 1.5 px) — where you are and how far it reaches — and
  in edit mode the area under the CURSOR in the edit orange
  (`AreaHoverPath`; the owner's idea). Pointing at your own area, orange
  wins; nothing at open sea; only while the Areas layer is on, so the
  button keeps one meaning. The owner chose the light line from a
  side-by-side render over a bolder navy (lost on forest) and over
  orange for your own area (it would be indistinguishable from the
  hover), and asked for it softer than rendered. This is why
  `AreaBorders.Trace` returns `BorderLine`s that know the two things
  they separate (constant along a junction-free chain):
  `AreaMapAsset.OutlineOf(area)` is the very lines of the layer that
  have that area on a side, so a highlight sits exactly on them. A
  single light outline is NOT the rejected white net. `UpdateArea` also
  runs for the layer alone, so the highlight works with the pill and
  the feed lines switched off. Optional heatmap
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
  transform (arrow, waypoints, trail, friends). Friends
  (`MinimapWindow.Friends.cs`, Sep 27 2026): the roster from
  `FriendsChanged` drawn into `FriendLayer` (a Canvas between the waypoint
  markers and your arrow) as arrows at three quarters of yours, rotated
  by their yaw + `MinimapYawOffsetDegrees`, in `FriendBook.ColourOf`; the
  drawn set = `OnMap` && `ShowsOnMap` && `FriendsOnMinimap`. The tracked
  friend (`config.TrackedFriendSteamId`) wears the 16 px ring and
  edge-clamps in the centered view like the tracked waypoint.
  `UpdateWaypointVisual` records the map translate TARGETS
  (`_mapTargetX/Y`) and calls `UpdateFriendVisual` with the same glide,
  so friends pan with the map; a new roster glides each arrow to its new
  spot over 0.9 × the poll interval (first placement snaps; a rebuild
  copies the old marker's transform so nothing jumps). Friends stay drawn
  while YOU are not in game — their positions are still true. Footer
  priority: hovered friend, else tracked friend while on the map, else the
  waypoint logic; a friend line is "▲ name · 340 m · ~2 min" in their
  colour — navigation only, never stats (`AppendDistance` is shared with
  the waypoint line). Right-click on a friend arrow: "Track/Untrack
  <name>", "Waypoint at <name>" (a library waypoint in their name and
  colour where they stand), then the usual entries with "Copy this spot"
  sharing their exact position; `FriendHitTest` runs before `HitTest`
  since friends sit above waypoints.
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
- **SpeedTracker.cs** — pure, tested: path length over a 15 s window of
  positions → m/s (zig-zags count as path, not displacement), and
  `ClosingMps(target)` — signed approach speed for the waypoint ETA. A
  jump > 60 m/s or a gap longer than the window restarts it, so a respawn
  or resumed idle polling never reads as a dash. The window is a ctor
  argument (v1.27): the minimap keeps the calm 15 s, the stats panel's
  combat Speed row runs its own instance at 2.5 poll intervals (min 6 s)
  so a sprint or a stop shows within two polls.
- **Compass.cs** — eight-point letter for a screen heading (0 = north,
  clockwise, matching the arrow's RotateTransform).
- **AreaMap.cs** — "which area am I in" (v1.29, the owner's idea, Oct 2
  2026): `Assets/areas.png` is an image the size of the island map with
  ONE FLAT COLOUR PER NAMED AREA and nothing else (transparent = no
  area, open sea); `Assets/areas.json` is its legend (colour → name, the
  label point in world cm, source and dates). Both are embedded
  resources like `rules.json`. A DATA file, never drawn: your position
  goes through `MapCalibration.ToFraction` (the arrow's transform, moved
  onto the record so it is the one copy), `floor(fraction × size)` picks
  the pixel, the pixel's colour is the area. Purely local — no request,
  nothing game-side, and our own asset, NOT the site's unapproved zone
  images. Pure, tested: `AreaLegend.Parse` (tolerant), `AreaMap.FromPixels`
  (BGRA → one byte per pixel; fully transparent = none, fully opaque in
  a legend colour = that area, anything else — a soft brush edge, a
  colour picked by eye — counts in `UnknownPixels` and reads as NO area
  rather than a wrong one), `IndexAt` / `NameAt`. **AreaReadout** is the
  no-flicker rule: a new area is taken only once you are CLEARLY inside
  it — the same area at your spot and at eight points `MarginPixels` (2
  px ≈ 25 m) around; the first reading of a life is taken as it is, and
  so is one after a jump (the shown area nowhere around you: no border to
  wait for); "no area" is a reading like any other. **AreaJournal**
  wraps it for MainWindow (which owns it, so the feed's lines don't
  depend on the minimap being shown — `UpdateArea`, per poll and after a
  Settings save; with pill and lines both off the map is never loaded):
  `Current` for the pill, and ONE rule for the Activity feed's "Entered
  Highland" (`SelfActivity.AreaLine`, `FeedKind.Area`,
  `ActivityAreaLines`, default on) — the feed names an area when it
  differs from the last one it named and the last such line is ≥ 30 s
  old. So a first crossing is said at once, a step over the border and
  straight back says nothing more, and staying on the other side is
  caught up when the half minute is over (the feed never ends on an
  area you left); where a life begins is no entry, open sea is never
  announced. Area lines light a faded Activity panel like every other
  line: the first build exempted them ("a crossing is worth a line, not
  a look") and the owner removed that on trying it in game (Oct 5 2026)
  — see the rule under ActivityWindow.
  The same switch names the area in SPAWN lines (Oct 4 2026, the
  owner's picks from a list of area ideas): "You spawned as Deino 42% ·
  Delta" (`SelfActivity.Update`'s `area`, which is why `UpdateArea` runs
  BEFORE your own lines in `OnSnapshot`) and a friend's "… spawned as
  Deino 42% · Swamps" / fresh-life line (`FriendFeed.Update`'s `areaAt`,
  MainWindow's `AreaLookup` — a plain lookup, no border rule), only for
  a friend the site lets us place (`OnMap`: location shared) and only
  where an area is named; "Uncharted" never reaches the feed. The area
  goes LAST in the line, so on a long name it is what the ellipsis cuts.
  `AreaBorders.Trace` gives the borders as lines for the minimap's
  optional layer (every edge of every area, see the border layer above).
  **AreaMapAsset.cs**
  is the WPF-imaging half: decodes the PNG (colour profile ignored,
  straight BGRA) once, on first use (~25 ms, a megabyte of grid). THE
  BORDERS ARE OURS: VulnonaMAP's data holds each of its 26 areas as ONE
  label point plus a size hint (`large` / `small` / `ocean`) and no
  bounds (a 27th, Central Dome, was added Oct 4 2026 from its landmark
  "Central Dome (Hexagon)" as one more `small` label — INPUT, not a
  rule: the dome had no area label and was split between three areas;
  the owner confirmed the name), and the map image has none drawn, so `tools/area-map` computes
  them — sea = the map's navy connected to the border (lakes and rivers
  stay land); every land pixel goes to the label that reaches it soonest
  OVER LAND (`large` spreads 1.3×, `small` 0.7×), so an area never jumps
  a bay; sea labels claim ~1.1 km of water; an offshore label of a land
  area takes the nearest shore plus the water round the label; islets
  take the nearest claimed area; a 375 m coastal band follows the land
  beside it. The owner accepted the generated borders as the first
  version ("the borders you draw and everything is fine"). Known weak
  spots: straight borders that ignore rivers and ridges, small areas as
  round blobs (Central Dome's is the dome and the land around it, not
  its walls), Port / East Coast claiming big octagons of sea. CORRECTIONS ARE
  PAINTED into `Assets/areas.png` with the legend's exact colours, hard
  edges, PNG — which is why it is an image and not polygons — and the
  bundled-asset tests hold it to that (only legend colours,
  every label point inside its own area). Elevation (a cave under a
  meadow) is DROPPED, not deferred — owner, Oct 2 2026: the game's caves
  are few and small, ignore them. (For the record: a second layer with
  a ceiling height per cave would be the route; packing heights into
  the alpha channel was considered and dismissed — no height data, and
  alpha is awkward to author.)
- **ShareCode.cs** — the share-a-spot text: `pandora:<x>,<y> [name]` in
  METRES (short, no decimals), parsed forgivingly (prefix optional, may
  sit inside a longer message, |value| ≤ 50 km; the rest of the line
  after the numbers is the name, v1.22). Pure, tested, invariant culture
  both ways.
- **WaypointLibrary.cs** — `Waypoint` (id, name, world cm, palette
  colour, visible, pack), `WaypointPalette` (12 hex colours — owner's
  call; the first three are the v1.20 slot colours so migrated slots keep
  their look; `Wrap` for cycling) and the library: up to 256 (uint8 —
  owner's call) entries in `waypoints.json` next to config.json — USER
  CONTENT, kept apart from runtime state and the cookie vault, and the
  file phase 3's export/import moves around. Every mutation validates
  (`InBounds` ≤ 50 km, `SanitizeName` ≤ 32 chars, no control chars,
  colour wrapped, capacity) and raises `Changed`; `FromJson` is the import
  gate (drops off-island entries, fixes empty/duplicate ids, caps). Pure
  and tested; nothing throws. Tracking state (`config.TrackedWaypointId`)
  and the draw policy (`config.WaypointVisibility`) are config, not
  library.
- **WaypointPacks.cs** — pure, tested export/import (v1.23, phase 3):
  `Export` writes the library's JSON shape plus a `Name` (ids KEPT so a
  re-import is recognised, per-entry `Pack` cleared, `Visible` true — the
  receiver's view is theirs); `Parse` reads a pack or a plain library file
  through `WaypointLibrary.FromJson`'s gate, falling back to the file's
  base name; `Merge` into the Settings draft skips entries present by id
  or by the same name within 20 m, stops at the cap, and adds the rest
  HIDDEN and tagged with the pack name (owner's decision: imports must not
  bury a map). The Settings page owns the file dialogs
  (`Microsoft.Win32.OpenFileDialog`/`SaveFileDialog`, filter `*.json`),
  reports the outcome in `WaypointStatus`, and nothing persists until
  Save. The list is one CARD per group (`AddGroupCard`: bordered surface,
  a caption band `BuildGroupBand` on the rows' own five-column grid with
  the group checkbox in the Show column — ticked / empty / mixed square —
  and a ✕ in the delete column for a pack, then zebra-striped rows
  `BuildWaypointRow`); the top column labels (Colour / Name / Show /
  Track / Delete) carry a 7 px side margin to line up with the cards'
  border + padding. A row's Show click calls back into its band so the
  group checkbox tracks all / none / mixed live (it only refreshed on
  rebuild at first — reported as a bug). The colour disc sits in the
  same 15 px dark well as the Check template's box; the per-row delete
  is a NeutralButtonStyle button with a soft red glyph, while the pack
  delete and Delete all stay red (they remove many at once). Three loose Show all / Hide
  all / Delete pack buttons, then bare caption rows, were tried first and
  the controls "floated" (owner, Sep 26 2026). The `Check` and `Radio`
  styles are our own dark templates for the same reason — stock white
  glyphs float on this surface; a content-less box drops its label gap,
  IsChecked = null draws the mixed square. Text boxes likewise:
  `TextBoxStyle` (dark well, 2 px corners, hover lightens the border,
  focus turns it the text colour, our own caret + selection brush — the
  stock template flashed Windows blue on hover/focus/selection) with
  `NameBox`, `HotkeyBox` and the cookie box derived from it
  (`VerticalContentAlignment` drives the content host: Center for
  single-line boxes, Top for the wrapping cookie box). An implicit
  ScrollBar style applies `DarkScrollBar` to every bar in the dialog.
  Rule: NO stock chrome anywhere in this dialog — every control type
  used here has its own template (buttons, checks, radios, text boxes,
  scrollbars); a new control kind gets one before it ships.
- **FriendBook.cs** — `FriendEntry` (steamId, the site's `Name` as last
  seen, local `Nickname` ≤ 32, `Colour` override or null, `ShowOnMap`,
  `Notify`, `LastDino`/`LastGrowth`/`LastSeenUtc`), `FriendColour.Default`
  (FNV-1a of the steamId into the waypoint palette MINUS orange — your
  arrow — and white — the outline; stable per friend across sessions and
  machines, unlike the site's roster-order colours; a collision between
  two friends is accepted, the feed names them and the colour can be
  overridden) and the book: `friends.json` next to config.json — USER
  CONTENT like the waypoint library. `Sync(roster, now)` mirrors the
  site's membership (adds unknowns, DROPS friends no longer on the roster
  with their prefs — you unfriended them there), follows renames, and
  refreshes the last-seen facts for in-game friends; it raises `Changed`
  (→ MainWindow saves) ONLY for membership/name changes, never per fetch.
  `ApplyPrefs(draft)` copies nickname/colour/map/feed onto live entries by
  steamId — never membership, so a stale Settings draft can't resurrect a
  dropped friend. `DisplayName` = nickname → site name → fallback.
  Pure, tested, nothing throws.
- **FriendFeed.cs** — pure, tested: the friends half of the Activity feed.
  Diffs each roster against the previous one (by steamId; self already
  filtered by PollService) into `FeedLine`s for the ActivityLog, and holds
  the header's facts (`Total`, `InGame`, `InGameNames`, `HasRoster`).
  Kinds: Roster (first sight seeds ONE
  "In game: a (Deino), b (Cera) and n more" line, so the widget never
  starts blank; new friend / unfriended), Spawned ("x spawned as Deino
  42%"), NewLife (same species, growth LOWER than their last in-game
  state: "started a fresh Deino" — a fact, never "died"), Left (neutral
  "is no longer in game": logout, restart and death look alike from
  outside; several in one fetch coalesce into one line), DinoChanged,
  Growth (one `GrowthMilestones` per friend, `StageName`), Fracture (each
  flag's false→true), Nearby (< 200 m, re-armed > 300 m, needs your
  position and their shared location). Growth and proximity run for
  EVERY in-game friend — muted (`notify` false) friends produce no lines
  but keep their baselines, and a friend already beside you at launch or
  spawning next to you is primed, not announced one fetch later (that was
  a bug caught by the tests). `ResetBaseline` after a cleared roster
  makes its return seed again instead of reporting everyone as new.
- **ActivityLog.cs** — pure, tested: `FeedKind` (Roster / Spawned / Left
  / NewLife / DinoChanged / Growth / Fracture / Nearby / LowStat / Damage
  / Prime / Skin / Area), `FeedLine` (time, text, a friend's `SteamId` or `Mine`) and
  the store: `Post` (one line or a batch keeping its order, newest first,
  cap 50) raises `Posted(batch)` for the widget; `Expire` drops lines
  older than 10 min; `AgeOpacity` 1 → 0.35. Owned by MainWindow.
- **SelfActivity.cs** — pure, tested: YOUR events for the feed. `Update`
  per poll: spawned ("You spawned as Deino 42%") or a fresh life (same
  species, growth lower than your last in-game state — a fact, never
  "died"), fractures taken, and — only with `ActivityDamageLines` — "Took
  damage · HP 62%" for a drop ≥ 5% at most once per 30 s (the fade's
  0.005 cue is far too fine for a line). The first poll of a session is a
  baseline (in game at launch ≠ spawned); a new life compares nothing.
  Builders for the rules MainWindow already runs: `GrowthLine(percent)`
  ("You are now a subadult"), `LowStatLine(stat, DrainTracker.Label)`
  ("Hunger under 20% · ~40m left"), `PrimeLines(before, fresh)` — the
  CHANGE, one line per flipped condition ("Prime · now met: <text>" /
  "lost: <text>", texts from `PrimeConditions`), else one summary "Prime
  check · 6/10 · ready / not ready / Prime Elder". Owner's call (Sep 28
  2026): the widget became a general activity feed because friend-only
  lines at a two-friend scale left it empty; own events turn the
  overlay's momentary cues (blink, chime, status line) into a readable
  last-ten-minutes; waypoint edits, heatmap toggles and connection blips
  are deliberately NOT posted (chores, not gameplay).
- **ActivityWindow.xaml(.cs)** — the fourth widget: header ("Activity" +
  "3 of 7 friends in game", "—" until the first roster, blank with
  friends' events off — the counts are part of the friends extra) over
  `FeedLines` (6) equal-height slots in a Grid filling the SMALL frame
  (WidgetFrame) under the header — as many as fit at 100%; a 3/5/8
  `ActivityRows` setting shipped in 1.24.0 and was dropped in 1.25
  (owner's call: inside a fixed frame three lines floated and eight
  didn't fit; the JSON key is simply ignored now); a pure renderer of
  MainWindow's ActivityLog: newest at the top, an orange ● (your arrow's
  colour) leads your lines, a book-coloured ● a friend's, none a roster
  line; Fracture/LowStat/Damage read amber; opacity = `AgeOpacity`;
  `Render` expires first (a window re-shown after a long hide must not
  show dead lines) and a 20 s timer re-renders while lines exist. Slot 0
  with no lines = the quiet line: "In game: a, b", "no friends in game
  right now", "waiting for the friends list…" or "friends unavailable ·
  retrying" (roster cleared) — or just "no recent activity" with friends'
  events off or no friends. Owner's design (Sep
  27 2026): a feed, NOT a row-per-friend list — rows would resize with
  the roster, and the arrows already say who is where; the tracked
  friend's stats live nowhere on the overlay (a footer stats line was
  proposed and REJECTED: the footer is navigation only). Takes part in
  the fade (`Fades => true`): `Posted` lights it 30 s, calm when opened.
  RULE (owner, Oct 5 2026): ANY new line wakes it — no kind of line is
  exempt, and no per-kind wake rules are added (`OnPosted`).
  No sounds here: the friend-spawn chime (`FriendsChimeEnabled`) is
  MainWindow's, decided in `OnFriendsRoster` like the stats chimes, so it
  plays with the widget hidden (owner, Sep 28 2026: a friends alert is
  not a widget option — it sits on the Friends page, not Activity).
  Derives OverlayWindowBase; own `ActivityScale` (default 1.0),
  `ActivityEnabled` (default ON — unlike Prime it costs
  requests, see constraint #2), `ActivityX/Y` nullable; first show = the
  `suggested` point under the Prime tracker (MainWindow computes it),
  else left edge centred.
- **Patreon skins (v1.28, a pilot — the owner's idea, Oct 1 2026)** — the
  website's `/patreon` page rebuilt as Settings → Skins, so a skin can be
  applied right after spawning without alt-tabbing (the server allows one
  about every 15 minutes, only while spawned in). Four parts:
  **Skins/PatreonSkins.cs** (pure, tested): `PatreonSkin` (id kept WITH
  its JSON type — `IdJson` sends back a number as a number, a string
  quoted — name, description, image/thumbnail, tier name + role id,
  `Locked`, seven colours), `ParseList`, `ParseApply`, `ToHex` (the
  site's own conversion: linear 0–1 channels raised to 1/1.8, rounded
  half-up like JS), `ResolvePicture` (https only; site-relative paths go
  under islapandora.eu; http / data: / file: = no picture), `Sorted`
  (unlocked, then tier rank from the site's four hard-coded role ids,
  then name), `Matches`, `Clean` (server text made showable: one line,
  capped, and DROPPED WHOLE if it mentions connect.sid / cf_clearance).
  The site's "SV" value is ignored (nobody knows what it means).
  **PandoraClient.Skins.cs**: the list POST, the apply POST (reads the
  body whatever the status, like Prime; Referer set to /patreon) and
  `FetchPictureAsync` on its own bare HttpClient (UA only, 60 s timeout,
  a 16 MB sanity cap — the first build's 4 MB cap rejected the site's real
  pictures; never throws — returns a `SkinPicture` with the bytes or a
  short reason). PICTURES ARE BIG: islapandora.eu serves each one at full
  size (4.7 MB seen, ~90 skins, thumbnail and image alike), so they are
  (1) fetched ONLY for tiles on screen, like the site's `loading="lazy"`
  (`LoadVisibleSkinPictures`: one loop, re-started by ScrollChanged, a
  re-render or coming back to the page; no preload margin on purpose),
  and (2) fetched ONCE: **Skins/SkinThumbnails.cs** (WPF imaging, tested)
  decodes the download straight to a 320 px thumbnail, saves it as a
  ~10–40 KB JPEG under `cache/skins/<hash of the address>.jpg` next to
  the exe (30 days, temp-file-then-move, fail-soft) and the bytes are
  dropped — neither PollService nor the window keeps a download in
  memory. PollService remembers only failures (per session; Refresh
  clears them) and downloads under way. "Reload pictures" (Oct 2 2026,
  `SettingsWindow.SkinPictures.cs`) is the way to discard the saved
  copies: `SkinThumbnails.Clear` deletes them (~90 small files, instant),
  every picture shown is marked stale (`_stalePictures`) and STAYS on its
  tile until its replacement arrives, the loader re-downloads on-screen
  tiles as usual, and the list is not re-rendered. Its OWN button, NOT
  part of Refresh (owner's call, after weighing it): Refresh is about the
  list and costs one small request; a reload costs ~5 MB per tile looked
  at (~28 MB a screenful, ~400 MB for the whole list) and only matters
  when the site replaced a picture under the SAME address — thumbnails
  are keyed by address, so a new skin loads by itself. Floored at 30 s in
  PollService (`TryBeginSkinPictureReload`).
  **PollService.Skins.cs**: ALL gating — `GetSkinsAsync(refresh)` (the
  session's copy for 10 min, 30 s floor, a failure keeps the last good
  list and names the problem), `GetSkinPictureAsync` (see the pictures
  note above; a failure is remembered), `ApplySkinAsync` (busy + 15 s guard
  and not-in-game answer locally; a stale not-in-game state gets one
  regular poll first; on Ok stores `config.SkinLastAppliedUtc` and
  `config.SkinChoices[species]`, raises `SkinApplied` → MainWindow posts
  `SelfActivity.SkinLine` to the feed); `RebuildClient` forgets the list.
  The server's cooldown is NEVER enforced locally — it may differ by
  rank like Prime's; the page only says how long ago the last apply was
  and shows the server's refusal (`SkinApplyOutcome.Refused`, its cleaned
  words — a deliberate exception to "never echo server strings" until
  the real messages are known and can be mapped). **SettingsWindow.Skins.cs**:
  tiles in a WrapPanel, three across (138 px; the page is 436 wide):
  picture well (a quiet dark box that SAYS its state: "loading…", "no
  picture" when the skin has none, "picture unavailable" when every
  address failed — the reason, host first, is on the hover tip: "HTTP
  404", "too large", "timed out", "not a picture format this Windows can
  show". The thumbnail is tried first, then the full image, like the
  site's own fallback. A first build filled the well with the seven
  colours as stripes and the owner read them as broken images — don't
  bring that back; the dots already show the colours), name, tier
  (orange, red + "locked"), colour dots, Apply → the six pattern buttons
  A–F in place (one tile armed at a time; the site uses a pop-up), a
  hover preview in our own tooltip colours, search + Available/All,
  "Apply again: <name> · C" for the current species, Refresh, Reload
  pictures and a link to the site (the page's note has its own line above
  the three buttons). THE ONE PAGE THAT ACTS AT ONCE instead of on Save, and
  says so. The list loads the first time the page is looked at per
  dialog (`SetPage` → `OpenSkinsPage`), never for a page you aren't on.
  Buttons stay enabled while not in game on purpose: the click is what
  refreshes a stale idle state. Rejected for it: a widget (needs clicks,
  isn't watched) and a control-panel button (no widget to group under).
  Later, if the pilot works: a hotkey for "apply my skin", favourites,
  the custom presets (other endpoints).
- **Dino storage (1.33, a few players' request, built Oct 8 2026)** — the
  website's `/extras` Dino Storage page rebuilt as Settings → Dino
  storage, the nav entry right after Skins. The page's sibling there, the
  Lucky Wheel, is NOT built (owner: ignore it for now). Analysis, plan and
  mockups came first (Oct 7, `Desktop\Pandora Overlay
  Files\pandora-dino-storage-sketch`); the owner chose the accordion,
  Delete in the first version and both extras (Oct 8). Storing and
  retrieving are not on the website ("Store a dino in-game to see it
  here"), so not here either. Four parts:
  **Storage/StoredDinos.cs** (pure, tested against a real answer the owner
  captured, anonymised): `StoredDino` — the id kept with its JSON type
  like a skin's; the species from `dinoClass` by the website's own rule
  (`BP_Herrerasaurus_C` → Herrerasaurus); the name null for the site's
  "Unnamed"; vitals as `StoredVital` through the website's TWO-SYSTEM
  rule (a value ≤ 1.5 beside a maximum > 1.5 is a FRACTION of it — the
  old storage system; the site's changelog: "work with the old and new
  storage system"); mutations as regular / parent / elder lists, an elder
  slot an "A + B" pair, "None" empty, and the badge count every filled
  value, like the site; the position in world cm, none when missing,
  exactly 0/0 (the site's fallback) or off the island; `storedAt` ISO or
  ms. Plus `StoredDinoList` (`With` / `Without`), the two bodies,
  `ParseRename` / `ParseDelete` (server words through `PatreonSkins.Clean`,
  shown as "The website said: …" like a skin refusal), `GrowthText`
  ("58% · subadult", rounded down with a hair of slack — 0.58 × 100 is
  57.999… in floating point, a test caught it) and `StoredText`.
  **PandoraClient.Storage.cs**: the three POSTs (Referer /extras), every
  answer's body read whatever the status, like the site; the site's
  refusal → `SessionEndedException`. **PollService.Storage.cs**: ALL
  gating — `GetStorageAsync` (a request per look, the session's copy
  within `StorageListFloor` 30 s of the last ask; NOT the skins list's
  10 min reuse, because the storage changes with play),
  `RenameStoredDinoAsync` / `DeleteStoredDinoAsync` (one at a time, a 3 s
  breather, `IsSignedOut` answered locally, a success patches the
  session's copy), `ForgetStorage` on a new client or a sign-out;
  `PollService.LastPlayer` is your last in-game snapshot, for the
  distance and the arrow. **SettingsWindow.Storage.cs** (the page, the
  list, the closed cards) + **.StorageDetail.cs** (an open card): a title
  row with "3 of 10 slots used" and a thin bar, the status line, a 331 px
  list, a ONE-LINE note (every pixel went to the list), Refresh and a
  link. A closed card: species, ♂/♀, "· name" in amber or "· unnamed",
  "stored 23 Sep · 11:42", the growth bar with its stage, the badges
  (Prime / Elder ×N / N mutations / Compensated); the whole summary is the
  click. ACCORDION (the owner's pick over a detail view with a back link):
  one card open at a time, scrolled to the list's top, its open part built
  on its first opening; a Refresh keeps it open. An open card: left = the
  description, VITALS (the stats panel's bar colours, plus blood) and
  MUTATIONS; right = STORED AT (the whole island at 130 px, a blue
  diamond, your arrow while in game), "<area> · N km away" (AreaMapAsset;
  "Uncharted" dimmed), "Waypoint here", then Rename… / Delete…. The
  actions sit under the map BY MEASUREMENT: under the mutations, an open
  card was ~30 px taller than the list and they fell below the fold
  (off-screen renders, Oct 8 2026). Rename = the name and description
  boxes in the left column (the website's 40 / 200, live counts, Enter
  saves, Escape cancels; the actions hidden meanwhile; nothing changed =
  nothing sent); a success rebuilds the card in place, open. Delete =
  armed in place: "Can't be undone." over Delete (red) / Keep, the whole
  question in the status line. "Waypoint here" ACTS AT ONCE: into the
  library (the minimap draws it, MainWindow saves the file) AND the
  Waypoints page's draft (a later Save keeps it, Cancel can't lose it),
  tracked in both; the same name within 20 m is tracked instead of
  twinned; the Waypoints rows rebuild on their next look. No Activity
  feed line for a rename or delete (chores). THE THIRD PAGE THAT ACTS AT
  ONCE, after Skins and Account. Not built in batches: a list is as long
  as the account's slots. The page measures 454 px against the Skins
  page's 456, so the dialog did not grow.
- **ServerRules.cs** — `RulesDocument` (Source, CopiedOn, Note,
  PackLimits = categories of `PackLimit(Name, Limit)`, Rules; `LimitFor`
  case-insensitive), `ServerRules.LoadBundled` (the `rules.json` embedded
  resource — EmbeddedResource with a LogicalName, NOT a WPF Resource, so
  the loader is WPF-free and the tests read the very file that ships) and
  the pure, tolerant `Parse` (blank rules / empty categories dropped,
  unusable → null). The seed was copied from the live bundle on Sep 29
  2026 (identical to the Sep 15 copy); updating it = a release, until an
  endpoint exists. Tests assert the bundled seed's shape (17 rules, 21
  species in Herbivore/Carnivore/Omnivore).
- **PrimeWindow.xaml(.cs)** — the Prime tracker widget: status header +
  ten ✓/✗ condition rows (texts baked into `PrimeConditions`, shared with
  the Activity feed — the site bakes them into its frontend too, the API
  only returns flags) + a two-line footer (what the
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
  without the fade. Built on the LARGE frame (WidgetFrame, v1.25 — 250 px
  content width before): DockPanel, header top, footer bottom, the ten
  rows a Grid of equal star rows filling the middle, so it is one size
  in every state and the minimap's twin.
  Derives OverlayWindowBase (drag/snap/clamp; sized by its own `PrimeScale`
  via the `AppearanceScale` override, default 1.0); first
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
- **SettingsWindow.xaml(.cs)** — paged settings dialog: a left-hand nav
  of our own glow-style buttons (`NavButton`; the selected one is
  recoloured in code by `SetPage`) and ONE page visible at a time on the
  right — Account / Controls / General (APP-WIDE ONLY) / Stats panel /
  Minimap / Prime tracker / Activity / Friends / Waypoints / Skins (see
  Patreon skins) / Dino storage (see Dino storage) / Server rules / About (this copy: version, updates, the settings folder, links — `SettingsWindow.About.cs`), widget pages in the control panel's order, each holding that
  widget's Scale-or-Size slider and its own options; a new widget adds a
  nav entry + page. Server rules page (`SettingsWindow.Rules.cs`, Sep 29
  2026): a REFERENCE page, nothing saved — the bundled RulesDocument
  rendered as a pack-limits card (one column per diet category, category
  names in diet colours, your live species — passed in as `currentDino`
  — in orange with its limit named in the caption) and a rules card
  (zebra rows, "01" numbers, wrapping text) in a 340 px `DarkScrollBar`
  viewer; "copied <date>" stamp, the site's Discord-priority note, and
  buttons to the page and the Discord. The ctor's `page` parameter opens
  the dialog on a given page (the tray's "Server rules…"); first run
  still forces Account.
  Activity page (Sep 28 2026, in the main file like Prime's): Scale,
  "Include friends'
  events" (`ActivityIncludeFriends` — own events are ALWAYS on: the feed
  is yours, friends are the extra; a "Friends only / Friends and me"
  radio pair was shipped for a day and replaced Sep 28 2026 as a leftover
  of the friends-first origin) and the damage-lines checkbox
  (`ActivityDamageLines`, read live); the friends checkbox sets
  `ActivityChanged`. Friends page (`SettingsWindow.Friends.cs`, Sep 27
  2026): "Show friends on the minimap" (`FriendsOnMinimap`), the
  friend-spawn chime (`FriendsChimeEnabled`, read live by MainWindow),
  then ONE card of rows from a DRAFT of the FriendBook
  (`_book.Clone()`), in-game first then by name: colour well (cycles;
  overrides the hashed default), a `NameBox` showing the name you see
  (typing sets a nickname, clearing it or typing the site's name drops
  it), a "Last seen" cell (green "in game" from the roster passed in at
  open, else "2 h ago" from `LastSeenUtc`, tooltip = last dino/growth),
  Map and Feed checkboxes, Track radio (click again untracks). Save
  commits via `ApplyPrefs` (preferences only — membership is the
  roster's) + `TrackedFriendSteamId` and sets `FriendsChanged`. No add /
  remove: a "Manage on islapandora.eu" button opens the site's friends
  page; the overlay never calls the management endpoints. The header
  alignment trick is shared (`AlignHeader(header, list, top)`).
  History: regrouped by widget Sep 24 2026 ("option A"), then the nav
  ("option B") Sep 25 2026 as the foundation for the waypoint library's
  list page. Waypoints page (v1.22): rows built in code — LAZILY since
  Oct 1 2026 (`OpenWaypointsPage`, on the first look at the page): a row
  is five templated controls and hidden pages are still laid out, so with
  the packs imported (122 waypoints) every opening of the dialog took
  ~0.6 s instead of ~0.25 s, whichever page was shown (measured; a full
  library ~0.9 s). Save never needed the rows. RULE (owner reported the
  dialog opening slowly twice, Oct 1 2026): content built in code is
  built on the page's FIRST LOOK (`SetPage` → `OpenWaypointsPage` /
  `OpenFriendsPage` / `OpenRulesPage` / `OpenSkinsPage` / `OpenStoragePage`), and anything
  long is added IN BATCHES — the first screenful at once, the rest per
  `DispatcherPriority.Background` tick, with a version counter so a newer
  rebuild stops an older one (waypoint rows 12 then 24, skin tiles 9 then
  12; skin tiles also build their pattern buttons and tooltip content
  only when first needed). The later batches normally take ~0.1–0.2 s;
  should they take longer than `BuildNoticeAfter` (300 ms, counted from
  the first idle batch so the dialog's own opening doesn't use it up) the
  page's count line reads "building… 48 of 122" until the list is whole
  — on a normal PC it never shows. Words in the count line on purpose: a
  spinner would live a fifth of a second and only flicker, and a COVER
  over the list until it is built was proposed and dropped (owner, Oct 2
  2026) — the first screenful is ready and usable at once, a cover would
  hide it and flash on every skin search. Measured after, with the overlay's windows
  already up (an idle PC, off-screen): a normal opening ~80 ms, the FIRST
  opening per launch ~200 ms — ~60 ms reading the dialog's BAML once,
  ~55 ms the first layout (control templates, six framework assemblies),
  only ~27 ms of it JIT, so ReadyToRun would barely help. A hidden
  warm-up (construct + lay out off-screen, ~180 ms once after launch)
  would bring the first opening to ~90 ms; the owner chose NOT to build
  it ("we can live with this") — don't re-propose it unasked. Only
  what sets a page's HEIGHT stays eager (the Rules note wraps, and the
  tallest page sets the dialog's height). The list is CHANGED IN PLACE,
  never rebuilt, by what you do inside it (owner, Oct 1 2026: a pack's
  Show all rebuilt the whole list, threw the view back to the top and
  redrew every row in front of them — "I lose track where I was"):
  `SetGroupVisible` ticks the built rows' boxes through `_rowShowChecks`,
  `DeleteWaypointRow` removes one row and re-stripes its card (stripes
  are by position), `DeletePack` removes one card; an emptied card
  leaves too. Only Import and Delete all still rebuild (they change the
  list's structure). The rows come from a DRAFT
  (`_library.Clone()`) so Cancel drops edits and Save commits via
  `ReplaceWith` — colour dot (click cycles the palette), `NameBox`,
  Show checkbox, Track radio (clicking the tracked one untracks, since a
  radio can't be un-clicked), ✕ delete, "N / 256" count, "Delete all"
  armed by a first click ("Really delete all?") instead of a modal box.
  The list scrolls in a fixed 260 px viewer with `DarkScrollBar` (a
  track + thumb template; the stock scrollbar is light). Import… /
  Export… (`NeutralButtonStyle`, same explicit disabled look) sit beside
  Delete all; see WaypointPacks. The Minimap page also holds the
  `WaypointVisibility` radios (All / Tracked only / Nearest 10). Not the stock TabControl — deliberate, its light chrome
  doesn't theme. Pages live in one Grid; unselected pages are HIDDEN,
  not Collapsed, so the grid keeps the tallest page's height and the
  dialog never jumps between pages; `_lastPage` (static, session-only)
  reopens on the page you were on, first run forces Account. Title and
  buttons are docked outside the page ScrollViewer and `MaxHeight` = 92%
  of the work area, so a small screen scrolls a page instead of losing
  the buttons. Slider rows share a 140 px label width (`SliderLabel`) so
  every slider starts on the same x; a checkbox that owns a slider sits
  on the slider's row ("Fade idle panels to [slider]", slider IsEnabled
  bound to the box).
  ACCOUNT PAGE (v1.31, `SettingsWindow.Account.cs`): the in-app sign-in
  replaced the cookie paste box and its DevTools walkthrough (v1.0–1.30;
  `Clean()` and the validation went with them). Not signed in: one
  "Sign in with Discord" button (`DiscordButtonStyle`, Discord's blurple)
  that runs `SignInWindow.Run(this)`, and three lines on what happens;
  signed in: a card — initial, name, Steam linked / not, when the session
  was taken — with "Sign in again" and "Sign out" (two clicks, like Delete
  all: it logs the site out too). THE SECOND PAGE THAT ACTS AT ONCE: a
  sign-in or sign-out is applied and saved on the spot and sets
  `CookieChanged`, which MainWindow reads before the saved/cancelled
  branch (signing in and closing with ✕ keeps the sign-in). The card's
  live facts come from ONE auth/me on the page's first look per dialog
  (`OpenAccountPage`); offline it shows what is saved. AVATAR (Oct 8
  2026): the circle shows the account's Discord avatar over the initial
  (`ShowAvatar`) — a saved copy whenever the card is drawn, a download
  only after the page's first look (`PollService.GetAvatarAsync`, the
  skin tiles' cookie-less path and per-session failure memory), saved
  as an 80 px JPEG flattened onto the circle's blue (a JPEG has no
  transparency) under `DataFolder.AvatarFolder`, keyed by address, so a
  new avatar on Discord is a new address and loads by itself; the
  address is kept in `config.AccountAvatar` for the offline card. No
  address, a failure or an unreadable picture: the initial stays. Sign
  out clears the address and the copy. `GateSave`: first
  run, Save stays off until signed in. Six hotkey capture boxes
  (edit / hide-overlay / minimap-view / heatmap / Check Prime / stats view) share the capture UX: combos are
  availability-tested via a throwaway RegisterHotKey on the dialog's hwnd
  and cross-duplicates rejected. MainWindow suspends its six
  registrations for the dialog's lifetime (WM_HOTKEY is system-level and
  would fire behind the modal dialog; suspension also lets the boxes see
  and reassign our own combos) and restores them in a finally on close.
  The Minimap section holds scale (v1.25; size in px before), view mode, centered zoom, the trail
  length (radio buttons, not a ComboBox — stock ComboBox chrome is light
  and ignores Background), the scale-bar checkbox and the heading/speed
  checkbox — NOT the
  heatmap on/off (removed Sep 2026: something you flip is not a preference,
  see the surface rules under Conventions).
  Save writes config + Run key and sets Cookie/Hotkey/Minimap/Appearance
  Changed flags; MainWindow hot-applies each (RebuildClient / re-register
  with fallback / minimap ApplySettings / ApplyAppearance)
  — no restart, ever. General = Start with Windows, the not-in-game
  auto-hide checkbox (no flag: MainWindow reads it live), background
  opacity (30–100%), the fade row (20–80%) and "Reset positions" (v1.25:
  arms `PositionsReset`, applied on Save — positions only). ABOUT page
  (`SettingsWindow.About.cs`, v1.30 — the owner's choice over an "Updates"
  page, which would have floated three controls): THIS COPY of the
  overlay — version and flavour (installed / zip / plain folder, from
  `UpdateHooks`), "Check for updates at launch" (`UpdateCheckEnabled`,
  read at the next launch) and "Check for updates now" (one check, then
  the button becomes "Update to vX and restart" — or "Open download
  page" for a plain folder — and hands over to MainWindow), the settings
  folder's path with "Open folder" and "Import from an older copy…" (see
  DataFolder; a successful import disables Save and asks for a restart),
  and the Releases / Report a problem / Discord buttons; "What's new"
  (1.32) sits beside "Check for updates now" and opens the update
  card's notes mode for the running version (the owner picked it over an
  underlined link on the version line: the dialog has no link look). A fresh install
  still lands on Account, which shows ONE pointer line to About while no
  cookie is stored; Stats panel = scale
  (75–150%), the View radios (Survival / Combat — the owner's names; the
  default view, the hotkey flips it mid-game), the time-left checkbox (hunger, thirst and
  stamina labels) and the two chime
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
  `RegisteredExePath` / `RepointToThisExe` (v1.30): the entry follows the
  copy actually run, see DataFolder.
- **App.xaml.cs** — single-instance mutex: a second launch shows a notice and
  exits (protects constraint #3 from silently doubled polling); then the
  settings move (`DataFolder.MigrateLegacy`, v1.30). Started from
  `Program.Main`, not the generated `App.Main` (Velopack first).
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
  on errors. The site's own refusal (403 with a JSON body) twice running
  → "Session ended · sign in again from the tray" and the tray's "Sign in
  again…" entry (v1.31; before, the user pasted a fresh cookie via ⚙).

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
cookie box folded and a scroll safety net — all verified in-game),
v1.19.0 (Check Prime hotkey Ctrl+F8 replacing the tray line; growth
stage blink at 25/50/75/100; opt-in low-stat and growth-stage chimes on
the Windows Exclamation sound; Prime tracker changed-row highlight and
cooldown-end blink — verified in-game), v1.20.0 (three coloured waypoint
slots via the edit-mode map menu, share-a-spot codes, heading + speed
pill, nearest-waypoint ETA; the single waypoint migrated to blue —
verified in-game), v1.20.1 (waypoint diamonds 14 → 10 px — three at the
old size crowded the arrow; owner's call), v1.21.0 (Settings left-hand
nav, one page at a time — the base for the waypoint library's page;
verified in-game), v1.22.0 (waypoint library phase 2: up to 256 named,
12-colour waypoints in waypoints.json, tracking, a Settings Waypoints
page, the visibility policy, named share codes; the v1.20 slots migrated
— verified in-game), v1.23.0 (waypoint packs: export/import with
duplicate skipping and hidden arrival, per-pack captions; source files
grouped by feature + partial-class splits; Settings polish — own
templates for buttons, checks, radios, text boxes and scrollbars, group
cards with striped rows, measured header alignment — verified in-game),
v1.24.0 (Sep 28 — friends, approved Sep 27: friend arrows on the minimap
in FriendBook colours, the tracked friend ringed + edge-clamped + first
claim on the footer, edit-mode menu Track / Waypoint at; the Activity
widget — a FEED of your own events (spawned / fresh life / growth stage /
low stat / fracture / Prime check diff; damage opt-in) with your friends'
as the default-on extra (spawned / left / fresh life / dino change /
growth stage / fracture / nearby / roster changes), fixed 3/5/8 lines,
age fade, 10 min expiry; a Settings Activity page and a Friends page
(nickname, colour, map, feed, track per friend; manage on the site);
`friends.json`; the roster on every 2nd in-game poll, only while a friends
surface is on. Built as a friends-only widget Sep 27, turned into the
general feed Sep 28 before release because two friends left it empty,
renamed while its config keys were still free — verified in-game),
v1.25.0 (Sep 29 — the UI/layout release: two fixed frames (WidgetFrame;
Prime = minimap, Activity = stats, all 298 wide), the permanent fracture
row, `MinimapScale` in percent with the pixel-size migration, the
Activity rows setting dropped; nullable `WindowX/Y` + `MinimapX/Y`,
first-show placement through the pure `DefaultLayout` ("Columns", under
the game's top HUD strip, preset-shaped for later) via
`FillDefaultPositions`, and "Reset positions" (positions ONLY) on
Settings → General; the ONE accepted resize of existing widgets — verified
in-game with both an existing and a fresh config; README screenshots
retaken on it), v1.26.0 (Sep 29 — Server rules: a Settings reference
page and the tray's "Server rules…" showing the pack limits with the
live species highlighted and the numbered rules, from a dated bundled
copy of the site's page (`Assets/rules.json`, ServerRules) — the site
has no rules endpoint; the `/api/rules` ask is pending on the owner's
side — verified in-game), v1.27.0 (Sep 30 — two player requests from the
#pandora-overlay channel: the stamina timer (StaminaTracker — time until
empty while draining, "full ~Ns" while recovering, on the stamina bar)
and the stats panel's two views, Survival / Combat (Combat = health,
stamina, damage taken this fight, speed — same rows, same frame;
"Wounded"/"Healthy" in the header corner, the view named in the footer,
its own fade ruleset, a flip always lighting the panel; Settings
default, Ctrl+F9 and the control panel's View button to flip; no
automatic switching; the first large-bar build was reworked the same
day — verified in-game), v1.28.0 (Oct 2 — the **Patreon skins page**,
the first feature under the blanket go-ahead and the overlay's first
write: Settings → Skins lists the account's skins as tiles and applies
one with its pattern A–F for a click; "Apply again" per species, a feed
line, pictures loaded only on screen and kept as thumbnails, Refresh
and Reload pictures. With it: Settings pages build on first look and in
batches (the dialog opened slowly with the waypoint packs imported),
and the Waypoints list changes in place. Verified in-game by the owner
over two days: applying works, all pictures load — served by
islapandora.eu itself at full size, ~4.7 MB each, 72 available + 15
locked on the owner's account — and a too-early apply is refused with a
message stating when the next skin can be applied, shown as the
server's words), v1.29.0 (Oct 5 — **areas**, the owner's idea: "which
area am I in" from a bundled colour-coded area map (see AreaMap under
Architecture and `tools/area-map`). Three parts: the minimap's area
pill (top-right, the owner's corner; the area under the cursor in edit
mode; "Uncharted" where none is named; a blink on a crossing),
"Entered …" lines plus the area in spawn lines (yours and friends') in
the Activity feed, and the border layer on the minimap (control panel →
Areas: vector lines, your area outlined in a soft light line, the
hovered area in orange). 27 areas: VulnonaMAP's 26 labels plus Central
Dome, with generated borders the owner accepted as the first version
and then began correcting by hand (Oct 5, Central Dome painted larger).
Tried in game by the owner Oct 2–5 — the pill, the feed line (which
they had wake the fade like any other line) and the borders; the spawn
lines, the blink, "Uncharted" and the two highlights were checked by
tests and off-screen renders only when the owner called the release),
v1.30.0 (Oct 6 — **one-click updates and the settings move**, the
release that changes how the overlay is distributed: every release is
now an installer, a self-updating zip and the plain zip (Velopack, see
Updater and the release files under Conventions); the tray's update
entry installs for a click; settings live in `%AppData%\PandoraOverlay`
and are copied over from the old folder on the first start (DataFolder);
Settings gains the About page; all four size sliders default to 100%
(a fresh install's minimap had started at 81%). Rehearsed as rc.1–rc.3
pre-releases on the owner's PC and on two more PCs running Defender
before the tag; the About page shipped without an rc of its own, tried
on the repo build only),
v1.31.0 (Oct 7 — **sign in with Discord inside the overlay**, replacing
the DevTools cookie copy: Settings → Account's one button opens the
website's own Discord login in a private WebView2 window (SignInWindow)
and the overlay takes the site session when Discord sends the player
back; the paste box and its walkthrough are gone (the config.json
`Cookie` inbox stays as the undocumented emergency route); the Account
card with Sign in again / Sign out (which logs out on the website too)
and the LinkID shown when Steam isn't linked; a session that ends reads
"Session ended · sign in again from the tray" with a tray entry instead
of retrying for good; the installer fetches the WebView2 Runtime where
missing, the plain zip relies on the one already there. Rehearsed the
same evening as rc.1 (one PC fresh, one over 1.30.0 with its stored
session) and rc.2 (the in-app update with the WebView2 DLLs in the
package) on the owner's PCs — all as expected; a PC without the WebView2
Runtime remains untested),
v1.31.1 (Oct 8 — the Account card shows the Discord avatar instead of
the initial: downloaded once from Discord's image server without the
session, only after the page's first look, kept as a small copy, dropped
on Sign out — tried by the owner on the repo build; no pre-release, the
owner's call for a cosmetic patch),
v1.31.2 (Oct 8 — the minimap no longer waits for a failed calibration
fetch: a bundled seed stands in until the site answers, and a failed
fetch is retried after a successful poll at most once a minute — a
player's report the same day; tests only, no pre-release),
v1.32.0 (Oct 8 — **the update card**: the overlay's own centred card at
launch, once per version, with the release's notes and Install and
restart (the tray entry's very action, with a progress bar), an Updated
card once after a version change (by version comparison, so a Setup.exe
or zip over an old copy counts too), About's What's new; the release
notes cut from the CHANGELOG into the update package and the GitHub
release text, GitHub's commit list dropped; the plain zip dropped.
Rehearsed the same day as rc.1 (installed over 1.31.2: the Updated card)
and rc.2 (the Available card on rc.1, Later, the tray install, the
Updated card with "you were on", and the card's own progress bar after
a reinstall of rc.1 with `UpdateCardShownFor` cleared by hand — the
settings folder survives an uninstall, so once-per-version did too).
No layout presets beyond the default for now.

Areas, left for later and not started: area names in new waypoints and
share codes. Further border corrections are painted into
`Assets/areas.png` (see `tools/area-map/`). Dropped: caves and
elevation.

Later/maybe: zone overlays
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

Updater TRIAL, Oct 5 2026 — the trigger above has fired (126 downloads
each for 1.27 and 1.28, single digits before the Discord channel), a
plan for 1.30.0 was drafted in conversation and Velopack 1.2.161 was
tried LOCALLY, unsigned, on a throwaway branch (nothing pushed, a
separate "PandoraOverlayTrial" identity, a Desktop folder as the update
feed). Seen on the owner's PC (SmartScreen on, Bitdefender active,
Defender and Smart App Control off — so those two are UNTESTED): the
installer downloaded by a browser gets the SmartScreen prompt once; the
in-app update got NO prompt (no installed or updated file carries the
download mark), took ~2 s from start to the overlay being back, and the
settings kept outside the app folder survived it. Sizes: Setup.exe 11.5
MB, zip 4.2 MB (Velopack's Update.exe alone is 5 MB). The ZIP UPDATES
ITSELF in place the same way (tried: SmartScreen once on the first run
of the downloaded copy, none on the update, and it leaves an installed
copy's Start menu and installed-apps entries alone); its launcher is
named after `--packTitle`, not the main exe, so the title must be
exactly "PandoraOverlay" for "unpack over the old folder" to replace
the old exe. Installed copy and zip copy share the settings folder.
UNINSTALL removes the app folder, the Start menu entry and the
installed-apps entry, and LEAVES the settings folder and Velopack's own
log folder (`%LocalAppData%\Velopack`). A SECOND ROUND (Oct 6) fetched
the update over HTTP from a small server on the owner's PC instead of
a folder: Velopack's own client (UA `Velopack/1.2.161`) got the release
list and the 7 KB DELTA package, rebuilt the full package locally,
applied it in ~2 s, again with no prompt and no download mark on any
file — so the folder result was not an artefact of the folder. The
owner also ran the first round on an office PC with SentinelOne as the
security agent: SmartScreen once on the installer, nothing on the
update, uninstall clean. Defender and Smart App Control remain
untested. vpk wants `--runtime win-x64` (it defaults to x86) and
`--shortcuts StartMenuRoot` (it defaults to a Desktop shortcut too), and
warns unless `VelopackApp.Build().Run()` is the first line of a real
`Main()`. Signing: SignPath Foundation is the free route but signs only
as a pipeline step, which fits Velopack badly (a project using the pair
still ships two helpers unsigned inside the update package); Azure
Artifact Signing is closed to EU individuals.
**OWNER'S DECISION on hard constraint 1 (Oct 5 2026):** Velopack's
Setup.exe and Update.exe list EVERY running program on the PC to find
and stop copies running from the overlay's own folder ("Inspected 335
running processes" in their log — once per install, three times per
update), so a running game is among what they look at. The owner
ACCEPTS this as outside what the rule is meant to stop: it happens only
on an install / update / uninstall click and compares exe locations
with our folder. It is not a licence for OUR code: the overlay itself
still never lists, opens or looks at any process.

BUILT Oct 6 2026 for 1.30.0 (the owner: "I'd bundle step 1 and 2 together"
— the settings move and the updater in one release, rehearsed as
pre-releases first): `DataFolder` + the migration; `Updater` +
`MainWindow.Updates.cs` + `Program.Main`; the tray entry that installs;
Settings → About (added after rc.3: it took the update checkbox and
button from General and the import from Account, where they first sat);
the release workflow packing three files (see Conventions). Decisions taken
with it, against the Sep 2026 text above: the ZIP UPDATES ITSELF too
(the trial showed it can, and "unpack over your old folder as always"
is the smoothest last manual update — "portable stays fully manual" is
superseded; the plain zip remains for anyone who wants that); settings
in `%AppData%\PandoraOverlay` for EVERY flavour, copied, never moved;
`UpdateChecker` stays as the plain copy's notice instead of retiring;
no code signing (SignPath Foundation may be applied for separately —
the owner's side). Plan for shipping: tag `v1.30.0-rc.1` (pre-release;
the owner installs fresh AND unpacks over the old folder; a few
volunteers, one on Defender), then `rc.2` to click the update, then
`v1.30.0`. THE RC ROUNDS (Oct 6 2026): rc.1 installed over the moved
settings and found them; rc.2 was the first update from GitHub to an
installed copy — the 80 KB delta, applied in ~2 s, and the update CREATED
the newly listed Desktop shortcut; rc.3 changed the title to "Pandora
Overlay" and the update RENAMED both shortcuts in place (an install that
lived through the rename keeps the old launcher exe beside the new one;
harmless, only rc.1/rc.2 installs have it). The owner then tried rc.1 →
rc.3 on two more PCs, a Windows 10 and a Windows 11 one, BOTH ON DEFENDER:
the SmartScreen box once on the installer, nothing on the updates, no
block — so Defender, the biggest open risk, is cleared. Still unverified:
Smart App Control (nobody had it on), a PC without .NET 8 (the installer's
runtime fetch). DECISIONS AROUND THE RELEASE (owner, Oct 6): the PLAIN
ZIP ships with 1.30.0 as the fallback without Velopack's helper files
and is to be DROPPED in a later release if nobody turned out to need
it (DROPPED in 1.32: one download each for 1.30.0 and 1.31.0). The
update NOTICE stayed as designed — the status line once, the tray entry
and tooltip for the session, no pop-up — until 1.32's update card
(see UpdateCardWindow; a tray balloon was considered first and
dropped, Oct 8 2026). The README's DOWNLOAD BADGE will inflate:
Velopack's check reads `releases.win.json` from each of the last ten
releases at every launch (`GitBase.GetReleaseFeed` merges them) — the
owner chose to ignore that and remove the badge if it gets silly; the
fix, if ever wanted, is an API-first check (ask GitHub's API for the
newest tag, which is not a counted download, and let Velopack fetch
only when it is newer). Read in Velopack's source: `GithubSource`'s
prerelease flag means pre-releases AND stable releases, newest first —
so rc installs are offered the final, and `vpk download github --pre`
in the workflow fetches the newest release of either kind for the delta.

BUILT Oct 7 2026 for 1.31.0 (RELEASED the same evening as v1.31.0 after
the rc.1 / rc.2 round, see the shipped list): **the in-app sign-in**,
replacing the DevTools cookie
copy (the owner: tricky for non-tech-savvy users and not the most secure
option). Settings → Account's "Sign in with Discord" opens SignInWindow —
the website's own Discord login in a private WebView2 — and the overlay
takes the site's session from the browser when Discord sends the player
back; the tray's "Sign in again…" does the same when the session ends.
Decisions (owner, Oct 7): the owner's okay suffices for `auth/me` +
`/auth/logout` under the blanket go-ahead; the window browses in private
(Discord's login is never on disk); the cookie paste box is CUT from the
UI — the `config.json` `Cookie` inbox stays as the undocumented emergency
route; Sign out also logs out on the website; Steam-not-linked is
explained (with the LinkID) rather than walked through site pages;
signing in acts at once, not on Save. Phase 0 spike on the owner's PC
(`Desktop\Pandora Overlay Files\pandora-webview-sign-in\`, PLAN.md §10):
Discord's login works in the private WebView2; the callback 302s to `/`,
and connect.sid — the ONLY site cookie, HttpOnly, 30 days — is set by
then; NO cf_clearance exists; a plain HttpClient passes with the
WebView's UA and the old one alike; a dead or missing session is `403
{"error":"Forbidden"}`; `vpk --framework webview2` is accepted; and
(clicked later that evening) `GET /auth/logout` answers `302 Found` → `/`
with a Set-Cookie replacing connect.sid, after which `auth/me` on the old
cookie is `200 {"ok":false,"authenticated":false}` — a 200 with no `user`,
NOT a refusal, which is why `ParseAccount` reads `authenticated` and the
Account page says "session has ended" on it — and `mylocation` is the
403 JSON refusal, so a session ended on the website is caught like a
dead one. Tried in game by the owner Oct 7, and the rc round the same
evening (one fresh install, one over 1.30.0, the rc.1 → rc.2 in-app
update) passed; still untested: a PC without the WebView2 Runtime.
The README's first-run section was rewritten; its three setup screenshots
need retaking by the owner.

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
  with no hotkey — "Server rules…" (Sep 29 2026; Check Prime sat here
  until it earned Ctrl+F8 in v1.19). The tray must never grow with the number
  of widgets: no per-widget show/hide. A mid-game action used often earns a
  **hotkey** instead of a tray line (the heatmap and Check Prime did).
- Every distinct widget gets its OWN size slider in Settings (owner's rule,
  Sep 2026): stats panel `UiScale`, minimap `MinimapScale` (percent since
  v1.25, pixels before), prime tracker
  `PrimeScale`, activity `ActivityScale` — a new widget ships with one
  (override `AppearanceScale`). ALL FOUR BEHAVE ALIKE (owner, Oct 6
  2026): plain numbers defaulting to 1.0, no seeding from another widget,
  no migration. Until then each new scale was seeded in `Load()` from an
  older one (Prime from UiScale, Activity from Prime, the minimap from
  its pre-1.25 pixel size) so an update never resized anyone's widget —
  and the last of those also ran on a FRESH config, where the pixel
  default of 230 over the 284 px map put a new install's minimap at 81%
  while the other widgets started at 100% (the owner noticed Oct 6).
  Dropped altogether: every player on a public release already has all
  four scales saved; only friends-era files from before v1.15 / v1.24 /
  v1.25 see a one-time size change. Sliders are independent:
  no global scale multiplier on top (considered and dropped — Windows
  display scaling already does it, two multiplying sliders confuse, and
  scaling everything at once breaks docked/snapped layouts).
- Fail soft: config/crypto/HTTP errors degrade to a UI state, never crash.
- Keep files well under ~500 lines; current style is regions + XML doc
  comments. A window that outgrows it gets another partial-class part
  (`Window.Topic.cs`, see Layout), not a folder shuffle.
- Versioning: SemVer. The csproj `<Version>` is the single source of truth;
  bump it each release and tag the commit `vX.Y.Z` (annotated). Features bump
  minor, fixes bump patch. Current: 1.32.0.
- Release model: main moves freely between releases; tags mark the stable
  points. Anyone wanting "a version" uses a tag or its GitHub Release (pushing
  a `vX.Y.Z` tag triggers the workflow that builds, tests and attaches the
  files) — never a random commit. No standing release/version branches.
  THE RELEASE FILES (v1.30, `.github/workflows/release.yml`): the workflow
  first checks that the tag equals the csproj `<Version>` (else it fails),
  publishes, cuts the version's RELEASE NOTES out of CHANGELOG.md (1.32;
  `## [Unreleased]` for a pre-release tag; a missing or empty section
  fails the release) into `notes.md` and `release-body.md` (the notes,
  then the "Which file?" block), then `vpk download github` (the
  previous release, for a delta; allowed to fail) and `vpk pack` (id `PandoraOverlay`, title "Pandora Overlay" — the title is
  the name on the shortcuts, in the Start menu and in the installed-apps
  list, AND the name of the zip's launcher: the owner chose the readable
  name (Oct 6 2026) over a launcher that replaces the old `PandoraOverlay.exe`
  when the zip is unpacked over an old folder, so the old exe stays there
  beside `Pandora Overlay.exe` and the docs say it can be deleted; `--runtime win-x64`, `--framework net8.0-x64-desktop`,
  `--shortcuts Desktop,StartMenuRoot` — the owner wants a Desktop shortcut too, Oct 6 2026; `--releaseNotes notes.md` since 1.32, which the update card reads), renames Setup.exe and the self-updating
  zip to `PandoraOverlay-vX.Y.Z-Setup.exe` / `PandoraOverlay-vX.Y.Z-win-x64.zip`
  and attaches everything in `Releases/` — the full and delta `.nupkg`,
  `releases.win.json`, `RELEASES`, `assets.win.json` are what the
  updater reads and keep Velopack's names. A tag containing "-"
  (`v1.30.0-rc.1`) is published as a PRE-RELEASE: players' overlays never
  see it (the old checker uses `/releases/latest`, Updater passes
  `prerelease: false` for a release build), only a pre-release build
  offers it — that is how a release is rehearsed with testers before the
  real tag. The release body is `release-body.md` (the notes + the two
  files for people; GitHub's auto-generated commit list was dropped in
  1.32 — commit titles are written for the code). The plain zip was
  dropped in 1.32 too. A pre-release
  tag also needs the csproj `<Version>` set to `1.30.0-rc.1` (SemVer; the
  SDK derives assembly version 1.30.0.0 from it).
- Never move or re-tag an existing tag. If a release ships broken, fix forward
  and tag the next patch version.
- Hotfixing an old release while main holds unreleased work:
  `git switch -c fix vX.Y.Z` → fix → bump patch in csproj + CHANGELOG →
  tag `vX.Y.(Z+1)` → push the tag (release builds automatically) →
  merge/cherry-pick the fix back to main → delete the branch.
