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

const ATLASES = [creatures];

const written = [];
const sheets = [];
for (const make of ATLASES) {
  const atlas = make();
  written.push(...writeAtlas(ART, atlas));
  if (process.argv.includes("--sheet")) sheets.push(writeSheet(atlas, SHEETS));
}
writeManifest("hatchling", written);
console.log("wrote " + written.length + " files to " + ART + " and the manifest" + (sheets.length ? "; sheets: " + sheets.join(", ") : ""));
