namespace PandoraOverlay.Games.Engine;

/// <summary>
/// Which mouse button a <see cref="PixelSurface"/> pointer event is about:
/// <see cref="None"/> for a move, <see cref="Left"/> or <see cref="Right"/>
/// for a press. The games are mouse only (A33): no other button is reported.
/// </summary>
public enum PointerButton
{
    /// <summary>A move: no button is being reported.</summary>
    None,

    /// <summary>The left button went down.</summary>
    Left,

    /// <summary>The right button went down.</summary>
    Right,
}

/// <summary>
/// The pointer on a playfield, in logical pixels: (<paramref name="X"/>,
/// <paramref name="Y"/>) is the pixel of the surface's
/// <see cref="PixelSurface.Buffer"/> under the pointer, whatever the display
/// scale, so a game's hit test is the same arithmetic as its drawing (a
/// <c>HexLayout.CellAt(x, y)</c>).
/// </summary>
/// <param name="X">The pixel's column, 0 to the buffer's width - 1.</param>
/// <param name="Y">The pixel's row, 0 to the buffer's height - 1.</param>
/// <param name="Button">The button pressed, or <see cref="PointerButton.None"/> for a move.</param>
public readonly record struct PixelPointer(int X, int Y, PointerButton Button);
