using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Friends part: the friend list — one row per friend the
/// roster has shown, editing YOUR side of it (nickname, colour, on the map,
/// in the feed, tracked) on a draft copy of the FriendBook; Save commits the
/// preferences, Cancel drops them. The roster's own facts (who is a friend,
/// in game now, last seen) are read-only here — they are the site's. Same
/// class as SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    // ---- Friends page -----------------------------------------------------------
    private readonly FriendBook _book;
    private readonly IReadOnlyList<FriendState>? _roster; // the last roster at dialog open (null = none fetched yet)
    private readonly List<FriendEntry> _friendDraft;
    private string? _draftTrackedFriend;
    private bool _friendsDirty;

    private static readonly Brush InGameBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0xC8, 0x84));

    /// <summary>The list: one card, rows sorted in-game first, then by name.</summary>
    private void BuildFriendRows()
    {
        FriendRows.Children.Clear();
        var inGame = _friendDraft.Count(e => IsInGame(e.SteamId));
        FriendCount.Text = _friendDraft.Count == 0 ? "" : $"{_friendDraft.Count} friends · {inGame} in game";

        if (_friendDraft.Count == 0)
        {
            FriendRows.Children.Add(new TextBlock
            {
                Text = _roster is null
                    ? "The friends list loads once the overlay is connected and the Activity widget or the minimap's friend arrows are on."
                    : "No friends yet — add them on islapandora.eu.",
                Foreground = HintNeutral, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
            });
            return;
        }

        var body = new StackPanel();
        body.Children.Add(BuildFriendBand());
        var stripe = false;
        foreach (var entry in _friendDraft
                     .OrderByDescending(e => IsInGame(e.SteamId))
                     .ThenBy(e => e.Nickname ?? e.Name, StringComparer.OrdinalIgnoreCase))
        {
            body.Children.Add(BuildFriendRow(entry, stripe));
            stripe = !stripe;
        }
        FriendRows.Children.Add(new Border
        {
            Child = body, CornerRadius = new CornerRadius(4), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            Background = CardSurface, Padding = new Thickness(6, 6, 6, 4), Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private bool IsInGame(string steamId) => _roster?.Any(f => f.SteamId == steamId && f.InGame) == true;

    /// <summary>The six-column grid every friend row and the caption share: colour | name | last seen | Map | Feed | Track.</summary>
    private static Grid NewFriendRowGrid()
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        return row;
    }

    private Border BuildFriendBand()
    {
        var row = NewFriendRowGrid();
        row.Margin = new Thickness(0);
        var caption = new TextBlock
        {
            Text = "YOUR FRIENDS", Foreground = HintNeutral, FontSize = 10,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(caption, 1);
        row.Children.Add(caption);
        return new Border
        {
            Child = row, Background = BandSurface, CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private Grid BuildFriendRow(FriendEntry entry, bool stripe)
    {
        var row = NewFriendRowGrid();
        row.Background = stripe ? RowStripe : Brushes.Transparent;

        // Colour disc in the same dark well as the checkboxes; click cycles
        // the palette. The default is the friend's stable hashed colour.
        var colour = entry.Colour ?? FriendColour.Default(entry.SteamId);
        var dot = new Ellipse { Width = 9, Height = 9, Fill = PaletteBrushes[colour] };
        var well = new Border
        {
            Child = dot, Width = 15, Height = 15, CornerRadius = new CornerRadius(2),
            Background = WellSurface, BorderBrush = WellBorder, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = $"{WaypointPalette.Colours[colour].Name} — click to change"
        };
        well.MouseLeftButtonDown += (_, _) =>
        {
            var next = WaypointPalette.Wrap((entry.Colour ?? FriendColour.Default(entry.SteamId)) + 1);
            entry.Colour = next;
            dot.Fill = PaletteBrushes[next];
            well.ToolTip = $"{WaypointPalette.Colours[next].Name} — click to change";
            _friendsDirty = true;
        };
        row.Children.Add(well);

        // One box shows the name you see: the nickname if set, else the site's.
        // Typing sets a nickname; clearing it, or typing the site's name, drops it.
        var name = new TextBox
        {
            Text = entry.Nickname ?? entry.Name, Style = (Style)FindResource("NameBox"), Margin = new Thickness(8, 0, 8, 0),
            ToolTip = $"On the site: {entry.Name}"
        };
        name.TextChanged += (_, _) =>
        {
            var nick = FriendBook.SanitizeNickname(name.Text);
            entry.Nickname = nick is null || nick == entry.Name ? null : nick;
            _friendsDirty = true;
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var seen = new TextBlock
        {
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        if (IsInGame(entry.SteamId))
        {
            seen.Text = "in game";
            seen.Foreground = InGameBrush;
        }
        else
        {
            seen.Text = entry.LastSeenUtc is { } at ? Ago(DateTime.UtcNow - at) : "—";
            seen.Foreground = HintNeutral;
        }
        if (entry.LastDino is { } dino)
        {
            seen.ToolTip = entry.LastGrowth is { } g ? $"Last seen as {dino} at {g * 100:0}% growth" : $"Last seen as {dino}";
        }
        Grid.SetColumn(seen, 2);
        row.Children.Add(seen);

        var map = new CheckBox
        {
            IsChecked = entry.ShowOnMap, Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "Draw them on the minimap while they are in game and sharing their location"
        };
        map.Click += (_, _) =>
        {
            entry.ShowOnMap = map.IsChecked == true;
            _friendsDirty = true;
        };
        Grid.SetColumn(map, 3);
        row.Children.Add(map);

        var feed = new CheckBox
        {
            IsChecked = entry.Notify, Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "Post their spawns, exits and other events to the Activity feed"
        };
        feed.Click += (_, _) =>
        {
            entry.Notify = feed.IsChecked == true;
            _friendsDirty = true;
        };
        Grid.SetColumn(feed, 4);
        row.Children.Add(feed);

        var track = new RadioButton
        {
            GroupName = "TrackFriend", IsChecked = entry.SteamId == _draftTrackedFriend,
            Style = (Style)FindResource("Radio"), HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "The minimap follows them: a ring on their arrow, and the footer shows the distance"
        };
        track.Click += (_, _) =>
        {
            // A radio can't be un-clicked, so clicking the tracked one untracks.
            if (_draftTrackedFriend == entry.SteamId)
            {
                _draftTrackedFriend = null;
                track.IsChecked = false;
            }
            else
            {
                _draftTrackedFriend = entry.SteamId;
            }
            _friendsDirty = true;
        };
        Grid.SetColumn(track, 5);
        row.Children.Add(track);

        return row;
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago".</summary>
    private static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }

    private void ActivityScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ActivityScaleLabel != null) ActivityScaleLabel.Text = $"{e.NewValue:0%}";
    }

    /// <summary>Friend management is the website's: requests, blocks and the privacy toggles are never called from the overlay.</summary>
    private void ManageFriends_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://islapandora.eu/friends") { UseShellExecute = true });
        }
        catch
        {
            // Fail soft — the hint line already names the site.
        }
    }
}
