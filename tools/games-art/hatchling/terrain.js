// The board's ground (PLAN-HATCHLING 6.1, task H10): the atlas `terrain`.
//
// Every frame here is a whole hex cell's box, 44 x 50 with its pivot at (0, 0),
// drawn on EXACTLY the pixels of hex.js's mask for (44, 50, 38): the board's
// HexLayout (2.7). A terrain tile is opaque on every pixel of the mask and on no
// other (A28: the game's tile-mask test holds it against the engine's own
// HexLayout.InCell); fog/dim is the mask in one translucent colour; the three
// marks are the mask's two-pixel rim only, laid over a tile.
//
// How a tile reads at 1x (the brief's test): by colour AND by shape.
//   grass   mid-green, soft patches and a few light tufts: open ground
//   forest  dark floor under round tree crowns, each lit on top and outlined as
//           a dino is: something to hide in (cover, H-rules 1.6); the crowns are
//           dark and big so none reads as food (a plant is its own sprite)
//   water   blue, light ripple arcs: crossable
//   rock    grey boulders, lit tops and dark gaps between them: no way through
// Crowns and boulders are sprite() shapes, so they are shaded and outlined by
// the same rule as the dinos: one hand for the whole board (ART-BRIEF).
//
// Where tiles meet: each tile draws one 1-pixel line, a shade darker than its
// own ground, on the edges it OWNS: its right side and its two lower slants.
// The other three edges are owned by the neighbours (the one to the left owns
// its right side, the two above own their lower slants), so every edge of the
// board gets exactly one line: a grid with no doubled edge and no gap.
//
// Variants (grass 3, forest 3, water 2, rock 2) differ in what is placed
// where, never in colour: the painter picks `cell % variants` (2.7), so a field
// of grass is three tiles in turn and must not show a repeating pattern.
// Deterministic: positions come from fixed lists and a fixed integer hash,
// never from Math.random.
const { ramp, ellipse, poly, sprite, image, pixel, hexToRgb, rgbToHex } = require("../engine");
const { mask } = require("../hex");

// The cell, as the board lays it out (2.7: HexLayout(grid, 44, 50, 38)).
const W = 44;
const H = 50;
const PITCH = 38;
const MASK = mask(W, H, PITCH);
const S = H - PITCH; // the slanted bands' height: rows 0..11 and 38..49
const inMask = (x, y) => x >= 0 && y >= 0 && x < W && y < H && MASK[y * W + x] === 1;

// The game's own colours for the board (2.7's stand-ins, HatchlingPalette):
// the tiles are built round them, so the art and the stand-in it replaces are
// the same colour at a glance.
const GRASS = "#3F6237";
const FOREST = "#24502F";
const WATER = "#1E5F82";
const ROCK = "#50565C";
const UNSEEN = "#11161C";
const DIM = "#0B0F13B8"; // #0B0F13 at alpha 184, exactly as 6.1 asks
const MOVE = ["#FFC864", "#FFDA94"]; // the second "a shade lighter" (the ambient cue)
const ATTACK = "#FF8A80";
const HOVER = "#ECF2F8";

// ---- Small helpers ----
function mix(a, b, t) {
  const p = hexToRgb(a);
  const q = hexToRgb(b);
  return rgbToHex(p.map((v, i) => v + (q[i] - v) * t));
}
// A fixed integer hash to 0..1: the same on every PC and in every run. Each
// coordinate is mixed in turn (a sum mixed once left whole columns alike: a
// stripe down the tile).
function mix32(h) {
  h ^= h >>> 16;
  h = Math.imul(h, 0x7feb352d);
  h ^= h >>> 15;
  h = Math.imul(h, 0x846ca68b);
  h ^= h >>> 16;
  return h >>> 0;
}
function hash(x, y, seed) {
  return mix32(mix32(mix32(seed + 0x9e37) ^ (x + 0x51)) ^ (y + 0x3f)) / 4294967296;
}
// Smooth value noise on a lattice of `cell` pixels: soft patches, not specks.
function smooth(x, y, seed, cell) {
  const gx = Math.floor(x / cell);
  const gy = Math.floor(y / cell);
  const fx = x / cell - gx;
  const fy = y / cell - gy;
  const ease = (t) => t * t * (3 - 2 * t);
  const a = hash(gx, gy, seed);
  const b = hash(gx + 1, gy, seed);
  const c = hash(gx, gy + 1, seed);
  const d = hash(gx + 1, gy + 1, seed);
  const u = ease(fx);
  const v = ease(fy);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}

