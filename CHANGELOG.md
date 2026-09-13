# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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