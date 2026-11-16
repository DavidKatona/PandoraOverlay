// The synthesis (R2): a take of the first sound board's recipe format to samples,
// by WebAudio's OWN definitions, not an imitation of how they sound.
//
// The owner picked his seven sounds by ear on the first board
// (reference\sound-board-v1.html in the games folder), and what he heard was his
// browser rendering a WebAudio graph: an OscillatorNode or a noise
// AudioBufferSourceNode through a BiquadFilterNode, into a GainNode whose gain is
// automated, all into an optional lowpass (the take's `tone`). So this file
// renders the same graph by the specification's formulas (Web Audio API, W3C
// Recommendation; the sections are cited per function), at the board's rate, so
// the WAV that ships is the sound he picked. The second board holds it (its two
// figures per pick) and his ear is the last check.
//
// A recipe is the first board's format, unchanged, so a recipe can be pasted
// between the two:
//   take  { name, dur, tone?, layers: [...] }
//   layer { kind: "osc", wave, f0, f1, glide?, t, dur, attack, gain }
//       | { kind: "noise", filter, f0, f1, q, t, dur, attack, gain }
// and the cue's `level`: the dBFS the take's peak is normalised to.
//
// Deterministic: no clock, no unseeded randomness; the same recipe gives the same
// samples on every run (Node's numbers are the browser's: IEEE doubles).
"use strict";

// The first board's own rate (`RATE` in its code), so a recipe renders at the
// rate it was heard at (PLAN-SOUND 5.2); the Contract's GameSound.SampleRate.
const RATE = 44100;
const NYQUIST = RATE / 2;

// ---- Time to frames -------------------------------------------------------------

// The first frame at or after a time (Web Audio 1.24.2 AudioScheduledSourceNode
// start() and stop(), and 1.6 AudioParam's events, are sample-accurate: a source
// "starts at" the first sample frame whose time is >= `when`). Computed by
// comparing frame times, not by ceil(t * RATE) alone: 0.07 * 44100 is
// 3087.0000000000005 in doubles, and 3087 / 44100 is exactly the double 0.07, so
// frame 3087 is AT the time. (Measured, Nov 15 2026, in the browser pane's Blink:
// it starts that note on 3088, plain ceil, one frame later. The brief's rule is
// kept; one frame moves the stage pick's correlation by 0.003.)
function frameAtOrAfter(t) {
  let k = Math.max(0, Math.ceil(t * RATE));
  while (k > 0 && (k - 1) / RATE >= t) k--;
  while (k / RATE < t) k++;
  return k;
}

// ---- AudioParam automation (Web Audio 1.6.3, the automation methods) --------------

// A parameter's timeline: events in the order they were made, each { type, value,
// time }, and its default value before the first one (1.6.1, `defaultValue`).
// value(t) is the spec's own computation, per sample (a-rate, 1.6.2):
//  - setValueAtTime(V, T) (1.6.3.6): from T on, V, until the next event.
//  - linearRampToValueAtTime(V1, T1) (1.6.3.4): from the previous event (T0, V0),
//    v(t) = V0 + (V1 - V0) * (t - T0) / (T1 - T0), then V1 from T1.
//  - exponentialRampToValueAtTime(V1, T1) (1.6.3.3): from the previous event,
//    v(t) = V0 * (V1 / V0) ^ ((t - T0) / (T1 - T0)), then V1 from T1; when V0 and
//    V1 are not both of one sign (or V0 is 0) the spec holds V0 until T1.
// A ramp's "previous event" is the event before it in the list: a setValue's value
// at its time, or the previous ramp's end. A ramp with no previous event starts at
// the default value at time 0 (never the case in a recipe: each starts with a set).
class Param {
  constructor(defaultValue) {
    this.defaultValue = defaultValue;
    this.events = [];
  }
  setValueAtTime(value, time) { this.events.push({ type: "set", value, time }); return this; }
  linearRampToValueAtTime(value, time) { this.events.push({ type: "linear", value, time }); return this; }
  exponentialRampToValueAtTime(value, time) { this.events.push({ type: "exponential", value, time }); return this; }