// The edges a tile owns (see the summary): its right side, between the slanted
// bands, and its lower slants, the pixels with nothing of the cell below them.
function ownsEdge(x, y) {
  if (!inMask(x, y)) return false;
  if (y >= S && y < H - S && !inMask(x + 1, y)) return true;
  return y >= H - S && !inMask(x, y + 1);
}

// A tile: `ground(x, y)` names every pixel's colour; `stamps` are engine images
// laid over it at their offsets (only their opaque pixels, only inside the
// mask); `edge` is the colour of the owned edges. The result is opaque on the
// mask and transparent everywhere else, whatever the stamps did.
function tile(ground, stamps, edge) {
  const img = image(W, H);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (inMask(x, y)) img.px[y * W + x] = pixel(ground(x, y));
  for (const { img: s, x: ox, y: oy } of stamps) {
    for (let y = 0; y < s.h; y++) {
      for (let x = 0; x < s.w; x++) {
        const p = s.px[y * s.w + x];
        if (p[3] === 0 || !inMask(ox + x, oy + y)) continue;
        img.px[(oy + y) * W + ox + x] = [p[0], p[1], p[2], 255];
      }
    }
  }
  if (edge) for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (ownsEdge(x, y)) img.px[y * W + x] = pixel(edge);
  return img;
}

// Pixels from a list [[dx, dy, colour], ...] at (x, y), as one stamp.
function dots(list, x, y) {
  const minX = Math.min(...list.map((d) => d[0]));
  const minY = Math.min(...list.map((d) => d[1]));
  const w = Math.max(...list.map((d) => d[0])) - minX + 1;
  const h = Math.max(...list.map((d) => d[1])) - minY + 1;
  const img = image(w, h);
  for (const [dx, dy, c] of list) img.px[(dy - minY) * w + dx - minX] = pixel(c);
  return { img, x: x + minX, y: y + minY };
}

// ---- Grass ----
const G = ramp(GRASS);
// Soft patches only: a strong patch is a shape, and a shape in three tiles laid
// in turn is a pattern the eye finds across a field.
const GRASS_DARK = mix(GRASS, G.shadow, 0.36);
const GRASS_LIGHT = mix(GRASS, G.light, 0.42);
const GRASS_EDGE = mix(GRASS, G.shadow, 0.85);

// Three tufts, each a few blades: a light blade or two over a dark root, so a
// tuft reads as a tuft at 1x and as nothing that could be picked up.
const TUFTS = [
  [[0, 0, G.shadow], [1, 0, G.shadow], [0, -1, G.light], [1, -2, G.highlight], [2, -1, G.light]],
  [[0, 0, G.shadow], [-1, -1, G.light], [0, -1, G.light], [1, -2, G.light], [1, -1, G.highlight]],
  [[0, 0, G.shadow], [1, 0, G.shadow], [0, -1, G.light], [0, -2, G.highlight], [2, -1, G.light], [2, -2, G.light]]
];
// Where each variant's tufts stand: [x, y, tuft]. Hand placed so three tiles
// in a row never line their tufts up, and clear of the cell's middle, where
// whatever stands on it hides them.
const GRASS_TUFTS = [
  [[9, 16, 0], [27, 9, 1], [35, 22, 2], [6, 30, 1], [16, 41, 2], [33, 38, 0], [21, 20, 2]],
  [[15, 10, 2], [31, 15, 0], [5, 21, 0], [38, 31, 1], [11, 37, 1], [26, 44, 0], [19, 26, 1]],
  [[22, 6, 0], [8, 13, 1], [36, 17, 1], [13, 27, 2], [30, 26, 0], [5, 35, 2], [29, 40, 2], [18, 45, 1]]
];

function grassTile(v) {
  const seed = 11 + v * 7;
  const ground = (x, y) => {
    // Two scales of noise, so no patch runs long in one direction (one scale
    // alone drew stripes down a tile), and a little per-pixel grain.
    const n = smooth(x, y, seed, 10) * 0.52 + smooth(x, y, seed + 1, 5) * 0.28 + hash(x, y, seed) * 0.2;
    return n < 0.32 ? GRASS_DARK : n > 0.68 ? GRASS_LIGHT : GRASS;
  };
  const stamps = GRASS_TUFTS[v].map(([x, y, t]) => dots(TUFTS[t], x, y));
  return tile(ground, stamps, GRASS_EDGE);
}

