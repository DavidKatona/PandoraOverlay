using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The maps' "Watch …" / "Unwatch …" on a friend's arrow: after Track, in the Pack's ticked mode only, never greyed.</summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class MapMenuWatchTests
{
    private static string Text(Button button) => ((StackPanel)button.Content).Children.OfType<TextBlock>().Single().Text;

    private static StackPanel Menu(MapActions actions, FriendState friend)
    {
        var items = new StackPanel();
        var builder = new MapMenuBuilder(items, new Style(typeof(Button)), () => Brushes.Gray, actions, _ => Brushes.White,
                                         () => null, () => { }, _ => { });
        builder.Build(new MapMenuSpot((1, 1), null, friend));
        return items;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void InTheTickedModeWatchSitsAfterTrackAndFlipsTheBook() => TheThemeOnScreen.OnSta(() =>
    {
        var roster = new[] { PackRowsTests.F("zorro") };
        var book = PackRowsTests.Book(roster);
        var actions = new MapActions(new OverlayConfig { PackShows = "ticked" }, new WaypointLibrary(), book, name => name);

        var menu = Menu(actions, roster[0]);
        Assert.Equal(new[] { "Track zorro", "Watch zorro", "Waypoint at zorro" }, menu.Children.OfType<Button>().Select(Text).Take(3));
        Click(menu.Children.OfType<Button>().Single(b => Text(b) == "Watch zorro"));
        Assert.True(book.Watches("zorro"));

        menu = Menu(actions, roster[0]);
        Click(menu.Children.OfType<Button>().Single(b => Text(b) == "Unwatch zorro"));
        Assert.False(book.Watches("zorro"));
    });

    /// <summary>By default the Pack shows every friend on your dino, and a tick would change nothing: no entry.</summary>
    [Fact]
    public void ByDefaultThereIsNoWatchEntry() => TheThemeOnScreen.OnSta(() =>
    {
        var roster = new[] { PackRowsTests.F("zorro") };
        var actions = new MapActions(new OverlayConfig(), new WaypointLibrary(), PackRowsTests.Book(roster), name => name);
        var texts = Menu(actions, roster[0]).Children.OfType<Button>().Select(Text).ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Watch"));
        Assert.Equal(new[] { "Track zorro", "Waypoint at zorro" }, texts.Take(2));
    });

    [Fact]
    public void ManyTickedNeverGreysIt() => TheThemeOnScreen.OnSta(() =>
    {
        var roster = Enumerable.Range(1, 15).Select(i => PackRowsTests.F("f" + i)).ToList();
        var book = PackRowsTests.Book(roster, roster.Take(14).Select(f => f.SteamId!).ToArray());
        var actions = new MapActions(new OverlayConfig { PackShows = "ticked" }, new WaypointLibrary(), book, name => name);
        Assert.True(Menu(actions, roster[14]).Children.OfType<Button>().Single(b => Text(b) == "Watch f15").IsEnabled);
    });
}
