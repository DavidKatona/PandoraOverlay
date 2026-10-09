---
paths:
  - "Settings/**"
---

# Writing code in the Settings dialog

- **No stock WPF chrome anywhere in the dialog.** Every control kind has its own
  template in `SettingsWindow.xaml` (buttons with an explicit disabled look, Check /
  Radio, `TextBoxStyle`-based boxes, `DarkScrollBar`); a new kind gets one before it
  ships. Radio rows instead of a ComboBox (stock ComboBox chrome is light and ignores
  Background). Our own nav, not the stock TabControl.
- One partial file per page (`SettingsWindow.<Page>.cs`), each under ~500 lines. A new
  widget adds a nav button (Tag = the `SetPage` key) and a page, in the control panel's
  order, holding its own scale slider; General is app-wide only.
- Content built in code is built on the page's **first look** (`SetPage` →
  `Open<Page>Page`), never for a page you aren't on: hidden pages are still laid out, and
  the dialog opened slowly twice before this rule (owner, Oct 1 2026). Long lists go in
  **batches**: the first screenful at once, the rest per `DispatcherPriority.Background`
  tick, a version counter so a newer rebuild stops an older one, and the "building… N of
  M" notice after 300 ms. Only what sets a page's height stays eager.
- Pages are **Hidden, not Collapsed**: the tallest page sets the dialog's height, so the
  dialog never jumps between pages — and a new page must fit within it. Lists live in
  fixed-height ScrollViewers.
- Actions inside a list change it **in place** (rows ticked, removed, re-striped); only
  structural changes (Import, Delete all) rebuild — a rebuild loses the reader's place.
- Edits go to a **draft** (`Clone()` of the library or friend book) that Save commits and
  Cancel drops. Everything applies on Save and hot-applies (a `*Changed` flag MainWindow
  acts on, or read live) — never a restart. The exceptions act at once and say so on the
  page: Account, Skins, Dino storage.
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
- A new global hotkey needs a capture box here, a line in Save, MainWindow's registration
  and an Unregister in `MainWindow.OpenSettingsOn` (else it fires behind the dialog and its
  own combo probes as taken).
