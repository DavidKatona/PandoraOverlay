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
const { ellipse, stroke, poly, sprite, place } = require("../engine");

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
  if (o.snout) parts.push(stroke(o.snout, { ramp: o.snoutRamp, bone: "snout" }));
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
  const m = o.snout && o.mouth !== false ? mouth(o.snout, o.teeth, o.mouthFrom) : { marks: [], tips: [] };
  return { parts, eye, head: [o.head[0], o.head[1]], tips: [...tips, ...m.tips], marks: m.marks };
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
  if (o.snout) parts.push(stroke(o.snout, { ramp: o.snoutRamp, bone: "snout" }));
  (o.onTop || []).forEach((p) => parts.push(p));
  parts.push(...post(o.hindX, o.rump[1] + 1.2, o.legW[0], { sep: true }, share(g.near, -1)));
  parts.push(...post(o.foreX, o.shoulder[1] + 1.2, o.legW[1], { sep: true }, share(g.foreNear, -1)));
  (o.front || []).forEach((p) => parts.push(p));
  // Two rows under the top of the head, so a lighter row always sits above it.
  const eye = [Math.floor(o.head[0] + o.head[2] * 0.2), Math.ceil(o.head[1] - o.head[3] - 0.5) + 2];
  const m = o.snout && o.mouth !== false ? mouth(o.snout, false, o.mouthFrom) : { marks: [], tips: [] };
  return { parts, eye, head: [o.head[0], o.head[1]], tips: m.tips, marks: m.marks };
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
    ramps: { bone: BONE, ...o.ramps },
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
        head: [28, 16.4, 2.3, 1.6], snout: [[29, 16.7, 1.3], [31.4, 17.2, 0.9]],
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
          ellipse(21.2, 9.4, 4.4, 5.5, { ramp: "rim", bone: "head", orn: DIABLO_FRILL }),
          ellipse(21.2, 9.6, 3.3, 4.4, { ramp: "frill", bone: "head", orn: DIABLO_FRILL }),
          stroke([[19.8, 5.0, 0.95], [18.6, 2.4, 0.6], [16.8, 0.7, 0.35]], { ramp: "bone", thin: true, bone: "head", orn: DIABLO_FRILL })
        ],
        onTop: [stroke([[25.8, 12.8, 0.8], [28.0, 10.4, 0.35]], { ramp: "bone", thin: true, bone: "head", orn: true })]
      }, g),
      {
        ramps: { body: "#96744E", belly: "#D8C4A0", frill: "#A84E3A", rim: "#D9C29A" },
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
          ellipse(22.0, 9.0, 5.4, 6.0, { ramp: "rim", bone: "head", orn: TRIKE_FRILL }),
          ellipse(22.0, 9.2, 4.3, 4.9, { ramp: "frill", bone: "head", orn: TRIKE_FRILL })
        ],
        onTop: [
          stroke([[26.6, 11.6, 1.0], [33.4, 6.4, 0.4]], { ramp: "bone", thin: true, bone: "head", orn: true }),
          stroke([[30.0, 14.6, 0.8], [31.8, 11.8, 0.35]], { ramp: "bone", thin: true, bone: "snout", orn: true })
        ]
      }, g),
      {
        ramps: { body: "#8B7B62", belly: "#D5C8A8", frill: "#B26A48", rim: "#CDB88E" },
        marks: [[20, 8, "shadow", "frill"], [23, 7, "shadow", "frill"], [21, 11, "shadow", "frill"], [24, 10, "shadow", "frill"]],
        tips: [[32, 18, BONE], [33, 18, BONE]]
      }
    ),

  // The flyer. Its walk is a wing beat (g.wing, 0 to 3: up as it stands, level,
  // down, level): each wing is raised, flattened or mirrored under its shoulder
  // line (WING_BEAT), and the highlights along the near wing's edge go with it.
  Pteranodon: (g) => {
    const beat = WING_BEAT[(g || STAND).wing];
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
      stroke([[27.0, 11.8, 1.3], [34.2, 13.4, 0.45]], { ramp: "beak", thin: true, bone: "snout" }),
      stroke([[16.4, 16.6, 0.8], [12.6, 19.2, 0.5]], { limb: true })
    ];
    const edge = [[17, 7], [15, 5], [12, 4], [9, 3], [7, 2]].map(([x, y]) => {
      const [tx, ty] = beat.near === 1 ? [x, y] : turn([x + 0.5, y + 0.5], NEAR_SHOULDER, beat.near).map(Math.floor);
      return [tx, ty, "highlight", "wing"];
    });
    return {
      w: W, h: H,
      head: [25.2, 11.4],
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
        tips: [[1, 9, "#E8EEF2"], [1, 12, "#E8EEF2"], [2, 14, "#E8EEF2"], [21, 17, "#E8EEF2"]],
        eyeColour: AMBER, pupil: true,
        marks: [[2, 13, "highlight", "feather"], [3, 11, "highlight", "feather"], [20, 16, "highlight", "feather"], [22, 16, "highlight", "feather"], [16, 12, "highlight"], [18, 11, "highlight"], [12, 13, "highlight"]]
      }
    ),

  Herrerasaurus: (g) =>
    finish(
      biped({
        hip: [14, 12.6], chest: [21, 11.8], girth: [4.2, 3.6],
        tail: [[11.5, 12.2, 3.2], [6.5, 10.8, 2.0], [2, 9.2, 0.9], [0.5, 8.8, 0.5]],
        neck: [[22.6, 11, 2.8], [25.4, 9, 2.3]],
        head: [28, 8.2, 3.5, 2.6], snout: [[29.2, 8.4, 2.0], [34.4, 9.2, 1.25]], teeth: true,
        leg: { T: 5.2, w: 3.3, f: 3.5 }, arm: { len: 3.2, w: 0.95 }
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
        tips: [[16, 21, BONE], [17, 20, BONE], [0, 8, "#E9B58A"], [0, 11, "#E9B58A"], [1, 13, "#E9B58A"]]
      }
    ),

  Carnotaurus: (g) =>
    finish(
      biped({
        hip: [13.5, 11.8], chest: [20.5, 10.8], girth: [4.4, 3.9],
        tail: [[11.5, 11.6, 3.4], [6, 10.4, 2.1], [2, 9.0, 1.0], [0.5, 8.6, 0.5]],
        neck: [[22, 9.8, 3.0], [24.6, 7.8, 2.6]],
        head: [27.2, 6.6, 3.2, 2.9], snout: [[28.2, 7.0, 2.3], [31.6, 7.7, 1.7]], teeth: true,
        behind: [poly([[24.6, 4.8], [25.0, 1.6], [26.2, 4.4]], { ramp: "bone", far: true, thin: "shadow", bone: "head", orn: true })],
        onTop: [poly([[26.2, 4.6], [27.2, 0.8], [28.7, 4.4]], { ramp: "bone", thin: "light", bone: "head", orn: true })],
        leg: { T: 6, w: 3.3, f: 3.4 }, arm: { len: 1.5, w: 0.7 }
      }, g),
      {
        ramps: { body: "#A84A32", belly: "#E0C49C" },
        eye: [28, 6], eyeColour: AMBER, pupil: true,
        marks: [[8, 10, "shadow"], [11, 9, "shadow"], [12, 10, "shadow"], [15, 9, "shadow"], [18, 9, "shadow"], [19, 10, "shadow"], [5, 10, "shadow"], [22, 8, "shadow"]],
        ridges: [{ from: 6, to: 22, step: 3, colour: "#5E2418" }]
      }
    ),

  Ceratosaurus: (g) =>
    finish(
      biped({
        hip: [13.5, 12.4], chest: [21, 11.6], girth: [4.3, 3.7],
        tail: [[11.5, 12.2, 3.5], [6, 11, 2.3], [2, 9.6, 1.1], [0.5, 9.2, 0.5]],
        neck: [[22.6, 10.8, 2.9], [25.2, 8.8, 2.4]],
        head: [27.8, 7.8, 3.5, 2.7], snout: [[29, 8.1, 2.1], [33.8, 8.9, 1.4]], teeth: true,
        onTop: [
          poly([[30.4, 7.3], [31.4, 3.6], [32.7, 7.5]], { ramp: "bone", thin: "light", bone: "snout", orn: true }),
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
    const m = mouth(snout, true, 0);
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
      stroke(snout, { belly: true, bone: "snout" }),
      ellipse(34.2, 17.0, 1.25, 1.2, { bone: "snout" }),
      ellipse(26.0, 14.8, 1.3, 1.05, { bone: "head" }),
      ...limb([11.2, 19.2, 1.6], [10.0, 22.0, 1.15], [[9.8, 22.9, 0.9], [12.8, 23.3, 0.75]], g.near, { limb: true, sep: true }),
      ...limb([20.2, 19.2, 1.6], [21.2, 22.0, 1.15], [[21.0, 22.9, 0.9], [24.0, 23.3, 0.75]], g.foreNear, { limb: true, sep: true })
    ];
    return {
      w: W, h: H,
      head: [26.6, 16.6],
      ramps: { body: "#50623F", belly: "#C9BC84", bone: BONE },
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
        hip: [13, 12], chest: [21, 11], girth: [4.8, 4.2],
        tail: [[10.8, 11.8, 3.8], [5.6, 10.4, 2.4], [1.8, 8.8, 1.1], [0.5, 8.4, 0.5]],
        neck: [[22.8, 10, 3.2], [25.4, 8, 2.8]],
        head: [28.2, 7.2, 3.9, 2.9], snout: [[29.4, 7.5, 2.3], [34.6, 8.4, 1.6]], teeth: true,
        onTop: [poly([[27.0, 5.1], [28.0, 2.6], [29.7, 5.0]], { ramp: "crest", sep: true, bone: "head", orn: true })],
        leg: { T: 5.8, w: 3.7, f: 3.6 }, arm: { len: 3.6, w: 1.1 }
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
          stroke([[20.4, 12.8, 1.1], [23.6, 15.2, 0.8]], { limb: true, sep: true }),
          stroke([[23.8, 15.2, 0.5], [27.0, 17.4, 0.3]], { ramp: "bone", thin: true, orn: true }),
          stroke([[23.8, 15.4, 0.5], [26.0, 18.6, 0.3]], { ramp: "bone", thin: true, orn: true }),
          stroke([[23.4, 15.6, 0.5], [24.6, 19.0, 0.3]], { ramp: "bone", thin: true, orn: true })
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
        head: [26.2, 3.2, 2.4, 1.7], snout: [[27.2, 3.5, 1.3], [30.8, 4.2, 0.75]], snoutRamp: "beak", mouth: false,
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

// One frame of a species: its size (a SIZES entry), its action ("stand" or
// "walk") and the frame's index. The adult standing frame is the description
// drawn as it is, so it is the 36 x 25 sprite it always was, to the pixel.
function draw(species, size, action, index) {
  const build = BUILD[species] || "biped";
  const frame = action === "walk" ? WALKS[build][index] : null;
  const spec = SPECIES[species](frame ? gaitOf(build, frame, size) : STAND, size);
  if (!frame && size.scale === 1) return sprite(spec);
  const offsets = frame ? { body: frame.body, head: frame.head, tail: frame.tail, swing: [0, -LIFT] } : {};
  // The ground line lies 0.4 below the top of the pivot's row at every size,
  // as G does on the 36 x 25 canvas; at full size the same point, exactly.
  const to = size.scale === 1 ? [W / 2, G] : [size.px, size.py + 0.4];
  return sprite(
    place(spec, {
      w: size.w,
      h: size.h,
      from: [W / 2, G],
      to,
      scale: size.scale,
      head: spec.head,
      grow: size.grow,
      offsets
    })
  );
}

module.exports = { SPECIES, ORDER, SIZES, BUILD, W, H, draw };
