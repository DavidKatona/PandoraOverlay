// The 21 species of the server, each an original sprite facing right on a
// 36 x 25 canvas. Shapes follow the real animals' build (what a field guide
// would show), not any game's art.
//
// Task H11 (PLAN-HATCHLING 6.2, H21): every species is also drawn at three
// smaller sizes, younger (a hatchling's head and eye a little large, its horns,
// plates, crests and claws short), and walking. Both come from the SAME shapes:
// a species' description takes a gait (where each foot is in the stride) and
// tags its parts with the bone they move with (`bone`) and whether they are an
// ornament a young animal has smaller (`orn`); the engine's place() then moves
// the shapes to the size, the age and the pose before they become pixels. The
// adult standing frame is the description with no gait and no place(): exactly
// the 36 x 25 sprite it always was.
const { ellipse, stroke, poly, sprite, place, flash } = require("../engine");

const W = 36;
const H = 25;
const G = 24.4; // the ground
const BONE = "#E4D9BC";
const TOOTH = "#F4EEDC";
const DARK_EYE = "#14140E";

// ---- Gaits ----
// A step is where one foot is: dx along the ground (in the description's own
// units) and whether it is the foot in the air. Standing is every foot at 0 on
// the ground; adding 0 changes no number, so a standing description is the one
// the adult sprite was always drawn from.
const PLANTED = { dx: 0, swing: false };
const STAND = { near: PLANTED, far: PLANTED, foreNear: PLANTED, foreFar: PLANTED, wing: 0 };

// A thigh rides the body, the shin's lower end and the foot stay on the ground
// (or are the lifted foot): so the body's bob stretches the leg, and the foot
// never slides.
function leg(hx, hy, L, more, step) {
  const d = (step || PLANTED).dx;
  const foot = step && step.swing ? "swing" : "ground";
  const ankleX = hx + 0.5 + d;
  return [
    ellipse(hx + 0.4 + d * 0.2, hy + L.T * 0.5, L.w, L.T * 0.62 + 0.5, { ...more, sep: Boolean(more.sep) && L.w >= 3.2 }),
    stroke([[hx + 1.3 + d * 0.45, hy + L.T, L.w * 0.5], [ankleX, G - 2.7, 0.95, foot]], { ...more, limb: !more.far }),
    stroke([[ankleX + 0.1, G - 1.5, 0.95, foot], [ankleX + L.f, G - 1.1, 0.75, foot]], { ...more, limb: !more.far })
  ];
}

function armPts(x, y, a) {
  return [[x, y, a.w], [x + a.len * 0.5, y + a.len * 0.85, a.w * 0.7]];
}

// The dark line of a mouth along a snout, with teeth for the meat eaters.
function mouth(snout, teeth, from) {
  const [x0, y0, r0] = snout[0];
  const [x1, y1, r1] = snout[snout.length - 1];
  const marks = [];
  const tips = [];
  for (let x = Math.ceil(x0 + (from || 0)); x <= Math.floor(x1 - 0.6); x++) {
    const t = (x + 0.5 - x0) / (x1 - x0);
    const r = r0 + (r1 - r0) * t;
    if (r < 1.45) continue; // too slim a snout for a mouth: it would turn solid dark
    const y = Math.floor(y0 + (y1 - y0) * t + r * 0.3);
    marks.push([x, y, "shadow"]);
    if (teeth && (x - Math.ceil(x0)) % 2 === 1) tips.push([x, y, TOOTH]);
  }
  return { marks, tips };
}

// Open jaws (task H12: a strike's second frame, a crocodile's meal): the snout
// drawn as two jaws hinged at its first point, the upper lifted a little, the
// lower dropped more, by `open` at the tip (the description's units), so a
// wedge opens between them toward the tip: open jaws are a change of
// SILHOUETTE, the one thing that survives at 1x. Inside the wedge, the maw: a
// dark red no ground and no dino has, so the open mouth reads on the dark
// ground (where a gap alone showed nothing) and on the green ones; each jaw
// keeps a dark line where it lies over it (`sep`). A meat eater's teeth stand
// on the upper jaw's edge, every other pixel, and one at the lower jaw's tip.
// The jaws and the maw are bone "snout": they go wherever the head goes.
// `withMaw` false (a thin beak, which has no outline to keep a line with)
// leaves the wedge open to the ground.
function jaws(snout, open, more, teeth, withMaw) {
  const x0 = snout[0][0];
  const x1 = snout[snout.length - 1][0];
  const along = (x) => Math.max(0, Math.min(1, (x - x0) / (x1 - x0)));
  // The snout's middle line and thickness at x, along its points.
  const at = (x) => {
    for (let i = 1; i < snout.length; i++) {
      const [ax, ay, ar] = snout[i - 1];
      const [bx, by, br] = snout[i];
      if (x <= bx || i === snout.length - 1) {
        const t = bx === ax ? 0 : Math.max(0, Math.min(1, (x - ax) / (bx - ax)));
        return [ay + (by - ay) * t, ar + (br - ar) * t];
      }
    }
    return [snout[0][1], snout[0][2]];
  };
  const upperOf = (x, y, r) => [x, y - r * JAW.upperSit - open * JAW.upperLift * along(x), Math.max(0.5, r * JAW.upperR)];
  const lowerOf = (x, y, r) => [x, y + r * JAW.lowerSit + open * JAW.lowerDrop * along(x), Math.max(0.45, r * JAW.lowerR)];
  const upper = snout.map(([x, y, r]) => upperOf(x, y, r));
  const lower = snout.map(([x, y, r]) => lowerOf(x, y, r));
  const parts = [];
  if (withMaw !== false) {
    const [ty, tr] = at(x1);
    const [, uy, ur] = upperOf(x1, ty, tr);
    const [, ly, lr] = lowerOf(x1, ty, tr);
    const [hy] = at(x0);
    parts.push(poly([[x0 + 0.4, hy - 0.4], [x1 - 0.2, uy + ur * 0.4], [x1 - 0.6, ly - lr * 0.4], [x0 + 0.4, hy + 0.8]], { ramp: "maw", thin: "base", bone: "snout" }));
  }
  const jaw = { ...more, sep: withMaw !== false };
  parts.push(stroke(lower, jaw), stroke(upper, jaw));
  const tips = [];
  if (teeth) {
    let last = null;
    for (let x = Math.ceil(x0 + 1); x <= Math.floor(x1 - 0.4); x++) {
      const [y, r] = at(x + 0.5);
      const [, uy, ur] = upperOf(x + 0.5, y, r);
      const [, ly, lr] = lowerOf(x + 0.5, y, r);
      if (ly - lr - (uy + ur) < 0.8) continue; // no gap here yet: no teeth to show
      if ((x - Math.ceil(x0)) % 2 === 1) tips.push([x, Math.floor(uy + ur - 0.5), TOOTH]);
      last = [x, Math.floor(ly - lr + 0.5), TOOTH];
    }
    if (last) tips.push(last);
  }
  return { parts, tips, lift: (x) => -at(x)[1] * JAW.upperSit - open * JAW.upperLift * along(x) };
}
// How the jaws part: where each sits on the closed snout's line (a part of its
// thickness), its thickness (a part of the snout's), and its share of `open`.
const JAW = { upperSit: 0.25, upperR: 0.8, upperLift: 0.3, lowerSit: 0.45, lowerR: 0.62, lowerDrop: 0.7 };
// The inside of an open mouth.
const MAW = "#6A2228";

