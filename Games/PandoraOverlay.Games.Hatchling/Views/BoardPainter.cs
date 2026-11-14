using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Hatchling;

/// <summary>
/// Draws a <see cref="Scene"/> into a <see cref="PixelBuffer"/>: the board, as pixels
/// of our own (A17), so every frame of it is a test with no window. Pure: the same
/// scene and the same art give the same pixels, and nothing here keeps time (a
/// sprite's tick is handed in) or knows the rules (the scene is already only what
/// the player may know, H29, H39).
///
/// <para><b>The nine layers</b>, in this order: the ground; each cell's tile (fog
/// for a cell never seen, the dim veil over one seen but out of sight); the food
/// shown; on your turn the move, attack and hover marks; the rings under the feet;
/// the creatures; the marks and numbers above the predators; the marks above you;
/// the rising numbers. The playfield holds pictures and the art's digits, never a
/// word (A18): every word is the chrome's.</para>
///
/// <para><b>Missing art (H30).</b> Every piece is asked of the <see cref="ArtBook"/>;
/// one it lacks is drawn as a plain shape in <see cref="HatchlingPalette"/>'s colours
/// by one small function named after the piece it stands for: a tile is its cell's
/// outline filled, a cell's mark the outline's 2-pixel rim, a ring a 28 x 10 ellipse
/// outline, a plant or a carcass a 7 x 7 diamond, small game and a mark above a head
/// a 5 x 5 square. A number, an ordinal and a creature with no art draw nothing (a
/// creature's ring and marks still show where it stands). So the game is playable,
/// and its golden frames are painted, with no art at all.</para>
///
/// <para><b>Cost.</b> The view paints up to 30 times a second: nothing here
/// allocates (the masks are spans worked out once in <see cref="BoardLayout"/>, the
/// art's lookups read tables made at load, the scene's lists are walked by index).
/// A test holds that.</para>
/// </summary>
internal static class BoardPainter
{
    /// <summary>The board, on the island's layout (<see cref="BoardLayout.Hexes"/>).</summary>
    public static void Paint(PixelBuffer buffer, Scene scene, ArtBook art) =>
        Paint(buffer, scene, art, BoardLayout.Hexes);

    /// <summary>
    /// A scene on any layout of the same cell shape (44 x 50, rows 38 apart): the
    /// board's, or the tile's twelve cells (<see cref="TileArt"/>). The scene's arrays
    /// hold one entry a cell of the layout's grid.
    /// </summary>
    internal static void Paint(PixelBuffer buffer, Scene scene, ArtBook art, HexLayout hexes)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(hexes);
        var cells = scene.Tiles.Length;
        if (scene.Seen.Length != cells || scene.InSight.Length != cells || scene.Plants.Length != cells || scene.Meat.Length != cells)
            throw new ArgumentException("A scene holds one tile, seen, in-sight, plant and carcass entry for every cell.", nameof(scene));

        // 1. The ground.
        buffer.Clear(HatchlingPalette.Ground);

        // 2. The tiles, fogged or dimmed.
        for (var cell = 0; cell < cells; cell++)
        {
            var (left, top) = hexes.TopLeft(cell);
            if (!scene.Seen[cell])
            {
                var fog = art.Frame(ArtNames.Fog, ArtNames.Unseen, 0);
                if (fog is not null) buffer.Draw(fog, left, top);
                else UnseenTile(buffer, left, top);
                continue;
            }
            var ground = scene.Tiles[cell];
            var subject = ArtNames.TerrainOf(ground);
            var variants = art.Variants(subject, ArtNames.Tile);
            var tile = variants > 0 ? art.Frame(subject, ArtNames.Tile, cell % variants) : null;
            if (tile is not null) buffer.Draw(tile, left, top);
            else TerrainTile(buffer, ground, left, top);
            if (scene.InSight[cell]) continue;
            var dim = art.Frame(ArtNames.Fog, ArtNames.Dim, 0);
            if (dim is not null) buffer.Draw(dim, left, top);
            else DimVeil(buffer, left, top);
        }

        // 3. The food shown (H29, H39): the scene holds only what Sight.ShowsFood answers.
        for (var cell = 0; cell < cells; cell++)
        {
            if (!scene.Plants[cell] && scene.Meat[cell] <= 0) continue;
            var (x, y) = hexes.Centre(cell);
            y += BoardLayout.ItemBelowCentre;
            if (scene.Plants[cell])
            {
                var plant = art.Frame(ArtNames.Plant, ArtNames.Stand, 0);
                if (plant is not null) buffer.Draw(plant, x, y);
                else PlantItem(buffer, x, y);
            }
            if (scene.Meat[cell] > 0)
            {
                var pile = art.Frame(ArtNames.Meat, ArtNames.Pile, scene.Meat[cell] > 1 ? 1 : 0) ?? art.Frame(ArtNames.Meat, ArtNames.Pile, 0);
                if (pile is not null) buffer.Draw(pile, x, y);
                else CarcassItem(buffer, x, y);
            }
        }

