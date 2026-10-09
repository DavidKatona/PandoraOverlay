---
paths:
  - "Minimap/**"
  - "BigMap/**"
  - "tools/area-map/**"
  - "Assets/areas.*"
  - "PandoraOverlay.Tests/Minimap/**"
  - "Shell/MainWindow.BigMap.cs"
  - "Core/PollService.Maps.cs"
---

# Writing code for the maps and the area map

- **Golden renders.** The minimap's drawing is held pixel-identical by a harness outside
  the repo: `Desktop\Pandora Overlay Files\pandora-big-map-sketch\golden-renders\run-golden.ps1`
  (31 scenarios at 1× and 2×, 62 renders, compared with `renders\baseline`). Run it after
  any change under `Minimap/`; re-baseline only after the owner has checked a deliberate
  visual change in game. It does NOT cover glides, the right-click menu, the real heatmap
  picture or click and wheel handling — those are checked in game. It reaches private
  names by reflection, so renaming any of these means updating its `Scenarios.cs`:
  MinimapWindow's `_trail`, `SetHoverSpot`, `_hover`, `_hoverFriend`, `UpdateFooter`;
  PollService's `SnapshotReceived`, `FriendsChanged` and `HeatmapChanged` event fields;
  the internal `BreadcrumbTrail.Add(p, keep, now)`.
- Both maps share ONE drawing (`MapCanvas`), ONE menu (`MapMenuBuilder` + `MapActions` —
  tracking on either redraws both) and ONE trail (MainWindow's). New map features go into
  the shared pieces, not into one window. MapCanvas draws and decides nothing — no config,
  no PollService, no footer; the window picks the sets, the view and the glides.
- Panel ⇄ fraction arithmetic goes only through `MapViewport`; world ⇄ fraction only
  through `MapCalibration.ToFraction` / `ToWorld`. No inline copies.
- Map text goes through `NameLayer`: one element, each name prepared once, Display-mode
  text on whole device pixels with a one-pixel outline — never a blur effect, never an
  element per name.
- A widget never changes size: anything transient on the minimap (the menu) is a Popup,
  its own HWND.
- The maps are pure consumers of the one PollService stream: trail, scale bar, speed,
  ETA and areas are computed locally. The map's Heatmap and Friends layers are extra
  surfaces on the existing gates (`PollService.Maps.cs`) — no endpoint, no pace.
- Friends are drawn only where the server says they share their location (`OnMap`).
- Any window drawn over the game: a translucent full-screen window uses WPF's own
  `AllowsTransparency`, **never** `SetLayeredWindowAttributes` on a WPF window (the game
  went pitch black, Oct 9 2026); keep moving content in opaque windows; snap cached
  layers' offsets to whole device pixels.
- **MAP-AGNOSTIC (owner's rule, Oct 2 2026): nothing is written for one named area.** The
  area code, the generator and the tests treat every area alike, and everything about a
  particular map is INPUT: a new map = replace `Assets/map.png` and
  `tools/area-map/area-labels.json`, run the generator, done. No rule, exception or test
  may name an area ("Spiky Isle", "Highland") or count on this island (the number of
  areas, 1000 px, sea in the corner, how much land there is). Distances are in metres,
  turned into pixels by the calibration (which is input, copied into `areas.json` for the
  tests); colours beyond the hand-picked ones are generated; the bundled-map tests check
  only properties any generated map has. The one number about the map PICTURE's style,
  the sea colour tolerance, stays in the generator, named as such.
- `Assets/areas.png` is **hand-corrected** (since Oct 5 2026): never re-run
  `make-area-map.ps1` over it without the owner's word. Corrections are painted into it
  with the legend's exact colours and hard edges; `preview-area-map.ps1` (which only reads
  Assets) redraws `docs/area-map.jpg`. `tools/area-map/` is Windows PowerShell 5.1 with
  inline C#, not part of the app or the build; the drawing code lives only in the preview
  script.
- Pure helpers take a timestamped `internal` overload for the tests; brushes are frozen.