// ---- Forest ----
const F = ramp(FOREST);
const FLOOR = mix(F.shadow, F.outline, 0.35);
const FLOOR_LIGHT = mix(F.shadow, FOREST, 0.35);
const FOREST_EDGE = F.outline;
// Two crown greens, a little brighter than the floor so the crowns stand out
// of it, and unlike each other so a canopy is not one stamp repeated. No
// lighter than the grass beside them (the planner's H10 verdict, H48): the
// first greens lit to tones above the grass's ground, so a forest tile was
// eleven bright objects and a green dino, a hatchling or a fern standing in it
// was one more. Dark crowns let whatever stands in a forest be read before the
// forest is.
const CROWNS = ["#234F2E", "#28552B"];

// A tree crown as the engine draws a dino's body: a lumpy round mass (a big
// lobe and smaller ones on its shoulders), lit on top, shaded below, outlined
// where it meets the floor. `k` turns which lobes it has, so no two neighbours
// are the same shape.
function crown(r, k) {
  const w = Math.ceil(r * 2 + 3);
  const h = Math.ceil(r * 2 + 2);
  const cx = w / 2;
  const cy = h / 2 + 0.5;
  const lobes = [
    ellipse(cx, cy + r * 0.12, r * 0.9, r * 0.8),
    ellipse(cx - r * 0.5, cy - r * 0.25, r * 0.5, r * 0.48),
    ellipse(cx + r * 0.45, cy - r * 0.32, r * 0.52, r * 0.5),
    ellipse(cx + (k % 2 ? -0.1 : 0.15) * r, cy - r * 0.55, r * 0.42, r * 0.38)
  ];
  if (k % 3 === 1) lobes.push(ellipse(cx + r * 0.72, cy + r * 0.15, r * 0.4, r * 0.4));
  if (k % 3 === 2) lobes.push(ellipse(cx - r * 0.75, cy + r * 0.2, r * 0.38, r * 0.38));
  return sprite({ w, h, ramps: { body: CROWNS[k % 2] }, parts: lobes });
}
// A crown's shadow on the floor, below and right of it (the light is from the
// upper left, as on every sprite): height, so the crowns stand up out of the cell.
function crownShadow(r) {
  const w = Math.ceil(r * 2 + 3);
  const h = Math.ceil(r * 2 + 2);
  const img = image(w, h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const dx = (x + 0.5 - w / 2) / (r * 0.95);
      const dy = (y + 0.5 - h / 2 - 0.5) / (r * 0.85);
      if (dx * dx + dy * dy <= 1) img.px[y * w + x] = pixel(F.outline);
    }
  }
  return img;
}
// [x, y, r]: the centre of each crown in the cell and its size: a canopy, the
// crowns touching and overlapping, the floor showing only in the gaps. Drawn
// top row first, so a lower crown overlaps the one behind it.
const FOREST_CROWNS = [
  [[16, 7, 5.5], [29, 9, 5], [8, 16, 5.5], [21, 17, 6.5], [35, 18, 5.5], [11, 28, 6], [27, 28, 6.5], [39, 32, 4.5], [5, 37, 4.5], [18, 39, 6], [32, 41, 5.5]],
  [[22, 6, 5.5], [11, 12, 5], [33, 13, 5.5], [20, 20, 6], [5, 23, 4.5], [36, 26, 5.5], [13, 31, 6.5], [27, 33, 5.5], [40, 37, 4], [8, 40, 4.5], [22, 43, 5.5]],
  [[18, 8, 5], [31, 10, 6], [9, 18, 6], [24, 21, 5.5], [38, 22, 4.5], [17, 30, 6], [32, 32, 6], [5, 31, 4], [12, 41, 5.5], [26, 42, 5], [38, 40, 3.5]]
];

