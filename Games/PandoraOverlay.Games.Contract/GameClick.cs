namespace PandoraOverlay.Games;

/// <summary>
/// One of the host's own click sounds (G27, A105), the same six the overlay's
/// windows play: a game names the KIND of click and never a sound, and the
/// host plays its own for it (<see cref="IGameSound.Click"/>), or nothing.
/// <para>
/// The host's and not a game's, because a click means "your press
/// registered" in every window of the overlay, the Games window and a game
/// included: one gesture, one sound (A100), under the player's one switch for
/// clicks (Settings › General › Sounds, A103) and the Games window's mute and
/// volume. A game that embedded clicks of its own would sound unlike the
/// window around it and ignore that switch. The cues are
/// <c>PLAN-CLICKS.md</c> 1.3's table; a game reaches them only through the
/// engine's rule, which decides which kind a left click on its view is.
/// </para>
/// </summary>
public enum GameClick
{
    /// <summary>A button that acts; an armed button's confirming click; a press the game answered with nothing.</summary>
    Press,

    /// <summary>A tab, a radio, a menu opener, a card picked.</summary>
    Select,

    /// <summary>A check ticked.</summary>
    ToggleOn,

    /// <summary>A check cleared.</summary>
    ToggleOff,

    /// <summary>An armed button's first click: it did not do the thing, it asks (A104).</summary>
    Arm,

    /// <summary>A way back or out: "Back", a close.</summary>
    Back,
}