// A two-legged build: hip and chest set the body, the rest hangs off them.
// g is the gait: g.near and g.far the hind feet, g.foreNear and g.foreFar the
// hands of the builds that walk on them too (Tenontosaurus, Maiasaura).
function biped(o, g) {
  g = g || STAND;
  const L = { T: 5, w: 3.1, f: 3.2, ...(o.leg || {}) };
  const [hx, hy] = o.hip;
  const [cx, cy] = o.chest;
  const stance = o.stride === undefined ? 1.9 : o.stride;
  // Walking, the two feet take turns about one point under the hip: the far
  // leg's standing offset (drawn 1.9 behind) is split between them, or the one
  // contact frame is a straddle and the other a closed, crossed stance.
  const near = g.walking ? { ...g.near, dx: g.near.dx - stance / 2 } : g.near;
  const far = g.walking ? { ...g.far, dx: g.far.dx + stance / 2 } : g.far;
  const hand = (step) => (step.swing ? "swing" : "ground");
  const parts = [];
  parts.push(...leg(hx - stance, hy + 0.4, L, { far: true }, far));
  if (o.arm) parts.push(stroke(armPts(cx - 1.0, cy + 0.9, o.arm), { far: true }));
  if (o.foreleg) parts.push(stroke([[cx - 1.4, cy + 1.4, o.foreleg], [cx - 0.9 + g.foreFar.dx, G - 1.7, o.foreleg * 0.8, hand(g.foreFar)]], { far: true }));
  (o.behind || []).forEach((p) => parts.push(p));
  parts.push(stroke(o.tail, { bone: "tail" }));
  parts.push(stroke([[hx, hy, o.girth[0]], [cx, cy, o.girth[1]]], { belly: true }));
  parts.push(stroke(o.neck, { belly: true, bone: "neck" }));
  parts.push(ellipse(o.head[0], o.head[1], o.head[2], o.head[3], { bone: "head", skull: true }));
  const open = o.snout && g.jaw ? jaws(o.snout, g.jaw, { ramp: o.snoutRamp, bone: "snout" }, o.teeth) : null;
  if (open) parts.push(...open.parts);
  else if (o.snout) parts.push(stroke(o.snout, { ramp: o.snoutRamp, bone: "snout" }));
  (o.onTop || []).forEach((p) => parts.push(p));
  parts.push(...leg(hx, hy, L, { sep: true }, near));
  if (o.arm) parts.push(stroke(armPts(cx, cy + 1.1, o.arm), { limb: true, sep: true }));
  if (o.foreleg) {
    parts.push(stroke([[cx + 0.3, cy + 1.5, o.foreleg], [cx + 0.9 + g.foreNear.dx, G - 1.7, o.foreleg * 0.8, hand(g.foreNear)]], { limb: true, sep: true }));
    parts.push(ellipse(cx + 1.3 + g.foreNear.dx, G - 1.05, o.foreleg + 0.35, 0.95, { limb: true, bone: hand(g.foreNear) }));
  }
  (o.front || []).forEach((p) => parts.push(p));
  // Two rows under the top of the head, so a lighter row always sits above it.
  const eye = [Math.floor(o.head[0] + o.head[2] * 0.2), Math.ceil(o.head[1] - o.head[3] - 0.5) + 2];
  const tips = [[Math.floor(hx + 0.5 + L.f + 0.55 + near.dx), Math.floor(G - 1.1), BONE]];
  if (o.arm) tips.push([Math.floor(cx + o.arm.len * 0.5 + 0.35), Math.floor(cy + 1.1 + o.arm.len * 0.85 + 0.25), BONE]);
  const m = open ? { marks: [], tips: open.tips } : o.snout && o.mouth !== false ? mouth(o.snout, o.teeth, o.mouthFrom) : { marks: [], tips: [] };
  return { parts, eye, head: [o.head[0], o.head[1]], skull: o.head, hip: o.hip, tips: [...tips, ...m.tips], marks: m.marks };
}

// A four-legged build: rump and shoulder set the body. g.near and g.far are
// the hind feet, g.foreNear and g.foreFar the front ones.
function quad(o, g) {
  g = g || STAND;
  // Walking, a little of the far legs' standing offset (2.1 behind) is split
  // between the pairs, as the two-legged walk does with all of it; the rest
  // stays, so the far legs still read as the other side's.
  const share = (step, k) => (g.walking ? { ...step, dx: step.dx + k * QUAD_SHARE } : step);
  const parts = [];
  const post = (x, topY, w, more, step) => {
    const foot = step.swing ? "swing" : "ground";
    return [
      stroke([[x, topY, w], [x + (o.lean || 0) + step.dx, G - 2.0, w * 0.88, foot]], { ...more, limb: !more.far }),
      ellipse(x + (o.lean || 0) + 0.45 + step.dx, G - 1.1, w + 0.5, 1.0, { ...more, limb: !more.far, bone: foot })
    ];
  };
  parts.push(...post(o.hindX - 2.1, o.rump[1] + 1.5, o.legW[0], { far: true }, share(g.far, 1)));
  parts.push(...post(o.foreX - 2.1, o.shoulder[1] + 1.5, o.legW[1], { far: true }, share(g.foreFar, 1)));
  (o.behind || []).forEach((p) => parts.push(p));
  parts.push(stroke(o.tail, { bone: "tail" }));
  parts.push(stroke([o.rump, o.shoulder], { belly: true }));
  if (o.neck) parts.push(stroke(o.neck, { belly: true, bone: "neck" }));
  (o.mid || []).forEach((p) => parts.push(p));
  parts.push(ellipse(o.head[0], o.head[1], o.head[2], o.head[3], { bone: "head", skull: true }));
  const open = o.snout && g.jaw ? jaws(o.snout, g.jaw, { ramp: o.snoutRamp, bone: "snout" }, false) : null;
  if (open) parts.push(...open.parts);
  else if (o.snout) parts.push(stroke(o.snout, { ramp: o.snoutRamp, bone: "snout" }));
  (o.onTop || []).forEach((p) => parts.push(p));
  parts.push(...post(o.hindX, o.rump[1] + 1.2, o.legW[0], { sep: true }, share(g.near, -1)));
  parts.push(...post(o.foreX, o.shoulder[1] + 1.2, o.legW[1], { sep: true }, share(g.foreNear, -1)));
  (o.front || []).forEach((p) => parts.push(p));
  // Two rows under the top of the head, so a lighter row always sits above it.
  const eye = [Math.floor(o.head[0] + o.head[2] * 0.2), Math.ceil(o.head[1] - o.head[3] - 0.5) + 2];
  const m = open ? { marks: [], tips: [] } : o.snout && o.mouth !== false ? mouth(o.snout, false, o.mouthFrom) : { marks: [], tips: [] };
  return { parts, eye, head: [o.head[0], o.head[1]], skull: o.head, hip: [o.rump[0], o.rump[1]], tips: m.tips, marks: m.marks };
}

// The description as the engine takes it (a sprite spec), not yet pixels: the
// adult standing frame is sprite() of it as it is, every other frame sprite()
// of it after place(). The eye and its pupil are tagged so place() can keep
// them off the edge of a smaller head.
function finish(build, o) {
  const eyeAt = o.eye || build.eye;
  const extras = [[eyeAt[0], eyeAt[1], o.eyeColour || DARK_EYE, "eye"]];
  if (o.pupil) extras.push([eyeAt[0] + 1, eyeAt[1], "#1A0E0A", "pupil"]);
  return {
    w: W,
    h: H,
    head: build.head,
    skull: build.skull,
    hip: build.hip,
    ramps: { bone: BONE, maw: MAW, ...o.ramps },
    parts: build.parts,
    stripes: o.stripes || [],
    marks: [...build.marks, ...(o.marks || [])],
    ridges: o.ridges || [],
    tips: [...build.tips, ...(o.tips || [])],
    extras: [...extras, ...(o.extras || [])]
  };
}

// The young Deinosuchus' trunk (H61): how much deeper its strokes are drawn
// and how far (in the description's units) it is carried up, by size.
const YOUNG_CROC = { "-s": { deep: 1.5, lift: 0.9 }, "-m": { deep: 1.25, lift: 0.5 } };

// A frill and the horns on its rim shrink as one, toward the frill's foot.
const DIABLO_FRILL = [21.2, 14.9];
const TRIKE_FRILL = [22.0, 15.0];

const stripes = (xs, len, more) => xs.map((x) => ({ x, len, ...(more || {}) }));
const AMBER = "#FFC864";
// The Pteranodon's beak, closed (its strike opens it: jaws()).
const BEAK = [[27.0, 11.8, 1.3], [34.2, 13.4, 0.45]];

