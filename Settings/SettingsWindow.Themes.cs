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
/// repaints every window, this dialog included (<see cref="ApplyTheme"/>).
/// A theme only paints, so no size or position moves.
/// </para>
/// <para>
/// <b>The dialog's own repaint.</b> Its XAML reads every colour, corner and
/// font by DynamicResource and its frame is a ThemedBorder, so it restyles by
/// itself under the mouse; the nav's selected row is the checked one, drawn
/// by its template; the status lines take their tone by resource reference
/// (ShowTone). What a page BUILT in code (the waypoint and friend rows, the
/// skin tiles, the stored dinos, the rules' cards) carries the brushes of
/// the theme it was built in, so each such page notes that theme
/// (<see cref="Drawn"/>) and is redrawn from the data it holds when it is
/// next shown in another (<see cref="RedrawIfStale"/>): the page on screen at
/// once, the others on their next look, which costs what a first look costs,
/// once, after a rare click. A redraw calls the page's RENDER, never its
/// Open…: Dino storage's asks the website on every call past its guards, and
/// a theme click must never become a trigger of a request (CLAUDE.md,
/// constraint 2). Pages built in XAML (Account, General, Hotkeys …) need
/// nothing.
/// </para>
/// </summary>
public partial class SettingsWindow
{
    /// <summary>The theme each code-built page was last drawn in, by its SetPage key.</summary>
    private readonly Dictionary<string, ThemeTokens> _builtFor = new();

    /// <summary>A code-built page drew itself in the theme on screen (each page's render calls it).</summary>
    private void Drawn(string page) => _builtFor[page] = Theme.Current;

    /// <summary>
    /// A theme switch with the dialog open (MainWindow.ApplyTheme, after the
    /// swap): the page shown is redrawn now if it was built in code; the
    /// others wait for their next look. Nothing is asked of the website and
    /// nothing is written.
    /// </summary>
    public void ApplyTheme() => RedrawIfStale(_lastPage);

    /// <summary>
    /// Redraws a code-built page from what it holds when it was drawn in
    /// another theme than the one on screen. Each case is the page's render,
    /// from its own copy: the waypoint rows from the draft, the friend rows
    /// from the friends' copy, the skin tiles from the held list, the stored
    /// dinos from the held list (it reopens the open card), the rules from the
    /// loaded copy. A page in the middle of something (a skin being applied;
    /// a stored dino's rename, delete or reload) is left as it is: its next
    /// render, when that is done, draws it in the new theme.
    /// </summary>
    private void RedrawIfStale(string page)
    {
        if (!_builtFor.TryGetValue(page, out var drawnIn) || drawnIn == Theme.Current) return;
        switch (page)
        {
            case "Waypoints" when _waypointRowsBuilt: BuildWaypointRows(); break;
            case "Friends" when _friendRowsBuilt: BuildFriendRows(); break;
            case "Skins" when !_skinApplying: RenderSkins(); break;
            case "Storage" when !_storageLoading && !_storageBusy && _storageCards.All(c => c.Disarm is null): RenderStorage(); break;
            case "Rules" when _rulesBuilt: RenderRules(); break;
        }
    }

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
