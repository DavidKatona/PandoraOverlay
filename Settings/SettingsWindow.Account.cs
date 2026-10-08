using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Account page (v1.31): the in-app sign-in. Not signed in,
/// the page is one "Sign in with Discord" button (SignInWindow) and what it
/// does; signed in, a card with the name, whether Steam is linked, when the
/// session was taken, and Sign in again / Sign out. It replaced the cookie
/// paste box (v1.0–1.30) and its DevTools walkthrough. THE SECOND PAGE THAT
/// ACTS AT ONCE instead of on Save (the Skins page is the first): a sign-in
/// or sign-out is applied and saved on the spot, and MainWindow reads
/// CookieChanged whether the dialog is saved or cancelled — a player who
/// signs in and closes the dialog with ✕ must not lose the sign-in. The
/// card's live facts come from ONE auth/me on the page's first look per
/// dialog (never on a timer); offline, it shows what is saved.
/// </summary>
public partial class SettingsWindow
{
    private bool _signedIn;        // a session is stored (at open, or taken here)
    private bool _accountLooked;   // the one auth/me per dialog has been sent
    private bool _signOutArmed;    // Sign out is two clicks, like Delete all

    private void InitAccountPage()
    {
        _signedIn = !_firstRun;
        RenderAccount(null);
    }

    /// <summary>The page's first look: one "who am I", so the card says what the website says and not only what was saved.</summary>
    private void OpenAccountPage()
    {
        if (_accountLooked || !_signedIn) return;
        _accountLooked = true;
        _ = LookUpAccountAsync();
    }

    private async Task LookUpAccountAsync()
    {
        AccountHint.Text = "Checking with the website…";
        try
        {
            RenderAccount(await _poll.FetchAccountAsync());
        }
        catch (Exception ex)
        {
            RenderAccount(null);
            AccountHint.Text = $"Couldn't reach the website ({ex.GetType().Name}) — showing what is saved.";
        }
    }

    /// <param name="live">The website's answer, or null for "only what is saved".</param>
    private void RenderAccount(AccountInfo? live)
    {
        SignInCard.Visibility = _signedIn ? Visibility.Collapsed : Visibility.Visible;
        AccountCard.Visibility = _signedIn ? Visibility.Visible : Visibility.Collapsed;
        if (!_signedIn)
        {
            ShowAvatar(null);
            AccountStatus.Text = "Not signed in — sign in once and the overlay shows your dino from then on.";
            AccountStatus.Foreground = HintWarn;
            AccountHint.Text = "";
            return;
        }

        var name = live?.Username ?? _config.AccountName ?? "your account";
        AccountNameText.Text = name;
        AccountInitial.Text = name[..1].ToUpperInvariant();
        if (live is { Authenticated: true }) _config.AccountAvatar = live.Avatar?.AbsoluteUri; // display only: kept with the next save
        ShowAvatar(PandoraClient.AvatarAddress(_config.AccountAvatar));

        if (live is { Authenticated: false })
        {
            AccountStatus.Text = "The website session has ended — sign in again.";
            AccountStatus.Foreground = HintWarn;
        }
        else
        {
            AccountStatus.Text = "Signed in ✓ — the session renews itself while the overlay runs.";
            AccountStatus.Foreground = HintGood;
        }

        (SteamText.Text, SteamText.Foreground) = live switch
        {
            { Authenticated: true, SteamLinked: true } => ("linked ✓", HintGood),
            { Authenticated: true } => ("not linked — the overlay can't find your dino until it is (one LinkID typed in game chat, see islapandora.eu)", HintWarn),
            _ => ("—", HintNeutral)
        };
        SignedInText.Text = _config.SignedInUtc is { } when
            ? $"{when.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)} · renews while in use" // English like the rest of the overlay, whatever the PC's locale
            : "with a pasted cookie (before 1.31) · renews while in use";
        AccountHint.Text = "Sign in again to switch accounts or after a long break. Sign out forgets the session on this PC and logs it out on the website too.";
    }

    // ---- Avatar (Oct 8 2026) ------------------------------------------------------------

    /// <summary>Saved at twice the card's 40 px circle, for 200 % display scaling.</summary>
    private const int AvatarPixels = 80;

