using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The maps' "Watch …" / "Unwatch …" on a friend's arrow (the Pack): after Track, greyed at the cap, unwatching never.</summary>
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
    public void WatchSitsAfterTrackAndFlipsTheBook() => TheThemeOnScreen.OnSta(() =>
    {
        var roster = new[] { PackRowsTests.F("zorro") };
        var book = PackRowsTests.Book(roster);
        var actions = new MapActions(new OverlayConfig(), new WaypointLibrary(), book, name => name);

        var menu = Menu(actions, roster[0]);
        var texts = menu.Children.OfType<Button>().Select(Text).ToList();
        Assert.Equal(new[] { "Track zorro", "Watch zorro", "Waypoint at zorro" }, texts.Take(3));
        Click(menu.Children.OfType<Button>().Single(b => Text(b) == "Watch zorro"));
        Assert.True(book.Watches("zorro"));

        menu = Menu(actions, roster[0]);
        Click(menu.Children.OfType<Button>().Single(b => Text(b) == "Unwatch zorro"));
        Assert.False(book.Watches("zorro"));
    });

    [Fact]
    public void AtTheCapWatchIsGreyedAndUnwatchIsNot() => TheThemeOnScreen.OnSta(() =>
    {
        var roster = Enumerable.Range(1, 12).Select(i => PackRowsTests.F("f" + i)).ToList();
        var book = PackRowsTests.Book(roster, roster.Take(11).Select(f => f.SteamId!).ToArray());
        var actions = new MapActions(new OverlayConfig(), new WaypointLibrary(), book, name => name);

        var watch = Menu(actions, roster[11]).Children.OfType<Button>().Single(b => Text(b) == "Watch f12");
        Assert.False(watch.IsEnabled);
        Assert.Equal("Up to 11", watch.ToolTip);
        var unwatch = Menu(actions, roster[0]).Children.OfType<Button>().Single(b => Text(b) == "Unwatch f1");
        Assert.True(unwatch.IsEnabled);
    });
}
