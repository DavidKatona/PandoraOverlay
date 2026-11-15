// The second sound board (PLAN-SOUND section 6): one HTML page the owner listens
// on, written by `node tools/games-sound/hatchling/build.js --board` into
// tools/games-sound/out/sound-board-v2.html (git-ignored) and copied by hand into
// the games folder; the orchestrator publishes it.
//
// It is the first board's page (reference\sound-board-v1.html): its CSS tokens and
// dark mode exactly as they are, its fonts as it loads them (Google Fonts), its
// layout, its way of picking and noting (kept in the browser under a key of its
// own), and no script from anywhere. What it plays is what SHIPS: every take is
// the WAV the build wrote, embedded as a data: URI and decoded by the page's own
// PCM reader into an AudioBuffer at 44,100 Hz (never decodeAudioData, which may
// resample). For the owner's seven picks it also renders the recipe with the first
// board's own renderTake(), copied verbatim, and shows how close the two are:
// the correlation at lag 0 and the largest difference of their 5 ms RMS envelopes
// where the browser's is above -40 dBFS, against R2's bar (0.95 or more; 1 dB or
// less). The bar is never loosened to pass (R2).
"use strict";
const fs = require("fs");

// ---- The demo turns ------------------------------------------------------------------
//
// Copied by hand from Games/PandoraOverlay.Games.Hatchling/Turns/Beat.cs (BeatTicks,
// H93) and Views/BeatStep.cs (WindUpTicks), with each cue at PLAN-SOUND 3.2's tick of
// its beat (H96). A later retune of the pace does not move them: the board is a
// review page, not a test (PLAN-SOUND risk 10). Engine ticks, 30 a second at Normal;
// Fast halves every tick (speed 2).
const STRETCH = 2;
const BeatTicks = {
  WalkPerCell: 5 * STRETCH, SneakPerCell: 6 * STRETCH, SprintPerCell: 3 * STRETCH,
  Eat: 8 * STRETCH, Drink: 8 * STRETCH, Rest: 8 * STRETCH,
  Strike: 9 * STRETCH, StrikeDefeatFade: 6 * STRETCH, AlsoStruck: 6 * STRETCH,
  Gap: 15, Focus: 15, Notices: 12, StepPerCell: 4 * STRETCH,
  Waits: 2 * STRETCH, Stuck: 5 * STRETCH, Bite: 10 * STRETCH, Dodge: 8 * STRETCH, Hurt: 6 * STRETCH,
  SecondWind: 12 * STRETCH, SmallGame: 5 * STRETCH, Arrives: 6 * STRETCH,
  UnseenQuiet: 3 * STRETCH, UnseenHeard: 8 * STRETCH, TimePasses: 4 * STRETCH, TimeHurts: 8 * STRETCH,
  Stage: 20 * STRETCH, Apex: 16 * STRETCH, End: 12 * STRETCH,
};
const WIND_UP = 3 * STRETCH; // BeatStep.WindUpTicks: the blow of a strike or a bite