  value(t) {
    const ev = this.events;
    let previousTime = 0;
    let previousValue = this.defaultValue;
    for (let i = 0; i < ev.length; i++) {
      const e = ev[i];
      if (e.time <= t) {
        previousTime = e.time;
        previousValue = e.value;
        continue;
      }
      // The first event still to come: a ramp is under way toward it; a set holds the value before it.
      if (e.type === "set") return previousValue;
      const span = e.time - previousTime;
      const k = span > 0 ? (t - previousTime) / span : 1;
      if (e.type === "linear") return previousValue + (e.value - previousValue) * k;
      if (previousValue === 0 || previousValue * e.value <= 0) return previousValue;
      return previousValue * Math.pow(e.value / previousValue, k);
    }
    return previousValue;
  }
}

// The first board's envelope(), exactly: 0 at t, linear to the gain at t + attack,
// exponential to 0.0001 at t + dur, 0 from t + dur + 0.001. A GainNode's gain
// (1.18, default 1).
function envelope(layer) {
  const g = new Param(1);
  const t0 = layer.t, a = layer.attack, end = layer.t + layer.dur;
  g.setValueAtTime(0, t0);
  g.linearRampToValueAtTime(layer.gain, t0 + a);
  g.exponentialRampToValueAtTime(0.0001, end);
  g.setValueAtTime(0, end + 0.001);
  return g;
}

// The first board's glide(), exactly: f0 at t; if f1 differs, a ramp to f1 at
// t + dur, exponential unless the layer says glide: "linear".
function glide(param, layer, f0, f1) {
  param.setValueAtTime(f0, layer.t);
  if (f0 !== f1) {
    if (layer.glide === "linear") param.linearRampToValueAtTime(f1, layer.t + layer.dur);
    else param.exponentialRampToValueAtTime(f1, layer.t + layer.dur);
  }
  return param;
}

// ---- OscillatorNode (Web Audio 1.27, and 1.28 PeriodicWave) ------------------------

// The built-in waveforms' Fourier coefficients (1.27.2, OscillatorType; 1.28.3,
// "Oscillator Coefficients"): every type is a sine series, its cosine terms 0:
//   sine      b[1] = 1, every other 0
//   square    b[n] = 2 / (n pi) * (1 - (-1)^n)
//   sawtooth  b[n] = (-1)^(n+1) * 2 / (n pi)
//   triangle  b[n] = 8 sin(n pi / 2) / (pi n)^2
// for n from 1 to PARTIALS. At 44,100 Hz Blink's PeriodicWave tables are 4,096
// samples, so 2,048 partials (its periodicWaveSize / 2): the "full band".
const PARTIALS = 2048;
const TABLE = 4096;

function coefficients(wave) {
  const b = new Float64Array(PARTIALS + 1);
  for (let n = 1; n <= PARTIALS; n++) {
    switch (wave) {
      case "sine": b[n] = n === 1 ? 1 : 0; break;
      case "square": b[n] = (2 / (n * Math.PI)) * (1 - (n % 2 === 0 ? 1 : -1)); break;
      case "sawtooth": b[n] = ((n % 2 === 1 ? 1 : -1) * 2) / (n * Math.PI); break;
      case "triangle": b[n] = (8 * Math.sin((n * Math.PI) / 2)) / ((Math.PI * n) * (Math.PI * n)); break;
      default: throw new Error(`unknown wave '${wave}'`);
    }
  }
  return b;
}

// sum over n = 1..count of b[n] sin(n phi), by the sines' own recurrence
// (sin((n+1)x) = 2 cos x sin(nx) - sin((n-1)x)): exact in doubles to far below
// a 16-bit step for the few hundred partials a sound uses.
function series(b, count, phi) {
  if (count <= 0) return 0;
  const c2 = 2 * Math.cos(phi);
  let prev = 0;          // sin(0 * phi)
  let cur = Math.sin(phi); // sin(1 * phi)
  let sum = b[1] * cur;
  for (let n = 2; n <= count; n++) {
    const next = c2 * cur - prev;
    prev = cur;
    cur = next;
    sum += b[n] * cur;
  }
  return sum;
}

