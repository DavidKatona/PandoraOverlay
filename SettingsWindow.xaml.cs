using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// First-run / on-demand setup dialog. Takes a pasted cookie header value,
/// cleans up the usual paste accidents, validates it live, and stores it
/// through the config's DPAPI vault. Returns DialogResult == true on save.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush HintNeutral = new SolidColorBrush(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush HintGood = new SolidColorBrush(Color.FromRgb(0x7C, 0xC8, 0x84));
    private static readonly Brush HintWarn = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x64));
    private static readonly Brush HintBad = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));

    private readonly OverlayConfig _config;

    public SettingsWindow(OverlayConfig config)
    {
        InitializeComponent();
        _config = config;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OpenMap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://islapandora.eu/live-map")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            HintText.Text = "Couldn't open the browser — visit islapandora.eu/live-map manually.";
            HintText.Foreground = HintWarn;
        }
    }

    private void CookieBox_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    /// <summary>Fixes the usual paste accidents without changing valid input.</summary>
    private static string Clean(string raw)
    {
        var s = (raw ?? "").Trim();
        s = Regex.Replace(s, @"^\s*cookie\s*:\s*", "", RegexOptions.IgnoreCase);
        s = s.Trim().Trim('"', '\'').Trim();
        s = Regex.Replace(s, @"\s*[\r\n]+\s*", " ");
        return s.TrimEnd(';', ' ');
    }

    private void Validate()
    {
        var s = Clean(CookieBox.Text);
        var hasSid = s.Contains("connect.sid=");
        var hasCf = s.Contains("cf_clearance=");

        if (s.Length == 0)
        {
            HintText.Text = "Waiting for a pasted cookie…";
            HintText.Foreground = HintNeutral;
            SaveButton.IsEnabled = false;
        }
        else if (hasSid && hasCf)
        {
            HintText.Text = "Looks good \u2713 — both session and Cloudflare cookies found.";
            HintText.Foreground = HintGood;
            SaveButton.IsEnabled = true;
        }
        else if (hasSid)
        {
            HintText.Text = "cf_clearance is missing. This can still work, but if the overlay " +
                            "gets blocked, go back and copy the WHOLE cookie value.";
            HintText.Foreground = HintWarn;
            SaveButton.IsEnabled = true;
        }
        else
        {
            HintText.Text = "connect.sid not found — that doesn't look like the cookie header. " +
                            "Make sure you copy the full value of \"cookie\" under Request Headers.";
            HintText.Foreground = HintBad;
            SaveButton.IsEnabled = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.SetCookie(Clean(CookieBox.Text));
        _config.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
