// The third sound board (PLAN-CLICKS 7.2, R10): one HTML page the owner listens
// on, written by `node tools/games-sound/clicks/build.js --board` into
// tools/games-sound/out/sound-board-v3.html (git-ignored) and copied by hand into
// the games folder; the orchestrator publishes it.
//
// It holds only the clicks, on the second board's page: the first board's CSS
// tokens and dark mode exactly, its fonts as it loads them (Google Fonts), its
// layout (one column of cue cards, each a row of three takes, a sticky bar), its
// way of picking and noting (kept in the browser under a key of its own), and no
// script from anywhere. What it plays is what SHIPS: every take is the WAV the
// build wrote, embedded as a data: URI and decoded by the page's own PCM reader
// into an AudioBuffer at 44,100 Hz (never decodeAudioData, which may resample);
// and the demos' two game sounds are Hatchling's shipped step.wav and bite.wav,
// read from the game's Sound/ folder, the same way.
//
// COPIED, not imported, from tools/games-sound/board.js (the second board): the
// first <style> block (the first board's head and CSS, verbatim but for the
// title), and in the page's script readWav(), measure() and drawWave() verbatim.
// Copied so the second board's source, its hash in hatchling/manifest.json and its
// page stay byte for byte as they were (C01's brief, section 3); a change to one
// copy is not a change to the other.
//
// The board is a review page, not a test (PLAN-CLICKS risk 12): its demos are
// hand-timed from 7.2, and a later recipe change in Hatchling does not retime them.
"use strict";
const fs = require("fs");
const path = require("path");

// ---- The demos (PLAN-CLICKS 7.2) -------------------------------------------------------
//
// Each: its window (the volume it plays at: "overlay" or "games"), and its events,
// [seconds, "click" | "ref", cue id or Hatchling's sound]. Clicks are taken at play
// time from the takes picked on the page (A until he picks) and pass the 80 ms gap
// (ClickSound.MinGap, A99) as the app will apply it: a click that starts within
// 80 ms of the last one played is dropped, never queued.
const MIN_GAP = 0.08;

function demos() {
  const at = (times, kind, ids) => times.map((t, i) => [t, kind, Array.isArray(ids) ? ids[i] : ids]);
  const after = (gaps) => gaps.reduce((acc, g) => [...acc, acc[acc.length - 1] + g], [0]);

  // 1. Browsing Settings: ten clicks at a reading pace, 0.6 to 1 s apart, about 7 s.
  const browsingIds = ["select", "select", "select", "toggle-on", "toggle-off", "select", "press", "arm", "press", "back"];
  const browsing = at(after([0.8, 0.7, 0.9, 0.8, 0.9, 0.7, 0.8, 0.6, 0.8]), "click", browsingIds);

  // 2. Fast clicking: one button twelve times at eight a second (0.125 s apart, all
  // past the gap), a second's pause, then twelve at twenty a second (0.05 s apart:
  // the gap drops every other one).
  const slow = Array.from({ length: 12 }, (_, k) => k * 0.125);
  const fastStart = slow[11] + 1;
  const fast = Array.from({ length: 12 }, (_, k) => fastStart + k * 0.05);
  const fastClicking = at([...slow, ...fast], "click", "press");

  // 3. In the Games window: a tab, a pace, Fast, then three of a turn's steps 0.33 s
  // apart (Hatchling's shipped step.wav), then "‹ Games".
  const games = [
    ...at([0, 0.8, 1.6], "click", ["select", "select", "toggle-on"]),
    ...at([2.4, 2.73, 3.06], "ref", "step"),
    ...at([3.86], "click", "back"),
  ];

  // 4. Under a bite: press, Hatchling's bite 0.5 s later, press again 0.5 s after it.
  const bite = [...at([0], "click", "press"), ...at([0.5], "ref", "bite"), ...at([1.0], "click", "press")];

  // 5. Asked, then done: arm, then press 0.8 s later (Delete all, asked and done).
  const asked = at([0, 0.8], "click", ["arm", "press"]);

  const list = [
    { id: "browsing", title: "Browsing Settings", window: "overlay",
      line: "Three nav clicks, a check ticked, a check cleared, a slider let go, a button, an armed button asked and confirmed, then Close: about 7 s at a reading pace.",
      events: browsing },
    { id: "fast", title: "Fast clicking", window: "overlay",
      line: "One button twelve times at eight a second (all heard), then twelve times at twenty a second with the 80 ms gap applied: every other one is dropped, as the overlay will drop it.",
      events: fastClicking },
    { id: "games", title: "In the Games window", window: "games",
      line: "A tab, a pace, Fast ticked, then three of a turn's steps (Hatchling's own step), then \"‹ Games\": a click's level against the game's.",
      events: games },
    { id: "bite", title: "Under a bite", window: "games",
      line: "A click, Hatchling's bite half a second later, and a click again.",
      events: bite },
    { id: "asked", title: "Asked, then done", window: "overlay",
      line: "An armed button asks (arm), and the second click does it (press) 0.8 s later: Delete all, asked and done.",
      events: asked },
  ];
  // 7.2's figures, held here so a slip in the times above shows at once.
  const span = (d) => d.events[d.events.length - 1][0];
  if (Math.abs(span(list[0]) - 7) > 0.5) throw new Error(`Browsing Settings lasts ${span(list[0])} s, 7.2 says about 7`);
  if (list[1].events.length !== 24) throw new Error("Fast clicking is twelve and twelve");
  return list;
}

