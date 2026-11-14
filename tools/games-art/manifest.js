// The manifest a game's build.js writes beside its description files (A28):
// the SHA-256 of every generator source and of every file the build wrote, so
// ArtInStepTests (Games/PandoraOverlay.Games.Tests/Art/) can fail the moment a
// source changed without a rebuild, or an output was edited by hand.
//
//   { "sources": { "tools/games-art/engine.js": "<sha256>", ... },
//     "outputs": { "Games/PandoraOverlay.Games.Hatchling/Art/creatures.png": "<sha256>", ... } }
//
// SOURCES are not only the files a build happened to require: they are EVERY .js
// the tool holds that this game depends on by being in the tool, that is, the
// shared ones (the .js files directly in tools/games-art/) and all of the game's
// own (everything under tools/games-art/<game>/), so a new shared helper or a
// new description file is in step with the art the moment it exists, whether
// build.js reads it yet or not. out/ (contact sheets, scratch, git-ignored) is
// never a source. Paths are relative to the repo's root with forward slashes,
// the hashes are of the bytes on disk, keys are sorted: the manifest is the same
// on every PC and in every run, so a second build changes nothing.
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const TOOL = __dirname; // <repo>/tools/games-art
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

// Every source of a game: the shared .js files and the game's own, as repo paths.
function sourcesOf(game) {
  const shared = fs
    .readdirSync(TOOL, { withFileTypes: true })
    .filter((e) => e.isFile() && e.name.endsWith(".js"))
    .map((e) => path.join(TOOL, e.name));
  return [...shared, ...jsFilesUnder(path.join(TOOL, game))].map(repoPath).sort();
}

// Writes tools/games-art/<game>/manifest.json. outputs: the files the build wrote (absolute or relative paths).
function writeManifest(game, outputs) {
  const sources = {};
  for (const rel of sourcesOf(game)) sources[rel] = sha256(path.join(REPO, rel));
  const outs = {};
  for (const rel of outputs.map(repoPath).sort()) outs[rel] = sha256(path.join(REPO, rel));
  const file = path.join(TOOL, game, "manifest.json");
  const text = JSON.stringify({ sources, outputs: outs }, null, 2) + "\n";
  if (!fs.existsSync(file) || fs.readFileSync(file, "utf8") !== text) fs.writeFileSync(file, text);
  return file;
}

module.exports = { writeManifest, sourcesOf, sha256, repoPath, REPO, TOOL };
