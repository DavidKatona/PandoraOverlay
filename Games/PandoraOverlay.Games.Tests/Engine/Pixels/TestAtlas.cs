using System.IO;
using PandoraOverlay.Games.Engine;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The engine's own test atlas, <c>Fixtures/test-atlas.png</c> and
/// <c>.json</c>, embedded in this test assembly as <c>Art/test-atlas.png</c>
/// and <c>.json</c> exactly as a game embeds its art (A28), so
/// <see cref="Atlas.Load(System.Reflection.Assembly, string)"/> is tested the
/// way a game calls it. A few solid-colour frames: a 36 x 25 block (a
/// creature's size, pivot on its feet), a half-transparent glass, and two
/// small marks that two animations run over, one looping, one held. Other
/// engine tests may draw with it (a board-like composition, a cost
/// measurement): it is small, and its pixels are known.
///
/// <para>The PNG is not hand-made: <see cref="DrawSheet"/> draws it, and
/// <c>AtlasTests.TheFixtureIsWhatItsCodeDraws</c> holds the checked-in file
/// to that drawing (recorded with <c>GAMES_RECORD_FRAMES=1</c>, like a golden
/// frame). The JSON is written by hand and checked in beside it.</para>
/// </summary>
internal static class TestAtlas
{
    /// <summary>Its name: the embedded resources are <c>Art/test-atlas.png</c> and <c>Art/test-atlas.json</c>.</summary>
    public const string Name = "test-atlas";

    /// <summary>The sheet's size: the frames take 44 x 25, the rest is transparent.</summary>
    public const int Width = 48, Height = 25;

    // The frames' colours, premultiplied, worked out by hand from the straight
    // bytes the PNG holds (each channel times alpha / 255, to the nearest).

    /// <summary><c>block/stand/0</c>: straight (200, 60, 40), opaque.</summary>
    public const uint Block = 0xFFC83C28;

    /// <summary><c>glass/stand/0</c>: straight (40, 120, 255) at alpha 128: (20, 60, 128).</summary>
    public const uint Glass = 0x80143C80;

    /// <summary><c>mark/walk/0</c>: straight (255, 255, 0), opaque.</summary>
    public const uint MarkA = 0xFFFFFF00;

    /// <summary><c>mark/walk/1</c>: straight (0, 255, 255), opaque.</summary>
    public const uint MarkB = 0xFF00FFFF;

    /// <summary>The atlas, as a game loads its own.</summary>
    public static Atlas Load() => Atlas.Load(typeof(TestAtlas).Assembly, Name);

    /// <summary>One of the two embedded files, as a stream of its own (the caller disposes it).</summary>
    public static Stream Resource(string extension) =>
        typeof(TestAtlas).Assembly.GetManifestResourceStream($"Art/{Name}.{extension}")
        ?? throw new InvalidOperationException($"Art/{Name}.{extension} is not embedded in the test assembly.");

    /// <summary>The frame table's text, for tests that load a variant of it.</summary>
    public static string Json()
    {
        using var reader = new StreamReader(Resource("json"));
        return reader.ReadToEnd();
    }

    /// <summary>The sheet the PNG holds: each frame's rectangle filled with its colour on a transparent ground.</summary>
    public static PixelBuffer DrawSheet()
    {
        var sheet = new PixelBuffer(Width, Height);
        sheet.Fill(0, 0, 36, 25, Block);
        sheet.Fill(36, 0, 8, 8, Glass);
        sheet.Fill(36, 8, 4, 4, MarkA);
        sheet.Fill(40, 8, 4, 4, MarkB);
        return sheet;
    }
}
