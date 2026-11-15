// Hatchling's sounds: renders take A of every cue (cues.js) and writes
//   Games/PandoraOverlay.Games.Hatchling/Sound/<cue>.wav
// and tools/games-sound/hatchling/manifest.json (every source's and every WAV's
// hash, and each cue's level and length, which Games.Tests' SoundInStepTests
// holds the repo to, A84).
//
//   node tools/games-sound/hatchling/build.js [--board]
//
// --board also renders takes B and C into tools/games-sound/out/hatchling/ and
// writes the second sound board, tools/games-sound/out/sound-board-v2.html, from
// the WAVs that ship (board.js). Run by hand, never by a build or a test (A84):
// sound reaches a build as the checked-in files, with no Node on the build
// machine. Deterministic: run twice in a row it changes nothing. It deletes
// nothing: a cue taken out of cues.js leaves its old WAV, which the test then
// names as a file no manifest wrote.
"use strict";
const fs = require("fs");
const path = require("path");
const { render, measure, RATE } = require("../synth");
const wav = require("../wav");
const { writeManifest, writeIfChanged, REPO, TOOL } = require("../manifest");
const { GROUPS, CUES } = require("./cues");

const SOUND = path.join(REPO, "Games", "PandoraOverlay.Games.Hatchling", "Sound");
const OUT = path.join(TOOL, "out");

// R3's and R4's bounds, refused here before a byte is written, so a recipe that
// breaks one never reaches the test as a mystery. The test holds the same bounds
// on the WAVs themselves (SoundInStepTests).
const MIN_SECONDS = 0.07, MAX_SECONDS = 0.85;   // R3
const MIN_ATTACK = 0.001;                         // R3: a millisecond or more
const MIN_LEVEL = -24, MAX_LEVEL = -5;            // R4
const MAX_SAMPLES = 2 * RATE;                     // GameSound.MaxSamples, 2 s (A78)
const EDGE = Math.floor(0.002 * RATE);            // the last 2 ms (88 samples), silent (A87)
const SILENT = Math.pow(10, -60 / 20) * 32767;    // -60 dBFS in 16-bit steps

function check(cue) {
  const fail = (why) => { throw new Error(`cue '${cue.id}': ${why}`); };
  if (!/^[a-z]+(-[a-z]+)*$/.test(cue.id)) fail("a name is lower-case words joined by '-' (a file name and an enum value)");
  if (!GROUPS.some((g) => g.id === cue.group)) fail(`unknown group '${cue.group}'`);
  if (!(cue.level >= MIN_LEVEL && cue.level <= MAX_LEVEL)) fail(`level ${cue.level} is outside ${MIN_LEVEL} to ${MAX_LEVEL} dBFS (R4)`);
  if (cue.takes.length !== (cue.pick ? 1 : 3)) fail(cue.pick ? "a pick has one take, the owner's (R1)" : "a new cue has three takes, A ships (R5)");
  for (const take of cue.takes) {
    if (!(take.dur >= MIN_SECONDS && take.dur <= MAX_SECONDS)) fail(`take '${take.name}' lasts ${take.dur} s, outside ${MIN_SECONDS} to ${MAX_SECONDS} (R3)`);
    for (const layer of take.layers) {
      if (!(layer.attack >= MIN_ATTACK)) fail(`take '${take.name}' has an attack under 1 ms (R3)`);
      if (layer.t + layer.dur > take.dur) fail(`take '${take.name}' has a layer that ends after the take`);
    }
  }
}

function checkRendered(cue, take, samples) {
  const fail = (why) => { throw new Error(`cue '${cue.id}', take '${take.name}': ${why}`); };
  if (samples.length < 1 || samples.length > MAX_SAMPLES) fail(`${samples.length} samples`);
  // The first sample and the last 2 ms under -60 dBFS (A87: the rise between is
  // R3's attack, which build.js refuses to break, in check()).
  if (Math.abs(samples[0]) >= SILENT) fail("the first sample is not silent");
  for (let k = samples.length - EDGE; k < samples.length; k++) if (Math.abs(samples[k]) >= SILENT) fail("the last 2 ms are not silent");
  if (Math.abs(measure(samples).peakDb - cue.level) > 0.5) fail("the peak is not the level");
}

function main() {
  const board = process.argv.includes("--board");
  const ids = new Set();
  for (const cue of CUES) {
    if (ids.has(cue.id)) throw new Error(`cue '${cue.id}' twice`);
    ids.add(cue.id);
    check(cue);
  }

  const written = [];
  const lengths = {};
  const takes = {}; // cue id -> [{ name, file, samples }], for the board
  for (const cue of CUES) {
    takes[cue.id] = [];
    cue.takes.forEach((take, i) => {
      if (i > 0 && !board) return;
      const { samples } = render(take, cue.level);
      checkRendered(cue, take, samples);
      const file = i === 0
        ? path.join(SOUND, `${cue.id}.wav`)
        : path.join(OUT, "hatchling", `${cue.id}-${String.fromCharCode(97 + i)}.wav`);
      writeIfChanged(file, wav.write(samples));
      if (i === 0) {
        written.push(file);
        lengths[cue.id] = { level: cue.level, ms: measure(samples).ms };
      }
      takes[cue.id].push({ name: take.name, file });
    });
  }
  const manifest = writeManifest("hatchling", written, lengths);
  console.log(`${written.length} cues -> ${path.relative(REPO, SOUND)}; ${path.relative(REPO, manifest)}`);

  if (board) {
    const { writeBoard } = require("../board");
    const file = writeBoard(path.join(OUT, "sound-board-v2.html"), { groups: GROUPS, cues: CUES, takes, read: (f) => wav.read(fs.readFileSync(f), path.basename(f)) });
    console.log(`the board -> ${path.relative(REPO, file)} (${fs.statSync(file).size} bytes)`);
  }
}

main();