// The normalisation (1.28.4, "Waveform Normalization"): a PeriodicWave made
// without disableNormalization is scaled so the peak of its time-domain
// waveform is 1. Blink takes that scale from its full-band table (all 2,048
// partials, the table of 4,096 samples) and uses it for every band-limited table
// it plays, as this does: one scale per wave, the peak over the same 4,096 points.
const scales = new Map();
function scaleOf(wave) {
  if (!scales.has(wave)) {
    const b = coefficients(wave);
    let peak = 0;
    for (let j = 0; j < TABLE; j++) peak = Math.max(peak, Math.abs(series(b, PARTIALS, (2 * Math.PI * j) / TABLE)));
    scales.set(wave, { b, scale: 1 / peak });
  }
  return scales.get(wave);
}

// One oscillator layer into `out` (the take's frames), through its envelope.
// Band-limited at each sample's own frequency: only the partials under Nyquist
// (n * f < RATE / 2) are summed, as a band-limited table would hold (1.27.2: the
// waveform "must be band-limited"). Phase from 0 at the start frame (1.27: a
// PeriodicWave's phase starts at 0), advanced by each sample's frequency (the
// a-rate `frequency`, 1.27.4, default 440), output read before the advance, as
// Blink's oscillator reads its table. Starts on the first frame at or after t,
// stops at t + dur + 0.01 (the first board's o.stop).
function renderOsc(layer, out) {
  const { b, scale } = scaleOf(layer.wave);
  const freq = glide(new Param(440), layer, layer.f0, layer.f1);
  const gain = envelope(layer);
  const start = frameAtOrAfter(layer.t);
  const stop = Math.min(out.length, frameAtOrAfter(layer.t + layer.dur + 0.01));
  let phi = 0;
  for (let k = start; k < stop; k++) {
    const t = k / RATE;
    const f = freq.value(t);
    const count = f > 0 ? Math.min(PARTIALS, Math.ceil(NYQUIST / f) - 1) : 0;
    out[k] += series(b, count, phi) * scale * gain.value(t);
    phi += (2 * Math.PI * f) / RATE;
    if (phi >= 2 * Math.PI) phi -= 2 * Math.PI * Math.floor(phi / (2 * Math.PI));
  }
}

// ---- BiquadFilterNode (Web Audio 1.13) --------------------------------------------

// The coefficients by the spec's formulas (1.13.6, "Filters Characteristics",
// after the Audio EQ Cookbook), with w0 = 2 pi f0 / Fs and the computed frequency
// clamped to [0, Nyquist] (1.13.3):
//   lowpass   alpha = sin(w0) / (2 * 10^(Q / 20))   (Q in dB)
//             b0 = (1 - cos w0) / 2, b1 = 1 - cos w0, b2 = (1 - cos w0) / 2
//   bandpass  alpha = sin(w0) / (2 Q)                (Q linear)
//             b0 = alpha, b1 = 0, b2 = -alpha
//   both      a0 = 1 + alpha, a1 = -2 cos w0, a2 = 1 - alpha
function biquadCoefficients(type, frequency, q) {
  const f = Math.min(Math.max(frequency, 0), NYQUIST);
  const w0 = (2 * Math.PI * f) / RATE;
  const cos = Math.cos(w0), sin = Math.sin(w0);
  if (type === "lowpass") {
    const alpha = sin / (2 * Math.pow(10, q / 20));
    return { b0: (1 - cos) / 2, b1: 1 - cos, b2: (1 - cos) / 2, a0: 1 + alpha, a1: -2 * cos, a2: 1 - alpha };
  }
  if (type === "bandpass") {
    const alpha = sin / (2 * q);
    return { b0: alpha, b1: 0, b2: -alpha, a0: 1 + alpha, a1: -2 * cos, a2: 1 - alpha };
  }
  throw new Error(`unknown filter '${type}'`);
}

// A filter over a whole signal in place, direct form I (1.13.6's difference
// equation: a0 y[n] = b0 x[n] + b1 x[n-1] + b2 x[n-2] - a1 y[n-1] - a2 y[n-2]),
// its coefficients recomputed EVERY sample from the automated frequency (the
// a-rate `frequency`, 1.13.3, default 350), as Blink does when a parameter has
// automation. Q is a plain value (the first board sets `.Q.value`).
function filter(signal, type, frequency, q) {
  let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  for (let k = 0; k < signal.length; k++) {
    const c = biquadCoefficients(type, frequency.value(k / RATE), q);
    const x = signal[k];
    const y = (c.b0 * x + c.b1 * x1 + c.b2 * x2 - c.a1 * y1 - c.a2 * y2) / c.a0;
    x2 = x1; x1 = x;
    y2 = y1; y1 = y;
    signal[k] = y;
  }
  return signal;
}

