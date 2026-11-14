// The sprite engine: a sprite is a list of shapes (ellipses, tapered strokes,
// polygons), turned into pixels, then shaded and outlined by one set of rules,
// so every species comes out in the same hand. Colours come in ramps of five
// (outline, shadow, base, light, highlight) made from one base colour.
//
// Moved here from the games folder's pixel-art/generator (A28) with what it
// draws unchanged: the PNG writer is its own file now (png.js) and is
// re-exported below, and the contact sheets' label font gained digits and a few
// marks (a frame's name has them). Neither touches a sprite's pixels.
const { writePng } = require("./png");

// ---- Colour ----
function hexToRgb(c) {
  return [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)];
}
function rgbToHex([r, g, b]) {
  return "#" + [r, g, b].map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, "0")).join("");
}
function hexToHsl(c) {
  const [r, g, b] = hexToRgb(c).map((v) => v / 255);
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h;
  if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) * 60;
  else if (max === g) h = ((b - r) / d + 2) * 60;
  else h = ((r - g) / d + 4) * 60;
  return [h, s, l];
}
function hslToHex(h, s, l) {
  h = ((h % 360) + 360) % 360;
  s = Math.max(0, Math.min(1, s));
  l = Math.max(0, Math.min(1, l));
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return rgbToHex([(r + m) * 255, (g + m) * 255, (b + m) * 255]);
}
// Moves a hue toward a target by up to "by" degrees, the short way round.
function toward(h, target, by) {
  const diff = ((target - h + 540) % 360) - 180;
  return h + Math.max(-by, Math.min(by, diff));
}
// Five tones from one colour: shadows lean to blue, lights lean to yellow.
function ramp(base) {
  const [h, s, l] = hexToHsl(base);
  return {
    outline: hslToHex(toward(h, 255, 16), Math.min(1, s * 1.05), l * 0.3),
    shadow: hslToHex(toward(h, 255, 9), Math.min(1, s * 1.05), l * 0.66),
    base,
    light: hslToHex(toward(h, 58, 6), s * 0.97, Math.min(0.9, l * 1.13 + 0.015)),
    highlight: hslToHex(toward(h, 58, 12), s * 0.9, Math.min(0.93, l * 1.27 + 0.04))
  };
}
function darker(r, k) {
  const out = {};
  for (const key of Object.keys(r)) {
    const [h, s, l] = hexToHsl(r[key]);
    out[key] = hslToHex(toward(h, 255, 6), s, l * k);
  }
  return out;
}

// ---- Shapes ----
const ellipse = (cx, cy, rx, ry, more) => ({ kind: "ellipse", cx, cy, rx, ry, ...(more || {}) });
const stroke = (points, more) => ({ kind: "stroke", points, ...(more || {}) });
const poly = (points, more) => ({ kind: "poly", points, ...(more || {}) });

function covers(part, x, y) {
  if (part.kind === "ellipse") {
    const dx = (x - part.cx) / part.rx;
    const dy = (y - part.cy) / part.ry;
    return dx * dx + dy * dy <= 1;
  }
  if (part.kind === "poly") {
    let inside = false;
    const p = part.points;
    for (let i = 0, j = p.length - 1; i < p.length; j = i++) {
      if (p[i][1] > y !== p[j][1] > y && x < ((p[j][0] - p[i][0]) * (y - p[i][1])) / (p[j][1] - p[i][1]) + p[i][0]) inside = !inside;
    }
    return inside;
  }
  const pts = part.points;
  for (let i = 1; i < pts.length; i++) {
    const [ax, ay, ar] = pts[i - 1];
    const [bx, by, br] = pts[i];
    const vx = bx - ax;
    const vy = by - ay;
    const len = vx * vx + vy * vy;
    const t = len ? Math.max(0, Math.min(1, ((x - ax) * vx + (y - ay) * vy) / len)) : 0;
    const r = ar + (br - ar) * t;
    const px = ax + vx * t - x;
    const py = ay + vy * t - y;
    if (px * px + py * py <= r * r) return true;
  }
  return false;
}

