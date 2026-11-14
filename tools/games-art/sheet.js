// Contact sheets: an atlas laid out to be LOOKED AT (ART-BRIEF's rule: never
// hand over art you have not looked at). One sheet holds
//   - every frame at 6x with its name, standing on a ground line at its pivot's row,
//   - then all of them at 1x on a dark ground,
//   - then all of them at 1x on a mid-green one,
// because at 1x on a hex a sprite is silhouette and colour only, and it must
// read on both. The 1x grids align the frames by their pivots, so feet share
// one line.
//
//   node tools/games-art/sheet.js <atlas.png> [scale] [columns]
//
// reads the atlas and its frame table (<atlas>.json beside the PNG) and writes
// tools/games-art/out/<name>-sheet.png (git-ignored). A game's build.js writes
// its sheets too, with --sheet. Frame names are drawn in capitals by the
// engine's tiny font (digits and / - _ . included), so case is lost on a sheet.
const fs = require("fs");
const path = require("path");
const { image, text, textWidth } = require("./engine");
const { writePng, readPng } = require("./png");

const DARK = "#10151B"; // the sheet's ground, as the old species sheet had it
const GREEN = "#4C7A3B"; // a mid-green hex's ground: the board the sprites will stand on
const LINE = [34, 42, 50, 255];
const LABEL = "#C7D1DA";
const PAD = 14;

function hex([r, g, b]) {
  return [r, g, b, 255];
}
function rgb(c) {
  return hex([parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)]);
}
function fillRect(dst, x, y, w, h, colour) {
  for (let yy = y; yy < y + h; yy++) for (let xx = x; xx < x + w; xx++) if (xx >= 0 && yy >= 0 && xx < dst.w && yy < dst.h) dst.px[yy * dst.w + xx] = colour;
}
// Lays a frame on the sheet at a scale, blending a translucent pixel over the
// ground (source-over, the one blend the game has) so a translucent frame (the
// fog, a ring's wash, an item's shadow) looks on the sheet as it will on the
// board; the engine's blit copies pixels as they are, which is right for
// packing an atlas and wrong for looking at one.
function blend(dst, src, ox, oy, scale) {
  for (let y = 0; y < src.h; y++) {
    for (let x = 0; x < src.w; x++) {
      const p = src.px[y * src.w + x];
      if (p[3] === 0) continue;
      const a = p[3] / 255;
      for (let dy = 0; dy < scale; dy++) {
        for (let dx = 0; dx < scale; dx++) {
          const tx = ox + x * scale + dx;
          const ty = oy + y * scale + dy;
          if (tx < 0 || ty < 0 || tx >= dst.w || ty >= dst.h) continue;
          const d = dst.px[ty * dst.w + tx];
          dst.px[ty * dst.w + tx] = [0, 1, 2].map((k) => Math.round(p[k] * a + d[k] * (1 - a))).concat([255]);
        }
      }
    }
  }
}
function crop(img, f) {
  const out = image(f.w, f.h);
  for (let y = 0; y < f.h; y++) for (let x = 0; x < f.w; x++) out.px[y * f.w + x] = img.px[(f.y + y) * img.w + f.x + x];
  return out;
}

// atlas: { image, table } as pack() returns, or loadAtlas() reads.
function contactSheet(atlas, options) {
  const scale = (options && options.scale) || 6;
  const columns = (options && options.columns) || 5;
  // options.only picks the frames by name: an atlas of hundreds of frames is
  // looked at in pages (the creatures atlas, one page a size: all 420 frames at
  // 6x on one sheet ran Node out of memory, H11).
  const names = Object.keys(atlas.table.frames).filter((options && options.only) || (() => true));
  const frames = names.map((n) => ({ name: n, ...atlas.table.frames[n], img: crop(atlas.image, atlas.table.frames[n]) }));
  const maxW = Math.max(...frames.map((f) => f.w));
  const maxH = Math.max(...frames.map((f) => f.h));
  const labelW = Math.max(...names.map((n) => textWidth(n, 2)));
  const labelH = 22;

  // 6x, labelled.
  const cellW = Math.max(maxW * scale, labelW) + PAD;
  const cellH = maxH * scale + labelH + PAD;
  const rows = Math.ceil(frames.length / columns);
  const width = columns * cellW + PAD;

  // The 1x grids, as many to a row as the sheet's width holds.
  const gap = 4;
  const maxPx = Math.max(...frames.map((f) => f.px));
  const maxPy = Math.max(...frames.map((f) => f.py));
  const boxW = Math.max(...frames.map((f) => f.px)) + Math.max(...frames.map((f) => f.w - f.px)) + gap;
  const boxH = maxPy + Math.max(...frames.map((f) => f.h - f.py)) + gap;
  const perRow = Math.max(1, Math.floor((width - PAD * 2) / boxW));
  const gridRows = Math.ceil(frames.length / perRow);
  const gridH = gridRows * boxH + gap;
  const sectionH = gridH + labelH + PAD;

  const top = PAD + rows * cellH;
  const sheet = image(width, top + sectionH * 2 + PAD, DARK);

  frames.forEach((f, k) => {
    const ox = PAD + (k % columns) * cellW;
    const oy = PAD + Math.floor(k / columns) * cellH;
    // The ground the pivot stands on (a row of the frame's own, so a pivot on
    // the far edge draws nothing).
    if (f.py < f.h) fillRect(sheet, ox, oy + f.py * scale, f.w * scale, Math.max(1, Math.floor(scale / 4)), LINE);
    blend(sheet, f.img, ox, oy, scale);
    text(sheet, f.name, ox, oy + maxH * scale + 6, 2, LABEL);
  });

  [["1X ON DARK", DARK], ["1X ON GREEN", GREEN]].forEach(([title, ground], section) => {
    const y0 = top + section * sectionH;
    text(sheet, title, PAD, y0 + 2, 2, LABEL);
    const gy = y0 + labelH;
    fillRect(sheet, PAD - gap / 2, gy, Math.min(perRow, frames.length) * boxW + gap, gridH, rgb(ground));
    frames.forEach((f, k) => {
      const bx = PAD + (k % perRow) * boxW;
      const by = gy + gap / 2 + Math.floor(k / perRow) * boxH;
      blend(sheet, f.img, bx + maxPx - f.px, by + maxPy - f.py, 1);
    });
  });
  return sheet;
}

// An atlas from its checked-in files: <png> and the .json beside it.
function loadAtlas(pngFile) {
  const table = JSON.parse(fs.readFileSync(pngFile.replace(/\.png$/i, ".json"), "utf8"));
  return { name: path.basename(pngFile, ".png"), image: readPng(pngFile), table };
}

// Writes the sheet of an atlas to <folder>/<name>-sheet.png (with options.page,
// <name>-<page>-sheet.png) and returns the path.
function writeSheet(atlas, folder, options) {
  fs.mkdirSync(folder, { recursive: true });
  const file = path.join(folder, atlas.name + (options && options.page ? "-" + options.page : "") + "-sheet.png");
  writePng(file, contactSheet(atlas, options));
  return file;
}

if (require.main === module) {
  const [png, scale, columns] = process.argv.slice(2);
  if (!png) {
    console.error("usage: node tools/games-art/sheet.js <atlas.png> [scale] [columns]");
    process.exit(2);
  }
  const atlas = loadAtlas(path.resolve(png));
  console.log("wrote " + writeSheet(atlas, path.join(__dirname, "out"), { scale: scale && Number(scale), columns: columns && Number(columns) }));
}

module.exports = { contactSheet, loadAtlas, writeSheet };
