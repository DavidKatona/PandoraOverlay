// What stands on the board besides the dinos (PLAN-HATCHLING 6.1, task H10):
// the atlas `things`. Sizes, pivots and colours are the plan's and binding (the
// painter draws by these names, 2.7):
//
//   plant/stand        20 x 16, (10, 13)  a fern: food for plant eaters
//   meat/pile          22 x 14, (11, 11)  0 one meal, 1 more
//   smallgame/stand    16 x 11, (8, 10)   a small lizard-like animal; walk 0, 1
//   ring/you, hostile, apex, acting 0..1   36 x 14, (18, 7): under the feet
//   mark/hunt, search, you 0..1            9 x 9, (4, 4): above a head
//   num-hurt, num-good, ordinal digits and signs   5 x 7, (0, 0)
//
// Made two ways, both the engine's:
//   - shapes (sprite()): the plant, the carcass and the small game, shaded and
//     outlined by the rule every dino is, so they sit on the board in the same
//     hand;
//   - grids (drawn() + outline()): the marks and the digits, where at 9 x 9 and
//     5 x 7 there is nothing to shade, only pixels to place, each with a dark
//     outline so it reads over grass, forest, water, rock and a dino.
// The rings are computed from two ellipses (the band between them), outlined
// dark inside and out, their back half a shade darker than the front, so a ring
// lies flat on the ground under the feet; inside, a faint wash of the same
// colour (translucent: the one blend the game has, source-over).
const { ramp, ellipse, stroke, sprite, image, pixel, drawn, outline, hexToRgb, rgbToHex } = require("../engine");

function mix(a, b, t) {
  const p = hexToRgb(a);
  const q = hexToRgb(b);
  return rgbToHex(p.map((v, i) => v + (q[i] - v) * t));
}
// Every grid piece's outline: the board's ground, a shade warmer, so it is
// dark on every terrain without being a hole.
const INK = "#0E1014";

// A soft shadow on the ground under an item, so it stands ON the cell, not in
// it: a translucent ellipse, under everything else of the sprite.
function withShadow(img, cx, cy, rx, ry) {
  const out = image(img.w, img.h);
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      const dx = (x + 0.5 - cx) / rx;
      const dy = (y + 0.5 - cy) / ry;
      if (dx * dx + dy * dy <= 1) out.px[y * img.w + x] = pixel("#0B0F1366");
      const p = img.px[y * img.w + x];
      if (p[3] > 0) out.px[y * img.w + x] = [...p];
    }
  }
  return out;
}

// ---- The plant: a fern ----
// Five fronds from one root, drawn as thin strokes (no outline of their own:
// at 20 x 16 a frond is two pixels wide, and the sprite rule would make it all
// outline), in the plan's mint (#9BE0A5) over a deeper green rib, then one
// dark outline round the whole plant. Light and fern-shaped on purpose: the
// one green thing on the board that is food, and nothing like a tree crown.
const FERN = ramp("#6CC07C");
function plant() {
  const frond = (pts, tone) => stroke(pts, { thin: tone });
  const img = sprite({
    w: 20,
    h: 16,
    ramps: { body: FERN, soil: "#6B4A30" },
    parts: [
      ellipse(10, 13.6, 3.6, 1.4, { ramp: "soil" }),
      frond([[9.2, 12.6, 1.1], [5.5, 7.5, 0.95], [2.6, 4.2, 0.7]], "base"),
      frond([[10.8, 12.6, 1.1], [14.5, 7.5, 0.95], [17.4, 4.2, 0.7]], "base"),
      frond([[9, 13, 1.0], [5, 10.6, 0.9], [1.6, 9.6, 0.7]], "shadow"),
      frond([[11, 13, 1.0], [15, 10.6, 0.9], [18.4, 9.6, 0.7]], "shadow"),
      frond([[10, 12.6, 1.15], [10, 6, 1.0], [10, 2.2, 0.7]], "light"),
      // leaflets: the light tips that make a frond a fern
      frond([[6.2, 8.4, 0.6], [4.6, 8.9, 0.6]], "highlight"),
      frond([[13.8, 8.4, 0.6], [15.4, 8.9, 0.6]], "highlight"),
      frond([[4.6, 5.8, 0.6], [5.6, 4.4, 0.6]], "highlight"),
      frond([[15.4, 5.8, 0.6], [14.4, 4.4, 0.6]], "highlight"),
      frond([[10, 5, 0.6], [8.6, 4.2, 0.6]], "highlight"),
      frond([[10, 7.6, 0.6], [11.4, 6.8, 0.6]], "highlight")
    ]
  });
  return withShadow(outline(img, FERN.outline), 10, 14.2, 5.2, 1.6);
}

