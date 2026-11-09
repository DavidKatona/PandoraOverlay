using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Settings → Friends' Watch column and the PACK WIDGET group, through the
/// dialog's real controls: a tick applies with the list's batch, the cap greys
/// the other boxes in place, the group writes its keys. A counting fake sits
/// behind the dialog: none of this asks the website anything.
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

    [Fact]
    public void WatchTicksApplyAndTheCapGreysTheRest() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var config = new OverlayConfig();
        var roster = Enumerable.Range(1, 12).Select(i => PackRowsTests.F("f" + i.ToString("00"))).ToList();
        var book = PackRowsTests.Book(roster);
        var poll = new PollService(config);
        var site = CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, new WaypointLibrary(), book, poll);
        typeof(SettingsWindow).GetMethods(Any).Single(m => m.Name == "SetPage" && m.GetParameters().Length == 1).Invoke(dialog, new object[] { "Friends" });

        var boxes = ((List<(FriendEntry Entry, CheckBox Box)>)typeof(SettingsWindow).GetField("_watchBoxes", Any)!.GetValue(dialog)!)
            .Select(b => b.Box).ToList();
        Assert.Equal(12, boxes.Count);
        Assert.All(boxes, b => Assert.True(b.IsEnabled));

        foreach (var box in boxes.Take(11)) Tick(box);
        Assert.False(boxes[11].IsEnabled); // at the cap: the twelfth is greyed
        Assert.Equal("Up to 11", boxes[11].ToolTip);
        Assert.True(boxes[0].IsEnabled);    // a ticked one can always be unticked

        typeof(SettingsWindow).GetMethod("FlushChanges", Any)!.Invoke(dialog, null);
        Assert.Equal(11, book.WatchedCount); // applied with the list's batch

        Tick(boxes[0]);
        Assert.True(boxes[11].IsEnabled);    // under the cap again
        typeof(SettingsWindow).GetMethod("FlushChanges", Any)!.Invoke(dialog, null);
        Assert.Equal(10, book.WatchedCount);

        Tick((ToggleButton)dialog.FindName("PackFrameLarge"));
        Tick((ToggleButton)dialog.FindName("PackRowsCompact"));
        ((Slider)dialog.FindName("PackScaleSlider")).Value = 1.25;
        Assert.Equal(("large", "compact", 1.25), (config.PackFrame, config.PackRows, config.PackScale));
        Assert.Equal(0, site.Requests);
        dialog.Close();
    });
}
