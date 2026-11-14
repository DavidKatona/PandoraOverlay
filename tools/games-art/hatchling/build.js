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
const { SPECIES, ORDER } = require("./species");
const terrainArt = require("./terrain");
const thingsArt = require("./things");

const ART = path.join(REPO, "Games", "PandoraOverlay.Games.Hatchling", "Art");
const SHEETS = path.join(REPO, "tools", "games-art", "out");

// A dino's pivot: the middle of its feet on the ground line, the pixel the game
// draws it at. The standing frames are 36 x 25 and the feet end on the bottom row.
const DINO_PIVOT = { px: 18, py: 24 };

// creatures: each species' standing frame, named as Assets/rules.json spells the
// species (how a reading names a dino), and its one-frame animation.
function creatures() {
  const frames = [];
  const animations = [];
  for (const species of ORDER) {
    const standing = frameName(species, "stand", 0);
    frames.push(frameOf(standing, SPECIES[species](), DINO_PIVOT.px, DINO_PIVOT.py));
    animations.push({ name: animationName(species, "stand"), frames: [standing], ticks: [1], loops: true });
  }
  return pack({ name: "creatures", frames, animations });
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

const ATLASES = [creatures, terrain, things];

const written = [];
const sheets = [];
for (const make of ATLASES) {
  const atlas = make();
  written.push(...writeAtlas(ART, atlas));
  if (process.argv.includes("--sheet")) sheets.push(writeSheet(atlas, SHEETS));
}
writeManifest("hatchling", written);
console.log("wrote " + written.length + " files to " + ART + " and the manifest" + (sheets.length ? "; sheets: " + sheets.join(", ") : ""));
