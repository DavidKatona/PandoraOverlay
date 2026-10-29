using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The sign-in window's size (SignInFrame, 1.40.1): its browser is wide
/// enough for Discord's login to show the QR code. Laid out, not summed:
/// the window is built as the harness builds it (never shown, so its
/// WebView2 never starts and nothing is asked of Discord or the website)
/// and its browser measured at the frame's size. No theme is applied or
/// named: a theme has no size of a window, so the browser's width is the
/// frame's and the XAML's alone. The collection only keeps it from running
/// beside a test that switches the theme the window's resources come from.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public class SignInFrameTests
{
    [Fact]
    public void TheWindowTakesItsFrame() => TheThemeOnScreen.OnSta(() =>
    {
        var window = new SignInWindow();
        try
        {
            Assert.Equal(SignInFrame.Width, window.Width);
            Assert.Equal(SignInFrame.Height, window.Height);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheBrowserIsWideEnoughForDiscordsQrCode() => TheThemeOnScreen.OnSta(() =>
    {
        var window = new SignInWindow();
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(SignInFrame.Width, SignInFrame.Height));
            root.Arrange(new Rect(0, 0, SignInFrame.Width, SignInFrame.Height));
            root.UpdateLayout();
            var browser = (FrameworkElement)window.FindName("Web");
            Assert.True(browser.ActualWidth >= SignInFrame.BrowserMinWidth,
                $"the browser is {browser.ActualWidth:0.#} wide; Discord's QR code needs {SignInFrame.BrowserMinWidth}");
        }
        finally { window.Close(); }
    });
}
