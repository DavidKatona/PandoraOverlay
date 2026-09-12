# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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