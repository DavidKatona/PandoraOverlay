# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Breadcrumb trail on the minimap: the path you walked over the last 30
  minutes, fading with age, in both views. Settings → Minimap → Trail picks
  Off / 10 / 30 / 60 min. It is drawn from the positions the overlay already
  receives (no extra requests), lives only for the session, and starts over
  when you die, switch dino or get teleported; a relog or server restart on
  the same spot keeps it.
- Scale bar in the minimap's bottom-left corner: a round real-world distance
  ("500 m", "2 km") that follows the view, zoom and map size — so the
  centered view's zoom finally means something. "Show scale bar" in
  Settings turns it off.

## [1.16.0] - 2026-09-21

### Added

- Time left on the hunger and thirst bars: once a stat has under about an
  hour to go, an estimate ("~40m") appears at the tip of the bar's fill, in
  the bar's own colour, measured from how fast the stat is draining right
  now. It needs about three minutes of play first, and is back right after
  you eat or drink because the drain rate is remembered. The panel keeps
  its exact size. A checkbox in Settings ("Show time left on the hunger and
  thirst bars") turns it off.

### Changed

- Adaptive polling: while you are not spawned in (menus, server restarts,
  the game closed) the overlay now checks every 15 seconds instead of every
  3, and once a minute after ten minutes — an overlay left running overnight
  used to send about 28,800 pointless requests a day, now about 1,440. The
  same applies once the connection has failed five times in a row (expired
  cookie, site down). In-game polling is unchanged. The status line says
  "checking every 15s/60s" while idling; entering edit mode, un-hiding the
  overlay or pressing Check Prime polls right away (never faster than the
  normal pace), so you don't have to wait the interval out after spawning.

## [1.15.0] - 2026-09-19

### Added

- "Prime tracker scale" slider in Settings (75–150%): the prime tracker no
  longer follows the stats panel's scale, so every widget now has its own
  size control. It starts out at your current stats panel scale, so nothing
  changes size when you update.

## [1.14.0] - 2026-09-19

### Added

- Prime tracker widget, approved by the server's dev: your Prime status and
  the ten Prime conditions as a ✓/✗ list, the same result as the website's
  "Prime Check" box. It docks to the left screen edge by default and drags,
  snaps and scales like the other panels. Checks are never automatic: press
  Check in the control panel's Prime group or "Check Prime status" in the
  tray menu.
  The server's cooldown is mirrored locally (with a live countdown), so a
  click during it sends no request, and neither does one made while you are
  not spawned in. The cooldown length is taken from the server after each
  check rather than assumed, so supporter ranks with a shorter cooldown are
  not held to the default 5 minutes, and it is remembered across restarts. The last result is kept across restarts with its time
  and dino, and turns amber once you are playing a different dino.

- Heatmap hotkey (default **Ctrl+F6**, rebindable in Settings like the
  others): flips the minimap's heatmap layer mid-game without entering edit
  mode. If Ctrl+F6 is already one of your own three combos, the overlay
  picks the next free one (Ctrl+F8, F9, F11) instead of clashing with it.
  The hotkey does nothing while the minimap is hidden.

### Changed

- The tray menu is short again: Edit mode, Hide/show overlay, Check Prime
  status, Settings and Exit. It keeps what must work while the overlay is
  locked or hidden; showing and hiding individual widgets (stats, minimap,
  prime tracker) is done from the control panel, and the heatmap entry is
  replaced by the new hotkey.
- The "Show activity heatmap" checkbox is gone from Settings: the heatmap is
  something you flip, not a preference you set once, so it lives on the
  hotkey and the control panel's Heatmap button. Your current on/off state
  is kept.
- The edit-mode control panel is organized by widget: still one row, but
  with a small caption over each widget's own controls — Stats (Show/hide),
  Minimap (Show/hide, Map view, Heatmap), Prime (Show/hide, Check) — between
  Settings and Lock/Exit. The button labels got shorter to match.

## [1.13.1] - 2026-09-16

### Fixed

- The minimap arrow (and waypoint placement) sat about 0.6% of the map
  width right and 1% of the map height below where the website draws
  them: the calibration's `pinOffset` turned out to be part of the
  site's coordinate transform for every marker, not a pin-icon anchor
  correction, and is now applied. Stored waypoints are unaffected (they
  live in world coordinates) and simply render at the corrected spot.

## [1.13.0] - 2026-09-15

### Added

- Optional activity heatmap on the minimap — approved by the server's dev:
  Settings → Minimap → "Show activity heatmap on the map", the edit-mode
  control panel's new Heatmap button, or "Show/hide heatmap" in the tray
  menu all toggle it. Overlays the
  same pre-rendered image the website's heatmap toggle shows (grayscale
  island, activity blobs, and its baked-in player-count + timestamp
  caption), blended at the website's own 55% opacity, in both map views.
  Refreshed every 60 s while enabled and the minimap is shown — the site's
  own page refetches every 10 s per open tab, so this stays well under its
  footprint — and the image is public, so no login cookie is ever sent for
  it. Off by default; if the server disables the heatmap, the layer simply
  hides until it returns.

