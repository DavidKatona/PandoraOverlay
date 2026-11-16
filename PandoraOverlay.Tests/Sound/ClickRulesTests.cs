using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The click layer's pure rules (<c>PLAN-CLICKS.md</c> 1.3 and 2; A99, A100,
/// A103): which cue a control's click plays, by its kind, its new state and
/// its mark, every combination; the gap; which scope plays at which step.
/// The handler that applies them is <see cref="UiClicksTests"/>', the gate
/// that plays them <see cref="ClickSoundTests"/>'.
/// </summary>
[Collection(TheThemeOnScreen.Name)] // one test builds controls: never beside a theme on screen
public sealed class ClickRulesTests
{
    private static readonly UiClicks.ClickControl[] Controls = Enum.GetValues<UiClicks.ClickControl>();
    private static readonly ClickMark[] Marks = Enum.GetValues<ClickMark>();
    private static readonly bool?[] States = { true, false, null };

    // ---- Section 1.3's table -------------------------------------------------------------------

    /// <summary>The table, row by row, with no mark: by type.</summary>
    [Theory]
    [InlineData(nameof(UiClicks.ClickControl.Button), null, ClickCue.Press)]
    [InlineData(nameof(UiClicks.ClickControl.CheckBox), true, ClickCue.ToggleOn)]   // the click ticked it
    [InlineData(nameof(UiClicks.ClickControl.CheckBox), false, ClickCue.ToggleOff)] // the click cleared it
    [InlineData(nameof(UiClicks.ClickControl.CheckBox), null, ClickCue.ToggleOff)]  // a mixed box read as off
    [InlineData(nameof(UiClicks.ClickControl.RadioButton), true, ClickCue.Select)]
    [InlineData(nameof(UiClicks.ClickControl.ToggleButton), true, ClickCue.Select)] // a nav, a tab, a menu opener
    [InlineData(nameof(UiClicks.ClickControl.ToggleButton), false, ClickCue.Select)]
    [InlineData(nameof(UiClicks.ClickControl.Slider), null, ClickCue.Select)]       // a release that moved it
    public void ByTypeTheTablesCue(string control, bool? isChecked, ClickCue cue) =>
        Assert.Equal(cue, UiClicks.CueOf(Enum.Parse<UiClicks.ClickControl>(control), isChecked, ClickMark.Auto, armed: false));

    /// <summary>Every kind, every state, every mark, armed or not: a RepeatButton and the None mark never sound; Press, Select and Back are their own; Toggle goes by the state; Arm by the arming call; Auto by type.</summary>
    [Fact]
    public void EveryKindStateAndMark()
    {
        var checkedAt = 0;
        foreach (var control in Controls)
        foreach (var state in States)
        foreach (var mark in Marks)
        foreach (var armed in new[] { false, true })
        {
            var cue = UiClicks.CueOf(control, state, mark, armed);
            var where = $"{control}, {state?.ToString() ?? "null"}, {mark}, armed {armed}";
            checkedAt++;
            if (control == UiClicks.ClickControl.RepeatButton || mark == ClickMark.None)
            {
                Assert.True(cue is null, where);
                continue;
            }
            // A slider has nothing to toggle and nothing to arm: those marks read as Auto on it.
            var read = control == UiClicks.ClickControl.Slider && mark is ClickMark.Toggle or ClickMark.Arm ? ClickMark.Auto : mark;
            ClickCue expected = read switch
            {
                ClickMark.Press => ClickCue.Press,
                ClickMark.Select => ClickCue.Select,
                ClickMark.Back => ClickCue.Back,
                ClickMark.Toggle => state == true ? ClickCue.ToggleOn : ClickCue.ToggleOff,
                ClickMark.Arm => armed ? ClickCue.Arm : ClickCue.Press,
                _ => UiClicks.CueOf(control, state, ClickMark.Auto, armed: false)!.Value,
            };
            Assert.True(cue == expected, $"{where}: {cue}, not {expected}");
            if (read == ClickMark.Auto) Assert.Equal(UiClicks.CueOf(control, state, ClickMark.Auto, !armed), cue); // Auto never reads the arming call
        }
        Assert.Equal(Controls.Length * States.Length * Marks.Length * 2, checkedAt);
    }

    /// <summary>The armed pair (A100): the first click asks, the second does it.</summary>
    [Fact]
    public void AnArmedButtonsFirstClickIsArmAndItsSecondPress()
    {
        Assert.Equal(ClickCue.Arm, UiClicks.CueOf(UiClicks.ClickControl.Button, null, ClickMark.Arm, armed: true));
        Assert.Equal(ClickCue.Press, UiClicks.CueOf(UiClicks.ClickControl.Button, null, ClickMark.Arm, armed: false));
    }

