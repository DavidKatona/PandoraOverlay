// Frames in, one atlas out: a PNG and its frame table (A28), the pair the
// engine's Atlas.Load reads (Games/PandoraOverlay.Games.Engine/Pixels/Atlas.cs
// has the same table in its summary).
//
//   const packed = pack({ name: "creatures", frames: [...], animations: [...] });
//   writeAtlas(folder, packed);     // <folder>/creatures.png and creatures.json
//
// A FRAME is { name, width, height, pixels, px, py }: pixels is the engine's
// own image data (w * h entries of [r, g, b, a], straight alpha), px, py the
// pivot, the pixel that lands where the game draws (a dino's feet on the ground
// line: the middle of the bottom row). Sprites face right; the engine flips.
// An ANIMATION is { name, frames: [frame names], ticks: [counts], loops }: ticks
// are the engine's, 30 a second, one count per frame, each at least 1.
// Names are <subject>/<action>/<index> for a frame and <subject>/<action> for
// an animation; frameName() and animationName() spell them, and a name is
// case-sensitive (a species is spelled as Assets/rules.json spells it, which is
// how a reading names a dino).
//
// The layout is the simplest one the engine can load: the frames in NAME order
// (plain code-unit order, the same on every PC), left to right in rows no wider
// than maxWidth, each row as tall as its tallest frame; no rotation, no
// trimming, no gutter, because the engine copies exact rectangles and never
// scales a sheet, so nothing bleeds. A frame is always placed whole, so a frame
// wider than maxWidth gets a row of its own.
//
// Everything the engine's Atlas refuses at load is refused HERE, before a byte
// is written, with the name of the entry: a frame with a side under 1, a pivot
// outside 0..w and 0..h, an empty or repeated name, an animation with no frames,
// naming a frame that isn't there, with ticks that don't match its frames, a tick
// under 1, a total past a 32-bit integer, or with no loops flag.
//
// Deterministic (A28): the same input gives the same bytes. Keys are written in
// one fixed order (the table's own: image, frames, animations; a frame's x, y, w,
// h, px, py; an animation's frames, ticks, loops), the entries in name order,
// LF line endings, a newline at the end, no date.
const fs = require("fs");
const path = require("path");
const { image, blit } = require("./engine");
const { encodePng } = require("./png");

const DEFAULT_MAX_WIDTH = 256;
const MAX_TICKS_TOTAL = 2147483647; // SpriteAnimation's int

function frameName(subject, action, index) {
  return `${subject}/${action}/${index}`;
}
function animationName(subject, action) {
  return `${subject}/${action}`;
}

// A frame for pack() from an engine image ({ w, h, px }), as sprite() returns.
function frameOf(name, img, px, py) {
  return { name, width: img.w, height: img.h, pixels: img.px, px, py };
}

function fail(atlas, what) {
  throw new Error(`atlas '${atlas}': ${what}`);
}

function isWhole(n) {
  return Number.isInteger(n);
}

function checkFrame(atlas, f) {
  const where = `frame '${f.name}'`;
  if (typeof f.name !== "string" || f.name.length === 0) fail(atlas, "a frame has an empty name");
  if (!isWhole(f.width) || !isWhole(f.height) || f.width < 1 || f.height < 1) fail(atlas, `${where} is ${f.width} x ${f.height}: each side is a whole number of at least 1`);
  if (!Array.isArray(f.pixels) || f.pixels.length !== f.width * f.height) fail(atlas, `${where} holds ${f.pixels && f.pixels.length} pixels, not ${f.width} x ${f.height}`);
  for (let i = 0; i < f.pixels.length; i++) {
    const p = f.pixels[i];
    if (!Array.isArray(p) || p.length !== 4 || !p.every((v) => isWhole(v) && v >= 0 && v <= 255)) fail(atlas, `${where}: pixel ${i} is not [r, g, b, a] of whole numbers 0 to 255`);
  }
  if (!isWhole(f.px) || !isWhole(f.py) || f.px < 0 || f.px > f.width || f.py < 0 || f.py > f.height) {
    fail(atlas, `${where}: its pivot (${f.px}, ${f.py}) lies outside its ${f.width} x ${f.height} frame (0 to ${f.width}, 0 to ${f.height})`);
  }
}

function checkAnimation(atlas, a, frameNames) {
  const where = `animation '${a.name}'`;
  if (typeof a.name !== "string" || a.name.length === 0) fail(atlas, "an animation has an empty name");
  if (typeof a.loops !== "boolean") fail(atlas, `${where}: 'loops' must be true or false`);
  if (!Array.isArray(a.frames) || a.frames.length === 0) fail(atlas, `${where} has no frame`);
  if (!Array.isArray(a.ticks) || a.ticks.length !== a.frames.length) fail(atlas, `${where} has ${a.frames.length} frames and ${a.ticks && a.ticks.length} tick counts: one count per frame`);
  let total = 0;
  a.frames.forEach((name, i) => {
    if (!frameNames.has(name)) fail(atlas, `${where} names the frame '${name}', which is not in the table`);
    if (!isWhole(a.ticks[i]) || a.ticks[i] < 1) fail(atlas, `${where} shows '${name}' for ${a.ticks[i]} ticks: every frame shows for at least 1, a whole number`);
    total += a.ticks[i];
  });
  if (total > MAX_TICKS_TOTAL) fail(atlas, `${where} runs ${total} ticks: more than a 32-bit integer`);
}

