using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Settings › General › Sounds' "Click sounds" row (A103), through the
/// dialog's real controls: filled from the config and wired after, so
/// filling writes nothing (a hand-edited step stays as it is); the box
/// writes on Checked and Unchecked, before the Click event, so the click
/// layer reads the new value (ticking it is heard at the step, clearing it
/// silent); the slider writes whole steps and shows "N of 5", is disabled
/// with the box off, and its release plays press at the new step. A
/// counting fake sits behind the dialog: none of this asks the website.
/// The layout in every theme is <see cref="DialogLayoutTests"/>' and
/// <see cref="FitFactorTests"/>'.
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class SettingsClickSoundTests
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed record Row(SettingsWindow Dialog, CheckBox Box, Slider Slider, TextBlock Label, CountingSite Site);

    private static Row General(OverlayConfig config)
    {
        Theme.EnsureLoaded();
        var poll = new PollService(config);
        var site = CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, new WaypointLibrary(), new FriendBook(), poll);
        typeof(SettingsWindow).GetMethods(Any).Single(m => m.Name == "SetPage" && m.GetParameters().Length == 1).Invoke(dialog, new object[] { "General" });
        return new Row(dialog, (CheckBox)dialog.FindName("ClickSoundCheck"), (Slider)dialog.FindName("ClickSoundSlider"), (TextBlock)dialog.FindName("ClickSoundLabel"), site);
    }

    [Fact]
    public void AFreshConfigShowsTheRowOnAtThreeOfFive() => TheThemeOnScreen.OnSta(() =>
    {
        var row = General(new OverlayConfig());

        Assert.True(row.Box.IsChecked);
        Assert.Equal("Click sounds", row.Box.Content);
        Assert.Equal((1.0, 5.0, 3.0), (row.Slider.Minimum, row.Slider.Maximum, row.Slider.Value));
        Assert.Equal((1.0, 1.0, 1.0), (row.Slider.SmallChange, row.Slider.LargeChange, row.Slider.TickFrequency));
        Assert.True(row.Slider.IsSnapToTickEnabled);
        Assert.True(row.Slider.IsMoveToPointEnabled);
        Assert.True(row.Slider.IsEnabled);
        Assert.Equal("3 of 5", row.Label.Text);
        Assert.Equal(ClickMark.Press, UiClicks.GetCue(row.Slider)); // its release: the commonest click, at the new level
        Assert.Equal(ClickMark.Auto, UiClicks.GetCue(row.Box)); // a CheckBox: toggle-on, toggle-off by type
        Assert.Equal(0, row.Site.Requests);
        row.Dialog.Close();
    });

    /// <summary>Filled from the config before it is wired: a step out of range shows at the nearest end and nothing is written back, the box's off included.</summary>
    [Theory]
    [InlineData(9, 5)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public void TheRowIsFilledFromTheConfigAndFillingWritesNothing(int stored, int shown) => TheThemeOnScreen.OnSta(() =>
    {
        var config = new OverlayConfig { ClickSoundEnabled = false, ClickSoundVolume = stored };
        var row = General(config);

        Assert.False(row.Box.IsChecked);
        Assert.Equal(shown, row.Slider.Value);
        Assert.Equal($"{shown} of 5", row.Label.Text);
        Assert.Equal((false, stored), (config.ClickSoundEnabled, config.ClickSoundVolume));
        row.Dialog.Close();
    });

    [Fact]
    public void TheSliderWritesWholeStepsAndShowsThem() => TheThemeOnScreen.OnSta(() =>
    {
        var config = new OverlayConfig();
        var row = General(config);

        row.Slider.Value = 5;
        Assert.Equal((5, "5 of 5"), (config.ClickSoundVolume, row.Label.Text));
        row.Slider.Value = 1.4; // set in code between two ticks: read as a whole step
        Assert.Equal((1, "1 of 5"), (config.ClickSoundVolume, row.Label.Text));
        row.Slider.Value = 3.6;
        Assert.Equal((4, "4 of 5"), (config.ClickSoundVolume, row.Label.Text));
        Assert.Equal(0, row.Site.Requests);
        row.Dialog.Close();
    });

    [Fact]
    public void TheSliderIsDisabledWithTheBoxOff() => TheThemeOnScreen.OnSta(() =>
    {
        var row = General(new OverlayConfig());

        row.Box.IsChecked = false;
        Assert.False(row.Slider.IsEnabled);
        row.Box.IsChecked = true;
        Assert.True(row.Slider.IsEnabled);
        row.Dialog.Close();
    });

    // ---- The box writes before the click ----------------------------------------------------

    private static readonly object WatchLock = new();
    private static bool _watching;

    [ThreadStatic]
    private static Action<object>? _onClick;

    /// <summary>A click handler of the test's own, registered as the click layer's is (a class handler on every ButtonBase), so it runs where the layer does.</summary>
    private static void Watch(Action<object>? onClick)
    {
        lock (WatchLock)
        {
            if (!_watching)
            {
                EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) => _onClick?.Invoke(sender)), handledEventsToo: true);
                _watching = true;
            }
        }
        _onClick = onClick;
    }

    /// <summary>The box writes ClickSoundEnabled on Checked and Unchecked, which WPF raises inside the toggle: a class handler on Click already reads the new value.</summary>
    [Fact]
    public void TheBoxWritesBeforeTheClick() => TheThemeOnScreen.OnSta(() =>
    {
        var config = new OverlayConfig();
        var row = General(config);
        var seen = new List<bool>();
        Watch(sender =>
        {
            if (ReferenceEquals(sender, row.Box)) seen.Add(config.ClickSoundEnabled);
        });
        try
        {
            ClickSeam.OnClick(row.Box); // cleared
            ClickSeam.OnClick(row.Box); // ticked
        }
        finally
        {
            Watch(null);
        }

        Assert.Equal(new[] { false, true }, seen);
        Assert.True(config.ClickSoundEnabled);
        row.Dialog.Close();
    });

    /// <summary>
    /// Through the click layer and the real gate on a fake speaker: ticking
    /// the box is heard at the step (toggle-on), clearing it is silent, and the
    /// slider's release plays press at the step it was moved to (A82: a level
    /// is never set blind).
    /// </summary>
    [Fact]
    public void TickingIsHeardClearingIsSilentAndTheSlidersReleaseIsHeardAtItsNewStep() => TheThemeOnScreen.OnSta(() =>
    {
        var config = new OverlayConfig();
        var row = General(config);
        var speaker = new FakeSpeaker();
        var now = TimeSpan.FromSeconds(1);
        var bank = new ClickBank(null, (_, _) => { });
        UiClicks.Attach();
        UiClicks.Sink = new ClickSound(speaker, config, () => now, bank);
        try
        {
            ClickSeam.Click(row.Box); // cleared: silent
            now += TimeSpan.FromSeconds(1);
            ClickSeam.Click(row.Box); // ticked: heard at step 3
            now += TimeSpan.FromSeconds(1);
            ClickSeam.Press(row.Slider);
            row.Slider.Value = 5;
            ClickSeam.Lift(row.Slider); // released at 5: press at step 5
        }
        finally
        {
            UiClicks.Sink = null;
        }

        Assert.Equal(new[] { (bank.Samples(ClickCue.ToggleOn)!, SoundSteps.GainOf(3)), (bank.Samples(ClickCue.Press)!, SoundSteps.GainOf(5)) }, speaker.Played);
        Assert.Equal((true, 5), (config.ClickSoundEnabled, config.ClickSoundVolume));
        Assert.Equal(0, row.Site.Requests);
        row.Dialog.Close();
    });

    // ---- Where the row sits ------------------------------------------------------------------------

    /// <summary>First under SOUNDS, before the chimes, and the hint says the Games window keeps its own volume.</summary>
    [Fact]
    public void TheRowIsFirstUnderSoundsAndTheHintSaysTheGamesWindowsOwn() => TheThemeOnScreen.OnSta(() =>
    {
        var row = General(new OverlayConfig());
        var page = (StackPanel)((FrameworkElement)row.Box.Parent).Parent;
        var children = page.Children.Cast<FrameworkElement>().ToList();
        var sounds = children.FindIndex(c => c is TextBlock { Text: "SOUNDS" });

        Assert.Same(row.Box.Parent, children[sounds + 1]);
        Assert.Same(row.Dialog.FindName("LowStatChimeCheck"), children[sounds + 2]);
        Assert.Equal(new Thickness(0, 8, 0, 0), children[sounds + 2].Margin);
        Assert.Equal(140, row.Box.Width);
        Assert.Contains(children, c => c is TextBlock { Text: "Each chime plays the Windows \"Exclamation\" sound; clicks in the Games window follow its own volume." });
        row.Dialog.Close();
    });
}
