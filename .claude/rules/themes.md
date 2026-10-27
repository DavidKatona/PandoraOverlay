---
paths:
  - "Themes/**"
  - "Assets/Themes/**"
  - "Assets/Fonts/**"
  - "PandoraOverlay.Tests/Themes/**"
---

# Writing code for the themes (1.39)

The plan and the owner's decisions: `Desktop\Pandora Overlay Files\pandora-themes\PLAN.md`
(with `DESIGN.md` and `REVIEW.md` beside it). How the pieces work: `Themes/Theme.cs`'s
summary.

- **A colour, corner, font or glow is a token, never a literal**, outside `Themes/`:
  XAML reads it by `{DynamicResource Theme.<path>}` (`Theme.Text.Title`,
  `Theme.Corner.Widget`), code by `Theme.Current.<path>` at the moment it paints, never into
  a `static readonly` field. Data colours are the exception, never a theme's: the waypoint
  palette, friend colours, the zone hues, Discord's blue, the website's badge and diet
  colours, the map picture and its sea, the area borders, the scrim. (Every window is on the
  tokens since the plan's step 6; what has no token yet is the same in every theme: font
  sizes, a few weights, stroke widths.)
- **No code outside `Themes/` asks which theme is on.** The tokens carry no id and no name
  (`ThemeTokens`); identity lives only in `Theme.All`'s entries, for the loader, the
  Settings row and the harness. A difference between themes is a VALUE the code reads by
  its meaning. Branching on a value a theme supplies is fine (a cut of 0 draws the Border's
  own glass); branching on which theme supplied it is never.
- **Default is today's look and never moves.** `Themes/DefaultTheme.cs` is its one copy,
  pinned by `ThemeFileTests.DefaultIsTodaysLook`; Default stays pixel-identical to 1.38.
  A new token gets today's literal there, so Default doesn't change. The one exception
  is the owner's (Oct 25 2026): the control panel's fixed, compact frame and its 10 px
  buttons, for every theme (`ControlPanelFrame`). (The sign-in window's Done and ✕ also
  hover in their own family's colours now: 1.38's grey hover was a copy slip.)
- **A shape is a pick from the catalogue** (`ShapeCatalogue`: the radio, the slider's thumb,
  the fracture badge, the title glyph), never a theme's own XAML, drawn by `ThemeShape`
  (`Slot`, `Kind="{DynamicResource Theme.Shapes.…}"`; None takes no room at all). A shape
  the catalogue lacks is added to it once, in code, for every theme.
- **A frame is a `ThemedBorder`** (the root of every widget, the control panel, the map
  window, the Settings dialog and the update card): `CutCorners="{DynamicResource
  Theme.Cut.…}"`, `Lit` for edit mode, `Kind` for whose look; no BorderBrush in its XAML (the
  outline is its own), and on a widget no Background either (the glass is
  ApplyAppearance's); a window that is no widget gives it its glass token (`Glass.Dialog`,
  `Glass.MapWindow`). With Default's numbers it is the plain Border's drawing.
