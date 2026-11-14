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

// ---- Sizes, ages and poses ----
// place(spec, look) returns the same sprite spec moved onto another canvas: at
// another SIZE, at another AGE, or in another POSE. It moves the shapes, before
// they become pixels; it never resamples a picture. So a smaller sprite is drawn
// by sprite() like any other: its outline is still one pixel where it meets
// nothing, its eye is still one pixel, its shading follows the same rule (task
// H11: a hatchling is drawn, not shrunk).
//
// What a part may carry for it (sprite() ignores all of these):
//   bone: what the part moves with: "body" (the default), "head", "snout",
//         "neck" and "tail" (graded along their points, from the body's
//         offset at the first point to the head's or the tail's at the last),
//         "tailEnd" (all of the tail's offset), "ground" (stays on the ground
//         line) and "swing" (a lifted foot). A stroke's point may name its
//         own bone as a fourth element, [x, y, r, "ground"], so a leg's top
//         rides the body while its foot stays planted.
//   along: for a part that sits ON a graded part (a spike on the tail), its
//         place along it, 0 to 1, so it moves as that place does.
//   orn:  an ornament (a horn, a plate, a spike, a crest, a frill, a claw),
//         which a young animal has smaller: true shrinks it toward its base
//         (a stroke's first point; a polygon's two lowest points; an
//         ellipse's bottom), or [x, y] toward that point (a group that shrinks
//         as one: a frill with the horns on its rim).
//
// look:
//   w, h          the new canvas
//   from, to      a point of the old canvas and where it lands (the feet: the
//                 pivot's ground point)
//   scale         how much smaller (1 keeps the size)
//   head          [cx, cy]: the head's centre, about which it grows
//   grow          { head, snout, neck, orn, spike }: a young animal's head
//                 (k > 1: larger), its snout's length (k < 1: shorter, on top
//                 of the head's k), its neck's thickness (k > 1), its plates,
//                 crests, frills and domes (orn: the ornaments drawn as
//                 polygons and ellipses) and its horns, spikes and claws
//                 (spike: those drawn as strokes; missing: orn's), each k < 1:
//                 smaller. A young animal keeps more of the plates that make
//                 its kind's outline than of its horns. Missing: 1.
//   offsets       { body, head, tail, swing }: [dx, dy] in NEW pixels, the
//                 pose: the body's bob, the head's nod, the tail tip's sway, a
//                 lifted foot. Whole pixels keep a shape's edge crisp.
//   pitch         { about: [x, y], angle }: the whole animal (every bone but
//                 "ground" and "swing") turned about a point of the OLD canvas
//                 (the hip), angle in radians, positive nose down: a biped
//                 bending to the ground to eat, its tail rising as its head
//                 falls (task H12). A leg's foot stays planted, its top goes
//                 with the body, so the leg bends instead of the foot sliding.
//                 `tail` (0 to 1, optional): the tail's share of the turn.
//   nod           { about: [x, y], angle }: the head and the snout (and what
//                 rides them: horns, frills, crests) turned about a point of
//                 the old canvas (the back of the skull), positive nose down: a
//                 horned dino lowering its horns, a Pachycephalosaurus putting
//                 its dome forward. Turned before the pitch.
//                 An ellipse turns its centre only (its axes stay level); at
//                 these sizes that is all a turn of a head can show.
//   keepEyes      true: the tagged eye is kept off the head's edge at full size
//                 too (a turned head can carry it onto the outline). Sizes
//                 under 1 always keep it.
//
// Marks, tips and extras (pixels, not shapes) go where the part under them
// went; stripes and ridges are spaced for the new size. An extra tagged "eye"
// as its fourth element is kept off the edge of a smaller head (an eye on the
// outline is no eye), and one tagged "pupil" stays beside its eye.
//
// Radii shrink with the scale, but never below the smaller of their own size
// and a half pixel (a stroke thinner than that breaks into dots): so a limb
// stays whole at 20 x 14 and nothing at full size gets thicker.
const MIN_STROKE = 0.5;
const MIN_ELLIPSE = 0.6;
// The skull (a part tagged `skull`) at a smaller size: its radii never under
// this, so it holds a row of skin round its eye.
const MIN_SKULL = 1.9;
function place(spec, look) {
  const s = look.scale === undefined ? 1 : look.scale;
  const [fx, fy] = look.from;
  const [tx, ty] = look.to;
  const same = s === 1 && fx === tx && fy === ty;
  const grow = look.grow || {};
  const kHead = grow.head || 1;
  const kSnout = grow.snout || 1;
  const kOrn = grow.orn || 1;
  const kSpike = grow.spike || kOrn;
  const kNeck = grow.neck || 1;
  const hc = look.head || [0, 0];
  const off = look.offsets || {};
  const at = (name) => off[name] || [0, 0];
  const lerp = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
  // The head's and the tail tip's offsets are on top of the body's.
  const body = at("body");
  const head = [body[0] + at("head")[0], body[1] + at("head")[1]];
  const tailTip = [body[0] + at("tail")[0], body[1] + at("tail")[1]];
  // The pose's offset of a bone, t the point's place along a graded part (0 at its first point, 1 at its last).
  const offsetOf = (bone, t) => {
    switch (bone) {
      case "head":
      case "snout":
        return head;
      case "neck":
        return lerp(body, head, t);
      case "tail":
        return lerp(body, tailTip, t);
      case "tailEnd":
        return tailTip;
      case "ground":
        return [0, 0];
      case "swing":
        return at("swing");
      default:
        return body;
    }
  };
  const anchorOf = (part) => {
    if (Array.isArray(part.orn)) return part.orn;
    if (part.kind === "stroke") return [part.points[0][0], part.points[0][1]];
    if (part.kind === "ellipse") return [part.cx, part.cy + part.ry];
    const low = [...part.points].sort((a, b) => b[1] - a[1]).slice(0, 2);
    return [(low[0][0] + low[1][0]) / 2, (low[0][1] + low[1][1]) / 2];
  };
  // Age. A head part grows about the head's centre (a snout's length less).
  // An ornament keeps its base where the part it stands on put it, and its own
  // size is its kind's k alone: a horn on a hatchling's large head is still a
  // stub (with the head's growth on top it was most of an adult's horn).
  const growsHead = (bone) => (bone === "head" || bone === "snout") && kHead !== 1;
  const grown = (x, y, bone) =>
    growsHead(bone) ? [hc[0] + (x - hc[0]) * kHead * (bone === "snout" ? kSnout : 1), hc[1] + (y - hc[1]) * kHead] : [x, y];
  const ornK = (part) => (part.kind === "stroke" ? kSpike : kOrn);
  const aged = (part, x, y, bone) => {
    if (part.orn && (ornK(part) !== 1 || growsHead(bone))) {
      const [ax, ay] = anchorOf(part);
      const [gx, gy] = grown(ax, ay, bone);
      return [gx + (x - ax) * ornK(part), gy + (y - ay) * ornK(part)];
    }
    return grown(x, y, bone);
  };
  const toCanvas = (x, y) => (same ? [x, y] : [tx + (x - fx) * s, ty + (y - fy) * s]);
  // The turns (task H12), in the old canvas's units, after the age and before
  // the size: the nod for the head's bones, then the pitch for every bone but
  // the ground's. No turn asked, no number changes (a frame with neither is
  // drawn exactly as before).
  const turnAbout = (x, y, turn) => {
    const c = Math.cos(turn.angle);
    const sn = Math.sin(turn.angle);
    const [ox, oy] = turn.about;
    return [ox + (x - ox) * c - (y - oy) * sn, oy + (x - ox) * sn + (y - oy) * c];
  };
  const turned = (x, y, bone) => {
    let p = [x, y];
    if (look.nod && (bone === "head" || bone === "snout")) p = turnAbout(p[0], p[1], look.nod);
    if (look.pitch && bone !== "ground" && bone !== "swing") {
      // The tail turns by its own share of the pitch (`tail`, 1 when not
      // given): a bending animal's tail rises, but less than its head falls.
      const k = (bone === "tail" || bone === "tailEnd") && look.pitch.tail !== undefined ? look.pitch.tail : 1;
      p = turnAbout(p[0], p[1], { about: look.pitch.about, angle: look.pitch.angle * k });
    }
    return p;
  };
  const mapPoint = (part, x, y, bone, t) => {
    let [ax, ay] = aged(part, x, y, bone);
    if (look.nod || look.pitch) [ax, ay] = turned(ax, ay, bone);
    const [cx, cy] = toCanvas(ax, ay);
    let [dx, dy] = offsetOf(bone, t);
    // A part fixed at one place on a graded part (a spike on the tail) moves
    // by whole pixels, all of it together: moved by a fraction it is drawn
    // anew each frame, a thin spike a different shape every time, and flickers.
    if (part.along !== undefined) [dx, dy] = [Math.round(dx), Math.round(dy)];
    return [cx + dx, cy + dy];
  };
  const radiusOf = (part, r, bone, min) => {
    // An ornament's thickness is its kind's (a frill's size shrinks; a horn's
    // width does not grow with the head); a head part's grows with the head; a
    // young neck is a little thicker for its body.
    let k = s;
    if (part.orn) k *= part.kind === "ellipse" ? kOrn : 1;
    else if (growsHead(bone)) k *= kHead;
    else if (bone === "neck") k *= kNeck;
    return k === 1 ? r : Math.max(r * k, Math.min(r, min));
  };
  const placePart = (part) => {
    const bone = part.bone || "body";
    if (part.kind === "ellipse") {
      const [cx, cy] = mapPoint(part, part.cx, part.cy, bone, part.along !== undefined ? part.along : 1);
      let rx = radiusOf(part, part.rx, bone, MIN_ELLIPSE);
      let ry = radiusOf(part, part.ry, bone, MIN_ELLIPSE);
      // A smaller skull keeps room for an eye inside its outline: a head two
      // or three pixels tall is all outline, and its eye a dent in it (the
      // small-headed Beipiaosaurus, Gallimimus and Kentrosaurus, Nov 14).
      if (part.skull && s < 1) {
        rx = Math.max(rx, MIN_SKULL);
        ry = Math.max(ry, MIN_SKULL);
      }
      return { ...part, cx, cy, rx, ry };
    }
    const n = part.points.length;
    const points = part.points.map((p, i) => {
      const b = p[3] || bone;
      const [x, y] = mapPoint(part, p[0], p[1], b, part.along !== undefined ? part.along : n > 1 ? i / (n - 1) : 0);
      return part.kind === "stroke" ? [x, y, radiusOf(part, p[2], b, MIN_STROKE), ...p.slice(3)] : [x, y, ...p.slice(2)];
    });
    return { ...part, points };
  };
  const parts = spec.parts.map(placePart);

  // A pixel of the old canvas goes where the part under it went (the topmost,
  // as sprite() decides it); a point of a stroke takes the bone and place of
  // the stroke's nearest point.
  // A pixel just beside the sprite (a claw at a toe's end) goes with the part
  // it touches: with the body's bob it would leave the planted foot behind.
  const topAt = (x, y) => {
    let found = -1;
    for (let k = 0; k < spec.parts.length; k++) if (covers(spec.parts[k], x + 0.5, y + 0.5)) found = k;
    return found;
  };
  const under = (x, y) => {
    for (const [dx, dy] of [[0, 0], [-1, 0], [0, -1], [1, 0], [0, 1]]) {
      const k = topAt(x + dx, y + dy);
      if (k >= 0) return k;
    }
    return -1;
  };
  const movePixel = (x, y) => {
    const k = under(x, y);
    const part = k < 0 ? { kind: "ellipse" } : spec.parts[k];
    let bone = part.bone || "body";
    let t = part.along !== undefined ? part.along : 1;
    if (part.points && part.kind === "stroke" && part.along === undefined) {
      let best = 0;
      part.points.forEach((p, i) => {
        const q = part.points[best];
        if ((p[0] - x - 0.5) ** 2 + (p[1] - y - 0.5) ** 2 < (q[0] - x - 0.5) ** 2 + (q[1] - y - 0.5) ** 2) best = i;
      });
      bone = part.points[best][3] || bone;
      t = part.points.length > 1 ? best / (part.points.length - 1) : 0;
    }
    const [nx, ny] = mapPoint(part, x + 0.5, y + 0.5, bone, t);
    return [Math.floor(nx), Math.floor(ny)];
  };
  const movePixels = (list) => list.map((m) => [...movePixel(m[0], m[1]), ...m.slice(2)]);

  // Stripes and ridges are columns of the body: placed by the canvas and the
  // body's bob, two pixels apart at least so a small body is not a dark mass.
  const column = (x) => Math.floor(toCanvas(x + 0.5, 0)[0] + body[0]);
  const stripes = [];
  for (const st of [...(spec.stripes || [])].sort((a, b) => a.x - b.x)) {
    const x = column(st.x);
    if (stripes.length && x - stripes[stripes.length - 1].x < 2) continue;
    stripes.push({ ...st, x, len: Math.max(1, Math.round((st.len || 2) * s)), skip: Math.round((st.skip || 0) * s) });
  }
  const ridges = (spec.ridges || []).map((r) => ({ ...r, from: column(r.from), to: column(r.to), step: Math.max(2, Math.round((r.step || 2) * s)) }));

  let extras = movePixels(spec.extras || []);
  if (s < 1 || look.keepEyes) extras = keepEyesInside(extras, parts, look.w, look.h, mapPoint({ kind: "ellipse" }, hc[0], hc[1], "head", 0));
  return {
    ...spec,
    w: look.w,
    h: look.h,
    parts,
    marks: movePixels(spec.marks || []),
    tips: movePixels(spec.tips || []),
    stripes,
    ridges,
    extras
  };
}