// ---- The page ------------------------------------------------------------------------

// The first board's <head> and CSS, as tools/games-sound/board.js has it (its tokens,
// its dark mode, its fonts), the title aside; then, in a second block, the parts
// the third board needs, in its tokens.
const HEAD = `<meta charset="utf-8">
<title>Click Sound Board</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Pixelify+Sans:wght@500;700&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
/* Layout: one column of cue cards, each a row of three takes; a sticky bar holds the master volume and the turn demo. */
:root {
  --bg: #eef0ea;
  --surface: #f8f9f5;
  --well: #e2e6dc;
  --fg: #1d2420;
  --muted: #5b665e;
  --line: #c9cfc3;
  --amber: #b86a00;
  --amber-soft: #f3dfbd;
  --wave: #3d6b4f;
  --focus: #1f5fbf;
  --display: "Pixelify Sans", "Courier New", monospace;
  --body: "IBM Plex Sans", "Segoe UI", system-ui, sans-serif;
  --mono: "IBM Plex Mono", Consolas, monospace;
  color-scheme: light;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg: #141a17; --surface: #1c2420; --well: #111613; --fg: #e6ebe4; --muted: #9aa69d;
    --line: #2f3a34; --amber: #f0a43a; --amber-soft: #3b2c14; --wave: #8fd1a6; --focus: #7fb0ff;
    color-scheme: dark;
  }
}
:root[data-theme="dark"] {
  --bg: #141a17; --surface: #1c2420; --well: #111613; --fg: #e6ebe4; --muted: #9aa69d;
  --line: #2f3a34; --amber: #f0a43a; --amber-soft: #3b2c14; --wave: #8fd1a6; --focus: #7fb0ff;
  color-scheme: dark;
}
html, body { background: var(--bg); color: var(--fg); }
body {
  font-family: var(--body); font-size: 15px; line-height: 1.5;
  padding-left: max(16px, env(safe-area-inset-left, 0px));
  padding-right: max(16px, env(safe-area-inset-right, 0px));
  padding-block: 0 48px;
}
body::before { content: ""; position: fixed; inset: 0 0 auto; height: env(safe-area-inset-top, 0px); z-index: 10; background: var(--bg); }
.wrap { max-width: 920px; margin: 0 auto; display: grid; gap: 20px; }
header { padding-top: 28px; display: grid; gap: 8px; }
h1 { font-family: var(--display); font-weight: 700; font-size: 34px; line-height: 1.1; margin: 0; letter-spacing: 0.5px; text-wrap: balance; }
h1 .amber { color: var(--amber); }
.lede { margin: 0; color: var(--muted); max-width: 64ch; }
.bar {
  position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5;
  background: var(--bg); border-bottom: 1px solid var(--line);
  display: flex; flex-wrap: wrap; align-items: center; gap: 12px 20px; padding-block: 12px;
}
.bar label { font-size: 13px; color: var(--muted); display: flex; align-items: center; gap: 8px; }
.bar input[type=range] { width: 140px; accent-color: var(--amber); }
.bar .spacer { flex: 1; }
button {
  font: inherit; font-size: 14px; font-weight: 500; color: var(--fg); background: var(--surface);
  border: 1px solid var(--line); border-radius: 6px; padding: 7px 12px; cursor: pointer;
}
button:hover { border-color: var(--amber); }
button:focus-visible, input:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
button.primary { background: var(--amber); border-color: var(--amber); color: #1a1206; }
.cue { display: grid; gap: 10px; padding-block: 16px; border-bottom: 1px solid var(--line); }
.cue-head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; }
.cue h2 { font-family: var(--display); font-weight: 500; font-size: 20px; margin: 0; }
.when { color: var(--muted); font-size: 13px; }
.takes { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; }
.take {
  display: grid; gap: 6px; padding: 10px; background: var(--surface);
  border: 1px solid var(--line); border-radius: 8px; min-width: 0;
}
.take.picked { border-color: var(--amber); box-shadow: inset 0 0 0 1px var(--amber); }
.take-top { display: flex; align-items: center; gap: 8px; }
.take-top .name { font-weight: 600; flex: 1; min-width: 0; }
.play { padding: 5px 10px; }
canvas { width: 100%; height: 40px; display: block; background: var(--well); border-radius: 4px; }
.meta { font-family: var(--mono); font-size: 12px; color: var(--muted); font-variant-numeric: tabular-nums; }
.pick { font-size: 13px; display: flex; align-items: center; gap: 6px; cursor: pointer; }
.pick input { accent-color: var(--amber); }
.note { font: inherit; font-size: 13px; width: 100%; box-sizing: border-box; padding: 6px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); color: var(--fg); }
details { font-size: 13px; color: var(--muted); }
details pre { font-family: var(--mono); font-size: 11.5px; background: var(--well); padding: 8px; border-radius: 6px; overflow-x: auto; color: var(--fg); }
.foot { display: grid; gap: 10px; }
.foot textarea { font-family: var(--mono); font-size: 12.5px; width: 100%; box-sizing: border-box; min-height: 120px; padding: 10px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface); color: var(--fg); }
.status { font-size: 13px; color: var(--muted); }
@media (max-width: 640px) {
  .takes { grid-template-columns: minmax(0, 1fr); }
  h1 { font-size: 28px; }
}
</style>
<style>
/* The third board's own parts, in the first board's tokens: the two windows' volume steps, the demo picker, the "ships" mark, the line that says what plays. */
.vol { display: flex; align-items: center; gap: 8px; }
.steps { display: flex; gap: 4px; }
.steps button, .mute { padding: 5px 9px; font-size: 13px; font-variant-numeric: tabular-nums; }
.steps button.lit { background: var(--amber-soft); border-color: var(--amber); }
.mute.on { background: var(--amber); border-color: var(--amber); color: #1a1206; }
.bar select { font: inherit; font-size: 14px; color: var(--fg); background: var(--surface); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; max-width: 100%; }
.bar .line { flex-basis: 100%; }
.ships { font-size: 12px; font-weight: 600; color: var(--amber); }
.truth { margin: 0; max-width: 64ch; font-weight: 500; border-left: 3px solid var(--amber); padding-left: 10px; }
.plays { display: flex; gap: 6px; }
</style>`;

