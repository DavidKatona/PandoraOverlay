namespace PandoraOverlay;

/// <summary>
/// The control panel's fixed frame (1.39, the owner, Oct 25 2026): like the
/// widgets' (<see cref="WidgetFrame"/>), its root takes an explicit outer
/// size at 100% and everything lays out INSIDE it, so no font, no theme and
/// no label can grow the panel. Until 1.39 it was the one window without a
/// frame, as big as its row of buttons and its hint in the font of the day.
/// The owner saw Isle's more compact panel (Montserrat at 10 px) and wanted
/// that size for every theme, Default included: these numbers are that
/// panel, measured in the themes' step 3 (940 × 74), each button its label
/// in 10 px Montserrat Medium plus 12 a side, to the nearest pixel. A
/// label longer than its button ends in an ellipsis, centred, never wider;
/// <c>ControlPanelWidthTests</c> checks every theme's labels, captions and
/// hint fit. The panel's position is its top-left and nothing snaps to it,
/// so the frame moves nothing. Outer sizes, border included; the scale
/// slider (<c>ControlPanelScale</c>) multiplies the frame as a whole.
/// <para>
/// The gaps between the parts stay in ControlPanelWindow.xaml and
/// Controls.xaml: the 10 × 8 padding inside a 1 px border, 6 between a
/// group's buttons, a separator of 1 with 10 a side, 3 under a caption and
/// 5 over the hint. The numbers here add up with them to <see cref="Width"/>
/// and <see cref="Height"/> (the test does the sum).
/// </para>
/// </summary>
public static class ControlPanelFrame
{
    /// <summary>The panel's outer width: its one row of buttons and separators, the padding and the border.</summary>
    public const double Width = 940;

    /// <summary>The panel's outer height: a caption, the buttons, the hint, the padding and the border.</summary>
    public const double Height = 74;

    /// <summary>Every button's height; its label is centred in it.</summary>
    public const double ButtonHeight = 22;

    /// <summary>A group's caption (STATS, MINIMAP …) over its buttons, 9 px.</summary>
    public const double CaptionHeight = 12;

    /// <summary>The hint line under the row, 10 px.</summary>
    public const double HintHeight = 14;

    // Each button's width, in the row's order.
    public const double SettingsButton = 73;
    public const double ShowHideButton = 78;
    public const double ViewButton = 49;
    public const double MapViewButton = 72;
    public const double HeatmapButton = 72;
    public const double AreasButton = 52;
    public const double CheckButton = 56;
    public const double LockButton = 48;
    public const double ExitButton = 43;
}
