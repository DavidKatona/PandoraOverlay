---
paths:
  - "Games/**"
  - "GamesHost/**"
  - "Shell/MainWindow.Games.cs"
  - "tools/games-art/**"
  - "tools/games-sound/**"
  - "PandoraOverlay.Tests/GamesHost/**"
---

# Writing code for the games

The plan, the decisions and the briefs: `Desktop\Pandora Overlay Files\pandora-games\`
(`PLAN-ARCHITECTURE.md`, `PLAN-HATCHLING.md`, `PLAN-SOUND.md`, `DECISIONS.md`: G = the
owner's, A = the architecture's, H = Hatchling's, R = the art's and the sound's
recipes). Built Nov 2026 by an unattended run, on
the branch `games`. The code comments cite these numbers; look one up there before
changing what it guards. Hatchling's own rules, numbers and reasons are the comments in
its `Data/`, `Rules/` and `Progress/` and the tests beside them.

- **A game never reaches the overlay.** A game → Engine → Contract; the app → the
  Contract and each game; nothing under `Games/` references the app, another game or
  a NuGet package; no app source names an Engine type, and only
  `GamesHost/GameCatalog.cs` names a game (A2). The compiler holds the first half, the
  architecture tests (`ProjectRulesTests`, `GamesArchitectureTests`) the rest.
- **A game learns four things about the real dino**: in game or not, species, gender,
  growth (`DinoReading`, A5; the gender only so the engine can tell one life
  from the next, A61). Never the Steam ID, the name, the position, the config or
  the session, and never health: that is read by the window's own `HealthWatch` (G18),
  not by a game. A game is FED (`MainWindow.OnSnapshot` → `FeedGames` →
  `GamesHost.Feed`, window open or not): it never asks. Nothing in `GamesHost/` or
  `MainWindow.Games.cs` names `PollService`, `PandoraClient`, a cookie or a process:
  the games add no request and no trigger, and opening Games nudges no poll
  (CLAUDE.md, constraints 1 to 4; a scan holds it).
- **The forbidden list** (`ForbiddenApiTests`, A29, A43, A44, A60): no networking,
  files, processes, native calls, windows, popups, tooltips, keyboard, WPF animation,
  effects, threads, timers, `System.Random`, wall-clock time, reflection by name
  (`GetField`, `Invoke` and the rest, `dynamic`, LINQ expressions) or a picture by
  address (`BitmapImage`, `UriBuilder`, the `Uri` schemes) or an object made by
  `Activator` from an assembly and a type given as text (A68) in the Contract, the
  Engine or a game, no assembly asked for its embedded files but by the Engine,
  whose `Atlas.Load`, `Atlas.NamesIn` and `SoundBank.Load` read a games assembly
  only (A94: the app's would hold the site's access header), and no address
  (`://`, `http:`, `https:`) in a source file (A45,
  A60); and in a game's XAML no `ObjectDataProvider` or `XmlDataProvider`, no CLR
  namespace or `x:Static` outside the games' own (A42, A60), and no element of an
  internal type of the game's own, which makes WPF compile a helper that reflects by
  name (A67: make the type public). It guards against an API reached for by accident,
  not a sandbox: the games are compiled in and reviewed. Storage is `IGameStorage`,
  time is `IGameClock`, dice are the Engine's `Rng`, the only timer is the Engine's
  `GameLoop` (the one `DispatcherTimer`), sound is the host's (below). The host is
  not bound by the list: it is where the clock, the files and the speaker are. The
  list is never edited to make a test pass.
- **A new game** is a project under `Games/` (`Games/Directory.Build.props` gives it
  its settings, warnings as errors included, A30), a `ProjectReference` in the
  app's `PandoraOverlay.csproj` and in `PandoraOverlay.Games.Tests`
  (`ProjectRulesTests` holds the second), and a third in `PandoraOverlay.Tests` when
  the app's tests name the game's own types, as Hatchling's do (A23), its entry in the
  solution, and ONE line in `GameCatalog.All`, at the end (G1: a game keeps its
  cell). Its `Id` is its save folder's name and never changes (`GameIds.IsValid`,
  A37).
- **Nothing enters the Engine until a game needs it** (G8), and then only what a
  second game would use unchanged and that touches WPF or guards a rule every game
  keeps (A21). The Engine's source names nothing of any game (a scan). Nothing enters
  the Contract until two games need it (A4); a service a game can't provide for
  itself is the other kind, as `IGameHost`'s members are, `Sound` among them (A77).
