// PNG in and out for the art tool: no dependency, only Node's zlib.
//
// An image here is { w, h, px } where px is w * h entries of [r, g, b, a], each
// 0 to 255 and STRAIGHT (not premultiplied) alpha: the format the engine's own
// sprite code draws in and the one the game's Atlas.Load reads (it premultiplies
// itself, A04). What is written is always 8-bit RGBA, no interlacing, filter 0
// on every row, so a PNG holds exactly the bytes of its pixels.
//
// Deterministic: no date, no text chunk, no randomness. The compressed bytes
// are Node's zlib at its default level, so they are the same on one PC every
// time; another Node version may compress the same pixels to other bytes, which
// shows as a changed file in git and is harmless (commit it and the manifest).
const fs = require("fs");
const zlib = require("zlib");

const SIGNATURE = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

const CRC = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}

// The image as the bytes of a PNG file.
function encodePng(img) {
  const stride = img.w * 4 + 1;
  const raw = Buffer.alloc(stride * img.h);
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      const p = img.px[y * img.w + x];
      const o = y * stride + 1 + x * 4;
      raw[o] = p[0];
      raw[o + 1] = p[1];
      raw[o + 2] = p[2];
      raw[o + 3] = p[3];
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(img.w, 0);
  ihdr.writeUInt32BE(img.h, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // colour type 6: RGBA
  return Buffer.concat([SIGNATURE, chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

function writePng(file, img) {
  fs.writeFileSync(file, encodePng(img));
}

// The image in a PNG file's bytes. Only what this tool writes (and what most
// tools save): 8 bits per channel, RGB or RGBA, no interlacing; every row
// filter is undone. Anything else throws, so a sheet is never read wrongly.
function decodePng(bytes) {
  if (bytes.length < 8 || !bytes.subarray(0, 8).equals(SIGNATURE)) throw new Error("not a PNG");
  let pos = 8;
  let w = 0;
  let h = 0;
  let channels = 0;
  const idat = [];
  while (pos + 8 <= bytes.length) {
    const len = bytes.readUInt32BE(pos);
    const type = bytes.toString("latin1", pos + 4, pos + 8);
    const data = bytes.subarray(pos + 8, pos + 8 + len);
    pos += 12 + len;
    if (type === "IHDR") {
      w = data.readUInt32BE(0);
      h = data.readUInt32BE(4);
      const depth = data[8];
      const colour = data[9];
      if (depth !== 8 || (colour !== 6 && colour !== 2) || data[12] !== 0) throw new Error("only 8-bit RGB or RGBA PNGs without interlacing");
      channels = colour === 6 ? 4 : 3;
    } else if (type === "IDAT") idat.push(data);
    else if (type === "IEND") break;
  }
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * channels;
  const rows = Buffer.alloc(stride * h);
  for (let y = 0; y < h; y++) {
    const filter = raw[y * (stride + 1)];
    for (let i = 0; i < stride; i++) {
      const x = raw[y * (stride + 1) + 1 + i];
      const a = i >= channels ? rows[y * stride + i - channels] : 0;
      const b = y > 0 ? rows[(y - 1) * stride + i] : 0;
      const c = y > 0 && i >= channels ? rows[(y - 1) * stride + i - channels] : 0;
      let v;
      if (filter === 0) v = x;
      else if (filter === 1) v = x + a;
      else if (filter === 2) v = x + b;
      else if (filter === 3) v = x + ((a + b) >> 1);
      else if (filter === 4) {
        const p = a + b - c;
        const pa = Math.abs(p - a);
        const pb = Math.abs(p - b);
        const pc = Math.abs(p - c);
        v = x + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
      } else throw new Error("unknown row filter " + filter);
      rows[y * stride + i] = v & 255;
    }
  }
  const px = new Array(w * h);
  for (let i = 0; i < w * h; i++) {
    const o = i * channels;
    px[i] = [rows[o], rows[o + 1], rows[o + 2], channels === 4 ? rows[o + 3] : 255];
  }
  return { w, h, px };
}

function readPng(file) {
  return decodePng(fs.readFileSync(file));
}

module.exports = { encodePng, writePng, decodePng, readPng };
