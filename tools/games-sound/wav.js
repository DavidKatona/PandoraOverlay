// WAV in and out (A84): RIFF/WAVE, one 16-byte `fmt ` chunk (PCM, 1 channel,
// 44,100 Hz, 88,200 bytes a second, block 2, 16 bits), one `data` chunk, and
// nothing else: no LIST, no date, no tool's name, so the same samples are the same
// bytes on every run and every PC. The engine's SoundBank (A78) and the tests'
// own reader refuse anything else; the reader here is the board's (it embeds the
// files that ship, read back, and nothing it rendered on the side).
"use strict";

const RATE = 44100;
const CHANNELS = 1;
const BITS = 16;
const BLOCK = CHANNELS * (BITS / 8);

// Int16Array (mono, 44,100 Hz) to the file's bytes, little-endian.
function write(samples) {
  const data = samples.length * BLOCK;
  const b = Buffer.alloc(44 + data);
  b.write("RIFF", 0, "ascii");
  b.writeUInt32LE(36 + data, 4);
  b.write("WAVE", 8, "ascii");
  b.write("fmt ", 12, "ascii");
  b.writeUInt32LE(16, 16);            // the chunk's size: PCM's 16 bytes, no extension
  b.writeUInt16LE(1, 20);             // PCM
  b.writeUInt16LE(CHANNELS, 22);
  b.writeUInt32LE(RATE, 24);
  b.writeUInt32LE(RATE * BLOCK, 28);  // 88,200 bytes a second
  b.writeUInt16LE(BLOCK, 32);
  b.writeUInt16LE(BITS, 34);
  b.write("data", 36, "ascii");
  b.writeUInt32LE(data, 40);
  for (let k = 0; k < samples.length; k++) b.writeInt16LE(samples[k], 44 + k * BLOCK);
  return b;
}

// The file's bytes to its samples, refusing anything but what write() makes.
function read(bytes, name = "the WAV") {
  const fail = (why) => { throw new Error(`${name}: ${why}`); };
  if (bytes.length < 44) fail("shorter than a WAV header");
  if (bytes.toString("ascii", 0, 4) !== "RIFF" || bytes.toString("ascii", 8, 12) !== "WAVE") fail("not RIFF/WAVE");
  if (bytes.readUInt32LE(4) !== bytes.length - 8) fail("the RIFF size is not the file's");
  if (bytes.toString("ascii", 12, 16) !== "fmt " || bytes.readUInt32LE(16) !== 16) fail("the first chunk is not a 16-byte fmt");
  if (bytes.readUInt16LE(20) !== 1) fail("not PCM");
  if (bytes.readUInt16LE(22) !== CHANNELS) fail("not one channel");
  if (bytes.readUInt32LE(24) !== RATE) fail("not 44,100 Hz");
  if (bytes.readUInt32LE(28) !== RATE * BLOCK || bytes.readUInt16LE(32) !== BLOCK) fail("the byte rate or block is wrong");
  if (bytes.readUInt16LE(34) !== BITS) fail("not 16 bits");
  if (bytes.toString("ascii", 36, 40) !== "data") fail("the second chunk is not data");
  const data = bytes.readUInt32LE(40);
  if (44 + data !== bytes.length || data % BLOCK !== 0) fail("the data chunk is not the rest of the file");
  const samples = new Int16Array(data / BLOCK);
  for (let k = 0; k < samples.length; k++) samples[k] = bytes.readInt16LE(44 + k * BLOCK);
  return samples;
}

module.exports = { write, read, RATE };