// ---- The carcass ----
// One meal: a haunch on its bone. More: a carcass, ribs standing out of red
// meat. Red and bone-white, the two colours no terrain has, so a hungry
// player's eye finds them at 1x. The plan's carcass colour (#E8A87C) is the
// cut flesh, lit.
const MEAT = ramp("#B5423C");
MEAT.light = "#D9765E";
MEAT.highlight = "#E8A87C";
const BONE = ramp("#E4D9BC");
BONE.base = "#E4D9BC";
function meatOne() {
  const img = sprite({
    w: 22,
    h: 14,
    ramps: { body: MEAT, bone: BONE },
    parts: [
      stroke([[13, 8.6, 1.1], [17.6, 6.6, 0.95]], { ramp: "bone" }),
      ellipse(18.4, 5.6, 1.5, 1.3, { ramp: "bone" }),
      ellipse(17.2, 7.4, 1.3, 1.1, { ramp: "bone" }),
      ellipse(9.6, 8.2, 5.4, 3.6),
      ellipse(7.4, 9.2, 3.2, 2.4)
    ]
  });
  return withShadow(img, 11, 11.4, 8, 1.8);
}
function meatMore() {
  const rib = (x, lean) => stroke([[x, 7.6, 0.75], [x + lean, 3.6, 0.7], [x + lean * 2.4, 2.4, 0.6]], { ramp: "bone", thin: "base" });
  const img = sprite({
    w: 22,
    h: 14,
    ramps: { body: MEAT, bone: BONE },
    parts: [
      ellipse(11, 8.6, 8.4, 3.4),
      rib(6.6, -0.4),
      rib(9.4, -0.2),
      rib(12.2, 0.2),
      rib(15, 0.4),
      ellipse(4.4, 9.4, 3.2, 2.4),
      stroke([[16.6, 9.6, 1.0], [20.4, 8.2, 0.85]], { ramp: "bone" }),
      ellipse(20.6, 7.6, 1.2, 1.1, { ramp: "bone" })
    ]
  });
  return withShadow(img, 11, 11.4, 10, 1.9);
}

