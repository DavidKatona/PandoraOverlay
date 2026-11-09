using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// The frame contract's strings (FIT.md section 3): the places where room is
/// tight and the layout can't be laid out from a theme file alone, measured
/// at a theme's fit factor. Each is a string (or a row of strings side by
/// side), a size, a weight and a box; the boxes come from the frame constants
/// (<see cref="WidgetFrame"/>, <see cref="ControlPanelFrame"/>) less the
/// known insets each window's XAML gives its parts, never a number of their
/// own. Until the fit factor's release they were two tests' private lists
/// (<c>MontserratTests</c>' widths, <c>ControlPanelWidthTests</c>' model);
/// those tests now hold every theme to this list, and <c>FitFactorTests</c>
/// measures it at every candidate factor beside the walk
/// (<see cref="FitProbe"/>) over the dialogs.
/// <para>
/// The one hand-kept part of the fit, kept short on purpose: what the walk
/// can reach it measures. It shrinks to nothing when the theme editor's
/// preview builds the widgets and the control panel in the app (EDITOR.md),
/// and the walk measures them as it measures the dialogs.
/// </para>
/// <para>
/// Body text is measured at the theme's factor (<see cref="ThemeTokens.Size"/>);
/// a title (the title family) at its own size, as it is drawn. A line within
/// half a pixel of its box is drawn whole (<see cref="FitProbe.Slack"/>), in
/// width and in height: the control panel's captions in Nunito came to 12.28
/// px in their 12 px row, empty descender room under capitals (the Nightshade
/// attempt, Oct 29 2026).
/// </para>
/// </summary>
public static class FrameContract
{
    #region The known insets (each as its window's XAML has it)

    /// <summary>A widget's border, a side (MainWindow, PrimeWindow, ActivityWindow: BorderThickness="1").</summary>
    public const double WidgetBorder = 1;

    /// <summary>A widget's padding, a side (Padding="12,10").</summary>
    public const double WidgetPadding = 12;

    /// <summary>A widget's content width: the frame less its border and padding a side.</summary>
    public const double WidgetContent = WidgetFrame.Width - 2 * WidgetBorder - 2 * WidgetPadding;

    /// <summary>A widget's padding, top and bottom (Padding="12,10").</summary>
    public const double WidgetPaddingY = 10;

    /// <summary>
    /// The Pack's header row and its gap to the rows (PackWindow.xaml: the
    /// header's fixed Height and its bottom Margin): the Activity feed's
    /// header as measured, a 19 px line for the 14 px title and 7 under it
    /// (the board, Nov 8 2026). The rows' room is the frame less these, the
    /// border and the padding (<see cref="PackRows.Room"/>).
    /// </summary>
    public const double PackHeaderHeight = 19, PackHeaderGap = 7;

    /// <summary>The stats panel's row names' column (MainWindow.xaml, ColumnDefinition Width="58": the frame's 58 + 170 + 44).</summary>
    public const double StatsLabelColumn = 58;

    /// <summary>The gap between a widget title's glyph and its title (MainWindow.xaml's ThemeShape, Gap="4").</summary>
    public const double TitleGlyphGap = 4;

    // The control panel's gaps (ControlPanelWindow.xaml and Controls.xaml), the
    // ones ControlPanelFrame's numbers add up with to its outer size.
    public const double PanelBorder = 1;
    public const double PanelPaddingX = 10, PanelPaddingY = 8;   // RootPanel's Padding="10,8"
    public const double PanelButtonGap = 6;                      // PanelButton's Margin, none on a group's last
    public const double PanelSeparator = 1 + 2 * 10;             // PanelSeparator: 1 wide, 10 a side
    public const double PanelCaptionMargin = 1;                  // PanelCaption's Margin="1,0,0,3"
    public const double PanelCaptionGap = 3;
    public const double PanelHintMargin = 2, PanelHintGap = 5;   // HintText's Margin="2,5,0,0"
    public const double PanelLabelPadding = 6;                   // PanelButton's Padding="6,0": how close a label may come to the edges

