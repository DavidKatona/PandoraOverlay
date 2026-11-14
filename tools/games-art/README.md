# games-art

The games' art tool. Not part of the app: plain Node (24), no dependency, no
`package.json`. It draws sprites from shapes, packs them into atlases and writes
them where a game embeds them. **Never run by a build or a test**: the output is
checked in, so a build needs no Node.

```
node tools/games-art/hatchling/build.js            writes the game's Art/ and the manifest
node tools/games-art/hatchling/build.js --sheet    the same, and a contact sheet of each atlas
node tools/games-art/sheet.js <atlas.png>          a contact sheet of a checked-in atlas
```

Everything it writes goes to `Games/PandoraOverlay.Games.<Game>/Art/<name>.png`
and `<name>.json` (embedded in the game by one csproj line), to
`tools/games-art/<game>/manifest.json` (checked in), or to `out/` (contact
sheets and scratch; git-ignored).

## The files

| File | What it is |
|---|---|
| `engine.js` | shapes to pixels: ellipses, tapered strokes and polygons, shaded and outlined by one set of rules, so every sprite is in one hand |
| `png.js` | PNG in and out (8-bit RGBA) |
| `pack.js` | named frames and animations in, one atlas PNG and its frame table out |
| `hex.js` | the hex outline rule for tiles |
| `sheet.js` | contact sheets, for looking |
| `manifest.js` | writes a game's manifest |
| `<game>/` | one folder per game: its descriptions (`species.js`), its `build.js` and its `manifest.json` |

## The rules

- **Deterministic.** No date, no unseeded randomness, keys in one fixed order,
  LF line endings. Run `build.js` twice and `git status` is the same. (The PNG's
  compressed bytes are Node's zlib's: another Node version may write other bytes
  for the same pixels. That shows as a changed file; commit it with the manifest.)
- **In step.** `build.js` writes the SHA-256 of every `.js` here (the shared ones
  and the game's own: a source is a source by being in the folder) and of every file
  it wrote. `ArtInStepTests` (in `Games/PandoraOverlay.Games.Tests`) fails when a
  source changed without a rebuild, and says `run node tools/games-art/<game>/build.js`.
  It also holds every file under a game's `Art/` to being an output, and loads every
  atlas through the engine's `Atlas.Load`.
- **New art goes through `engine.js`.** If it can't draw something, improve it
  for every sprite; never draw a one-off. One engine is what makes the set one hand.
- **Look at every piece at 1x** before handing it over (`ART-BRIEF.md`): run the
  sheet, open it, judge the silhouette and colour on a dark ground and on a mid-green
  one. A render is not a check you can skip.
- Data art: the same in every theme, no text in it, no blending the engine lacks.

## The frame table

```json
{ "image": "creatures.png",
  "frames": { "Dryosaurus/stand/0": { "x": 0, "y": 0, "w": 36, "h": 25, "px": 18, "py": 24 } },
  "animations": { "Dryosaurus/stand": { "frames": ["Dryosaurus/stand/0"], "ticks": [1], "loops": true } } }
```

- Frames are named `<subject>/<action>/<index>`, animations `<subject>/<action>`;
  names are case-sensitive and a species is spelled as `Assets/rules.json` spells it.
- The frames lie in rows in name order: no rotation, no trimming, no gutter (the
  engine copies exact rectangles and never scales a sheet).
- `px`, `py` is the **pivot**: the pixel that lands where the game draws. For a
  dino, the middle of its feet on the ground line: `(18, 24)` on a 36 x 25 frame.
  It may sit on the far edge (`0..w`, `0..h`).
- Sprites face right. The engine flips.
- `ticks` are the engine's: 30 a second, one count per frame, each at least 1.
- `pack.js` refuses what the engine's `Atlas` refuses, before writing a byte.

## The hex rule

`hex.js` is `HexLayout.InCell` in JavaScript (`inCell(x, y, w, h, p)`, `mask(w, h, p)`):
a tile is drawn on exactly the pixels a click on it is decided by, so tiles
interlock with no gap and a click lands on the tile it looks like. A test of the
game holds every tile's opaque pixels to the engine's own mask; if the two differ,
`hex.js` is the one that is wrong.
