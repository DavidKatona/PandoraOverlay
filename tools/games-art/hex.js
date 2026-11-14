// The hex outline rule, for tiles (A28): the engine's HexLayout.InCell
// (Games/PandoraOverlay.Games.Engine/Grids/HexLayout.cs) in JavaScript. A tile
// is drawn on exactly the pixels a click on it is decided by, so tiles
// interlock with no gap and a click lands on the tile it looks like. The art is
// checked against the C# rule (a test of the game holds every tile's opaque
// pixels to the engine's own mask), never the other way: if the two ever
// differ, THIS file is wrong.
//
// The rule, for a pixel (x, y) in a cell's box of w (even) by h, row pitch p,
// with s = h - p and dx = |2x + 1 - w|:
//   top band, y < s:        inside when 2 * dx * s <  w * (2y + 1)
//   bottom band, y >= h-s:  with m = h - 1 - y, inside when 2 * dx * s <= w * (2m + 1)
//   between:                inside
// Whole numbers only. The top is strict and the bottom is not, so two rows of
// cells that interlock share no pixel and leave none out (HexLayout's summary
// has the proof).

// The shape's checks, as HexLayout's own: they throw for a cell the engine
// would refuse, so a tile is never drawn for a grid that can't exist.
function check(w, h, p) {
  if (!Number.isInteger(w) || !Number.isInteger(h) || !Number.isInteger(p)) throw new Error("a cell's width, height and row pitch must be whole numbers");
  if (w <= 0 || w % 2 !== 0) throw new Error(`A cell's width must be even and positive; it is ${w}.`);
  if (p <= 0 || p >= h) throw new Error(`The row pitch must be above 0 and below the cell's height (${h}); it is ${p}.`);
  if (2 * (h - p) > h) throw new Error(`The rows overlap too far: 2 * (height - pitch) must be at most the height (${h}).`);
}

// Whether the pixel (x, y) of a cell's own box is the cell's. False outside the box.
function inCell(x, y, w, h, p) {
  check(w, h, p);
  if (x < 0 || y < 0 || x >= w || y >= h) return false;
  const s = h - p;
  const dx = Math.abs(2 * x + 1 - w);
  if (y < s) return 2 * dx * s < w * (2 * y + 1);
  if (y >= h - s) {
    const m = h - 1 - y;
    return 2 * dx * s <= w * (2 * m + 1);
  }
  return true;
}

// The whole box at once: a Uint8Array of w * h, row by row, 1 where inCell is true.
function mask(w, h, p) {
  check(w, h, p);
  const out = new Uint8Array(w * h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) out[y * w + x] = inCell(x, y, w, h, p) ? 1 : 0;
  return out;
}

module.exports = { inCell, mask };
