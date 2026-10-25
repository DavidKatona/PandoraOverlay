namespace PandoraOverlay;

/// <summary>
/// The Default theme: the overlay as it looked in 1.38, the ONE copy of
/// today's look (PLAN.md section 1, REVIEW.md objection 2). It is compiled in,
/// never a file: the record's required members make the compiler its
/// completeness test, every other theme is a sparse file laid over it, and it
/// never fails to load. Default must stay pixel-identical to 1.38, so these
/// values never move; <c>ThemeFileTests.DefaultIsTodaysLook</c> pins them,
/// and the golden renders (and from step 2 the harness's windows mode) catch
/// a window that reads the wrong one.
/// <para>
/// Each value is the literal the survey found (PLAN.md, "What the code does
/// today"), in the form the XAML wrote it, with where it comes from. A part
/// Default never draws (a glow, a lit corner, a list rule) is
/// <see cref="ThemeColor.None"/> with a size of 0. A few catalogue roles have
/// two values today (the map's pills are denser than the minimap's; the
/// map's title is SemiBold, the widgets' Bold): those got a token each, so
/// Default can keep both.
/// </para>
/// </summary>
public static class DefaultTheme
{
    private static ThemeColor C(string hex) => ThemeColor.Parse(hex);

    private static readonly ThemeColor None = ThemeColor.None;

    // The greys every window shares (the stats panel, Prime, Activity, Settings).
    private static readonly ThemeColor Bright = C("#ECF2F8"); // titles, values that must stand out
    private static readonly ThemeColor Text = C("#C7D1DA");   // labels, percents, lines
    private static readonly ThemeColor Label = C("#9AA7B0");  // secondary header text
    private static readonly ThemeColor Dim = C("#7B8790");    // status lines, footers, hints
    private static readonly ThemeColor Off = C("#5E6B76");    // nav captions, disabled text
    private static readonly ThemeColor Amber = C("#FFB300");  // a warning (health under half, a lost Prime row)
    private static readonly ThemeColor Red = C("#E53935");    // health under a quarter; the damage bar
    private static readonly ThemeColor Green = C("#7CC884");  // good: Prime's ✓, a hint, the update card
    private static readonly ThemeColor Gold = C("#FFC864");   // you, and edit mode
    private static readonly ThemeColor Glass = C("#10151B");  // the panels' glass, opaque
    private static readonly ThemeColor Hairline = C("#33FFFFFF");
    private static readonly ThemeColor HoverGlow = C("#2EFFFFFF");
    private static readonly ThemeColor PressedGlow = C("#4DFFFFFF");