// Lays the frames out, checks everything, and returns the atlas in memory:
// { name, image, table } (image is the engine's image data, table the frame table as an object).
function pack({ name, frames, animations, maxWidth }) {
  if (typeof name !== "string" || !/^[A-Za-z0-9_-]+$/.test(name)) throw new Error(`an atlas's name is letters, digits, - and _ (it becomes a file name); it is '${name}'`);
  const limit = maxWidth === undefined ? DEFAULT_MAX_WIDTH : maxWidth;
  if (!isWhole(limit) || limit < 1) fail(name, `maxWidth must be a whole number of at least 1; it is ${limit}`);
  if (!Array.isArray(frames) || frames.length === 0) fail(name, "it has no frame");

  const names = new Set();
  for (const f of frames) {
    checkFrame(name, f);
    if (names.has(f.name)) fail(name, `frame '${f.name}' is listed twice`);
    names.add(f.name);
  }
  const anims = animations || [];
  const animNames = new Set();
  for (const a of anims) {
    checkAnimation(name, a, names);
    if (animNames.has(a.name)) fail(name, `animation '${a.name}' is listed twice`);
    animNames.add(a.name);
  }

  const byName = (a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
  const sorted = [...frames].sort(byName);

  // The rows.
  const placed = [];
  let x = 0;
  let y = 0;
  let rowHeight = 0;
  let width = 0;
  for (const f of sorted) {
    if (x > 0 && x + f.width > limit) {
      y += rowHeight;
      x = 0;
      rowHeight = 0;
    }
    placed.push({ f, x, y });
    x += f.width;
    rowHeight = Math.max(rowHeight, f.height);
    width = Math.max(width, x);
  }
  const height = y + rowHeight;

  const sheet = image(width, height);
  const table = { image: name + ".png", frames: {}, animations: {} };
  for (const { f, x: fx, y: fy } of placed) {
    blit(sheet, { w: f.width, h: f.height, px: f.pixels }, fx, fy, 1);
    table.frames[f.name] = { x: fx, y: fy, w: f.width, h: f.height, px: f.px, py: f.py };
  }
  for (const a of [...anims].sort(byName)) table.animations[a.name] = { frames: [...a.frames], ticks: [...a.ticks], loops: a.loops };
  return { name, image: sheet, table };
}

// The frame table as JSON text: one frame and one animation to a line, in the
// table's own key order, so a diff shows a changed frame as one changed line.
function tableToJson(table) {
  const lines = [];
  lines.push("{");
  lines.push(`  "image": ${JSON.stringify(table.image)},`);
  const entries = (obj, render) => Object.keys(obj).map((k) => `    ${JSON.stringify(k)}: ${render(obj[k])}`);
  const frame = (f) => `{ "x": ${f.x}, "y": ${f.y}, "w": ${f.w}, "h": ${f.h}, "px": ${f.px}, "py": ${f.py} }`;
  const animation = (a) => `{ "frames": ${JSON.stringify(a.frames)}, "ticks": ${JSON.stringify(a.ticks)}, "loops": ${a.loops} }`;
  lines.push(`  "frames": {`);
  lines.push(entries(table.frames, frame).join(",\n"));
  lines.push(`  },`);
  const anims = entries(table.animations, animation);
  if (anims.length) {
    lines.push(`  "animations": {`);
    lines.push(anims.join(",\n"));
    lines.push(`  }`);
  } else lines.push(`  "animations": {}`);
  lines.push("}");
  return lines.join("\n") + "\n";
}

// Writes <folder>/<name>.png and <folder>/<name>.json and returns the two
// paths. A file whose bytes would not change is left alone (its date too).
function writeAtlas(folder, packed) {
  fs.mkdirSync(folder, { recursive: true });
  const written = [];
  const put = (file, bytes) => {
    const target = path.join(folder, file);
    if (!fs.existsSync(target) || !fs.readFileSync(target).equals(bytes)) fs.writeFileSync(target, bytes);
    written.push(target);
  };
  put(packed.name + ".png", encodePng(packed.image));
  put(packed.name + ".json", Buffer.from(tableToJson(packed.table), "utf8"));
  return written;
}

module.exports = { pack, writeAtlas, tableToJson, frameOf, frameName, animationName, DEFAULT_MAX_WIDTH };