## [1.12.0] - 2026-09-15

### Changed

- The default edit-mode hotkey moves from **Ctrl+F3** to **Ctrl+F7**:
  Ctrl+F3 turned out to be held globally by third-party software in the
  wild, which made edit mode look dead. The quick mid-game toggles keep
  the nearest keys — **Ctrl+F4** (hide/show overlay) and **Ctrl+F5**
  (minimap view) are unchanged — while edit mode, an occasional setup
  action, sits farthest out, still clear of the game's F2 (recording)
  and F10 (hide HUD) keys. Configs still holding an exact past default
  trio (F3/F4/F5 or the pre-1.11 F7/F8/F9) are migrated automatically;
  any customized set is left untouched, and everything remains
  rebindable in Settings.

### Fixed

- Hotkeys no longer mis-fire when the combo is held slightly too long:
  Windows' key auto-repeat used to fire the hotkey twice (edit mode
  toggled on and instantly off, looking like a dead keypress). Registration
  now uses MOD_NOREPEAT.
- If another app already holds one of our combos when the overlay launches,
  it is no longer silently dead for the session: the status line says so at
  launch, and — since that line is overwritten by the next poll — the tray
  now carries a persistent "Hotkey … in use elsewhere — open Settings" menu
  entry plus a tooltip note until the conflict is resolved by rebinding.
- The app's own hotkeys no longer fire while the settings dialog is open
  (pressing the hide hotkey mid-configuration used to hide the overlay behind the
  dialog), and the current combos can now be captured and reassigned
  between the hotkey boxes: all registrations are suspended for the
  dialog's lifetime and restored on close.

## [1.11.0] - 2026-09-13

### Changed

- Default hotkeys rebased to **Ctrl+F3** (edit mode), **Ctrl+F4** (hide/show
  overlay) and **Ctrl+F5** (minimap view), staying clear of the game's F2
  (recording) and F10 (hide HUD) keys — raw-input games can react to the
  bare F-key even with Ctrl held. Configs still holding the exact old
  F7/F8/F9 defaults are migrated automatically; any customized set is left
  untouched, and everything remains rebindable in Settings.

## [1.10.0] - 2026-09-13

### Added

- The stats panel can now be hidden, mirroring the minimap: toggle it from
  the control panel's new "Show/hide stats" button or the tray menu. The
  app keeps running from the tray; hotkeys and the minimap stay live, and
  a deliberately hidden panel stays hidden across hide-all round trips.
- Per-widget sizing: a "Map size" slider in Settings → Minimap resizes the
  map natively (160–400 px — bigger map, same crisp text), and the old
  "Overlay scale" slider is now "Stats panel scale", scaling only the stats
  panel (and the control panel). The two widgets size independently.

## [1.9.0] - 2026-09-13

### Changed

- Edit mode redesigned around a new **control panel**: the per-widget button
  banners are gone — panels now only recolor their border and stay
  pixel-identical in size and position in both modes — and a draggable
  control panel appears with edit mode (bottom-center by default, position
  remembered) carrying labeled buttons grouped as features (Settings,
  Show/hide minimap, Map view) and session controls (Lock, Exit), plus the
  hint line. This also retires the whole banner-compensation machinery
  behind the recent sizing/position bugs.

### Fixed

- Entering edit mode now takes focus (our own window only), so the game
  releases its mouse capture and the cursor appears immediately —
  previously the cursor stayed invisible until it wandered over a panel.
- Control panel buttons stay readable on hover: a faint glow overlay
  replaces the default chrome's bright highlight.

## [1.8.0] - 2026-09-13

### Added

- Snap guide lines: while dragging a panel in edit mode, a guide line lights
  up along whatever the panel snapped to — orange for screen edges and the
  inset stops, blue for the other panel's edges. Guides vanish the moment
  you release (or hold Alt).

## [1.7.0] - 2026-09-13

### Added

- Two new global hotkeys, both rebindable in Settings → Controls: hide/show
  the whole overlay (default Ctrl+F9 — for screenshots and cutscenes; polling
  continues, and the app always starts visible) and toggling the minimap
  island/centered view from gameplay (default Ctrl+F7). The tray menu gains
  a matching "Hide/show overlay" entry.
- Magnetic snapping while dragging in edit mode: panels snap to the
  monitor's true edges (taskbar deliberately included — the game covers it),
  a small inset from them, and to each other's edges (align or abut). Hold
  Alt while dragging for pixel-perfect freedom.
- Panels can no longer be lost off-screen: locking edit mode (and every
  launch) pulls each panel fully back into its monitor's bounds —
  dragging itself stays free, so cross-monitor moves still work. This also
  covers stale positions from a disconnected monitor or changed resolution.

### Fixed

- Entering edit mode no longer shifts the panels around: the edit banner now
  grows upward into empty space, so the position you set in edit mode is
  exactly where the panel sits once locked (previously the banner pushed the
  content down by its own height).
