---
paths:
  - "Settings/**"
---

# Writing code in the Settings dialog

- **No stock WPF chrome anywhere in the dialog.** Every control kind has its own
  template in `Themes/Controls.xaml` (1.39; shared with the update card and the sign-in
  window: buttons with an explicit disabled look, Check / Radio, `TextBoxStyle`-based
  boxes, `DarkScrollBar`, `NavButton`); a new kind gets one there before it ships. Radio
  rows instead of a ComboBox (stock ComboBox chrome is light and ignores Background).
  Our own nav, not the stock TabControl. Sliders take `Style="{DynamicResource
  Theme.SliderStyle}"` (WPF's own in Default, `ThemedSlider` for a drawn thumb).
- **The look is the theme's** (1.39): colours, corners and fonts are tokens, read by
  `DynamicResource` in XAML and from `Theme.Current` as a page builds; a status line that
  stays on screen takes its tone by `ShowTone` (a resource reference: it follows a switch
  by itself); the website's own meanings (Discord's blue, the diet colours, a stored dino's
  sex, badges and vitals) are `SiteBrushes`, never a theme's. A code-built page notes the
  theme it drew in (`Drawn`) and is redrawn from the data it holds (`RedrawIfStale`,
  `SettingsWindow.Themes.cs`), never by its `Open…` — see `.claude/rules/themes.md`.
- One partial file per page (`SettingsWindow.<Page>.cs`); a page splits further only
  along a topic of its own (CLAUDE.md, Conventions → File size).
- The nav is grouped (owner, Oct 22 2026): the overlay's pages (Account, General,
  Themes (1.39, after General: the owner, Oct 25 2026), Hotkeys, Widgets, Maps,
  Waypoints, Friends), then the website's under an
  ISLAPANDORA.EU caption (Skins, Dino storage, Server rules), then About. A page is a
  nav ToggleButton (Tag = the `SetPage` key; checked = shown, so the theme draws the
  selected row); captions and dividers in `Nav` are not pages.
- A new widget adds a captioned group to the **Widgets** page, in the control panel's
  order, its own Scale slider first; it gets a page of its own only when its options
  would make Widgets taller than the tallest page. The minimap's options are on **Maps**,
  with what the minimap and the map share (the trail). General is app-wide only:
  start-up, the look, and every chime (a chime plays whether or not its widget is on
  screen). Groups inside a page use `SectionCaption`.
- Content built in code is built on the page's **first look** (`SetPage` →
  `Open<Page>Page`), never for a page you aren't on: anything built up front is paid for
  on every opening, and the dialog opened slowly twice before this rule (owner, Oct 1
  2026). Long lists go in **batches**: the first screenful at once, the rest per
  `DispatcherPriority.Background` tick, a version counter so a newer rebuild stops an
  older one, and the "building… N of M" notice after 300 ms.
- **The dialog has a fixed frame** (`SettingsFrame`, 1.39, the owner's call, Oct 25 2026):
  660 × 551.77 at 100%, Default's 1.38 size to the sub-pixel, the same in every theme. The
  root takes it and the window sizes to the root; the small-screen limit is the root's
  MaxHeight. A new page must fit within the frame in every theme's font at its fit factor,
  and a one-line string within its room or end in an ellipsis that shows
  (`DialogLayoutTests`, `FitFactorTests`): a page never grows the dialog, it would scroll.
  The pages not shown are **Collapsed** (since the fit factor's release; Hidden before,
  from the days the tallest page set the dialog's height): each page is measured on its
  own, and only a page that overflows ever scrolls. The page column is marked
  `FitProbe.HoldsWhole`, the scroll area the walk holds whole. Lists live in fixed-height
  ScrollViewers. Every body text size is a `Theme.Size.N` key or `Theme.Current.Size(n)`,
  never a number (`.claude/rules/themes.md`).
- Actions inside a list change it **in place** (rows ticked, removed, re-striped); only
  structural changes (Import, Delete all) rebuild — a rebuild loses the reader's place.
- **Everything applies at once** (1.38, no Save or Cancel): a new setting writes its config
  key in `WireLiveSettings` (`SettingsWindow.Apply.cs`) and names the `SettingsChange`
  MainWindow re-applies (`ApplySettingsChange`), or is read live — never a restart. Wire a
  control after its value is filled, so filling writes nothing. The lists edit the page's
  own copy (`Clone()` of the library or friend book), applied with each batch
  (`FlushChanges`); a text box applies when left, on Enter, and on closing — not per letter.
  What removes many at once or moves the layout is armed (two clicks); one row's ✕ is one.
- The dialog closes on a click on the game (`OverlayFocus`). A window it opens for the
  player (a file picker, the sign-in window, a card) goes through `OwnWindow`, so the
  dialog waits under it; a link or folder handed to Windows goes through `OpenUrl`.
- A page that talks to the site goes only through its PollService part; the page only
  draws. It requests only when looked at or clicked — never for another page, never on a
  timer.
- Async page handlers: after every await, check the dialog-closed flag (`_skinsClosed`,
  `_storageClosed`) before touching the page. An `async void` handler that redraws after
  a write the site already took catches and reports (`RedrawFailed`) — an exception there
  takes the overlay down.
- Never assign into an occupied Panel index: `RemoveAt`, then `Insert` (crashed Oct 8 2026).
- A static field in one partial file must not be initialised from another part's statics:
  C# leaves the order across partial files open.
- Destructive actions are armed in place by a first click (Delete all, Sign out, a stored
  dino's Delete), never a modal box.
- Layout: slider rows use `SliderLabel` (140 px); a checkbox that owns a slider sits in
  the label slot with the slider's IsEnabled bound to it; list pages are a card with a
  caption band and zebra rows on a shared column grid, with a measured header
  (`AlignHeader`); colour wells 15 px; a per-row delete is neutral grey with a soft red
  glyph, a many-at-once delete red.
- A new global hotkey needs a capture box here, a line in `WriteHotkeys`, MainWindow's
  registration and an Unregister in `MainWindow.OpenSettingsOn` (else it fires behind the
  dialog and its own combo probes as taken).
