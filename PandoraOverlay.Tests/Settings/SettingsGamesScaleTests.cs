using System.Reflection;
using System.Windows.Controls;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Settings → Widgets' GAMES group (the owner, Nov 15 2026, A75), through the
/// dialog's real controls: the Games window's Scale slider runs 75% to 200%
/// (the window's own range, <see cref="GamesFrame"/>), shows the config's value
/// held to that range without writing it, and writes GamesScale at once with
/// its percent beside it. A counting fake sits behind the dialog: none of this
/// asks the website.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class SettingsGamesScaleTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static SettingsWindow Dialog(OverlayConfig config, out CountingSite site)
    {
        var poll = new PollService(config);
        site = CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, new WaypointLibrary(), new FriendBook(), poll);
        typeof(SettingsWindow).GetMethods(Any).Single(m => m.Name == "SetPage" && m.GetParameters().Length == 1).Invoke(dialog, new object[] { "Widgets" });
        return dialog;
    }

    [Fact]
    public void TheSliderRunsTheWindowsRangeAndWritesTheScaleAtOnce() => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var config = new OverlayConfig();
        var dialog = Dialog(config, out var site);
        var slider = (Slider)dialog.FindName("GamesScaleSlider");
        var label = (TextBlock)dialog.FindName("GamesScaleLabel");

        Assert.Equal((GamesFrame.MinScale, GamesFrame.MaxScale), (slider.Minimum, slider.Maximum));
        Assert.Equal((0.05, 0.25), (slider.SmallChange, slider.LargeChange)); // the widgets' steps
        Assert.Equal(1.0, slider.Value);
        Assert.Equal($"{1.0:0%}", label.Text);

        slider.Value = 1.5;
        Assert.Equal(1.5, config.GamesScale);
        Assert.Equal($"{1.5:0%}", label.Text);
        slider.Value = 2.0;
        Assert.Equal(2.0, config.GamesScale);
        Assert.Equal(0, site.Requests);
        dialog.Close();
    });

    /// <summary>A hand-edited value out of range shows at the nearest end, and filling the slider writes nothing (.claude/rules/settings.md).</summary>
    [Theory]
    [InlineData(1.75, 1.75)]
    [InlineData(3.0, 2.0)]
    [InlineData(0.2, 0.75)]
    public void TheSliderShowsTheConfigsValueHeldToItsRange(double stored, double shown) => TheThemeOnScreen.OnSta(() =>
    {
        Theme.EnsureLoaded();
        var config = new OverlayConfig { GamesScale = stored };
        var dialog = Dialog(config, out _);

        Assert.Equal(shown, ((Slider)dialog.FindName("GamesScaleSlider")).Value);
        Assert.Equal(stored, config.GamesScale);
        dialog.Close();
    });
}
