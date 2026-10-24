# PandoraOverlay — CLAUDE.md

Personal in-game overlay for The Isle: Evrima (Isla Pandora EU server): the
player's own dino stats in always-on-top widgets, plus a minimap, the map, a
tray icon and a settings dialog. **v1.38.0 is released, working, and endorsed
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
   only to hand a URL — or the settings folder, About → Open folder, or the
   log folder, About → Open log folder — to the Windows shell). It activates only its OWN windows (the
   control panel, the map, dialogs; our hotkey press grants the right); the
   game window is never found, focused or touched. A click on the game is known
   only as our own windows losing the focus (`OverlayFocus`, 1.38: Settings
   closes, edit mode locks); what took the focus is never asked.
2. **Approved endpoints only**, each with the trigger and pace below. Nothing is
   added, and nothing goes faster, without the owner's explicit okay.

   | Endpoint (islapandora.eu) | Cookie | Triggered by | At most |
   |---|---|---|---|
   | `POST /api/map/mylocation` (empty body) | yes | the poll timer; user nudges (edit mode, un-hide, opening Settings → Skins, a Check Prime or skin apply on a stale not-in-game state) | per constraint 3 |
   | `POST /api/map/calibration` (empty body) | yes (works without) | once per launch, and after a credential swap | a FAILED fetch is retried right after a successful poll, at most once a minute, until one succeeds; a bundled seed (`Assets/calibration.json`) stands in meanwhile |
   | `GET /map/api/heatmap-status` + `GET /map/heatmap-live.png` | **no** (the site headers are sent) | only while a heatmap layer is on: the minimap's (the minimap widget switched on, its layer on, and the overlay not hidden by hide-all or the auto-hide) or the open map's own Heatmap layer | never two fetches less than 60 s apart, whatever triggers them: within a minute the kept picture is reused, and a fetch re-arms the 60 s timer (the site's page: 10 s per tab, while its layer is on) |
   | `POST /api/prime/check` | yes | USER ONLY: the control panel's Check or the Check Prime hotkey, never a timer | the server's cooldown, mirrored locally: no session (signed out or ended), cooling down or not in game = no request (a STALE not-in-game state first gets one regular `mylocation` poll); 15 s floor |
   | `POST /api/prime/cooldown` | yes | exactly once after each SUCCESSFUL check (the success answer carries no cooldown, and its length varies with supporter rank; the site button's hard-coded 5 min is only the fallback when this request fails) | once per check |
   | `POST /api/map/friends` (empty body) | yes | only while a friends surface is switched on (the Activity widget with friends' events included, the minimap's friend arrows, the open map's Friends layer; all off = zero requests; hide-all and the auto-hide don't stop it, on purpose: the friend chime and the Activity log work while hidden): right after a successful `mylocation` poll, or once for a hot trigger (a surface shown, including one switched on in Settings since 1.38; a surface hidden or switched off, or a friend's nickname or colour changed, sends nothing) when the last poll succeeded and the last roster is at least one poll interval old | every 2nd in-game poll, every idle poll, never two within one poll interval (the site: with every poll at 5 s); read-only |
   | `POST /api/skins/patreon-skins` (empty body) | yes | opening Settings → Skins; Refresh | reused 10 min, 30 s floor |
   | `POST /api/skins/apply-patreon` `{skinId, patternIndex}` — **WRITE** | yes | a click on a pattern button (or Apply your favourite); the Apply my skin hotkey, which sends your favourite for this dino (1.38, the owner's okay Oct 24 2026) | 15 s mash guard; no session or not in game answered locally; no favourite for the dino = nothing sent |
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
3. **Poll interval >= 2 s** (default 3 s, the owner's choice, kept Oct 16 2026;
   the website's live map polls every 5 s).
   Server-side rate limit is 300/window; the dev specifically praised the
   polling restraint. The cadence is adaptive, DOWNWARDS ONLY: the configured
   interval is the in-game pace; not in-game (or 5 straight failures) idles at
   15 s, then 60 s after 10 min. Off-schedule polls (user nudges, a new
   session's first poll) are floored at the configured interval and re-arm the
   timer — nothing may ever poll faster.
   A WRITE happens only for the player's own click or key press (the skin
   hotkey, 1.38), never on a timer.
4. **The cookie is a credential.** Never log it, print it, put it in exceptions,
   window text, or commit it. Error paths surface exception *type* only. It is
   stored only as the DPAPI blob (the rolled cookie re-encrypted on edit-mode
   lock and on exit). Server text reaches a window only mapped, or through
   `PatreonSkins.Clean` (which drops whole any string mentioning `connect.sid` /
   `cf_clearance`) and labelled as the website's words.
   **The site's access header is a credential too** (1.35, see Permissions):
   its name and its value are NEVER written into this repo — not code, docs,
   tests, commit messages — and never logged or shown. They live only in the
   GitHub Actions secret `PANDORA_OVERLAY_ACCESS`, the git-ignored, scrambled
   `Assets/overlay-access.bin` (`tools/overlay-access/`) and the owner's own
   records. Only the site client sends it, and only to the site's own host
   (`Core/OverlayAccess.cs`).
   **The log** (`AppLog`, 1.36) holds facts only: never the cookie, a header's
   value, a body, server text, a name, a Steam ID or a position; an exception
   as its type, codes and stack trace, never its message. It never leaves the
   PC unless the player sends it.
5. `config.json` is runtime state (holds the DPAPI blob) — stays in `.gitignore`;
   so does `Assets/overlay-access.bin`.

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
  a click or the player's own key press; the admin / staff endpoints are never
  called.
- **The site's anti-abuse rule and the overlay's access header (Oct 2026).**
  Since Oct 12 a Cloudflare rule in front of the site guards mylocation,
  friends, the Prime check and its cooldown: browsers pass, the overlay (like
  any program) got Cloudflare's block page, so every player was stuck on
  "Disconnected · retrying". The site dev confirmed the rule as deliberate
  (Oct 16) and, turning down an honest User-Agent (anyone can copy what the
  public code sends), gave the overlay a header of its own (Oct 17), sent from
  1.35 (constraint 4 keeps it secret). The owner told him plainly that it is a
  speed bump: it ships inside every copy and can be read off one's own
  traffic, and per-account limits (every request carries the player's
  session) are the real backstop. A new value from him = update the secret and
  the local file, then a release. The rule is NEVER got past any other way:
  no browser imitation, TLS impersonation or requests run in WebView2.
- **NOT approved:** the zone overlay images (the live-map bundles patrols /
  sanctuaries / migrations / salt rocks as static PNGs) — ask the dev first.
  The map's zone layers (1.35) don't use them: their shapes are VulnonaMAP's
  community data (`Assets/zones.json`, converted by `tools/zones/`), the packs'
  basis, with no request; the site's pictures were also older than the game's
  layout (Oct 2026). A
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
cookie only (the access header gets requests past the site's anti-abuse rule;
it is not a login). The request headers, the response shapes, the site's refusal and
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
- The site's access header gets into a build as `Assets/overlay-access.bin`,
  embedded when it exists. Release builds: the workflow runs
  `tools/overlay-access/make-access.ps1` with the `PANDORA_OVERLAY_ACCESS`
  secret (tags only) and refuses to release a build without the file. The
  owner's PC: the owner runs the script once (a hidden prompt; never the
  value on a command line). A build without the file runs, with the four
  guarded endpoints blocked — what any build from source gets.
- The user's files (`config.json`, `waypoints.json`, `friends.json`, the
  picture caches) live in `%AppData%\PandoraOverlay` (`DataFolder`); the
  install itself is Velopack's, under `%LocalAppData%\PandoraOverlay`, beside
  the sign-in browser's `WebView2` folder and the log's `Logs` folder.
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
are the real decoupling. A feature's requests go in
`Core/PandoraClient.<Topic>.cs`, its gating in `Core/PollService.<Topic>.cs`,
its page in `Settings/SettingsWindow.<Topic>.cs`. The first features predate
that split: the mylocation, calibration, heatmap, Prime and friends requests
are in `PandoraClient.cs`, and Prime's and friends' gating in `PollService.cs`.

- `Core/` — the plumbing: PandoraClient (HTTP), PollService (the timers and
  EVERY request gate), OverlayConfig (config.json + the DPAPI vault),
  DataFolder, Updater (Velopack) + UpdateChecker (the fallback notice),
  HotkeySpec, StartupRegistration, and the log: AppLog (Unreal Engine's
  categories and verbosity levels) with LogFile and RequestLogHandler.
- `Shell/` — MainWindow (the orchestrator, and also the stats panel),
  OverlayWindowBase, WidgetFrame, DefaultLayout, SnapResolver, SnapGuideWindow,
  ControlPanelWindow, TrayIcon, OverlayFocus (the "click on the game" signal).
- `Stats/` — the stats panel's trackers and rules.
- `Minimap/` — MinimapWindow; MapCanvas (the drawing every map shares) and the
  shared map menu; the area map (AreaMap, AreaBorders, AreaMapAsset); the zones
  (ZoneSet, ZoneAsset; drawn on the map only); the pure map helpers.
- `BigMap/` — BigMapWindow ("the map" to players) and ScrimWindow, its backdrop.
- `Waypoints/`, `Friends/`, `Activity/`, `Prime/`, `Rules/`, `Skins/`,
  `Storage/` (Dino storage), `Account/` (the in-app sign-in), `Updates/` (the
  update card), `Settings/` (the paged dialog, one partial per page).
- `Assets/` — `map.png`, `app.ico` (WPF Resources) and the embedded data
  (`rules.json`, `calibration.json`, `areas.png` + `areas.json`, `zones.json`;
  the CHANGELOG is embedded too, for the update card).
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
  gates, caves, air currents, other food spawns, zone shapes (those became the
  map's zone layers, below). These are
  user-imported waypoints from a community map — NOT the site's unapproved
  zone-overlay images (salt rocks included).
- `tools/area-map/` — the area map's generator and preview scripts, not part of
  the app. `Assets/areas.png` is HAND-CORRECTED: never re-run the generator over
  it without the owner's word. And nothing is ever written for one named area —
  everything about a particular map is input (owner's rule; see
  `.claude/rules/maps.md`).
- `tools/zones/` — `make-zones.ps1` converts a hand-downloaded VulnonaMAP data
  file into `Assets/zones.json` (the map's zone layers: where migration zones,
  patrol zones and sanctuaries CAN be; which is active is the game's secret).
  It makes no request and fails on anything it can't place; a re-run is a
  no-op. Refresh it after a game patch moves zones (Vulnona's index:
  vulnona.com/game/map/dat.txt), then a patch release. The plan and the owner's
  decisions: `Desktop\Pandora Overlay Files\pandora-zones\PLAN.md`.
- `tools/overlay-access/` — `make-access.ps1` turns the site's access header
  (one `name:value` line, from a hidden prompt or the release secret) into the
  scrambled, git-ignored `Assets/overlay-access.bin`; it never prints the line
  (see constraint 4 and Stack & build).
- `.claude/rules/` — conventions for writing new code in `Settings/`, the maps
  (`Minimap/`, `BigMap/`, the area tool), `Shell/` and `Core/`; each loads when a
  matching file is touched. `.claude/skills/release-pass/` — the release
  procedure (see Releases); `.claude/skills/patch-notes/` — the Discord post
  for a release (a short intro, then New / Changes / Fixes / Removed).

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
- Status line shows the last update; "Disconnected · retrying (…)" on errors,
  the brackets naming the cause in our own words since 1.36
  (`ConnectionProblem`: no connection, no answer, blocked by the website,
  website error 502 …; the type's name for anything else, as every case read
  before). The site's refusal twice running → "Session ended · sign in again
  from the tray" and the tray's "Sign in again…" entry. Cloudflare's block page
  (a build without the access header, or one the site no longer accepts) reads
  as "(blocked by the website)", and as "(HttpRequestException)" before 1.36:
  1.34 and older have shown that for every player since Oct 12 2026.

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
- The map's zones (1.35): whether they match the game's own (the data covers
  0.21.720–0.21.772). The Layers menu, every zone kind and the readout were
  checked in game by the owner (Oct 19).
- The site client no longer following redirects (1.35): a redirect from the
  site would now fail (none seen in Oct 2026). The access header itself was
  checked in game on every page that talks to the site, with a local build
  and with the v1.35.0-rc.1 install, whose release build ran the access steps
  (Oct 19).
- The log (1.36): run in game by the owner with Detailed log on, from a
  plain-folder build (Oct 21: launch, idle, in game, the auto-hide,
  hide-all, edit mode, the map, a Prime check, Open log folder), and the
  requests in it matched the pacing rules; the hidden-surface fix (no
  roster request on a hide) was seen in the same evening's last log. Not
  yet seen: an installed copy's log and the update to 1.36, a failed
  request, a refused session, the 2 MB roll-over, a real crash.
- The status line's words for a failed poll (1.36, `ConnectionProblem`):
  tests only, no failure seen in game (the owner skipped the network-off
  test). About's "Report a problem" opening GitHub's form chooser, and the
  bug-report form itself, were not tried (the chooser needs a login).
- Updating to 1.37 with the stats panel at a size other than 100%: the
  control panel then starts at 100% (`ControlPanelScale`; tests only). The
  rest of 1.37 was checked by the owner (Oct 22–23): every Settings page,
  the control panel's slider, and area-named waypoints on both maps,
  pasted codes included.
- From 1.38 (no pre-release, the owner's call): a Windows shutdown with the
  overlay running, so its save and log lines and the handler starting no
  request (`OnSessionEnding`; code only); the update card closing on a
  click elsewhere (the "updated" card and What's new at the owner's own
  update to 1.38 are the first look, the "update available" card needs a
  newer release); the focus rule on other PCs. The rest was checked by the
  owner in game (Oct 24), the "not signed in" answers included.

SHIPPED in 1.38.0 (Oct 24 2026): a player was attacked
with Settings open, and a click on the game left it on top with every hotkey
off. So: **Settings applies every change at once** (no Save or Cancel), and
**a click on the game** closes Settings, the update cards and What's new and
locks edit mode (`OverlayFocus`); the trail is kept an hour whatever its
setting; About's "Import from an older copy…" is cut; a Windows shutdown
saves and logs before the process is ended (`MainWindow.OnSessionEnding`;
before, the Oct 24 log just stopped); **the skin hotkey** (Ctrl+F9), which
applies **a favourite skin per species**, chosen with a ☆ on a Skins tile
and sending nothing (it replaced "Apply again"; the attacked player's idea,
the owner's per-species call); and **Esc locks edit mode**. A link or
folder Settings hands to the browser counts as a click away (the owner tried
both ways with a trial switch, now gone: the browser loading behind the
overlay "felt controlled"). **Esc acts on its RELEASE everywhere** (edit
mode, Settings, the map's Esc and M, the update cards), and a window keeps
the keystroke it acts on: the map closed on the press, the release reached
the game, and The Isle opened its menu (the owner, Oct 24; Esc on the
release in edit mode and Settings never did, which confirmed it). What's new
closing on an unhandled press also handed that keystroke on to Settings, so
one Esc closed both; now one Esc closes one window. The skin key was
Ctrl+F11 at first: F11 switches the game between windowed and fullscreen and
F12 is Steam's screenshot (the owner, Oct 24), so the other keys' fallbacks
dropped both too. **A new default layout** (the owner's ask, Oct 24): the
F-row's blocks, F1–F4 the overlay, F5–F8 the widgets in the control panel's
order, F9 the website, M the map (`OverlayConfig.DefaultHotkey`); an update
keeps a player's keys, and Settings → Hotkeys → "Use the default keys"
moves one over (the owner's pick of three; a migration of untouched
defaults was turned down). Checked by the owner in game (Oct 24), the
new layout, its button and the favourites included. The constraint review
before the tag (a fresh reviewer, the owner's ask) found no violation and
two near misses, both fixed: with no session (signed out, or ended by the
website) Check Prime and a skin apply answer "not signed in" and send
nothing (`PollService.NoSession`), and the shutdown handler marks the
overlay as exiting before Settings closes.

SHIPPED in 1.37.0 (Oct 23 2026): the **Settings revision** (owner's ask, Oct
22): the grouped nav (the overlay's pages, the website's under a caption,
About), the Widgets and Maps pages, General's Look and Sounds, Hotkeys, and
the control panel's own scale; and **area-named waypoints**. The dialog's
layout rules: `.claude/rules/settings.md`.

SHIPPED in 1.36.0 (Oct 21 2026): **app-wide logging**, modelled on Unreal
Engine's (owner's ask): categories, verbosity levels, Unreal's line and its
backup files. The owner's choices: on for every player at quiet levels;
`%LocalAppData%\PandoraOverlay\Logs`, ten files of 2 MB at most, backups
gone after 14 days; Settings → About's "Open log folder" and "Detailed log",
with the per-category levels in `config.json` only; an exception as its type
and stack trace. How it works: AppLog's summary.

Areas: new waypoints and pasted codes without a name are named after their
area ("Delta", "Delta 2"; "Waypoint N" at sea) since 1.37. Further border
corrections are painted into `Assets/areas.png`.

Later/maybe: official token auth (the nudge
went out with the heatmap ask ~Sep 14 2026, unanswered); Segoe Fluent Icons for
stat glyphs; layout presets beyond the default; splitting the stats panel out
of MainWindow into its own window (a refactor); if the Patreon skins page (a
pilot) works: the website's own favourites and custom presets (other
endpoints; the overlay's local per-species favourite came in 1.38); the
Lucky Wheel, Dino storage's sibling on the website
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
  is MainWindow's, on General's Sounds with the other chimes since 1.37).
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
- Zones on the minimap (owner, Oct 12 2026): the map only. And the site's zone
  pictures as their source (raster, stale, patrols as points): VulnonaMAP's
  vectors were chosen.

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
  explanation or validation. Test: "set once and forget?"
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
  widget ships with one (override `AppearanceScale`). The control panel has its
  own too, `ControlPanelScale` (1.37; it rode `UiScale` before). All behave
  alike (owner, Oct 6 2026): plain numbers defaulting to 1.0, no seeding from
  another widget, no migration. No global multiplier on top.
- **A widget never changes size.** Every widget is 298 wide on one of two fixed
  frames (`WidgetFrame`: the minimap and the Prime tracker share the large one,
  the stats panel and the Activity feed the small one) and lays its content out
  inside; additions overlay existing space, transient things are popups. An
  update must never resize an existing widget — 1.25's move to the frames was
  the one accepted exception, never again. (How to build one:
  `.claude/rules/shell.md`.)
- Settings apply at once (1.38, the owner's call, Oct 24 2026: no Save or
  Cancel, like Windows 11's Settings), and no setting ever needs a restart.
  A click on the game, Esc, ✕ or Close closes the dialog and loses nothing.
  What removes many things at once or moves the layout asks for a second click.
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
- File size (owner, Oct 9 2026): split a class into another partial part
  (`Class.Topic.cs`) when it holds a second topic that reads on its own,
  such as a feature's requests or a page's section, not to meet a line
  count; past about 800 lines, look for such a seam. Never a "manager"
  class or a folder shuffle. Current style is regions + XML doc comments.

## Releases

- Current: 1.38.0. The csproj `<Version>` is the single source of truth
  (SemVer: features bump minor, fixes patch); each release is an annotated
  `vX.Y.Z` tag, and pushing it runs `.github/workflows/release.yml`.
- Never bump the version or tag a release without the owner's explicit "do
  the release pass" (a version number in a request is not that); push only
  when the owner asks. When they say it, or ask for any
  release, pre-release or hotfix, use the `release-pass` skill
  (`.claude/skills/release-pass/SKILL.md`). It starts with a checklist for the
  owner, never with the bump, and holds the release model, rc tags and
  hotfixing.
- Never move or re-tag an existing tag; a broken release is fixed forward with
  the next patch.
