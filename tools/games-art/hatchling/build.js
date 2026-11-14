// Hatchling's art: draws it, packs it, and writes
//   Games/PandoraOverlay.Games.Hatchling/Art/<atlas>.png and .json
// and tools/games-art/hatchling/manifest.json (every source's and every
// output's hash, which Games.Tests' ArtInStepTests holds the repo to).
//
//   node tools/games-art/hatchling/build.js [--sheet]
//
// --sheet also writes each atlas's contact sheet to tools/games-art/out/. It is
// run by hand and never by a build or a test (A28): art reaches a build as the
// checked-in files, with no Node on the build machine. Deterministic: run twice
// in a row it changes nothing.
//
// A new atlas (terrain, things, interface) is one more entry in ATLASES below,
// built by its own file in this folder; add the file's output to `written` and
// it is in the manifest. Every .js here is a source in the manifest by being
// here (manifest.js), so a new file needs no registration.
const path = require("path");
const { pack, writeAtlas, frameOf, frameName, animationName } = require("../pack");
const { writeManifest, REPO } = require("../manifest");
const { writeSheet } = require("../sheet");
const { ORDER, SIZES, ACTIONS, draw } = require("./species");
const terrainArt = require("./terrain");
const thingsArt = require("./things");

const ART = path.join(REPO, "Games", "PandoraOverlay.Games.Hatchling", "Art");
const SHEETS = path.join(REPO, "tools", "games-art", "out");

// creatures: every species at its four sizes (6.2, H21: `<Species>-s`, `-m`,
// `-l` and the full `<Species>`), each named as Assets/rules.json spells the
// species (how a reading names a dino) with the size's suffix, in every action
// of species.js's ACTIONS: standing (one frame), walking (four; 6.2), and
// striking (two), hurt (one) and eating (two; 6.3, task H12), each action an
// animation with its ticks and `loops` (A39). A frame's pivot is the middle
// of its feet on the ground line, the pixel the game draws it at: the size's
// own (10, 13), (13, 17), (15, 21) or (18, 24), on the frame's bottom row.
function creatures() {
  const frames = [];
  const animations = [];
  for (const species of ORDER) {
    for (const size of SIZES) {
      const subject = species + size.suffix;
      for (const { action, ticks, loops } of ACTIONS) {
        const names = ticks.map((_, i) => frameName(subject, action, i));
        names.forEach((name, i) => frames.push(frameOf(name, draw(species, size, action, i), size.px, size.py)));
        animations.push({ name: animationName(subject, action), frames: names, ticks: [...ticks], loops });
      }
    }
  }
  return pack({ name: "creatures", frames, animations });
}

// portraits (6.3, task H12): what Camp shows in its well, `<Species>/portrait/0`,
// 72 x 50, pivot (0, 0): the full-size standing frame doubled exactly, each
// pixel 2 x 2, made here from the same drawing and never drawn anew, so a
// portrait is always its animal's standing frame. One animation each (A39),
// one frame, looping.
const PORTRAIT_SCALE = 2;
function portraits() {
  const frames = [];
  const animations = [];
  const full = SIZES.find((s) => s.scale === 1);
  for (const species of ORDER) {
    const name = frameName(species, "portrait", 0);
    frames.push(frameOf(name, doubled(draw(species, full, "stand", 0), PORTRAIT_SCALE), 0, 0));
    animations.push({ name: animationName(species, "portrait"), frames: [name], ticks: [1], loops: true });
  }
  return pack({ name: "portraits", frames, animations });
}
function doubled(img, k) {
  const out = { w: img.w * k, h: img.h * k, px: new Array(img.w * k * img.h * k) };
  for (let y = 0; y < out.h; y++) for (let x = 0; x < out.w; x++) out.px[y * out.w + x] = [...img.px[Math.floor(y / k) * img.w + Math.floor(x / k)]];
  return out;
}

// An atlas of frames that are not dinos (terrain, things): each description
// file lists its frames as { subject, action, index, img, px, py } and the
// ticks of its actions; every action becomes an animation of its frames in
// index order (A39: every action is one, its ticks and loops stated). An
// action with no entry in `ticks` shows each frame for 1 tick: the one-frame
// ones, and the tile variants and the digits, which the painter picks by index
// and never plays. All of them loop.
function atlasOf(name, list, ticks) {
  const frames = [];
  const actions = new Map();
  for (const f of list) {
    frames.push(frameOf(frameName(f.subject, f.action, f.index), f.img, f.px, f.py));
    const anim = animationName(f.subject, f.action);
    if (!actions.has(anim)) actions.set(anim, []);
    actions.get(anim).push(f);
  }
  const animations = [...actions].map(([anim, members]) => {
    const ordered = [...members].sort((a, b) => a.index - b.index);
    return { name: anim, frames: ordered.map((f) => frameName(f.subject, f.action, f.index)), ticks: ordered.map(() => ticks[anim] || 1), loops: true };
  });
  return pack({ name, frames, animations });
}

// terrain (6.1): the board's tiles, the fog and the cell marks, 44 x 50 each,
// pivot (0, 0): the cell box's top-left.
function terrain() {
  const list = terrainArt.frames().map(([subject, action, index, img]) => ({ subject, action, index, img, px: 0, py: 0 }));
  return atlasOf("terrain", list, terrainArt.TICKS);
}

// things (6.1): plants, carcasses, small game, rings, marks and digits.
function things() {
  return atlasOf("things", thingsArt.frames(), thingsArt.TICKS);
}

const ATLASES = [creatures, terrain, things, portraits];

// The creatures atlas is looked at one size and one half of its actions to a
// page (420 frames on one sheet was more than Node's memory held, H11; with
// the acting frames there are 840): creatures-full-move-sheet.png (stand,
// walk) and creatures-full-act-sheet.png (strike, hurt, eat), the same for -l,
// -m and -s.
const SIZE_PAGES = [
  ["full", (n) => /^[A-Za-z]+\//.test(n)],
  ["l", (n) => n.includes("-l/")],
  ["m", (n) => n.includes("-m/")],
  ["s", (n) => n.includes("-s/")]
];
const ACTION_PAGES = [
  ["move", (n) => /\/(stand|walk)\//.test(n)],
  ["act", (n) => /\/(strike|hurt|eat)\//.test(n)]
];
const SHEET_PAGES = {
  creatures: SIZE_PAGES.flatMap(([size, bySize]) => ACTION_PAGES.map(([half, byAction]) => ({ page: size + "-" + half, only: (n) => bySize(n) && byAction(n) })))
};

const written = [];
const sheets = [];
for (const make of ATLASES) {
  const atlas = make();
  written.push(...writeAtlas(ART, atlas));
  if (process.argv.includes("--sheet")) for (const options of SHEET_PAGES[atlas.name] || [{}]) sheets.push(writeSheet(atlas, SHEETS, options));
}
writeManifest("hatchling", written);
console.log("wrote " + written.length + " files to " + ART + " and the manifest" + (sheets.length ? "; sheets: " + sheets.join(", ") : ""));
