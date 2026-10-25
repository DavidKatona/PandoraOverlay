namespace PandoraOverlay;

/// <summary>
/// Everything a theme can act on (PLAN.md section 11, "the catalogue"): the
/// colour of every STATE the code names, the shape picks, the cut, lit, glow
/// and corner numbers, and the type. A theme only paints, so there is no
/// member for the size or position of a widget, a frame or a window, and none
/// for a scale (section 12): the only sizes here are text sizes and the bar
/// height, all INSIDE a frame that never moves.
/// <para>
/// The record carries NO identity (no id, no name), on purpose (section 0,
/// REVIEW.md objection 4): code outside Themes/ reads <c>Theme.Current</c>, the
/// tokens alone, so it cannot ask which theme is on; it asks for the look of
/// a state by its meaning (<c>Theme.Current.Bar(BarKind.Health, BarState.Below25).Fill</c>)
/// and branches, at most, on a VALUE (a cut of 0 is no cut). Identity lives in
/// <see cref="ThemeEntry"/>, which only the loader, the Settings row and the
/// harness use.
/// </para>
/// <para>
/// Every member is <c>required</c>, so the compiler is Default's completeness
/// test (<see cref="DefaultTheme"/>, today's look, the one theme in code); a
/// theme file is a SPARSE override laid over Default (<see cref="ThemeFile"/>)
/// and names only what differs. In a file, and as a resource key, a token is
/// its path: <c>"Text": { "Title": "#B8E6EC" }</c>, <c>{DynamicResource Theme.Text.Title}</c>.
/// Adding a token: a member here, its value in DefaultTheme (today's literal),
/// and it is in every theme, every file and the resource dictionary at once.
/// </para>
/// <para>
/// Colours that are DATA, not chrome, are never here (section 11, "Never a
/// theme's"): the waypoint and friend palettes, the zone hues, Discord's blue,
/// the website's badge and diet colours, the map picture and its sea, the
/// area borders, the scrim.
/// </para>
/// </summary>
public sealed record ThemeTokens
{
    public required BarTable Bars { get; init; }
    public required PrimeLook Prime { get; init; }
    public required ActivityLook Activity { get; init; }
    public required GlassLook Glass { get; init; }
    public required FrameLook Frame { get; init; }
    public required BadgeLook Badge { get; init; }
    public required GrowthLook Growth { get; init; }
    public required TextLook Text { get; init; }
    public required ButtonLook Button { get; init; }
    public required CheckLook Check { get; init; }
    public required SliderLook Slider { get; init; }
    public required TextBoxLook TextBox { get; init; }
    public required ScrollBarLook ScrollBar { get; init; }
    public required NavLook Nav { get; init; }
    public required MenuRowLook MenuRow { get; init; }

    /// <summary>Hairline dividers: the control panel's group separators, the menus' separators, the dialog's page column.</summary>
    public required ThemeColor Separator { get; init; }

    public required CardLook Card { get; init; }
    public required InfoCardLook InfoCard { get; init; }
    public required WellLook Well { get; init; }
    public required ListRuleLook ListRule { get; init; }
    public required MapLook Map { get; init; }
    public required UpdateCardLook UpdateCard { get; init; }
    public required ShapePicks Shapes { get; init; }
    public required CutNumbers Cut { get; init; }
    public required LitNumbers Lit { get; init; }
    public required GlowNumbers Glow { get; init; }
    public required CornerNumbers Corner { get; init; }
    public required FontLook Font { get; init; }

    #region Lookups by state

    /// <summary>
    /// The look of one bar in one of its four states. The STATE is the code's
    /// (<see cref="BarStates.Of"/>: the lines are behaviour); the theme only
    /// colours it. Where Default has three health colours and another theme
    /// two, that theme gives three states the same colour: no code asks why.
    /// </summary>
    public BarStateLook Bar(BarKind kind, BarState state) => BarOf(kind).In(state);

