using System.Windows;

namespace PandoraOverlay.Games;

/// <summary>
/// A game on screen. Disposed when the window closes or goes back to the
/// grid: every timer stops, nothing is lost.
/// <para>
/// The pause belongs to the view, not to the game (A4): a game with no view
/// on screen has nothing running. Nothing is lost on a close because the
/// game's state is whole at every moment: what the view shows moving is a
/// cosmetic playback of a state already committed and saved (A20), so the
/// next view paints the final state. A view is also disposed when its game
/// faults (A10).
/// </para>
/// </summary>
public interface IGameView : IDisposable
{
    /// <summary>
    /// Laid out in <see cref="GameFrame.ViewWidth"/> x <see cref="GameFrame.ViewHeight"/>,
    /// never asks for more: the window never changes size (G13).
    /// </summary>
    FrameworkElement Root { get; }

    /// <summary>
    /// The window covered it (the health warning, G18): stop playback, take
    /// no input.
    /// </summary>
    void Pause();

    /// <summary>The warning was acknowledged: carry on where it was.</summary>
    void Resume();

    /// <summary>
    /// Every state worth laying out, by name ("camp", "run", "run-over" ...),
    /// with sample content: for the layout tests in every theme and for
    /// renders. Never called by the window.
    /// <para>
    /// A test hook in a production interface, on purpose (A26): the fit-factor
    /// walk and the layout tests must reach every screen of every game in
    /// every theme, and an explicit hook is what the golden-render harness
    /// lacked (it reaches private names by reflection).
    /// </para>
    /// </summary>
    IReadOnlyList<string> LayoutStates { get; }

    /// <summary>Shows one of <see cref="LayoutStates"/>, with its sample content.</summary>
    void ShowLayoutState(string state);
}
