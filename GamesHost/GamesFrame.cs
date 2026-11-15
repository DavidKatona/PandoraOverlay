using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// The Games window's fixed frame (A11, G13): like the Settings dialog's
/// (<see cref="SettingsFrame"/>) and the widgets' (<see cref="WidgetFrame"/>),
/// the root takes an explicit outer size and everything lays out INSIDE it, so
/// no font, no theme, no game and no number of games can resize the window.
/// The same numbers in every theme: a theme paints, it never sizes a frame.
/// <para>
/// 800 x 552 outside, the 1 px border included (the prototype's size): a 40
/// px header over a view area of 798 x 510, which is exactly the Contract's
/// <c>GameFrame.ViewWidth</c> x <c>ViewHeight</c>, the room a game lays its
/// screens out in. The grid inside the same area holds three tiles to a row,
/// 244 wide with 12 between them and 16 at each side, and keeps the
/// scrollbar's room at the right whether or not there is a bar, so the tiles
/// never shift when the seventh game makes the grid scroll (G1). A tile's art
/// box is the tile's width less 12 (its border, padding and the well's line)
/// by 96: the Contract's <c>TileArtWidth</c> x <c>TileArtHeight</c>.
/// <c>GamesFrameTests</c> holds every one of these sums.
/// </para>
/// <para>
/// The whole window is drawn at ONE layout scale, <see cref="FitScale"/>: the
/// player's <c>GamesScale</c> (Settings → Widgets, 75% to 200%; the owner,
/// Nov 15 2026, A75, reversing G15's "no size slider"), and smaller still on
/// a screen where that size and a margin round it don't fit (1366 x 768 at
/// 150%, for one): never cut off, never re-laid out (A11). The layout stays
/// 800 x 552 at every scale; only the transform over it changes.
/// </para>
/// </summary>
public static class GamesFrame
{
    /// <summary>The window's outer width, the 1 px border included: the root takes it and the window sizes to the root.</summary>
    public const double Width = 800;

    /// <summary>The window's outer height, the 1 px border included.</summary>
    public const double Height = 552;

    /// <summary>The header's height, its bottom line inside it.</summary>
    public const double HeaderHeight = 40;

    /// <summary>The header's room at its left, before the back control or the title.</summary>
    public const double HeaderPaddingLeft = 12;

    /// <summary>The header's room at its right, after ✕.</summary>
    public const double HeaderPaddingRight = 8;

    /// <summary>The header's padding, as its XAML takes it.</summary>
    public static readonly Thickness HeaderPadding = new(HeaderPaddingLeft, 0, HeaderPaddingRight, 0);

    /// <summary>What the header lays its parts out in: the outer width less the border's two sides and its padding (778).</summary>
    public const double HeaderInnerWidth = Width - 2 - HeaderPaddingLeft - HeaderPaddingRight;

    /// <summary>A tile's width: three to a row (G1).</summary>
    public const double TileWidth = 244;

    /// <summary>A tile's height: fixed, whatever its game says (its texts trim).</summary>
    public const double TileHeight = 208;

    /// <summary>Between two tiles, across and down.</summary>
    public const double TileGap = 12;

    /// <summary>Left of the first column, and right of the last before the scrollbar's room.</summary>
    public const double GridSidePadding = 16;

    /// <summary>Above the first row (and, as the gap under each tile, below the last).</summary>
    public const double GridTopPadding = 12;

    /// <summary>Always reserved at the grid's right for its scrollbar (8 wide, a margin round it), so the tiles never shift when it appears (G1).</summary>
    public const double ScrollBarRoom = 10;

    /// <summary>Kept clear round the window on a small screen before it is drawn smaller (<see cref="FitScale"/>).</summary>
    public const double ScreenMargin = 24;

    /// <summary>Tiles to a row.</summary>
    public const int Columns = 3;

    /// <summary>The view area's width under the header: the border's two sides off the outer width (the Contract's view width).</summary>
    public const double ViewWidth = Width - 2;

    /// <summary>The view area's height: the border's two sides and the header off the outer height (the Contract's view height).</summary>
    public const double ViewHeight = Height - 2 - HeaderHeight;

    /// <summary>The tile's art box: the tile less its 1 px border, 4 px padding and the well's 1 px line, each side.</summary>
    public const double TileArtWidth = TileWidth - 12;

    /// <summary>The tile's art box's height.</summary>
    public const double TileArtHeight = 96;

    /// <summary>The tiles' panel: a row of tiles, each with the gap at its right (the last one's falls inside the right padding), left-aligned after the side padding.</summary>
    public const double GridPanelWidth = Columns * (TileWidth + TileGap);

    /// <summary>
    /// The tile's button: fixed, so a label that changes on arming moves
    /// nothing (.claude/rules/settings.md), and wide enough that the longest
    /// label, "Click again to start fresh", shows whole in every bundled
    /// theme's font (A52: 147 px in the widest against 138 of room at 150).
    /// </summary>
    public const double TileButtonWidth = 170;

    /// <summary>The tile's button row, at its bottom: fixed, so a font with a taller line box shows as clipped in the layout tests instead of moving the button.</summary>
    public const double TileButtonRowHeight = 24;