- **Rules are pure; the state is saved before anything is shown** (A20):
  `Apply(state, action)` returns the new state and an ordered list of events (Hatchling:
  `TurnRules.Apply`, a `TurnResult`), with every roll from an `Rng` kept in the state;
  the game commits and writes the save (`Now`), and only then plays the events back as
  `IStep`s. A step changes what is drawn, never the state, so a playback can be paused,
  sped up, skipped or dropped with the window and nothing is lost. Every pure rule has
  tests; every number a name and one home; a game with progression ships a bot and
  pacing targets as tests.
- **Saves are the host's** (A7): one folder per game under `%AppData%\PandoraOverlay\games`
  (`DataFolder.GamesFolder`), written whole to a temp file and swapped in, the old one
  kept as the backup. A save nobody can read is HELD: it is never overwritten until the
  player's armed "Start fresh", which sets the files aside and deletes nothing, and a
  Reset or a restart after a fault saves and keeps the old file FIRST, or does nothing
  (A38, A49); each Reset's copy is a numbered `save.before-reset-<n>.json`, never
  overwritten (A62). A game's own shape is a `SaveSchema<T>`: an upgrade step when a
  meaning changes, and a fixture of every version that ever shipped, kept in the tests
  for ever (A9), with `AssertRoundTrips` on a fresh, a mid-run and a finished state
  (A51). Until the owner releases, Hatchling's version stays 1 and a changed shape is
  handled as `Corrupt` (held); from the first release on, every shape change is a
  version, a step and a fixture. `Now` for a turn or a purchase, `Soon` for accrual
  from readings (A8).
