namespace PandoraOverlay.Games.Engine;

/// <summary>
/// One picture on a sheet (A28): the rectangle it occupies there, and its
/// pivot, the pixel of the frame (counted from the frame's top-left) that
/// lands on the point a game draws it at. For a dino that is the middle of its
/// feet on the ground line, so a game places a creature by where it stands,
/// whatever the picture's size, and a flipped one keeps its feet on the same
/// pixel (<see cref="PixelBuffer.Draw(Sprite, int, int, bool, byte)"/>).
///
/// <para>Sprites face right; the engine flips. The sheet is shared by every
/// sprite of its atlas and by every view that draws one: it is only ever drawn
/// FROM. A sprite usually comes from an <see cref="Atlas"/>, which checks its
/// frame and pivot at load; one built by hand is checked where it is drawn
/// (a frame off its sheet throws, a build defect).</para>
/// </summary>
public sealed record Sprite(string Name, PixelBuffer Sheet, PixelRect Frame, int PivotX, int PivotY);