const SPECIES = {
  Dryosaurus: (g) =>
    finish(
      biped({
        hip: [14, 13], chest: [20, 12.5], girth: [3.9, 3.2],
        tail: [[12, 12.6, 3.0], [7, 11.3, 1.9], [2.5, 9.8, 0.9], [0.6, 9.2, 0.5]],
        neck: [[21.5, 11.6, 2.5], [24.2, 8.6, 1.8], [25.8, 6.8, 1.6]],
        head: [27.6, 6.0, 3.0, 2.2], snout: [[28.8, 6.3, 1.7], [32.4, 7.0, 0.95]],
        leg: { T: 5, w: 3.0, f: 3.2 }, arm: { len: 3.2, w: 0.9 }
      }, g),
      { ramps: { body: "#5E8547", belly: "#D3C995" }, stripes: stripes([5, 8, 11, 14, 17], 1, { tone: "shadow" }) }
    ),

  Hypsilophodon: (g) =>
    finish(
      biped({
        hip: [15, 14.4], chest: [20, 13.6], girth: [3.3, 2.8],
        tail: [[13.5, 14, 2.5], [9, 12.8, 1.6], [5, 11.2, 0.9], [2.6, 10.2, 0.5]],
        neck: [[21, 12.6, 2.2], [23, 10.2, 1.7], [24.2, 8.8, 1.5]],
        head: [25.8, 8.0, 2.9, 2.3], snout: [[27, 8.3, 1.6], [29.9, 8.9, 1.0]],
        leg: { T: 4.2, w: 2.6, f: 2.8 }, arm: { len: 2.6, w: 0.8 }, stride: 1.6
      }, g),
      { ramps: { body: "#C08A4A", belly: "#EBDDB4" }, stripes: stripes([7, 10, 13, 16, 19], 2, { tone: "shadow" }) }
    ),

  Kentrosaurus: (g) =>
    finish(
      quad({
        rump: [12.5, 13, 4.4], shoulder: [20, 14.2, 3.6], hindX: 12, foreX: 20.6, legW: [1.8, 1.5],
        tail: [[10, 13, 3.0], [5.5, 13, 1.9], [2, 12.6, 1.0], [0.6, 12.4, 0.6]],
        neck: [[22.6, 14.6, 2.2], [25.8, 15.8, 1.7]],
        head: [28, 16.4, 2.3, 1.9], snout: [[29, 16.7, 1.3], [31.4, 17.2, 0.9]],
        onTop: [
          poly([[20.4, 11.6], [21.6, 8.0], [22.9, 11.8]], { ramp: "plate", sep: true, orn: true }),
          poly([[17.4, 10.8], [18.8, 6.6], [20.3, 11.0]], { ramp: "plate", sep: true, orn: true }),
          poly([[14.4, 9.8], [16.0, 5.4], [17.5, 10.0]], { ramp: "plate", sep: true, orn: true }),
          stroke([[12.6, 9.4, 0.8], [11.2, 3.4, 0.35]], { ramp: "bone", thin: true, orn: true }),
          stroke([[9.6, 10.0, 0.8], [7.6, 4.2, 0.35]], { ramp: "bone", thin: true, orn: true, bone: "tail", along: 0.05 }),
          stroke([[6.6, 11.3, 0.75], [4.2, 6.0, 0.35]], { ramp: "bone", thin: true, orn: true, bone: "tail", along: 0.36 }),
          stroke([[3.8, 11.9, 0.7], [1.6, 7.4, 0.35]], { ramp: "bone", thin: true, orn: true, bone: "tail", along: 0.66 })
        ]
      }, g),
      { ramps: { body: "#647A5C", belly: "#CBCBA6", plate: "#B5653A" } }
    ),

  Pachycephalosaurus: (g) =>
    finish(
      biped({
        hip: [13.5, 12.5], chest: [20.5, 11.5], girth: [4.6, 4.0],
        tail: [[11.5, 12.2, 3.4], [6.5, 11, 2.2], [2.2, 9.8, 1.0], [0.6, 9.4, 0.5]],
        neck: [[22, 10.6, 2.9], [24.6, 8.4, 2.4]],
        head: [26.8, 6.8, 3.0, 2.5], snout: [[28.2, 7.5, 1.7], [31.2, 8.3, 1.1]],
        onTop: [ellipse(26.4, 5.3, 2.8, 2.0, { ramp: "dome", bone: "head", orn: true })],
        leg: { T: 5.2, w: 3.4, f: 3.3 }, arm: { len: 2.8, w: 0.9 }
      }, g),
      {
        ramps: { body: "#86764A", belly: "#DCCFA6", dome: "#C2A873" },
        eye: [27, 7],
        tips: [[23, 6, BONE], [23, 8, BONE], [30, 7, BONE]],
        stripes: stripes([6, 9, 12, 15, 18], 2, { tone: "shadow" })
      }
    ),

  Tenontosaurus: (g) =>
    finish(
      biped({
        hip: [14.5, 11.6], chest: [22, 12.8], girth: [4.8, 4.0],
        tail: [[12, 11.6, 4.2], [6, 11.2, 3.0], [2, 10.8, 1.9], [0.5, 10.6, 1.1]],
        neck: [[23.6, 11.8, 2.6], [26.4, 9.4, 2.0]],
        head: [28.6, 8.6, 2.8, 2.1], snout: [[29.8, 9.0, 1.7], [32.8, 9.8, 1.2]],
        leg: { T: 5.8, w: 3.6, f: 3.2 }, foreleg: 1.3
      }, g),
      { ramps: { body: "#7F8C62", belly: "#D9D3AE" }, stripes: stripes([2, 5, 8, 11, 14, 17, 20], 3, { tone: "shadow" }) }
    ),

  Diabloceratops: (g) =>
    finish(
      quad({
        rump: [10.6, 14.0, 4.0], shoulder: [17.4, 14.0, 4.0], hindX: 10.0, foreX: 17.6, legW: [1.9, 1.8],
        tail: [[8.2, 14.2, 2.7], [4.4, 15.4, 1.6], [2.0, 16.4, 0.9]],
        head: [24.8, 15.0, 3.7, 3.0], snout: [[27.0, 15.8, 2.3], [30.4, 17.4, 1.35], [31.4, 18.3, 0.9]],
        mid: [
          stroke([[22.8, 4.8, 0.95], [24.0, 2.2, 0.6], [25.8, 0.6, 0.35]], { ramp: "bone", thin: "shadow", bone: "head", orn: DIABLO_FRILL }),
          // The frill: a fan swept back from the skull, not a disc (a round
          // plate with a pale ring read as a shield, H12); knobs on its edge.
          poly([[23.8, 12.8], [22.2, 15.2], [19.2, 14.0], [17.2, 10.8], [16.6, 7.4], [17.4, 5.0], [19.2, 4.0], [21.2, 4.4], [22.8, 6.6], [24.4, 9.8]], { ramp: "frill", bone: "head", orn: DIABLO_FRILL }),
          stroke([[19.8, 5.0, 0.95], [18.6, 2.4, 0.6], [16.8, 0.7, 0.35]], { ramp: "bone", thin: true, bone: "head", orn: DIABLO_FRILL })
        ],
        onTop: [stroke([[25.8, 12.8, 0.8], [28.0, 10.4, 0.35]], { ramp: "bone", thin: true, bone: "head", orn: true })]
      }, g),
      {
        ramps: { body: "#96744E", belly: "#D8C4A0", frill: "#A84E3A" },
        ridges: [{ from: 17, to: 23, step: 2, colour: "#D9C29A" }],
        marks: [[20, 8, "shadow", "frill"], [22, 10, "shadow", "frill"], [20, 12, "shadow", "frill"]],
        tips: [[31, 18, BONE], [30, 18, BONE]]
      }
    ),

  Maiasaura: (g) =>
    finish(
      biped({
        hip: [13, 11.8], chest: [21, 12.6], girth: [5.0, 4.4],
        tail: [[10.6, 11.6, 4.0], [5.4, 10.6, 2.6], [1.8, 9.6, 1.2], [0.5, 9.4, 0.6]],
        neck: [[22.6, 11.4, 3.0], [25.4, 9.2, 2.4]],
        head: [27.6, 8.2, 3.0, 2.3], snout: [[28.8, 8.6, 1.8], [32.2, 9.7, 1.55], [33.6, 10.0, 1.45]], snoutRamp: "bill", mouth: false,
        onTop: [poly([[26.8, 6.7], [27.9, 4.6], [29.3, 6.6]], { ramp: "crest", sep: true, bone: "head", orn: true })],
        leg: { T: 6, w: 3.9, f: 3.4 }, foreleg: 1.25
      }, g),
      {
        ramps: { body: "#B58F58", belly: "#EADBB6", bill: "#8A7658", crest: "#9A6238" },
        stripes: stripes([4, 8, 12, 16, 20], 2, { tone: "shadow" })
      }
    ),

  Stegosaurus: (g) =>
    finish(
      quad({
        rump: [12, 11.6, 5.2], shoulder: [20.5, 13.4, 4.2], hindX: 11.4, foreX: 21.4, legW: [2.0, 1.6],
        tail: [[9.5, 11.8, 3.6], [5, 12.4, 2.2], [1.8, 12.8, 1.2], [0.6, 12.8, 0.7]],
        neck: [[23.5, 13.8, 2.6], [27, 15.4, 1.9]],
        head: [29.6, 16.2, 2.6, 1.7], snout: [[30.6, 16.5, 1.4], [33.2, 17.1, 0.95]],
        behind: [
          poly([[16.2, 7.4], [17.8, 2.2], [19.6, 8.2]], { ramp: "plate", far: true, orn: true }),
          poly([[8.4, 7.8], [10.2, 2.4], [12.0, 6.8]], { ramp: "plate", far: true, orn: true, bone: "tail", along: 0.12 })
        ],
        onTop: [
          poly([[21.2, 10.6], [22.6, 6.4], [24.1, 10.8]], { ramp: "plate", sep: true, orn: true }),
          poly([[18.2, 9.0], [19.9, 3.8], [21.7, 9.4]], { ramp: "plate", sep: true, orn: true }),
          poly([[14.3, 7.4], [16.2, 1.0], [18.2, 7.8]], { ramp: "plate", sep: true, orn: true }),
          poly([[10.3, 7.0], [12.2, 0.8], [14.2, 7.0]], { ramp: "plate", sep: true, orn: true }),
          poly([[6.7, 8.6], [8.4, 3.4], [10.2, 7.8]], { ramp: "plate", sep: true, orn: true, bone: "tail", along: 0.31 }),
          poly([[3.9, 10.6], [5.2, 6.8], [6.6, 10.0]], { ramp: "plate", sep: true, orn: true, bone: "tail", along: 0.63 }),
          stroke([[2.4, 12.2, 0.7], [0.5, 8.8, 0.35]], { ramp: "bone", thin: true, orn: true, bone: "tail", along: 0.8 }),
          stroke([[3.8, 11.8, 0.7], [2.6, 8.0, 0.35]], { ramp: "bone", thin: true, orn: true, bone: "tail", along: 0.64 })
        ]
      }, g),
      { ramps: { body: "#6F8058", belly: "#CFCBA4", plate: "#C8623A" } }
    ),

  Triceratops: (g) =>
    finish(
      quad({
        rump: [10.2, 13.2, 4.7], shoulder: [17.2, 13.4, 4.7], hindX: 9.6, foreX: 17.6, legW: [2.1, 2.0],
        tail: [[7.6, 13.6, 3.0], [3.8, 15.2, 1.7], [1.6, 16.4, 0.9]],
        head: [25.2, 14.2, 4.2, 3.4], snout: [[27.8, 15.2, 2.6], [31.6, 17.0, 1.5], [32.8, 18.0, 1.0]],
        mid: [
          stroke([[25.0, 11.0, 0.9], [30.6, 5.4, 0.4]], { ramp: "bone", thin: "shadow", bone: "head", orn: true }),
          // The frill: a short fan swept back and up from the skull, knobbed
          // along its edge, not a disc in a pale ring (it read as a shield, H12).
          poly([[25.2, 11.6], [23.4, 15.4], [19.8, 14.0], [17.0, 10.6], [16.4, 7.0], [17.6, 4.4], [20.2, 3.0], [22.8, 3.4], [24.6, 5.4], [25.8, 8.6]], { ramp: "frill", bone: "head", orn: TRIKE_FRILL })
        ],
        onTop: [
          stroke([[26.6, 11.6, 1.0], [33.4, 6.4, 0.4]], { ramp: "bone", thin: true, bone: "head", orn: true }),
          stroke([[30.0, 14.6, 0.8], [31.8, 11.8, 0.35]], { ramp: "bone", thin: true, bone: "snout", orn: true })
        ]
      }, g),
      {
        ramps: { body: "#8B7B62", belly: "#D5C8A8", frill: "#B26A48" },
        ridges: [{ from: 16, to: 24, step: 2, colour: "#CDB88E" }],
        marks: [[20, 8, "shadow", "frill"], [23, 7, "shadow", "frill"], [21, 11, "shadow", "frill"], [24, 10, "shadow", "frill"]],
        tips: [[32, 18, BONE], [33, 18, BONE]]
      }
    ),

  // The flyer. Its walk is a wing beat (g.wing, 0 to 3: up as it stands, level,
  // down, level): each wing is raised, flattened or mirrored under its shoulder
  // line (WING_BEAT), and the highlights along the near wing's edge go with it.
  Pteranodon: (g, size) => {
    const beat = wingBeat((g || STAND).wing, size);
    const nearWing = (pts) => pts.map((p) => turn(p, NEAR_SHOULDER, beat.near));
    const farWing = (pts) => pts.map((p) => turn(p, FAR_SHOULDER, beat.far));
    const parts = [
      poly(farWing([[20.8, 13.4], [22.8, 5.6], [27.6, 1.4], [26.0, 6.8], [23.2, 13.0]]), { ramp: "wing", far: true }),
      stroke([[15.4, 16.0, 0.8], [11.4, 18.4, 0.5]], { far: true }),
      poly(nearWing([[19.2, 13.6], [17.6, 6.2], [12.4, 2.8], [5.4, 1.2], [9.4, 7.4], [12.8, 12.6], [15.0, 16.2]]), { ramp: "wing" }),
      ellipse(17.6, 15.2, 3.5, 2.3, { belly: true }),
      stroke([[20.2, 14.4, 1.6], [23.4, 12.2, 1.4]], { bone: "neck" }),
      stroke([[24.0, 10.4, 1.2], [20.6, 7.6, 0.8], [17.6, 6.2, 0.45]], { ramp: "crest", thin: true, bone: "head", orn: true }),
      ellipse(25.2, 11.4, 2.5, 2.0, { bone: "head", skull: true }),
      ...(g && g.jaw ? jaws(BEAK, g.jaw, { ramp: "beak", thin: true, bone: "snout" }, false, false).parts : [stroke(BEAK, { ramp: "beak", thin: true, bone: "snout" })]),
      stroke([[16.4, 16.6, 0.8], [12.6, 19.2, 0.5]], { limb: true })
    ];
    const edge = [[17, 7], [15, 5], [12, 4], [9, 3], [7, 2]].map(([x, y]) => {
      const [tx, ty] = beat.near === 1 ? [x, y] : turn([x + 0.5, y + 0.5], NEAR_SHOULDER, beat.near).map(Math.floor);
      return [tx, ty, "highlight", "wing"];
    });
    return {
      w: W, h: H,
      head: [25.2, 11.4],
      skull: [25.2, 11.4, 2.5, 2.0],
      hip: [17.6, 15.2],
      ramps: { body: "#D2CAB6", belly: "#F1ECDD", wing: "#7F92A3", crest: "#D9762B", beak: "#E3BD55", bone: BONE },
      parts,
      marks: edge,
      extras: [[26, 11, DARK_EYE, "eye"]]
    };
  },

  Troodon: (g) =>
    finish(
      biped({
        hip: [15, 14], chest: [20.5, 13], girth: [3.2, 2.7],
        tail: [[13.4, 13.6, 2.2], [8, 12.6, 1.2], [4.6, 12.0, 0.8]],
        neck: [[21.6, 12, 2.0], [23.6, 9.4, 1.6], [24.6, 8.0, 1.5]],
        head: [26.2, 7.2, 2.8, 2.1], snout: [[27.4, 7.5, 1.5], [31.0, 8.2, 0.9]], teeth: true, mouthFrom: 1,
        behind: [stroke([[5.4, 11.8, 1.0], [1.6, 9.8, 0.5]], { ramp: "feather", thin: true, bone: "tailEnd" }), stroke([[5.4, 12.2, 1.0], [1.0, 12.2, 0.5]], { ramp: "feather", thin: true, bone: "tailEnd" }), stroke([[5.4, 12.4, 1.0], [1.8, 14.4, 0.5]], { ramp: "feather", thin: true, bone: "tailEnd" })],
        onTop: [poly([[23.6, 6.4], [24.6, 4.2], [26.4, 5.4]], { ramp: "feather", sep: true, bone: "head", orn: true })],
        front: [poly([[19.4, 13.6], [23.2, 14.4], [22.2, 17.6], [18.6, 16.0]], { ramp: "feather", sep: true })],
        leg: { T: 4.4, w: 2.5, f: 3.0 }, stride: 1.6
      }, g),
      {
        ramps: { body: "#6C7C8C", belly: "#D4D8D8", feather: "#4F6274" },
        tips: [[1, 9, "#E8EEF2", "fine"], [1, 12, "#E8EEF2", "fine"], [2, 14, "#E8EEF2", "fine"], [21, 17, "#E8EEF2"]],
        eyeColour: AMBER, pupil: true,
        marks: [[2, 13, "highlight", "feather"], [3, 11, "highlight", "feather"], [20, 16, "highlight", "feather"], [22, 16, "highlight", "feather"], [16, 12, "highlight"], [18, 11, "highlight"], [12, 13, "highlight"]]
      }
    ),

  Herrerasaurus: (g) =>
    finish(
      biped({
        hip: [14, 13.6], chest: [21.2, 13.0], girth: [3.4, 2.9],
        tail: [[12, 13.4, 2.6], [7, 12.6, 1.6], [2.5, 11.6, 0.8], [0.4, 11.2, 0.45]],
        neck: [[22.4, 12.4, 2.3], [25.2, 10.8, 1.9]],
        head: [27.6, 10.0, 3.1, 2.2], snout: [[28.8, 10.2, 1.7], [34.2, 11.0, 1.05]], teeth: true,
        leg: { T: 4.6, w: 2.8, f: 3.4 }, arm: { len: 3.4, w: 0.85 }
      }, g),
      { ramps: { body: "#9A4E30", belly: "#E2C496" }, eyeColour: AMBER, pupil: true, stripes: stripes([4, 7, 10, 13, 16, 19], 2, { tone: "shadow" }) }
    ),

  Omniraptor: (g) =>
    finish(
      biped({
        hip: [14, 12.8], chest: [20.5, 11.6], girth: [4.0, 3.5],
        tail: [[12, 12.4, 2.6], [6, 11.4, 1.4], [3.0, 10.9, 0.9]],
        neck: [[22, 10.6, 2.6], [24.4, 8.2, 2.1]],
        head: [26.8, 6.8, 3.2, 2.3], snout: [[28, 7.1, 1.8], [32.6, 7.9, 1.15]], teeth: true,
        behind: [stroke([[4.4, 10.8, 1.1], [0.8, 8.6, 0.5]], { ramp: "feather", thin: true, bone: "tailEnd" }), stroke([[4.4, 11.0, 1.1], [0.4, 11.0, 0.5]], { ramp: "feather", thin: true, bone: "tailEnd" }), stroke([[4.4, 11.2, 1.1], [1.0, 13.4, 0.5]], { ramp: "feather", thin: true, bone: "tailEnd" })],
        onTop: [poly([[24.0, 5.6], [25.2, 3.2], [27.0, 4.7]], { ramp: "feather", sep: true, bone: "head", orn: true })],
        front: [poly([[19.3, 12.2], [23.8, 13.2], [22.6, 17.4], [18.4, 15.2]], { ramp: "feather", sep: true })],
        leg: { T: 5.2, w: 3.2, f: 3.4 }
      }, g),
      {
        ramps: { body: "#57483C", belly: "#D9C09A", feather: "#A9572F" },
        eyeColour: AMBER, pupil: true,
        marks: [[1, 12, "highlight", "feather"], [2, 10, "highlight", "feather"], [20, 15, "highlight", "feather"], [22, 16, "highlight", "feather"]],
        tips: [[16, 21, BONE], [17, 20, BONE], [0, 8, "#E9B58A", "fine"], [0, 11, "#E9B58A", "fine"], [1, 13, "#E9B58A", "fine"]]
      }
    ),

  Carnotaurus: (g) =>
    finish(
      biped({
        hip: [13.5, 10.2], chest: [20.5, 9.4], girth: [4.0, 3.5],
        tail: [[11.5, 10.0, 3.1], [6, 9.0, 1.9], [2, 7.8, 0.9], [0.5, 7.4, 0.5]],
        neck: [[21.8, 8.4, 2.7], [24.4, 6.6, 2.4]],
        head: [27.0, 5.6, 3.0, 2.7], snout: [[28.0, 6.0, 2.2], [31.2, 6.6, 1.7]], teeth: true,
        behind: [poly([[24.3, 3.8], [24.4, 0.8], [25.7, 3.4]], { ramp: "bone", far: true, thin: "shadow", bone: "head", orn: true })],
        onTop: [poly([[25.8, 3.6], [26.4, 0.3], [28.2, 3.4]], { ramp: "bone", thin: "light", bone: "head", orn: true })],
        leg: { T: 7.4, w: 3.1, f: 3.4 }, arm: { len: 0.9, w: 0.6 }
      }, g),
      {
        ramps: { body: "#A84A32", belly: "#E0C49C" },
        eye: [28, 5], eyeColour: AMBER, pupil: true,
        marks: [[8, 9, "shadow"], [11, 8, "shadow"], [12, 9, "shadow"], [15, 8, "shadow"], [18, 8, "shadow"], [19, 9, "shadow"], [5, 9, "shadow"], [22, 7, "shadow"]],
        ridges: [{ from: 6, to: 22, step: 3, colour: "#5E2418" }]
      }
    ),

  Ceratosaurus: (g) =>
    finish(
      biped({
        hip: [13.5, 12.4], chest: [21, 11.6], girth: [4.3, 3.7],
        tail: [[11.5, 12.2, 3.9], [6.5, 11.6, 3.1], [2.6, 10.6, 2.0], [0.5, 10.0, 0.9]],
        neck: [[22.6, 10.8, 2.9], [25.2, 8.8, 2.4]],
        head: [27.8, 7.8, 3.5, 2.7], snout: [[29, 8.1, 2.1], [33.8, 8.9, 1.4]], teeth: true,
        onTop: [
          poly([[30.1, 7.3], [31.2, 2.4], [32.9, 7.5]], { ramp: "bone", thin: "light", bone: "snout", orn: true }),
          poly([[26.5, 5.9], [27.2, 3.6], [28.5, 5.7]], { ramp: "bone", thin: "light", bone: "head", orn: true })
        ],
        leg: { T: 5.4, w: 3.4, f: 3.5 }, arm: { len: 3.0, w: 0.95 }
      }, g),
      {
        ramps: { body: "#B48C3E", belly: "#E8D8A8" },
        eyeColour: AMBER, pupil: true,
        stripes: stripes([3, 6, 9, 12, 15, 18, 21], 3, { tone: "shadow" }),
        ridges: [{ from: 3, to: 24, step: 2, colour: "#4A3410" }]
      }
    ),

  Dilophosaurus: (g) =>
    finish(
      biped({
        hip: [13.5, 12.8], chest: [20.5, 11.8], girth: [3.8, 3.2],
        tail: [[11.5, 12.4, 3.0], [6, 11, 1.9], [2, 9.6, 0.9], [0.5, 9.2, 0.5]],
        neck: [[22, 10.6, 2.4], [24.6, 7.8, 1.9], [25.8, 6.4, 1.7]],
        head: [27.8, 5.9, 3.2, 2.1], snout: [[29, 6.2, 1.7], [33.6, 7.1, 1.1]], teeth: true,
        behind: [poly([[24.6, 4.8], [25.2, 2.6], [27.0, 1.6], [28.6, 2.6], [29.0, 4.6]], { ramp: "crest", far: true, bone: "head", orn: true })],
        onTop: [poly([[25.6, 4.9], [26.4, 2.2], [28.4, 1.0], [30.2, 2.2], [30.8, 4.8]], { ramp: "crest", sep: true, bone: "head", orn: true })],
        leg: { T: 5.4, w: 3.0, f: 3.4 }, arm: { len: 3.4, w: 0.9 }
      }, g),
      {
        ramps: { body: "#72904C", belly: "#E0D89C", crest: "#C84A34" },
        eye: [28, 5], eyeColour: AMBER, pupil: true,
        stripes: stripes([4, 7, 10, 13, 16, 19, 22], 2, { tone: "shadow" })
      }
    ),

  // The crawler: four sprawled legs, the body low, the head steady. Its walk is
  // the quadrupeds' diagonal pairs with a short reach (CRAWL in the gaits).
  // A young crocodile is short and stout (H61): at a hatchling's and a
  // juvenile's size its trunk is drawn deeper and carried a little higher
  // (YOUNG_CROC), or at four or five pixels deep it has no belly row and no lit
  // back and is mostly outline, a row of dots on a forest tile. The larger
  // sizes multiply by 1 and add 0: the numbers they always had.
  Deinosuchus: (g, size) => {
    g = g || STAND;
    const young = YOUNG_CROC[(size && size.suffix) || ""] || { deep: 1, lift: 0 };
    const trunk = ([x, y, r]) => [x, y - young.lift, r * young.deep];
    const snout = [[28.2, 17.0, 1.9], [34.4, 17.7, 1.35]];
    const open = g.jaw ? jaws(snout, g.jaw, { belly: true, bone: "snout" }, true) : null;
    const m = open ? { marks: [], tips: open.tips } : mouth(snout, true, 0);
    const foot = (step) => (step.swing ? "swing" : "ground");
    // A sprawled leg: its upper end on the body, its lower end and its foot moved by the step.
    const limb = (top, low, toe, step, more) => {
      const d = step.dx;
      return [
        stroke([top, [low[0] + d, low[1], low[2], foot(step)]], more),
        stroke([[toe[0][0] + d, toe[0][1], toe[0][2], foot(step)], [toe[1][0] + d, toe[1][1], toe[1][2], foot(step)]], more.far ? more : { limb: true })
      ];
    };
    const parts = [
      ...limb([9.2, 18.8, 1.4], [8.0, 22.0, 1.05], [[7.8, 22.9, 0.85], [10.4, 23.3, 0.7]], g.far, { far: true }),
      ...limb([18.4, 18.8, 1.4], [19.4, 22.0, 1.05], [[19.2, 22.9, 0.85], [22.0, 23.3, 0.7]], g.foreFar, { far: true }),
      stroke([trunk([8.5, 17.6, 3.0]), [4, 16.6, 1.9], [1.4, 15.0, 1.0], [0.5, 14.0, 0.6]], { bone: "tail" }),
      stroke([trunk([9.5, 17.6, 3.4]), trunk([19, 17.4, 3.6])], { belly: true }),
      stroke([trunk([21.5, 17.2, 3.0]), [24.6, 16.8, 2.6]], { belly: true, bone: "neck" }),
      ellipse(26.6, 16.6, 2.9, 2.3, { belly: true, bone: "head", skull: true }),
      ...(open ? open.parts : [stroke(snout, { belly: true, bone: "snout" })]),
      // The nose's knob rides the upper jaw.
      ellipse(34.2, 17.0 + (open ? open.lift(34.2) : 0), 1.25, 1.2, { bone: "snout" }),
      ellipse(26.0, 14.8, 1.3, 1.05, { bone: "head" }),
      ...limb([11.2, 19.2, 1.6], [10.0, 22.0, 1.15], [[9.8, 22.9, 0.9], [12.8, 23.3, 0.75]], g.near, { limb: true, sep: true }),
      ...limb([20.2, 19.2, 1.6], [21.2, 22.0, 1.15], [[21.0, 22.9, 0.9], [24.0, 23.3, 0.75]], g.foreNear, { limb: true, sep: true })
    ];
    return {
      w: W, h: H,
      head: [26.6, 16.6],
      skull: [26.6, 16.6, 2.9, 2.3],
      hip: [9.5, 17.6],
      ramps: { body: "#50623F", belly: "#C9BC84", bone: BONE, maw: MAW },
      parts,
      stripes: stripes([2, 4, 6, 9, 12, 15, 18, 21], 2, { tone: "shadow" }),
      marks: m.marks,
      tips: [...m.tips, [Math.floor(12.5 + g.near.dx), 23, BONE], [Math.floor(24.5 + g.foreNear.dx), 23, BONE]],
      ridges: [{ from: 1, to: 24, step: 2, colour: "#1F2A1A" }],
      extras: [[26, 14, AMBER, "eye"], [27, 14, "#1A0E0A", "pupil"]]
    };
  },

  Allosaurus: (g) =>
    finish(
      biped({
        hip: [13, 11.8], chest: [21, 10.8], girth: [5.2, 4.5],
        tail: [[10.8, 11.6, 4.0], [5.6, 10.4, 2.6], [1.8, 8.8, 1.2], [0.5, 8.4, 0.5]],
        neck: [[22.8, 9.8, 3.4], [25.4, 8, 3.0]],
        head: [28.4, 7.2, 4.0, 2.9], snout: [[29.6, 7.5, 2.4], [34.6, 8.4, 1.6]], teeth: true,
        onTop: [
          // The horns before and behind its eyes, light on the skull (an
          // outlined knob this small was all outline: a dark nub, H12).
          poly([[28.7, 5.3], [29.8, 2.0], [31.5, 5.1]], { ramp: "crest", thin: "light", bone: "head", orn: true }),
          poly([[25.9, 5.1], [26.6, 3.2], [27.9, 5.0]], { ramp: "crest", thin: "base", bone: "head", orn: true })
        ],
        leg: { T: 5.8, w: 3.9, f: 3.6 }, arm: { len: 3.6, w: 1.1 }
      }, g),
      {
        ramps: { body: "#6F7F86", belly: "#D6D2C0", crest: "#B5603A" },
        eye: [29, 6], eyeColour: AMBER, pupil: true,
        stripes: stripes([3, 6, 9, 12, 15, 18, 21], 2, { tone: "#B5603A" })
      }
    ),

  Tyrannosaurus: (g) =>
    finish(
      biped({
        hip: [12.5, 11.6], chest: [20.5, 10.6], girth: [5.4, 4.9],
        tail: [[10.2, 11.4, 4.2], [5.2, 10.2, 2.7], [1.6, 8.8, 1.2], [0.4, 8.4, 0.6]],
        neck: [[22.6, 9.6, 3.8], [25, 7.8, 3.5]],
        head: [28.2, 7.0, 4.4, 3.5], snout: [[29.6, 7.4, 3.0], [34.4, 8.2, 2.3]], teeth: true,
        leg: { T: 6.2, w: 4.0, f: 3.8 }, arm: { len: 1.9, w: 0.8 }
      }, g),
      {
        ramps: { body: "#6E553A", belly: "#D2BC92" },
        eye: [29, 5], eyeColour: AMBER, pupil: true,
        marks: [[28, 4, "shadow"], [29, 4, "shadow"], [30, 4, "shadow"]],
        stripes: stripes([3, 6, 9, 12, 15, 18], 2, { tone: "shadow" })
      }
    ),

  Beipiaosaurus: (g) =>
    finish(
      biped({
        hip: [14.5, 14], chest: [19.5, 11.2], girth: [4.6, 3.8],
        tail: [[12.6, 14, 3.0], [8, 13.6, 1.8], [5.4, 13.2, 1.2]],
        neck: [[20.6, 9.6, 2.4], [22.6, 6.6, 1.8], [23.8, 4.6, 1.5]],
        head: [25.4, 3.9, 2.4, 1.8], snout: [[26.4, 4.2, 1.3], [29.2, 4.9, 0.8]], mouth: false,
        behind: [ellipse(4.4, 13.3, 2.7, 2.1, { bone: "tailEnd" })],
        front: [
          // The hand reaches its claws, and the claws are the bone's shadow:
          // a pale claw apart from the arm was a white fleck before the animal
          // at every size (H12). Long claws are still its kind's outline.
          stroke([[20.4, 12.8, 1.1], [24.0, 15.6, 0.85]], { limb: true, sep: true }),
          stroke([[24.0, 15.4, 0.55], [27.0, 17.4, 0.3]], { ramp: "bone", thin: "shadow", orn: true }),
          stroke([[24.0, 15.6, 0.55], [26.0, 18.6, 0.3]], { ramp: "bone", thin: "shadow", orn: true }),
          stroke([[23.6, 15.8, 0.55], [24.6, 19.0, 0.3]], { ramp: "bone", thin: "shadow", orn: true })
        ],
        leg: { T: 4.6, w: 3.3, f: 3.2 }
      }, g),
      {
        ramps: { body: "#B27C46", belly: "#EBD9B0" },
        tips: [[28, 4, BONE], [29, 4, BONE]],
        marks: [[10, 12, "highlight"], [13, 11, "highlight"], [16, 10, "highlight"], [8, 13, "highlight"], [4, 12, "highlight"], [22, 7, "highlight"], [18, 9, "shadow"], [12, 13, "shadow"]]
      }
    ),

  Gallimimus: (g) =>
    finish(
      biped({
        hip: [14, 12.8], chest: [20, 11.4], girth: [3.9, 3.3],
        tail: [[12, 12.4, 2.8], [6.5, 11.2, 1.7], [2.4, 10, 0.9], [0.6, 9.6, 0.5]],
        neck: [[21, 10.2, 2.2], [23.4, 6.4, 1.6], [24.6, 3.8, 1.4]],
        head: [26.2, 3.2, 2.4, 1.9], snout: [[27.2, 3.5, 1.3], [30.8, 4.2, 0.75]], snoutRamp: "beak", mouth: false,
        front: [poly([[19.2, 12.2], [23.0, 13.2], [21.8, 16.6], [18.4, 14.8]], { ramp: "feather", sep: true })],
        leg: { T: 5.6, w: 2.9, f: 3.4 }
      }, g),
      {
        ramps: { body: "#C9A868", belly: "#EFE2BE", feather: "#4E3E2E", beak: "#B79A62" },
        stripes: stripes([3, 5, 7, 9, 11, 13, 15, 17], 1, { tone: "shadow" }),
        marks: [[20, 15, "highlight", "feather"], [21, 14, "highlight", "feather"]]
      }
    )
};