// An eye on a smaller head can land on its edge, where it would read as a dent
// in the outline: move it (and its pupil with it) a pixel at a time toward the
// head's centre until every pixel beside it is the sprite's own.
function keepEyesInside(extras, parts, w, h, centre) {
  const inside = (x, y) => x >= 0 && y >= 0 && x < w && y < h && parts.some((p) => covers(p, x + 0.5, y + 0.5));
  const clear = (x, y) => inside(x, y) && inside(x - 1, y) && inside(x + 1, y) && inside(x, y - 1) && inside(x, y + 1);
  const eye = extras.find((e) => e[3] === "eye");
  if (!eye) return extras;
  const pupil = extras.find((e) => e[3] === "pupil");
  let [x, y] = eye;
  for (let tries = 0; tries < 3 && !(clear(x, y) && (!pupil || clear(x + 1, y))); tries++) {
    const dy = Math.sign(Math.floor(centre[1]) - y);
    const dx = Math.sign(Math.floor(centre[0]) - x);
    if (dy !== 0) y += dy;
    else if (dx !== 0) x += dx;
    else break;
  }
  return extras.map((e) => (e === eye ? [x, y, ...e.slice(2)] : e === pupil ? [x + 1, y, ...e.slice(2)] : e));
}

// ---- Images ----
function image(w, h, fill) {
  return { w, h, px: Array.from({ length: w * h }, () => (fill ? [...hexToRgb(fill), 255] : [0, 0, 0, 0])) };
}