// A beat: its ticks, and its (tick, cue) pairs (PLAN-SOUND 3.2).
const beat = (ticks, cues = []) => ({ ticks, cues });
const B = {
  walk: (cells, pace = "walk") => {
    const per = { walk: BeatTicks.WalkPerCell, sneak: BeatTicks.SneakPerCell, sprint: BeatTicks.SprintPerCell }[pace];
    const cue = { walk: "step", sneak: "step-sneak", sprint: "step-sprint" }[pace];
    return beat(cells * per, Array.from({ length: cells }, (_, c) => [c * per, cue]));
  },
  eat: () => beat(BeatTicks.Eat, [[0, "eat"]]),
  strike: (defeats) => defeats
    ? beat(BeatTicks.Strike + BeatTicks.StrikeDefeatFade, [[WIND_UP, "hit"], [BeatTicks.Strike, "defeat"]])
    : beat(BeatTicks.Strike, [[WIND_UP, "hit"]]),
  gap: () => beat(BeatTicks.Gap),
  focus: (apex = false) => beat(BeatTicks.Focus, [[0, apex ? "focus-apex" : "focus"]]),
  notices: (cue) => beat(BeatTicks.Notices, [[0, cue]]),
  steps: (cells) => beat(cells * BeatTicks.StepPerCell, Array.from({ length: cells }, (_, c) => [c * BeatTicks.StepPerCell, "hunter-step"])),
  bite: () => beat(BeatTicks.Bite, [[WIND_UP, "bite"]]),
  dodge: () => beat(BeatTicks.Dodge, [[WIND_UP, "dodge"]]),
  hurt: () => beat(BeatTicks.Hurt, [[0, "sting"]]),
  secondWind: () => beat(BeatTicks.SecondWind, [[0, "second-wind"]]),
  unseen: (heard = false) => heard ? beat(BeatTicks.UnseenHeard, [[0, "heard"]]) : beat(BeatTicks.UnseenQuiet),
  time: () => beat(BeatTicks.TimePasses),
  timeHurts: () => beat(BeatTicks.TimeHurts, [[0, "starve"]]),
  stage: () => beat(BeatTicks.Stage, [[0, "stage"]]),
  apex: () => beat(BeatTicks.Apex, [[0, "apex"]]),
  end: (won) => beat(BeatTicks.End, [[0, won ? "win" : "lost"]]),
};

// A turn: its beats laid end to end, with a cue after the last when the page that
// follows the playback has one (a won run's summary with its achievement, H99).
function turn(beats, after) {
  const cues = [];
  let at = 0;
  for (const b of beats) {
    for (const [tick, cue] of b.cues) cues.push([at + tick, cue]);
    at += b.ticks;
  }
  if (after) cues.push([at, after]);
  return { ticks: at, cues };
}

// A hunter that spots you, steps twice and bites: the two-hunter turn's unit.
const hunts = (apex = false) => [B.focus(apex), B.notices("spotted"), B.steps(2), B.bite()];
const twoHunters = () => [B.walk(1), B.gap(), ...hunts(), B.gap(), ...hunts(), B.gap(), B.unseen(), B.time()];