- The panels no longer widen in edit mode either: the banner is constrained
  to the content width (its text trims when space is tight), so snapping
  measures the true locked size and a right-edge snap stays flush after
  locking.

## [1.6.0] - 2026-09-13

### Added

- Update notifier: one quiet GitHub releases check at launch. On a newer
  version the status line mentions it once, the tray tooltip carries a note
  for the session, and the tray menu gains an "open download page" entry.
  Fails silently when offline; never re-checks or pops anything up.
- Minimap waypoint: right-click the minimap in edit mode to place or move a
  marker (right-click the marker to clear it). The footer shows the distance,
  and in the centered view an off-screen waypoint clamps to the panel edge as
  a direction indicator. Survives restarts.
- Critical-stat pulse: the health, hunger and thirst bars pulse when below
  25%. Stamina is deliberately excluded — it drains by design every sprint.
- Overlay scale (75–150%) and background opacity (30–100%) sliders in the
  settings General section, applied to both windows without a restart.
- Unit test suite (`PandoraOverlay.Tests`, xUnit) covering GrowthTracker,
  hotkey parsing, cookie cleaning, calibration parsing and release-tag
  comparison — run by CI on every push.

## [1.5.0] - 2026-09-13

### Added

- Growth ETA: after about five minutes of baseline, the stats header shows
  the estimated in-game time to full growth ("Growth 41.6% · ~3h 10m"),
  measured from a 15-minute sliding window so it adapts to growth events and
  buffs. If growth stalls while spawned, the readout turns amber and shows
  "paused". Session-only; resets on death, dino swap, or leaving the game.

## [1.4.0] - 2026-09-13

### Added

- Full settings window (tray → Settings…, or ⚙ in edit mode), sectioned into
  Account / Controls / General / Minimap: replace the cookie (leaving the box
  empty keeps the current one), rebind the edit-mode hotkey with live
  availability checking, toggle Start with Windows, and set the minimap view
  and centered zoom — all applied without a restart.
- Rebindable edit-mode hotkey (default Ctrl+F8) — previously changing it
  required editing the source and recompiling.
- Start with Windows toggle (HKCU Run entry).
- The tray icon's hover tooltip now shows live stats (dino · HP · growth).
- Single-instance guard: launching a second copy shows a notice and exits
  instead of silently doubling the poll rate.

## [1.3.1] - 2026-09-12

### Fixed

- The setup dialog's disabled Save button no longer renders as a near-white
  block with unreadable text (WPF's default disabled chrome ignored the dark
  theme); it now dims to a dark muted green until a valid cookie is pasted.

## [1.3.0] - 2026-09-12

### Added

- Footer under the minimap showing the active view — "island view" or
  "centered · N×" with the current zoom.
- System tray icon: right-click for Edit mode, Show/hide minimap, Settings,
  and Exit — the overlay finally has an always-visible way to quit
  (double-click toggles edit mode). The app also gains a proper icon.

### Changed

- Centered-view default zoom raised from 3× to 5×; the zoom range is now
  1.25–6× (was 1.25–4×).

## [1.2.0] - 2026-09-12

### Added

- Player-centered minimap view (north-up): the map pans under an arrow fixed
  at the centre. Toggle with the VIEW button on the minimap's edit banner;
  the mouse wheel adjusts zoom (1.25–4×) while in edit mode. The whole-island
  view remains the default; both modes and the zoom persist.

## [1.1.0] - 2026-09-12

### Added

- Minimap window: bundled island map with a player arrow that glides between
  polls (shortest-arc yaw smoothing); draggable in edit mode, hidden with its
  ✕ button, brought back with the new MAP button on the stats panel; position
  persists. Same Ctrl+F8 edit mode as the stats panel.
- Map calibration constants are fetched from `/api/map/calibration` once per
  launch and cached in `config.json` (`MinimapYawOffsetDegrees` there tunes
  the arrow orientation).

### Changed

- Polling extracted into a shared `PollService`: the stats panel and the
  minimap consume one request stream — the request rate is unchanged from
  v1.0.0 regardless of how many overlay windows are open.
- Debug symbols are now embedded in the executable (no separate `.pdb` file
  in release zips).

## [1.0.0] - 2026-09-12

Initial public release.

### Added

- Always-on-top, click-through overlay showing your own dino's health, stamina,
  hunger, thirst, growth, gender, and fracture status on Isla Pandora (The Isle:
  Evrima), fed exclusively by the islapandora.eu live-map API — fully external,
  never touches the game process.
- Global **Ctrl+F8** hotkey toggling edit mode: drag to reposition, open
  settings, close; window position persists.
- First-run setup dialog with cookie paste-box, live validation, and automatic
  cleanup of pasted values; saving hot-swaps the credentials without a restart.
- DPAPI-encrypted (CurrentUser scope) cookie storage in `config.json`; rolling
  `connect.sid` session renewal, persisted on exit.
- Health bar recoloring (amber below 50 %, red below 25 %) and fracture badges.
- Fail-soft status line with not-set-up / not-in-game / disconnected-retrying
  states; errors never crash the overlay.