const BODY = `<div class="wrap">
  <header>
    <h1>Click <span class="amber">Sound Board</span> 3</h1>
    <p class="truth">What plays here is exactly what ships: every ▶ plays the very file the overlay will embed. The levels are relative (your speakers and Windows set the rest), and the real test is in game, against The Isle.</p>
    <p class="lede">The six click sounds, for the overlay's buttons and the games' alike, every one made by code from the recipe under it. Each has three takes: A ships until you pick another. ▶ plays a take once and ▶▶ eight times, a third of a second apart, the way a player browsing would hear it; both play at the Overlay volume. The two volumes are the overlay's own and the Games window's, five steps each with the third the default; each demo plays at its window's step, built from the takes you picked. Pick, add a note where one is off, play the demos, then copy your picks at the bottom and paste them to Claude.</p>
  </header>

  <div class="bar">
    <span class="vol"><label>Overlay</label><span class="steps" id="steps-overlay"></span></span>
    <span class="vol"><label>Games window</label><span class="steps" id="steps-games"></span></span>
    <button class="mute" id="mute" aria-pressed="false">Mute</button>
    <label for="demo">Demo <select id="demo"></select></label>
    <button class="primary" id="play-demo">▶ Play the demo</button>
    <span class="status line" id="demoStatus"></span>
  </div>

  <main id="cues"></main>

  <section class="foot">
    <h2 style="font-family: var(--display); font-weight: 500; font-size: 20px; margin: 0;">Your picks</h2>
    <textarea id="picksText" readonly aria-label="Your picks as text"></textarea>
    <div style="display:flex; gap:10px; flex-wrap:wrap; align-items:center;">
      <button class="primary" id="copy">Copy my picks</button>
      <span class="status" id="copyStatus"></span>
    </div>
  </section>
</div>`;