const ORDER = [
  "Dryosaurus", "Hypsilophodon", "Pachycephalosaurus", "Tenontosaurus", "Maiasaura", "Beipiaosaurus", "Gallimimus",
  "Kentrosaurus", "Stegosaurus", "Diabloceratops", "Triceratops", "Deinosuchus", "Pteranodon", "Troodon",
  "Herrerasaurus", "Omniraptor", "Dilophosaurus", "Carnotaurus", "Ceratosaurus", "Allosaurus", "Tyrannosaurus"
];

// ---- Sizes (PLAN-HATCHLING 6.2, H21) ----
// Your dino is drawn at its stage's size. The frame sizes and pivots are the
// plan's; `scale` is how much smaller the shapes are drawn (about the frame's
// width over 36), and `grow` is the age: a hatchling's head is larger for its
// body and its snout shorter (a young animal's face), its horns, plates,
// spikes, crests, frills and claws short (orn). An Adult and an Elder are the
// full size, which has no place() at all when standing.
const SIZES = [
  { suffix: "", w: 36, h: 25, px: 18, py: 24, scale: 1, grow: {} },
  { suffix: "-l", w: 31, h: 22, px: 15, py: 21, scale: 0.86, grow: { head: 1.1, snout: 0.93, neck: 1.05, orn: 0.92, spike: 0.85 } },
  { suffix: "-m", w: 26, h: 18, px: 13, py: 17, scale: 0.72, grow: { head: 1.28, snout: 0.82, neck: 1.15, orn: 0.82, spike: 0.65 } },
  { suffix: "-s", w: 20, h: 14, px: 10, py: 13, scale: 0.56, grow: { head: 1.55, snout: 0.68, neck: 1.3, orn: 0.72, spike: 0.42 } }
];

