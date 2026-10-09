# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server): the
player's own dino stats in always-on-top widgets, plus a minimap, the map, a
tray icon and a settings dialog. **v1.34.0 is released, working, and endorsed
by the server team.**

**Where the detail lives.** This file holds only what applies everywhere, and
what has no code to sit beside: the constraints, the permissions, the
conventions, the designs the owner turned down. Everything about how a
particular class works — its reasons, pitfalls, rejected implementations and
the owner's calls on it — is written as comments in that class (the code is
commented heavily on purpose) and pinned by its tests. Read a feature's files
before proposing a change to it. A few folders also have
`.claude/rules/*.md`, conventions for writing new code there, which load when
a file in the folder is touched.

## Hard constraints (never violate)

1. **Fully external, always.** Never read game memory, inject, hook, enumerate or
   touch the game process in any way. The Isle runs Easy Anti-Cheat. The ONLY data
   source is the islapandora.eu web API. If a feature seems to need game-side data,
   the answer is no. One accepted edge (the owner's reading, Oct 5 2026):
   Velopack's Setup.exe and Update.exe list every running program to find and
   stop copies running from the overlay's own folder ("Inspected 335 running
   processes" in their log), so a running game is among what they look at. It
   happens only on an install / update / uninstall click and compares exe
   locations with our folder. It is not a licence for OUR code: the overlay
   itself never lists, opens or looks at any process (`Process.Start` appears
   only to hand a URL — or the settings folder, About → Open folder — to the
   Windows shell). It activates only its OWN windows (the
   control panel, the map, dialogs; our hotkey press grants the right); the
   game window is never found, focused or touched.
2. **Approved endpoints only**, each with the trigger and pace below. Nothing is
   added, and nothing goes faster, without the owner's explicit okay.

   | Endpoint (islapandora.eu) | Cookie | Triggered by | At most |
   |---|---|---|---|
   | `POST /api/map/mylocation` (empty body) | yes | the poll timer; user nudges (edit mode, un-hide, opening Settings → Skins, a Check Prime or skin apply on a stale not-in-game state) | per constraint 3 |
   | `POST /api/map/calibration` (empty body) | yes (works without) | once per launch, and after a credential swap | a FAILED fetch is retried right after a successful poll, at most once a minute, until one succeeds; a bundled seed (`Assets/calibration.json`) stands in meanwhile |
   | `GET /map/api/heatmap-status` + `GET /map/heatmap-live.png` | **no** (the site headers are sent) | only while a heatmap layer is on: the minimap's (the minimap widget switched on, its layer on, and the overlay not hidden by hide-all or the auto-hide) or the open map's own Heatmap layer | never two fetches less than 60 s apart, whatever triggers them: within a minute the kept picture is reused, and a fetch re-arms the 60 s timer (the site's page: 10 s per tab, while its layer is on) |
   | `POST /api/prime/check` | yes | USER ONLY: the control panel's Check or the Check Prime hotkey, never a timer | the server's cooldown, mirrored locally: cooling down or not in game = no request (a STALE not-in-game state first gets one regular `mylocation` poll); 15 s floor |
   | `POST /api/prime/cooldown` | yes | exactly once after each SUCCESSFUL check (the success answer carries no cooldown, and its length varies with supporter rank; the site button's hard-coded 5 min is only the fallback when this request fails) | once per check |
   | `POST /api/map/friends` (empty body) | yes | only while a friends surface is switched on (the Activity widget with friends' events included, the minimap's friend arrows, the open map's Friends layer; all off = zero requests; hide-all and the auto-hide don't stop it, on purpose: the friend chime and the Activity log work while hidden): right after a successful `mylocation` poll, or once for a hot trigger (a surface shown, a Settings save) when the last poll succeeded and the last roster is at least one poll interval old | every 2nd in-game poll, every idle poll, never two within one poll interval (the site: with every poll at 5 s); read-only |
   | `POST /api/skins/patreon-skins` (empty body) | yes | opening Settings → Skins; Refresh | reused 10 min, 30 s floor |
   | `POST /api/skins/apply-patreon` `{skinId, patternIndex}` — **WRITE** | yes | a click on a pattern button | 15 s mash guard; not in game answered locally |
   | `POST /api/auth/me` | yes | once right after a sign-in; once per dialog when Settings → Account is looked at | never on a timer |
   | `GET /auth/logout` | yes | a Sign out click | once |
   | `POST /api/user/dinos` (empty body) | yes | every opening of Settings → Dino storage (as the website's page does, the owner's call); Refresh | one at a time |
   | `POST /api/user/dinos/rename` `{dinoId, name, description}`, `POST /api/user/dinos/delete` `{dinoId}` — **WRITES** | yes | a card's rename Save; the armed Delete | one at a time, 3 s breather |

   Also allowed: the sign-in is the website's own `/auth/discord` flow, browsed
   by the player in the overlay's WebView2 window (SignInWindow), which stops
   the navigation after `/auth/discord/callback`, so no site page ever renders
   in it. Pictures from addresses the site names (skin tiles; the account's
   Discord avatar on Discord's image server, the one picture from outside the
   site, the owner's okay Oct 8 2026) are plain GETs from a separate client
   with NO cookie and none of the site headers, only for what is on screen,
   once each (kept as small copies). The GitHub releases API (the launch
   update check, `UpdateCheckEnabled`) and the update package (downloaded only
   for a click) are not islapandora endpoints and sit outside this constraint.

   NEVER called: the friends page's management calls (`/api/friends/data`,
   requests, accept/decline, block, `/api/preferences/*` — friends are managed
   on the website), the admin / staff endpoints (`/api/admin/*`, the skin-store
   and admin-skin editors). NOT cleared: the zone overlay images (see
   Permissions).
3. **Poll interval >= 2 s** (default 3 s, matching the website's own cadence).
   Server-side rate limit is 300/window; the dev specifically praised the
   polling restraint. The cadence is adaptive, DOWNWARDS ONLY: the configured
   interval is the in-game pace; not in-game (or 5 straight failures) idles at
   15 s, then 60 s after 10 min. Off-schedule polls (user nudges, a new
   session's first poll) are floored at the configured interval and re-arm the
   timer — nothing may ever poll faster.
   A WRITE happens only for a click, never on a timer.
4. **The cookie is a credential.** Never log it, print it, put it in exceptions,
   window text, or commit it. Error paths surface exception *type* only. It is
   stored only as the DPAPI blob (the rolled cookie re-encrypted on edit-mode
   lock and on exit). Server text reaches a window only mapped, or through
   `PatreonSkins.Clean` (which drops whole any string mentioning `connect.sid` /
   `cf_clearance`) and labelled as the website's words.
5. `config.json` is runtime state (holds the DPAPI blob) — stays in `.gitignore`.

## Permissions status

- Approved by **instantnameofficial** (site dev, via admin Emilyana, Discord
  ticket Sep 2026): use of the overlay, explicitly including a **minimap** from
  the same endpoint. Distributing the app / public source is fine — it's a
  simple tracker and the server team has no problem with such tools.
- Approved Sep 15 2026 (same dev): the **heatmap layer** — mirroring the
  live-map page's pre-rendered `/map/heatmap-live.png` (+ its status endpoint).
- Approved Sep 19 2026 (relayed by the owner): the **Prime tracker** —
  rebuilding the live-map page's "Prime Check" box as an overlay widget.
- Approved Sep 27 2026 (relayed by the owner): **friends** — the
  `/api/map/friends` roster the live-map page polls. Consent stays the
  server's: a friend who enabled "hide my location" arrives flagged
  `hideLocation` and is never placed anywhere — no arrow, no distance, no
  "nearby" line, no area. Nicknames/colours are local and never leave the PC
  (the site's nicknames are browser-localStorage too, no API).
- Server rules (Sep 29 2026): the site has NO rules endpoint — `/rules` is a
  client-rendered page whose rules and pack-limit table are baked into the
  frontend bundle (which itself says the Discord rules have priority "due to
  the website requiring updates"). The overlay ships a DATED COPY as
  `Assets/rules.json` and makes no request for it; scraping the bundle at
  runtime was rejected (fragile minified names, ~600 KB per launch, uncleared
  traffic). A `GET /api/rules` ask to the dev is PENDING (the owner will ask);
  when it exists it follows the calibration pattern (once per launch, cached,
  the seed only a fresh install's first content).
- **Blanket go-ahead, Oct 1 2026** (the server owner, Tar, relayed by the
  owner; the overlay is endorsed and has its own announcement channel):
  "anything the site can do, we can do too. No need to ask now." So a normal
  player-facing endpoint of islapandora.eu no longer needs a per-endpoint ask
  to the site dev — it needs the OWNER's okay for the feature, as always. Used
  so far for the Patreon skins page, the sign-in's account pair and Dino
  storage. Such a feature mirrors the website's own page: the same requests
  and bodies, its input limits and fallbacks, never a faster pace than the
  page's. What still holds whatever the endpoint: fully external; the cookie
  is a credential; nothing polls faster than the site; a WRITE happens only for
  a click; the admin / staff endpoints are never called.
- **NOT approved:** the zone overlay images (the live-map bundles patrols /
  sanctuaries / migrations / salt rocks as static PNGs) — ask the dev first. A
  read-only API token feature was pitched to him; if it ships, auth migration
  happens inside `OverlayConfig.GetCookie/SetCookie` + the `PandoraClient`
  header — nothing else changes.

## What the API tells us

The player's live state comes only from `POST /api/map/mylocation`:
`{"inGame":false}`, or `inGame:true` with a `player` record — steamId, name,
dino, gender; growth, health, stamina, hunger, thirst as 0–1 floats; yaw
(facing, degrees); head / body / legs fractured; x, y, z in Unreal world cm.
Nothing else about the player's live state exists (the friends roster
carries the same record per friend). Auth is the `connect.sid` session
cookie only. The request headers, the response shapes, the site's refusal and
the calibration are in `.claude/rules/core.md`, which loads with `Core/`.

## Stack & build

- .NET 8, WPF, x64. Three NuGet deps: `System.Security.Cryptography.ProtectedData`;
  `Velopack` 1.2.161 — the SAME version as the `vpk` packer the release
  workflow installs, bump both together; `Microsoft.Web.WebView2` (the sign-in
  window; needs Microsoft's WebView2 Runtime — part of Windows 11, on nearly
  every Windows 10 through Edge, and the installer fetches it where missing).
  Velopack is used only by `Program.cs` and `Core/Updater.cs`, WebView2 only by
  `Account/SignInWindow` — keep the packages there.
  WinForms interop (`UseWindowsForms`) enabled solely for the tray NotifyIcon.
- `dotnet build -c Release` → `bin/Release/net8.0-windows/PandoraOverlay.exe`.
  `PandoraOverlay.sln` also carries **PandoraOverlay.Tests** (xUnit; `dotnet
  test`; CI runs it on every push). The app csproj globs the repo root, so the
  test subtree is `Compile Remove`d from it.
- The working copy is LF (`.gitattributes` `eol=lf`; the solution file CRLF).
- The user's files (`config.json`, `waypoints.json`, `friends.json`, the
  picture caches) live in `%AppData%\PandoraOverlay` (`DataFolder`); the
  install itself is Velopack's, under `%LocalAppData%\PandoraOverlay`.
- The golden-render harness guards the minimap's drawing:
  `Desktop\Pandora Overlay Files\pandora-big-map-sketch\golden-renders\run-golden.ps1`
  (62 renders compared with a baseline; see the `release-pass` skill). The
  map's plan and decisions (D1–D12) live beside it: `ACTION-PLAN.md`, Fable's
  `PLAN.md` and the perf spike's `perf-spike\RESULTS.md` (code comments cite
  the D-numbers).

## Layout

Files are grouped BY FEATURE — a widget, its pure helpers and its Settings page —
not by layer. ONE flat namespace `PandoraOverlay` on purpose (`.editorconfig`
silences IDE0130); the SDK-style csproj globs subfolders, so moving a file needs
no project edit. Big classes are PARTIAL CLASSES split by concern
(`MainWindow.Hotkeys.cs`, `SettingsWindow.Skins.cs`, `PollService.Storage.cs`):
same class, same fields — a reading aid, not decoupling; the pure helper classes
are the real decoupling. A feature's requests live in
`Core/PandoraClient.<Topic>.cs`, its gating in `Core/PollService.<Topic>.cs`,
its page in `Settings/SettingsWindow.<Topic>.cs`.

- `Core/` — the plumbing: PandoraClient (HTTP), PollService (the timers and
  EVERY request gate), OverlayConfig (config.json + the DPAPI vault),
  DataFolder, Updater (Velopack) + UpdateChecker (the fallback notice),
  HotkeySpec, StartupRegistration.
- `Shell/` — MainWindow (the orchestrator, and also the stats panel),
  OverlayWindowBase, WidgetFrame, DefaultLayout, SnapResolver, SnapGuideWindow,
  ControlPanelWindow, TrayIcon.
- `Stats/` — the stats panel's trackers and rules.
- `Minimap/` — MinimapWindow; MapCanvas (the drawing every map shares) and the
  shared map menu; the area map (AreaMap, AreaBorders, AreaMapAsset); the pure
  map helpers.
- `BigMap/` — BigMapWindow ("the map" to players) and ScrimWindow, its backdrop.
- `Waypoints/`, `Friends/`, `Activity/`, `Prime/`, `Rules/`, `Skins/`,
  `Storage/` (Dino storage), `Account/` (the in-app sign-in), `Updates/` (the
  update card), `Settings/` (the paged dialog, one partial per page).
- `Assets/` — `map.png`, `app.ico` (WPF Resources) and the embedded data
  (`rules.json`, `calibration.json`, `areas.png` + `areas.json`; the CHANGELOG
  is embedded too, for the update card).
- `App.xaml(.cs)` (single-instance mutex, the settings move) and `Program.cs`
  (the entry point: Velopack's start-up call first, then WPF).
- `PandoraOverlay.Tests/` mirrors the folders.
- `packs/` — DATA, not code: five waypoint packs players import (with a README
  and an import walkthrough), converted once from VulnonaMAP's Gateway label
  data (vulnona.com, community-made): world cm = its Lat/Long × 1000, Long → X,
  Lat → Y (checked against hand-placed waypoints and a plot on
  `Assets/map.png`). Ids are deterministic (MD5 of "vulnona-gateway/<pack>/<record
  name>"), so a regenerated pack re-imports as duplicates. A credit line in
  each file (`Source`, `CopiedOn`) and in the README is enough (owner's call);
  the overlay never contacts vulnona.com; not in the release. Left out: fence
  gates, caves, air currents, other food spawns, zone shapes. These are
  user-imported waypoints from a community map — NOT the site's unapproved
  zone-overlay images (salt rocks included).
- `tools/area-map/` — the area map's generator and preview scripts, not part of
  the app. `Assets/areas.png` is HAND-CORRECTED: never re-run the generator over
  it without the owner's word. And nothing is ever written for one named area —
  everything about a particular map is input (owner's rule; see
  `.claude/rules/maps.md`).
- `.claude/rules/` — conventions for writing new code in `Settings/`, the maps
  (`Minimap/`, `BigMap/`, the area tool), `Shell/` and `Core/`; each loads when a
  matching file is touched. `.claude/skills/release-pass/` — the release
  procedure (see Releases).

## Architecture

- Dependency rule: `MainWindow` → { `PollService`, `OverlayConfig`,
  `WaypointLibrary` } and `PollService` → `PandoraClient`.
  `PandoraClient`/`OverlayConfig`/`WaypointLibrary` have zero WPF references —
  keep it that way. Windows receive the library like the config (pure
  consumers; the minimap redraws on its `Changed`, MainWindow saves on it).
- ONE request stream: PollService owns the client, the timers and the busy
  guard, and every request goes through its gates; windows are pure consumers
  of its events. N windows, still one stream — that is what preserves
  constraint 3.
- Every widget and the control panel derive from `OverlayWindowBase`
  (click-through, no-activate, edit-mode drag with snapping, appearance, the
  fade); the map, its backdrop and the snap guides are their own windows.
- MainWindow owns whatever must outlive a widget being hidden: the config, the
  PollService, the waypoint library, the friend book, the activity log and its
  feeds, the area journal, the trail, the map menu's actions.

## Runtime expectations

- Game must run **borderless windowed** (overlay can't beat exclusive fullscreen).
- `inGame:false` during server restarts/menus is normal — UI shows "Not in-game"
  and self-recovers. `getplayerdata` upstream only includes spawned players.
- Growth (owner's server knowledge, Sep 2026): Isla Pandora runs a **1.3×**
  growth multiplier vs official; total grow time differs per species; growth
  does NOT appear to pause when starving/dehydrated. GrowthTracker measures
  the effective rate, so none of this needs configuring.
- Status line shows the last update; "Disconnected · retrying (TypeName)" on
  errors; the site's refusal twice running → "Session ended · sign in again
  from the tray" and the tray's "Sign in again…" entry.

## Roadmap

The release history is CHANGELOG.md (player-facing; the update card shows it).

**Never tested in a real session** (checked by tests or renders only, or not at
all) — say so when it matters:
- Dino storage's Delete (1.33; the owner kept their dino).
- The map's own friends rule (tests only), weaker PCs, high DPI (1.34).
- A PC without the WebView2 Runtime (1.31; the installer's fetch).
- Smart App Control; a PC without .NET 8 (the installer's runtime fetch) (1.30).
  Defender, Bitdefender and SentinelOne were tried: SmartScreen once on the
  installer, nothing on updates.
- The calibration retry after a failed launch fetch, and the bundled seed
  standing in (1.31.2; tests only — a real failure never seen in game).
- From the areas (1.29): the crossing blink, "Uncharted", and the edit-mode
  hover outline (tests and renders only at release).

Open: the README's setup screenshots need retaking for the in-app sign-in
(the owner's, since 1.31).

PLANNED (owner, Oct 9 2026), design NOT decided: **app-wide logging**.
- The goal: a local log of the fetch requests and the app's state, so that when
  a player hits a crash or odd behaviour there is something to go on.
- How it works (what is logged, where it lives, how big it grows, whether it is
  on by default, how a player hands it over) is decided in a separate session.
  Don't build it before then.
- The model is the temporary request log of Oct 9 2026 (used for the map's
  in-game request count, then removed): a DelegatingHandler around each
  HttpClient writing one line per request — the time, the method, the path
  without the query, the status, the duration and the request gates' state —
  plus notes marking the map opening and closing. Its two rounds caught heatmap
  fetches 15 s and 32 s apart (hence the one-minute rule).
- Whatever the design, constraint #4 holds: never the cookie, a header or a
  body. Nothing leaves the PC unless the player sends it.

Areas, left for later and not started: area names in new waypoints and share
codes. Further border corrections are painted into `Assets/areas.png`.

Later/maybe: zone overlays (needs permission); official token auth (the nudge
went out with the heatmap ask ~Sep 14 2026, unanswered); Segoe Fluent Icons for
stat glyphs; layout presets beyond the default; splitting the stats panel out
of MainWindow into its own window (a refactor); if the Patreon skins page (a
pilot) works: a hotkey for "apply my skin", favourites, the custom presets
(other endpoints); the Lucky Wheel, Dino storage's sibling on the website
(owner: "ignore it for now"); code signing (SignPath
Foundation is the free route, the owner's side; Azure Artifact Signing is closed
to EU individuals).

## Decided against — don't re-propose

The owner turned these down; bring one back only if the owner raises it.
(Implementation-level rejections are commented in the code they concern.)

Widgets and the stats panel
- A widget that changes size (see Conventions): a shorter Combat panel (the
  player's literal ask) was turned down for the frame's sake.
- Emphasis by size: large health/stamina bars, big percents, bigger badges were
  rejected on sight — size is not how this overlay emphasises anything.
- Content changing by itself: no automatic switch to the Combat view on damage.
- The Combat view always lit (combat can be someone's default view; the fade
  would silently be off for them).
- Hunger/thirst estimates beyond 1 h (3 h read as clutter).
- Bundled or custom alert sounds: the Windows Exclamation sound; a custom one
  only if players ask.
- A two-row control panel (one row, grouped by widget).
- A global scale multiplier on top of the per-widget sliders (Windows display
  scaling already does it, two multiplying sliders confuse, and scaling
  everything at once breaks snapped layouts).
- Vertical centring of the default layout (it runs into the game's
  bottom-right stat hexes).
- Per-widget show/hide or Check Prime in the tray.
- Persisting the hidden state; an auto-hide reveal that "stays until the next
  spawn" (tried Sep 24 2026: it read as a bug).

Activity and friends
- A row-per-friend Activity list (it would resize with the roster; the arrows
  already say who is where), and an Activity rows setting.
- A friend's stats anywhere on the overlay — including a stats line for the
  tracked friend in the minimap footer (the footer is navigation only).
- A friends-only feed: your own events are always on, friends' are the
  default-on extra.
- Per-kind wake rules: any new line wakes the Activity widget (owner, Oct 5 2026).
- Feed lines for chores: waypoint edits, layer toggles, connection blips, Dino
  storage rename/delete, updates.
- A sound in the Activity widget, or the friend alert as an Activity option (it
  is MainWindow's, on the Friends page).
- Managing friends in the overlay (add, remove, requests, block, privacy): it
  stays on the website.

Maps
- A rotating (facing-up) minimap mode (Sep 2026).
- The map as a widget: no position, snapping, scale slider, Settings page or
  remembered zoom — it opens fitted to the land every time.
- Walking time in the map's cursor readout (the dino stands still while the map
  has focus).
- A name box in the map's right-click menu (names are typed on Settings →
  Waypoints); timing rules on that menu, or swallowing the click that closes it.
- Area borders in light lines, in the area colours, or cut down (a land mask,
  per-case outlines): every line, in dark navy (owner, Oct 2 2026).
- "Uncharted waters", or any word that claims water: it is "Uncharted".
- Caves and elevation in the area map: dropped, not deferred.
- Imported waypoint packs arriving visible: an import must never bury the map.

Settings and site features
- Mid-game flips in Settings (the heatmap checkbox was removed: a layer you flip
  is not a preference).
- A hidden warm-up to speed up the first opening of Settings ("we can live with
  this"); a cover or spinner over a list while it builds.
- An "Updates" page (About instead); an underlined "What's new" link (a button).
- Stock WPF chrome, the stock TabControl.
- Patreon skins as a widget or a control-panel button (a widget needs clicks and
  isn't watched); a local skin cooldown (the server's may vary by rank, like
  Prime's).
- Storing or retrieving dinos (the website can't either).
- Scraping the site's frontend bundle for data (see Permissions → Server rules).

Updates and the account
- Installing or downloading an update without a click: no "install
  automatically", no silent pre-download (Sep 2026).
- Tray balloons / Windows notifications for updates (Windows can snooze or
  swallow them): the overlay's own update card.
- The plain zip (dropped in 1.32).
- The cookie paste box and its DevTools walkthrough (Oct 7 2026): the
  `config.json` `Cookie` inbox stays as the undocumented emergency route.
- An API-first update check to stop the README's download badge inflating
  (Velopack reads `releases.win.json` from the last ten releases at every
  launch): the owner accepts the inflation, and the badge goes if it gets silly.

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
  needing a persistent home — update, hotkey conflict, session ended; (3)
  mid-game actions with no hotkey — "Server rules…". The tray must never grow
  with the number of widgets: no per-widget show/hide. A mid-game action used
  often earns a **hotkey** instead of a tray line (the heatmap and Check Prime
  did).
- Every distinct widget gets its OWN size slider in Settings (owner's rule):
  `UiScale` (stats panel), `MinimapScale`, `PrimeScale`, `ActivityScale` — a new
  widget ships with one (override `AppearanceScale`). All behave alike (owner,
  Oct 6 2026): plain numbers defaulting to 1.0, no seeding from another widget,
  no migration. No global multiplier on top.
- **A widget never changes size.** Every widget is 298 wide on one of two fixed
  frames (`WidgetFrame`: the minimap and the Prime tracker share the large one,
  the stats panel and the Activity feed the small one) and lays its content out
  inside; additions overlay existing space, transient things are popups. An
  update must never resize an existing widget — 1.25's move to the frames was
  the one accepted exception, never again. (How to build one:
  `.claude/rules/shell.md`.)
- Settings hot-apply: no setting ever needs a restart. They apply on Save and
  Cancel drops them, except the pages that act at once (Account, Skins, Dino
  storage), which say so.
- Destructive actions are armed in place by a first click, never a modal box.
- The app always starts visible; hidden states are runtime-only.
- No stock WPF chrome in any of our windows: every control kind has its own
  dark template; icons are font glyphs (✕ ♂ ♀, text badges), not image files.
- User content (`waypoints.json`, `friends.json`) is kept apart from config and
  the cookie vault.
- Player-facing text states only what the API shows: never "died" (a fresh life
  "started a fresh Deino", leaving "is no longer in game"); never that drain
  varies with activity (unverified), who hit you, or that every hit is caught.
- Players never see "big map": it is "Map" / "the map" in Settings, the
  CHANGELOG, the README and Discord posts. The classes and `BigMap*` keys keep
  their names (owner's call).
- CHANGELOG entries are player-facing text: the update card and the GitHub
  release show them.
- Fail soft: config/crypto/HTTP errors degrade to a UI state, never crash.
- Keep files well under ~500 lines; current style is regions + XML doc
  comments. A class that outgrows it gets another partial-class part
  (`Class.Topic.cs`), not a "manager" class and not a folder shuffle.

## Releases

- Current: 1.34.0. The csproj `<Version>` is the single source of truth
  (SemVer: features bump minor, fixes patch); each release is an annotated
  `vX.Y.Z` tag, and pushing it runs `.github/workflows/release.yml`.
- Never bump, tag or push without the owner's explicit "do the release pass";
  a version number in a request is not that. When they say it, or ask for any
  release, pre-release or hotfix, use the `release-pass` skill
  (`.claude/skills/release-pass/SKILL.md`). It starts with a checklist for the
  owner, never with the bump, and holds the release model, rc tags and
  hotfixing.
- Never move or re-tag an existing tag; a broken release is fixed forward with
  the next patch.
