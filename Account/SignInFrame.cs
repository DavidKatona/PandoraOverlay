namespace PandoraOverlay;

/// <summary>
/// The sign-in window's fixed size (1.40.1), the same in every theme: a
/// theme has no size of a window. Its width is set by Discord's login page,
/// which shows its QR code only on a page at least about 831 px wide
/// (measured Oct 26 2026: 830 hid it, 835 showed it) and otherwise offers
/// only email and password. From 1.31 to 1.40 the window was 620 wide, its
/// browser 584, so no player ever saw the QR code, and as the browser forgets
/// Discord on close (InPrivate), Discord took every password login for a new
/// device and could ask for its email check: a player without access to
/// their email could not sign in at all (a support ticket, Oct 26 2026).
/// <para>
/// The browser is the window less 36: the window's 1 px border, its 16 px
/// padding a side and the browser frame's 1 px border
/// (SignInWindow.xaml). At 920 it is 884, some 50 px over Discord's line,
/// room for Discord to move it a little; <see cref="BrowserMinWidth"/> is
/// what <c>SignInFrameTests</c> holds the laid-out browser to. Should
/// Discord move the line further, the fix is this width, measured again.
/// The height is 1.31's. Outer sizes, border included.
/// </para>
/// </summary>
public static class SignInFrame
{
    /// <summary>The window's outer width: the browser, wide enough for Discord's QR code, plus 36 (see the summary).</summary>
    public const double Width = 920;

    /// <summary>The window's outer height, unchanged since 1.31.</summary>
    public const double Height = 690;

    /// <summary>The narrowest the browser may be laid out: Discord's QR line (about 831) and some room.</summary>
    public const double BrowserMinWidth = 860;
}