// How each species gets about. Everything not named here walks on two legs
// (Tenontosaurus and Maiasaura put their hands down too, with the quadrupeds'
// diagonal pairs).
const BUILD = {
  Kentrosaurus: "quad", Stegosaurus: "quad", Diabloceratops: "quad", Triceratops: "quad",
  Deinosuchus: "crawl", Pteranodon: "fly"
};

// ---- The walk: walk/0 to walk/3, ticks 3 each, looping (6.2) ----
// Pair A is the near hind foot with the far front one, pair B the far hind foot
// with the near front one (a two-legged walker has only the hind feet of each).
// A frame says where each pair is in the stride (+1 reaching forward, -1 pushed
// back, 0 under the body), which pair is the lifted one, and the pose in whole
// pixels of the frame: the body's bob, the head's and the tail tip's offsets
// on top of it. Contact, passing, contact, passing: the body rises a pixel as
// the legs pass under it, the head and the tail tip stay where they were (so
// they counter it), and the feet that carry it never leave the ground line.
const WALKS = {
  // Two legs: a pixel of bob, the head and the tail held level against it.
  biped: [
    { A: 1, B: -1, body: [0, 0], head: [0, 0], tail: [0, 0] },
    { A: 0, B: 0, lift: "B", body: [0, -1], head: [0, 1], tail: [0, 1] },
    { A: -1, B: 1, body: [0, 0], head: [0, 0], tail: [0, 0] },
    { A: 0, B: 0, lift: "A", body: [0, -1], head: [0, 1], tail: [0, 1] }
  ],
  // Four legs: no bob (a heavy body rides level); the head nods a pixel as each
  // pair passes, the tail tip swings the other way.
  quad: [
    { A: 1, B: -1, body: [0, 0], head: [0, 0], tail: [0, -1] },
    { A: 0, B: 0, lift: "B", body: [0, 0], head: [0, 1], tail: [0, 0] },
    { A: -1, B: 1, body: [0, 0], head: [0, 0], tail: [0, 1] },
    { A: 0, B: 0, lift: "A", body: [0, 0], head: [0, 1], tail: [0, 0] }
  ],
  // Crawling low: no bob, the head steady, the tail tip swinging.
  crawl: [
    { A: 1, B: -1, body: [0, 0], head: [0, 0], tail: [0, -1] },
    { A: 0, B: 0, lift: "B", body: [0, 0], head: [0, 0], tail: [0, 0] },
    { A: -1, B: 1, body: [0, 0], head: [0, 0], tail: [0, 1] },
    { A: 0, B: 0, lift: "A", body: [0, 0], head: [0, 0], tail: [0, 0] }
  ],
  // The flyer beats its wings: up (as it stands), level, down, level. The
  // downstroke lifts it a pixel.
  fly: [
    { wing: 0, body: [0, 0] },
    { wing: 1, body: [0, 0] },
    { wing: 2, body: [0, -1] },
    { wing: 3, body: [0, 0] }
  ]
};