function demos() {
  const worst = [B.walk(1), B.gap()];
  for (let i = 0; i < 8; i++) {
    if (i > 0) worst.push(B.gap());
    worst.push(...hunts(i === 0), B.hurt(), B.hurt());
  }
  worst.push(B.gap(), B.unseen(), B.time());

  const list = [
    { id: "quiet", title: "A quiet turn",
      line: "You walk one cell; a gap, nothing heard out of sight, time passes. One soft step.",
      turns: [turn([B.walk(1), B.gap(), B.unseen(), B.time()])] },
    { id: "two-hunters", title: "Two hunters",
      line: "You walk one cell; two hunters each spot you, step twice and bite.",
      turns: [turn(twoHunters())] },
    { id: "sneak", title: "A sneak past a hunter",
      line: "You sneak two cells; a hunter hears you and searches two cells.",
      turns: [turn([B.walk(2, "sneak"), B.gap(), B.focus(), B.notices("heard"), B.steps(2), B.gap(), B.unseen(), B.time()])] },
    { id: "strike", title: "A strike, then a dodge",
      line: "Your strike defeats a hunter; another steps in and bites, and you dodge.",
      turns: [turn([B.strike(true), B.gap(), B.focus(), B.notices("spotted"), B.steps(1), B.dodge(), B.gap(), B.unseen(), B.time()])] },
    { id: "meal", title: "A meal, then starving",
      line: "You eat; on a later turn you walk and hunger takes health.",
      turns: [turn([B.eat(), B.gap(), B.unseen(), B.time()]), turn([B.walk(1), B.gap(), B.unseen(), B.time(), B.timeHurts()])] },
    { id: "elder", title: "Elder, and the apex",
      line: "You walk and grow into Elder; the apex has your scent; on its first turn it spots you and steps twice.",
      turns: [turn([B.walk(1), B.gap(), B.unseen(), B.time(), B.stage(), B.apex()]),
        turn([B.walk(1), B.gap(), B.focus(true), B.notices("spotted"), B.steps(2), B.gap(), B.unseen(), B.time()])] },
    { id: "second-wind", title: "Second wind",
      line: "A hunter spots you, steps and bites, and the bite gives you your second wind.",
      turns: [turn([B.walk(1), B.gap(), B.focus(), B.notices("spotted"), B.steps(1), B.bite(), B.secondWind(), B.gap(), B.unseen(), B.time()])] },
    { id: "won", title: "A run won",
      line: "You walk out the last turn and win; the summary has an achievement in it.",
      turns: [turn([B.walk(1), B.gap(), B.unseen(), B.time(), B.end(true)], "reward")] },
    { id: "lost", title: "A run lost",
      line: "A hunter spots you, steps twice and bites, and the run is lost.",
      turns: [turn([B.walk(1), B.gap(), ...hunts(), B.gap(), B.end(false)])] },
    { id: "worst", title: "The worst turn",
      line: "Danger 5: you walk; the apex and seven hunters each spot you, step twice, bite, and are hurt by your spikes and by bleeding.",
      turns: [turn(worst)] },
    { id: "skip", title: "A skip mid-turn",
      line: "The two-hunter turn, skipped as the second hunter begins; the run then ends: the sounds stop, and one 'lost' plays (H98).",
      turns: [turn([...twoHunters(), B.end(false)])], skip: { turn: 0, tick: 110, cue: "lost" } },
  ];
  // H93's figures, held here so a slip in the copy above shows at once.
  if (list[0].turns[0].ticks !== 39) throw new Error(`a quiet turn is ${list[0].turns[0].ticks} ticks, Beat.cs says 39`);
  if (list[1].turns[0].ticks !== 195) throw new Error(`the two-hunter turn is ${list[1].turns[0].ticks} ticks, Beat.cs says 195`);
  if (list[1].turns[0].cues.length !== 11) throw new Error("the two-hunter turn is 11 cues (H97)");
  return list;
}

// ---- The page ------------------------------------------------------------------------

// The first board's <head> and CSS, exactly as they are (its tokens, its dark mode,
// its fonts); then, in a second block, the few rules the new parts need, in its tokens.
const HEAD = `<meta charset="utf-8">
<title>Hatchling Sound Board</title>
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
/* The second board's own parts, in the first board's tokens: the groups' headings, the window's volume steps, the demo picker, the picks' match figures. */
.group { font-family: var(--display); font-weight: 700; font-size: 24px; margin: 12px 0 0; color: var(--amber); }
.steps { display: flex; gap: 4px; }
.steps button, .mute { padding: 5px 9px; font-size: 13px; font-variant-numeric: tabular-nums; }
.steps button.lit { background: var(--amber-soft); border-color: var(--amber); }
.mute.on { background: var(--amber); border-color: var(--amber); color: #1a1206; }
.bar select { font: inherit; font-size: 14px; color: var(--fg); background: var(--surface); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; max-width: 100%; }
.bar .line { flex-basis: 100%; }
.yours { font-size: 12px; font-weight: 600; color: var(--amber); }
.figures { font-family: var(--mono); font-size: 12px; font-variant-numeric: tabular-nums; }
.figures .miss { color: var(--amber); font-weight: 600; }
.from { font-size: 13px; color: var(--muted); font-style: italic; flex-basis: 100%; }
.why { font-size: 12px; color: var(--muted); }
</style>`;

