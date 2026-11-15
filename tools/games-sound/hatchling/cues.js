// Hatchling's 29 cues (H95), in PLAN-SOUND.md section 3.1's four groups and order:
// each its name (a file name, Sound/<name>.wav, and a value of the game's own enum,
// S04's), its group, when it plays, how often, its level (dBFS of the take's peak,
// the same for every take of one cue, R4), and its takes in the first board's
// recipe format (synth.js), A first: take A is what ships (R5).
//
// - The owner's seven picks (R1, `pick: true`): take A only, copied from the first
//   board (reference\sound-board-v1.html) as data with NOTHING changed, its `N`
//   table and `notes()` helper with them, and its levels. Never edit them: a change
//   to one is the owner's, as a new R line.
// - `hit` (take A, the first board's bite take B "Snap", verbatim) and
//   `hunter-step` (take A, its step take C "Soft thud" with the sine 100 -> 55 Hz,
//   the rest as it is) say where they come from (R3).
// - Every other take is new, in the picks' style (R3): 0.07 to 0.85 s; attacks of
//   1 ms or more and exponential decays (the envelope's own); the picks' C-major
//   palette for every held note (C, E and G and their octaves: the first board's N
//   table, with C7 and the octaves below it as needed; by R7, a take the plan
//   derives from a pick keeps the pick's pitch (`focus-apex` A), an octave overtone
//   over a palette note may sit above C7 (`unlock` A), and a glide is a sweep, not a
//   held note); triangle and sine for what
//   is gentle or good, square only for a warning or a fanfare, filtered noise for
//   what is physical. Take A in the direction the table gives, B and C each a real
//   alternative (brighter or sharper, softer or darker, or another idea of the
//   moment), each with a short name. build.js refuses a take outside the length
//   bounds or with an attack under 1 ms.
//
// A pick other than A, or a note, from the second board is a recipe change here, a
// new R line and a rebuild, in a task of its own (R5).
"use strict";

// ---- The first board's own helpers, verbatim (R1) ------------------------------------
const N = { C3:130.81, G3:196.00, C4:261.63, E4:329.63, G4:392.00, C5:523.25, E5:659.25, G5:783.99, C6:1046.50, E6:1318.51, G6:1567.98 };
const notes = (wave, list, gain = 0.6) => list.map(([f, t, d]) => ({ kind: "osc", wave, f0: f, f1: f, t, dur: d, attack: 0.005, gain }));

// The palette's notes the first board's table lacks (R3): C7 above it, and the
// octaves below C3 a low sound needs, each the octave of one in N.
const P = { C7: 2093.00, E3: 164.81, C2: 65.41, E2: 82.41, G2: 98.00 };

// The groups, in their order on the board (PLAN-SOUND 3.1).
const GROUPS = [
  { id: "you", name: "Your turn" },
  { id: "hunter", name: "A hunter's turn" },
  { id: "world", name: "The world" },
  { id: "pages", name: "The pages" },
];