// ---- The small game ----
// A small lizard with its head up, original: no species of the 21 is this
// small, this orange or this low. Bright orange-tan (the plan's #FFB74D) so it
// reads as prey on green; a dark stripe down the back; the eye a dark pixel.
// `pose` moves the legs and the tail: 0 standing, 1 and 2 the walk's two steps,
// the near legs and the far legs in turn, the body and its feet's ground line
// still (ART-BRIEF: the mass steady between frames).
const LIZARD = ramp("#F2A54A"); // near the plan's #FFB74D once lit: it must pop on forest too
function lizard(pose) {
  const ground = 10.4;
  // [near hind, near front, far hind, far front]: the foot's x for each pose.
  const feet = [
    [4.6, 11.2, 6.2, 12.6],
    [3.6, 12.2, 7.2, 11.6],
    [5.6, 10.2, 5.2, 13.6]
  ][pose];
  const leg = (hipX, footX, far) => stroke([[hipX, 7.9, 0.85], [footX, ground - 0.6, 0.6]], far ? { far: true } : { limb: true });
  const tailEnd = [[0.4, 8.4], [0.4, 7.6], [0.6, 9.0]][pose];
  const img = sprite({
    w: 16,
    h: 11,
    ramps: { body: LIZARD, belly: "#F3D9A4" },
    parts: [
      leg(6.4, feet[2], true),
      leg(10.6, feet[3], true),
      stroke([[6, 7.6, 1.5], [3, 8.4, 1.0], [tailEnd[0], tailEnd[1], 0.55]]),
      ellipse(8.4, 7.2, 3.6, 1.9, { belly: true }),
      stroke([[10.8, 6.6, 1.25], [12.4, 5.0, 1.15]]),
      ellipse(13.2, 4.6, 1.9, 1.35),
      stroke([[14, 4.8, 0.95], [15.4, 5.1, 0.6]]),
      leg(6.8, feet[0], false),
      leg(10.2, feet[1], false)
    ],
    marks: [
      [7, 6, "shadow"],
      [9, 6, "shadow"],
      [5, 7, "shadow"]
    ],
    extras: [[13, 4, "#14140E"]]
  });
  return withShadow(img, 8.4, 10.4, 5.4, 0.9);
}

// ---- The rings: under the feet ----
// A band between two ellipses centred on the pivot (18, 7), outlined dark on
// both sides. `thick` makes the apex's heavier: a wider band. The back half
// (behind the feet) is a shade darker than the front, the front's lowest row a
// shade lighter: the ring lies on the ground and is seen from above and in
// front, as the dinos are.
function ring(colour, thick, wash) {
  const w = 36;
  const h = 14;
  const cx = 18;
  const cy = 7;
  const outer = [16.6, 6.0];
  const inner = thick ? [12.8, 2.9] : [14.1, 3.7];
  const inside = (x, y, [rx, ry]) => {
    const dx = (x + 0.5 - cx) / rx;
    const dy = (y + 0.5 - cy) / ry;
    return dx * dx + dy * dy <= 1;
  };
  const r = ramp(colour);
  const back = mix(colour, r.shadow, 0.45);
  const front = colour;
  const lip = mix(colour, "#FFFFFF", 0.35);
  let img = image(w, h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (!inside(x, y, outer) || inside(x, y, inner)) continue;
      const lowest = !inside(x, y + 1, outer);
      img.px[y * w + x] = pixel(y < cy ? back : lowest ? lip : front);
    }
  }
  img = outline(img, INK);
  // The wash inside the inner outline: the ring's colour, faint.
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (img.px[y * w + x][3] === 0 && inside(x, y, inner)) img.px[y * w + x] = pixel(colour + wash);
    }
  }
  return img;
}

// ---- Grid pieces: the marks and the digits ----
// A grid's '#' is the piece's colour, '+' a lighter glint of it; outline()
// then gives it the dark edge, diagonals included.
function glyph(rows, colour, w, h) {
  const light = mix(colour, "#FFFFFF", 0.45);
  const core = drawn(rows, { "#": colour, "+": light });
  if (core.w !== w || core.h !== h) throw new Error(`a grid piece is ${core.w} x ${core.h}, not ${w} x ${h}`);
  return outline(core, INK, true);
}

// "It has seen me": a heavy red "!". "It has heard something": a gold "?".
// Unlike in colour, in weight (a solid bar against a thin hook) and in shape.
const HUNT = [
  ".........",
  "...+##...",
  "...###...",
  "...###...",
  "....#....",
  ".........",
  "...###...",
  "...###...",
  "........."
];
const SEARCH = [
  ".........",
  "..+###...",
  ".##..##..",
  ".....##..",
  "....##...",
  "....#....",
  ".........",
  "....#....",
  "........."
];
// Whose turn it is: a chevron pointing down at your dino; the second frame a
// pixel lower, so the two in turn are a small bob.
const YOU = [
  ".........",
  ".##...##.",
  ".+##.##+.",
  "..#####..",
  "...###...",
  "....#....",
  ".........",
  ".........",
  "........."
];
function lower(rows) {
  return [rows[rows.length - 1], ...rows.slice(0, -1)];
}