    #endregion

    #region The strings

    /// <summary>Where a string sits, for the messages: one of the four groups below.</summary>
    public const string PrimeRows = "Prime's rows", BarLabels = "the stats panel's row names",
                        Headers = "the widgets' headers", ControlPanel = "the control panel",
                        LayersMenu = "the control panel's Layers menu";

    /// <summary>One text in one face: a size (a body size, at the theme's factor; a title's as drawn) and its weight from the theme.</summary>
    public sealed record Piece(string Text, Func<ThemeTokens, double> Size, Func<ThemeTokens, ThemeWeight> Weight, bool Title = false);

    /// <summary>
    /// Pieces side by side in one box: their widths, plus what stands beside
    /// them (a title's glyph), fit its width; the tallest fits its height.
    /// </summary>
    public sealed record Slot(string Where, string Name, IReadOnlyList<Piece> Pieces, Func<ThemeTokens, double> Width,
                             double Height = double.PositiveInfinity, Func<ThemeTokens, double>? Beside = null);

    /// <summary>A string that does not fit its box at a theme's factor.</summary>
    public sealed record Miss(string Where, string Name, string Problem)
    {
        public override string ToString() => $"{Where}: \"{Name}\" {Problem}";
    }

    /// <summary>
    /// The control panel's row as its XAML has it: a lone Settings button, then
    /// each group's caption over its buttons, then (after the session gap,
    /// <see cref="ControlPanelFrame.SessionGap"/>) Lock and Exit. The minimap's
    /// Map view, Heatmap and Areas are one Layers menu since the Pack (Nov 2026).
    /// </summary>
    public static readonly IReadOnlyList<(string? Caption, (string Label, double Width)[] Buttons)> PanelRow = new (string?, (string, double)[])[]
    {
        (null, new[] { ("Settings…", ControlPanelFrame.SettingsButton) }),
        ("STATS", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("View", ControlPanelFrame.ViewButton) }),
        ("MINIMAP", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("Layers", ControlPanelFrame.LayersButton) }),
        ("PRIME", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("Check", ControlPanelFrame.CheckButton) }),
        ("ACTIVITY", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton) }),
        ("PACK", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton) }),
        (null, new[] { ("Lock", ControlPanelFrame.LockButton), ("Exit", ControlPanelFrame.ExitButton) }),
    };

    /// <summary>
    /// The control panel's Layers menu (ControlPanelWindow.Layers.cs): its two
    /// captions with the longest hotkey a capture box can write on their right,
    /// and its rows with a hotkey where a row has one, in the menu's width less
    /// its padding, the row's padding and the mark's column.
    /// </summary>
    public static readonly IReadOnlyList<(string Text, string? Key, bool Caption)> LayersMenuRows = new (string, string?, bool)[]
    {
        ("VIEW", "Ctrl+Shift+F12", true),
        ("Island view", null, false),
        ("Centered view", null, false),
        ("LAYERS", null, true),
        ("Heatmap", "Ctrl+Shift+F12", false),
        ("Areas", null, false),
    };

    // The Layers menu's insets (ControlPanelWindow.xaml and Layers.cs).
    public const double LayersMenuPadding = 4 + 1;   // the panel's Padding="4" in its 1 px border
    public const double LayersRowPadding = 10;       // MenuTick's Padding="10,4,16,4" (left), and the right edge's 10 a caption keeps
    public const double LayersMarkColumn = 18;       // MenuTick's ✓ column
    public const double LayersKeyGap = 8;            // between a row's label and its hotkey

    /// <summary>A group of the panel's buttons, with the gaps between them.</summary>
    public static double PanelGroupWidth((string Label, double Width)[] buttons) =>
        buttons.Sum(b => b.Width) + PanelButtonGap * (buttons.Length - 1);

    /// <summary>The longest hint the control panel's SetHotkeyLabel can write: a two-modifier F-key.</summary>
    public const string PanelHint = "drag panels to move · right-click map = waypoints · Ctrl+Shift+F12 locks";

    /// <summary>Every slot, in the order a message names the first that fails.</summary>
    public static IReadOnlyList<Slot> Slots { get; } = Build();

    private static List<Slot> Build()
    {
        var slots = new List<Slot>();

        // Prime: every condition at every row state's weight (a newly met row
        // is the widest) in what a row leaves: the content less the theme's
        // list rule and its gap and the ✓ / ✗ column.
        Func<ThemeTokens, ThemeWeight>[] rowWeights =
        {
            t => t.Prime.Met.Weight, t => t.Prime.Unmet.Weight, t => t.Prime.Lost.Weight,
            t => t.Prime.Fresh.Weight, t => t.Prime.Unchecked.Weight,
        };
        foreach (var text in PrimeConditions.Texts)
            foreach (var weight in rowWeights)
                slots.Add(new Slot(PrimeRows, text, new[] { new Piece(text, _ => 11, weight) },
                                   t => WidgetContent - t.ListRule.Width - t.ListRule.Gap - PrimeWindow.IconColumn));

        // The bars' row names in their column, at the body weight.
        foreach (var label in new[] { "Health", "Stamina", "Hunger", "Thirst", "Damage", "Speed" })
            slots.Add(new Slot(BarLabels, label, new[] { new Piece(label, _ => 11, t => t.Font.BodyWeight) }, _ => StatsLabelColumn));

        // The headers: the title (its own family and weight; the stats panel's
        // after the theme's title glyph and its gap, the one header that has
        // it) beside its right-hand readout. The stats panel's dino name ends
        // in an ellipsis where it runs long, so there the readout must still
        // leave the name its first letter and the ellipsis; Prime's and
        // Activity's titles don't trim, so their headers fit whole.
        // "Deinosuchus ♂" and a growth line (GrowthReadout's: the most
        // characters, the widest drawn, and a common one; measured Nov 2 2026,
        // "Subadult" runs wider than "Juvenile" in every bundled font and the
        // digits differ in Montserrat and Rajdhani), the combat view's corner,
        // the longest species on Server rules with "Elder" and a common line,
        // and the words that stand there out of game.
        static double Glyph(ThemeTokens t) =>
            ShapeCatalogue.NaturalSize(ShapeSlot.TitleGlyph, t.Shapes.TitleGlyph) is { Width: > 0 } g ? g.Width + TitleGlyphGap : 0;
        static Piece Title(string s) => new(s, _ => 14, t => t.Font.TitleWeight, Title: true);
        static Piece Corner(string s) => new(s, _ => 12, t => t.Font.BodyWeight);
        foreach (var (name, corner) in new[]
                 {
                     ("Deinosuchus ♂", "12% · ~23h 59m to Juvenile"),
                     ("Deinosuchus ♂", "40% · ~20h 44m to Subadult"),
                     ("Deinosuchus ♂", "62% · ~2h 18m to Adult"),
                     ("Deinosuchus ♂", "Wounded"),
                     ("Pachycephalosaurus ♀", "Elder"),
                     ("Pachycephalosaurus ♀", "62% · ~2h 18m to Adult"),
                     ("Pachycephalosaurus ♀", "Wounded"),
                     ("Not in-game", ""),
                     ("Not signed in", ""),
                     ("Connecting…", ""),
                 })
            slots.Add(new Slot(Headers, $"{name} · {corner}", new[] { Title(name[..1] + "…"), Corner(corner) }, _ => WidgetContent, Beside: Glyph));
        foreach (var (title, corner) in new[] { ("Prime", "Not ready · 10/10 (7 needed)"), ("Activity", "12 of 30 friends in game"), ("Pack", "11 of 11 in game") })
            slots.Add(new Slot(Headers, $"{title} · {corner}", new[] { Title(title), Corner(corner) }, _ => WidgetContent));

        // The control panel: every label in its button (inside the label
        // padding a side) and the button's height; every caption over its
        // group and in its row; the longest hint inside the panel and its row.
        foreach (var (caption, buttons) in PanelRow)
        {
            foreach (var (label, width) in buttons)
                slots.Add(new Slot(ControlPanel, label, new[] { new Piece(label, t => t.Font.ControlPanelButtonSize, t => t.Font.BodyWeight) },
                                   _ => width - 2 * PanelLabelPadding, ControlPanelFrame.ButtonHeight));
            if (caption is null) continue;
            var group = PanelGroupWidth(buttons);
            slots.Add(new Slot(ControlPanel, caption, new[] { new Piece(caption, _ => 9, t => t.Font.CaptionWeight) },
                               _ => group - PanelCaptionMargin, ControlPanelFrame.CaptionHeight));
        }
        slots.Add(new Slot(ControlPanel, "the hint", new[] { new Piece(PanelHint, _ => 10, t => t.Font.BodyWeight) },
                           _ => ControlPanelFrame.Width - 2 * (PanelPaddingX + PanelBorder) - PanelHintMargin, ControlPanelFrame.HintHeight));

        // The control panel's Layers menu: a caption and its hotkey across the
        // menu less the caption's own margins; a row's label and its hotkey in
        // what the tick's padding and its ✓ column leave.
        foreach (var (text, key, caption) in LayersMenuRows)
        {
            var pieces = new List<Piece>
            {
                caption ? new Piece(text, _ => 10, t => t.Font.CaptionWeight) : new Piece(text, _ => 12, t => t.Font.BodyWeight),
            };
            if (key is not null) pieces.Add(new Piece(key, _ => 10, t => t.Font.BodyWeight));
            slots.Add(new Slot(LayersMenu, text, pieces, _ => caption ? LayersCaptionWidth : LayersRowWidth,
                               Beside: key is null ? null : _ => LayersKeyGap));
        }

        AddPackSlots(slots);
        return slots;
    }

    /// <summary>A Layers menu caption's room: the menu less its padding and the caption's margins (12 left, 10 right).</summary>
    public const double LayersCaptionWidth = ControlPanelFrame.LayersMenuWidth - 2 * LayersMenuPadding - 12 - 10;

    /// <summary>A Layers menu row's room: the menu less its padding, MenuTick's (10 left, 16 right) and the ✓ column.</summary>
    public const double LayersRowWidth = ControlPanelFrame.LayersMenuWidth - 2 * LayersMenuPadding - LayersRowPadding - 16 - LayersMarkColumn;

    #region The Pack (Nov 2026)

    // The Pack's row parts (PackWindow.xaml.cs), the insets its rows lay out with.
    public const double PackDot = 8 + 6;              // the colour dot and its gap
    public const double PackBadges = 3 * 16 + 2 * 3;  // three 16 px badges, 3 apart
    public const double PackBadgeWidth = 16, PackBadgeHeight = 14;
    public const double PackFullBadgeMargins = 6 + 4; // a full row's badges: 6 before, 4 after
    public const double PackCompactBadgeMargins = 4 + 4;
    public const double PackPercentMargin = 2;
    public const double PackMetaMargin = 6, PackSexMargin = 4;
    public const double PackCompactNameMax = 88;      // a compact row's name column (its names end in "…" past it)
    public const double PackCompactBarMin = 40;       // the least a compact row's bar keeps
    public const double PackCompactBarMargins = 6 + 2;
    public const double PackColumnGap = 6;            // between a full row's four captioned bars
    public const double PackCaptionHeight = 14;  // PackWindow's CaptionHeight: a 10 px line in every bundled font

    /// <summary>The Pack's rows, in its strings' group.</summary>
    public const string PackRowsWhere = "the Pack's rows";

    /// <summary>
    /// The Pack's tight strings: a full row's right end (the sex, the three
    /// badges and "100%") beside a one-letter name and its ellipsis, the four
    /// captions in their quarter columns, a compact row's badges and "100%"
    /// beside its name column and its least bar, a badge's letter in its badge, a compact row's
    /// note beside the longest name, and the lines that take the whole width.
    /// The header is with the other widgets' headers.
    /// </summary>
    private static void AddPackSlots(List<Slot> slots)
    {
        static Piece Name(string s) => new(s, _ => 13, t => t.Font.CaptionWeight);
        static Piece Body(string s, double size) => new(s, _ => size, t => t.Font.BodyWeight);

        slots.Add(new Slot(PackRowsWhere, "a full row's right end", new[] { Name("W…"), Body("♂", 12), Name("100%") }, _ => WidgetContent,
                           Beside: _ => PackDot + PackMetaMargin + PackSexMargin + PackFullBadgeMargins + PackBadges + PackPercentMargin));
        foreach (var caption in new[] { "Health", "Stamina", "Hunger", "Thirst" })
            slots.Add(new Slot(PackRowsWhere, caption, new[] { Body(caption, 10) }, _ => (WidgetContent - 3 * PackColumnGap) / 4, PackCaptionHeight));
        slots.Add(new Slot(PackRowsWhere, "a compact row's right end", new[] { Name("100%") }, _ => WidgetContent,
                           Beside: _ => PackDot + PackCompactNameMax + PackCompactBarMargins + PackCompactBarMin + PackCompactBadgeMargins + PackBadges + PackPercentMargin));
        foreach (var letter in new[] { "H", "B", "L" })
            slots.Add(new Slot(PackRowsWhere, $"the {letter} badge", new[] { new Piece(letter, t => t.Font.BadgeSize, _ => ThemeWeight.Bold) },
                               _ => PackBadgeWidth, PackBadgeHeight));
        foreach (var note in new[] { "Other species", "No stats shown", "Not in game" })
            slots.Add(new Slot(PackRowsWhere, $"a compact row's \"{note}\"", new[] { Body(note, 11) },
                               _ => WidgetContent - PackDot - PackCompactNameMax - PackMetaMargin));
        foreach (var (line, size) in new[]
                 {
                     ("Other species · no stats shown", 11.0), ("No stats shown", 11.0), ("+11 more in game", 11.0),
                     (PackRows.NobodyInGame, 12.0), (PackRows.NotInGame, 12.0),
                 })
            slots.Add(new Slot(PackRowsWhere, line, new[] { Body(line, size) }, _ => WidgetContent));
    }

    #endregion

    #endregion

    #region The measure

    /// <summary>Every slot that doesn't fit at this theme's factor (its tokens carry it), in the list's order.</summary>
    public static IEnumerable<Miss> Misses(ThemeTokens t)
    {
        var families = new Dictionary<ThemeFont, FontFamily>();
        FontFamily Family(ThemeFont font) =>
            families.TryGetValue(font, out var f) ? f : families[font] = Theme.FontFamilyOf(font.Address);

        foreach (var slot in Slots)
        {
            double width = 0, height = 0;
            foreach (var piece in slot.Pieces)
            {
                if (piece.Text.Length == 0) continue;
                var size = piece.Size(t);
                var text = new FormattedText(piece.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Family(piece.Title ? t.Font.TitleFamily : t.Font.Family), FontStyles.Normal, piece.Weight(t).ToFontWeight(), FontStretches.Normal),
                    piece.Title ? size : t.Size(size), Brushes.White, 1.0);
                width += text.WidthIncludingTrailingWhitespace;
                height = Math.Max(height, text.Height);
            }
            width += slot.Beside?.Invoke(t) ?? 0;
            var room = slot.Width(t);
            if (width > room + FitProbe.Slack)
                yield return new Miss(slot.Where, slot.Name, FormattableString.Invariant($"{width:0.0} px in {room:0.0}"));
            if (height > slot.Height + FitProbe.Slack)
                yield return new Miss(slot.Where, slot.Name, FormattableString.Invariant($"{height:0.00} px tall in a {slot.Height:0.##} px row"));
        }
    }

    #endregion
}
