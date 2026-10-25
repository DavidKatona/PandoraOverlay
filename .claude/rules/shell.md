---
paths:
  - "Shell/**"
  - "App.xaml"
  - "App.xaml.cs"
---

# Writing code in Shell/ (widgets, hotkeys, MainWindow)

- **A new widget** derives `OverlayWindowBase`; its root is a `ThemedBorder` (the theme's
  frame; `RootPanel.Lit = editMode` in `OnEditModeChanged`) that takes
  `WidgetFrame.Width` and `LargeHeight` or `SmallHeight` (`x:Static`) and lays its content
  out INSIDE (DockPanel + star rows), so it never grows with its content; it overrides
  `AppearanceScale` with its own scale key (a plain 1.0); `Fades => true` only if
  something can wake it. MainWindow owns its lifetime: a Show/Toggle pair, its position
  saved into config on hide, `SetEditMode(true)` when shown during edit mode,
  `Refresh*Async` if it is a request surface, and a nullable X/Y filled by
  `FillDefaultPositions` (DefaultLayout). It gets a captioned control-panel group and a
  captioned group on Settings → Widgets (`.claude/rules/settings.md`) — never a tray line.
- **A new global hotkey** needs: an id constant (0xA11D is reserved for the Settings
  dialog's test registration); a default WITH a modifier, clear of the game's F2
  (recording), F10 (hide HUD), F11 (windowed / fullscreen) and Steam's F12 (screenshot) —
  raw-input games see the bare key despite Ctrl — and of
  Ctrl+F3 (held by third-party software in the wild); a letter key only after the owner
  has checked in game that the bare key does nothing; a candidate list through
  `ResolveLateHotkey`, one longer than its takers; registration in `OnSourceInitialized`
  and `ApplyHotkeysFromConfig`; an Unregister in `OpenSettingsOn`; a WndProc case; a
  Settings capture box.
- While the map is open no widget shows: anything that would show a widget or a dialog
  closes the map first (`ShowWindows` does nothing meanwhile); hide-all closes it and
  leaves the screen clear.
- **A click on the game** is `OverlayFocus`: a window of ours that takes focus calls
  `OverlayFocus.Watch`, and what must get out of the way on that click subscribes to
  `OverlayFocus.Left` (MainWindow closes Settings and locks edit mode; the update cards
  close themselves). Never ask which window took the focus.
- **A key that closes or locks something acts on its RELEASE** and marks its press and its
  release handled (Esc, the map's M). Acted on at the press, the focus goes back with the
  key still down and the release lands in the game: The Isle opened its menu after the map
  closed (Oct 2026). And a press left unhandled is handed on to whichever of our windows
  gets the focus next: one Esc closed What's new and Settings under it.
- The overlay activates only its OWN windows (the control panel, the map, dialogs); the
  hotkey press grants foreground rights. The game window is never found, focused or touched.
- Every stats-panel status goes through `SetStatus`, so the view name can step aside;
  never trim a status for it.
- Panels use our own button templates (the glow overlay) and font-glyph icons — no stock
  chrome.
- **The control panel has a fixed frame too** (`ControlPanelFrame`, 1.39, the owner's call,
  Oct 25 2026): its root, every button's width and height, the caption and hint rows are
  constants, the same in every theme, and a label is centred and trimmed inside its part,
  so no font or label grows the panel. A new button gets its width there, the sum is
  tested (`ControlPanelWidthTests`), and every theme's text must fit it.
- **Colours, corners, fonts and glows are theme tokens** (1.39): a widget reads them by
  `DynamicResource Theme.…` or `Theme.Current`, never as a literal, and never asks which
  theme is on — see `.claude/rules/themes.md`.
- MainWindow is split by concern (`MainWindow.Topic.cs`), never into "manager" classes.
  A new topic gets its own part (the stats panel's drawing is `MainWindow.Stats.cs`);
  see CLAUDE.md, Conventions → File size.
