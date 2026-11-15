using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Hatchling's bar quarters against the stats panel's own (the plan's section 2.3):
/// the Run page's health, food and water bars take the theme's colour for the
/// quarter a value is in, by the lines 75, 50 and 25, "exactly as the stats panel's
/// bars over the game do". The game cannot reference the app (A2), so it mirrors
/// <see cref="BarStates.Of"/> in <c>Quarters.Of</c>, and this test, which sees both
/// sides, holds the two equal: at each line, a hair either side of it, and over the
/// whole 0 to 1 range. And the twelve fill keys the game writes out are tokens every
/// theme holds, named as the app names a state.
/// </summary>
public sealed class BarQuartersInStepTests
{
    private static readonly double[] Lines = { 0.25, 0.50, 0.75 };

    [Fact]
    public void TheFourQuartersAreTheAppsFourStatesByName()
    {
        // The same names in the same order, so a quarter and a state agree on every value
        // they could be compared by (the code asks by name and by order).
        Assert.Equal(Enum.GetNames<BarState>(), Enum.GetNames<Quarter>());
        Assert.Equal(Enum.GetValues<BarState>().Select(s => (int)s), Enum.GetValues<Quarter>().Select(q => (int)q));
    }

    [Fact]
    public void TheQuarterOfAValueIsTheStateOfItAtAndRoundEveryLine()
    {
        var values = new List<double> { 0, 1, -0.5, 1.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
        foreach (var line in Lines)
        {
            values.Add(line);
            values.Add(Math.BitDecrement(line));
            values.Add(Math.BitIncrement(line));
            values.Add(line - 0.001);
            values.Add(line + 0.001);
        }

        foreach (var value in values)
            Assert.True(StateName(value) == QuarterName(value), $"{value}: the app says {StateName(value)}, the game {QuarterName(value)}");
    }

    [Fact]
    public void AValueOnALineBelongsToTheQuarterAboveIt()
    {
        // The rule both sides state in words; held on the game's side as well as by the equality above.
        Assert.Equal(Quarter.Above75, Quarters.Of(0.75));
        Assert.Equal(Quarter.From50To75, Quarters.Of(0.50));
        Assert.Equal(Quarter.From25To50, Quarters.Of(0.25));
        Assert.Equal(Quarter.Below25, Quarters.Of(0));
    }

    [Fact]
    public void TheQuarterOfEveryThousandthIsTheStateOfIt()
    {
        for (var i = 0; i <= 1000; i++)
        {
            var value = i / 1000.0;
            Assert.True(StateName(value) == QuarterName(value), $"{value}: the app says {StateName(value)}, the game {QuarterName(value)}");
        }
    }

    [Fact]
    public void EveryFillKeyTheGameWritesIsATokenNamedForItsBarAndState()
    {
        // The game writes each key whole (the app's key scan reads literals, not pieces); each is the
        // theme's own "Theme.Bars.<bar>.<state>.Fill", the stats panel's Hunger and Thirst for food and water.
        foreach (var quarter in Enum.GetValues<Quarter>())
        {
            Assert.Equal($"Theme.Bars.Health.{quarter}.Fill", Quarters.HealthKey(quarter));
            Assert.Equal($"Theme.Bars.Hunger.{quarter}.Fill", Quarters.FoodKey(quarter));
            Assert.Equal($"Theme.Bars.Thirst.{quarter}.Fill", Quarters.WaterKey(quarter));
            foreach (var key in new[] { Quarters.HealthKey(quarter), Quarters.FoodKey(quarter), Quarters.WaterKey(quarter) })
                Assert.Contains(key, Theme.Keys);
        }
    }

    private static string StateName(double value) => BarStates.Of(value).ToString();

    private static string QuarterName(double value) => Quarters.Of(value).ToString();
}