function forestTile(v) {
  const seed = 41 + v * 5;
  const ground = (x, y) => (smooth(x, y, seed, 6) > 0.62 ? FLOOR_LIGHT : FLOOR);
  const sorted = [...FOREST_CROWNS[v]].sort((a, b) => a[1] - b[1]);
  const shadows = sorted.map(([x, y, r]) => {
    const img = crownShadow(r);
    return { img, x: Math.round(x - img.w / 2) + 1, y: Math.round(y - img.h / 2) + 2 };
  });
  const crowns = sorted.map(([x, y, r], i) => {
    const img = crown(r, i + v);
    return { img, x: Math.round(x - img.w / 2), y: Math.round(y - img.h / 2) };
  });
  return tile(ground, [...shadows, ...crowns], FOREST_EDGE);
}

// ---- Water ----
const Wr = ramp(WATER);
const WATER_DEEP = mix(WATER, Wr.shadow, 0.45);
const WATER_EDGE = mix(Wr.shadow, Wr.outline, 0.4);

// A ripple: a short light arc with its crest in the highlight. Two sizes.
const RIPPLES = [
  [[0, 0, Wr.light], [1, -1, Wr.highlight], [2, -1, Wr.highlight], [3, 0, Wr.light]],
  [[0, 0, Wr.light], [1, -1, Wr.light], [2, -1, Wr.highlight], [3, -1, Wr.highlight], [4, -1, Wr.light], [5, 0, Wr.light]]
];
const WATER_RIPPLES = [
  [[10, 12, 0], [26, 8, 1], [31, 19, 0], [6, 24, 1], [19, 23, 0], [33, 31, 1], [12, 35, 0], [24, 41, 1]],
  [[18, 9, 0], [7, 16, 1], [28, 15, 1], [15, 22, 0], [35, 25, 0], [5, 31, 0], [22, 33, 1], [31, 41, 0], [13, 43, 0]]
];

function waterTile(v) {
  const seed = 71 + v * 3;
  const ground = (x, y) => (smooth(x, y, seed, 8) < 0.4 ? WATER_DEEP : WATER);
  const stamps = WATER_RIPPLES[v].map(([x, y, r]) => dots(RIPPLES[r], x, y));
  return tile(ground, stamps, WATER_EDGE);
}

// ---- Rock ----
const R = ramp(ROCK);
const GAP = mix(R.shadow, R.outline, 0.55); // the dark between boulders
// A crag's two faces: the one turned to the light (upper left, as on every
// sprite) and the one turned away. Both lighter than the gravel between crags.
const LIT_FACE = "#858C93";
const DARK_FACE = "#555C64";

// A crag: a jagged peak standing up out of the cell, in two faces that meet
// at a ridge running from the peak down to its foot. `left` is the outline
// from the peak down the left side to the ground, `right` the same on the
// right, `foot` the ridge's lower end. Drawn by the sprite rule (each face
// shaded down its height, an outline round the crag and a line where the two
// faces meet), so a crag is a thing with height, not paving: the whole point
// of rock is that you cannot walk through it.
function crag(peak, left, right, foot) {
  const pts = [peak, ...left, ...right, foot];
  const x0 = Math.floor(Math.min(...pts.map((p) => p[0]))) - 1;
  const y0 = Math.floor(Math.min(...pts.map((p) => p[1]))) - 1;
  const w = Math.ceil(Math.max(...pts.map((p) => p[0]))) - x0 + 2;
  const h = Math.ceil(Math.max(...pts.map((p) => p[1]))) - y0 + 2;
  const at = ([x, y]) => [x - x0, y - y0];
  const img = sprite({
    w,
    h,
    ramps: { body: LIT_FACE, dark: DARK_FACE },
    parts: [
      poly([peak, ...left, foot].map(at)),
      poly([peak, foot, ...[...right].reverse()].map(at), { ramp: "dark", sep: true })
    ]
  });
  return { img, x: x0, y: y0 };
}
// The crags of each variant, in the cell's own coordinates, back to front: a
// big one in the middle and smaller ones round its foot, so the cell is full.
const ROCK_CRAGS = [
  [
    crag([31, 4], [[27, 9], [24, 12], [22, 20]], [[36, 9], [40, 15], [43, 24]], [32, 22]),
    crag([10, 9], [[6, 15], [2, 20], [0, 28]], [[14, 13], [18, 19], [19, 27]], [11, 27]),
    crag([22, 8], [[17, 15], [14, 21], [9, 37], [6, 41]], [[26, 13], [31, 18], [35, 28], [38, 40]], [23, 42]),
    crag([37, 26], [[34, 32], [31, 38], [30, 44]], [[40, 30], [44, 35], [44, 44]], [38, 44]),
    crag([8, 31], [[4, 37], [1, 41], [1, 46]], [[11, 35], [14, 41], [16, 47]], [9, 47]),
    crag([22, 38], [[18, 42], [14, 48], [13, 50]], [[25, 41], [29, 45], [31, 50]], [23, 50])
  ],
  [
    crag([15, 5], [[11, 10], [7, 16], [3, 24]], [[19, 9], [22, 14], [24, 22]], [16, 24]),
    crag([33, 10], [[29, 14], [26, 20], [25, 28]], [[37, 15], [41, 20], [44, 27]], [34, 28]),
    crag([26, 15], [[21, 20], [17, 27], [12, 40], [10, 43]], [[30, 19], [34, 25], [38, 35], [40, 42]], [27, 44]),
    crag([7, 25], [[3, 30], [0, 35], [0, 42]], [[10, 29], [13, 35], [14, 42]], [8, 42]),
    crag([14, 39], [[11, 43], [8, 48], [7, 50]], [[17, 42], [20, 46], [22, 50]], [15, 50]),
    crag([35, 37], [[32, 41], [29, 46], [28, 50]], [[38, 40], [42, 44], [44, 48]], [36, 49])
  ]
];