        // 4. Your choices: where you can move, what you can attack, the cell under the mouse.
        for (var i = 0; i < scene.MoveCells.Count; i++)
            CellMark(buffer, art, hexes, scene.MoveCells[i], ArtNames.Move, scene.AmbientFrame, HatchlingPalette.MoveRim);
        for (var i = 0; i < scene.AttackCells.Count; i++)
            CellMark(buffer, art, hexes, scene.AttackCells[i], ArtNames.Attack, scene.AmbientFrame, HatchlingPalette.AttackRim);
        if (scene.HoverCell >= 0)
            CellMark(buffer, art, hexes, scene.HoverCell, ArtNames.Hover, scene.AmbientFrame, HatchlingPalette.HoverRim);

        // 5. The rings, under every creature's feet before any creature is drawn.
        for (var i = 0; i < scene.Sprites.Count; i++)
        {
            var sprite = scene.Sprites[i];
            if (sprite.Ring == Ring.None) continue;
            var ring = art.Frame(ArtNames.Ring, RingAction(sprite.Ring), scene.AmbientFrame) ?? art.Frame(ArtNames.Ring, RingAction(sprite.Ring), 0);
            if (ring is not null) buffer.Draw(ring, sprite.X, sprite.Y, opacity: sprite.Opacity);
            else RingOutline(buffer, sprite.X, sprite.Y, RingColour(sprite.Ring), sprite.Opacity);
        }

        // 6. The creatures, in the scene's order: top row first, small game under predators under you.
        for (var i = 0; i < scene.Sprites.Count; i++)
        {
            var sprite = scene.Sprites[i];
            var picture = art.Find(sprite.Subject, sprite.Action, sprite.Tick);
            if (picture is not null) buffer.Draw(picture, sprite.X, sprite.Y, sprite.Flip, sprite.Opacity);
            else if (sprite.Kind == SpriteKind.SmallGame) SmallGameSquare(buffer, sprite.X, sprite.Y, sprite.Opacity);
        }

        // 7. Above each predator: its alert, its afflictions beside it, and its number.
        for (var i = 0; i < scene.Sprites.Count; i++)
        {
            var sprite = scene.Sprites[i];
            if (sprite.Kind != SpriteKind.Predator) continue;
            var colour = sprite.Ring == Ring.None ? HatchlingPalette.HostileRing : RingColour(sprite.Ring);
            var x = sprite.X;
            var y = sprite.Y - BoardLayout.FeetBelowCentre - BoardLayout.MarkAboveCentre;
            var slot = 0;
            if ((sprite.Marks & Marks.Hunt) != 0) HeadMark(buffer, art, ArtNames.Hunt, 0, x, y, ref slot, colour, sprite.Opacity);
            else if ((sprite.Marks & Marks.Search) != 0) HeadMark(buffer, art, ArtNames.Search, 0, x, y, ref slot, colour, sprite.Opacity);
            if ((sprite.Marks & Marks.Dazed) != 0) HeadMark(buffer, art, ArtNames.Dazed, 0, x, y, ref slot, colour, sprite.Opacity);
            if ((sprite.Marks & Marks.Slow) != 0) HeadMark(buffer, art, ArtNames.Slow, 0, x, y, ref slot, colour, sprite.Opacity);
            if ((sprite.Marks & Marks.Bleed) != 0) HeadMark(buffer, art, ArtNames.Bleed, 0, x, y, ref slot, colour, sprite.Opacity);
            OrdinalDigits(buffer, art, sprite);
        }

        // 8. Above you: your turn, your hiding, your Elder.
        for (var i = 0; i < scene.Sprites.Count; i++)
        {
            var sprite = scene.Sprites[i];
            if (sprite.Kind != SpriteKind.You) continue;
            var colour = sprite.Ring == Ring.None ? HatchlingPalette.YouRing : RingColour(sprite.Ring);
            var x = sprite.X;
            var y = sprite.Y - BoardLayout.FeetBelowCentre - BoardLayout.MarkAboveCentre;
            var slot = 0;
            if (scene.ShowYourMark && (sprite.Marks & Marks.You) != 0) HeadMark(buffer, art, ArtNames.You, scene.AmbientFrame, x, y, ref slot, colour, sprite.Opacity);
            if ((sprite.Marks & Marks.Hidden) != 0) HeadMark(buffer, art, ArtNames.Hidden, 0, x, y, ref slot, colour, sprite.Opacity);
            if ((sprite.Marks & Marks.Elder) != 0) HeadMark(buffer, art, ArtNames.Elder, 0, x, y, ref slot, colour, sprite.Opacity);
        }