// 3 x 5 digits in a 5 x 7 frame: the outline is the frame's other pixels, so
// two digits 6 pixels apart (H38: "12") keep a one-pixel gap.
const DIGITS = [
  ["###", "#.#", "#.#", "#.#", "###"],
  [".#.", "##.", ".#.", ".#.", "###"],
  ["##.", "..#", ".#.", "#..", "###"],
  ["##.", "..#", ".#.", "..#", "##."],
  ["#.#", "#.#", "###", "..#", "..#"],
  ["###", "#..", "##.", "..#", "##."],
  [".##", "#..", "###", "#.#", "###"],
  ["###", "..#", ".#.", ".#.", ".#."],
  ["###", "#.#", "###", "#.#", "###"],
  ["###", "#.#", "###", "..#", "##."]
];
const MINUS = ["...", "...", "###", "...", "..."];
const PLUS = ["...", ".#.", "###", ".#.", "..."];
function framed(rows) {
  return [".....", ...rows.map((r) => "." + r + "."), "....."];
}

const HURT = "#FF8A80";
const GOOD = "#9BE0A5";
const ORDINAL = "#FFFFFF";

// The frames: [name parts, image, pivot].
function frames() {
  const out = [];
  const add = (subject, action, index, img, px, py) => out.push({ subject, action, index, img, px, py });
  add("plant", "stand", 0, plant(), 10, 13);
  add("meat", "pile", 0, meatOne(), 11, 11);
  add("meat", "pile", 1, meatMore(), 11, 11);
  add("smallgame", "stand", 0, lizard(0), 8, 10);
  add("smallgame", "walk", 0, lizard(1), 8, 10);
  add("smallgame", "walk", 1, lizard(2), 8, 10);
  add("ring", "you", 0, ring("#FFC864", false, "26"), 18, 7);
  add("ring", "hostile", 0, ring("#FF6A5E", false, "26"), 18, 7);
  add("ring", "apex", 0, ring("#FF3B30", true, "38"), 18, 7);
  add("ring", "acting", 0, ring("#D9E0E8", false, "22"), 18, 7);
  add("ring", "acting", 1, ring("#FFFFFF", false, "40"), 18, 7);
  add("mark", "hunt", 0, glyph(HUNT, "#FF6A5E", 9, 9), 4, 4);
  add("mark", "search", 0, glyph(SEARCH, "#FFC864", 9, 9), 4, 4);
  add("mark", "you", 0, glyph(YOU, "#FFC864", 9, 9), 4, 4);
  add("mark", "you", 1, glyph(lower(YOU), "#FFC864", 9, 9), 4, 4);
  DIGITS.forEach((d, i) => {
    add("num-hurt", "digit", i, glyph(framed(d), HURT, 5, 7), 0, 0);
    add("num-good", "digit", i, glyph(framed(d), GOOD, 5, 7), 0, 0);
    add("ordinal", "digit", i, glyph(framed(d), ORDINAL, 5, 7), 0, 0);
  });
  add("num-hurt", "sign", 0, glyph(framed(MINUS), HURT, 5, 7), 0, 0);
  add("num-good", "sign", 0, glyph(framed(PLUS), GOOD, 5, 7), 0, 0);
  return out;
}

// The animations' ticks where they are not one tick a frame (A39): the small
// game's walk 4, 4; the two-frame marks 8, 8 (the game steps those itself,
// the table is whole anyway). Every animation here loops.
const TICKS = { "smallgame/walk": 4, "ring/acting": 8, "mark/you": 8 };

module.exports = { frames, TICKS };