    /// <summary>A control's kind is its most derived one: a CheckBox and a RadioButton are ToggleButtons, a RepeatButton is a ButtonBase.</summary>
    [Fact]
    public void AControlsKindIsItsMostDerived() => TheThemeOnScreen.OnSta(() =>
    {
        Assert.Equal(UiClicks.ClickControl.Button, UiClicks.KindOf(new Button()));
        Assert.Equal(UiClicks.ClickControl.CheckBox, UiClicks.KindOf(new CheckBox()));
        Assert.Equal(UiClicks.ClickControl.RadioButton, UiClicks.KindOf(new RadioButton()));
        Assert.Equal(UiClicks.ClickControl.ToggleButton, UiClicks.KindOf(new ToggleButton()));
        Assert.Equal(UiClicks.ClickControl.RepeatButton, UiClicks.KindOf(new RepeatButton()));
        Assert.Equal(UiClicks.ClickControl.Slider, UiClicks.KindOf(new Slider()));
        Assert.Equal(UiClicks.ClickControl.Button, UiClicks.KindOf(new GridViewColumnHeader())); // another ButtonBase: press
    });

    // ---- Section 2's gap (A99) ----------------------------------------------------------------------

    [Fact]
    public void TheGapIsEightyMillisecondsFromTheLastStart()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(80), ClickSound.MinGap);
        Assert.True(ClickSound.Spaced(null, TimeSpan.Zero)); // the first click
        var last = TimeSpan.FromSeconds(10);
        Assert.False(ClickSound.Spaced(last, last));
        Assert.False(ClickSound.Spaced(last, last + TimeSpan.FromMilliseconds(79)));
        Assert.False(ClickSound.Spaced(last, last + ClickSound.MinGap - TimeSpan.FromTicks(1)));
        Assert.True(ClickSound.Spaced(last, last + ClickSound.MinGap));
        Assert.True(ClickSound.Spaced(last, last + TimeSpan.FromMilliseconds(150))); // a deliberate double click: two sounds
        Assert.False(ClickSound.Spaced(last, last - TimeSpan.FromMilliseconds(1)));  // a clock read out of order is no gap
    }

    // ---- The scopes and their steps (A103) -------------------------------------------------------

    [Theory]
    [InlineData(1, -24)]
    [InlineData(2, -18)]
    [InlineData(3, -12)]
    [InlineData(4, -6)]
    [InlineData(5, 0)]
    public void TheOverlayScopePlaysAtTheOverlaysStep(int step, double decibels)
    {
        var config = new OverlayConfig { ClickSoundVolume = step, GamesSoundVolume = 6 - step };

        Assert.Equal(SoundSteps.GainOf(step), ClickSound.GainOf(ClickScope.Overlay, config));
        Assert.Equal(decibels, 20 * Math.Log10(ClickSound.GainOf(ClickScope.Overlay, config)!.Value), 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void TheGamesScopePlaysAtTheGamesWindowsStepUnlessMuted(int step)
    {
        var config = new OverlayConfig { GamesSoundVolume = step, ClickSoundVolume = 6 - step };

        Assert.Equal(SoundSteps.GainOf(step), ClickSound.GainOf(ClickScope.Games, config));
        config.GamesSoundMuted = true;
        Assert.Null(ClickSound.GainOf(ClickScope.Games, config));
        Assert.Equal(SoundSteps.GainOf(6 - step), ClickSound.GainOf(ClickScope.Overlay, config)); // the Games mute is the window's alone
    }

    [Fact]
    public void ClicksOffSilenceEveryScopeAndTheGameScopeIsNeverTheGates()
    {
        var off = new OverlayConfig { ClickSoundEnabled = false };
        foreach (var scope in Enum.GetValues<ClickScope>()) Assert.Null(ClickSound.GainOf(scope, off));

        Assert.Null(ClickSound.GainOf(ClickScope.Game, new OverlayConfig())); // the Engine's (A105)
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(9, 5)]
    public void AStepOutOfRangeIsReadAsTheNearestAndNotWritten(int stored, int read)
    {
        var config = new OverlayConfig { ClickSoundVolume = stored, GamesSoundVolume = stored };

        Assert.Equal(SoundSteps.GainOf(read), ClickSound.GainOf(ClickScope.Overlay, config));
        Assert.Equal(SoundSteps.GainOf(read), ClickSound.GainOf(ClickScope.Games, config));
        Assert.Equal((stored, stored), (config.ClickSoundVolume, config.GamesSoundVolume));
    }

    // ---- The steps' one home (A103) ---------------------------------------------------------------

    [Fact]
    public void TheGamesSoundsStepsAreTheSoundSteps()
    {
        Assert.Equal((SoundSteps.Count, SoundSteps.Default, SoundSteps.DecibelsPerStep), (GamesSound.Steps, GamesSound.DefaultStep, GamesSound.DecibelsPerStep));
        for (var step = -1; step <= 7; step++)
        {
            Assert.Equal(SoundSteps.StepOf(step), GamesSound.StepOf(step));
            Assert.Equal(SoundSteps.DecibelsOf(step), GamesSound.DecibelsOf(step));
            Assert.Equal(SoundSteps.GainOf(step), GamesSound.GainOf(step));
        }
        Assert.Equal((5, 3, 6.0), (SoundSteps.Count, SoundSteps.Default, SoundSteps.DecibelsPerStep));
    }
}
