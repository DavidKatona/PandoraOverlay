using Xunit;

namespace PandoraOverlay.Tests;

public class ConfigDefaultsTests
{
    /// <summary>
    /// Every widget's size slider starts at 100% on a fresh install, and no
    /// scale is seeded from another: a 230 px legacy minimap default once made
    /// a new install start the minimap at 81% while the rest started at 100%.
    /// </summary>
    [Fact]
    public void AFreshConfigStartsEveryWidgetAtFullScale()
    {
        var cfg = new OverlayConfig();
        Assert.Equal(1.0, cfg.UiScale);
        Assert.Equal(1.0, cfg.MinimapScale);
        Assert.Equal(1.0, cfg.PrimeScale);
        Assert.Equal(1.0, cfg.ActivityScale);
        Assert.True(cfg.UpdateCheckEnabled, "the launch update check is on until switched off");
    }

    /// <summary>A sign-in leaves the session (encrypted), the browser identity and the name; a sign-out forgets the first and the last and keeps the identity.</summary>
    [Fact]
    public void SignInAndSignOutRoundTrip()
    {
        var cfg = new OverlayConfig();
        var when = new DateTime(2026, 10, 7, 16, 0, 0, DateTimeKind.Utc);
        var avatar = new Uri("https://cdn.discordapp.com/avatars/1/abc.png?size=128");
        cfg.ApplySignIn("connect.sid=s%3Aabc", "  Mozilla/5.0 Test  ", " dave ", when, avatar);
        Assert.Equal("connect.sid=s%3Aabc", cfg.GetCookie());
        Assert.Equal("", cfg.Cookie); // never the plaintext inbox
        Assert.Equal("Mozilla/5.0 Test", cfg.UserAgent);
        Assert.Equal("dave", cfg.AccountName);
        Assert.Equal(avatar.AbsoluteUri, cfg.AccountAvatar);
        Assert.Equal(when, cfg.SignedInUtc);

        cfg.ClearSignIn();
        Assert.Equal("", cfg.GetCookie());
        Assert.Null(cfg.AccountName);
        Assert.Null(cfg.AccountAvatar);
        Assert.Null(cfg.SignedInUtc);
        Assert.Equal("Mozilla/5.0 Test", cfg.UserAgent);
    }

    [Fact]
    public void ABlankIdentityOrNameChangesNothingOfTheSort()
    {
        var cfg = new OverlayConfig();
        var before = cfg.UserAgent;
        cfg.ApplySignIn("connect.sid=x", "   ", "  ", DateTime.UtcNow);
        Assert.Equal(before, cfg.UserAgent);
        Assert.Null(cfg.AccountName);
    }

    /// <summary>A pre-1.25 file's pixel size is just an unknown key now: it does not touch the scale.</summary>
    [Fact]
    public void TheOldPixelMinimapSizeIsIgnored()
    {
        var cfg = System.Text.Json.JsonSerializer.Deserialize<OverlayConfig>("""{ "MinimapSize": 230, "UiScale": 1.3 }""");
        Assert.NotNull(cfg);
        Assert.Equal(1.0, cfg!.MinimapScale);
        Assert.Equal(1.0, cfg.PrimeScale);
        Assert.Equal(1.3, cfg.UiScale);
    }
}
