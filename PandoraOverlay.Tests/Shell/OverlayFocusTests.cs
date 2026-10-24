using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The "focus left the overlay" rule (OverlayFocus.Decide): what closes Settings and the update cards, and locks edit mode.</summary>
public class OverlayFocusTests
{
    private static readonly TimeSpan TrayWait = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan LongAgo = TimeSpan.FromHours(1);

    [Fact]
    public void NoneOfOursActiveIsAClickAway() =>
        Assert.Equal(OverlayFocus.Verdict.Left, OverlayFocus.Decide(false, LongAgo, TrayWait, anyOfOursActive: false));

    [Fact]
    public void FocusMovingBetweenOurOwnWindowsIsNot() // the control panel to a widget, Settings to its file picker's owner
    {
        Assert.Equal(OverlayFocus.Verdict.Stay, OverlayFocus.Decide(false, LongAgo, TrayWait, anyOfOursActive: true));
    }

    [Fact]
    public void TheTraysMenuIsOurs()
    {
        Assert.Equal(OverlayFocus.Verdict.Stay, OverlayFocus.Decide(trayMenuShowing: true, LongAgo, TrayWait, anyOfOursActive: false));
    }

    /// <summary>A click on the tray icon hands the focus to the taskbar first: waited out, for the menu or a double-click's second click.</summary>
    [Fact]
    public void AFreshTrayClickIsWaitedOut()
    {
        Assert.Equal(OverlayFocus.Verdict.Wait, OverlayFocus.Decide(false, TimeSpan.FromMilliseconds(200), TrayWait, anyOfOursActive: false));
        Assert.Equal(OverlayFocus.Verdict.Left, OverlayFocus.Decide(false, TimeSpan.FromMilliseconds(700), TrayWait, anyOfOursActive: false));
    }

    [Fact]
    public void AWaitedOutTrayClickThatGaveUsTheFocusBackStays() // the double-click entered edit mode: the control panel has it
    {
        Assert.Equal(OverlayFocus.Verdict.Stay, OverlayFocus.Decide(false, TimeSpan.FromMilliseconds(700), TrayWait, anyOfOursActive: true));
    }
}