// ---- Noise (the first board's own) -----------------------------------------------

// The first board's noiseBuffer(): its generator, s = (s * 1103515245 + 12345)
// >>> 0, seeded 12345 for every noise layer, in JavaScript's own arithmetic (the
// product exceeds 2^53 and rounds exactly as the browser's does), each value
// (s / 2^32) * 2 - 1 stored as a 32-bit float (an AudioBuffer's channel is a
// Float32Array, 1.4), Math.ceil((dur + 0.02) * 44100) of them.
function noiseBuffer(seconds) {
  const d = new Float32Array(Math.ceil(seconds * RATE));
  let s = 12345;
  for (let i = 0; i < d.length; i++) { s = (s * 1103515245 + 12345) >>> 0; d[i] = (s / 4294967296) * 2 - 1; }
  return d;
}

// One noise layer into `out`: the buffer from the first frame at or after t
// (an AudioBufferSourceNode, 1.9, played once at rate 1, stopped at
// t + dur + 0.01 as the first board's src.stop), through its biquad (its
// frequency glided, its Q the layer's), then its envelope.
function renderNoise(layer, out) {
  const buffer = noiseBuffer(layer.dur + 0.02);
  const start = frameAtOrAfter(layer.t);
  const stop = Math.min(out.length, frameAtOrAfter(layer.t + layer.dur + 0.01));
  const signal = new Float64Array(out.length);
  for (let k = start; k < stop && k - start < buffer.length; k++) signal[k] = buffer[k - start];
  filter(signal, layer.filter, glide(new Param(350), layer, layer.f0, layer.f1), layer.q);
  const gain = envelope(layer);
  for (let k = start; k < out.length; k++) out[k] += signal[k] * gain.value(k / RATE);
}

// ---- The take ---------------------------------------------------------------------

// The first board's renderTake(): Math.ceil(dur * 44100) frames (its
// OfflineAudioContext's length); every layer summed (each through its own
// envelope); through the take's `tone` when it has one (a lowpass at that
// frequency and the default Q, 1 dB, 1.13.3); then peak-normalised to the cue's
// level. Then, for the WAV: each sample Math.round(x * 32767), clamped to 16 bits,
// no dither (the peak sits at the level, -5 dBFS at the loudest, so the clamp
// never acts).
function render(take, level) {
  const out = new Float64Array(Math.ceil(take.dur * RATE));
  for (const layer of take.layers) {
    if (layer.kind === "osc") renderOsc(layer, out);
    else if (layer.kind === "noise") renderNoise(layer, out);
    else throw new Error(`unknown layer kind '${layer.kind}'`);
  }
  if (take.tone) {
    const tone = new Param(350);
    tone.setValueAtTime(take.tone, 0);
    filter(out, "lowpass", tone, 1);
  }
  let peak = 0;
  for (let k = 0; k < out.length; k++) peak = Math.max(peak, Math.abs(out[k]));
  const target = Math.pow(10, level / 20);
  const scale = peak > 0 ? target / peak : 1;
  const samples = new Int16Array(out.length);
  for (let k = 0; k < out.length; k++) {
    out[k] *= scale;
    samples[k] = Math.max(-32768, Math.min(32767, Math.round(out[k] * 32767)));
  }
  return { samples, signal: out };
}

// What a set of 16-bit samples measures, as the tests read a WAV: its peak and
// RMS in dBFS (full scale 32767) and its length in ms, rounded.
function measure(samples) {
  let peak = 0, sum = 0;
  for (let k = 0; k < samples.length; k++) {
    const x = samples[k] / 32767;
    peak = Math.max(peak, Math.abs(x));
    sum += x * x;
  }
  const rms = Math.sqrt(sum / Math.max(1, samples.length));
  return {
    peakDb: 20 * Math.log10(peak || 1e-9),
    rmsDb: 20 * Math.log10(rms || 1e-9),
    ms: Math.round((samples.length * 1000) / RATE),
  };
}

module.exports = { RATE, render, measure, frameAtOrAfter, Param, envelope, glide, noiseBuffer, coefficients, biquadCoefficients };