- **Growth counts only when the overlay saw it** (A6, the Engine's `SeenGrowth`): two
  consecutive in-game readings of one launch, same species and gender, not lower, at
  most 120 s apart, no faster than 100% in ten minutes. Nothing bridges a restart, a
  not-in-game reading or a jump. A game decides what a seen step pays, never what was
  seen. (Known and accepted, A48: two stored dinos of one species and gender within
  0.5% can be swapped for a trickle of growth.)
- **The playfield is our own pixels** (A17): a `PixelSurface`, one logical pixel per
  WPF unit, presented by the Engine at the device's resolution; WPF never scales it.
  No `Effect`, no WPF animation; all motion is the loop's, 30 ticks a second, and the
  loop holds a timer only while its rate is not Off: a playback, or a quiet cue at 4
  looks a second; a paused loop keeps its timer and does nothing (A19, A55, A56). No
  word is drawn on it (A18).
- **Words are the theme's** (A18, A25): a game's screens are WPF in its `Views/`,
  reading the app's tokens and `Controls.xaml` styles by `DynamicResource` only, body
  sizes as `Theme.Size.N`, no literal colour, corner, font or text (scans), a theme key
  written whole as one literal. Data colours live in the game's one palette class, the
  same in every theme (G21). Every string is in the game's one text class; the
  currency's name is in one file (A24). What is chosen wears `Theme.Text.Highlight`
  (A46). No tooltip, popup, window or keyboard handling in a game (A33, A42).
- **The frames are fixed and the themes' numbers do not move** (A26): the window is
  `GamesFrame` (800 x 552), a game's view `GameFrame` (798 x 510). The whole window
  is drawn at the player's `GamesScale` (Settings → Widgets, 75% to 200%; A75, the
  owner reversing G15's "no size slider"), clamped to its monitor's work area less
  `ScreenMargin` (`GamesFrame.FitScale`): one transform over the layout, which stays
  800 x 552 logical, so no frame number changes and a board stays on whole device
  pixels (the presenter reads the transform, A17). Every screen of
  every game is a `LayoutState` and is laid out in every theme by `GamesLayoutTests`
  and the fit-factor walk. If a theme's computed `Fit` changes, the screen is wrong:
  give the text room or an ellipsis. Never edit a theme file for a game. A game that
  must scroll asks the architecture first (`DarkScrollBar` can't be reached from its
  XAML).
- **Every game has a Guide page** (A76, the owner's ask of Nov 15 2026: he did not know
  Walk from Sneak): its goal, its rules and its terms, in plain short words, reachable
  from every page, during a run included, changing nothing in the game. Every number in
  it is built from the game's own constants and tables, never written as a figure, and
  a test holds its key sentences to them. Each of its sections is a `LayoutState`, laid
  out in every theme like any screen, and fits whole: no ellipsis cuts a sentence
  (fewer words, never cut words). Hatchling's: the Guide tab (H94, `Views/GuidePage`).
- **A game that throws is closed, not the overlay** (A10): every call into a game
  goes through `GameGuard`; a view's handlers go through `GameViewBase.Guard`; `App`
  contains what slips past (`GameFaults`). Never call a game or its view directly from
  the app. A throw in the app's own handler of a host event that runs inside a game's
  call (a tile's redraw, a faulted view's close) is on that game's stack and is
  contained as the game's (A63); anywhere else an app bug crashes as before (A50).
- **The Games window** (`GamesHost/GamesWindow`) is opaque, topmost, activated on
  opening, created at open and destroyed at close (A11, A12). It closes on the release
  of Esc or J (`GamesKeys`: a repeat is never a first press, and a repeat is what
  Windows says it is, bit 30 of the key message, since WPF's `IsRepeat` only knows the
  presses WPF saw: A53), on a click on the game (`OverlayFocus.Left`), and for whatever
  `GamesGate.Closes` says: edit mode, Settings, the map, the sign-in, hide-all (A13). A
  new `OverlayEvent` is decided in that table, and MainWindow's source is read for it
  (`GamesGateTests`). The widgets stay up (G15), under it: whatever shows a widget while
  Games may be open ends in `RaiseGames()` (A58). Its hotkey does nothing while a modal
  dialog of ours is up (A57: the tray's sign-in leaves the hotkeys registered). It
  nudges no poll. The health warning is the window's (`HealthWatch`, G18, A15, A59,
  A66: a hit between two polls, or a fall of 2 points within 2 minutes, a fall already
  won back not counted again), never a game's; the header says when health is not
  watched (A16). The header's right side holds the sound's controls (A82, A83): a
  speaker that mutes (U+1F50A on, U+1F507 muted, drawn at `FontWeight` Normal in every theme), `−`,
  five pips and `+`, the same on the grid and in a game. Five steps 6 dB apart (−24
  to 0 dB), the third the default, sound on; `GamesSoundVolume` and `GamesSoundMuted`
  in `config.json`, plain values, no Settings mirror (the overlay's chimes are
  General → Sounds, another thing); a click on `−`, `+` or an unmuting speaker plays
  the host's own preview blip at the new step, a mute stops every sound at once. A
  pip's corner is the theme's, held to a quarter of its width, and an unlit pip is
  `Theme.Storage.SlotsTrack` (A89). The group's sizes are `GamesFrame` constants and
  the title gets what is left of the header (`TitleMaxWidth`, measured in the widest
  theme), held in every theme by the layout tests: a new part on the header is
  measured there first.
- **Sound is the host's** (A77 to A81, A85): a game cannot make a sound (the
  forbidden list refuses every audio API), so the door is `IGameHost.Sound.Play(GameSound)`,
  as storage is a door: the game names one of its own, the host plays it or drops it,
  and the game never learns which. A game's sounds are WAVs embedded as
  `Sound/<name>.wav` (mono, 16-bit, 44,100 Hz, at most 2 s) and read once by the
  Engine's `SoundBank.Load`, the one reader, which refuses any other format by name
  (A78); a game plays only through `GameViewBase.Play`. Three layers keep it silent
  when it should be (A79, A88): the view does nothing while paused or once disposed;
  the host's gate, `GamesSound` (not `GamesGate`, which decides what closes the
  window), plays only for the CURRENT instance of the game the window shows, with the
  window open, the warning not up and the player not muted, the window telling it
  what it shows (`Stage`) at every change; and every stage at which nobody, or another
  instance, is heard stops what plays, faded over 5 ms. A detached instance reaches
  nothing. The gate's limits (A81): at most 6 sounds at once, the same sound not twice
  within 40 ms, an empty or longer-than-2 s sound refused with one Warning; the mix
  holds 8 voices behind it. Behind the gate is `ISpeaker`: `WaveOutSpeaker` (winmm
  `waveOut`, one stream mixed by `SpeakerMix`, on a thread of its own that exists
  only while something plays and ends 3 s after the last sound; A80) and
  `NullSpeaker`. It never calls `waveOutSetVolume` or any other volume setter
  (the app's volume in Windows' mixer, the player's: a scan holds it, A93), never
  starts a process or a timer, and fails soft: one rule for every failure, a
  device that hands no buffer back for 3 s included (A91), the first a Warning,
  the same again Verbose (A90). The host's native calls are its thirteen, on its
  own window, its own event and Windows' default output, held by a scan (A92): a
  new one is the architecture's call first. **Never for a reading** (A85): a game
  sounds for the player's own clicks and the playback of what they did, never for
  what a poll brought (the
  overlay's chimes already mark the real dino's moments, and the Games mute never
  touches them; the health warning is silent). Hatchling's 29 cues and where they
  fall are its `Turns/BeatSounds` and the comments beside it (H95 to H101).
- **Art is code** (`tools/games-art/`, A28): every sprite goes through the one
  generator; its atlases are checked in under the game's `Art/` and embedded; no
  build runs Node; `ArtInStepTests` fails when a source changed without a rebuild
  (`node tools/games-art/<game>/build.js`). Look at every piece at 1x before it is
  handed over. With `PANDORA_GAMES_RENDERS` set to a folder, `dotnet test
  PandoraOverlay.Tests` writes a PNG of the window in every theme for a reviewer's eyes.
- **Sound is code** (`tools/games-sound/`, A84, R2), beside the art's and apart from it
  (the art's manifest takes every `.js` in its folder as a source of every game): plain
  Node, no dependency, never run by a build, deterministic. Each cue is a recipe in the
  first sound board's format, rendered by WebAudio's own definitions, not an imitation
  of them (R2), into a WAV (RIFF/WAVE, PCM, 1 channel, 44,100 Hz, 16 bits, no other
  chunk) under the game's `Sound/`, which is checked in and embedded by one csproj line,
  with a manifest of every source's and output's hash and each cue's level and length.
  `SoundInStepTests` fails when a source changed without a rebuild
  (`node tools/games-sound/<game>/build.js`), when an output was edited by hand, when a
  file under `Sound/` no manifest wrote exists, and when a WAV is outside its format,
  1 to 88,200 samples, its peak within 0.5 dB of its cue's level, that level within −24
  to −5 dBFS, a silent first sample and last 2 ms (A87) or a sample at full scale. The
  owner's seven picks are his, the first board's take A with nothing changed (R1); a pick
  other than A, or a note, from the board is a recipe change, a new R line and a rebuild
  (R5). `build.js --board` writes `sound-board-v2.html` and takes B and C of each new cue
  into the git-ignored `out/`: the board is how the owner listens, and nobody else can
  judge a sound. Where the tool is exact, see its `README.md`.
- **Where the tests are, and what runs without the app**: `PandoraOverlay.Games.Tests`
  (the Contract, the Engine, the architecture, Hatchling) builds and runs with the
  overlay running: `dotnet test Games/PandoraOverlay.Games.Tests`. What needs both sides
  (the window, the layout in every theme, the host, a game's species against
  `Assets/rules.json`, A23) is in `PandoraOverlay.Tests/GamesHost/`. Tests never touch
  the real data folder: `AttachAll` runs from `Loaded`, never from a constructor.
- **What the public was promised** (the Suggestions post, Nov 2026) is binding on
  every game: nothing to buy, no streaks, timers or reminders, no rewards inside The
  Isle, nothing touching the game, no new request, never in the way, and nobody
  loses anything for playing a popular species (G3, G4, G6, G22).

## Checked only by tests and renders

Nobody has run the overlay with the games in it over The Isle (the run was unattended,
and `WORKER-RULES.md` barred starting it); say so when it matters.

- The window over the game: Ctrl+J, the cursor let go, Esc and J closing on their
  release with the game not reacting, a click on the game closing it, reopening where
  you left off mid-run (G12, G14, G17).
- The Isle's frame rate with Games open, idle and during a playback (A05 timed
  composing and presenting only: 0.6 ms at 100% and 200%, 2.3 ms at 150%).
- Pixel art sharp at a real display scale, 125% and 150% included, and the window on
  a second monitor or a screen smaller than the frame (A17: the arithmetic is tested,
  the placement is not).
- The window at a `GamesScale` above 100% on a real screen, 200% and the clamp to a
  1080p monitor included, and the board's sharpness at a fractional total (125% x 1.5,
  say): the arithmetic and the transform are tested (A75), nothing was seen.
