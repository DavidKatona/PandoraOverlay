---
paths:
  - "Shell/**"
  - "App.xaml"
  - "App.xaml.cs"
---

# Writing code in Shell/ (widgets, hotkeys, MainWindow)

- **A new widget** derives `OverlayWindowBase`; its root Border takes
  `WidgetFrame.Width` and `LargeHeight` or `SmallHeight` (`x:Static`) and lays its content
  out INSIDE (DockPanel + star rows), so it never grows with its content; it overrides
  `AppearanceScale` with its own scale key (a plain 1.0); `Fades => true` only if
  something can wake it. MainWindow owns its lifetime: a Show/Toggle pair, its position
  saved into config on hide, `SetEditMode(true)` when shown during edit mode,
  `Refresh*Async` if it is a request surface, and a nullable X/Y filled by
  `FillDefaultPositions` (DefaultLayout). It gets a captioned control-panel group and a
  Settings page — never a tray line.
- **A new global hotkey** needs: an id constant (0xA11D is reserved for the Settings
  dialog's test registration); a default WITH a modifier, clear of the game's F2
  (recording) and F10 (hide HUD) — raw-input games see the bare key despite Ctrl — and of
  Ctrl+F3 (held by third-party software in the wild); a letter key only after the owner
  has checked in game that the bare key does nothing; a candidate list through
  `ResolveLateHotkey`, one longer than its takers; registration in `OnSourceInitialized`
  and `ApplyHotkeysFromConfig`; an Unregister in `OpenSettingsOn`; a WndProc case; a
  Settings capture box.
- While the map is open no widget shows: anything that would show a widget or a dialog
  closes the map first (`ShowWindows` does nothing meanwhile); hide-all closes it and
  leaves the screen clear.
- The overlay activates only its OWN windows (the control panel, the map, dialogs); the
  hotkey press grants foreground rights. The game window is never found, focused or touched.
- Every stats-panel status goes through `SetStatus`, so the view name can step aside;
  never trim a status for it.
- Panels use our own button templates (the glow overlay) and font-glyph icons — no stock
  chrome.
- MainWindow is split by concern (`MainWindow.Topic.cs`), never into "manager" classes.
  `MainWindow.xaml.cs` is near the ~500-line guideline (474 lines, Oct 9 2026, after the
  stats panel's drawing moved to `MainWindow.Stats.cs`): a new topic gets its own part.
