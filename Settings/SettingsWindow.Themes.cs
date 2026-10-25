using System.Windows;
using System.Windows.Controls;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Themes page (1.39, PLAN.md section 9): the theme list, one
/// radio row per theme in Theme.All's order (Default first), so
/// a theme's file under Assets/Themes adds its own row and nothing here
/// changes. A page of its own after General, not a row on General's Look
/// (the owner, Oct 25 2026, question 25): a row of radios beside a label
/// would have run out of room by the fourth theme, and the theme editor's
/// button, its folder and the players' own themes join this page later
/// (EDITOR.md, 1.40). Thin on purpose until then.
/// <para>
/// A click applies at once, like every setting (SettingsWindow.Apply.cs):
/// <c>_config.Theme</c> takes the theme's id and MainWindow.ApplyTheme
/// repaints every window. A theme only paints, so no size or position moves.
/// The page wears today's chrome; the dialog's own restyling is the themes'
/// step 5.
/// </para>
/// </summary>
public partial class SettingsWindow
{
    /// <summary>Each theme's radio, with the theme it chooses (WireLiveSettings writes the choice).</summary>
    private readonly List<(RadioButton Radio, ThemeEntry Theme)> _themeRadios = new();

    /// <summary>
    /// One radio row per theme, the one config names ticked (a hand-edited
    /// unknown id reads as Default, Theme.Resolve). Built with the other
    /// preferences, before InitApply, so filling it writes nothing: a radio
    /// per theme costs an opening nothing, so it doesn't wait for the page's
    /// first look.
    /// </summary>
    private void InitThemesPage()
    {
        var chosen = Theme.Resolve(_config.Theme).Id;
        var style = (Style)FindResource("Radio");
        foreach (var theme in Theme.All)
        {
            var radio = new RadioButton
            {
                Content = theme.Name,
                Style = style,
                GroupName = "Theme",
                IsChecked = theme.Id == chosen,
                Margin = new Thickness(0, _themeRadios.Count == 0 ? 0 : 6, 0, 0), // the rows' spacing, as General's checkboxes
            };
            ThemeList.Children.Add(radio);
            _themeRadios.Add((radio, theme));
        }
    }
}