const BODY = `<div class="wrap">
  <header>
    <h1>Hatchling <span class="amber">Sound Board</span> 2</h1>
    <p class="lede">All 29 of Hatchling's sounds, as the game will ship them: every one made by code from the recipe under it, and every ▶ plays the very file that ships. The seven you picked on the first board are here as they were, each with a second ▶ that renders it the way the first board did and two figures for how close they are. The 22 new ones have three takes each: A ships until you pick another. The volume is the Games window's own, five steps with the third the default. Pick, add a note where one is off, play the turns, then copy your picks at the bottom and paste them to Claude.</p>
  </header>

  <div class="bar">
    <label>Volume <span class="steps" id="steps"></span></label>
    <button class="mute" id="mute" aria-pressed="false">Mute</button>
    <label for="demo">Turn <select id="demo"></select></label>
    <button class="primary" id="turn">▶ Play the turn</button>
    <label><input type="checkbox" id="fast"> Fast</label>
    <span class="status line" id="turnStatus"></span>
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
const { GROUPS, CUES, DEMOS, STEPS_DB, DEFAULT_STEP } = DATA;

// ---- The first board's synthesis, copied verbatim (for "as the first board played it") ----
const RATE = 44100;
function noiseBuffer(ctx, seconds) {
  const b = ctx.createBuffer(1, Math.ceil(seconds * RATE), RATE);
  const d = b.getChannelData(0);
  let s = 12345; // seeded, so a take is the same every time
  for (let i = 0; i < d.length; i++) { s = (s * 1103515245 + 12345) >>> 0; d[i] = (s / 4294967296) * 2 - 1; }
  return b;
}
function envelope(ctx, layer) {
  const g = ctx.createGain();
  const t0 = layer.t, a = layer.attack, end = layer.t + layer.dur;
  g.gain.setValueAtTime(0, t0);
  g.gain.linearRampToValueAtTime(layer.gain, t0 + a);
  g.gain.exponentialRampToValueAtTime(0.0001, end);
  g.gain.setValueAtTime(0, end + 0.001);
  return g;
}
function glide(param, layer, f0, f1) {
  param.setValueAtTime(f0, layer.t);
  if (f0 !== f1) {
    if (layer.glide === "linear") param.linearRampToValueAtTime(f1, layer.t + layer.dur);
    else param.exponentialRampToValueAtTime(f1, layer.t + layer.dur);
  }
}
async function renderTake(take, level) {
  const ctx = new OfflineAudioContext(1, Math.ceil(take.dur * RATE), RATE);
  let out = ctx.destination;
  if (take.tone) { const lp = ctx.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = take.tone; lp.connect(ctx.destination); out = lp; }
  for (const L of take.layers) {
    const env = envelope(ctx, L);
    env.connect(out);
    if (L.kind === "osc") {
      const o = ctx.createOscillator(); o.type = L.wave;
      glide(o.frequency, L, L.f0, L.f1);
      o.connect(env); o.start(L.t); o.stop(L.t + L.dur + 0.01);
    } else {
      const src = ctx.createBufferSource(); src.buffer = noiseBuffer(ctx, L.dur + 0.02);
      const f = ctx.createBiquadFilter(); f.type = L.filter; f.Q.value = L.q;
      glide(f.frequency, L, L.f0, L.f1);
      src.connect(f); f.connect(env); src.start(L.t); src.stop(L.t + L.dur + 0.01);
    }
  }
  const buf = await ctx.startRendering();
  const d = buf.getChannelData(0);
  let peak = 0; for (let i = 0; i < d.length; i++) peak = Math.max(peak, Math.abs(d[i]));
  const target = Math.pow(10, level / 20);
  const k = peak > 0 ? target / peak : 1;
  let sum = 0; for (let i = 0; i < d.length; i++) { d[i] *= k; sum += d[i] * d[i]; }
  const rms = Math.sqrt(sum / d.length);
  return { buf, peakDb: level, rmsDb: 20 * Math.log10(rms || 1e-9), ms: Math.round(take.dur * 1000) };
}
// R6: the same render with ONE line added, each oscillator's frequency set to its own
// f0 before its automation. A diagnostic only, never played and never the bar's
// figures: it shows whether a pick's miss is the browser's oscillator quirk alone.
async function renderTakeFrequencySet(take, level) {
  const ctx = new OfflineAudioContext(1, Math.ceil(take.dur * RATE), RATE);
  let out = ctx.destination;
  if (take.tone) { const lp = ctx.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = take.tone; lp.connect(ctx.destination); out = lp; }
  for (const L of take.layers) {
    const env = envelope(ctx, L);
    env.connect(out);
    if (L.kind === "osc") {
      const o = ctx.createOscillator(); o.type = L.wave;
      o.frequency.value = L.f0;
      glide(o.frequency, L, L.f0, L.f1);
      o.connect(env); o.start(L.t); o.stop(L.t + L.dur + 0.01);
    } else {
      const src = ctx.createBufferSource(); src.buffer = noiseBuffer(ctx, L.dur + 0.02);
      const f = ctx.createBiquadFilter(); f.type = L.filter; f.Q.value = L.q;
      glide(f.frequency, L, L.f0, L.f1);
      src.connect(f); f.connect(env); src.start(L.t); src.stop(L.t + L.dur + 0.01);
    }
  }
  const buf = await ctx.startRendering();
  const d = buf.getChannelData(0);
  let peak = 0; for (let i = 0; i < d.length; i++) peak = Math.max(peak, Math.abs(d[i]));
  const target = Math.pow(10, level / 20);
  const k = peak > 0 ? target / peak : 1;
  let sum = 0; for (let i = 0; i < d.length; i++) { d[i] *= k; sum += d[i] * d[i]; }
  const rms = Math.sqrt(sum / d.length);
  return { buf, peakDb: level, rmsDb: 20 * Math.log10(rms || 1e-9), ms: Math.round(take.dur * 1000) };
}

// ---- The page's own PCM reader: the shipped file, exactly ---------------------------
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

// ---- R2's two figures -------------------------------------------------------------
// The correlation of the two at lag 0 (sum ab / sqrt(sum a^2 sum b^2)), and the
// largest difference in dB of their 5 ms RMS envelopes (windows of 220 samples)
// where the browser's is above -40 dBFS. The bar: 0.95 or more; 1 dB or less.
const BAR_CORRELATION = 0.95, BAR_ENVELOPE_DB = 1, ENVELOPE_FLOOR_DB = -40, WINDOW = 220;
function figures(shipped, browser) {
  const a = shipped.getChannelData(0), b = browser.getChannelData(0), n = Math.min(a.length, b.length);
  let ab = 0, aa = 0, bb = 0;
  for (let i = 0; i < n; i++) { ab += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
  const correlation = ab / Math.sqrt(aa * bb);
  let envelope = 0, worstAt = 0;
  for (let w = 0; w + WINDOW <= n; w += WINDOW) {
    let sa = 0, sb = 0;
    for (let i = w; i < w + WINDOW; i++) { sa += a[i] * a[i]; sb += b[i] * b[i]; }
    const da = 10 * Math.log10(sa / WINDOW || 1e-30), db = 10 * Math.log10(sb / WINDOW || 1e-30);
    if (db > ENVELOPE_FLOOR_DB && Math.abs(da - db) > envelope) { envelope = Math.abs(da - db); worstAt = w; }
  }
  return { correlation, envelope, worstMs: Math.round(worstAt * 1000 / RATE), lengths: [a.length, b.length] };
}

// ---- Playback: the Games window's volume (A82) ----------------------------------------
let live = null, master = null;
let step = DEFAULT_STEP, muted = false;
function audio() {
  if (!live) { live = new AudioContext(); master = live.createGain(); master.connect(live.destination); setVolume(); }
  if (live.state === "suspended") live.resume();
  return live;
}
function setVolume() { if (master) master.gain.value = muted ? 0 : Math.pow(10, STEPS_DB[step - 1] / 20); }
function play(buf, when = 0, to) {
  const ctx = audio();
  const s = ctx.createBufferSource(); s.buffer = buf; s.connect(to || master); s.start(ctx.currentTime + 0.02 + when);
  return s;
}

// ---- Picks (kept in this browser only, under the second board's own key) ----------
const KEY = "hatchling-sound-board-v2-picks";
let picks = {};
try { picks = JSON.parse(localStorage.getItem(KEY) || "{}") || {}; } catch { picks = {}; }
function save() { try { localStorage.setItem(KEY, JSON.stringify(picks)); } catch {} writePicks(); }
function pickOf(cue) { return cue.pick ? 0 : (picks[cue.id]?.take ?? 0); }

// ---- The page ------------------------------------------------------------------------
const decoded = {};   // cue id + take index -> AudioBuffer of the shipped (or B, C) file
const escape = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
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
  for (const group of GROUPS) {
    const h = document.createElement("h2"); h.className = "group"; h.textContent = group.name; root.appendChild(h);
    for (const cue of CUES.filter((c) => c.group === group.id)) {
      const sec = document.createElement("section"); sec.className = "cue";
      sec.innerHTML = `<div class="cue-head"><h2>${escape(cue.id)}</h2><span class="when">${escape(cue.when)} ${escape(cue.often)}. Level ${cue.level} dBFS.</span>
        ${cue.from ? `<span class="from">${escape(cue.from)}</span>` : ""}</div><div class="takes"></div>
        <input class="note" id="note-${cue.id}" placeholder="A note for this one (optional): too sharp, shorter, lower…" value="${escape(picks[cue.id]?.note ?? "")}">`;
      const takes = sec.querySelector(".takes");
      cue.takes.forEach((take, i) => {
        const el = document.createElement("div"); el.className = "take"; el.dataset.cue = cue.id; el.dataset.i = i;
        const letter = String.fromCharCode(65 + i);
        el.innerHTML = `<div class="take-top"><span class="name">${letter} · ${escape(take.name)}</span>
            <button class="play" id="play-${cue.id}-${i}" aria-label="Play ${escape(cue.id)}, take ${escape(take.name)}">▶</button></div>
          <canvas aria-hidden="true"></canvas><span class="meta">reading…</span>
          ${cue.pick
            ? `<span class="yours">★ your pick</span>
               <button class="play" id="v1-${cue.id}" aria-label="Play ${escape(cue.id)} as the first board played it">▶ as the first board played it</button>
               <span class="figures" id="fig-${cue.id}">rendering the first board's way…</span>`
            : `<label class="pick"><input type="radio" name="pick-${cue.id}" id="pick-${cue.id}-${i}" ${pickOf(cue) === i ? "checked" : ""}> Pick this one${i === 0 ? " (ships until you pick)" : ""}</label>`}
          <details><summary>Recipe</summary><pre>${escape(JSON.stringify({ level: cue.level, ...take.recipe }, null, 1))}</pre></details>`;
        el.querySelector(".play").addEventListener("click", () => { const b = decoded[cue.id + i]; if (b) play(b); });
        if (cue.pick) el.querySelector(`#v1-${cue.id}`).addEventListener("click", () => { const r = firstBoard[cue.id]; if (r) play(r.buf); });
        else el.querySelector("input[type=radio]").addEventListener("change", () => { picks[cue.id] = { ...(picks[cue.id] || {}), take: i }; markPicked(cue); save(); });
        takes.appendChild(el);
      });
      sec.querySelector(".note").addEventListener("input", (e) => { picks[cue.id] = { ...(picks[cue.id] || {}), note: e.target.value }; save(); });
      root.appendChild(sec);
      markPicked(cue);
    }
  }
}
function markPicked(cue) {
  document.querySelectorAll(`.take[data-cue="${cue.id}"]`).forEach((el) => el.classList.toggle("picked", Number(el.dataset.i) === pickOf(cue)));
}
const firstBoard = {};
async function renderAll() {
  for (const cue of CUES) cue.takes.forEach((take, i) => {
    const buf = readWav(take.wav);
    decoded[cue.id + i] = buf;
    const el = document.querySelector(`.take[data-cue="${cue.id}"][data-i="${i}"]`);
    drawWave(el.querySelector("canvas"), buf);
    const m = measure(buf);
    el.querySelector(".meta").textContent = `${m.ms} ms · peak ${m.peakDb.toFixed(1)} dB · avg ${m.rmsDb.toFixed(0)} dB`;
  });
  const results = {};
  for (const cue of CUES.filter((c) => c.pick)) {
    const r = await renderTake(cue.takes[0].recipe, cue.level);
    firstBoard[cue.id] = r;
    const f = figures(decoded[cue.id + 0], r.buf);
    results[cue.id] = f;
    const ok = (good, text) => good ? text : `<span class="miss">${text} UNDER THE BAR</span>`;
    const meets = (x) => x.correlation >= BAR_CORRELATION && x.envelope <= BAR_ENVELOPE_DB;
    let why = "";
    if (!meets(f)) {
      // R6: a miss is explained only by a measurement in this browser, never by a claim.
      const corrected = figures(decoded[cue.id + 0], (await renderTakeFrequencySet(cue.takes[0].recipe, cue.level)).buf);
      f.corrected = corrected;
      if (meets(corrected))
        why = `<br><span class="why">Why: your browser starts some of these notes at 440 Hz for up to 1.5 ms before their own pitch, a quirk of its oscillator, so their waves line up a little differently with the file's. The notes and their loudness are the same. With that one setting corrected, the same recipe matches the file: correlation ${corrected.correlation.toFixed(3)}, envelope ${corrected.envelope.toFixed(2)} dB.</span>`;
    }
    document.getElementById(`fig-${cue.id}`).innerHTML =
      `${ok(f.correlation >= BAR_CORRELATION, `correlation ${f.correlation.toFixed(4)} (bar ${BAR_CORRELATION})`)}<br>`
      + `${ok(f.envelope <= BAR_ENVELOPE_DB, `envelope ${f.envelope.toFixed(2)} dB at ${f.worstMs} ms (bar ${BAR_ENVELOPE_DB} dB)`)}`
      + why;
  }
  window.matchFigures = results;
  console.log("match figures " + JSON.stringify(results));
  document.body.dataset.ready = "true";
}
function writePicks() {
  const lines = ["Hatchling sound picks (v2):"];
  for (const cue of CUES) {
    const note = (picks[cue.id]?.note || "").trim();
    if (cue.pick) { if (note) lines.push(`${cue.id} (your pick): ${note}`); continue; }
    const i = pickOf(cue);
    lines.push(`${cue.id}: ${String.fromCharCode(65 + i)} (${cue.takes[i].name})${picks[cue.id]?.take === undefined ? " [not picked, A ships]" : ""}${note ? " - " + note : ""}`);
  }
  document.getElementById("picksText").value = lines.join("\n");
}