// The flash of a hit (task H12, PLAN-HATCHLING 6.3: `hurt/0`): a finished
// sprite with everything inside its outline taken most of the way to `to`
// (near white), the outline itself left as it is. The outline is what a
// sprite has where it meets nothing: an opaque pixel with an empty pixel (or
// the frame's edge) on one of its four sides, as sprite() decides it. So the
// animal keeps its silhouette, dark-edged, on every ground, and is lit from
// inside for the beat of the hit: "it bit you" before a word is read. The
// lines inside it (a near leg over the body, a plate's edge) go light grey,
// lighter than the edge and darker than the flash, so the shape stays one
// animal. Works on any image, so every sprite flashes by the one rule.
function flash(img, to, k) {
  const target = hexToRgb(to);
  const solid = (x, y) => x >= 0 && y >= 0 && x < img.w && y < img.h && img.px[y * img.w + x][3] > 0;
  const out = { w: img.w, h: img.h, px: img.px.map((p) => [...p]) };
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      if (!solid(x, y)) continue;
      if (!solid(x - 1, y) || !solid(x + 1, y) || !solid(x, y - 1) || !solid(x, y + 1)) continue;
      const p = img.px[y * img.w + x];
      out.px[y * img.w + x] = [0, 1, 2].map((i) => Math.round(p[i] + (target[i] - p[i]) * k)).concat([p[3]]);
    }
  }
  return out;
}