// The page's script, as a function so it is real JavaScript here; it is written
// into the page as its source text and called with the data. Nothing in it
// reaches beyond the page: no fetch, no other script.
/* eslint-disable */
function page(DATA) {
const { CUES, REFS, DEMOS, STEPS_DB, DEFAULT_STEP, MIN_GAP, BURST } = DATA;
const RATE = 44100;

// ---- The page's own PCM reader: the shipped file, exactly ---------------------------
// Verbatim from the second board (tools/games-sound/board.js).
// RIFF/WAVE, PCM, 1 channel, 44,100 Hz, 16 bits (what wav.js writes), each sample
// s / 32767, the writer's own scale undone, into an AudioBuffer at 44,100 Hz.
function readWav(uri) {
  const text = atob(uri.slice(uri.indexOf(",") + 1));
  const bytes = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) bytes[i] = text.charCodeAt(i);
  const v = new DataView(bytes.buffer);
  const tag = (at) => String.fromCharCode(bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]);
  if (tag(0) !== "RIFF" || tag(8) !== "WAVE" || tag(12) !== "fmt " || v.getUint16(20, true) !== 1 || v.getUint16(22, true) !== 1
    || v.getUint32(24, true) !== RATE || v.getUint16(34, true) !== 16 || tag(36) !== "data") throw new Error("not the shipped format");
  const n = v.getUint32(40, true) / 2;
  const d = new Float32Array(n);
  for (let i = 0; i < n; i++) d[i] = v.getInt16(44 + 2 * i, true) / 32767;
  const buf = new AudioBuffer({ length: n, sampleRate: RATE, numberOfChannels: 1 });
  buf.copyToChannel(d, 0);
  return buf;
}
function measure(buf) {
  const d = buf.getChannelData(0);
  let peak = 0, sum = 0;
  for (let i = 0; i < d.length; i++) { peak = Math.max(peak, Math.abs(d[i])); sum += d[i] * d[i]; }
  return { ms: Math.round(d.length * 1000 / RATE), peakDb: 20 * Math.log10(peak || 1e-9), rmsDb: 20 * Math.log10(Math.sqrt(sum / d.length) || 1e-9) };
}

// ---- Playback: two windows' volumes and a mute (A82's steps, A103) -----------------------
// The overlay's clicks play at Settings' "Click sounds" step, the Games window's at
// its own step; the mute silences both. A source goes into its window's gain, a
// demo's or a burst's through a bus of its own so it can be stopped.
let live = null, master = null;
const gains = {};
const steps = { overlay: DEFAULT_STEP, games: DEFAULT_STEP };
let muted = false;
const log = []; // what was played, for whoever checks the page: [cue or sound, take, seconds from now, window]
window.boardLog = log;
function audio() {
  if (!live) {
    live = new AudioContext();
    master = live.createGain(); master.connect(live.destination);
    for (const w of ["overlay", "games"]) { gains[w] = live.createGain(); gains[w].connect(master); }
    setVolume();
  }
  if (live.state === "suspended") live.resume();
  return live;
}
function setVolume() {
  if (!master) return;
  master.gain.value = muted ? 0 : 1;
  for (const w of ["overlay", "games"]) gains[w].gain.value = Math.pow(10, STEPS_DB[steps[w] - 1] / 20);
}
function play(buf, when, to, what) {
  const ctx = audio();
  const s = ctx.createBufferSource(); s.buffer = buf; s.connect(to); s.start(ctx.currentTime + 0.02 + when);
  log.push(what.concat([Number(when.toFixed(3))]));
  return s;
}

