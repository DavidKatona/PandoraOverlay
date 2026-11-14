using System.Windows;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// The picture on Hatchling's tile in the Games window's grid: a strip of the board
/// with the player's species as a hatchling, gold-ringed, and a Troodon hunting it.
/// Painted once, by the board's own painter, so it is the board in small and falls
/// back to the same stand-ins while art is missing (H30); data art, the same in every
/// theme (G21), with no word in it (A18).
///
/// <para>Twelve cells in two rows of six, in a fixed pattern of ground; the chosen
/// species at its smallest size on the second cell with its ring; a Troodon on the
/// fifth, facing it, with the hostile ring and the hunting mark. The strip is wider
/// than the tile, so it is centred and both ends are cut by the tile's edge: the
/// two creatures sit well inside it.</para>
///
/// <para>The surface that shows the picture (the engine's <see cref="PixelSurface"/>,
/// 232 x 96, the Contract's tile art size) is the one WPF piece of the board's code:
/// <see cref="Create"/> makes it, <see cref="Paint"/>s into its buffer and presents it
/// once. Once is enough: the surface presents itself again from its buffer when its
/// scale on screen changes. Water under the Troodon is on purpose: a hunter's red
/// ring on the tile's one blue cell is what the eye finds first.</para>
/// </summary>
internal static class TileArt
{
    /// <summary>The tile picture's size, in logical pixels (the surface's sides are multiples of 4).</summary>
    public const int Width = 232, Height = 96;

    /// <summary>The strip's cells: two rows of six.</summary>
    private static readonly HexGrid Grid = new(6, 2);

    /// <summary>The strip's ground, cell by cell, row by row.</summary>
    private static readonly Terrain[] Pattern =
    {
        Terrain.Forest, Terrain.Forest, Terrain.Grass, Terrain.Grass, Terrain.Water, Terrain.Grass,
        Terrain.Grass, Terrain.Forest, Terrain.Grass, Terrain.Grass, Terrain.Grass, Terrain.Water,
    };

    /// <summary>Your cell: the second.</summary>
    private const int YourCell = 1;

    /// <summary>The Troodon's cell: the fifth.</summary>
    private const int HunterCell = 4;

    /// <summary>The strip in the tile's pixels: the board's cell shape, centred both ways.</summary>
    internal static readonly HexLayout Hexes = Centred();

    /// <summary>
    /// The tile's picture for <paramref name="species"/>, on a surface of its own:
    /// painted and presented once, as the game's tile art. On the UI thread.
    /// </summary>
    public static FrameworkElement Create(ArtBook art, string species)
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(species);
        var surface = new PixelSurface(Width, Height);
        Paint(surface.Buffer, art, species);
        surface.Present();
        return surface;
    }

    /// <summary>
    /// The tile's picture for <paramref name="species"/> into <paramref name="buffer"/>
    /// (232 x 96; a buffer of another size is drawn the same, clipped). Pure, and the
    /// same pixels every time for the same art.
    /// </summary>
    public static void Paint(PixelBuffer buffer, ArtBook art, string species)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(species);
        BoardPainter.Paint(buffer, SceneFor(species), art, Hexes);
    }

    /// <summary>The strip as a scene: every cell seen and in sight, no food, the two creatures at rest.</summary>
    internal static Scene SceneFor(string species)
    {
        var cells = Grid.Count;
        var scene = new Scene
        {
            Tiles = (Terrain[])Pattern.Clone(),
            Seen = Enumerable.Repeat(true, cells).ToArray(),
            InSight = Enumerable.Repeat(true, cells).ToArray(),
            Plants = new bool[cells],
            Meat = new int[cells],
            FoodShown = new bool[cells],
        };
        scene.Sprites.Add(At(new SceneSprite
        {
            Kind = SpriteKind.You,
            Subject = ArtBook.StageSubject(species, Stages.Hatchling),
            Ring = Ring.You,
        }, YourCell));
        scene.Sprites.Add(At(new SceneSprite
        {
            Creature = 1,
            Kind = SpriteKind.Predator,
            Subject = ArtNames.PredatorOf(PredatorType.Troodon),
            Flip = true,   // it faces you: you are to its left
            Ring = Ring.Hostile,
            Marks = Marks.Hunt,
            Ordinal = 1,   // the only one: no digit
        }, HunterCell));
        scene.Sort();
        return scene;
    }

    private static SceneSprite At(SceneSprite sprite, int cell)
    {
        var (x, y) = Hexes.Centre(cell);
        sprite.X = x;
        sprite.Y = y + BoardLayout.FeetBelowCentre;
        sprite.Row = Grid.Row(cell);
        return sprite;
    }

    private static HexLayout Centred()
    {
        var size = new HexLayout(Grid, BoardLayout.CellWidth, BoardLayout.CellHeight, BoardLayout.RowPitch);
        return new HexLayout(Grid, BoardLayout.CellWidth, BoardLayout.CellHeight, BoardLayout.RowPitch,
            (Width - size.Width) / 2, (Height - size.Height) / 2);
    }
}
