---
paths:
  - "Sound/**"
  - "Assets/Clicks/**"
  - "tools/games-sound/clicks/**"
  - "Shell/MainWindow.Sound.cs"
  - "PandoraOverlay.Tests/Sound/**"
---

# Writing code for the app's sound (the speaker and the click sounds)

The plan, the decisions and the briefs: `Desktop\Pandora Overlay Files\pandora-games\`
(`PLAN-CLICKS.md`, `PLAN-SOUND.md`, `DECISIONS.md`: G = the owner's, A = the
architecture's, H = Hatchling's, R = the recipes). Built Nov 2026 on the branch `games`
(G27, the click sounds, from the speaker the games' sounds made, G26). The code comments
cite these numbers; look one up there before changing what it guards. The games' own side
(the Engine's click rule, a game's cues, the Games window) is `.claude/rules/games.md`.

## The speaker

- **One speaker for the whole app** (A101), `Sound/`: `ISpeaker` (with `NullSpeaker`),
  `WaveOutSpeaker`, `WaveOut` (the native calls), `SpeakerMix` (the pure mix). MainWindow
  makes it ONCE, in its constructor (`Shell/MainWindow.Sound.cs`), and disposes it at the
  exit AFTER every gate. It is lazy: making it opens no device and starts no thread, so a
  MainWindow built and never shown plays nothing.
- **Two channels**, `SpeakerChannel.Games` and `.Clicks`. A voice carries its channel and a
  stop is per channel, so a game's stop (every stage change, A88) never fades a click and
  a click never touches a game's sound. Each gate plays through
  `WaveOutSpeaker.Channel(c)`, an `ISpeaker` view whose `Dispose` does nothing (the owner
  disposes the speaker, so one gate's end can't close the stream the other plays on). A
  new gate is one more field in `MainWindow.Sound.cs` made over its own channel and
  disposed before `CloseSound`.
- **A stop is one with its channel's last queued stop**, whatever the other channel
  queued since (A108): the earlier stop already fades everything the new one would, so the
  queue holds at most one stop of a channel after each of its plays, and
  `MaxQueued`'s 64 plays bound it (a needed stop is never dropped, so only this rule
  bounds the stops). Never go back to "two stops in a row": two channels' stops in turn
  queued 2,001 requests against 64.
- **What stays as A80 to A96 say it**: one stream to `WAVE_MAPPER` (the default device),
  44,100 Hz, 16-bit mono, five buffers of 441, on a thread named "Overlay sound" that
  exists only while something plays and ends 3 s after the last sound; the mix holds 8
  voices for both channels; never `waveOutSetVolume` or any other volume setter (the app's
  volume in Windows' mixer is the player's, A93); never a process or a timer; fail soft, one
  rule for every failure (a device that hands no buffer back for 3 s included, A91), the
  first a Warning, the same again Verbose (A90); `Dispose` joins for at most 200 ms and
  does not fade. The speaker's lines go to the log's `LogSound` category ("Sound: …",
  facts only: constraint 4); the games' gate keeps its own in `LogGames`.
- **`Sound/` calls nothing of `GamesHost/` and holds no gate** (A109): the games' gate
  reaches `Sound/`, never the reverse. `Sound/` may name the Contract's
  `GameSound.SampleRate` and `MaxSamples`, the sound format's one home (A78) which the
  click files share, so `ClickBank` checks a file against those and never a literal of its
  own, and its documentation may name the gates it plays for.
- **The speaker's tests use a fake device**, `PandoraOverlay.Tests/Sound/FakeWaveDevice.cs`
  (A80's `IWaveDevice`); the real device has run once, on silence, on an idle PC.

## The scans (A102, A86, A92, A93)

- `GamesArchitectureTests` reads `Sound/`, `GamesHost/`, `Shell/MainWindow.Games.cs` and
  `Shell/MainWindow.Sound.cs` for its request, volume, native-call and reach scans: no
  request, cookie, process type or volume setter in them, and only the thirteen native
  calls (ten in `Sound/WaveOut.cs`, three in `GamesHost/GamesWindow.xaml.cs`) against
  `Winmm.WaveMapper`, the one device. A new native call, or a second device, is the
  architecture's call first; the scans' words are never edited to make code pass.
- **The `\bProcess\w*` trap.** The reach scan reads `ProcessInput` and
  `ProcessInputEventArgs` as the Process family. WPF's input manager names them, so write
  the handlers as lambdas on its `PreProcessInput` and `PostProcessInput` events, which
  name neither (there is no word boundary inside `PreProcessInput`), as `UiClicks.Attach`
  does. Never edit the scan for it.

## The click layer (G27: A98 to A100, A104, A111, A112)

- **One class handler for every click in the app**, `UiClicks` in `Sound/`:
  `EventManager.RegisterClassHandler` on `ButtonBase.ClickEvent`, `handledEventsToo`, made
  once per process by `UiClicks.Attach()` on the UI thread, from MainWindow only, never by
  a window or a template. It acts at the source button only (a button inside another clicks
  once). So a new button, in a popup too, needs nothing; a control that must be silent
  needs its mark. It is process-wide and permanent on purpose.
- **Only a left click of the mouse sounds.** Space, Enter, an access key, `IsDefault`,
  `IsCancel`, automation's Invoke, a `RaiseEvent` and `IsChecked` set in code all reach the
  handler outside a left `MouseUp` and stay silent; hover and focus raise no click.
  `Attach` hooks the UI thread's input manager and keeps a stack of the input items being
  handled; a click is the mouse's when (A111):
  1. the INNERMOST input item still being handled is a left `Mouse.MouseUpEvent` item. A
     frame is pushed at each item's `PreProcessInput` and taken off, with every frame
     above it, at its `PostProcessInput`, and it counts only while the input manager's own
     `PreProcessInputEventArgs` still name its item and are not cancelled (an item
     cancelled in a pre-process handler that runs after ours gets no post-process, and its
     frame left on top would silence every real click); dead frames are dropped from the
     top before every look. Items nest (ButtonBase releases its capture inside the
     release, and a click that opens a dialog runs a message loop inside it), so "between
     the two" alone would read a key pressed in that dialog as the release's;
  2. and the MouseUp is AIMED at the clicked button: its original source is the button or
     inside it, so a click raised in code from inside a release on another button is not
     the mouse's.

  The per-window fallback of the plan stays unused. Each rule has a test that fails
  without it.
- **Known limit** (A112): an exception that escapes a release's handling and is then
  contained (only a game's is, `GameFaults`) leaves that release's frame looking live, so a
  click raised in code or by automation on that one button could sound; never a key, never
  a wrong real click. Accepted, documented in `UiClicks`, no reset hook in the crash path.
  The app raises no click in code (searched), and has no `ClickMode` of its own and no
  ComboBox: a new one of those is checked against the layer first.
- **The scope** (`UiClicks.Scope`, inherited): `Overlay` (the default), `Games` (the Games
  window's root: its mute and its step), `Game` (its view container: a game's controls,
  which the app's handler leaves to the Engine's rule, A105).
- **The mark** (`UiClicks.Cue`, not inherited; `ClickMark`): `Auto` (by type), `None`,
  `Press`, `Select`, `Toggle` (on or off by `IsChecked`), `Back`, `Arm`. By type (A100):
  a CheckBox `toggle-on` or `toggle-off` by its new state, a RadioButton and every other
  ToggleButton (a nav, a tab, a menu opener) `select`, any other button `press`; the menu
  tick is `Toggle` by a setter on its style in `Themes/Controls.xaml` (the one line the
  layer put there: not a look, so no theme can change it). A dismissal (✕, Close, Cancel,
  Later, Keep, "‹ Games", "Back to camp") is `Back`: one gesture, one sound, in every
  window (A113 added a stored dino's rename Cancel). A control with a sound of its own, or
  that must be silent, is `None` (the control panel's Exit, because the exit cuts what
  plays, A90; the Games window's speaker, `−` and `+`).
- **An armed button** (a two-click one: Reset positions, Delete all, Sign out, a map menu
  entry that asks) is marked `Arm`, and its arming branch calls `UiClicks.Armed(button)`.
  The class handler posts its decision at `DispatcherPriority.Send`, after the button's
  own handler and before the next frame: `arm` when the branch called `Armed` (it asked,
  it did NOT do the thing), else `press` (it acted). Only an armed button waits, never
  long: its handler must arm or act in place and never open a dialog. Every other click
  plays at once, before the button's handler, so a click that opens Settings is heard as
  the button comes up. Nothing else of an armed site changes.
- **Sliders**: class handlers on the left press and release, so one cue at the release
  when the value moved (`select`, or the slider's mark; the click volume's own slider is
  `Press`, so the commonest click is heard at the new level), never while dragging and
  never for a key.
- **A clickable thing that is not a button** says so itself: `UiClicks.Clicked(element,
  cue)`, called from its own MOUSE handler (the colour wells, a stored dino card's
  summary, the Games window's tiles); it trusts its caller to be one. Text boxes, links,
  hotkey boxes, the route's drag, the map itself and the tray (WinForms, Windows' own
  menu) never click.
- **The sink is thread-static** (`UiClicks.Sink`, an `IClickSink`): MainWindow's
  `ClickSound`, set in `MainWindow.Sound.cs` only and null on any other thread, so every
  test, every dialog a test builds and the render harness are silent. A test that needs a
  click uses `ClickSeam` and `FakeClickSink` (`PandoraOverlay.Tests/Sound/UiClicksTests.cs`),
  which open the bracket through the real input manager; it never starts the app or moves
  the pointer. Settings names nothing of the layer but its attached properties, and
  writes two config keys.

## The gate and the settings (A99, A103)

- **`ClickSound`** (`Sound/`): the rules in this order: nothing when the player turned
  clicks off (Settings › General › Sounds, one switch for every click, the games' too);
  in the `Games` scope nothing when the Games window is muted, else the gain of ITS step;
  in the `Overlay` scope the gain of the overlay's own step; the `Game` scope is refused;
  nothing when the last click that sounded started less than `MinGap` (80 ms) ago,
  whatever its cue or window (dropped, never queued; a muted or off click starts no gap);
  then the cue's samples and the speaker's Clicks channel. Config is read at each click,
  so a change is heard at the next one. No click is longer than `MinGap` (R8), so at most
  one sounds at a time. It starts no timer, reads no file and opens no device at
  construction, and logs nothing per click (constraint 4).
- **A game's click** reaches it through `GamesSound.Click`, which plays only for the
  instance heard (A79's second layer) and takes the click sink's interface, `IClickSink`
  (A114); the mute, the step, the switch and the gap are `ClickSound`'s, never written
  twice.
- **`SoundSteps`** (`Sound/`) is the five steps' one home (A103): 1 to 5, 6 dB apart, −24
  to 0 dB, `StepOf` and `GainOf`; the games' gate forwards to it.
- **`config.json`**: `ClickSoundEnabled` (default true) and `ClickSoundVolume` (1 to 5,
  default 3; a value out of range is read as the nearest step and never written back by
  reading), plain values, never seeded, no migration. **ON by default**, so an update turns
  clicks on for every player: the release's notes say so (`PLAN-CLICKS.md` 5.2); the
  default is one constant. The Settings row writes the box on `Checked` and `Unchecked`,
  which WPF raises before the click event, so ticking it plays `toggle-on` at the step and
  clearing it is silent; the slider writes whole steps and names no `SettingsChange`. The
  row is on General, first under SOUNDS (`.claude/rules/settings.md`).
- **The files** (`ClickBank`): each cue's WAV is embedded as `Clicks/<file>.wav` and read
  ONCE, at the first click that needs it, by the app's own reader (never the Engine's
  `SoundBank`, A2): RIFF/WAVE, PCM, mono, 16-bit, at `GameSound.SampleRate`, at most
  `GameSound.MaxSamples`; any other field is refused by name. A file missing or refused is
  ONE Warning naming the cue (and the exception's type, never its message), then that
  cue's silence, never a throw.

## The sounds and their tool (A106, R8 to R11, G29)

- Six cues, one family for the overlay and the games: `press`, `select`, `toggle-on`,
  `toggle-off`, `arm`, `back` (`ClickCue`; the Contract's `GameClick` has the same six).
  The WAVs are `Assets/Clicks/*.wav`, checked in, embedded by ONE csproj line
  (`Clicks/%(Filename)%(Extension)`) and written by `node tools/games-sound/clicks/build.js`
  (the tool's README says what it refuses; takes B and C and the audition board go to the
  git-ignored `out/`). Take A of each is the owner's (G29); B and C stay in `cues.js` as the
  takes he heard beside it, with no `pick` mark. A change to a take is a recipe change, a
  new R line and a rebuild, in a task of its own (R9), and a sound is judged by ear by the
  owner, nobody else.
- **A click is short and quiet by rule** (R8, R11): 15 to 80 ms (662 to 3,528 samples),
  never longer than `MinGap`; peaks −24 to −14 dBFS by role (`arm` −16, `press` and
  `toggle-on` −18, `select` and `toggle-off` −20, `back` −21); attacks of 1 ms or more, a
  silent first sample and last 2 ms (A87), no sample at full scale; triangle, sine and
  filtered noise only, never a square (a click is never a warning). The clicks are never an
  alert: no click for a reading, a timer, a hotkey, an update, a friend or a route stop,
  and the overlay's alerts stay Windows' "Exclamation" (CLAUDE.md, Decided against).
- Held by tests: `SoundInStepTests` (Games.Tests: sources in step, outputs not edited by
  hand, every file under `Assets/Clicks/` an output and every cue a WAV, R8's bounds as the
  test's own constants, never read from the manifest) and `ClickBankTests` (every
  `ClickCue` has its embedded WAV, every embedded `Clicks/*.wav` is a cue, no click longer
  than `MinGap` or louder than −14 dBFS). A new cue is a `ClickCue` value, a recipe, a
  rebuild, and a decision first.

## Checked only by the owner (`PLAN-CLICKS.md` 11)

Nobody has heard a click in the app, and no real mouse click has been made in a test: a
window a test builds can't hold a mouse capture, so the tests reach the class handler
through the input manager with a MouseUp of their own (A107); the known key paths (Space
and Enter on a focused button, an access key, a key pressed inside a release),
automation's Invoke and a click raised in code are tested silent, and any other key
raises its click inside its own input item, never inside a left release (A111). Say so when it matters. The owner's own to see: a real
left click ticking, and Space, Enter, Esc and every hotkey not; each surface of the plan's
appendix; the Games window and Hatchling's table (nothing during a turn, no double with its
own cues); the default (on, step 3) against The Isle; the six by ear; the first click after
a launch, a click after the stream's idle close, a Bluetooth headset and the overlay's
entry in Windows' volume mixer. The full list is in CLAUDE.md's Roadmap.