// How far a foot reaches forward or back, in pixels of the frame, by size, and
// a crawler's shorter reach. A lifted foot is one pixel up.
// (1.5 at full size was tried first: under a two-legged walker's big thigh only
// the shin moved, and the stride could not be seen.)
const REACH = { "": 2.4, "-l": 2.1, "-m": 1.7, "-s": 1.3 };
const CRAWL_REACH = 1.2;
// How much of a quadruped's far-leg offset each pair gives up walking (of 2.1).
const QUAD_SHARE = 0.5;
const LIFT = 1;

// The wing beat: each wing's height above its shoulder line, as a part of the
// drawn one (1: raised, as it stands; near 0: level, seen almost edge on; below
// 0: mirrored under the line, the downstroke). Turning the wing about the
// shoulder was tried first: a turned wing keeps its full breadth and read as a
// blob trailing behind (Nov 14), where a wing seen from the side flattens.
const NEAR_SHOULDER = [18.6, 13.8];
const FAR_SHOULDER = [21.8, 13.2];
const WING_BEAT = [
  { near: 1, far: 1 },
  { near: 0.35, far: 0.4 },
  { near: -0.6, far: -0.45 },
  { near: 0.35, far: 0.4 }
];
// A level wing at the two small sizes (H12): flattened to a third of its height
// it was a sliver of outline, so the beat read as wing, nothing, wing; there
// it keeps more of its height.
const LEVEL_SMALL = { "-m": 0.55, "-s": 0.6 };
function wingBeat(wing, size) {
  const beat = WING_BEAT[wing];
  const level = LEVEL_SMALL[(size && size.suffix) || ""];
  return level && (wing === 1 || wing === 3) ? { near: level, far: level + 0.05 } : beat;
}
function turn(p, about, f) {
  if (f === 1) return p;
  return [p[0], about[1] + (p[1] - about[1]) * f, ...p.slice(2)];
}