    // ---- The header's sound group (A82, A83) --------------------------------------------
    // On the header's right, before the hint: the speaker (mute), `−`, five level
    // pips, `+`. Before the hint, so the watch note, which comes and goes, grows to
    // its LEFT and nothing of the group ever moves (a control that jumps when a note
    // appears is clicked wrong). The same in every theme: a theme paints the group,
    // it never sizes it.

    /// <summary>Each of the group's three buttons, square: ✕'s size.</summary>
    public const double SoundButtonSize = 22;

    /// <summary>How many level pips: one per volume step (GamesSound.Steps, held equal by a test).</summary>
    public const int SoundPips = 5;

    /// <summary>A level pip's width.</summary>
    public const double SoundPipWidth = 4;

    /// <summary>Between two pips.</summary>
    public const double SoundPipGap = 2;

    /// <summary>The first (quietest) pip's height; each next one is <see cref="SoundPipRise"/> taller: 4, 6, 8, 10, 12, a meter like the growth segments.</summary>
    public const double SoundPipShortest = 4;

    /// <summary>How much taller each pip is than the one before.</summary>
    public const double SoundPipRise = 2;

    /// <summary>A pip's corner at most this, whatever the theme's <c>Corner.Progress</c>, so the meter reads as rising bars in every theme (A89).</summary>
    public const double SoundPipMaxCorner = SoundPipWidth / 4;

    /// <summary>The pips' box: as tall as the tallest pip, the pips bottom-aligned in it, the box centred in the header.</summary>
    public const double SoundPipsHeight = SoundPipShortest + (SoundPips - 1) * SoundPipRise;

    /// <summary>The pips' box's width: the pips and the gaps between them.</summary>
    public const double SoundPipsWidth = SoundPips * SoundPipWidth + (SoundPips - 1) * SoundPipGap;

    /// <summary>Between each two parts of the group.</summary>
    public const double SoundGroupGap = 4;

    /// <summary>The group's width: three buttons, the pips and the three gaps between the four (106).</summary>
    public const double SoundGroupWidth = 3 * SoundButtonSize + SoundPipsWidth + 3 * SoundGroupGap;

    /// <summary>Between the group and the hint at its right: the watch note's own 14 at its left, mirrored.</summary>
    public const double SoundGroupSpace = 14;

    /// <summary>A pip's height, from the quietest (0) up.</summary>
    public static double SoundPipHeight(int index) => SoundPipShortest + index * SoundPipRise;

    /// <summary>The space before each part of the group but the first, as its XAML takes it.</summary>
    public static readonly Thickness SoundGapBefore = new(SoundGroupGap, 0, 0, 0);

    /// <summary>The space before each pip but the first.</summary>
    public static readonly Thickness SoundPipGapBefore = new(SoundPipGap, 0, 0, 0);

    /// <summary>The group's own margin: the space to the hint.</summary>
    public static readonly Thickness SoundGroupMargin = new(0, 0, SoundGroupSpace, 0);

    /// <summary>
    /// The header's title at most: what is left of the header's
    /// <see cref="HeaderInnerWidth"/> when every optional part is shown at its
    /// widest, rounded down to a whole 10; a longer title trims (A83).
    /// <para>
    /// Measured in every bundled theme at its own fit factor (Nov 15 2026), each
    /// part with its margin; the widest is Isle's Montserrat: the back control
    /// 75.45, "Paused" 55.32, the longer watch note ("Disconnected · health not
    /// watched") 209.54, the sound group 120 (106 and its 14 to the hint), the
    /// hint 89.12 and ✕ 22, 571.43 in all; 778 less that is 206.57, so 200.
    /// (Default leaves 259.76, Nunito's themes 257.56, Rajdhani's 280.94; the
    /// planner's sums had guessed about 170.) Today's "Hatchling" is 70 to 110
    /// wide by theme. GamesLayoutTests holds the header whole in every theme
    /// with every part shown and a title longer than this.
    /// </para>
    /// </summary>
    public const double TitleMaxWidth = 200;

    /// <summary>The smallest <c>GamesScale</c> (Settings → Widgets): the widgets' own floor.</summary>
    public const double MinScale = 0.75;

    /// <summary>The largest <c>GamesScale</c>: 200%, above the widgets' 150%, since the point of the window's slider is to go bigger (A75).</summary>
    public const double MaxScale = 2.0;

    /// <summary>
    /// The one scale the whole window is drawn at on this screen (its work
    /// area, in WPF units): the chosen scale (<c>GamesScale</c>, held to
    /// <see cref="MinScale"/>..<see cref="MaxScale"/>, a value that is not a
    /// number read as 1), or less where the frame at that scale and its
    /// margin don't fit: then the largest that fits both ways. So the window
    /// is never larger than its monitor's work area less the margin, and at a
    /// chosen 1 this is the fit it always was (A11, A75).
    /// </summary>
    public static double FitScale(Size screen, double chosen)
    {
        var wanted = double.IsFinite(chosen) ? Math.Clamp(chosen, MinScale, MaxScale) : 1;
        return Math.Min(wanted, Math.Min((screen.Width - 2 * ScreenMargin) / Width, (screen.Height - 2 * ScreenMargin) / Height));
    }
}