// ---- A sprite ----
// spec: { w, h, ramps: { body, belly, ... }, parts, ridges, marks, extras }
// A part may say: ramp (name), far (a limb behind the body), limb (shade across,
// not down), belly (its lower part takes the belly colour), sep (keep a soft
// line where it lies over the part beneath).
function sprite(spec) {
  const { w, h, parts } = spec;
  const ramps = {};
  for (const name of Object.keys(spec.ramps)) ramps[name] = typeof spec.ramps[name] === "string" ? ramp(spec.ramps[name]) : spec.ramps[name];
  const farRamps = {};
  for (const name of Object.keys(ramps)) farRamps[name] = darker(ramps[name], 0.7);
  const top = new Array(w * h).fill(-1);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      for (let k = 0; k < parts.length; k++) if (covers(parts[k], x + 0.5, y + 0.5)) top[y * w + x] = k;
    }
  }
  const at = (x, y) => (x < 0 || y < 0 || x >= w || y >= h ? -1 : top[y * w + x]);
  const partAt = (x, y) => (at(x, y) < 0 ? null : parts[at(x, y)]);
  const keyOf = (x, y) => {
    const k = at(x, y);
    if (k < 0) return "";
    const p = parts[k];
    return (p.ramp || "body") + (p.far ? ":far" : "") + (p.limb ? ":limb" + k : "");
  };
  const out = new Array(w * h).fill(null);
  const toneAt = new Array(w * h).fill("");
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const k = at(x, y);
      if (k < 0) continue;
      const part = parts[k];
      const name = part.ramp || "body";
      const colours = part.far ? farRamps[name] : ramps[name];
      const near = [[x - 1, y], [x + 1, y], [x, y - 1], [x, y + 1]];
      let tone;
      if (part.thin) tone = part.thin === true ? "base" : part.thin;
      else if (near.some(([nx, ny]) => at(nx, ny) < 0)) tone = "outline";
      else if (!part.far && near.some(([nx, ny]) => partAt(nx, ny).far)) tone = "outline";
      else if (part.sep && near.some(([nx, ny]) => at(nx, ny) !== k && at(nx, ny) < k && !partAt(nx, ny).far && (partAt(nx, ny).ramp || "body") !== name)) tone = "outline";
      else if (part.sep && near.some(([nx, ny]) => at(nx, ny) !== k && at(nx, ny) < k && !partAt(nx, ny).far)) tone = "shadow";
      else if (part.far) tone = "base";
      else if (part.limb) {
        let left = x;
        let right = x;
        while (at(left - 1, y) === k) left--;
        while (at(right + 1, y) === k) right++;
        const run = right - left + 1;
        const s = run > 1 ? (x - left) / (run - 1) : 0.5;
        tone = run <= 2 ? "base" : s < 0.34 ? "light" : s > 0.7 ? "shadow" : "base";
      } else {
        const key = keyOf(x, y);
        let a = y;
        let b = y;
        while (keyOf(x, a - 1) === key) a--;
        while (keyOf(x, b + 1) === key) b++;
        const run = b - a + 1;
        const pos = y - a;
        const t = run > 1 ? pos / (run - 1) : 0.5;
        if (run <= 2) tone = "base";
        else if (pos === 1 && run >= 6) tone = "highlight";
        else if (t <= 0.42) tone = "light";
        else if (t >= 0.82 && run >= 5) tone = "shadow";
        else tone = "base";
        // The underside: the two or three rows above the bottom outline.
        if (part.belly && ramps.belly && run >= 6 && pos < run - 1 && pos >= run - (run >= 9 ? 4 : 3)) tone = pos === run - 2 ? "bellyShadow" : "belly";
      }
      toneAt[y * w + x] = tone;
      out[y * w + x] = tone === "belly" ? ramps.belly.base : tone === "bellyShadow" ? ramps.belly.shadow : colours[tone];
    }
  }
  // Marks sit on the skin only: never on an outline, and only where the pixel
  // is the ramp they were meant for.
  for (const mark of spec.marks || []) {
    const [x, y, tone] = mark;
    const k = at(x, y);
    if (k < 0 || parts[k].far) continue;
    const name = parts[k].ramp || "body";
    if (name !== (mark[3] || "body")) continue;
    const current = toneAt[y * w + x];
    if (current === "outline" || (current.startsWith("belly") && !mark[4])) continue;
    out[y * w + x] = tone.startsWith("#") ? tone : ramps[name][tone];
  }
  // Stripes hang from the top edge of the back, wherever it is in that column.
  for (const s of spec.stripes || []) {
    let y = 0;
    while (y < h && (at(s.x, y) < 0 || parts[at(s.x, y)].far || (parts[at(s.x, y)].ramp || "body") !== (s.ramp || "body"))) y++;
    for (let i = 1 + (s.skip || 0); i <= (s.len || 2) + (s.skip || 0); i++) {
      const yy = y + i;
      const xx = s.x + Math.round((s.slant || 0) * i);
      if (yy >= h || at(xx, yy) < 0) break;
      const p = parts[at(xx, yy)];
      const current = toneAt[yy * w + xx];
      if (p.far || (p.ramp || "body") !== (s.ramp || "body") || current === "outline" || current.startsWith("belly")) continue;
      out[yy * w + xx] = (s.tone || "shadow").startsWith("#") ? s.tone : ramps[s.ramp || "body"][s.tone || "shadow"];
    }
  }
  // Tips (claws, teeth) land only on the sprite itself.
  for (const [x, y, colour] of spec.tips || []) {
    if (at(x, y) >= 0) out[y * w + x] = colour;
  }
  // Ridges: one-pixel bumps standing on the top edge (scutes, knobs).
  for (const ridge of spec.ridges || []) {
    for (let x = ridge.from; x <= ridge.to; x += ridge.step || 2) {
      let y = 0;
      while (y < h && (at(x, y) < 0 || parts[at(x, y)].far)) y++;
      if (y > 0 && y < h && at(x, y - 1) < 0) out[(y - 1) * w + x] = ridge.colour;
    }
  }
  for (const [x, y, colour] of spec.extras || []) {
    if (x < 0 || y < 0 || x >= w || y >= h) continue;
    out[y * w + x] = colour || null;
  }
  const img = image(w, h);
  out.forEach((c, i) => {
    if (c) img.px[i] = [...hexToRgb(c), 255];
  });
  img.inside = (x, y) => at(x, y) >= 0;
  return img;
}

