# games-sound

The games' sound tool. Not part of the app: plain Node (24), no dependency, no
`package.json`. It renders each cue from its recipe, writes it where a game embeds
it, and writes the sound board the owner listens on. **Never run by a build or a
test**: the WAVs are checked in, so a build needs no Node (A84).

```
node tools/games-sound/hatchling/build.js           writes the game's Sound/ and the manifest
node tools/games-sound/hatchling/build.js --board   the same, takes B and C, and the board
```

Everything it writes goes to `Games/PandoraOverlay.Games.<Game>/Sound/<cue>.wav`
(embedded in the game by one csproj line), to `tools/games-sound/<game>/manifest.json`
(checked in), or to `out/` (takes B and C, `sound-board-v2.html`; git-ignored).

It is apart from `tools/games-art/` on purpose: the art's manifest takes every `.js`
in its folder as a source of every game, so a sound file there would make the art
stale (PLAN-SOUND 5.1).

## The files

| File | What it is |
|---|---|
| `synth.js` | a recipe to samples, by WebAudio's own definitions (R2): band-limited oscillators, the biquads, the automation, the first board's seeded noise; 44,100 Hz, peak-normalised, rounded to 16 bits |
| `wav.js` | WAV out (RIFF/WAVE, PCM, mono, 44,100 Hz, 16 bits, no other chunk) and in, for the board |
| `manifest.js` | writes a game's manifest |
| `board.js` | the sound board: every take from the WAVs that ship, the picks' match figures (under a pick that misses a bar, the figures of a corrected render and a line saying why, R6), the window's five volume steps and a mute, the demo turns |
| `<game>/` | one folder per game: its `cues.js`, its `build.js` and its `manifest.json` |

## The recipe

The first sound board's format, unchanged, so a recipe pastes between the two:

```js
{ name: "Example", dur: 0.09, tone: 6000 /* optional lowpass on the take */, layers: [
  { kind: "noise", filter: "lowpass", f0: 900, f1: 250, q: 0.7, t: 0, dur: 0.07, attack: 0.003, gain: 0.8 },
  { kind: "osc", wave: "sine", f0: 140, f1: 85, glide: "linear" /* else exponential */, t: 0, dur: 0.06, attack: 0.002, gain: 0.6 } ] }
```

and the cue's `level`, the dBFS its peak is normalised to (the same for every take of
a cue). A lowpass's `q` is in dB, a bandpass's linear, as WebAudio has them.

## The rules

- **Deterministic.** No date, the first board's own seeded noise, keys in one fixed
  order, LF. Run `build.js` twice and `git status` is the same. It deletes nothing.
- **In step.** `build.js` writes the SHA-256 of every `.js` here (the shared ones and
  the game's own: a source is a source by being in the folder) and of every WAV it
  wrote, and each cue's level and length. `SoundInStepTests` (in
  `Games/PandoraOverlay.Games.Tests/Sound/`) fails when a source changed without a
  rebuild and says `run node tools/games-sound/<game>/build.js`; it also holds every
  file under a game's `Sound/` to being an output, and every WAV to its format, 1 to
  88,200 samples (`GameSound.MaxSamples`), its peak within 0.5 dB of its cue's level,
  that level within −24 to −5 dBFS (R4), a silent first sample and last 2 ms (A87), and no
  sample at full scale.
- **The owner's picks are his.** The seven cues marked `pick` are the first board's
  take A, copied with nothing changed (R1). A pick other than A on the second board,
  or a note, is a recipe change, a new R line and a rebuild, in a task of its own (R5).
- **New cues in the picks' style** (R3): 0.07 to 0.85 s; attacks of 1 ms or more;
  C, E and G and their octaves for every held note; triangle and sine for what is
  gentle or good, square only for a warning or a fanfare, filtered noise for what is
  physical. By R7, a take the plan derives from a pick keeps the pick's pitch
  (`focus-apex` A), an octave overtone over a palette note may sit above C7 (`unlock`
  A), and a glide is a sweep, not a held note. Before it writes a byte `build.js`
  refuses a name that is not lower-case words joined by `-`, an unknown group, a
  level outside −24 to −5 dBFS, a pick with other than one take or a new cue with
  other than three, a take outside the length bounds, a layer with an attack under
  1 ms or one that ends after its take, and a render the test would refuse (a peak
  more than 0.5 dB off its level, a first sample or last 2 ms not silent, more than
  88,200 samples).
- **Listen before handing over.** Nobody but the owner can judge a sound; the board
  is how he does (PLAN-SOUND section 10).
