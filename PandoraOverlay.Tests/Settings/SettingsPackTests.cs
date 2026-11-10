using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Settings → Friends' Watch column and the PACK WIDGET group, through the
/// dialog's real controls: who the Pack shows (every friend on your dino, or
/// only the ticked ones) greys or frees the Watch boxes in place, a tick
/// applies with the list's batch, any number of them, the group writes its
/// keys. A counting fake sits behind the dialog: none of this asks the website.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class SettingsPackTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Tick(ToggleButton box)
    {
        box.IsChecked = box.IsChecked != true; // a click toggles first, then raises Click
        box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void Choose(RadioButton radio)
    {
        radio.IsChecked = true;
        radio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    [Fact]
    public void TheModeGreysTheWatchBoxesAndTicksHaveNoCap() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var config = new OverlayConfig();
        var roster = Enumerable.Range(1, 14).Select(i => PackRowsTests.F("f" + i.ToString("00"))).ToList();
        var book = PackRowsTests.Book(roster);
        var poll = new PollService(config);
        var site = CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, new WaypointLibrary(), book, poll);
        typeof(SettingsWindow).GetMethods(Any).Single(m => m.Name == "SetPage" && m.GetParameters().Length == 1).Invoke(dialog, new object[] { "Friends" });
        var boxes = ((List<(FriendEntry Entry, CheckBox Box)>)typeof(SettingsWindow).GetField("_watchBoxes", Any)!.GetValue(dialog)!)
            .Select(b => b.Box).ToList();
        Assert.Equal(14, boxes.Count);

        // By default every friend on your dino: the ticks don't count, the boxes are greyed.
        Assert.True(((RadioButton)dialog.FindName("PackShowsAll")).IsChecked);
        Assert.All(boxes, b => Assert.False(b.IsEnabled));

        Choose((RadioButton)dialog.FindName("PackShowsTicked"));
        Assert.Equal("ticked", config.PackShows);
        Assert.All(boxes, b => Assert.True(b.IsEnabled));

        foreach (var box in boxes.Take(13)) Tick(box); // more than 1.44.0-rc.1's eleven
        Assert.True(boxes[13].IsEnabled);
        typeof(SettingsWindow).GetMethod("FlushChanges", Any)!.Invoke(dialog, null);
        Assert.Equal(13, book.WatchedCount);

        Choose((RadioButton)dialog.FindName("PackShowsAll"));
        Assert.Equal("all", config.PackShows);
        Assert.All(boxes, b => Assert.False(b.IsEnabled));
        Assert.Equal(13, book.WatchedCount); // kept for the ticked mode

        Tick((ToggleButton)dialog.FindName("PackFrameLarge"));
        Tick((ToggleButton)dialog.FindName("PackRowsCompact"));
        ((Slider)dialog.FindName("PackScaleSlider")).Value = 1.25;
        Assert.Equal(("large", "compact", 1.25), (config.PackFrame, config.PackRows, config.PackScale));
        Assert.Equal(0, site.Requests);
        dialog.Close();
    });
}
