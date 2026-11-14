using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The Contract's own few facts. Small on purpose: the architecture tests
/// (who references whom, the forbidden list) are a test file of their own.
/// </summary>
public class ContractTests
{
    /// <summary>Out of game a reading carries nothing: no dino, no gender, no growth (A5).</summary>
    [Fact]
    public void NotInGameCarriesNothing()
    {
        var reading = DinoReading.NotInGame;

        Assert.False(reading.InGame);
        Assert.Null(reading.Species);
        Assert.Null(reading.Gender);
        Assert.Equal(0, reading.Growth);
        Assert.Equal(default, reading); // the same as a reading nobody filled in
    }

    /// <summary>
    /// The view area is a real size, and the tile's art is a whole number of
    /// 4-pixel steps both ways, as a playfield surface must be, so a game can
    /// fill it with one exactly.
    /// </summary>
    [Fact]
    public void TheFrameSizesAreUsable()
    {
        Assert.True(GameFrame.ViewWidth > 0);
        Assert.True(GameFrame.ViewHeight > 0);
        Assert.True(GameFrame.TileArtWidth > 0);
        Assert.True(GameFrame.TileArtHeight > 0);
        Assert.Equal(0, GameFrame.TileArtWidth % 4);
        Assert.Equal(0, GameFrame.TileArtHeight % 4);
    }

    /// <summary>The Contract is one namespace, PandoraOverlay.Games (G20): nothing public of it lives elsewhere.</summary>
    [Fact]
    public void EveryPublicTypeIsInTheGamesNamespace()
    {
        var types = typeof(IGame).Assembly.GetExportedTypes();

        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.Equal("PandoraOverlay.Games", t.Namespace));
    }
}