// ---- Picks (kept in this browser only, under the third board's own key) ------------
const KEY = "pandora-click-board-v3-picks";
let picks = {};
try { picks = JSON.parse(localStorage.getItem(KEY) || "{}") || {}; } catch { picks = {}; }
function save() { try { localStorage.setItem(KEY, JSON.stringify(picks)); } catch {} writePicks(); }
function pickOf(cue) { return picks[cue.id]?.take ?? 0; }

// ---- The page ------------------------------------------------------------------------
const decoded = {};   // cue id + take index -> AudioBuffer of the file
const refs = {};      // Hatchling's step and bite, decoded the same way
const escape = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
// Verbatim from the second board.
function drawWave(canvas, buf) {
  const dpr = window.devicePixelRatio || 1, w = canvas.clientWidth || 200, h = canvas.clientHeight || 40;
  canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
  const g = canvas.getContext("2d"); g.scale(dpr, dpr); g.clearRect(0, 0, w, h);
  g.strokeStyle = css("--line"); g.beginPath(); g.moveTo(0, h / 2); g.lineTo(w, h / 2); g.stroke();
  const d = buf.getChannelData(0), step = d.length / w;
  g.fillStyle = css("--wave");
  for (let x = 0; x < w; x++) {
    let lo = 0, hi = 0;
    for (let i = Math.floor(x * step); i < Math.floor((x + 1) * step); i++) { lo = Math.min(lo, d[i]); hi = Math.max(hi, d[i]); }
    g.fillRect(x, h / 2 - hi * h / 2, 1, Math.max(1, (hi - lo) * h / 2));
  }
}
function build() {
  const root = document.getElementById("cues");
  for (const cue of CUES) {
    const sec = document.createElement("section"); sec.className = "cue";
    sec.innerHTML = `<div class="cue-head"><h2>${escape(cue.id)}</h2><span class="when">${escape(cue.when)} ${escape(cue.often)}. Level ${cue.level} dBFS, ${cue.ms} ms.</span></div>
      <div class="takes"></div>
      <input class="note" id="note-${cue.id}" placeholder="A note for this one (optional): too sharp, softer, shorter…" value="${escape(picks[cue.id]?.note ?? "")}">`;
    const takes = sec.querySelector(".takes");
    cue.takes.forEach((take, i) => {
      const el = document.createElement("div"); el.className = "take"; el.dataset.cue = cue.id; el.dataset.i = i;
      const letter = String.fromCharCode(65 + i);
      el.innerHTML = `<div class="take-top"><span class="name">${letter} · ${escape(take.name)}${i === 0 ? ` <span class="ships">ships</span>` : ""}</span>
          <span class="plays"><button class="play once" id="play-${cue.id}-${i}" aria-label="Play ${escape(cue.id)}, take ${escape(take.name)}, once">▶</button>
          <button class="play burst" id="burst-${cue.id}-${i}" aria-label="Play ${escape(cue.id)}, take ${escape(take.name)}, eight times">▶▶</button></span></div>
        <canvas aria-hidden="true"></canvas><span class="meta">reading…</span>
        <label class="pick"><input type="radio" name="pick-${cue.id}" id="pick-${cue.id}-${i}" ${pickOf(cue) === i ? "checked" : ""}> Pick this one${i === 0 ? " (ships until you pick)" : ""}</label>
        <details><summary>Recipe</summary><pre>${escape(JSON.stringify({ level: cue.level, ...take.recipe }, null, 1))}</pre></details>`;
      el.querySelector(".once").addEventListener("click", () => { const b = decoded[cue.id + i]; if (b) { audio(); play(b, 0, gains.overlay, [cue.id, letter, "overlay"]); } });
      el.querySelector(".burst").addEventListener("click", () => playBurst(cue, i));
      el.querySelector("input[type=radio]").addEventListener("change", () => { picks[cue.id] = { ...(picks[cue.id] || {}), take: i }; markPicked(cue); save(); });
      takes.appendChild(el);
    });
    sec.querySelector(".note").addEventListener("input", (e) => { picks[cue.id] = { ...(picks[cue.id] || {}), note: e.target.value }; save(); });
    root.appendChild(sec);
    markPicked(cue);
  }
}
function markPicked(cue) {
  document.querySelectorAll(`.take[data-cue="${cue.id}"]`).forEach((el) => el.classList.toggle("picked", Number(el.dataset.i) === pickOf(cue)));
}
function readAll() {
  for (const cue of CUES) cue.takes.forEach((take, i) => {
    const buf = readWav(take.wav);
    decoded[cue.id + i] = buf;
    const el = document.querySelector(`.take[data-cue="${cue.id}"][data-i="${i}"]`);
    drawWave(el.querySelector("canvas"), buf);
    const m = measure(buf);
    el.querySelector(".meta").textContent = `${m.ms} ms · peak ${m.peakDb.toFixed(1)} dB · avg ${m.rmsDb.toFixed(0)} dB`;
  });
  for (const [id, uri] of Object.entries(REFS)) refs[id] = readWav(uri);
  document.body.dataset.ready = "true";
}
function writePicks() {
  const lines = ["Click sound picks (v3):"];
  for (const cue of CUES) {
    const note = (picks[cue.id]?.note || "").trim();
    const i = pickOf(cue);
    lines.push(`${cue.id}: ${String.fromCharCode(65 + i)} (${cue.takes[i].name})${picks[cue.id]?.take === undefined ? " [not picked, A ships]" : ""}${note ? " - " + note : ""}`);
  }
  document.getElementById("picksText").value = lines.join("\n");
}