- **A stats bar is a `BarTrack`** (the theme's inset line, `Bars.TrackLineWidth`) **holding a
  `BarFill`** (the theme's `Below25Halo`, set by the stats panel on the critical bars, those
  that pulse); both are the Border's own drawing at Default's 0.
- **A theme switch is `MainWindow.ApplyTheme`**, in the order its comment gives (the swap,
  the glass, then each painted part); a window that paints from code gets a public
  `ApplyTheme()`: `ApplyAppearance` plus its renders from what it holds, never a re-feed, no
  placement, no request. A new such window adds its call there.
- **Code paints from `Theme.Current` at the moment it draws**, a state's colour through
  `.ToBrush()` (one kept frozen brush per colour), a weight through `.ToFontWeight()`; a
  painted part repaints from held state, never by re-feeding what it painted from (the stats
  panel: `PaintStats`, never `IngestStats`).
- **A threshold is the code's; a theme only colours the state.** The bars' four states and
  their lines are `Stats/BarState.cs`; the pulse and StatsAttention's wake lines read them.
  A new coloured thing names its states in code and asks the theme for each one's look.
- **A theme has no size of a widget, a frame or a window, no scale and no position**
  (PLAN.md section 12). Its only sizes are text sizes and the bar height, inside a frame.
  `Theme.Apply` and a theme switch write nothing into config and place no window.
- **A glow is painted, never an `Effect`** (`FrameGeometry.Glow`: three strokes under the
  line). A live DropShadowEffect halved the frame rate in the glow spike; the owner's
  NameLayer rule says no blur effect. No exception, "Prime Elder" included. Round a shape
  (your arrow on the maps) it is a `PaintedGlow` under the shape, with its geometry and
  transform; the inner outline of a cut surface inside a frame (the minimap's map square)
  is a `CutOutline`. Both draw nothing at Default's 0.
- **A redraw of a code-built Settings page calls its RENDER, never its `Open…`**:
  `OpenStoragePage` asks the website on every call past its guards, and a theme click must
  never become a trigger of a request (CLAUDE.md, constraint 2). A page's render calls
  `Drawn("<page>")`; `RedrawIfStale` (SettingsWindow.Themes.cs) gets a case for it, and
  `SettingsThemeTests` holds the rule (a counting fake for the website).
- **The dialogs' templates are `Themes/Controls.xaml`'s** (Settings, the update card, the
  sign-in window, from step 5): a window never keeps its own copy of a button family.
- **A new window derives `OverlayWindowBase`, or calls `Theme.EnsureLoaded()` before
  `InitializeComponent`**, so a harness without the App has the theme's resources. A theme
  key read by `StaticResource` never follows a switch: always `DynamicResource`.
- **A theme file** (`Assets/Themes/<id>.json`) is a sparse override of Default: `Name`,
  `Order` and the `Tokens` that differ, by their paths. A key Default lacks or a value that
  doesn't parse fails `ThemeFileTests`; it never inherits silently. **A third theme is one
  file there and nothing else**: the loader, the Settings row, the tests and the harness
  iterate `Theme.All`.
- **Fonts**: one folder per family under `Assets/Fonts/`, the family's files and its own
  licence as shipped (`Assets/Fonts/** -text` keeps them byte for byte). The csproj takes
  them by wildcard (`*.ttf` as WPF Resources, each `OFL.txt` embedded and copied beside the
  program). A theme names a bundled family as `"Assets/Fonts/<folder>/#<Family>"`;
  `Theme.FontFamilyOf` addresses it by the `;component` pack form, never the plain
  `pack://application:,,,/` (the entry assembly: a harness would fall back silently).
  Never install a bundled font into Windows on the owner's PC: it would hide a bundling
  mistake. A new font needs a build `--no-incremental` once: files a glob adds are not
  seen by an incremental build, and a theme then measures and renders in a fallback font.
  `MontserratTests` read every folder there: each family's faces resolve and are in the
  build, every weight a theme names is a real face (`EveryNamedWeightHasAFace`) and every
  face bundled is one a theme names (`EveryBundledFaceIsOneAThemeNames`: no face that no
  theme reads), and the strings with the least room fit or trim. A string that no longer
  fits ends in an ellipsis (the owner, Oct 25 2026): never another wording per theme,
  never a size of its own. `ControlPanelWidthTests` checks every theme's labels, captions
  and hint fit the control panel's fixed frame (`ControlPanelFrame`); `DialogLayoutTests`
  lays the dialogs out in every theme (the Settings dialog in its frame, `SettingsFrame`,
  every page with nothing to scroll; no one-line string of Settings, the update cards and
  the sign-in window cut off: it fits, or it ends in an ellipsis that shows).
- **A theme test tests the framework, never a theme** (the owner, Oct 27 2026): every
  theme, a player's own later included, must work within the framework, so no test names
  a bundled theme, pins its values or keeps a list of exceptions per theme. A test either
  holds EVERY theme to a rule of the framework (iterating `Theme.All`: it loads, its faces
  exist, its text fits or ends in an ellipsis, its frames keep their size, a ticked mark
  shows under the mouse) or builds a sample theme of its own (`ThemeFileTests.Sample`).
  A theme's design is judged in game and in the golden renders, not in the tests. The one
  exception is Default's pin (`DefaultIsTodaysLook` and its siblings): Default is the
  compiled base, and the pin keeps today's look from moving.
- **The golden renders run in every theme**
  (`Desktop\Pandora Overlay Files\pandora-big-map-sketch\golden-renders`), the minimap's
  (`run-golden.ps1`) and the windows mode's (`run-windows.ps1`): Default must stay 62 of 62
  and 104 of 104 identical; `-Theme <id>` renders another theme, whose every render must
  keep its Default twin's pixel size.