    private Uri? _avatarWanted; // the address the card should show now
    private Uri? _avatarShown;  // the address whose picture is on the card

    /// <summary>
    /// The picture over the initial. A saved copy shows at once, whenever
    /// the card is drawn; a download happens only once the page has really
    /// been looked at (the same first look that asks the website), through
    /// PollService's cookie-less picture path, and is then saved small so
    /// the next opening asks nothing. No address, a failed download or a
    /// picture Windows can't read: the initial stays.
    /// </summary>
    private async void ShowAvatar(Uri? address)
    {
        _avatarWanted = address;
        if (address is null || (_avatarShown is not null && _avatarShown != address)) PaintAvatar(null, null);
        if (address is null || _avatarShown == address) return;

        var saved = SkinThumbnails.TryLoad(address, DateTime.UtcNow, DataFolder.AvatarFolder);
        if (saved is not null)
        {
            PaintAvatar(address, saved);
            return;
        }
        if (!_accountLooked) return;

        try
        {
            var picture = await _poll.GetAvatarAsync(address);
            if (picture.Bytes is null || _avatarWanted != address) return;
            var thumbnail = OnCircleBackground(SkinThumbnails.Make(picture.Bytes, AvatarPixels));
            if (thumbnail is null) return;
            SkinThumbnails.TrySave(address, thumbnail, DataFolder.AvatarFolder);
            if (_avatarWanted == address) PaintAvatar(address, thumbnail);
        }
        catch
        {
            // fail soft: the initial is a fine answer
        }
    }

    /// <summary>
    /// The saved copy is a JPEG, which has no transparency: a see-through
    /// avatar would come back with black corners. Flattened onto the
    /// circle's own Discord blue first, it looks the same saved or not.
    /// </summary>
    private static BitmapSource? OnCircleBackground(BitmapSource? picture)
    {
        if (picture is null) return null;
        var size = new Rect(0, 0, picture.PixelWidth, picture.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2)), null, size);
            dc.DrawImage(picture, size);
        }
        var flat = new RenderTargetBitmap(picture.PixelWidth, picture.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        flat.Render(visual);
        flat.Freeze();
        return flat;
    }

    private void PaintAvatar(Uri? address, ImageSource? picture)
    {
        _avatarShown = picture is null ? null : address;
        AccountAvatarBrush.ImageSource = picture;
        AccountAvatar.Visibility = picture is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Save is gated on a session: a fresh install must sign in before anything else is worth saving.</summary>
    private void GateSave() => SaveButton.IsEnabled = !_firstRun || _signedIn;

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var outcome = SignInWindow.Run(this);
        if (outcome is null) return;
        _config.ApplySignIn(outcome.CookieHeader, outcome.UserAgent, outcome.Account.Username, DateTime.UtcNow, outcome.Account.Avatar);
        _config.Save(); // acts at once
        CookieChanged = true;
        _signedIn = true;
        _accountLooked = true; // the sign-in's own auth/me is this dialog's look
        _signOutArmed = false;
        SignOutButton.Content = "Sign out";
        RenderAccount(outcome.Account);
        GateSave();
    }

    /// <summary>Two clicks, since it ends the session on the website too; the site is told once, then everything of the session is forgotten here.</summary>
    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (!_signOutArmed)
        {
            _signOutArmed = true;
            SignOutButton.Content = "Really sign out?";
            return;
        }
        SignOutButton.IsEnabled = false;
        SignInAgainButton.IsEnabled = false;
        AccountHint.Text = "Signing out…";
        var told = await _poll.SignOutAsync();
        _config.ClearSignIn();
        _config.Save(); // acts at once
        _poll.ForgetSession();
        DataFolder.ClearBrowserFolder();
        SkinThumbnails.Clear(DataFolder.AvatarFolder);
        CookieChanged = true;
        _signedIn = false;
        _signOutArmed = false;
        SignOutButton.Content = "Sign out";
        SignOutButton.IsEnabled = true;
        SignInAgainButton.IsEnabled = true;
        RenderAccount(null);
        AccountHint.Text = told
            ? "Signed out here and on the website."
            : "Signed out here. The website could not be reached, so its side of the session lives on until it expires.";
        AccountHint.Foreground = HintNeutral;
        GateSave();
    }
}
