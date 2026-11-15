// The click sounds (G27): six cues, one family for the overlay and the games
// (A100), in PLAN-CLICKS.md section 3.2's order. Each its name (a file name,
// Assets/Clicks/<name>.wav, and a value of the app's ClickCue and the Contract's
// GameClick, PLAN-CLICKS 3.3), where it plays, how often, its level (dBFS of the
// take's peak, the same for every take of one cue) and its three takes in the
// first sound board's recipe format (synth.js), A first: take A is what ships.
//
// Where the rules come from:
// - A99 ("do not make these annoying", as rules): short, quiet, soft, never
//   doubled, only what the player clicked.
// - R8 (the clicks' own bounds, apart from R3's and R4's, which stay the games'
//   cues'): a take lasts 0.015 to 0.08 s, never longer than ClickSound.MinGap;
//   attacks of 1 ms or more (1 to 3 ms here) and the envelope's own exponential
//   decay; the silent first sample and last 2 ms (A87); no sample at full scale;
//   peaks by role, `arm` -16 (meant to be noticed), `press` and `toggle-on` -18,
//   `select` and `toggle-off` -20 (`select` is the commonest), `back` -21 (a
//   dismissal, the softest); triangle, sine and filtered noise only, never a
//   square (a click is never a warning or a fanfare); one or two notes; noise
//   bands between about 1.5 and 3.5 kHz; every held note in the picks' C, E and G
//   (R3), and a glide is a sweep, not a held note (R7); rising for on and
//   forward, falling for off and back. build.js refuses a cue outside them.
// - R9 (the takes): A in the direction PLAN-CLICKS 3.2's table gives, B and C the
//   real alternatives it gives, each with its short name. Every take of a cue
//   lasts the cue's Length in that table; its layers are the table's.
//
// No take is the owner's yet (nothing is marked `pick`): A ships until he picks on
// the third board. A pick other than A, or a note, is a recipe change here, a new
// R line and a rebuild, in a task of its own (R9, R5's way).
//
// The notes are COPIED, not imported: each set's sources are its own (a game's
// cues.js is that game's). `N` is the first board's table
// (reference\sound-board-v1.html), `P.C7` the hatchling set's (P), value for value.
"use strict";

// ---- The palette (R3): the first board's N, and C7 from the hatchling set's P -------
const N = { C3:130.81, G3:196.00, C4:261.63, E4:329.63, G4:392.00, C5:523.25, E5:659.25, G5:783.99, C6:1046.50, E6:1318.51, G6:1567.98 };
const P = { C7: 2093.00 };

// ---- The pieces the takes share ---------------------------------------------------------
//
// A tone: one oscillator of fixed pitch (a held note, in the palette) or a glide (a
// sweep, R7). The attack is 2 ms, inside A99's 1 to 3.
const tone = (wave, f0, f1, t, dur, gain, attack = 0.002) => ({ kind: "osc", wave, f0, f1, t, dur, attack, gain });
// A noise band: the first board's seeded noise through a bandpass (q linear) or a
// lowpass (q in dB; 0.7 as the hatchling set's), held at one frequency: a colour,
// not a note. The attack is 1 ms, the shortest R8 allows: a tap starts at once.
const band = (filter, f, q, t, dur, gain, attack = 0.001) => ({ kind: "noise", filter, f0: f, f1: f, q, t, dur, attack, gain });

// The Tap (press A): a bandpass tick at 3000 Hz, q 1.5, 12 ms, under a triangle G6 of
// 30 ms at a third of the tick's gain. toggle-on C and toggle-off C take its noise;
// back C is it an octave down.
const TAP_GAIN = 0.8;
const tapNoise = (f = 3000, gain = TAP_GAIN) => band("bandpass", f, 1.5, 0, 0.012, gain);

