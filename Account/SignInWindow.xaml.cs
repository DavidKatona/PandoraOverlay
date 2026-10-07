using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PandoraOverlay;

/// <summary>What a sign-in hands back: the session as the site will see it, the browser identity it was issued to, and who it is.</summary>
public sealed record SignInOutcome(string CookieHeader, string UserAgent, AccountInfo Account);

/// <summary>
/// The in-app sign-in (v1.31): the website's own Discord login, inside a
/// WebView2 that browses IN PRIVATE, so Discord's login lives in memory and
/// is gone when the window closes. The player logs in on Discord's real page
/// (or scans its QR code); when Discord sends them back and the site wants
/// to show a page, the window stops that navigation, reads the site's
/// session cookie from the browser, confirms it with one /api/auth/me and
/// hands the result to the caller — no site page (and so none of its
/// polling) ever renders here. Navigation is held to islapandora.eu's login
/// routes and discord.com (SignInPolicy); anything else a player clicks
/// opens in their real browser. The address strip is read-only and always
/// shows the real host, since the password is typed into a window that is
/// ours. Nothing in here logs, shows or keeps the cookie's value. Rehearsed
/// as a spike Oct 7 2026 (see PLAN.md in the design folder).
/// </summary>
public partial class SignInWindow : Window
{
    private const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(3);

    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x7C, 0xC8, 0x84));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
    private static readonly Brush Quiet = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB0));
    private static readonly Brush Highlight = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

    private bool _landed;   // the site tried to show a page: from here on only our own about:blank may load
    private DispatcherTimer? _closeTimer;

    /// <summary>Set once the website confirmed the session; the caller stores it.</summary>
    public SignInOutcome? Outcome { get; private set; }

    /// <summary>
    /// Runs the sign-in modally and returns the outcome, or null when it was
    /// cancelled or failed. An owner (the Settings dialog) keeps the window
    /// above it; none (the tray's "Sign in again…") makes it topmost, since
    /// the game is a borderless window underneath.
    /// </summary>
    public static SignInOutcome? Run(Window? owner)
    {
        var window = new SignInWindow();
        if (owner is not null) window.Owner = owner;
        else window.Topmost = true;
        window.ShowDialog();
        return window.Outcome;
    }

    public SignInWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) =>
        {
            _closeTimer?.Stop();
            try { Web.Dispose(); } catch { /* the browser processes end with it either way */ }
        };
    }

    // ---- The browser --------------------------------------------------------

    private async Task StartAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, DataFolder.BrowserFolder);
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true; // Discord's login: memory only, forgotten on close
            await Web.EnsureCoreWebView2Async(environment, options);
        }
        catch (Exception ex)
        {
            ShowNoRuntime(ex.GetType().Name); // WebView2RuntimeNotFoundException on a PC without the runtime
            return;
        }

        var core = Web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.SourceChanged += (_, _) => ShowAddress(core.Source);
        Navigate();
    }

    private void Navigate()
    {
        _landed = false;
        ShowWeb();
        Web.CoreWebView2.Navigate(SignInPolicy.StartUrl);
    }

    /// <summary>The allowlist, and the landing that ends the sign-in (SignInPolicy decides both).</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            e.Cancel = true;
            return;
        }
        if (_landed)
        {
            // After the landing the only page we load is our own blank one.
            if (!string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            return;
        }
        if (SignInPolicy.IsSignedInLanding(uri))
        {
            e.Cancel = true; // the site's page never renders here: nothing of it runs in the background
            _landed = true;
            _ = CaptureAsync();
            return;
        }
        if (!SignInPolicy.IsAllowed(uri))
        {
            e.Cancel = true;
            if (e.IsUserInitiated) OpenInBrowser(e.Uri); // a clicked link belongs in the real browser; a redirect is just stopped
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // never a second window of ours
        if (e.IsUserInitiated) OpenInBrowser(e.Uri);
    }

    private void ShowAddress(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return;
        var https = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        LockRun.Foreground = https ? Good : Quiet;
        HostRun.Text = https ? uri.Host : source;
        PathRun.Text = https ? uri.AbsolutePath : "";
        StepText.Text = SignInPolicy.StepLabel(uri);
    }

    // ---- The capture --------------------------------------------------------

    /// <summary>
    /// The site has signed the browser in: read ITS cookies (the HttpOnly
    /// session cookie included — this is the browser's own jar, not page
    /// script), confirm the session with one auth/me through the same
    /// client the overlay polls with, and show the result.
    /// </summary>
    private async Task CaptureAsync()
    {
        var core = Web.CoreWebView2;
        var userAgent = core.Settings.UserAgent;
        string header;
        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync("https://" + SignInPolicy.SiteHost);
            header = SignInPolicy.CookieHeader(cookies.Select(c => new KeyValuePair<string, string>(c.Name, c.Value)));
            core.Navigate("about:blank");
        }
        catch (Exception ex)
        {
            ShowFailed($"The session could not be read from the browser ({ex.GetType().Name}).");
            return;
        }
        if (!SignInPolicy.HasSession(header))
        {
            ShowFailed("The website did not sign you in — it sent you back without a session.");
            return;
        }

        ShowChecking();
        AccountInfo account;
        string rolled;
        try
        {
            using var client = new PandoraClient(header, userAgent);
            account = await client.FetchAccountAsync();
            rolled = client.CurrentCookie; // the site renews the session on every answer; keep the renewed one
        }
        catch (Exception ex)
        {
            ShowFailed($"Signed in, but the website could not be reached to confirm it ({ex.GetType().Name}).");
            return;
        }
        if (!account.Authenticated)
        {
            ShowFailed("The website does not recognise the session it just issued. Please try again.");
            return;
        }

        Outcome = new SignInOutcome(rolled, userAgent, account);
        if (account.SteamLinked) ShowDone(account);
        else ShowNoSteam(account);
    }

    // ---- The stages ---------------------------------------------------------

    private void ShowWeb()
    {
        WebFrame.Visibility = Visibility.Visible;
        AddressStrip.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        Buttons(cancel: true);
        FooterNote.Text = "This is Discord's own login page — the overlay never reads what you type here. " +
                          "Only islapandora.eu and discord.com can open in this window; other links open in your browser.";
    }

    private void ShowResult(Brush tone, string glyph, string title, string subtitle, Brush subtitleTone)
    {
        WebFrame.Visibility = Visibility.Collapsed;
        AddressStrip.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ResultRing.Stroke = tone;
        ResultGlyph.Foreground = tone;
        ResultGlyph.Text = glyph;
        ResultTitle.Text = title;
        ResultSubtitle.Text = subtitle;
        ResultSubtitle.Foreground = subtitleTone;
        ResultLines.Children.Clear();
        ResultBox.Visibility = Visibility.Collapsed;
        ResultFoot.Text = "";
    }

    private void ShowChecking()
    {
        ShowResult(Quiet, "", "Signed in — one moment", "Confirming the session with the website…", Quiet);
        Buttons(cancel: true);
        FooterNote.Text = "One request to islapandora.eu, to read your name and whether a Steam account is linked.";
    }

    private void ShowDone(AccountInfo account)
    {
        ShowResult(Good, "", $"Signed in as {account.Username ?? "your account"}",
                   "Steam account linked ✓   ·   the overlay is connected", Good);
        AddFact("", Good, "The website session is saved, encrypted for your Windows user.");
        AddFact("", Good, "Discord's login is NOT kept: this window forgets it as it closes.");
        AddFact("", Good, "The session renews itself while the overlay runs.");
        ResultBox.Visibility = Visibility.Visible;
        Buttons(done: true);
        FooterNote.Text = "The website's page was never loaded: the overlay stopped as soon as Discord sent you back.";
        StartAutoClose();
    }

    private void ShowNoSteam(AccountInfo account)
    {
        ShowResult(Warn, "", $"Signed in as {account.Username ?? "your account"}",
                   "…but no Steam account is linked to it yet", Warn);
        AddLine("The overlay needs it to find your dino. Linking is done once:");
        var step = 1;
        if (!account.IsVerified)
        {
            AddStep(step++, "Verify yourself in the server's Discord first — the website asks for that before linking.");
        }
        if (account.LinkId is { } linkId)
        {
            AddStep(step, "In game, type your LinkID in local chat:");
            AddCode(linkId);
        }
        else
        {
            AddStep(step++, "Open islapandora.eu and get your LinkID (it is on the Friends and Extras pages).");
            AddStep(step, "In game, type the LinkID in local chat.");
        }
        AddLine("That's it — the overlay finds your dino on its next poll; no new sign-in needed.");
        ResultBox.Visibility = Visibility.Visible;
        Buttons(site: true, done: true);
        FooterNote.Text = "You ARE signed in and the session is saved. Linking Steam is the website's job (the LinkID is checked in game), so this only explains it.";
    }

    private void ShowFailed(string reason)
    {
        ShowResult(Bad, "", "That didn't work", reason, Warn);
        AddLine("Try again. If it keeps failing, log in to islapandora.eu once in your normal browser, then come back.");
        ResultBox.Visibility = Visibility.Visible;
        Buttons(retry: true, cancel: true);
        FooterNote.Text = "Nothing was saved.";
    }

    private void ShowNoRuntime(string note)
    {
        ShowResult(Bad, "", "The sign-in window needs Microsoft's WebView2 Runtime",
                   $"It comes with Windows 11 and most Windows 10 PCs; this PC does not seem to have it ({note}).", Warn);
        AddLine("Install it from Microsoft — free, a minute — then try again.");
        ResultBox.Visibility = Visibility.Visible;
        Buttons(runtime: true, retry: true, cancel: true);
        FooterNote.Text = "The installer normally fetches it; a plain-zip copy relies on the one already on the PC.";
    }

    private void Buttons(bool site = false, bool runtime = false, bool retry = false, bool done = false, bool cancel = false)
    {
        SiteButton.Visibility = site ? Visibility.Visible : Visibility.Collapsed;
        RuntimeButton.Visibility = runtime ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        DoneButton.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = cancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddFact(string glyph, Brush tone, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = tone,
            Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top
        });
        row.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Fact"), Margin = default });
        ResultLines.Children.Add(row);
    }

    private void AddLine(string text) => ResultLines.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Fact") });

    private void AddStep(int number, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock
        {
            Text = $"{number}.", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Highlight,
            Width = 22, VerticalAlignment = VerticalAlignment.Top
        });
        row.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Fact"), Margin = default });
        ResultLines.Children.Add(row);
    }

    /// <summary>The LinkID, big enough to copy by eye into the game's chat.</summary>
    private void AddCode(string code) => ResultLines.Children.Add(new Border
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x21, 0x29)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x5E, 0x6B, 0x76)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
        Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(22, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock
        {
            Text = code, FontFamily = new FontFamily("Consolas"), FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xF8))
        }
    });

    private void StartAutoClose()
    {
        var left = (int)AutoClose.TotalSeconds;
        ResultFoot.Text = $"This window closes in {left} s…";
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _closeTimer.Tick += (_, _) =>
        {
            if (--left <= 0)
            {
                _closeTimer.Stop();
                DialogResult = true;
                return;
            }
            ResultFoot.Text = $"This window closes in {left} s…";
        };
        _closeTimer.Start();
    }

    // ---- Buttons ------------------------------------------------------------

    private void Done_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(); // Outcome, if any, is kept

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2 is null)
        {
            _ = StartAsync(); // the runtime may have been installed meanwhile
            return;
        }
        Navigate();
    }

    private void OpenSite_Click(object sender, RoutedEventArgs e) => OpenInBrowser("https://" + SignInPolicy.SiteHost + "/");

    private void GetRuntime_Click(object sender, RoutedEventArgs e) => OpenInBrowser(RuntimeDownloadUrl);

    private static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* fail soft: the address is on screen */ }
    }
}
