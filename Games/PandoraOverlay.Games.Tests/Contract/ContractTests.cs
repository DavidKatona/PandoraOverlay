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

    /// <summary>A game's sound is mono 16-bit at the first board's 44,100 a second, and at most 2 s (A78).</summary>
    [Fact]
    public void ASoundsFormatIsTheContracts()
    {
        Assert.Equal(44_100, GameSound.SampleRate);
        Assert.Equal(88_200, GameSound.MaxSamples);
        Assert.Equal(2 * GameSound.SampleRate, GameSound.MaxSamples);
    }

    /// <summary>
    /// The sound's door (A77): the host's one new member, after the log, and
    /// its two calls, a Play of the game's own sound and, since A105 amended
    /// A77's "one member", a Click of the host's own; and the types are the
    /// Contract's public ones, so the namespace test below holds them too.
    /// </summary>
    [Fact]
    public void TheHostHasASoundWithAPlayAndAClick()
    {
        Assert.Equal(typeof(IGameSound), typeof(IGameHost).GetProperty(nameof(IGameHost.Sound))!.PropertyType);
        var methods = typeof(IGameSound).GetMethods();
        Assert.Equal(2, methods.Length);
        var play = Assert.Single(methods, m => m.Name == nameof(IGameSound.Play));
        Assert.Equal((nameof(IGameSound.Play), typeof(void)), (play.Name, play.ReturnType));
        Assert.Equal(typeof(GameSound), Assert.Single(play.GetParameters()).ParameterType);
        var click = Assert.Single(methods, m => m.Name == nameof(IGameSound.Click));
        Assert.Equal(typeof(void), click.ReturnType);
        Assert.Equal(typeof(GameClick), Assert.Single(click.GetParameters()).ParameterType);

        var types = typeof(IGame).Assembly.GetExportedTypes();
        Assert.Contains(typeof(IGameSound), types);
        Assert.Contains(typeof(GameSound), types);
        Assert.Contains(typeof(GameClick), types);
    }

    /// <summary>
    /// A click's kinds are the host's six (A105, <c>PLAN-CLICKS.md</c> 3.3), the
    /// names the app's own cues have, in their order: a game names the kind, never
    /// a sound.
    /// </summary>
    [Fact]
    public void AClickIsOneOfTheHostsSix()
    {
        Assert.Equal(new[] { "Press", "Select", "ToggleOn", "ToggleOff", "Arm", "Back" }, Enum.GetNames<GameClick>());
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