// ---- The bar ------------------------------------------------------------------------
function drawSteps() {
  const el = document.getElementById("steps"); el.innerHTML = "";
  STEPS_DB.forEach((db, i) => {
    const b = document.createElement("button"); b.textContent = `${db === 0 ? "0" : "−" + Math.abs(db)} dB`;
    b.className = !muted && i + 1 === step ? "lit" : ""; b.setAttribute("aria-pressed", String(i + 1 === step));
    b.addEventListener("click", () => { step = i + 1; muted = false; setVolume(); drawSteps(); });
    el.appendChild(b);
  });
  const m = document.getElementById("mute"); m.classList.toggle("on", muted); m.setAttribute("aria-pressed", String(muted)); m.textContent = muted ? "Muted" : "Mute";
}
let current = null; // the demo playing: its bus and its sources
function stopDemo() {
  if (!current) return;
  const ctx = audio(), now = ctx.currentTime;
  current.bus.gain.setValueAtTime(current.bus.gain.value, now); current.bus.gain.linearRampToValueAtTime(0, now + 0.005);
  for (const s of current.sources) try { s.stop(now + 0.006); } catch {}
  current = null;
}
function demoLine() { const d = DEMOS[document.getElementById("demo").selectedIndex]; document.getElementById("turnStatus").textContent = d.line; }
function playDemo() {
  const d = DEMOS[document.getElementById("demo").selectedIndex];
  const speed = document.getElementById("fast").checked ? 2 : 1, PAUSE = 0.8; // between turns: the player's next click, not a tick
  const ctx = audio(); stopDemo();
  const bus = ctx.createGain(); bus.connect(master);
  const sources = [];
  const bufOf = (id) => { const cue = CUES.find((c) => c.id === id); return decoded[id + pickOf(cue)]; };
  let start = 0, end = 0, count = 0, skipAt = null;
  d.turns.forEach((t, n) => {
    for (const [tick, id] of t.cues) {
      if (d.skip && (n > d.skip.turn || (n === d.skip.turn && tick >= d.skip.tick))) continue;
      sources.push(play(bufOf(id), start + tick / 30 / speed, bus)); count++;
    }
    if (d.skip && n === d.skip.turn) skipAt = start + d.skip.tick / 30 / speed;
    end = start + t.ticks / 30 / speed;
    start = end + PAUSE;
  });
  if (skipAt !== null) {
    // The skip (H98): what plays fades over 5 ms, the rest never starts, then one cue.
    const at = ctx.currentTime + 0.02 + skipAt;
    bus.gain.setValueAtTime(1, at); bus.gain.linearRampToValueAtTime(0, at + 0.005);
    for (const s of sources) s.stop(at + 0.006);
    play(bufOf(d.skip.cue), skipAt + 0.01);
    count++; end = skipAt + 0.7;
  }
  current = { bus, sources };
  const st = document.getElementById("turnStatus");
  st.textContent = `Playing (${speed === 2 ? "Fast" : "Normal"}, ${end.toFixed(1)} s, ${count} sounds): ${d.line}`;
  const mine = current;
  setTimeout(() => { if (current === mine) { current = null; demoLine(); } }, (end + 0.8) * 1000);
}

