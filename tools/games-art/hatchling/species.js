// The 21 species of the server, each an original sprite facing right on a
// 36 x 25 canvas. Shapes follow the real animals' build (what a field guide
// would show), not any game's art.
const { ellipse, stroke, poly, sprite, ramp } = require("../engine");

const W = 36;
const H = 25;
const G = 24.4; // the ground
const BONE = "#E4D9BC";
const TOOTH = "#F4EEDC";
const DARK_EYE = "#14140E";

function leg(hx, hy, L, more) {
  const ankleX = hx + 0.5;
  return [
    ellipse(hx + 0.4, hy + L.T * 0.5, L.w, L.T * 0.62 + 0.5, { ...more, sep: Boolean(more.sep) && L.w >= 3.2 }),
    stroke([[hx + 1.3, hy + L.T, L.w * 0.5], [ankleX, G - 2.7, 0.95]], { ...more, limb: !more.far }),
    stroke([[ankleX + 0.1, G - 1.5, 0.95], [ankleX + L.f, G - 1.1, 0.75]], { ...more, limb: !more.far })
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
function biped(o) {
  const L = { T: 5, w: 3.1, f: 3.2, ...(o.leg || {}) };
  const [hx, hy] = o.hip;
  const [cx, cy] = o.chest;
  const parts = [];
  parts.push(...leg(hx - (o.stride === undefined ? 1.9 : o.stride), hy + 0.4, L, { far: true }));
  if (o.arm) parts.push(stroke(armPts(cx - 1.0, cy + 0.9, o.arm), { far: true }));
  if (o.foreleg) parts.push(stroke([[cx - 1.4, cy + 1.4, o.foreleg], [cx - 0.9, G - 1.7, o.foreleg * 0.8]], { far: true }));
  (o.behind || []).forEach((p) => parts.push(p));
  parts.push(stroke(o.tail));
  parts.push(stroke([[hx, hy, o.girth[0]], [cx, cy, o.girth[1]]], { belly: true }));
  parts.push(stroke(o.neck, { belly: true }));
  parts.push(ellipse(o.head[0], o.head[1], o.head[2], o.head[3]));
  if (o.snout) parts.push(stroke(o.snout, { ramp: o.snoutRamp }));
  (o.onTop || []).forEach((p) => parts.push(p));
  parts.push(...leg(hx, hy, L, { sep: true }));
  if (o.arm) parts.push(stroke(armPts(cx, cy + 1.1, o.arm), { limb: true, sep: true }));
  if (o.foreleg) {
    parts.push(stroke([[cx + 0.3, cy + 1.5, o.foreleg], [cx + 0.9, G - 1.7, o.foreleg * 0.8]], { limb: true, sep: true }));
    parts.push(ellipse(cx + 1.3, G - 1.05, o.foreleg + 0.35, 0.95, { limb: true }));
  }
  (o.front || []).forEach((p) => parts.push(p));
  // Two rows under the top of the head, so a lighter row always sits above it.
  const eye = [Math.floor(o.head[0] + o.head[2] * 0.2), Math.ceil(o.head[1] - o.head[3] - 0.5) + 2];
  const tips = [[Math.floor(hx + 0.5 + L.f + 0.55), Math.floor(G - 1.1), BONE]];
  if (o.arm) tips.push([Math.floor(cx + o.arm.len * 0.5 + 0.35), Math.floor(cy + 1.1 + o.arm.len * 0.85 + 0.25), BONE]);
  const m = o.snout && o.mouth !== false ? mouth(o.snout, o.teeth, o.mouthFrom) : { marks: [], tips: [] };
  return { parts, eye, tips: [...tips, ...m.tips], marks: m.marks };
}

// A four-legged build: rump and shoulder set the body.
function quad(o) {
  const parts = [];
  const post = (x, topY, w, more) => [
    stroke([[x, topY, w], [x + (o.lean || 0), G - 2.0, w * 0.88]], { ...more, limb: !more.far }),
    ellipse(x + (o.lean || 0) + 0.45, G - 1.1, w + 0.5, 1.0, { ...more, limb: !more.far })
  ];
  parts.push(...post(o.hindX - 2.1, o.rump[1] + 1.5, o.legW[0], { far: true }));
  parts.push(...post(o.foreX - 2.1, o.shoulder[1] + 1.5, o.legW[1], { far: true }));
  (o.behind || []).forEach((p) => parts.push(p));
  parts.push(stroke(o.tail));
  parts.push(stroke([o.rump, o.shoulder], { belly: true }));
  if (o.neck) parts.push(stroke(o.neck, { belly: true }));
  (o.mid || []).forEach((p) => parts.push(p));
  parts.push(ellipse(o.head[0], o.head[1], o.head[2], o.head[3]));
  if (o.snout) parts.push(stroke(o.snout, { ramp: o.snoutRamp }));
  (o.onTop || []).forEach((p) => parts.push(p));
  parts.push(...post(o.hindX, o.rump[1] + 1.2, o.legW[0], { sep: true }));
  parts.push(...post(o.foreX, o.shoulder[1] + 1.2, o.legW[1], { sep: true }));
  (o.front || []).forEach((p) => parts.push(p));
  // Two rows under the top of the head, so a lighter row always sits above it.
  const eye = [Math.floor(o.head[0] + o.head[2] * 0.2), Math.ceil(o.head[1] - o.head[3] - 0.5) + 2];
  const m = o.snout && o.mouth !== false ? mouth(o.snout, false, o.mouthFrom) : { marks: [], tips: [] };
  return { parts, eye, tips: m.tips, marks: m.marks };
}

function finish(build, o) {
  const eyeAt = o.eye || build.eye;
  const extras = [[eyeAt[0], eyeAt[1], o.eyeColour || DARK_EYE]];
  if (o.pupil) extras.push([eyeAt[0] + 1, eyeAt[1], "#1A0E0A"]);
  return sprite({
    w: W,
    h: H,
    ramps: { bone: BONE, ...o.ramps },
    parts: build.parts,
    stripes: o.stripes || [],
    marks: [...build.marks, ...(o.marks || [])],
    ridges: o.ridges || [],
    tips: [...build.tips, ...(o.tips || [])],
    extras: [...extras, ...(o.extras || [])]
  });
}

const stripes = (xs, len, more) => xs.map((x) => ({ x, len, ...(more || {}) }));
const AMBER = "#FFC864";

const SPECIES = {
  Dryosaurus: () =>
    finish(
      biped({
        hip: [14, 13], chest: [20, 12.5], girth: [3.9, 3.2],
        tail: [[12, 12.6, 3.0], [7, 11.3, 1.9], [2.5, 9.8, 0.9], [0.6, 9.2, 0.5]],
        neck: [[21.5, 11.6, 2.5], [24.2, 8.6, 1.8], [25.8, 6.8, 1.6]],
        head: [27.6, 6.0, 3.0, 2.2], snout: [[28.8, 6.3, 1.7], [32.4, 7.0, 0.95]],
        leg: { T: 5, w: 3.0, f: 3.2 }, arm: { len: 3.2, w: 0.9 }
      }),
      { ramps: { body: "#5E8547", belly: "#D3C995" }, stripes: stripes([5, 8, 11, 14, 17], 1, { tone: "shadow" }) }
    ),

  Hypsilophodon: () =>
    finish(
      biped({
        hip: [15, 14.4], chest: [20, 13.6], girth: [3.3, 2.8],
        tail: [[13.5, 14, 2.5], [9, 12.8, 1.6], [5, 11.2, 0.9], [2.6, 10.2, 0.5]],
        neck: [[21, 12.6, 2.2], [23, 10.2, 1.7], [24.2, 8.8, 1.5]],
        head: [25.8, 8.0, 2.9, 2.3], snout: [[27, 8.3, 1.6], [29.9, 8.9, 1.0]],
        leg: { T: 4.2, w: 2.6, f: 2.8 }, arm: { len: 2.6, w: 0.8 }, stride: 1.6
      }),
      { ramps: { body: "#C08A4A", belly: "#EBDDB4" }, stripes: stripes([7, 10, 13, 16, 19], 2, { tone: "shadow" }) }
    ),

  Kentrosaurus: () =>
    finish(
      quad({
        rump: [12.5, 13, 4.4], shoulder: [20, 14.2, 3.6], hindX: 12, foreX: 20.6, legW: [1.8, 1.5],
        tail: [[10, 13, 3.0], [5.5, 13, 1.9], [2, 12.6, 1.0], [0.6, 12.4, 0.6]],
        neck: [[22.6, 14.6, 2.2], [25.8, 15.8, 1.7]],
        head: [28, 16.4, 2.3, 1.6], snout: [[29, 16.7, 1.3], [31.4, 17.2, 0.9]],
        onTop: [
          poly([[20.4, 11.6], [21.6, 8.0], [22.9, 11.8]], { ramp: "plate", sep: true }),
          poly([[17.4, 10.8], [18.8, 6.6], [20.3, 11.0]], { ramp: "plate", sep: true }),
          poly([[14.4, 9.8], [16.0, 5.4], [17.5, 10.0]], { ramp: "plate", sep: true }),
          stroke([[12.6, 9.4, 0.8], [11.2, 3.4, 0.35]], { ramp: "bone", thin: true }),
          stroke([[9.6, 10.0, 0.8], [7.6, 4.2, 0.35]], { ramp: "bone", thin: true }),
          stroke([[6.6, 11.3, 0.75], [4.2, 6.0, 0.35]], { ramp: "bone", thin: true }),
          stroke([[3.8, 11.9, 0.7], [1.6, 7.4, 0.35]], { ramp: "bone", thin: true })
        ]
      }),
      { ramps: { body: "#647A5C", belly: "#CBCBA6", plate: "#B5653A" } }
    ),

  Pachycephalosaurus: () =>
    finish(
      biped({
        hip: [13.5, 12.5], chest: [20.5, 11.5], girth: [4.6, 4.0],
        tail: [[11.5, 12.2, 3.4], [6.5, 11, 2.2], [2.2, 9.8, 1.0], [0.6, 9.4, 0.5]],
        neck: [[22, 10.6, 2.9], [24.6, 8.4, 2.4]],
        head: [26.8, 6.8, 3.0, 2.5], snout: [[28.2, 7.5, 1.7], [31.2, 8.3, 1.1]],
        onTop: [ellipse(26.4, 5.3, 2.8, 2.0, { ramp: "dome" })],
        leg: { T: 5.2, w: 3.4, f: 3.3 }, arm: { len: 2.8, w: 0.9 }
      }),
      {
        ramps: { body: "#86764A", belly: "#DCCFA6", dome: "#C2A873" },
        eye: [27, 7],
        tips: [[23, 6, BONE], [23, 8, BONE], [30, 7, BONE]],
        stripes: stripes([6, 9, 12, 15, 18], 2, { tone: "shadow" })
      }
    ),

  Tenontosaurus: () =>
    finish(
      biped({
        hip: [14.5, 11.6], chest: [22, 12.8], girth: [4.8, 4.0],
        tail: [[12, 11.6, 4.2], [6, 11.2, 3.0], [2, 10.8, 1.9], [0.5, 10.6, 1.1]],
        neck: [[23.6, 11.8, 2.6], [26.4, 9.4, 2.0]],
        head: [28.6, 8.6, 2.8, 2.1], snout: [[29.8, 9.0, 1.7], [32.8, 9.8, 1.2]],
        leg: { T: 5.8, w: 3.6, f: 3.2 }, foreleg: 1.3
      }),
      { ramps: { body: "#7F8C62", belly: "#D9D3AE" }, stripes: stripes([2, 5, 8, 11, 14, 17, 20], 3, { tone: "shadow" }) }
    ),

  Diabloceratops: () =>
    finish(
      quad({
        rump: [10.6, 14.0, 4.0], shoulder: [17.4, 14.0, 4.0], hindX: 10.0, foreX: 17.6, legW: [1.9, 1.8],
        tail: [[8.2, 14.2, 2.7], [4.4, 15.4, 1.6], [2.0, 16.4, 0.9]],
        head: [24.8, 15.0, 3.7, 3.0], snout: [[27.0, 15.8, 2.3], [30.4, 17.4, 1.35], [31.4, 18.3, 0.9]],
        mid: [
          stroke([[22.8, 4.8, 0.95], [24.0, 2.2, 0.6], [25.8, 0.6, 0.35]], { ramp: "bone", thin: "shadow" }),
          ellipse(21.2, 9.4, 4.4, 5.5, { ramp: "rim" }),
          ellipse(21.2, 9.6, 3.3, 4.4, { ramp: "frill" }),
          stroke([[19.8, 5.0, 0.95], [18.6, 2.4, 0.6], [16.8, 0.7, 0.35]], { ramp: "bone", thin: true })
        ],
        onTop: [stroke([[25.8, 12.8, 0.8], [28.0, 10.4, 0.35]], { ramp: "bone", thin: true })]
      }),
      {
        ramps: { body: "#96744E", belly: "#D8C4A0", frill: "#A84E3A", rim: "#D9C29A" },
        marks: [[20, 8, "shadow", "frill"], [22, 10, "shadow", "frill"], [20, 12, "shadow", "frill"]],
        tips: [[31, 18, BONE], [30, 18, BONE]]
      }
    ),

  Maiasaura: () =>
    finish(
      biped({
        hip: [13, 11.8], chest: [21, 12.6], girth: [5.0, 4.4],
        tail: [[10.6, 11.6, 4.0], [5.4, 10.6, 2.6], [1.8, 9.6, 1.2], [0.5, 9.4, 0.6]],
        neck: [[22.6, 11.4, 3.0], [25.4, 9.2, 2.4]],
        head: [27.6, 8.2, 3.0, 2.3], snout: [[28.8, 8.6, 1.8], [32.2, 9.7, 1.55], [33.6, 10.0, 1.45]], snoutRamp: "bill", mouth: false,
        onTop: [poly([[26.8, 6.7], [27.9, 4.6], [29.3, 6.6]], { ramp: "crest", sep: true })],
        leg: { T: 6, w: 3.9, f: 3.4 }, foreleg: 1.25
      }),
      {
        ramps: { body: "#B58F58", belly: "#EADBB6", bill: "#8A7658", crest: "#9A6238" },
        stripes: stripes([4, 8, 12, 16, 20], 2, { tone: "shadow" })
      }
    ),

  Stegosaurus: () =>
    finish(
      quad({
        rump: [12, 11.6, 5.2], shoulder: [20.5, 13.4, 4.2], hindX: 11.4, foreX: 21.4, legW: [2.0, 1.6],
        tail: [[9.5, 11.8, 3.6], [5, 12.4, 2.2], [1.8, 12.8, 1.2], [0.6, 12.8, 0.7]],
        neck: [[23.5, 13.8, 2.6], [27, 15.4, 1.9]],
        head: [29.6, 16.2, 2.6, 1.7], snout: [[30.6, 16.5, 1.4], [33.2, 17.1, 0.95]],
        behind: [
          poly([[16.2, 7.4], [17.8, 2.2], [19.6, 8.2]], { ramp: "plate", far: true }),
          poly([[8.4, 7.8], [10.2, 2.4], [12.0, 6.8]], { ramp: "plate", far: true })
        ],
        onTop: [
          poly([[21.2, 10.6], [22.6, 6.4], [24.1, 10.8]], { ramp: "plate", sep: true }),
          poly([[18.2, 9.0], [19.9, 3.8], [21.7, 9.4]], { ramp: "plate", sep: true }),
          poly([[14.3, 7.4], [16.2, 1.0], [18.2, 7.8]], { ramp: "plate", sep: true }),
          poly([[10.3, 7.0], [12.2, 0.8], [14.2, 7.0]], { ramp: "plate", sep: true }),
          poly([[6.7, 8.6], [8.4, 3.4], [10.2, 7.8]], { ramp: "plate", sep: true }),
          poly([[3.9, 10.6], [5.2, 6.8], [6.6, 10.0]], { ramp: "plate", sep: true }),
          stroke([[2.4, 12.2, 0.7], [0.5, 8.8, 0.35]], { ramp: "bone", thin: true }),
          stroke([[3.8, 11.8, 0.7], [2.6, 8.0, 0.35]], { ramp: "bone", thin: true })
        ]
      }),
      { ramps: { body: "#6F8058", belly: "#CFCBA4", plate: "#C8623A" } }
    ),

  Triceratops: () =>
    finish(
      quad({
        rump: [10.2, 13.2, 4.7], shoulder: [17.2, 13.4, 4.7], hindX: 9.6, foreX: 17.6, legW: [2.1, 2.0],
        tail: [[7.6, 13.6, 3.0], [3.8, 15.2, 1.7], [1.6, 16.4, 0.9]],
        head: [25.2, 14.2, 4.2, 3.4], snout: [[27.8, 15.2, 2.6], [31.6, 17.0, 1.5], [32.8, 18.0, 1.0]],
        mid: [
          stroke([[25.0, 11.0, 0.9], [30.6, 5.4, 0.4]], { ramp: "bone", thin: "shadow" }),
          ellipse(22.0, 9.0, 5.4, 6.0, { ramp: "rim" }),
          ellipse(22.0, 9.2, 4.3, 4.9, { ramp: "frill" })
        ],
        onTop: [
          stroke([[26.6, 11.6, 1.0], [33.4, 6.4, 0.4]], { ramp: "bone", thin: true }),
          stroke([[30.0, 14.6, 0.8], [31.8, 11.8, 0.35]], { ramp: "bone", thin: true })
        ]
      }),
      {
        ramps: { body: "#8B7B62", belly: "#D5C8A8", frill: "#B26A48", rim: "#CDB88E" },
        marks: [[20, 8, "shadow", "frill"], [23, 7, "shadow", "frill"], [21, 11, "shadow", "frill"], [24, 10, "shadow", "frill"]],
        tips: [[32, 18, BONE], [33, 18, BONE]]
      }
    ),

  Pteranodon: () => {
    const parts = [
      poly([[20.8, 13.4], [22.8, 5.6], [27.6, 1.4], [26.0, 6.8], [23.2, 13.0]], { ramp: "wing", far: true }),
      stroke([[15.4, 16.0, 0.8], [11.4, 18.4, 0.5]], { far: true }),
      poly([[19.2, 13.6], [17.6, 6.2], [12.4, 2.8], [5.4, 1.2], [9.4, 7.4], [12.8, 12.6], [15.0, 16.2]], { ramp: "wing" }),
      ellipse(17.6, 15.2, 3.5, 2.3, { belly: true }),
      stroke([[20.2, 14.4, 1.6], [23.4, 12.2, 1.4]]),
      stroke([[24.0, 10.4, 1.2], [20.6, 7.6, 0.8], [17.6, 6.2, 0.45]], { ramp: "crest", thin: true }),
      ellipse(25.2, 11.4, 2.5, 2.0),
      stroke([[27.0, 11.8, 1.3], [34.2, 13.4, 0.45]], { ramp: "beak", thin: true }),
      stroke([[16.4, 16.6, 0.8], [12.6, 19.2, 0.5]], { limb: true })
    ];
    return sprite({
      w: W, h: H,
      ramps: { body: "#D2CAB6", belly: "#F1ECDD", wing: "#7F92A3", crest: "#D9762B", beak: "#E3BD55", bone: BONE },
      parts,
      marks: [[17, 7, "highlight", "wing"], [15, 5, "highlight", "wing"], [12, 4, "highlight", "wing"], [9, 3, "highlight", "wing"], [7, 2, "highlight", "wing"]],
      extras: [[26, 11, DARK_EYE]]
    });
  },

  Troodon: () =>
    finish(
      biped({
        hip: [15, 14], chest: [20.5, 13], girth: [3.2, 2.7],
        tail: [[13.4, 13.6, 2.2], [8, 12.6, 1.2], [4.6, 12.0, 0.8]],
        neck: [[21.6, 12, 2.0], [23.6, 9.4, 1.6], [24.6, 8.0, 1.5]],
        head: [26.2, 7.2, 2.8, 2.1], snout: [[27.4, 7.5, 1.5], [31.0, 8.2, 0.9]], teeth: true, mouthFrom: 1,
        behind: [stroke([[5.4, 11.8, 1.0], [1.6, 9.8, 0.5]], { ramp: "feather", thin: true }), stroke([[5.4, 12.2, 1.0], [1.0, 12.2, 0.5]], { ramp: "feather", thin: true }), stroke([[5.4, 12.4, 1.0], [1.8, 14.4, 0.5]], { ramp: "feather", thin: true })],
        onTop: [poly([[23.6, 6.4], [24.6, 4.2], [26.4, 5.4]], { ramp: "feather", sep: true })],
        front: [poly([[19.4, 13.6], [23.2, 14.4], [22.2, 17.6], [18.6, 16.0]], { ramp: "feather", sep: true })],
        leg: { T: 4.4, w: 2.5, f: 3.0 }, stride: 1.6
      }),
      {
        ramps: { body: "#6C7C8C", belly: "#D4D8D8", feather: "#4F6274" },
        tips: [[1, 9, "#E8EEF2"], [1, 12, "#E8EEF2"], [2, 14, "#E8EEF2"], [21, 17, "#E8EEF2"]],
        eyeColour: AMBER, pupil: true,
        marks: [[2, 13, "highlight", "feather"], [3, 11, "highlight", "feather"], [20, 16, "highlight", "feather"], [22, 16, "highlight", "feather"], [16, 12, "highlight"], [18, 11, "highlight"], [12, 13, "highlight"]]
      }
    ),

  Herrerasaurus: () =>
    finish(
      biped({
        hip: [14, 12.6], chest: [21, 11.8], girth: [4.2, 3.6],
        tail: [[11.5, 12.2, 3.2], [6.5, 10.8, 2.0], [2, 9.2, 0.9], [0.5, 8.8, 0.5]],
        neck: [[22.6, 11, 2.8], [25.4, 9, 2.3]],
        head: [28, 8.2, 3.5, 2.6], snout: [[29.2, 8.4, 2.0], [34.4, 9.2, 1.25]], teeth: true,
        leg: { T: 5.2, w: 3.3, f: 3.5 }, arm: { len: 3.2, w: 0.95 }
      }),
      { ramps: { body: "#9A4E30", belly: "#E2C496" }, eyeColour: AMBER, pupil: true, stripes: stripes([4, 7, 10, 13, 16, 19], 2, { tone: "shadow" }) }
    ),

  Omniraptor: () =>
    finish(
      biped({
        hip: [14, 12.8], chest: [20.5, 11.6], girth: [4.0, 3.5],
        tail: [[12, 12.4, 2.6], [6, 11.4, 1.4], [3.0, 10.9, 0.9]],
        neck: [[22, 10.6, 2.6], [24.4, 8.2, 2.1]],
        head: [26.8, 6.8, 3.2, 2.3], snout: [[28, 7.1, 1.8], [32.6, 7.9, 1.15]], teeth: true,
        behind: [stroke([[4.4, 10.8, 1.1], [0.8, 8.6, 0.5]], { ramp: "feather", thin: true }), stroke([[4.4, 11.0, 1.1], [0.4, 11.0, 0.5]], { ramp: "feather", thin: true }), stroke([[4.4, 11.2, 1.1], [1.0, 13.4, 0.5]], { ramp: "feather", thin: true })],
        onTop: [poly([[24.0, 5.6], [25.2, 3.2], [27.0, 4.7]], { ramp: "feather", sep: true })],
        front: [poly([[19.3, 12.2], [23.8, 13.2], [22.6, 17.4], [18.4, 15.2]], { ramp: "feather", sep: true })],
        leg: { T: 5.2, w: 3.2, f: 3.4 }
      }),
      {
        ramps: { body: "#57483C", belly: "#D9C09A", feather: "#A9572F" },
        eyeColour: AMBER, pupil: true,
        marks: [[1, 12, "highlight", "feather"], [2, 10, "highlight", "feather"], [20, 15, "highlight", "feather"], [22, 16, "highlight", "feather"]],
        tips: [[16, 21, BONE], [17, 20, BONE], [0, 8, "#E9B58A"], [0, 11, "#E9B58A"], [1, 13, "#E9B58A"]]
      }
    ),

  Carnotaurus: () =>
    finish(
      biped({
        hip: [13.5, 11.8], chest: [20.5, 10.8], girth: [4.4, 3.9],
        tail: [[11.5, 11.6, 3.4], [6, 10.4, 2.1], [2, 9.0, 1.0], [0.5, 8.6, 0.5]],
        neck: [[22, 9.8, 3.0], [24.6, 7.8, 2.6]],
        head: [27.2, 6.6, 3.2, 2.9], snout: [[28.2, 7.0, 2.3], [31.6, 7.7, 1.7]], teeth: true,
        behind: [poly([[24.6, 4.8], [25.0, 1.6], [26.2, 4.4]], { ramp: "bone", far: true, thin: "shadow" })],
        onTop: [poly([[26.2, 4.6], [27.2, 0.8], [28.7, 4.4]], { ramp: "bone", thin: "light" })],
        leg: { T: 6, w: 3.3, f: 3.4 }, arm: { len: 1.5, w: 0.7 }
      }),
      {
        ramps: { body: "#A84A32", belly: "#E0C49C" },
        eye: [28, 6], eyeColour: AMBER, pupil: true,
        marks: [[8, 10, "shadow"], [11, 9, "shadow"], [12, 10, "shadow"], [15, 9, "shadow"], [18, 9, "shadow"], [19, 10, "shadow"], [5, 10, "shadow"], [22, 8, "shadow"]],
        ridges: [{ from: 6, to: 22, step: 3, colour: "#5E2418" }]
      }
    ),

  Ceratosaurus: () =>
    finish(
      biped({
        hip: [13.5, 12.4], chest: [21, 11.6], girth: [4.3, 3.7],
        tail: [[11.5, 12.2, 3.5], [6, 11, 2.3], [2, 9.6, 1.1], [0.5, 9.2, 0.5]],
        neck: [[22.6, 10.8, 2.9], [25.2, 8.8, 2.4]],
        head: [27.8, 7.8, 3.5, 2.7], snout: [[29, 8.1, 2.1], [33.8, 8.9, 1.4]], teeth: true,
        onTop: [
          poly([[30.4, 7.3], [31.4, 3.6], [32.7, 7.5]], { ramp: "bone", thin: "light" }),
          poly([[26.5, 5.9], [27.2, 3.6], [28.5, 5.7]], { ramp: "bone", thin: "light" })
        ],
        leg: { T: 5.4, w: 3.4, f: 3.5 }, arm: { len: 3.0, w: 0.95 }
      }),
      {
        ramps: { body: "#B48C3E", belly: "#E8D8A8" },
        eyeColour: AMBER, pupil: true,
        stripes: stripes([3, 6, 9, 12, 15, 18, 21], 3, { tone: "shadow" }),
        ridges: [{ from: 3, to: 24, step: 2, colour: "#4A3410" }]
      }
    ),

  Dilophosaurus: () =>
    finish(
      biped({
        hip: [13.5, 12.8], chest: [20.5, 11.8], girth: [3.8, 3.2],
        tail: [[11.5, 12.4, 3.0], [6, 11, 1.9], [2, 9.6, 0.9], [0.5, 9.2, 0.5]],
        neck: [[22, 10.6, 2.4], [24.6, 7.8, 1.9], [25.8, 6.4, 1.7]],
        head: [27.8, 5.9, 3.2, 2.1], snout: [[29, 6.2, 1.7], [33.6, 7.1, 1.1]], teeth: true,
        behind: [poly([[24.6, 4.8], [25.2, 2.6], [27.0, 1.6], [28.6, 2.6], [29.0, 4.6]], { ramp: "crest", far: true })],
        onTop: [poly([[25.6, 4.9], [26.4, 2.2], [28.4, 1.0], [30.2, 2.2], [30.8, 4.8]], { ramp: "crest", sep: true })],
        leg: { T: 5.4, w: 3.0, f: 3.4 }, arm: { len: 3.4, w: 0.9 }
      }),
      {
        ramps: { body: "#72904C", belly: "#E0D89C", crest: "#C84A34" },
        eye: [28, 5], eyeColour: AMBER, pupil: true,
        stripes: stripes([4, 7, 10, 13, 16, 19, 22], 2, { tone: "shadow" })
      }
    ),

  Deinosuchus: () => {
    const snout = [[28.2, 17.0, 1.9], [34.4, 17.7, 1.35]];
    const m = mouth(snout, true, 0);
    const parts = [
      stroke([[9.2, 18.8, 1.4], [8.0, 22.0, 1.05]], { far: true }),
      stroke([[7.8, 22.9, 0.85], [10.4, 23.3, 0.7]], { far: true }),
      stroke([[18.4, 18.8, 1.4], [19.4, 22.0, 1.05]], { far: true }),
      stroke([[19.2, 22.9, 0.85], [22.0, 23.3, 0.7]], { far: true }),
      stroke([[8.5, 17.6, 3.0], [4, 16.6, 1.9], [1.4, 15.0, 1.0], [0.5, 14.0, 0.6]]),
      stroke([[9.5, 17.6, 3.4], [19, 17.4, 3.6]], { belly: true }),
      stroke([[21.5, 17.2, 3.0], [24.6, 16.8, 2.6]], { belly: true }),
      ellipse(26.6, 16.6, 2.9, 2.3, { belly: true }),
      stroke(snout, { belly: true }),
      ellipse(34.2, 17.0, 1.25, 1.2),
      ellipse(26.0, 14.8, 1.3, 1.05),
      stroke([[11.2, 19.2, 1.6], [10.0, 22.0, 1.15]], { limb: true, sep: true }),
      stroke([[9.8, 22.9, 0.9], [12.8, 23.3, 0.75]], { limb: true }),
      stroke([[20.2, 19.2, 1.6], [21.2, 22.0, 1.15]], { limb: true, sep: true }),
      stroke([[21.0, 22.9, 0.9], [24.0, 23.3, 0.75]], { limb: true })
    ];
    return sprite({
      w: W, h: H,
      ramps: { body: "#50623F", belly: "#C9BC84", bone: BONE },
      parts,
      stripes: stripes([2, 4, 6, 9, 12, 15, 18, 21], 2, { tone: "shadow" }),
      marks: m.marks,
      tips: [...m.tips, [12, 23, BONE], [24, 23, BONE]],
      ridges: [{ from: 1, to: 24, step: 2, colour: "#1F2A1A" }],
      extras: [[26, 14, AMBER], [27, 14, "#1A0E0A"]]
    });
  },

  Allosaurus: () =>
    finish(
      biped({
        hip: [13, 12], chest: [21, 11], girth: [4.8, 4.2],
        tail: [[10.8, 11.8, 3.8], [5.6, 10.4, 2.4], [1.8, 8.8, 1.1], [0.5, 8.4, 0.5]],
        neck: [[22.8, 10, 3.2], [25.4, 8, 2.8]],
        head: [28.2, 7.2, 3.9, 2.9], snout: [[29.4, 7.5, 2.3], [34.6, 8.4, 1.6]], teeth: true,
        onTop: [poly([[27.0, 5.1], [28.0, 2.6], [29.7, 5.0]], { ramp: "crest", sep: true })],
        leg: { T: 5.8, w: 3.7, f: 3.6 }, arm: { len: 3.6, w: 1.1 }
      }),
      {
        ramps: { body: "#6F7F86", belly: "#D6D2C0", crest: "#B5603A" },
        eye: [29, 6], eyeColour: AMBER, pupil: true,
        stripes: stripes([3, 6, 9, 12, 15, 18, 21], 2, { tone: "#B5603A" })
      }
    ),

  Tyrannosaurus: () =>
    finish(
      biped({
        hip: [12.5, 11.6], chest: [20.5, 10.6], girth: [5.4, 4.9],
        tail: [[10.2, 11.4, 4.2], [5.2, 10.2, 2.7], [1.6, 8.8, 1.2], [0.4, 8.4, 0.6]],
        neck: [[22.6, 9.6, 3.8], [25, 7.8, 3.5]],
        head: [28.2, 7.0, 4.4, 3.5], snout: [[29.6, 7.4, 3.0], [34.4, 8.2, 2.3]], teeth: true,
        leg: { T: 6.2, w: 4.0, f: 3.8 }, arm: { len: 1.9, w: 0.8 }
      }),
      {
        ramps: { body: "#6E553A", belly: "#D2BC92" },
        eye: [29, 5], eyeColour: AMBER, pupil: true,
        marks: [[28, 4, "shadow"], [29, 4, "shadow"], [30, 4, "shadow"]],
        stripes: stripes([3, 6, 9, 12, 15, 18], 2, { tone: "shadow" })
      }
    ),

  Beipiaosaurus: () =>
    finish(
      biped({
        hip: [14.5, 14], chest: [19.5, 11.2], girth: [4.6, 3.8],
        tail: [[12.6, 14, 3.0], [8, 13.6, 1.8], [5.4, 13.2, 1.2]],
        neck: [[20.6, 9.6, 2.4], [22.6, 6.6, 1.8], [23.8, 4.6, 1.5]],
        head: [25.4, 3.9, 2.4, 1.8], snout: [[26.4, 4.2, 1.3], [29.2, 4.9, 0.8]], mouth: false,
        behind: [ellipse(4.4, 13.3, 2.7, 2.1)],
        front: [
          stroke([[20.4, 12.8, 1.1], [23.6, 15.2, 0.8]], { limb: true, sep: true }),
          stroke([[23.8, 15.2, 0.5], [27.0, 17.4, 0.3]], { ramp: "bone", thin: true }),
          stroke([[23.8, 15.4, 0.5], [26.0, 18.6, 0.3]], { ramp: "bone", thin: true }),
          stroke([[23.4, 15.6, 0.5], [24.6, 19.0, 0.3]], { ramp: "bone", thin: true })
        ],
        leg: { T: 4.6, w: 3.3, f: 3.2 }
      }),
      {
        ramps: { body: "#B27C46", belly: "#EBD9B0" },
        tips: [[28, 4, BONE], [29, 4, BONE]],
        marks: [[10, 12, "highlight"], [13, 11, "highlight"], [16, 10, "highlight"], [8, 13, "highlight"], [4, 12, "highlight"], [22, 7, "highlight"], [18, 9, "shadow"], [12, 13, "shadow"]]
      }
    ),

  Gallimimus: () =>
    finish(
      biped({
        hip: [14, 12.8], chest: [20, 11.4], girth: [3.9, 3.3],
        tail: [[12, 12.4, 2.8], [6.5, 11.2, 1.7], [2.4, 10, 0.9], [0.6, 9.6, 0.5]],
        neck: [[21, 10.2, 2.2], [23.4, 6.4, 1.6], [24.6, 3.8, 1.4]],
        head: [26.2, 3.2, 2.4, 1.7], snout: [[27.2, 3.5, 1.3], [30.8, 4.2, 0.75]], snoutRamp: "beak", mouth: false,
        front: [poly([[19.2, 12.2], [23.0, 13.2], [21.8, 16.6], [18.4, 14.8]], { ramp: "feather", sep: true })],
        leg: { T: 5.6, w: 2.9, f: 3.4 }
      }),
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

module.exports = { SPECIES, ORDER, W, H };
