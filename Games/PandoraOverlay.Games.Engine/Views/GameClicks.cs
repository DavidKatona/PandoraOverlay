using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PandoraOverlay.Games.Engine;

/// <summary>
/// What a game's button says about its click (A105), when its type does not say
/// it: the value of <see cref="GameClicks.Cue"/>. <see cref="Auto"/>, the default,
/// leaves it to the button's type (<see cref="GameClicks.ByType"/>).
/// <para>
/// No "arm": a click arms by what the game's handler did, not by which button it
/// was (the same button asks on its first click and acts on its second), so a
/// view says it in the handler's arming branch (<c>GameViewBase.Arming</c>, A104,
/// A105). Public, as everything a game's XAML places must be (A67: an internal
/// type there makes WPF compile a helper that reflects by name).
/// </para>
/// </summary>
public enum GameClickMark
{
    /// <summary>By the button's type: a check on or off, a radio or a tab a select, any other button a press (A100).</summary>
    Auto,

    /// <summary>No click: the button has a sound of its own, or must be silent.</summary>
    None,

    /// <summary>A press, whatever the type.</summary>
    Press,

    /// <summary>A select, whatever the type.</summary>
    Select,

    /// <summary>On or off by the button's <c>IsChecked</c> after the click (a button that is not a toggle reads as off).</summary>
    Toggle,

    /// <summary>A way back or out.</summary>
    Back,
}

/// <summary>
/// The attached mark of a game's button for the engine's click rule (A105,
/// <c>PLAN-CLICKS.md</c> 6.2): <c>engine:GameClicks.Cue="Back"</c> in a view's
/// XAML, or <see cref="SetCue"/> in its code. Read only on the button whose click
/// a left release noted, never inherited: a mark says what ONE button means
/// ("back", or "nothing, it sounds by itself"), and a panel's mark reaching every
/// button in it would be a mistake waiting to happen.
/// <para>
/// In the engine because the rule is (A21: every game keeps it, and it touches
/// WPF), and in the games' own namespace, which a game's XAML may map (A42).
/// The overlay's windows have a mark of their own, by the same names (the app's
/// click layer); the two never meet, since a game names no app type (A2).
/// </para>
/// </summary>
public static class GameClicks
{
    /// <summary>The mark: <see cref="GameClickMark.Auto"/> unless a game sets it; not inherited.</summary>
    public static readonly DependencyProperty CueProperty = DependencyProperty.RegisterAttached(
        "Cue", typeof(GameClickMark), typeof(GameClicks), new PropertyMetadata(GameClickMark.Auto));

    /// <summary>The button's mark.</summary>
    public static GameClickMark GetCue(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (GameClickMark)element.GetValue(CueProperty);
    }

    /// <summary>Marks a button (a game's code; its XAML writes the attribute).</summary>
    public static void SetCue(DependencyObject element, GameClickMark value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CueProperty, value);
    }

    /// <summary>
    /// The click a button's release gives by its mark, then by its type, as the
    /// overlay's windows give it (A100, <c>PLAN-CLICKS.md</c> 1.3): null for
    /// <see cref="GameClickMark.None"/>; a mark's own kind; else a CheckBox
    /// <see cref="GameClick.ToggleOn"/> when it is ticked now (the click toggled it
    /// before it was raised, and the game's handler may have put it back as its
    /// state says: the sound follows what the box shows), else
    /// <see cref="GameClick.ToggleOff"/> (a mixed box reads as off); a RadioButton
    /// and every other ToggleButton (a tab, a section of a list, a menu opener)
    /// <see cref="GameClick.Select"/>; any other button <see cref="GameClick.Press"/>.
    /// </summary>
    internal static GameClick? ByType(ButtonBase button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return GetCue(button) switch
        {
            GameClickMark.None => null,
            GameClickMark.Press => GameClick.Press,
            GameClickMark.Select => GameClick.Select,
            GameClickMark.Back => GameClick.Back,
            GameClickMark.Toggle => Ticked(button) ? GameClick.ToggleOn : GameClick.ToggleOff,
            _ => button switch
            {
                CheckBox box => Ticked(box) ? GameClick.ToggleOn : GameClick.ToggleOff,
                ToggleButton => GameClick.Select,   // RadioButton is one
                _ => GameClick.Press,
            },
        };
    }

    private static bool Ticked(ButtonBase button) => button is ToggleButton { IsChecked: true };
}
