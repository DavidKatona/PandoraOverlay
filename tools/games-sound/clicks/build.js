// The click sounds (G27, A106): renders take A of every cue (cues.js) and writes
//   Assets/Clicks/<cue>.wav
// and tools/games-sound/clicks/manifest.json (every source's and every WAV's hash,
// and each cue's level and length, which Games.Tests' SoundInStepTests holds the
// repo to, as it holds a game's).
//
//   node tools/games-sound/clicks/build.js [--board]
//
// --board also renders takes B and C into tools/games-sound/out/clicks/ and writes
// the third sound board, tools/games-sound/out/sound-board-v3.html, from the WAVs
// that ship (clicks/board.js). Run by hand, never by a build or a test (A84): the
// clicks reach a build as the checked-in files, embedded in the APP (not a game:
// the overlay plays them, and the host plays a game's clicks from the same files,
// A106), with no Node on the build machine. Deterministic: run twice in a row it
// changes nothing. It deletes nothing: a cue taken out of cues.js leaves its old
// WAV, which the test then names as a file no manifest wrote.
//
// The set's own place and bounds, beside hatchling/build.js's (whose R3 and R4 stay
// the games' cues'): R8's.
"use strict";
const fs = require("fs");
const path = require("path");
const { render, measure, RATE } = require("../synth");
const wav = require("../wav");
const { writeManifest, writeIfChanged, REPO, TOOL } = require("../manifest");
const { CUES } = require("./cues");

const SET = "clicks";
const CLICKS = path.join(REPO, "Assets", "Clicks");
const OUT = path.join(TOOL, "out");

// R8's bounds, refused here before a byte is written, so a recipe that breaks one
// never reaches the test as a mystery. The test holds the same bounds on the WAVs
// themselves, as its own constants (SoundInStepTests).
const MIN_SECONDS = 0.015, MAX_SECONDS = 0.08;   // R8: never longer than ClickSound.MinGap, 80 ms
const MIN_ATTACK = 0.001;                         // R8: a millisecond or more
const MIN_LEVEL = -24, MAX_LEVEL = -14;           // R8
const TAKES = 3;                                  // R9: A ships, B and C for the owner to pick from
const WAVES = ["triangle", "sine"];               // R8: no square, a click is never a warning or a fanfare
const MAX_SAMPLES = Math.round(MAX_SECONDS * RATE); // 3,528: 80 ms
const EDGE = Math.floor(0.002 * RATE);            // the last 2 ms (88 samples), silent (A87)
const SILENT = Math.pow(10, -60 / 20) * 32767;    // -60 dBFS in 16-bit steps

function check(cue) {
  const fail = (why) => { throw new Error(`cue '${cue.id}': ${why}`); };
  if (!/^[a-z]+(-[a-z]+)*$/.test(cue.id)) fail("a name is lower-case words joined by '-' (a file name and an enum value)");
  if (!(cue.level >= MIN_LEVEL && cue.level <= MAX_LEVEL)) fail(`level ${cue.level} is outside ${MIN_LEVEL} to ${MAX_LEVEL} dBFS (R8)`);
  if (cue.takes.length !== TAKES) fail(`a click has ${TAKES} takes, A ships (R9)`);
  for (const take of cue.takes) {
    if (!(take.dur >= MIN_SECONDS && take.dur <= MAX_SECONDS)) fail(`take '${take.name}' lasts ${take.dur} s, outside ${MIN_SECONDS} to ${MAX_SECONDS} (R8)`);
    for (const layer of take.layers) {
      if (!(layer.attack >= MIN_ATTACK)) fail(`take '${take.name}' has an attack under 1 ms (R8)`);
      if (layer.t + layer.dur > take.dur) fail(`take '${take.name}' has a layer that ends after the take`);
      if (layer.kind === "osc" && !WAVES.includes(layer.wave)) fail(`take '${take.name}' has a ${layer.wave} wave: triangle and sine only (R8)`);
    }
  }
}

function checkRendered(cue, take, samples) {
  const fail = (why) => { throw new Error(`cue '${cue.id}', take '${take.name}': ${why}`); };
  if (samples.length < 1 || samples.length > MAX_SAMPLES) fail(`${samples.length} samples: 1 to ${MAX_SAMPLES} (80 ms)`);
  // The first sample and the last 2 ms under -60 dBFS (A87: the rise between is
  // R8's attack, which check() refuses to break).
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

  // Render and check EVERY take before a byte is written, so a take that breaks a
  // rule leaves the files as they were.
  const rendered = CUES.map((cue) => cue.takes.map((take, i) => {
    if (i > 0 && !board) return null;
    const { samples } = render(take, cue.level);
    checkRendered(cue, take, samples);
    return samples;
  }));

  const written = [];
  const lengths = {};
  const takes = {}; // cue id -> [{ name, file }], for the board
  CUES.forEach((cue, c) => {
    takes[cue.id] = [];
    cue.takes.forEach((take, i) => {
      const samples = rendered[c][i];
      if (!samples) return;
      const file = i === 0
        ? path.join(CLICKS, `${cue.id}.wav`)
        : path.join(OUT, SET, `${cue.id}-${String.fromCharCode(97 + i)}.wav`);
      writeIfChanged(file, wav.write(samples));
      if (i === 0) {
        written.push(file);
        lengths[cue.id] = { level: cue.level, ms: measure(samples).ms };
      }
      takes[cue.id].push({ name: take.name, file });
    });
  });
  const manifest = writeManifest(SET, written, lengths);
  console.log(`${written.length} cues -> ${path.relative(REPO, CLICKS)}; ${path.relative(REPO, manifest)}`);

  if (board) {
    const { writeBoard } = require("./board");
    const hatchling = path.join(REPO, "Games", "PandoraOverlay.Games.Hatchling", "Sound");
    const file = writeBoard(path.join(OUT, "sound-board-v3.html"), {
      cues: CUES,
      takes,
      // Hatchling's shipped step and bite, the demos' level references (PLAN-CLICKS 7.2), read, never rendered here.
      refs: { step: path.join(hatchling, "step.wav"), bite: path.join(hatchling, "bite.wav") },
      read: (f) => wav.read(fs.readFileSync(f), path.basename(f)),
    });
    console.log(`the board -> ${path.relative(REPO, file)} (${fs.statSync(file).size} bytes)`);
  }
}

main();