        // 9. The rising numbers.
        for (var i = 0; i < scene.Numbers.Count; i++)
            RisingNumber(buffer, art, scene.Numbers[i]);
    }

    // ---- The pieces, each with its stand-in (H30) --------------------------------------

    /// <summary>A cell's mark (move, attack, hover): the art's frame of the ambient beat, else its frame 0, else the rim.</summary>
    private static void CellMark(PixelBuffer buffer, ArtBook art, HexLayout hexes, int cell, string action, int ambient, uint colour)
    {
        var (left, top) = hexes.TopLeft(cell);
        var mark = art.Frame(ArtNames.Mark, action, ambient) ?? art.Frame(ArtNames.Mark, action, 0);
        if (mark is not null) buffer.Draw(mark, left, top);
        else CellRim(buffer, left, top, colour);
    }

    /// <summary>
    /// A mark above a head in the next slot (the first at the mark's point, each next
    /// <see cref="BoardLayout.MarkStep"/> to its right): the art's 9 x 9 mark on its
    /// pivot, else a 5 x 5 square in the ring's colour.
    /// </summary>
    private static void HeadMark(PixelBuffer buffer, ArtBook art, string action, int frame, int x, int y, ref int slot,
        uint colour, byte opacity)
    {
        var at = x + slot++ * BoardLayout.MarkStep;
        var mark = art.Frame(ArtNames.Mark, action, frame) ?? art.Frame(ArtNames.Mark, action, 0);
        if (mark is not null) buffer.Draw(mark, at, y, opacity: opacity);
        else MarkSquare(buffer, at, y, colour, opacity);
    }

    /// <summary>
    /// A predator's number (H38), from 2 to 99, in <c>ordinal/digit</c>s: the last digit's
    /// top-left at its cell's centre plus (14, 12), a first digit 6 to its left. 1 (the
    /// only one of its species so far) and anything above 99 draw nothing, and so does
    /// a number whose digit is not drawn: half a number would name another creature.
    /// The turn list always has the whole number.
    /// </summary>
    private static void OrdinalDigits(PixelBuffer buffer, ArtBook art, SceneSprite sprite)
    {
        var n = sprite.Ordinal;
        if (n < 2 || n > 99) return;
        var last = art.Frame(ArtNames.Ordinal, ArtNames.Digit, n % 10);
        var first = n >= 10 ? art.Frame(ArtNames.Ordinal, ArtNames.Digit, n / 10) : null;
        if (last is null || (n >= 10 && first is null)) return;
        var x = sprite.X + BoardLayout.OrdinalRight;
        var y = sprite.Y - BoardLayout.FeetBelowCentre + BoardLayout.OrdinalDown;
        buffer.Draw(last, x, y, opacity: sprite.Opacity);
        if (first is not null) buffer.Draw(first, x - BoardLayout.OrdinalDigitStep, y, opacity: sprite.Opacity);
    }

    /// <summary>
    /// A rising number: its sign, then its digits, <see cref="BoardLayout.NumberStep"/>
    /// apart, centred on its x with its top on its y. Drawn whole or not at all: with
    /// any of its glyphs missing it draws nothing (the banner carries the number).
    /// </summary>
    private static void RisingNumber(PixelBuffer buffer, ArtBook art, SceneNumber number)
    {
        var subject = number.Good ? ArtNames.NumberGood : ArtNames.NumberHurt;
        long value = Math.Abs((long)number.Value);
        long top = 1;
        var digits = 1;
        while (value / top >= 10) { top *= 10; digits++; }

        var sign = number.Sign ? art.Frame(subject, ArtNames.Sign, 0) : null;
        if (number.Sign && sign is null) return;
        for (var d = top; d > 0; d /= 10)
            if (art.Frame(subject, ArtNames.Digit, (int)(value / d % 10)) is null) return;

        var glyphs = digits + (sign is null ? 0 : 1);
        var x = number.X - (glyphs * BoardLayout.NumberStep - 1) / 2;
        if (sign is not null)
        {
            buffer.Draw(sign, x, number.Y, opacity: number.Opacity);
            x += BoardLayout.NumberStep;
        }
        for (var d = top; d > 0; d /= 10)
        {
            buffer.Draw(art.Frame(subject, ArtNames.Digit, (int)(value / d % 10))!, x, number.Y, opacity: number.Opacity);
            x += BoardLayout.NumberStep;
        }
    }

    /// <summary>The stand-in for a cell never seen: its outline in the unseen colour.</summary>
    private static void UnseenTile(PixelBuffer buffer, int left, int top) =>
        FillSpans(buffer, BoardLayout.CellSpans, left, top, HatchlingPalette.Unseen);

    /// <summary>The stand-in for a terrain's tile: its outline in the terrain's colour.</summary>
    private static void TerrainTile(PixelBuffer buffer, Terrain ground, int left, int top) =>
        FillSpans(buffer, BoardLayout.CellSpans, left, top, ground switch
        {
            Terrain.Grass => HatchlingPalette.Grass,
            Terrain.Forest => HatchlingPalette.Forest,
            Terrain.Water => HatchlingPalette.Water,
            _ => HatchlingPalette.Rock,
        });

    /// <summary>The stand-in for <c>fog/dim</c>: the outline in the ground's colour at alpha 184, over the tile.</summary>
    private static void DimVeil(PixelBuffer buffer, int left, int top) =>
        FillSpans(buffer, BoardLayout.CellSpans, left, top, HatchlingPalette.Dim);

    /// <summary>The stand-in for a cell's mark: the outline's 2-pixel rim in the mark's colour.</summary>
    private static void CellRim(PixelBuffer buffer, int left, int top, uint colour) =>
        FillSpans(buffer, BoardLayout.RimSpans, left, top, colour);

    /// <summary>The stand-in for a ring: a 28 x 10 ellipse outline centred on the feet.</summary>
    private static void RingOutline(PixelBuffer buffer, int x, int y, uint colour, byte opacity) =>
        FillSpans(buffer, BoardLayout.RingSpans, x - BoardLayout.RingWidth / 2, y - BoardLayout.RingHeight / 2,
            HatchlingPalette.Faded(colour, opacity));

    /// <summary>The stand-in for a plant: a 7 x 7 diamond centred on the item's point.</summary>
    private static void PlantItem(PixelBuffer buffer, int x, int y) =>
        FillSpans(buffer, BoardLayout.DiamondSpans, x - BoardLayout.DiamondSize / 2, y - BoardLayout.DiamondSize / 2, HatchlingPalette.Plant);

    /// <summary>The stand-in for a carcass: the plant's diamond in the carcass's colour.</summary>
    private static void CarcassItem(PixelBuffer buffer, int x, int y) =>
        FillSpans(buffer, BoardLayout.DiamondSpans, x - BoardLayout.DiamondSize / 2, y - BoardLayout.DiamondSize / 2, HatchlingPalette.Meat);

    /// <summary>The stand-in for small game: a 5 x 5 square standing on the feet (its bottom row on them), centred across.</summary>
    private static void SmallGameSquare(PixelBuffer buffer, int x, int y, byte opacity) =>
        buffer.Fill(x - BoardLayout.SquareSize / 2, y - (BoardLayout.SquareSize - 1), BoardLayout.SquareSize, BoardLayout.SquareSize,
            HatchlingPalette.Faded(HatchlingPalette.SmallGame, opacity));

    /// <summary>The stand-in for a mark above a head: a 5 x 5 square centred on the mark's point.</summary>
    private static void MarkSquare(PixelBuffer buffer, int x, int y, uint colour, byte opacity) =>
        buffer.Fill(x - BoardLayout.SquareSize / 2, y - BoardLayout.SquareSize / 2, BoardLayout.SquareSize, BoardLayout.SquareSize,
            HatchlingPalette.Faded(colour, opacity));

    private static void FillSpans(PixelBuffer buffer, BoardLayout.Span[] spans, int left, int top, uint colour)
    {
        foreach (var span in spans)
            buffer.Fill(left + span.X, top + span.Y, span.Length, 1, colour);
    }

    private static string RingAction(Ring ring) => ring switch
    {
        Ring.You => ArtNames.You,
        Ring.Hostile => ArtNames.Hostile,
        Ring.Apex => ArtNames.Apex,
        _ => ArtNames.Acting,
    };

    private static uint RingColour(Ring ring) => ring switch
    {
        Ring.You => HatchlingPalette.YouRing,
        Ring.Hostile => HatchlingPalette.HostileRing,
        Ring.Apex => HatchlingPalette.ApexRing,
        _ => HatchlingPalette.ActingRing,
    };
}
