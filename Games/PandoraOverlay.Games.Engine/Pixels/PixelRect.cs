namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A rectangle of whole pixels: its top-left corner and its size. A sprite's
/// frame on its sheet, or the part of a buffer drawn onto another. A plain
/// value: the draw that takes one decides what a size of zero or a rectangle
/// off its sheet means (a build defect: it throws).
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
