# games-sound

The sound tool, for the games' sounds and the click sounds. Not part of the app: plain
Node (24), no dependency, no `package.json`. It renders each cue from its recipe,
writes it where the game (or, for the clicks, the app) embeds it, and writes the sound
board the owner listens on. **Never run by a build or a
test**: the WAVs are checked in, so a build needs no Node (A84).

```
node tools/games-sound/hatchling/build.js           writes the game's Sound/ and the manifest
node tools/games-sound/hatchling/build.js --board   the same, takes B and C, and the board
```

Everything it writes goes to `Games/PandoraOverlay.Games.<Game>/Sound/<cue>.wav`
(embedded in the game by one csproj line), to `tools/games-sound/<game>/manifest.json`
(checked in), or to `out/` (takes B and C, `sound-board-v2.html`; git-ignored). The
clicks set, the one set that is not a game, writes to the app's `Assets/Clicks/`
instead (see "The clicks set" below).

It is apart from `tools/games-art/` on purpose: the art's manifest takes every `.js`
in its folder as a source of every game, so a sound file there would make the art
stale (PLAN-SOUND 5.1).

## The files

| File | What it is |
|---|---|
| `synth.js` | a recipe to samples, by WebAudio's own definitions (R2): band-limited oscillators, the biquads, the automation, the first board's seeded noise; 44,100 Hz, peak-normalised, rounded to 16 bits |
| `wav.js` | WAV out (RIFF/WAVE, PCM, mono, 44,100 Hz, 16 bits, no other chunk) and in, for the board |
| `manifest.js` | writes a set's manifest (a game's, or the clicks') |
| `board.js` | the games' sound board (the clicks' is `clicks/board.js`): every take from the WAVs that ship, the picks' match figures (under a pick that misses a bar, the figures of a corrected render and a line saying why, R6), the window's five volume steps and a mute, the demo turns |
| `<game>/` | one folder per game: its `cues.js`, its `build.js` and its `manifest.json`; and `clicks/`, the click sounds, a set that is not a game (below) |

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
  A), and a glide is a sweep, not a held note. Before it writes a byte, `build.js`
  refuses a cue named twice, a name that is not lower-case words joined by `-`, an
  unknown group, a level outside −24 to −5 dBFS, a pick with other than one take or
  a new cue with other than three, a take outside the length bounds, and a layer
  with an attack under 1 ms or one that ends after its take. It refuses a render
  the test would refuse (a peak more than 0.5 dB off its level, a first sample or
  last 2 ms not silent, no samples or more than 88,200) before it writes that
  take's file.
- **Listen before handing over.** Nobody but the owner can judge a sound; the board
  is how he does (PLAN-SOUND section 10).

## The clicks set

```
node tools/games-sound/clicks/build.js           writes Assets/Clicks/ and the manifest
node tools/games-sound/clicks/build.js --board   the same, takes B and C, and the third board
```

The click sounds (G27, A106): six cues, one family for the overlay's buttons and the
games' (`press`, `select`, `toggle-on`, `toggle-off`, `arm`, `back`; PLAN-CLICKS 3.2 and
3.3). A set of this tool that is NOT a game: `clicks/` holds its own `cues.js`,
`build.js`, `board.js` and `manifest.json`, and its take A of each cue goes to the
APP's `Assets/Clicks/<cue>.wav` (checked in, embedded by the app, since the overlay
plays them and the host plays a game's clicks from the same files), never to a game's
`Sound/`. Takes B and C go to `out/clicks/`, the third board to
`out/sound-board-v3.html` (git-ignored; copied by hand into the games folder).

- **Its own bounds, R8's** (apart from R3's and R4's, which stay the games' cues'): a
  take of 0.015 to 0.08 s, never longer than `ClickSound.MinGap` (80 ms, so two clicks
  never overlap); attacks of 1 ms or more; levels −24 to −14 dBFS by role (`arm` −16,
  `press` and `toggle-on` −18, `select` and `toggle-off` −20, `back` −21); triangle,
  sine and filtered noise only, no square; held notes in C, E and G, a glide a sweep
  (R7); three takes a cue, A ships (R9). `clicks/build.js` refuses, before it writes a
  byte, a cue named twice, a bad name, a level or a take outside those bounds, other
  than three takes, an attack under 1 ms, a layer ending after its take and a square;
  and it renders every take and refuses one the test would refuse (a peak more than
  0.5 dB off its level, a first sample or last 2 ms not silent, fewer than 662
  samples or more than 3,528 (15 to 80 ms, R11)) before it writes any file.
- **Held as a game is**, by `SoundInStepTests`, but for its place and bounds: its
  outputs under `Assets/Clicks/` only, and every file there one of them; every cue a
  WAV and every WAV a cue; every WAV in the format, 662 to 3,528 samples (15 to 80 ms),
  its peak within 0.5 dB of its cue's level, that level within −24 to −14 dBFS, silent
  ends, no sample at full scale. The bounds are the test's own constants, never read
  from the manifest. Its sources are the shared `.js` files and its own, like a game's.
- **Take A of each is the owner's** (G29). B and C stay in `clicks/cues.js` as the takes
  he heard beside it, with no `pick` mark (R11); a change to a take is a recipe change, a
  new R line and a rebuild, in a task of its own (R9, R5's way).
- **The third board** (`clicks/board.js`, R10) is the second board's page (its first
  `<style>` block, its PCM reader, its measure and waveform copied, not imported, so
  the second board stays byte for byte): two five-step volumes (Overlay, Games window)
  and a mute; each take with ▶ once and ▶▶ eight times at 0.35 s; five demos built at
  play time from the takes picked on the page, the 80 ms gap applied, two of them with
  Hatchling's shipped `step.wav` and `bite.wav`; the picks as "Click sound picks (v3):",
  kept in the browser under `pandora-click-board-v3-picks`.