function rockTile(v) {
  const seed = 97 + v * 13;
  const ground = (x, y) => (hash(x, y, seed) < 0.2 ? R.outline : GAP);
  return tile(ground, ROCK_CRAGS[v], R.outline);
}

// ---- Fog ----
// Never seen: near-black, a shade lighter than the board's ground (#0B0F13)
// so the island's unknown part is still a set of cells, with a faint cloudy
// texture and the faintest grid.
const UNSEEN_DARK = mix(UNSEEN, "#0B0F13", 0.6);
const UNSEEN_LIGHT = mix(UNSEEN, "#1C232B", 0.45);
function unseenTile() {
  const ground = (x, y) => {
    const n = smooth(x, y, 5, 7) * 0.8 + hash(x, y, 5) * 0.2;
    return n < 0.32 ? UNSEEN_DARK : n > 0.74 ? UNSEEN_LIGHT : UNSEEN;
  };
  return tile(ground, [], UNSEEN_DARK);
}
// Seen but not in sight: the mask in one translucent colour, laid over a tile.
function dimTile() {
  return tile(() => DIM, [], null);
}

// ---- The marks: the mask's two-pixel rim only ----
// A rim pixel is a mask pixel with a pixel outside the mask within two steps,
// diagonals included, so the slants are as thick as the sides. Not a tile: the
// painter lays it over one (where you can move, what you can attack, the cell
// under the mouse), and two marked neighbours show both rims side by side.
function rim(colour) {
  const img = image(W, H);
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      if (!inMask(x, y)) continue;
      let edge = false;
      for (let dy = -2; dy <= 2 && !edge; dy++) for (let dx = -2; dx <= 2 && !edge; dx++) if (!inMask(x + dx, y + dy)) edge = true;
      if (edge) img.px[y * W + x] = pixel(colour);
    }
  }
  return img;
}

// The frames, by name (`<subject>/<action>/<index>`), all with the pivot (0, 0).
function frames() {
  return [
    ...[0, 1, 2].map((v) => ["grass", "tile", v, grassTile(v)]),
    ...[0, 1, 2].map((v) => ["forest", "tile", v, forestTile(v)]),
    ...[0, 1].map((v) => ["water", "tile", v, waterTile(v)]),
    ...[0, 1].map((v) => ["rock", "tile", v, rockTile(v)]),
    ["fog", "unseen", 0, unseenTile()],
    ["fog", "dim", 0, dimTile()],
    ["mark", "move", 0, rim(MOVE[0])],
    ["mark", "move", 1, rim(MOVE[1])],
    ["mark", "attack", 0, rim(ATTACK)],
    ["mark", "hover", 0, rim(HOVER)]
  ];
}

// Every action is an animation (A39): a one-frame one shows its frame for 1
// tick and loops; mark/move steps its two frames 8 and 8 (the game steps it
// itself, but the table is whole). The tile variants are one animation each
// too (`grass/tile` with three frames): the painter picks a frame by the cell,
// never plays them, so their ticks are 1 each.
const TICKS = { "mark/move": 8 };

module.exports = { frames, TICKS, CELL: { W, H, PITCH }, inMask };