- The six themes on a real screen, and every picture of a game: stills only. Nobody
  has seen the walk, a strike, a bite, the fade or a turn's pace move (PLAN-HATCHLING.md, section 10).
- The window against hide-all, edit mode, the map, Settings, the tray, Check Prime and
  the tray's sign-in with the hotkey (A13, A57, A58): the table and MainWindow's source
  by tests, the rest by reading.
- The health warning with a real hit, "Keep playing", a quarter crossed, the note when
  disconnected; a real slow loss (starving, thirst, a bleed) against the drain's 2
  points in 2 minutes, which misses anything slower than a point a minute; it can be
  up to 15 s (60 s after ten minutes) late after a spawn, because opening Games nudges
  no poll (risk 11: constraint 2, the owner's to allow).
- An update, Exit and a Windows shutdown with a run in progress; the real data folder
  (saves are tested in temp folders only).
- Growth credited with Games closed and not credited for a storage swap; whether a
  swap passes through a not-in-game reading, and the server's fastest grow, which
  `MaxGrowthPerSecond` and `MaxStepGap` guess (A6, A48).
- The API's spelling of the 21 species against Hatchling's table (A23, H89): only
  "Deinosuchus" has been seen from the API; a name the table doesn't know pays
  nothing, and the log says so once each time the game is started, without the name.
- Hatchling's balance: a bot's, which plays worse than a person; nobody has played
  these rules by hand (PLAN-HATCHLING.md, section 10: the apex's 15 turns, the middle of the
  climb, danger 5, what server play pays per hour to a small species).
- Hatchling's tile on the real grid: laid out in every theme and rendered once by the
  A14 review's probe, not by a permanent test (the window's renders use stand-in
  games); in Isle its pitch ends in an ellipsis.