// A walk frame's gait, in the description's own units (a frame's pixel is
// 1 / scale of them, so a step reads the same at every size).
function gaitOf(build, frame, size) {
  if (build === "fly") return { ...STAND, wing: frame.wing };
  const reach = (build === "crawl" ? CRAWL_REACH : REACH[size.suffix]) / size.scale;
  const step = (pair) => ({ dx: frame[pair] * reach, swing: frame.lift === pair });
  return { near: step("A"), foreFar: step("A"), far: step("B"), foreNear: step("B"), wing: 0, walking: true };
}

// ---- Acting (task H12, PLAN-HATCHLING 6.3, G9) ----
// Every action must say what it is before a word is read: a predator that
// lunges and a dino that flinches say "it bit you". The frames are the
// standing description posed by place(), never redrawn: the feet stay on the
// ground line in every frame (a strike, a hurt and a meal are done standing).
//
// A STRIKE is two frames: the wind-up (the head drawn back and up, the body
// leaning back a pixel), then the blow. Its kind is the animal's weapon:
//   bite   the head thrust forward with the jaws open (every species not
//          named below; a plant eater bites too, and the jaws are what reads)
//   butt   the Pachycephalosaurus: the head turned down so the dome leads
//   horns  the horned dinos: the head lowered, the horns brought forward
//   tail   the Stegosaurus and the Kentrosaurus: the tail swung up, its
//          spikes high, the head kept
//   croc   the Deinosuchus: low, the jaws wide
//   beak   the Pteranodon: a stab with the beak open
// Offsets are whole pixels of the full-size frame (smaller sizes scale them,
// a moving part always moves at least a pixel); nod and pitch are turns in
// radians (place(): positive nose down). `forward` marks the blow: its head
// goes forward only as far as the frame has room in front of the standing
// animal, so a long snout (the Tyrannosaurus' fills the frame) is never cut by
// the frame's edge; its wind-up still draws it back.
const STRIKES = {
  bite: [
    { body: [-1, 0], head: [-3, 0], tail: [1, 0], nod: -0.2 },
    { body: [1, 0], head: [1, 1], nod: 0.08, jaw: 3.6, forward: true }
  ],
  butt: [
    { body: [-1, 0], head: [-2, 0], tail: [1, 0], nod: -0.25 },
    { body: [1, 0], head: [1, 2], nod: 0.8, forward: true }
  ],
  horns: [
    { body: [-1, 0], head: [-1, 0], tail: [1, 0], nod: -0.25 },
    { body: [1, 0], head: [1, 2], nod: 0.12, nodAt: "centre", forward: true }
  ],
  tail: [
    { body: [1, 0], tail: [-1, 2] },
    { body: [-1, 0], tail: [1, -7] }
  ],
  croc: [
    { body: [-1, 0], head: [-1, -1], tail: [1, 0], nod: -0.15 },
    { body: [1, 0], head: [0, 0], jaw: 3.4, forward: true }
  ],
  beak: [
    { body: [-1, 0], head: [-2, -1], tail: [1, 0], nod: -0.2 },
    { body: [1, 0], head: [1, 1], nod: 0.15, jaw: 2.4, forward: true }
  ]
};
const STRIKE_KIND = {
  Pachycephalosaurus: "butt", Triceratops: "horns", Diabloceratops: "horns",
  Stegosaurus: "tail", Kentrosaurus: "tail", Deinosuchus: "croc", Pteranodon: "beak"
};

