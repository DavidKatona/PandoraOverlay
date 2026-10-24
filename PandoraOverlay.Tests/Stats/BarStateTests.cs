using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// The four states every bar is in (Stats/BarState.cs) and the theme's
/// colours for them. The lines are the code's; with Default the lookups give
/// exactly the colours the stats panel drew before 1.39 (Isle's are checked
/// in step 3, with its file).
/// </summary>
public class BarStateTests
{
    [Theory]
    [InlineData(1.0, BarState.Above75)]
    [InlineData(0.75, BarState.Above75)]      // on a line: the quarter above
    [InlineData(0.749, BarState.From50To75)]
    [InlineData(0.5, BarState.From50To75)]
    [InlineData(0.499, BarState.From25To50)]
    [InlineData(0.25, BarState.From25To50)]
    [InlineData(0.249, BarState.Below25)]
    [InlineData(0.0, BarState.Below25)]
    public void TheLinesSitAtTheirEdges(double value, BarState expected) =>
        Assert.Equal(expected, BarStates.Of(value));

    /// <summary>A NaN lands where the old "&lt; line" tests left it: no pulse, no wake.</summary>
    [Fact]
    public void ANaNIsAbove75AsTheOldTestsReadIt() =>
        Assert.Equal(BarState.Above75, BarStates.Of(double.NaN));

    [Fact]
    public void TheQuestionsMatchTheOldLines()
    {
        foreach (var v in new[] { 0.0, 0.1, 0.249, 0.25, 0.3, 0.499, 0.5, 0.6, 0.749, 0.75, 0.9, 1.0 })
        {
            var s = BarStates.Of(v);
            Assert.Equal(v < 0.25, s.UnderQuarter());
            Assert.Equal(v < 0.50, s.UnderHalf());
            Assert.Equal(v < 0.75, s.UnderThreeQuarters());
        }
    }

    // ---- Default's colours by state: the stats panel as it was --------------------

    private static ThemeTokens D => DefaultTheme.Tokens;
    private static ThemeColor C(string hex) => ThemeColor.Parse(hex);

    public static IEnumerable<object[]> EveryBarAndState() =>
        from kind in Enum.GetValues<BarKind>()
        from state in Enum.GetValues<BarState>()
        select new object[] { kind, state };

    /// <summary>Every bar in every state answers, with the percent's grey and the track every bar had.</summary>
    [Theory]
    [MemberData(nameof(EveryBarAndState))]
    public void EveryBarAndStateHasDefaultsLook(BarKind kind, BarState state)
    {
        var look = D.Bar(kind, state);
        Assert.Equal(C("#C7D1DA"), look.Percent);
        Assert.Equal(C("#3D202830"), D.BarOf(kind).Track);
        Assert.Equal(ThemeColor.None, D.BarOf(kind).TrackLine);
        Assert.Equal(C("#10151B"), D.BarOf(kind).OnFill); // the old TimeLeftOnFill: the panel's glass
    }

    /// <summary>Health: green, then amber under half, red under a quarter (the old HealthGood / HealthWarn / HealthCrit at 50% and 25%).</summary>
    [Theory]
    [InlineData(BarState.Above75, "#4CAF50")]
    [InlineData(BarState.From50To75, "#4CAF50")]
    [InlineData(BarState.From25To50, "#FFB300")]
    [InlineData(BarState.Below25, "#E53935")]
    public void HealthStepsAsItDid(BarState state, string fill) =>
        Assert.Equal(C(fill), D.Bar(BarKind.Health, state).Fill);

    /// <summary>The combat view's "Healthy" / "Wounded": quiet grey, amber, red.</summary>
    [Theory]
    [InlineData(BarState.Above75, "#9AA7B0")]
    [InlineData(BarState.From50To75, "#9AA7B0")]
    [InlineData(BarState.From25To50, "#FFB300")]
    [InlineData(BarState.Below25, "#E53935")]
    public void TheReadoutStepsAsItDid(BarState state, string colour) =>
        Assert.Equal(C(colour), D.Readout(state));

    /// <summary>The other bars keep one fill in every state, and their lightened tint for the time label.</summary>
    [Theory]
    [InlineData(BarKind.Stamina, "#FFD54F", "#FFE082")]
    [InlineData(BarKind.Hunger, "#FF9800", "#FFB74D")]
    [InlineData(BarKind.Thirst, "#4FC3F7", "#81D4FA")]
    [InlineData(BarKind.Damage, "#E53935", null)]
    public void TheOtherBarsKeepOneColour(BarKind kind, string fill, string? tint)
    {
        foreach (var state in Enum.GetValues<BarState>())
        {
            Assert.Equal(C(fill), D.Bar(kind, state).Fill);
            if (tint is not null) Assert.Equal(C(tint), D.Bar(kind, state).TimeLeft);
        }
    }
}