// ---- Images ----
function image(w, h, fill) {
  return { w, h, px: Array.from({ length: w * h }, () => (fill ? [...hexToRgb(fill), 255] : [0, 0, 0, 0])) };
}
function blit(dst, src, ox, oy, scale) {
  for (let y = 0; y < src.h; y++) {
    for (let x = 0; x < src.w; x++) {
      const p = src.px[y * src.w + x];
      if (p[3] === 0) continue;
      for (let dy = 0; dy < scale; dy++) {
        for (let dx = 0; dx < scale; dx++) {
          const tx = ox + x * scale + dx;
          const ty = oy + y * scale + dy;
          if (tx >= 0 && ty >= 0 && tx < dst.w && ty < dst.h) dst.px[ty * dst.w + tx] = p;
        }
      }
    }
  }
}
const FONT = {
  A: "010101111101101", B: "110101110101110", C: "011100100100011", D: "110101101101110", E: "111100110100111", F: "111100110100100",
  G: "011100101101011", H: "101101111101101", I: "111010010010111", J: "001001001101010", K: "101101110101101", L: "100100100100111",
  M: "101111111101101", N: "110101101101101", O: "010101101101010", P: "110101110100100", Q: "010101101110011", R: "110101110101101",
  S: "011100010001110", T: "111010010010010", U: "101101101101111", V: "101101101101010", W: "101101111111101", X: "101101010101101",
  Y: "101101010010010", Z: "111001010100111", " ": "000000000000000",
  // Added for frame names (Dryosaurus/walk/2): digits and a few marks. A name
  // is drawn in capitals, so case is lost on a sheet and only there.
  0: "111101101101111", 1: "010110010010111", 2: "111001111100111", 3: "111001111001111", 4: "101101111001001",
  5: "111100111001111", 6: "111100111101111", 7: "111001010010010", 8: "111101111101111", 9: "111101111001111",
  "/": "001001010100100", "-": "000000111000000", "_": "000000000000111", ".": "000000000000010"
};
function text(dst, str, ox, oy, scale, colour) {
  const rgb = [...hexToRgb(colour), 255];
  [...str.toUpperCase()].forEach((ch, i) => {
    const bits = FONT[ch] || FONT[" "];
    for (let k = 0; k < 15; k++) {
      if (bits[k] !== "1") continue;
      const x = ox + (i * 4 + (k % 3)) * scale;
      const y = oy + Math.floor(k / 3) * scale;
      for (let dy = 0; dy < scale; dy++) for (let dx = 0; dx < scale; dx++) if (x + dx < dst.w && y + dy < dst.h) dst.px[(y + dy) * dst.w + x + dx] = rgb;
    }
  });
}
function textWidth(str, scale) {
  return str.length * 4 * scale - scale;
}

module.exports = { ramp, darker, ellipse, stroke, poly, sprite, image, blit, text, textWidth, writePng, hexToHsl, hslToHex };
