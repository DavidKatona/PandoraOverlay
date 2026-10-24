using System.Windows;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// "Focus left the overlay" (1.38): the one signal behind the windows that
/// get out of the way when you click the game, the owner's ask after a
/// player was attacked with Settings open (Oct 23 2026). Settings closes,
/// the update cards close, edit mode locks (MainWindow.OnFocusLeftOverlay,
/// UpdateCardWindow). The map keeps its own instant rule (BigMapWindow).
/// <para>
/// It is read from our OWN windows only: one of them lost activation, and a
/// moment later none of them has it. What took the focus is never asked —
/// no other window is found or looked at (constraint 1) — which is also why
/// it only works while one of ours had focus to lose: a locked overlay is
/// click-through and never has it (so a "hide when the game loses focus"
/// can't be built on this).
/// </para>
/// <para>
/// The moment is for the tray. A click on its icon hands the focus to the
/// taskbar first, and its menu then takes it back for us; a double-click
/// (edit mode) needs its second click. So after a touch of the icon the
/// check waits a double-click's time, and nothing counts as "left" while
/// the tray's menu is showing.
/// </para>
/// </summary>
internal static class OverlayFocus
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);
    private static DispatcherTimer? _timer;
    private static DateTime _trayTouchedUtc;

    /// <summary>Raised on the UI thread once focus has left every overlay window.</summary>
    public static event Action? Left;

    /// <summary>Whether the tray's menu is on screen (TrayIcon sets it: the menu is WinForms, not one of the WPF windows checked here).</summary>
    public static Func<bool>? TrayMenuShowing { get; set; }

    /// <summary>A double-click's time and a little: what a tray click may need before our own window has the focus again.</summary>
    private static TimeSpan TrayWait => TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime + 100);

    /// <summary>Starts the check whenever the window loses activation.</summary>
    public static void Watch(Window window) => window.Deactivated += (_, _) => Arm();

    /// <summary>The tray icon was clicked: what follows is ours (its menu, a double-click), so the check waits for it.</summary>
    public static void TrayTouched()
    {
        _trayTouchedUtc = DateTime.UtcNow;
        Arm();
    }

    /// <summary>(Re)starts the check: a moment from now, has focus left the overlay? Also called when one of our own dialogs or the tray's menu closes.</summary>
    public static void Arm()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = Settle };
            _timer.Tick += (_, _) => Check();
        }
        _timer.Stop();
        _timer.Start();
    }

    private static void Check()
    {
        _timer?.Stop();
        var oursActive = Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) == true;
        switch (Decide(TrayMenuShowing?.Invoke() == true, DateTime.UtcNow - _trayTouchedUtc, TrayWait, oursActive))
        {
            case Verdict.Wait:
                Arm();
                break;
            case Verdict.Left:
                Left?.Invoke();
                break;
        }
    }

    internal enum Verdict { Stay, Wait, Left }

    /// <summary>The rule, pure for the tests. The tray's menu is ours (its closing arms the check again); a fresh touch of the icon is waited out.</summary>
    internal static Verdict Decide(bool trayMenuShowing, TimeSpan sinceTrayTouched, TimeSpan trayWait, bool anyOfOursActive)
    {
        if (trayMenuShowing) return Verdict.Stay;
        if (sinceTrayTouched < trayWait) return Verdict.Wait;
        return anyOfOursActive ? Verdict.Stay : Verdict.Left;
    }
}