// A colour as one pixel: "#RRGGBB" is opaque, "#RRGGBBAA" carries its alpha
// (straight, as a PNG keeps it; the game's atlas premultiplies at load). The
// one translucent thing the engine draws is source-over, which the game's
// PixelBuffer has too, so a translucent pixel is no effect the game lacks.
function pixel(c) {
  return [...hexToRgb(c), c.length === 9 ? parseInt(c.slice(7, 9), 16) : 255];
}

// ---- Grids ----
// Small pieces drawn pixel by pixel: digits, the marks above a head. A grid is
// rows of characters, each mapped to a colour by `key` ("." and " " are
// nothing). It is the earlier sample's way of drawing its 12 px icons
// (pixel-art/earlier-sample), brought into the one engine as ART-BRIEF asks:
// at 5 x 7 there are no shapes to shade, only pixels to place, so a grid is the
// honest way to draw them, and outline() below gives them the same dark edge a
// sprite gets where it meets nothing. Added for task H10; it draws nothing a
// sprite() draws, so no existing sprite moves.
function drawn(rows, key) {
  const h = rows.length;
  const w = Math.max(...rows.map((r) => r.length));
  const img = image(w, h);
  rows.forEach((row, y) =>
    [...row].forEach((ch, x) => {
      const c = key[ch];
      if (c) img.px[y * w + x] = pixel(c);
    })
  );
  return img;
}