    /// <summary>Today's look.</summary>
    public static ThemeTokens Tokens { get; } = new()
    {
        // MainWindow.xaml's bars and MainWindow.Stats.cs's brushes.
        Bars = new()
        {
            // Health: green, then amber under half, red under a quarter (the old HealthGood/Warn/Crit).
            // No time label is ever drawn on health or damage: their TimeLeft is the percent's grey.
            Health = Bar(fill75: C("#4CAF50"), fill50: C("#4CAF50"), fill25: Amber, fill0: Red, tint: Text),
            Stamina = Bar(C("#FFD54F"), tint: C("#FFE082")),
            Hunger = Bar(C("#FF9800"), tint: C("#FFB74D")),
            Thirst = Bar(C("#4FC3F7"), tint: C("#81D4FA")),
            Damage = Bar(Red, tint: Text),
            // The combat view's corner: quiet grey, amber when Wounded, red under a quarter.
            HealthReadout = new() { Above75 = Label, From50To75 = Label, From25To50 = Amber, Below25 = Red },
            Label = Text, // MainWindow.xaml's row names
            Height = 12,
            TrackLineWidth = 0,
            Below25Halo = 0,
        },
        // PrimeWindow.xaml.cs. A row that flipped in the latest check reads SemiBold.
        Prime = new()
        {
            Met = new() { Icon = Green, Text = Text, Weight = ThemeWeight.Normal },
            Unmet = new() { Icon = C("#9A5F5F"), Text = Dim, Weight = ThemeWeight.Normal },
            Lost = new() { Icon = C("#9A5F5F"), Text = Amber, Weight = ThemeWeight.SemiBold },
            Fresh = new() { Icon = Green, Text = Bright, Weight = ThemeWeight.SemiBold },
            Unchecked = new() { Icon = Dim, Text = Dim, Weight = ThemeWeight.Normal },
            Status = new() { PrimeElder = Gold, Ready = Green, NotReady = Label, Unknown = Label },
            Footer = new() { Checked = Dim, Stale = Amber, Failed = Amber, Cooldown = Dim },
        },
        // ActivityWindow.xaml(.cs): your bullet in your arrow's gold.
        Activity = new() { Mine = Gold, Line = Text, Warning = Amber, Quiet = Dim, Count = Label },
        Glass = new()
        {
            Widget = Glass,             // OverlayWindowBase.ApplyAppearance; the alpha is the player's slider
            Dialog = C("#F210151B"),    // Settings, the update card
            Menu = C("#F210151B"),      // the maps' menus
            Pill = C("#9910151B"),      // the minimap's pills
            MapPill = C("#B310151B"),   // the map's PillGlass
            MapWindow = Glass,          // BigMapWindow, opaque
        },
        // The widgets' frames (ThemedBorder, once OverlayWindowBase's BorderLocked / BorderEdit); the control panel's is gold for good.
        Frame = new()
        {
            Locked = new() { Line = Hairline, Lit = None, Glow = None },
            Edit = new() { Line = Gold, Glow = None },
            ControlPanel = new() { Line = Gold, Glow = None },
        },
        // MainWindow.Stats.cs's IntactBadge / FracturedBadge.
        Badge = new()
        {
            Intact = new() { Fill = C("#14FFFFFF"), Text = C("#4A5560") },
            Fractured = new() { Fill = C("#B3402020"), Text = C("#FF8A80") },
        },
        Growth = new() { Growing = Label, Paused = Amber, FullyGrown = Label },
        Text = new()
        {
            Title = Bright,
            Glyph = None,             // no title glyph in Default (Shapes.TitleGlyph is None)
            PageTitle = Label,        // Settings' PageTitle
            Bright = Bright,          // SliderValue, the selected nav row
            Text = Text,
            Text2 = Text,
            Label = Label,
            Dim = Dim,
            Off = Off,
            Caption = Dim,            // the control panel's GroupCaption, Settings' SectionCaption
            Link = C("#7FB2D9"),
            Good = Green,             // HintGood, the sign-in's Good
            Caution = Gold,           // HintWarn, the sign-in's Warn
            Danger = C("#FF8A80"),    // HintBad, the sign-in's Bad
        },
        // Settings' SaveButtonStyle / NeutralButtonStyle / DangerButtonStyle (the cards' copies are the same);
        // their hover IS the press (no pressed trigger). The control panel lays a glow over the idle fill instead.
        Button = new()
        {
            Neutral = Buttons(C("#2A3038"), Text, hover: C("#3A4250"), disabledFill: C("#1E242B"), disabledText: Off),
            Primary = Buttons(C("#2E5E38"), C("#DFF3E2"), hover: C("#3A7347"), disabledFill: C("#1E2A22"), disabledText: C("#5E7263")),
            Danger = Buttons(C("#552B2B"), C("#FFDDDD"), hover: C("#6E3838"), disabledFill: C("#2A2022"), disabledText: C("#6E5A5A")),
            HoverGlow = HoverGlow,
            PressedGlow = PressedGlow,
        },
        // Settings' Check and Radio. Hover changes only the box; disabled is the whole control at half opacity (the template's).
        Check = new()
        {
            Unticked = new() { Box = C("#1A2129"), Line = Off, Mark = Bright, Glow = None },
            Ticked = new() { Box = C("#1A2129"), Line = Label, Mark = Bright, Glow = None },
            Hover = new() { Box = C("#242C36"), Line = Off, Mark = Bright, Glow = None },
            Disabled = new() { Box = C("#1A2129"), Line = Off, Mark = Bright, Glow = None },
        },
        // Never drawn in Default: its sliders are WPF's own (the Stock thumb, Windows' colours).
        // The check box's colours, so a theme that picks a drawn thumb starts from the dialog's look.
        Slider = new() { Track = C("#1A2129"), Fill = Label, Thumb = C("#1A2129"), ThumbLine = Label, Glow = None },
        // Settings' TextBoxStyle (the sign-in's code box is the same).
        TextBox = new()
        {
            Idle = TextBoxIn(line: Off),
            Hover = TextBoxIn(line: Label),
            Focus = TextBoxIn(line: Bright),
        },
        ScrollBar = new() { Track = C("#1A2129"), Thumb = Off }, // DarkScrollBar
        // NavButton (its hover is the glow over the row) and SettingsWindow's NavSelected / NavText.
        Nav = new()
        {
            Idle = new() { Fill = None, Text = Text, Bar = None },
            Hover = new() { Fill = HoverGlow, Text = Text, Bar = None },
            Selected = new() { Fill = C("#2A3038"), Text = Bright, Bar = None },
            Caption = Off,
            Divider = C("#262E37"),
            BarWidth = 0,
        },
        // MapMenuStyles.xaml's MenuButton and MenuTick.
        MenuRow = new()
        {
            Idle = new() { Fill = None, Text = Text },
            Hover = new() { Fill = HoverGlow, Text = Text },
            Pressed = new() { Fill = PressedGlow, Text = Text },
            Disabled = new() { Fill = None, Text = Off },
            Ticked = new() { Fill = None, Text = Bright },
        },
        Separator = Hairline,
        // SettingsWindow.Waypoints.cs's CardSurface / CardBorder / BandSurface / RowStripe.
        Card = new() { Surface = C("#161C23"), Line = Hairline, Band = C("#1F262E"), Stripe = C("#0AFFFFFF") },
        // Account's cards, What's new's notes, the sign-in's result panel.
        InfoCard = new() { Surface = C("#1B222A"), Line = C("#333C46") },
        Well = new() { Surface = C("#1A2129"), Line = Off }, // WellSurface / WellBorder
        ListRule = new() { Line = None, Width = 0, Gap = 0 },
        Map = new()
        {
            // MapCanvas.xaml's PlayerArrow and the trail's three bands.
            You = new() { Arrow = Gold, Outline = C("#991A130A"), Trail = Gold, Glow = None },
            Hover = Gold,                     // AreaHoverPath, AreaHover
            OwnArea = Bright,                 // AreaOwnPath, AreaOwn
            AreaName = C("#F2F5F8"),          // NameLayer.AreaBrush
            Name = C("#E2E8EE"),              // NameLayer.NameBrush
            NameOutline = C("#E6060A10"),     // NameLayer's outline
            MarkerOutline = C("#990A1520"),   // MapCanvas.Markers
            Readout = new() { Glass = C("#E610151B"), Line = C("#26FFFFFF"), Name = Bright, Rest = Label },
            ScaleBar = Bright,
            PillText = Bright,
            // BigMapWindow.xaml's LayerButton.
            LayerButton = new()
            {
                Idle = new() { Fill = C("#0FFFFFFF"), Line = C("#1FFFFFFF"), Text = C("#6B7782") },
                Open = new() { Fill = C("#2BFFFFFF"), Line = C("#40FFFFFF"), Text = Bright },
                HoverGlow = C("#1AFFFFFF"),
            },
            SnapGuide = new() { Screen = C("#E6FFC864"), Peer = C("#E64FC3F7") }, // SnapGuideWindow
            WindowRounded = true,             // DWMWCP_ROUND
        },
        // UpdateCardWindow.
        UpdateCard = new() { Bullet = C("#FFAA00"), Good = Green, Progress = Green, ProgressTrack = C("#262D35") },
        Shapes = new()
        {
            Radio = ShapeKind.Circle,
            SliderThumb = ShapeKind.Stock,
            Badge = ShapeKind.Box,
            TitleGlyph = ShapeKind.None,
            ActivityBullet = "● ",
        },
        Cut = new() { Widget = 0, MapSquare = 0, Dialog = 0 },
        Lit = new() { Edge = 0, Side = 0, Width = 0, Top = 0 },
        Glow = new() { Corners = 0, Edit = 0, ControlPanel = 0, Radio = 0, Thumb = 0, You = 0 },
        Corner = new()
        {
            Widget = 10,
            Dialog = 10,
            Menu = 6,
            Pill = 4,
            Button = 2,
            Card = 4,
            Well = 2,
            Bar = 6,
            Check = 2,
            ScrollThumb = 4,
            Badge = 4,
            Nav = 3,
            LayerButton = 3,
        },
        Font = new()
        {
            Family = new ThemeFont("Segoe UI"),
            TitleFamily = new ThemeFont("Segoe UI"), // the titles share the family in Default
            TitleWeight = ThemeWeight.Bold,
            MapTitleWeight = ThemeWeight.SemiBold,
            ValueWeight = ThemeWeight.Normal,
            BodyWeight = ThemeWeight.Normal,
            CaptionWeight = ThemeWeight.SemiBold,
            // 12 until 1.39. The one value here that moved on purpose: the owner saw
            // Isle's compact control panel (Oct 25 2026) and wanted it for every theme,
            // so the panel got a fixed frame (ControlPanelFrame) and 10 px buttons.
            ControlPanelButtonSize = 10,
            BadgeSize = 10,
            MapAreaNameSize = 13,   // NameLayer.AreaFontSize
            MapNameSize = 12,       // NameLayer.NameFontSize
            MapAreaNameCaps = false,
            SpacedCaptions = false,
        },
    };

