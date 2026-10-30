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
                        Headers = "the widgets' headers", ControlPanel = "the control panel";

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

    /// <summary>The control panel's row as its XAML has it: a lone Settings button, then each group's caption over its buttons, then Lock and Exit.</summary>
    public static readonly IReadOnlyList<(string? Caption, (string Label, double Width)[] Buttons)> PanelRow = new (string?, (string, double)[])[]
    {
        (null, new[] { ("Settings…", ControlPanelFrame.SettingsButton) }),
        ("STATS", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("View", ControlPanelFrame.ViewButton) }),
        ("MINIMAP", new[]
        {
            ("Show/hide", ControlPanelFrame.ShowHideButton), ("Map view", ControlPanelFrame.MapViewButton),
            ("Heatmap", ControlPanelFrame.HeatmapButton), ("Areas", ControlPanelFrame.AreasButton),
        }),
        ("PRIME", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton), ("Check", ControlPanelFrame.CheckButton) }),
        ("ACTIVITY", new[] { ("Show/hide", ControlPanelFrame.ShowHideButton) }),
        (null, new[] { ("Lock", ControlPanelFrame.LockButton), ("Exit", ControlPanelFrame.ExitButton) }),
    };

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
        // "Deinosuchus ♂" and a growth line (the longest, and a common one), the
        // combat view's corner, the longest species on Server rules, and the
        // words that stand there out of game.
        static double Glyph(ThemeTokens t) =>
            ShapeCatalogue.NaturalSize(ShapeSlot.TitleGlyph, t.Shapes.TitleGlyph) is { Width: > 0 } g ? g.Width + TitleGlyphGap : 0;
        static Piece Title(string s) => new(s, _ => 14, t => t.Font.TitleWeight, Title: true);
        static Piece Corner(string s) => new(s, _ => 12, t => t.Font.BodyWeight);
        foreach (var (name, corner) in new[]
                 {
                     ("Deinosuchus ♂", "Growth 99.9% · ~23h 59m"),
                     ("Deinosuchus ♂", "Growth 62.5% · ~2h 18m"),
                     ("Deinosuchus ♂", "Wounded"),
                     ("Pachycephalosaurus ♀", "Fully grown"),
                     ("Pachycephalosaurus ♀", "Growth 62.5% · ~2h 18m"),
                     ("Pachycephalosaurus ♀", "Wounded"),
                     ("Not in-game", ""),
                     ("Not signed in", ""),
                     ("Connecting…", ""),
                 })
            slots.Add(new Slot(Headers, $"{name} · {corner}", new[] { Title(name[..1] + "…"), Corner(corner) }, _ => WidgetContent, Beside: Glyph));
        foreach (var (title, corner) in new[] { ("Prime", "Not ready · 10/10 (7 needed)"), ("Activity", "12 of 30 friends in game") })
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
        return slots;
    }

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