// ---- The bar ------------------------------------------------------------------------
function drawSteps() {
  for (const w of ["overlay", "games"]) {
    const el = document.getElementById(`steps-${w}`); el.innerHTML = "";
    STEPS_DB.forEach((db, i) => {
      const b = document.createElement("button"); b.textContent = `${db === 0 ? "0" : "−" + Math.abs(db)} dB`;
      b.className = !muted && i + 1 === steps[w] ? "lit" : ""; b.setAttribute("aria-pressed", String(i + 1 === steps[w]));
      b.setAttribute("aria-label", `${w === "overlay" ? "Overlay" : "Games window"} volume, step ${i + 1} of 5`);
      b.addEventListener("click", () => { steps[w] = i + 1; muted = false; setVolume(); drawSteps(); });
      el.appendChild(b);
    });
  }
  const m = document.getElementById("mute"); m.classList.toggle("on", muted); m.setAttribute("aria-pressed", String(muted)); m.textContent = muted ? "Muted" : "Mute";
}
let current = null; // the demo or burst playing: its bus and its sources
function stopCurrent() {
  if (!current) return;
  const ctx = audio(), now = ctx.currentTime;
  current.bus.gain.setValueAtTime(current.bus.gain.value, now); current.bus.gain.linearRampToValueAtTime(0, now + 0.005);
  for (const s of current.sources) try { s.stop(now + 0.006); } catch {}
  current = null;
}
function demoLine() { const d = DEMOS[document.getElementById("demo").selectedIndex]; document.getElementById("demoStatus").textContent = d.line; }
// Plays a list of [seconds, buffer, what] through a fresh bus into a window's gain;
// clicks pass the 80 ms gap first (A99: within MIN_GAP of the last click PLAYED, dropped).
function playList(items, w) {
  const ctx = audio(); stopCurrent();
  const bus = ctx.createGain(); bus.connect(gains[w]);
  const sources = [];
  let lastClick = -Infinity, clicks = 0, dropped = 0, end = 0;
  for (const it of items) {
    if (it.click) {
      clicks++;
      if (it.t - lastClick < MIN_GAP - 1e-9) { dropped++; continue; }
      lastClick = it.t;
    }
    sources.push(play(it.buf, it.t, bus, it.what));
    end = Math.max(end, it.t + it.buf.length / RATE);
  }
  const mine = current = { bus, sources };
  return { clicks, dropped, end, done: (fn) => setTimeout(() => { if (current === mine) { current = null; fn(); } }, (end + 0.5) * 1000) };
}
function playBurst(cue, i) {
  const buf = decoded[cue.id + i]; if (!buf) return;
  const letter = String.fromCharCode(65 + i);
  const items = Array.from({ length: BURST.count }, (_, k) => ({ t: k * BURST.every, buf, click: true, what: [cue.id, letter, "overlay"] }));
  const r = playList(items, "overlay");
  const st = document.getElementById("demoStatus");
  st.textContent = `Playing ${cue.id}, take ${letter} (${cue.takes[i].name}), ${BURST.count} times ${BURST.every} s apart at the Overlay volume.`;
  r.done(demoLine);
}
function playDemo() {
  const d = DEMOS[document.getElementById("demo").selectedIndex];
  const items = d.events.map(([t, kind, id]) => {
    if (kind === "ref") return { t, buf: refs[id], click: false, what: [`hatchling ${id}`, "shipped", d.window] };
    const cue = CUES.find((c) => c.id === id), i = pickOf(cue);
    return { t, buf: decoded[id + i], click: true, what: [id, String.fromCharCode(65 + i), d.window] };
  });
  const r = playList(items, d.window);
  const heard = r.clicks - r.dropped;
  const st = document.getElementById("demoStatus");
  st.textContent = `Playing at the ${d.window === "overlay" ? "Overlay" : "Games window"} volume (${r.end.toFixed(1)} s, ${heard} of ${r.clicks} clicks heard`
    + `${r.dropped ? `, ${r.dropped} dropped by the 80 ms gap` : ""}): ${d.line}`;
  r.done(demoLine);
}