// A MEAL is two frames, looping: the head down to the ground, then half up.
// A biped bends at the hip (`pitch`: its tail rises as its head falls, by
// `tailK` of the turn, or it stood on end); a
// quadruped only lowers its neck; the flyer bows its head on its neck (a
// pitch would turn its raised wings out of the frame); the
// crocodile dips its head with the jaws open. How far the head goes down is
// not a number per species: it is measured, so the lowest point of the head
// lands on the feet's row (`drop`, a part of the way: 1 down, 0.5 half up).
const EATS = {
  biped: [{ pitch: 0.55, tailK: 0.4, drop: 1 }, { pitch: 0.28, tailK: 0.4, drop: 0.5 }],
  quad: [{ drop: 1, nod: 0.15 }, { drop: 0.5 }],
  crawl: [{ drop: 1, jaw: 2.6 }, { drop: 0.5 }],
  fly: [{ drop: 1, nod: 0.6 }, { drop: 0.5, nod: 0.3 }]
};

// The flash of a hit (`hurt/0`): the standing frame lit near white inside its
// outline (engine flash()).
const HURT_TO = "#FFFDF6";
const HURT_K = 0.8;

// How much of a full-size offset each size moves (a smaller animal moves
// fewer pixels; a part that moves moves at least one).
const ACT_AMP = { "": 1, "-l": 1, "-m": 0.75, "-s": 0.6 };

// The actions a creature has, with their frames' ticks and whether they loop
// (A39; the plan's 6.2 and 6.3). build.js makes the atlas from this list.
const ACTIONS = [
  { action: "stand", ticks: [1], loops: true },
  { action: "walk", ticks: [3, 3, 3, 3], loops: true },
  { action: "strike", ticks: [3, 6], loops: false },
  { action: "hurt", ticks: [4], loops: false },
  { action: "eat", ticks: [4, 4], loops: true }
];

// The pivot point of a nod: the back of the skull, low, where the neck meets it.
const nodAbout = (skull) => [skull[0] - skull[2] * 0.6, skull[1] + skull[3] * 0.3];

const scaled = (v, k) => (v === 0 ? 0 : Math.sign(v) * Math.max(1, Math.round(Math.abs(v) * k)));
const scaledPair = (p, k) => (p ? [scaled(p[0], k), scaled(p[1], k)] : [0, 0]);

// The rightmost opaque column of an image.
function rightmost(img) {
  let best = -1;
  for (let y = 0; y < img.h; y++) for (let x = img.w - 1; x > best; x--) if (img.px[y * img.w + x][3] > 0) best = x;
  return best;
}

// The lowest edge of the head (its skull, snout and whatever rides them), in
// the new frame's pixels, after place().
function headBottom(placed) {
  let low = -Infinity;
  for (const p of placed.parts) {
    const bone = p.bone || "body";
    if (bone !== "head" && bone !== "snout") continue;
    if (p.kind === "ellipse") low = Math.max(low, p.cy + p.ry);
    else if (p.kind === "stroke") for (const q of p.points) low = Math.max(low, q[1] + q[2]);
    else for (const q of p.points) low = Math.max(low, q[1]);
  }
  return low;
}

// One frame of a species: its size (a SIZES entry), its action (ACTIONS) and
// the frame's index. The adult standing frame is the description drawn as it
// is, so it is the 36 x 25 sprite it always was, to the pixel.
function draw(species, size, action, index) {
  const build = BUILD[species] || "biped";
  if (action === "hurt") return flash(draw(species, size, "stand", 0), HURT_TO, HURT_K);
  if (action === "strike" || action === "eat") return act(species, build, size, action, index);
  const frame = action === "walk" ? WALKS[build][index] : null;
  const spec = SPECIES[species](frame ? gaitOf(build, frame, size) : STAND, size);
  if (!frame && size.scale === 1) return sprite(spec);
  const offsets = frame ? { body: frame.body, head: frame.head, tail: frame.tail, swing: [0, -LIFT] } : {};
  return sprite(place(spec, lookOf(spec, size, offsets)));
}

// The ground line lies 0.4 below the top of the pivot's row at every size,
// as G does on the 36 x 25 canvas; at full size the same point, exactly.
function lookOf(spec, size, offsets, more) {
  const to = size.scale === 1 ? [W / 2, G] : [size.px, size.py + 0.4];
  return { w: size.w, h: size.h, from: [W / 2, G], to, scale: size.scale, head: spec.head, grow: size.grow, offsets, ...(more || {}) };
}

function act(species, build, size, action, index) {
  const pose = action === "strike" ? STRIKES[STRIKE_KIND[species] || "bite"][index] : EATS[build][index];
  const k = ACT_AMP[size.suffix];
  const spec = SPECIES[species](pose.jaw ? { ...STAND, jaw: pose.jaw } : STAND, size);
  const body = scaledPair(pose.body, k);
  const head = scaledPair(pose.head, k);
  const tail = scaledPair(pose.tail, k);
  if (pose.forward) {
    // Room in front of the standing animal: the blow's head (on top of the
    // body's lean) goes no further forward than that.
    const room = size.w - 1 - rightmost(draw(species, size, "stand", 0));
    const ahead = Math.max(0, Math.min(body[0] + head[0], room));
    head[0] = ahead - body[0];
  }
  const more = {};
  if (pose.nod) more.nod = { about: pose.nodAt === "centre" ? [spec.skull[0], spec.skull[1]] : nodAbout(spec.skull), angle: pose.nod };
  if (pose.pitch) more.pitch = { about: spec.hip, angle: pose.pitch, tail: pose.tailK };
  if (more.nod || more.pitch) more.keepEyes = true;
  if (pose.drop) {
    // Measured: where the head's lowest edge is with the turns alone, and how
    // far it must come down to reach the feet's row (its edge 0.1 above the
    // pivot's row, so it covers the row above it and not the pivot's own).
    const bare = place(spec, lookOf(spec, size, { body, head: [0, 0], tail }, more));
    const down = Math.max(0, size.py - 0.1 - headBottom(bare));
    head[1] += Math.round(down * pose.drop);
  }
  // The feet stay on the ground line and nothing goes under it: a head
  // brought down (a horned dino's blow at a hatchling's size) that reaches
  // the pivot's row is raised until it does not.
  for (let lift = 0; ; lift++) {
    const img = sprite(place(spec, lookOf(spec, size, { body, head: [head[0], head[1] - lift], tail }, more)));
    if (lift >= 3 || !rowUsed(img, size.py)) return img;
  }
}

function rowUsed(img, y) {
  for (let x = 0; x < img.w; x++) if (y < img.h && img.px[y * img.w + x][3] > 0) return true;
  return false;
}

module.exports = { SPECIES, ORDER, SIZES, BUILD, ACTIONS, W, H, draw };