    #region Builders (Default's own shorthand)

    // One fill in every state (every bar but health), its lightened tint for the time label.
    private static ThemeTokens.BarLook Bar(ThemeColor fill, ThemeColor tint) => Bar(fill, fill, fill, fill, tint);

    private static ThemeTokens.BarLook Bar(ThemeColor fill75, ThemeColor fill50, ThemeColor fill25, ThemeColor fill0, ThemeColor tint) => new()
    {
        Above75 = new() { Fill = fill75, Percent = Text, TimeLeft = tint },
        From50To75 = new() { Fill = fill50, Percent = Text, TimeLeft = tint },
        From25To50 = new() { Fill = fill25, Percent = Text, TimeLeft = tint },
        Below25 = new() { Fill = fill0, Percent = Text, TimeLeft = tint },
        Track = C("#3D202830"),
        TrackLine = None,
        OnFill = Glass, // the old TimeLeftOnFill: the panel's glass, dark on the bright fill
    };

    private static ThemeTokens.ButtonFamilyLook Buttons(ThemeColor fill, ThemeColor text, ThemeColor hover, ThemeColor disabledFill, ThemeColor disabledText) => new()
    {
        Idle = new() { Fill = fill, Text = text, Line = None },
        Hover = new() { Fill = hover, Text = text, Line = None },
        Pressed = new() { Fill = hover, Text = text, Line = None },
        Disabled = new() { Fill = disabledFill, Text = disabledText, Line = None },
    };

    private static ThemeTokens.TextBoxStateLook TextBoxIn(ThemeColor line) => new()
    {
        Fill = C("#1A2129"),
        Line = line,
        Text = Bright,
        Caret = Bright,
        Selection = C("#3A5A78"),
    };

    #endregion
}