const select = document.getElementById("demo");
for (const d of DEMOS) { const o = document.createElement("option"); o.textContent = d.title; select.appendChild(o); }
select.addEventListener("change", demoLine);
document.getElementById("turn").addEventListener("click", () => { if (Object.keys(decoded).length) playDemo(); });
document.getElementById("mute").addEventListener("click", () => { muted = !muted; setVolume(); drawSteps(); if (muted) stopDemo(); });
document.getElementById("copy").addEventListener("click", async () => {
  const text = document.getElementById("picksText").value, st = document.getElementById("copyStatus");
  try { await navigator.clipboard.writeText(text); st.textContent = "Copied. Paste it to Claude."; }
  catch { const ta = document.getElementById("picksText"); ta.focus(); ta.select(); st.textContent = "Selected: press Ctrl+C to copy."; }
});
drawSteps(); demoLine(); build(); writePicks(); renderAll();
}
/* eslint-enable */

// The page's data: groups, cues (each take its recipe and its WAV as a data: URI),
// the demos, and the window's volume steps (A82: -24 to 0 dB, the third default).
function writeBoard(file, { groups, cues, takes, read }) {
  const data = {
    GROUPS: groups.map(({ id, name }) => ({ id, name })),
    CUES: cues.map((cue) => ({
      id: cue.id, group: cue.group, pick: !!cue.pick, from: cue.from || null, when: cue.when, often: cue.often, level: cue.level,
      takes: cue.takes.map((recipe, i) => {
        const shipped = takes[cue.id][i].file;
        read(shipped); // the board embeds only a file the format allows
        return { name: recipe.name, recipe, wav: "data:audio/wav;base64," + fs.readFileSync(shipped).toString("base64") };
      }),
    })),
    DEMOS: demos(),
    STEPS_DB: [-24, -18, -12, -6, 0],
    DEFAULT_STEP: 3,
  };
  const json = JSON.stringify(data).replace(/</g, "\\u003c");
  const html = `${HEAD}\n\n${BODY}\n\n<script>\nconst DATA = ${json};\n(${page.toString()})(DATA);\n</script>\n`;
  fs.mkdirSync(require("path").dirname(file), { recursive: true });
  if (!fs.existsSync(file) || fs.readFileSync(file, "utf8") !== html) fs.writeFileSync(file, html);
  return file;
}

module.exports = { writeBoard, demos, BeatTicks };
