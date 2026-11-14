using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A seen step's two answers: the growth it counts and the stage it reached,
/// the highest line in (From, To]. A07.
/// </summary>
public class SeenStepTests
{
    private static SeenStep Step(double from, double to) => new("Alpha", "Male", from, to);

    [Fact]
    public void GrownIsTheRise()
    {
        Assert.Equal(0.002, Step(0.300, 0.302).Grown, 12);
    }

    [Theory]
    [InlineData(0.749, 0.751, 3)]
    [InlineData(0.9990, 0.9995, 4)]
    [InlineData(0.2499, 0.25, 1)]        // onto a line reaches it
    [InlineData(0.4999, 0.50, 2)]
    [InlineData(0.49, 0.76, 3)]          // over two lines: the higher
    [InlineData(0.10, 1.0, 4)]           // over all four
    public void AStepReachesTheHighestLineInsideIt(double from, double to, int stage)
    {
        Assert.Equal(stage, Step(from, to).StageReached);
    }

    [Theory]
    [InlineData(0.745, 0.7499)]          // ends just under a line
    [InlineData(0.75, 0.76)]             // starts exactly on one: already there
    [InlineData(0.9995, 1.0)]
    [InlineData(0.30, 0.31)]
    [InlineData(0.76, 0.74)]             // not a rise (only a hand-made step can be)
    public void AStepWithNoLineInsideItReachesNone(double from, double to)
    {
        Assert.Null(Step(from, to).StageReached);
    }
}