// Every empty pixel that touches an opaque one becomes `colour`: a one-pixel
// outline round a grid's drawing. `corners` counts the four diagonal
// neighbours too, so a stroke's ends and a diagonal step are closed as well
// (what a glyph needs to read over any ground); without it only the four
// sides count. Stays inside the image: leave a pixel of room round the drawing.
function outline(img, colour, corners) {
  const out = { w: img.w, h: img.h, px: img.px.map((p) => [...p]) };
  const solid = (x, y) => x >= 0 && y >= 0 && x < img.w && y < img.h && img.px[y * img.w + x][3] > 0;
  const c = pixel(colour);
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      if (solid(x, y)) continue;
      let touch = solid(x - 1, y) || solid(x + 1, y) || solid(x, y - 1) || solid(x, y + 1);
      if (!touch && corners) touch = solid(x - 1, y - 1) || solid(x + 1, y - 1) || solid(x - 1, y + 1) || solid(x + 1, y + 1);
      if (touch) out.px[y * img.w + x] = [...c];
    }
  }
  return out;
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

module.exports = { ramp, darker, ellipse, stroke, poly, sprite, place, covers, image, flash, pixel, drawn, outline, blit, text, textWidth, writePng, hexToHsl, hslToHex, hexToRgb, rgbToHex };
