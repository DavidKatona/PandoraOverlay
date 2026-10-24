---
paths:
  - "Themes/**"
  - "Assets/Themes/**"
  - "Assets/Fonts/**"
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
  colours, the map picture and its sea, the area borders, the scrim. (Today's literals move
  onto the tokens in the plan's steps 2 to 6; new code follows this rule from now.)
- **No code outside `Themes/` asks which theme is on.** The tokens carry no id and no name
  (`ThemeTokens`); identity lives only in `Theme.All`'s entries, for the loader, the
  Settings row and the harness. A difference between themes is a VALUE the code reads by
  its meaning. Branching on a value a theme supplies is fine (a cut of 0 draws the Border's
  own glass); branching on which theme supplied it is never.
- **Default is today's look and never moves.** `Themes/DefaultTheme.cs` is its one copy,
  pinned by `ThemeFileTests.DefaultIsTodaysLook`; Default stays pixel-identical to 1.38.
  A new token gets today's literal there, so Default doesn't change.
- **A shape is a pick from the catalogue** (`ShapeCatalogue`: the radio, the slider's thumb,
  the fracture badge, the title glyph), never a theme's own XAML, drawn by `ThemeShape`
  (`Slot`, `Kind="{DynamicResource Theme.Shapes.…}"`; None takes no room at all). A shape
  the catalogue lacks is added to it once, in code, for every theme.
- **A frame is a `ThemedBorder`** (a widget's or the control panel's root, from step 2):
  `CutCorners="{DynamicResource Theme.Cut.…}"`, `Lit` for edit mode, `Kind` for whose look;
  no BorderBrush or Background in its XAML (the outline is its own, the glass
  ApplyAppearance's). With Default's numbers it is the plain Border's drawing.
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
  NameLayer rule says no blur effect. No exception, "Prime Elder" included.
- **A redraw of a code-built Settings page calls its RENDER, never its `Open…`**:
  `OpenStoragePage` asks the website on every call past its guards, and a theme click must
  never become a trigger of a request (CLAUDE.md, constraint 2).
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
  mistake. `MontserratTests` prove the bundled faces resolve.
- **The golden renders run in both themes**
  (`Desktop\Pandora Overlay Files\pandora-big-map-sketch\golden-renders`): Default must stay
  62 of 62 identical; another theme's renders keep their Default twin's pixel size.