const CUES = [
  // ---- Your turn ---------------------------------------------------------------------
  { id: "step", group: "you", pick: true, when: "A cell you walk.", often: "Every cell", level: -16, takes: [
    { name: "Pad", dur: 0.09, layers: [
      { kind: "noise", filter: "lowpass", f0: 900, f1: 250, q: 0.7, t: 0, dur: 0.07, attack: 0.003, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: 140, f1: 85, t: 0, dur: 0.06, attack: 0.002, gain: 0.6 } ] } ] },

  // The Pad, darker and softer: what a sneak is (quiet, H97).
  { id: "step-sneak", group: "you", when: "A cell you sneak.", often: "Every cell", level: -22, takes: [
    { name: "Soft pad", dur: 0.08, layers: [
      { kind: "noise", filter: "lowpass", f0: 600, f1: 200, q: 0.7, t: 0, dur: 0.06, attack: 0.003, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: 120, f1: 75, t: 0, dur: 0.05, attack: 0.002, gain: 0.6 } ] },
    { name: "Hush", dur: 0.08, layers: [
      { kind: "noise", filter: "bandpass", f0: 500, f1: 250, q: 1, t: 0, dur: 0.06, attack: 0.004, gain: 0.8 } ] },
    { name: "Deep pad", dur: 0.09, layers: [
      { kind: "osc", wave: "sine", f0: 90, f1: 60, t: 0, dur: 0.07, attack: 0.004, gain: 0.8 },
      { kind: "noise", filter: "lowpass", f0: 400, f1: 150, q: 0.7, t: 0, dur: 0.05, attack: 0.004, gain: 0.5 } ] } ] },

  // The Pad, tighter and brighter: a sprint is loud (H97). 0.07 long, so at Fast
  // (six ticks a cell at speed 2, 0.10 s apart) a step ends before the next.
  { id: "step-sprint", group: "you", when: "A cell you sprint.", often: "Every cell", level: -14, takes: [
    { name: "Quick pad", dur: 0.07, layers: [
      { kind: "noise", filter: "lowpass", f0: 1400, f1: 350, q: 0.7, t: 0, dur: 0.05, attack: 0.002, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: 160, f1: 90, t: 0, dur: 0.05, attack: 0.002, gain: 0.6 } ] },
    { name: "Scuff", dur: 0.07, layers: [
      { kind: "noise", filter: "bandpass", f0: 2000, f1: 900, q: 1.2, t: 0, dur: 0.045, attack: 0.001, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: 180, f1: 100, t: 0, dur: 0.04, attack: 0.001, gain: 0.5 } ] },
    { name: "Stomp", dur: 0.07, layers: [
      { kind: "osc", wave: "sine", f0: 150, f1: 70, t: 0, dur: 0.06, attack: 0.002, gain: 0.9 },
      { kind: "noise", filter: "lowpass", f0: 900, f1: 300, q: 0.7, t: 0, dur: 0.03, attack: 0.001, gain: 0.5 } ] } ] },

  { id: "eat", group: "you", when: "You eat: plants, meat or fish.", often: "Once a beat", level: -14, takes: [
    { name: "Two bites", dur: 0.2, layers: [
      { kind: "noise", filter: "bandpass", f0: 900, f1: 500, q: 1.5, t: 0, dur: 0.05, attack: 0.003, gain: 0.8 },
      { kind: "noise", filter: "bandpass", f0: 900, f1: 500, q: 1.5, t: 0.09, dur: 0.05, attack: 0.003, gain: 0.8 } ] },
    { name: "Crunchy", dur: 0.17, layers: [
      { kind: "noise", filter: "bandpass", f0: 1400, f1: 700, q: 1.2, t: 0, dur: 0.04, attack: 0.001, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: 200, f1: 120, t: 0, dur: 0.04, attack: 0.002, gain: 0.4 },
      { kind: "noise", filter: "bandpass", f0: 1400, f1: 700, q: 1.2, t: 0.08, dur: 0.04, attack: 0.001, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: 200, f1: 120, t: 0.08, dur: 0.04, attack: 0.002, gain: 0.4 } ] },
    { name: "Three nibbles", dur: 0.2, layers: [
      { kind: "noise", filter: "bandpass", f0: 800, f1: 500, q: 2, t: 0, dur: 0.035, attack: 0.002, gain: 0.8 },
      { kind: "noise", filter: "bandpass", f0: 800, f1: 500, q: 2, t: 0.07, dur: 0.035, attack: 0.002, gain: 0.7 },
      { kind: "noise", filter: "bandpass", f0: 800, f1: 500, q: 2, t: 0.14, dur: 0.035, attack: 0.002, gain: 0.6 } ] } ] },

  { id: "drink", group: "you", when: "You drink.", often: "Once a beat", level: -15, takes: [
    { name: "Two drops", dur: 0.22, layers: [
      { kind: "osc", wave: "sine", f0: 520, f1: 880, t: 0, dur: 0.05, attack: 0.002, gain: 0.7 },
      { kind: "osc", wave: "sine", f0: 520, f1: 880, t: 0.1, dur: 0.05, attack: 0.002, gain: 0.7 } ] },
    { name: "Plip", dur: 0.18, layers: [
      { kind: "osc", wave: "triangle", f0: N.E5, f1: N.C6, t: 0, dur: 0.045, attack: 0.002, gain: 0.6 },
      { kind: "osc", wave: "triangle", f0: N.E5, f1: N.C6, t: 0.09, dur: 0.045, attack: 0.002, gain: 0.6 } ] },
    { name: "Sip", dur: 0.2, layers: [
      { kind: "noise", filter: "lowpass", f0: 1200, f1: 600, q: 0.7, t: 0, dur: 0.12, attack: 0.02, gain: 0.5 },
      { kind: "osc", wave: "sine", f0: N.G4, f1: N.C5, t: 0.05, dur: 0.07, attack: 0.004, gain: 0.5 } ] } ] },

  { id: "rest", group: "you", when: "You rest.", often: "Once a beat", level: -15, takes: [
    { name: "Slow rise", dur: 0.4, layers: [
      { kind: "osc", wave: "triangle", f0: N.C5, f1: N.C5, t: 0, dur: 0.18, attack: 0.04, gain: 0.6 },
      { kind: "osc", wave: "triangle", f0: N.E5, f1: N.E5, t: 0.14, dur: 0.24, attack: 0.04, gain: 0.6 } ] },
    { name: "Breath", dur: 0.4, layers: [
      { kind: "noise", filter: "lowpass", f0: 500, f1: 300, q: 0.7, t: 0, dur: 0.34, attack: 0.12, gain: 0.6 },
      { kind: "osc", wave: "sine", f0: N.C4, f1: N.C4, t: 0, dur: 0.32, attack: 0.1, gain: 0.3 } ] },
    { name: "Low hum", dur: 0.4, layers: [
      { kind: "osc", wave: "sine", f0: N.C4, f1: N.C4, t: 0, dur: 0.36, attack: 0.08, gain: 0.6 },
      { kind: "osc", wave: "sine", f0: N.E4, f1: N.E4, t: 0.1, dur: 0.26, attack: 0.06, gain: 0.4 } ] } ] },

  // Yours, lighter than a bite on you (R3).
  { id: "hit", group: "you", from: "The first board's bite, take B \"Snap\", as it was.",
    when: "Your strike lands, and each one a sweep strikes.", often: "Once a hit", level: -9, takes: [
    { name: "Snap", dur: 0.15, tone: 6000, layers: [
      { kind: "osc", wave: "square", f0: 230, f1: 60, t: 0, dur: 0.12, attack: 0.001, gain: 0.6 },
      { kind: "noise", filter: "lowpass", f0: 3000, f1: 1200, q: 0.7, t: 0, dur: 0.05, attack: 0.001, gain: 0.6 } ] },
    { name: "Smack", dur: 0.14, layers: [
      { kind: "noise", filter: "bandpass", f0: 1800, f1: 600, q: 1, t: 0, dur: 0.08, attack: 0.001, gain: 0.9 },
      { kind: "osc", wave: "sine", f0: 160, f1: 70, t: 0, dur: 0.1, attack: 0.001, gain: 0.6 } ] },
    { name: "Thud", dur: 0.15, layers: [
      { kind: "osc", wave: "sine", f0: 140, f1: 60, t: 0, dur: 0.12, attack: 0.002, gain: 0.9 },
      { kind: "noise", filter: "bandpass", f0: 900, f1: 400, q: 1.5, t: 0, dur: 0.06, attack: 0.001, gain: 0.6 } ] } ] },

  { id: "defeat", group: "you", when: "A creature falls: your hit, your spikes, its bleeding.", often: "Once a fall", level: -10, takes: [
    { name: "Dull fall", dur: 0.3, layers: [
      ...notes("triangle", [[N.G4, 0, 0.12], [N.C4, 0.1, 0.18]]),
      { kind: "osc", wave: "sine", f0: 90, f1: 50, t: 0.1, dur: 0.16, attack: 0.004, gain: 0.5 } ] },
    { name: "Slump", dur: 0.3, layers: [
      { kind: "noise", filter: "lowpass", f0: 700, f1: 150, q: 0.7, t: 0, dur: 0.22, attack: 0.01, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: P.G2, f1: P.G2 / 2, t: 0, dur: 0.26, attack: 0.01, gain: 0.6 } ] },
    { name: "Two down", dur: 0.3, layers: notes("triangle", [[N.E4, 0, 0.12], [N.C4, 0.12, 0.16]]) } ] },

  // ---- A hunter's turn ---------------------------------------------------------------
  { id: "focus", group: "hunter", pick: true, when: "A known predator's turn begins.", often: "Each hunter", level: -11, takes: [
    { name: "Ping", dur: 0.16, layers: [
      { kind: "osc", wave: "triangle", f0: 880, f1: 880, t: 0, dur: 0.15, attack: 0.002, gain: 0.7 } ] } ] },

  // Take A as the table gives it (R7): the owner's Ping (880 Hz) an octave down, over
  // a sine two octaves under that. B and C keep to the C-major palette.
  { id: "focus-apex", group: "hunter", when: "The apex's turn begins.", often: "Each of its turns", level: -10, takes: [
    { name: "Low ping", dur: 0.22, layers: [
      { kind: "osc", wave: "triangle", f0: 440, f1: 440, t: 0, dur: 0.2, attack: 0.002, gain: 0.7 },
      { kind: "osc", wave: "sine", f0: 110, f1: 110, t: 0, dur: 0.2, attack: 0.004, gain: 0.6 } ] },
    { name: "Deep drum", dur: 0.22, layers: [
      { kind: "noise", filter: "bandpass", f0: 300, f1: 250, q: 3, t: 0, dur: 0.15, attack: 0.002, gain: 0.8 },
      { kind: "osc", wave: "sine", f0: N.C3, f1: P.G2, t: 0, dur: 0.18, attack: 0.002, gain: 0.7 } ] },
    { name: "Low two-tone", dur: 0.22, layers: [
      ...notes("triangle", [[N.C4, 0, 0.09], [N.G3, 0.09, 0.12]]),
      { kind: "osc", wave: "sine", f0: N.C3, f1: N.C3, t: 0, dur: 0.2, attack: 0.004, gain: 0.4 } ] } ] },

  { id: "spotted", group: "hunter", pick: true, when: "It spots you.", often: "When it does", level: -8, takes: [
    { name: "Chirp up", dur: 0.16, tone: 4000, layers: [
      { kind: "osc", wave: "square", f0: 600, f1: 1200, glide: "linear", t: 0, dur: 0.14, attack: 0.003, gain: 0.5 } ] } ] },

  // Half the warning's chirp, in the gentle triangle: heard is not yet seen.
  { id: "heard", group: "hunter", when: "It hears you; and \"Something heard you\" out of sight.", often: "When it does", level: -13, takes: [
    { name: "Half chirp", dur: 0.12, layers: [
      { kind: "osc", wave: "triangle", f0: 600, f1: 800, glide: "linear", t: 0, dur: 0.1, attack: 0.003, gain: 0.6 } ] },
    { name: "Two pips", dur: 0.12, layers: notes("triangle", [[N.G5, 0, 0.04], [N.C6, 0.05, 0.05]]) },
    { name: "Ear twitch", dur: 0.12, layers: [
      { kind: "noise", filter: "bandpass", f0: 2500, f1: 3500, q: 2, t: 0, dur: 0.06, attack: 0.003, gain: 0.6 },
      { kind: "osc", wave: "triangle", f0: 600, f1: 800, glide: "linear", t: 0, dur: 0.08, attack: 0.003, gain: 0.3 } ] } ] },

  // The chirp turned down and soft: the danger going.
  { id: "lost-trail", group: "hunter", when: "It loses your trail.", often: "When it does", level: -15, takes: [
    { name: "Chirp down", dur: 0.16, layers: [
      { kind: "osc", wave: "triangle", f0: 900, f1: 600, glide: "linear", t: 0, dur: 0.14, attack: 0.003, gain: 0.6 } ] },
    { name: "Two pips down", dur: 0.16, layers: notes("triangle", [[N.C6, 0, 0.05], [N.G5, 0.06, 0.08]]) },
    { name: "Sigh", dur: 0.16, layers: [
      { kind: "osc", wave: "sine", f0: 700, f1: 400, t: 0, dur: 0.14, attack: 0.01, gain: 0.6 } ] } ] },

  // Quieter and lower than your step (H97). Under 0.13 s, the gap between two of a
  // hunter's steps at Fast (eight ticks a cell at speed 2).
  { id: "hunter-step", group: "hunter", from: "The first board's step, take C \"Soft thud\", its sine lowered to 100 → 55 Hz.",
    when: "A cell a known predator walks.", often: "Every cell", level: -20, takes: [
    { name: "Soft thud", dur: 0.11, layers: [
      { kind: "osc", wave: "sine", f0: 100, f1: 55, t: 0, dur: 0.1, attack: 0.004, gain: 0.9 } ] },
    { name: "Padfall", dur: 0.11, layers: [
      { kind: "noise", filter: "lowpass", f0: 500, f1: 150, q: 0.7, t: 0, dur: 0.06, attack: 0.003, gain: 0.7 },
      { kind: "osc", wave: "sine", f0: 90, f1: 55, t: 0, dur: 0.08, attack: 0.003, gain: 0.7 } ] },
    { name: "Heavy", dur: 0.12, layers: [
      { kind: "osc", wave: "sine", f0: 80, f1: 45, t: 0, dur: 0.1, attack: 0.004, gain: 0.9 },
      { kind: "noise", filter: "bandpass", f0: 300, f1: 200, q: 2, t: 0, dur: 0.04, attack: 0.002, gain: 0.3 } ] } ] },

  { id: "bite", group: "hunter", pick: true, when: "A bite on you.", often: "Once a bite", level: -5, takes: [
    { name: "Crunch", dur: 0.22, layers: [
      { kind: "noise", filter: "bandpass", f0: 1300, f1: 380, q: 1, t: 0, dur: 0.16, attack: 0.002, gain: 0.9 },
      { kind: "osc", wave: "sine", f0: 95, f1: 45, t: 0, dur: 0.2, attack: 0.002, gain: 0.8 } ] } ] },

  { id: "dodge", group: "hunter", when: "You dodge a bite.", often: "Once", level: -12, takes: [
    { name: "Whoosh", dur: 0.14, layers: [
      { kind: "noise", filter: "bandpass", f0: 1200, f1: 3500, q: 1.2, t: 0, dur: 0.12, attack: 0.02, gain: 0.8 } ] },
    { name: "Swish down", dur: 0.13, layers: [
      { kind: "noise", filter: "bandpass", f0: 3500, f1: 1200, q: 1.5, t: 0, dur: 0.11, attack: 0.01, gain: 0.8 } ] },
    { name: "Whoosh and hop", dur: 0.15, layers: [
      { kind: "noise", filter: "bandpass", f0: 1500, f1: 3000, q: 1.2, t: 0, dur: 0.09, attack: 0.015, gain: 0.7 },
      { kind: "osc", wave: "triangle", f0: N.G5, f1: N.G5, t: 0.08, dur: 0.06, attack: 0.003, gain: 0.35 } ] } ] },

  { id: "sting", group: "hunter", when: "Your spikes hurt it, or it bleeds.", often: "Once a hurt", level: -13, takes: [
    { name: "Prick", dur: 0.1, layers: [
      { kind: "noise", filter: "bandpass", f0: 3000, f1: 3000, q: 2, t: 0, dur: 0.03, attack: 0.001, gain: 0.7 },
      { kind: "osc", wave: "triangle", f0: 1200, f1: 900, t: 0, dur: 0.06, attack: 0.001, gain: 0.5 } ] },
    { name: "Needle", dur: 0.08, layers: [
      { kind: "osc", wave: "triangle", f0: P.C7, f1: N.G6, t: 0, dur: 0.05, attack: 0.001, gain: 0.6 },
      { kind: "noise", filter: "bandpass", f0: 5000, f1: 5000, q: 3, t: 0, dur: 0.02, attack: 0.001, gain: 0.5 } ] },
    { name: "Dull prick", dur: 0.1, layers: [
      { kind: "noise", filter: "bandpass", f0: 1500, f1: 1500, q: 1.5, t: 0, dur: 0.04, attack: 0.001, gain: 0.7 },
      { kind: "osc", wave: "sine", f0: 600, f1: 400, t: 0, dur: 0.07, attack: 0.002, gain: 0.5 } ] } ] },

  { id: "second-wind", group: "hunter", when: "Second wind.", often: "Once", level: -9, takes: [
    { name: "Three up", dur: 0.36, layers: notes("triangle", [[N.E5, 0, 0.1], [N.G5, 0.08, 0.1], [N.C6, 0.16, 0.18]]) },
    { name: "Breath in", dur: 0.36, layers: [
      { kind: "noise", filter: "lowpass", f0: 400, f1: 1600, q: 0.7, t: 0, dur: 0.2, attack: 0.12, gain: 0.5 },
      { kind: "osc", wave: "triangle", f0: N.C6, f1: N.C6, t: 0.16, dur: 0.18, attack: 0.005, gain: 0.6 } ] },
    { name: "Chord swell", dur: 0.36, layers: [
      { kind: "osc", wave: "sine", f0: N.C5, f1: N.C5, t: 0, dur: 0.32, attack: 0.06, gain: 0.5 },
      { kind: "osc", wave: "sine", f0: N.E5, f1: N.E5, t: 0, dur: 0.32, attack: 0.06, gain: 0.4 },
      { kind: "osc", wave: "sine", f0: N.G5, f1: N.G5, t: 0, dur: 0.32, attack: 0.06, gain: 0.4 } ] } ] },

  { id: "arrive", group: "hunter", when: "A predator arrives where you see it.", often: "Each", level: -12, takes: [
    { name: "Low swell", dur: 0.35, layers: [
      { kind: "noise", filter: "lowpass", f0: 300, f1: 700, q: 0.7, t: 0, dur: 0.3, attack: 0.15, gain: 0.7 },
      { kind: "osc", wave: "sine", f0: P.G2, f1: P.G2, t: 0, dur: 0.32, attack: 0.1, gain: 0.5 } ] },
    { name: "Rumble", dur: 0.35, layers: [
      { kind: "noise", filter: "lowpass", f0: 200, f1: 400, q: 3, t: 0, dur: 0.3, attack: 0.1, gain: 0.7 },
      { kind: "osc", wave: "sine", f0: P.C2, f1: P.C2, t: 0, dur: 0.3, attack: 0.08, gain: 0.6 } ] },
    { name: "Dark tone", dur: 0.35, layers: [
      { kind: "osc", wave: "triangle", f0: N.G3, f1: N.G3, t: 0, dur: 0.3, attack: 0.08, gain: 0.5 },
      { kind: "osc", wave: "sine", f0: P.G2, f1: P.G2, t: 0, dur: 0.32, attack: 0.1, gain: 0.5 } ] } ] },

  // ---- The world ---------------------------------------------------------------------
  { id: "starve", group: "world", when: "Hunger or thirst takes health.", often: "Once a turn", level: -11, takes: [
    { name: "Hollow pulse", dur: 0.32, layers: [
      { kind: "osc", wave: "sine", f0: 80, f1: 60, t: 0, dur: 0.12, attack: 0.005, gain: 0.9 },
      { kind: "osc", wave: "sine", f0: 80, f1: 60, t: 0.16, dur: 0.12, attack: 0.005, gain: 0.9 } ] },
    { name: "Growling belly", dur: 0.32, layers: [
      { kind: "noise", filter: "lowpass", f0: 250, f1: 120, q: 2, t: 0, dur: 0.1, attack: 0.005, gain: 0.6 },
      { kind: "osc", wave: "sine", f0: 70, f1: 55, t: 0, dur: 0.12, attack: 0.005, gain: 0.8 },
      { kind: "noise", filter: "lowpass", f0: 250, f1: 120, q: 2, t: 0.16, dur: 0.1, attack: 0.005, gain: 0.6 },
      { kind: "osc", wave: "sine", f0: 70, f1: 55, t: 0.16, dur: 0.12, attack: 0.005, gain: 0.8 } ] },
    { name: "Low tones", dur: 0.32, layers: notes("triangle", [[N.C3, 0, 0.12], [P.G2, 0.16, 0.14]]) } ] },

  { id: "stage", group: "world", pick: true, when: "You grow into a stage.", often: "Each stage", level: -9, takes: [
    { name: "Arpeggio", dur: 0.4, layers: notes("triangle", [[N.C5, 0, 0.13], [N.E5, 0.07, 0.13], [N.G5, 0.14, 0.13], [N.C6, 0.21, 0.18]]) } ] },

  // The apex is a warning: a square may growl (R3).
  { id: "apex", group: "world", when: "The apex has your scent.", often: "Once a run", level: -7, takes: [
    { name: "Growl", dur: 0.7, tone: 600, layers: [
      { kind: "osc", wave: "square", f0: 70, f1: 55, t: 0, dur: 0.6, attack: 0.08, gain: 0.5 },
      { kind: "noise", filter: "lowpass", f0: 400, f1: 400, q: 0.7, t: 0, dur: 0.55, attack: 0.08, gain: 0.5 },
      { kind: "osc", wave: "triangle", f0: N.G3, f1: N.C3, t: 0, dur: 0.6, attack: 0.08, gain: 0.4 } ] },
    { name: "Roar", dur: 0.65, tone: 900, layers: [
      { kind: "noise", filter: "lowpass", f0: 800, f1: 300, q: 3, t: 0, dur: 0.5, attack: 0.06, gain: 0.8 },
      { kind: "osc", wave: "square", f0: P.G2, f1: P.C2, t: 0, dur: 0.55, attack: 0.06, gain: 0.35 } ] },
    { name: "Dread", dur: 0.7, layers: [
      { kind: "osc", wave: "sine", f0: P.G2, f1: P.G2, t: 0, dur: 0.6, attack: 0.1, gain: 0.6 },
      { kind: "osc", wave: "triangle", f0: N.C3, f1: N.C3, t: 0, dur: 0.6, attack: 0.12, gain: 0.4 },
      { kind: "noise", filter: "lowpass", f0: 200, f1: 200, q: 0.7, t: 0, dur: 0.6, attack: 0.2, gain: 0.4 } ] } ] },

  { id: "win", group: "world", pick: true, when: "The run is won.", often: "Once", level: -7, takes: [
    { name: "Fanfare", dur: 0.85, tone: 6000, layers: [
      ...notes("square", [[N.G4, 0, 0.11], [N.C5, 0.12, 0.11], [N.E5, 0.24, 0.11], [N.G5, 0.36, 0.45]], 0.35),
      { kind: "osc", wave: "triangle", f0: N.C3, f1: N.C3, t: 0.36, dur: 0.45, attack: 0.01, gain: 0.5 } ] } ] },

  { id: "lost", group: "world", pick: true, when: "The run is lost: caught, out of food, out of water.", often: "Once", level: -10, takes: [
    { name: "Fall", dur: 0.7, layers: notes("triangle", [[N.G4, 0, 0.22], [N.E4, 0.18, 0.22], [N.C4, 0.36, 0.32]], 0.6) } ] },

  // ---- The pages ---------------------------------------------------------------------
  { id: "hatch", group: "pages", when: "A run starts (Camp's \"Start a run\").", often: "Each start", level: -10, takes: [
    { name: "Egg", dur: 0.3, layers: [
      { kind: "noise", filter: "bandpass", f0: 2500, f1: 2500, q: 2, t: 0, dur: 0.03, attack: 0.001, gain: 0.8 },
      { kind: "osc", wave: "triangle", f0: N.C5, f1: N.G5, t: 0.05, dur: 0.2, attack: 0.01, gain: 0.6 } ] },
    { name: "Two cracks", dur: 0.3, layers: [
      { kind: "noise", filter: "bandpass", f0: 3000, f1: 3000, q: 2, t: 0, dur: 0.025, attack: 0.001, gain: 0.7 },
      { kind: "noise", filter: "bandpass", f0: 3000, f1: 3000, q: 2, t: 0.06, dur: 0.025, attack: 0.001, gain: 0.8 },
      { kind: "osc", wave: "triangle", f0: N.G5, f1: N.G5, t: 0.12, dur: 0.15, attack: 0.005, gain: 0.6 } ] },
    { name: "Soft hatch", dur: 0.3, layers: [
      { kind: "noise", filter: "lowpass", f0: 1800, f1: 800, q: 0.7, t: 0, dur: 0.04, attack: 0.002, gain: 0.6 },
      ...notes("sine", [[N.C5, 0.06, 0.1], [N.G5, 0.14, 0.14]]) ] } ] },

  { id: "choose", group: "pages", when: "An instinct picked; a trait's rank taken.", often: "Each", level: -12, takes: [
    { name: "Two up", dur: 0.2, layers: notes("triangle", [[N.G5, 0, 0.08], [N.C6, 0.06, 0.12]]) },
    { name: "Click and note", dur: 0.16, layers: [
      { kind: "noise", filter: "bandpass", f0: 3000, f1: 3000, q: 2, t: 0, dur: 0.015, attack: 0.001, gain: 0.5 },
      { kind: "osc", wave: "triangle", f0: N.C6, f1: N.C6, t: 0.01, dur: 0.12, attack: 0.003, gain: 0.6 } ] },
    { name: "Soft two", dur: 0.2, layers: notes("sine", [[N.E5, 0, 0.08], [N.G5, 0.06, 0.12]]) } ] },

  { id: "shuffle", group: "pages", when: "A redraw; a journal entry swapped.", often: "Each", level: -14, takes: [
    { name: "Riffle", dur: 0.2, layers: [0, 0.035, 0.07, 0.105].map((t) =>
      ({ kind: "noise", filter: "bandpass", f0: 4000, f1: 4000, q: 2, t, dur: 0.02, attack: 0.001, gain: 0.7 })) },
    { name: "Swipe", dur: 0.18, layers: [
      { kind: "noise", filter: "bandpass", f0: 1500, f1: 4000, q: 1, t: 0, dur: 0.14, attack: 0.02, gain: 0.7 } ] },
    { name: "Soft riffle", dur: 0.2, layers: [0, 0.04, 0.08, 0.12].map((t) =>
      ({ kind: "noise", filter: "lowpass", f0: 2500, f1: 1500, q: 0.7, t, dur: 0.025, attack: 0.001, gain: 0.7 })) } ] },

  // Take A as the table gives it: C6 then G6 with a sine an octave over each, G6's
  // at G7 (3,136 Hz), an overtone above C7 (R7). B and C keep to the C-major palette.
  { id: "unlock", group: "pages", when: "A species unlocked for amber.", often: "Each", level: -10, takes: [
    { name: "Ding-ding", dur: 0.4, layers: [
      { kind: "osc", wave: "triangle", f0: N.C6, f1: N.C6, t: 0, dur: 0.15, attack: 0.003, gain: 0.6 },
      { kind: "osc", wave: "sine", f0: N.C6 * 2, f1: N.C6 * 2, t: 0, dur: 0.12, attack: 0.003, gain: 0.25 },
      { kind: "osc", wave: "triangle", f0: N.G6, f1: N.G6, t: 0.14, dur: 0.24, attack: 0.003, gain: 0.6 },
      { kind: "osc", wave: "sine", f0: N.G6 * 2, f1: N.G6 * 2, t: 0.14, dur: 0.18, attack: 0.003, gain: 0.25 } ] },
    { name: "Rising bell", dur: 0.4, layers: notes("triangle", [[N.E6, 0, 0.12], [N.G6, 0.1, 0.12], [P.C7, 0.2, 0.18]]) },
    { name: "Warm ding", dur: 0.4, layers: notes("sine", [[N.G5, 0, 0.16], [N.C6, 0.12, 0.26]]) } ] },

  { id: "reward", group: "pages", when: "An achievement earned.", often: "Each action that earned one", level: -10, takes: [
    { name: "Sparkle", dur: 0.5, layers: notes("triangle", [[N.C6, 0, 0.08], [N.E6, 0.05, 0.08], [N.G6, 0.1, 0.08], [P.C7, 0.15, 0.33]], 0.55) },
    { name: "Chime run", dur: 0.5, layers: [
      ...notes("triangle", [[N.C5, 0, 0.12], [N.E5, 0.06, 0.12], [N.G5, 0.12, 0.12], [N.C6, 0.18, 0.3]], 0.5),
      { kind: "osc", wave: "sine", f0: P.C7, f1: P.C7, t: 0.18, dur: 0.25, attack: 0.005, gain: 0.2 } ] },
    { name: "Glow", dur: 0.5, layers: [
      { kind: "osc", wave: "sine", f0: N.C5, f1: N.C5, t: 0, dur: 0.45, attack: 0.03, gain: 0.5 },
      { kind: "osc", wave: "sine", f0: N.E5, f1: N.E5, t: 0, dur: 0.45, attack: 0.03, gain: 0.4 },
      { kind: "osc", wave: "sine", f0: N.G5, f1: N.G5, t: 0, dur: 0.45, attack: 0.03, gain: 0.4 },
      { kind: "osc", wave: "triangle", f0: N.C6, f1: N.C6, t: 0.1, dur: 0.35, attack: 0.005, gain: 0.3 } ] } ] },
];

module.exports = { N, P, notes, GROUPS, CUES };