    /// <summary>One bar's colours that don't depend on its state: the track, its line, the text on the fill.</summary>
    public BarLook BarOf(BarKind kind) => kind switch
    {
        BarKind.Health => Bars.Health,
        BarKind.Stamina => Bars.Stamina,
        BarKind.Hunger => Bars.Hunger,
        BarKind.Thirst => Bars.Thirst,
        BarKind.Damage => Bars.Damage,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The colour of the combat view's health readout ("Healthy" / "Wounded") in a health state.</summary>
    public ThemeColor Readout(BarState state) => Bars.HealthReadout.In(state);

    #endregion

    #region Bars

    /// <summary>The five bars, each with the same four states (PLAN.md section 6), and the bar layout's two numbers.</summary>
    public sealed record BarTable
    {
        public required BarLook Health { get; init; }
        public required BarLook Stamina { get; init; }
        public required BarLook Hunger { get; init; }
        public required BarLook Thirst { get; init; }
        /// <summary>The combat view's damage taken in this fight (a bar that GROWS).</summary>
        public required BarLook Damage { get; init; }
        /// <summary>The combat view's "Healthy" / "Wounded" word, per health state.</summary>
        public required QuarterColors HealthReadout { get; init; }
        /// <summary>The rows' names at their left (Health, Stamina, … Speed).</summary>
        public required ThemeColor Label { get; init; }
        /// <summary>The bar's height (the row absorbs it; the frame does not move).</summary>
        public required double Height { get; init; }
        /// <summary>The track's inset line, 0 = none.</summary>
        public required double TrackLineWidth { get; init; }
        /// <summary>The halo round a bar in its Below25 state, 0 = none. ONE number per theme (REVIEW.md decision 16).</summary>
        public required double Below25Halo { get; init; }
    }

    public sealed record BarLook
    {
        public required BarStateLook Above75 { get; init; }
        public required BarStateLook From50To75 { get; init; }
        public required BarStateLook From25To50 { get; init; }
        public required BarStateLook Below25 { get; init; }
        /// <summary>The bar's empty part.</summary>
        public required ThemeColor Track { get; init; }
        public required ThemeColor TrackLine { get; init; }
        /// <summary>A time-left label with no room past the tip sits inside the fill's end, in this colour.</summary>
        public required ThemeColor OnFill { get; init; }

        public BarStateLook In(BarState state) => state switch
        {
            BarState.Above75 => Above75,
            BarState.From50To75 => From50To75,
            BarState.From25To50 => From25To50,
            BarState.Below25 => Below25,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    public sealed record BarStateLook
    {
        public required ThemeColor Fill { get; init; }
        /// <summary>The percent at the row's right end.</summary>
        public required ThemeColor Percent { get; init; }
        /// <summary>The time-left label riding the fill's tip ("~25s", "1h 10m").</summary>
        public required ThemeColor TimeLeft { get; init; }
    }

    /// <summary>One colour per bar state.</summary>
    public sealed record QuarterColors
    {
        public required ThemeColor Above75 { get; init; }
        public required ThemeColor From50To75 { get; init; }
        public required ThemeColor From25To50 { get; init; }
        public required ThemeColor Below25 { get; init; }

        public ThemeColor In(BarState state) => state switch
        {
            BarState.Above75 => Above75,
            BarState.From50To75 => From50To75,
            BarState.From25To50 => From25To50,
            BarState.Below25 => Below25,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    #endregion

    #region Widgets

    /// <summary>The Prime tracker: its condition rows, the header's status and the footer's lines.</summary>
    public sealed record PrimeLook
    {
        public required PrimeRowLook Met { get; init; }
        public required PrimeRowLook Unmet { get; init; }
        /// <summary>A condition newly LOST since the last check.</summary>
        public required PrimeRowLook Lost { get; init; }
        /// <summary>A condition newly MET since the last check.</summary>
        public required PrimeRowLook Fresh { get; init; }
        /// <summary>Every row before the first check (a "·" for its icon).</summary>
        public required PrimeRowLook Unchecked { get; init; }
        public required PrimeStatusLook Status { get; init; }
        public required PrimeFooterLook Footer { get; init; }
    }

    public sealed record PrimeRowLook
    {
        /// <summary>The ✓ / ✗.</summary>
        public required ThemeColor Icon { get; init; }
        public required ThemeColor Text { get; init; }
        /// <summary>The condition's text (the icon is bold in every state).</summary>
        public required ThemeWeight Weight { get; init; }
    }

    public sealed record PrimeStatusLook
    {
        public required ThemeColor PrimeElder { get; init; }
        public required ThemeColor Ready { get; init; }
        public required ThemeColor NotReady { get; init; }
        /// <summary>Not checked yet ("—").</summary>
        public required ThemeColor Unknown { get; init; }
    }

    public sealed record PrimeFooterLook
    {
        /// <summary>"Checked 14:02 · Deinosuchus".</summary>
        public required ThemeColor Checked { get; init; }
        /// <summary>Checked as another dino than the one you play now.</summary>
        public required ThemeColor Stale { get; init; }
        /// <summary>A notice: a failed check, "join the server first".</summary>
        public required ThemeColor Failed { get; init; }
        /// <summary>"next check in 4:12", "check available", "checking…".</summary>
        public required ThemeColor Cooldown { get; init; }
    }

    /// <summary>The Activity feed. A friend's bullet is the friend's colour: data, never a theme's.</summary>
    public sealed record ActivityLook
    {
        /// <summary>The bullet before your own lines.</summary>
        public required ThemeColor Mine { get; init; }
        public required ThemeColor Line { get; init; }
        /// <summary>A fracture, a low stat, damage.</summary>
        public required ThemeColor Warning { get; init; }
        /// <summary>The empty feed's quiet line.</summary>
        public required ThemeColor Quiet { get; init; }
        /// <summary>"3 of 7 friends in game".</summary>
        public required ThemeColor Count { get; init; }
    }

    /// <summary>
    /// The glass behind each kind of surface. The widgets' alpha is the
    /// player's Background opacity slider (OverlayWindowBase.ApplyAppearance),
    /// so <see cref="Widget"/>'s own alpha is not used.
    /// </summary>
    public sealed record GlassLook
    {
        public required ThemeColor Widget { get; init; }
        /// <summary>Settings, the update cards.</summary>
        public required ThemeColor Dialog { get; init; }
        /// <summary>The maps' popup menus.</summary>
        public required ThemeColor Menu { get; init; }
        /// <summary>The minimap's pills (area, scale bar, heading and speed).</summary>
        public required ThemeColor Pill { get; init; }
        /// <summary>The map's pills (scale bar, zoom): denser than the minimap's today.</summary>
        public required ThemeColor MapPill { get; init; }
        /// <summary>The map window, opaque on purpose (the perf spike).</summary>
        public required ThemeColor MapWindow { get; init; }
    }

    /// <summary>A frame's outline: locked, in edit mode, and the control panel (always in edit mode).</summary>
    public sealed record FrameLook
    {
        public required LockedFrameLook Locked { get; init; }
        public required LitFrameLook Edit { get; init; }
        public required LitFrameLook ControlPanel { get; init; }
    }

    public sealed record LockedFrameLook
    {
        public required ThemeColor Line { get; init; }
        /// <summary>The lit accents round the cut corners (<see cref="LitNumbers"/>).</summary>
        public required ThemeColor Lit { get; init; }
        public required ThemeColor Glow { get; init; }
    }

    public sealed record LitFrameLook
    {
        public required ThemeColor Line { get; init; }
        public required ThemeColor Glow { get; init; }
    }

    /// <summary>The stats panel's HEAD / BODY / LEGS badges.</summary>
    public sealed record BadgeLook
    {
        public required BadgeStateLook Intact { get; init; }
        public required BadgeStateLook Fractured { get; init; }
    }

    public sealed record BadgeStateLook
    {
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Text { get; init; }
    }

    /// <summary>The stats panel's growth header.</summary>
    public sealed record GrowthLook
    {
        public required ThemeColor Growing { get; init; }
        public required ThemeColor Paused { get; init; }
        public required ThemeColor FullyGrown { get; init; }
    }

    /// <summary>Text by role. The Settings hints read these (Dim, Good, Caution, Danger), not a group of their own.</summary>
    public sealed record TextLook
    {
        /// <summary>Widget titles, the dino name, the dialog's and the map's titles.</summary>
        public required ThemeColor Title { get; init; }
        /// <summary>The title glyph's line (<see cref="ShapePicks.TitleGlyph"/>), before a title.</summary>
        public required ThemeColor Glyph { get; init; }
        public required ThemeColor PageTitle { get; init; }
        /// <summary>Selected text, slider values, readout names.</summary>
        public required ThemeColor Bright { get; init; }
        /// <summary>Values, percents, bar labels.</summary>
        public required ThemeColor Text { get; init; }
        /// <summary>Feed lines, nav, buttons, field labels.</summary>
        public required ThemeColor Text2 { get; init; }
        /// <summary>Secondary header text (growth, Prime's status, the count).</summary>
        public required ThemeColor Label { get; init; }
        /// <summary>Status lines, footers, hints.</summary>
        public required ThemeColor Dim { get; init; }
        /// <summary>The quietest: idle, unavailable.</summary>
        public required ThemeColor Off { get; init; }
        /// <summary>Caps captions (STATS, LOOK).</summary>
        public required ThemeColor Caption { get; init; }
        public required ThemeColor Link { get; init; }
        public required ThemeColor Good { get; init; }
        public required ThemeColor Caution { get; init; }
        public required ThemeColor Danger { get; init; }
    }

    #endregion

    #region Controls

    /// <summary>
    /// The three button families. The control panel's buttons light with a
    /// glow laid OVER their fill (<see cref="HoverGlow"/>, <see cref="PressedGlow"/>)
    /// where the dialog's change their fill (each family's Hover / Pressed).
    /// </summary>
    public sealed record ButtonLook
    {
        public required ButtonFamilyLook Neutral { get; init; }
        /// <summary>The Save-style button; the control panel's Lock.</summary>
        public required ButtonFamilyLook Primary { get; init; }
        /// <summary>Exit, the cards' ✕.</summary>
        public required ButtonFamilyLook Danger { get; init; }
        public required ThemeColor HoverGlow { get; init; }
        public required ThemeColor PressedGlow { get; init; }
    }

    public sealed record ButtonFamilyLook
    {
        public required ButtonStateLook Idle { get; init; }
        public required ButtonStateLook Hover { get; init; }
        public required ButtonStateLook Pressed { get; init; }
        public required ButtonStateLook Disabled { get; init; }
    }

    public sealed record ButtonStateLook
    {
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Text { get; init; }
        public required ThemeColor Line { get; init; }
    }

    /// <summary>The check box and the radio button (one look for both).</summary>
    public sealed record CheckLook
    {
        public required CheckStateLook Unticked { get; init; }
        public required CheckStateLook Ticked { get; init; }
        public required CheckStateLook Hover { get; init; }
        public required CheckStateLook Disabled { get; init; }
    }

    public sealed record CheckStateLook
    {
        public required ThemeColor Box { get; init; }
        public required ThemeColor Line { get; init; }
        /// <summary>The tick, the radio's dot.</summary>
        public required ThemeColor Mark { get; init; }
        public required ThemeColor Glow { get; init; }
    }

    /// <summary>The slider when its thumb pick is drawn (<see cref="ShapePicks.SliderThumb"/>); a Stock thumb is WPF's own slider and uses none of these.</summary>
    public sealed record SliderLook
    {
        public required ThemeColor Track { get; init; }
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Thumb { get; init; }
        public required ThemeColor ThumbLine { get; init; }
        public required ThemeColor Glow { get; init; }
    }

    public sealed record TextBoxLook
    {
        public required TextBoxStateLook Idle { get; init; }
        public required TextBoxStateLook Hover { get; init; }
        public required TextBoxStateLook Focus { get; init; }
    }

    public sealed record TextBoxStateLook
    {
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Line { get; init; }
        public required ThemeColor Text { get; init; }
        public required ThemeColor Caret { get; init; }
        public required ThemeColor Selection { get; init; }
    }

    public sealed record ScrollBarLook
    {
        public required ThemeColor Track { get; init; }
        public required ThemeColor Thumb { get; init; }
    }

    /// <summary>The Settings dialog's page list.</summary>
    public sealed record NavLook
    {
        public required NavStateLook Idle { get; init; }
        public required NavStateLook Hover { get; init; }
        public required NavStateLook Selected { get; init; }
        /// <summary>The ISLAPANDORA.EU caption over the website's pages.</summary>
        public required ThemeColor Caption { get; init; }
        /// <summary>The line between the groups.</summary>
        public required ThemeColor Divider { get; init; }
        /// <summary>The selected row's bar on its left, 0 = none.</summary>
        public required double BarWidth { get; init; }
    }

    public sealed record NavStateLook
    {
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Text { get; init; }
        public required ThemeColor Bar { get; init; }
    }

    /// <summary>A row of the maps' menus (an entry, or a tick in the map's Layers menu).</summary>
    public sealed record MenuRowLook
    {
        public required MenuRowStateLook Idle { get; init; }
        public required MenuRowStateLook Hover { get; init; }
        public required MenuRowStateLook Pressed { get; init; }
        public required MenuRowStateLook Disabled { get; init; }
        public required MenuRowStateLook Ticked { get; init; }
    }

    public sealed record MenuRowStateLook
    {
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Text { get; init; }
    }

    /// <summary>The list pages' card (Waypoints, Friends): its caption band and zebra rows.</summary>
    public sealed record CardLook
    {
        public required ThemeColor Surface { get; init; }
        public required ThemeColor Line { get; init; }
        public required ThemeColor Band { get; init; }
        public required ThemeColor Stripe { get; init; }
    }

    /// <summary>The plainer card: Account's cards, What's new's notes, the sign-in's result.</summary>
    public sealed record InfoCardLook
    {
        public required ThemeColor Surface { get; init; }
        public required ThemeColor Line { get; init; }
    }

    /// <summary>A colour well's frame, a picture's well.</summary>
    public sealed record WellLook
    {
        public required ThemeColor Surface { get; init; }
        public required ThemeColor Line { get; init; }
    }

    /// <summary>The vertical rule left of the Activity lines and the Prime rows. A 0-wide rule with a 0 gap is truly nothing in the layout.</summary>
    public sealed record ListRuleLook
    {
        public required ThemeColor Line { get; init; }
        public required double Width { get; init; }
        /// <summary>The space between the rule and the rows.</summary>
        public required double Gap { get; init; }
    }

    #endregion

    #region Maps and cards

    /// <summary>What the maps draw that is chrome. Waypoints, friends and zones keep their own colours (data).</summary>
    public sealed record MapLook
    {
        public required YouLook You { get; init; }
        /// <summary>The area under the cursor in edit mode: its outline and the area pill's text.</summary>
        public required ThemeColor Hover { get; init; }
        /// <summary>Your area: its outline and the area pill's text.</summary>
        public required ThemeColor OwnArea { get; init; }
        public required ThemeColor AreaName { get; init; }
        public required ThemeColor Name { get; init; }
        public required ThemeColor NameOutline { get; init; }
        public required ThemeColor MarkerOutline { get; init; }
        public required ReadoutLook Readout { get; init; }
        public required ThemeColor ScaleBar { get; init; }
        public required ThemeColor PillText { get; init; }
        public required LayerButtonLook LayerButton { get; init; }
        public required SnapGuideLook SnapGuide { get; init; }
        /// <summary>Whether Windows 11 rounds the map window's corners (its DWM preference).</summary>
        public required bool WindowRounded { get; init; }
    }

    /// <summary>You on the maps: your arrow and your trail.</summary>
    public sealed record YouLook
    {
        public required ThemeColor Arrow { get; init; }
        /// <summary>The arrow's outline.</summary>
        public required ThemeColor Outline { get; init; }
        public required ThemeColor Trail { get; init; }
        /// <summary>
        /// A casing under the trail, a road's: its strokes drawn once more
        /// UNDERNEATH, 2 px wider, in this colour, for a light trail that would
        /// vanish on snow or a beach (PLAN.md question 26). Transparent = none,
        /// and then nothing at all is drawn (MapCanvas.CasingWidth).
        /// </summary>
        public required ThemeColor TrailOutline { get; init; }
        /// <summary>The arrow's painted glow (its radius: <see cref="GlowNumbers.You"/>).</summary>
        public required ThemeColor Glow { get; init; }
    }

    /// <summary>The map's cursor readout.</summary>
    public sealed record ReadoutLook
    {
        public required ThemeColor Glass { get; init; }
        public required ThemeColor Line { get; init; }
        public required ThemeColor Name { get; init; }
        public required ThemeColor Rest { get; init; }
    }

    /// <summary>The map's Layers button: idle, and open while its menu is.</summary>
    public sealed record LayerButtonLook
    {
        public required LayerButtonStateLook Idle { get; init; }
        public required LayerButtonStateLook Open { get; init; }
        public required ThemeColor HoverGlow { get; init; }
    }

    public sealed record LayerButtonStateLook
    {
        public required ThemeColor Fill { get; init; }
        public required ThemeColor Line { get; init; }
        public required ThemeColor Text { get; init; }
    }

    /// <summary>The edit-mode snap guides: to the screen, and to another panel.</summary>
    public sealed record SnapGuideLook
    {
        public required ThemeColor Screen { get; init; }
        public required ThemeColor Peer { get; init; }
    }

    /// <summary>The update cards and What's new.</summary>
    public sealed record UpdateCardLook
    {
        public required ThemeColor Bullet { get; init; }
        /// <summary>The ✓ after an update.</summary>
        public required ThemeColor Good { get; init; }
        public required ThemeColor Progress { get; init; }
        public required ThemeColor ProgressTrack { get; init; }
    }

    #endregion

    #region Shapes, numbers, type

    /// <summary>
    /// The four slots that have a shape, each a pick from the catalogue the
    /// code draws (<see cref="ShapeCatalogue"/>): no theme ships XAML, and a
    /// pick the slot doesn't offer fails the theme's test. The Activity bullet
    /// is a character, not a shape.
    /// </summary>
    public sealed record ShapePicks
    {
        public required ShapeKind Radio { get; init; }
        /// <summary>Stock = WPF's own slider (Theme.Apply places the matching Style).</summary>
        public required ShapeKind SliderThumb { get; init; }
        public required ShapeKind Badge { get; init; }
        /// <summary>The glyph before the dino name, the dialog's title and "Map".</summary>
        public required ShapeKind TitleGlyph { get; init; }
        /// <summary>The bullet before an Activity line, with its space ("● ").</summary>
        public required string ActivityBullet { get; init; }
    }

    /// <summary>The two cut corners (45°, top-right and bottom-left), 0 = none (<see cref="FrameGeometry"/>).</summary>
    public sealed record CutNumbers
    {
        public required double Widget { get; init; }
        /// <summary>The minimap's map square.</summary>
        public required double MapSquare { get; init; }
        public required double Dialog { get; init; }
    }

    /// <summary>The lit accents round a frame's two corners: how far they run, and the line's width; and a lit line along the whole top edge. All 0 = none.</summary>
    public sealed record LitNumbers
    {
        /// <summary>Along the top and bottom edges, past the cut.</summary>
        public required double Edge { get; init; }
        /// <summary>Down and up the sides, past the cut.</summary>
        public required double Side { get; init; }
        public required double Width { get; init; }
        /// <summary>
        /// The width of a lit line along the frame's whole top edge, 0 = none
        /// (FrameGeometry.LitTop). It wears the lit corners' colour and their
        /// glow (<see cref="GlowNumbers.Corners"/>), in both states.
        /// </summary>
        public required double Top { get; init; }
    }

    /// <summary>The painted glows' radii (never an Effect: PLAN.md section 4), 0 = none.</summary>
    public sealed record GlowNumbers
    {
        /// <summary>The lit corners.</summary>
        public required double Corners { get; init; }
        /// <summary>The whole outline in edit mode.</summary>
        public required double Edit { get; init; }
        /// <summary>The control panel's, painted inside its outline: at most its padding.</summary>
        public required double ControlPanel { get; init; }
        public required double Radio { get; init; }
        public required double Thumb { get; init; }
        /// <summary>Your arrow on the maps.</summary>
        public required double You { get; init; }
    }

    /// <summary>The corner radii, 0 = square.</summary>
    public sealed record CornerNumbers
    {
        public required ThemeRadius Widget { get; init; }
        public required ThemeRadius Dialog { get; init; }
        public required ThemeRadius Menu { get; init; }
        public required ThemeRadius Pill { get; init; }
        public required ThemeRadius Button { get; init; }
        public required ThemeRadius Card { get; init; }
        public required ThemeRadius Well { get; init; }
        public required ThemeRadius Bar { get; init; }
        public required ThemeRadius Check { get; init; }
        public required ThemeRadius ScrollThumb { get; init; }
        /// <summary>A fracture badge drawn as a Box.</summary>
        public required ThemeRadius Badge { get; init; }
        public required ThemeRadius Nav { get; init; }
        public required ThemeRadius LayerButton { get; init; }
    }

    /// <summary>The type: the family, the weights by role, and the few text sizes a theme may set (all inside a frame).</summary>
    public sealed record FontLook
    {
        public required ThemeFont Family { get; init; }
        /// <summary>
        /// The titles' family: read wherever a title weight is (the widgets'
        /// titles and the dino name; the dialog's title and the map's "Map"
        /// when they move onto the tokens). Everything else reads <see cref="Family"/>.
        /// </summary>
        public required ThemeFont TitleFamily { get; init; }
        public required ThemeWeight TitleWeight { get; init; }
        /// <summary>"Map" in the map's header.</summary>
        public required ThemeWeight MapTitleWeight { get; init; }
        /// <summary>Values and percents.</summary>
        public required ThemeWeight ValueWeight { get; init; }
        public required ThemeWeight BodyWeight { get; init; }
        public required ThemeWeight CaptionWeight { get; init; }
        public required double ControlPanelButtonSize { get; init; }
        public required double BadgeSize { get; init; }
        public required double MapAreaNameSize { get; init; }
        public required double MapNameSize { get; init; }
        public required bool MapAreaNameCaps { get; init; }
        /// <summary>Caps captions letter-spaced (WPF has no tracking: a hair-space approximation, if ever).</summary>
        public required bool SpacedCaptions { get; init; }
    }

    #endregion
}

/// <summary>
/// One theme as the loader, the Settings row and the harness see it: its id
/// (the file's name, "default" for the compiled one, what config.json holds),
/// the name players read, and its tokens. The tokens themselves never learn
/// the id (<see cref="ThemeTokens"/>).
/// </summary>
public sealed record ThemeEntry(string Id, string Name, int Order, ThemeTokens Tokens);
