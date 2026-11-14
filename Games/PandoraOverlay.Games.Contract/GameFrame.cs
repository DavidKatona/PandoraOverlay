namespace PandoraOverlay.Games;

/// <summary>
/// The sizes a game lays out in, in WPF units. The window's own frame (the
/// app's GamesFrame) is held equal by a test.
/// <para>
/// The Games window is one fixed frame in every theme and never changes size
/// (G13): 800 x 552 outside, a 1 px border and a 40 px header, so the view
/// area under the header is its width less 2 and its height less 2 and the
/// header. A game fits its screens into these numbers; it never asks for
/// more room, and nothing it shows resizes the window.
/// </para>
/// </summary>
public static class GameFrame
{
    /// <summary>The view area's width: <see cref="IGameView.Root"/> is laid out in exactly this.</summary>
    public const double ViewWidth = 798;

    /// <summary>The view area's height, under the window's header.</summary>
    public const double ViewHeight = 510;

    /// <summary>The width of <see cref="IGame.CreateTileArt"/>'s element on a tile. A multiple of 4, as a playfield surface's sizes are, so one can fill it exactly.</summary>
    public const double TileArtWidth = 232;

    /// <summary>The height of <see cref="IGame.CreateTileArt"/>'s element on a tile. A multiple of 4, as <see cref="TileArtWidth"/>.</summary>
    public const double TileArtHeight = 96;
}