const select = document.getElementById("demo");
for (const d of DEMOS) { const o = document.createElement("option"); o.textContent = d.title; select.appendChild(o); }
select.addEventListener("change", demoLine);
document.getElementById("play-demo").addEventListener("click", () => { if (Object.keys(decoded).length) playDemo(); });
document.getElementById("mute").addEventListener("click", () => { muted = !muted; audio(); setVolume(); drawSteps(); if (muted) stopCurrent(); });
document.getElementById("copy").addEventListener("click", async () => {
  const text = document.getElementById("picksText").value, st = document.getElementById("copyStatus");
  try { await navigator.clipboard.writeText(text); st.textContent = "Copied. Paste it to Claude."; }
  catch { const ta = document.getElementById("picksText"); ta.focus(); ta.select(); st.textContent = "Selected: press Ctrl+C to copy."; }
});
drawSteps(); demoLine(); build(); writePicks(); readAll();
}
/* eslint-enable */

// The page's data: the cues (each take its recipe and its WAV as a data: URI, the
// cue's length its take A's), Hatchling's two shipped sounds, the demos, the five
// steps (A82's: -24 to 0 dB, 6 dB a step, the third the default, for both windows),
// the gap and the burst (eight at 0.35 s).
function writeBoard(file, { cues, takes, refs, read }) {
  const uri = (f) => { read(f); /* the board embeds only a file the format allows */ return "data:audio/wav;base64," + fs.readFileSync(f).toString("base64"); };
  const data = {
    CUES: cues.map((cue) => ({
      id: cue.id, when: cue.when, often: cue.often, level: cue.level,
      ms: Math.round((read(takes[cue.id][0].file).length * 1000) / 44100),
      takes: cue.takes.map((recipe, i) => ({ name: recipe.name, recipe, wav: uri(takes[cue.id][i].file) })),
    })),
    REFS: Object.fromEntries(Object.entries(refs).map(([id, f]) => [id, uri(f)])),
    DEMOS: demos(),
    STEPS_DB: [-24, -18, -12, -6, 0],
    DEFAULT_STEP: 3,
    MIN_GAP,
    BURST: { count: 8, every: 0.35 },
  };
  const json = JSON.stringify(data).replace(/</g, "\\u003c");
  const html = `${HEAD}\n\n${BODY}\n\n<script>\nconst DATA = ${json};\n(${page.toString()})(DATA);\n</script>\n`;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  if (!fs.existsSync(file) || fs.readFileSync(file, "utf8") !== html) fs.writeFileSync(file, html);
  return file;
}

module.exports = { writeBoard, demos, MIN_GAP };