- **The sound** (G26; checked by tests with a fake speaker, a fake device and the
  board's code only; nobody has heard a sound of the games, and the real device ran
  once, on silence, on an idle PC):
  - Every sound by ear: the owner's seven picks, the 22 new cues' takes, the demo
    turns at Normal and Fast (whether a quiet turn is quiet enough, whether a busy
    one is a wall of noise, whether the hunters' steps help or clutter), and the
    board whole (its 2.3 MB page was opened only in parts; its match figures
    were computed in Chromium, where `stage` and `win` miss the correlation bar
    for a reason measured, R6, and the corrected render meets it).
  - The default step (3) against The Isle's own sound, and the bite landing with
    its blow (about 40 ms after it in the plan; the first cue after a launch waits
    for the device's first open, about 93 ms from the play to the device on the
    build PC).
  - Crackle with The Isle running, and after the window has been open an hour: the
    spike ran clean with every core spinning and the probe ran 3.6 s on an idle PC.
  - A device unplugged, in exclusive use or absent, one that stops handing buffers
    back (closed after 3 s, A91), a changed default device, a Bluetooth headset:
    the failure paths ran on the fake device only, and the codes a real driver
    gives, and whether one stalls, are not known.
  - The overlay's entry in Windows' volume mixer, and the PC's sleep with the
    window closed (the stream closes 3 s after the last sound; not observed).
  - The mute, `−`, `+`, the preview, and the glyphs and pips in each theme in a real
    session: renders at 96 DPI only, and WPF draws the glyphs through font fallback,
    which another PC may not have.
  - Nothing sounding with the window closed, on the grid, under the warning or after
    a click on the game: the gate is tested with a fake speaker, not heard.
  - Exit from the tray while a turn plays: the cue then sounding is cut by the
    device's reset, not faded (A90); whether the cut is heard is the owner's.
  - MainWindow making the speaker and disposing it: compiled, lazy by test, never run.
  - Whether the gate's 40 ms and six-at-once limits ever drop one of Hatchling's cues
    at Fast: by reasoning, not by a test against the gate.
- The golden renders: Settings › Hotkeys differs in every theme (the new row), and
  Settings › Widgets should too (A75's GAMES group, by reading: the harness was not
  run after the Games window's scale); the baselines are the owner's to record
  after his look. The harness draws no Games window and makes MainWindow without
  running its constructor, so no sound's gate or speaker exists there.
- CI's SDK 8.0.x: every build here was SDK 9 compiling `net8.0-windows`.
- Saves belong to the Windows user, not the website account: two accounts on one PC
  share a game's progress (risk 9, left on purpose).
