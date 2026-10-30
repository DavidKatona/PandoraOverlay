namespace PandoraOverlay;

/// <summary>
/// The Settings dialog's fixed frame (1.39, the owner, Oct 25 2026: "is the
/// settings window not fixed?", themes PLAN.md question 30). Like the
/// widgets' (<see cref="WidgetFrame"/>) and the control panel's
/// (<see cref="ControlPanelFrame"/>), the dialog's root takes an explicit
/// outer size and the pages lay out INSIDE it, so no font and no theme can
/// resize the dialog. Until 1.39 its height followed its text: the tallest
/// page set it, and Isle's Montserrat, with its tighter line spacing, made
/// the dialog 7 px shorter than Default's.
/// <para>
/// These are Default's dialog as 1.38 laid it out at 100%: 660 wide (the
/// window's old Width) and 551.77 tall, the sum of its rows in Segoe UI
/// (the Skins page, the tallest, under the title and over the Close row),
/// which Windows shows as a window 552 px tall. The fraction is kept on
/// purpose: WPF lays the dialog out at exactly this height, so Default's
/// pages land where they always did, to the sub-pixel, on screen and in the
/// golden renders' windows mode. A theme whose text runs shorter gets the
/// slack under its pages (Isle about 7.7 px); one whose text runs taller is
/// drawn smaller by its fit factor until every page fits (FitFactorTests), and
/// a page that still does not would scroll, never a taller dialog (the page
/// column's ScrollViewer, which the fit factor's walk holds whole).
/// Outer sizes, border included. A new page must fit within them: the
/// Settings layout tests hold every page of every theme to the frame.
/// </para>
/// <para>
/// On a small screen the frame gives way to the work area (the root's
/// MaxHeight, SettingsWindow's constructor): the page scrolls and the Close
/// row stays in view, as before. The update card is not a frame: a notice,
/// it sizes to its text.
/// </para>
/// </summary>
public static class SettingsFrame
{
    /// <summary>The dialog's outer width: the 150 px nav, the page column, the 16 px padding a side and the border.</summary>
    public const double Width = 660;

    /// <summary>The dialog's outer height, Default's 1.38 height to the sub-pixel (see the summary).</summary>
    public const double Height = 551.77;
}
