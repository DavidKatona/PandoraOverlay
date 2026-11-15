// The manifest a game's build.js writes beside its cues (A84), the art's twin
// (tools/games-art/manifest.js, A28): the SHA-256 of every generator source and of
// every WAV the build wrote, and each cue's level and length, so SoundInStepTests
// (Games/PandoraOverlay.Games.Tests/Sound/) fails the moment a source changed
// without a rebuild, an output was edited by hand, or a WAV is not what its cue
// says.
//
//   { "sources": { "tools/games-sound/synth.js": "<sha256>", ... },
//     "outputs": { "Games/PandoraOverlay.Games.Hatchling/Sound/bite.wav": "<sha256>", ... },
//     "cues":    { "bite": { "level": -5, "ms": 220 }, ... } }
//
// SOURCES are every .js the tool holds that this game depends on by being in the
// tool: the shared ones (directly in tools/games-sound/) and all of the game's own
// (everything under tools/games-sound/<game>/), so a new helper is in step the
// moment it exists, whether build.js reads it yet or not. out/ (takes B and C, the
// board, git-ignored) is never a source. A separate tool from the art's on
// purpose: the art's manifest takes every .js in tools/games-art/ as a source of
// every game, so a synth there would send someone to rebuild the art for a sound
// (PLAN-SOUND 5.1). Paths are repo-relative with forward slashes, the hashes are
// of the bytes on disk, keys sorted, two-space JSON, LF; the file is written only
// when its text changed: a second build changes nothing.
"use strict";
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const TOOL = __dirname; // <repo>/tools/games-sound
const REPO = path.resolve(TOOL, "..", "..");

function repoPath(file) {
  const rel = path.relative(REPO, path.resolve(file)).split(path.sep).join("/");
  if (rel.startsWith("..") || path.isAbsolute(rel)) throw new Error(`'${file}' is outside the repo`);
  return rel;
}

function sha256(file) {
  return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

function jsFilesUnder(folder) {
  const found = [];
  for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
    const full = path.join(folder, entry.name);
    if (entry.isDirectory()) found.push(...jsFilesUnder(full));
    else if (entry.isFile() && entry.name.endsWith(".js")) found.push(full);
  }
  return found;
}

// Every source of a game: the shared .js files and the game's own, as repo paths, sorted.
function sourcesOf(game) {
  const shared = fs
    .readdirSync(TOOL, { withFileTypes: true })
    .filter((e) => e.isFile() && e.name.endsWith(".js"))
    .map((e) => path.join(TOOL, e.name));
  return [...shared, ...jsFilesUnder(path.join(TOOL, game))].map(repoPath).sort();
}

function sorted(object) {
  const out = {};
  for (const key of Object.keys(object).sort()) out[key] = object[key];
  return out;
}

// Writes tools/games-sound/<game>/manifest.json. outputs: the WAVs the build wrote;
// cues: { <cue>: { level, ms } }.
function writeManifest(game, outputs, cues) {
  const sources = {};
  for (const rel of sourcesOf(game)) sources[rel] = sha256(path.join(REPO, rel));
  const outs = {};
  for (const rel of outputs.map(repoPath).sort()) outs[rel] = sha256(path.join(REPO, rel));
  const lengths = {};
  for (const [name, cue] of Object.entries(sorted(cues))) lengths[name] = { level: cue.level, ms: cue.ms };
  const file = path.join(TOOL, game, "manifest.json");
  const text = JSON.stringify({ sources, outputs: outs, cues: lengths }, null, 2) + "\n";
  if (!fs.existsSync(file) || fs.readFileSync(file, "utf8") !== text) fs.writeFileSync(file, text);
  return file;
}

// Writes a file only when its bytes differ, so a rebuild leaves untouched files untouched.
function writeIfChanged(file, bytes) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  if (fs.existsSync(file) && Buffer.compare(fs.readFileSync(file), bytes) === 0) return false;
  fs.writeFileSync(file, bytes);
  return true;
}

module.exports = { writeManifest, writeIfChanged, sourcesOf, sha256, repoPath, REPO, TOOL };