const CUES = [
  { id: "press", when: "A button that acts; an armed button's confirming click; a refused game click.", often: "Each click", level: -18, takes: [
    { name: "Tap", dur: 0.035, layers: [
      tapNoise(),
      tone("triangle", N.G6, N.G6, 0, 0.03, TAP_GAIN / 3) ] },
    // A knock: a falling sine sweep (R7) over a short low noise.
    { name: "Knock", dur: 0.035, layers: [
      tone("sine", 900, 600, 0, 0.03, 0.7),
      band("lowpass", 2000, 0.7, 0, 0.01, 0.5) ] },
    { name: "Glass", dur: 0.035, layers: [
      tone("triangle", P.C7, P.C7, 0, 0.025, 0.7) ] } ] },

  { id: "select", when: "A nav, a tab, a radio, a menu opener, a slider's release, a card with no cue of its own.", often: "Each click: the commonest", level: -20, takes: [
    // The Tap's sibling, a little higher in noise and lower in tone.
    { name: "Tick", dur: 0.03, layers: [
      band("bandpass", 2500, 2, 0, 0.01, 0.8),
      tone("triangle", N.E6, N.E6, 0, 0.025, 0.8 / 3) ] },
    { name: "Tock", dur: 0.03, layers: [
      tone("sine", N.G5, N.G5, 0, 0.03, 0.7, 0.003) ] },
    { name: "Grain", dur: 0.03, layers: [
      band("bandpass", 2500, 2, 0, 0.02, 0.8) ] } ] },

  { id: "toggle-on", when: "A check ticked; a menu tick on.", often: "Each click", level: -18, takes: [
    // Rising (R8): E6 then G6, 18 ms each, 22 ms apart.
    { name: "Up", dur: 0.05, layers: [
      tone("triangle", N.E6, N.E6, 0, 0.018, 0.6),
      tone("triangle", N.G6, N.G6, 0.022, 0.018, 0.6) ] },
    { name: "Sweep up", dur: 0.05, layers: [
      tone("triangle", N.C6, N.G6, 0, 0.035, 0.6) ] },
    // The Tap's noise UNDER a triangle G6: here the tone leads at full gain and the
    // tick sits at a third, so it is not press's Tap again at another level.
    { name: "Tap high", dur: 0.05, layers: [
      tapNoise(3000, 0.6 / 3),
      tone("triangle", N.G6, N.G6, 0, 0.03, 0.6) ] } ] },

  { id: "toggle-off", when: "A check cleared; a menu tick off.", often: "Each click", level: -20, takes: [
    // Falling (R8): Up mirrored, G6 then E6.
    { name: "Down", dur: 0.05, layers: [
      tone("triangle", N.G6, N.G6, 0, 0.018, 0.6),
      tone("triangle", N.E6, N.E6, 0.022, 0.018, 0.6) ] },
    { name: "Sweep down", dur: 0.05, layers: [
      tone("triangle", N.G6, N.C6, 0, 0.035, 0.6) ] },
    { name: "Tap low", dur: 0.05, layers: [
      tapNoise(3000, 0.6 / 3),
      tone("triangle", N.C6, N.C6, 0, 0.03, 0.6) ] } ] },

  { id: "arm", when: "An armed button's first click: it asks \"Really…?\" and did not do the thing.", often: "Each first click of a two-click action", level: -16, takes: [
    // Two taps of a triangle C6, each with a noise tick, 20 ms each, 45 ms apart:
    // the second ends at 65 ms, inside the take's 75.
    { name: "Double tap", dur: 0.075, layers: [
      tone("triangle", N.C6, N.C6, 0, 0.02, 0.6),
      tapNoise(3000, 0.4),
      tone("triangle", N.C6, N.C6, 0.045, 0.02, 0.6),
      band("bandpass", 3000, 1.5, 0.045, 0.012, 0.4) ] },
    { name: "Fall", dur: 0.075, layers: [
      tone("triangle", N.E6, N.E6, 0, 0.025, 0.6),
      tone("triangle", N.C6, N.C6, 0.03, 0.025, 0.6) ] },
    // A falling sine sweep C5 -> G4 (R7) over a short lowpass noise at 1500 Hz.
    { name: "Thunk", dur: 0.075, layers: [
      tone("sine", N.C5, N.G4, 0, 0.06, 0.8, 0.003),
      band("lowpass", 1500, 0.7, 0, 0.015, 0.4) ] } ] },

  { id: "back", when: "✕, Close, Later, Cancel, Keep, \"‹ Games\", \"Back to camp\".", often: "Each click", level: -21, takes: [
    // Falling (R8): a sine sweep C6 -> G5 over a 10 ms lowpass noise at 1500 Hz.
    { name: "Soft", dur: 0.035, layers: [
      tone("sine", N.C6, N.G5, 0, 0.03, 0.7, 0.003),
      band("lowpass", 1500, 0.7, 0, 0.01, 0.4) ] },
    { name: "Low tap", dur: 0.035, layers: [
      tone("triangle", N.E5, N.E5, 0, 0.03, 0.7) ] },
    // The Tap an octave down: its noise at 1800 Hz, its triangle G5, the same thirds.
    { name: "Tap down", dur: 0.035, layers: [
      tapNoise(1800),
      tone("triangle", N.G5, N.G5, 0, 0.03, TAP_GAIN / 3) ] } ] },
];

module.exports = { N, P, CUES };
