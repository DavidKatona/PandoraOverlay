using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// "Only what the overlay saw" (A6): which growth <see cref="SeenGrowth"/>
/// counts, fed streams built the way the website sends them: growth with three
/// decimals (parsed to the nearest double, which <c>k / 1000.0</c> is), and a
/// reading every 3 s, the default poll. A07.
/// </summary>
public class SeenGrowthTests
{
    private const string Species = "Alpha";
    private const string Gender = "Male";

    private static DinoReading In(double growth, string species = Species, string? gender = Gender) =>
        new(true, species, gender, growth);

    private static double G(int thousandths) => thousandths / 1000.0;

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Feeds the readings 3 s apart from <paramref name="start"/> and collects every step.</summary>
    private static List<SeenStep> Feed(SeenGrowth seen, IEnumerable<double> growths, double start = 0, double every = 3)
    {
        var steps = new List<SeenStep>();
        var t = start;
        foreach (var g in growths)
        {
            if (seen.Update(In(g), S(t)) is { } step) steps.Add(step);
            t += every;
        }
        return steps;
    }

    // ---- Honest growth -------------------------------------------------------------------

    [Fact]
    public void AnHonestGrowOverTenMinutesCountsAllOfIt()
    {
        // 0.300 to 0.320 in ten minutes: 201 readings at 3 s, a 0.1% step every 30 s.
        var growths = Enumerable.Range(0, 201).Select(i => G(300 + i / 10));
        var steps = Feed(new SeenGrowth(), growths);

        Assert.Equal(20, steps.Count);
        Assert.Equal(0.020, steps.Sum(s => s.Grown), 12);
        Assert.Equal(0.300, steps[0].From);
        Assert.Equal(0.320, steps[^1].To);
        Assert.All(steps, s => Assert.Equal(Species, s.Species));
        Assert.All(steps, s => Assert.Equal(Gender, s.Gender));
    }

    [Fact]
    public void AnHonestGrowThatWobblesAtEveryStepCountsTheSame()
    {
        // At each new thousandth the website's last digit goes back once and up again.
        var growths = new List<double>();
        for (var k = 300; k < 320; k++)
        {
            growths.AddRange(Enumerable.Repeat(G(k), 6));
            growths.Add(G(k + 1));
            growths.Add(G(k));
            growths.Add(G(k + 1));
            growths.Add(G(k));
        }
        growths.Add(G(320));
        var steps = Feed(new SeenGrowth(), growths);

        Assert.Equal(20, steps.Count);
        Assert.Equal(0.020, steps.Sum(s => s.Grown), 12);
    }

    [Fact]
    public void TheFastestAllowedRiseCountsEveryStep()
    {
        // 0.5% every 3 s is exactly MaxGrowthPerSecond: every step counts, every
        // value from 0 to 0.995 (the floating-point allowance at work).
        var growths = Enumerable.Range(0, 200).Select(i => G(i * 5));
        var steps = Feed(new SeenGrowth(), growths);
        Assert.Equal(199, steps.Count);
        Assert.Equal(0.995, steps.Sum(s => s.Grown), 9);
    }

    [Fact]
    public void ARiseFasterThanAnyDinoNeverCounts()
    {
        // 0.6% every 3 s: each reading is a jump no dino makes, so each is a new baseline.
        var growths = Enumerable.Range(0, 150).Select(i => G(i * 6));
        Assert.Empty(Feed(new SeenGrowth(), growths));
    }

    // ---- A first sight never counts -----------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.6)]
    [InlineData(0.9995)]
    [InlineData(1.0)]
    public void TheFirstReadingOfALifeNeverCounts(double growth)
    {
        Assert.Null(new SeenGrowth().Update(In(growth), S(10)));
    }

    [Fact]
    public void ASecondSeenGrowthKnowsNothingOfTheFirst()
    {
        // A restart: a new launch is a new object, with no baseline.
        var before = new SeenGrowth();
        Assert.Null(before.Update(In(0.300), S(0)));
        Assert.NotNull(before.Update(In(0.301), S(3)));

        var after = new SeenGrowth();
        Assert.Null(after.Update(In(0.302), S(6)));
        Assert.Null(after.Update(In(0.302), S(9)));
        var step = after.Update(In(0.303), S(12));
        Assert.Equal(0.302, step!.Value.From);   // counts from its own first sight, not the old object's
    }

    // ---- Wobbles --------------------------------------------------------------------------

    [Theory]
    [InlineData(5)]
    [InlineData(1000)]
    public void AWobbleCountsNothingAtAnyLength(int readings)
    {
        var growths = Enumerable.Range(0, readings).Select(i => i % 2 == 0 ? 0.416 : 0.415);
        Assert.Empty(Feed(new SeenGrowth(), growths));
    }

    [Fact]
    public void AWobbleCountsNothingAtEveryThousandth()
    {
        // Every k / 1000 down one step and back: 0.014 − 0.001 is a hair above
        // 0.013 in binary, which the floating-point allowance covers.
        for (var k = 1; k <= 1000; k++)
        {
            var seen = new SeenGrowth();
            Assert.Empty(Feed(seen, new[] { G(k), G(k - 1), G(k), G(k - 1), G(k) }));
        }
    }

    [Fact]
    public void AWobbleUnderTheMarkThenAnHonestStepCountsOneThousandth()
    {
        var steps = Feed(new SeenGrowth(), new[] { 0.416, 0.415, 0.416, 0.415, 0.417 });
        var step = Assert.Single(steps);
        Assert.Equal(0.416, step.From);
        Assert.Equal(0.417, step.To);
    }

    // ---- What breaks a life ---------------------------------------------------------------

    [Fact]
    public void ASwapCountsNothingAndHonestGrowthAfterItCountsFromTheNewDino()
    {
        // A stored dino at 70% swapped in for one at 30%, three seconds later.
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        Assert.Null(seen.Update(In(0.700), S(3)));
        Assert.Null(seen.Update(In(0.700), S(6)));
        var step = seen.Update(In(0.701), S(9));
        Assert.Equal(new SeenStep(Species, Gender, 0.700, 0.701), step);
    }

    [Fact]
    public void AFallIsAFreshLifeCountedFromWhereItStarts()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.700), S(0)));
        Assert.Null(seen.Update(In(0.300), S(3)));
        var step = seen.Update(In(0.301), S(6));
        Assert.Equal(new SeenStep(Species, Gender, 0.300, 0.301), step);
    }

    [Fact]
    public void AFallOfMoreThanTheSlackIsANewLife()
    {
        // 0.002 down is more than the last digit's wobble.
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.416), S(0)));
        Assert.Null(seen.Update(In(0.414), S(3)));
        Assert.Equal(0.414, seen.Update(In(0.415), S(6))!.Value.From);
    }

    [Fact]
    public void AnotherSpeciesIsANewBaseline()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        Assert.Null(seen.Update(In(0.301, species: "Beta"), S(3)));
        var step = seen.Update(In(0.302, species: "Beta"), S(6));
        Assert.Equal(new SeenStep("Beta", Gender, 0.301, 0.302), step);
    }

    [Fact]
    public void AnotherGenderIsANewBaseline()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        Assert.Null(seen.Update(In(0.301, gender: "Female"), S(3)));
        var step = seen.Update(In(0.302, gender: "Female"), S(6));
        Assert.Equal(new SeenStep(Species, "Female", 0.301, 0.302), step);
    }

    [Fact]
    public void SpellingIsComparedIgnoringCase()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300, species: "Alpha", gender: "Male"), S(0)));
        var step = seen.Update(In(0.301, species: "ALPHA", gender: "male"), S(3));
        Assert.Equal(new SeenStep("ALPHA", "male", 0.300, 0.301), step);   // the reading's own spelling
    }

    [Fact]
    public void NoGenderIsTheEmptyOne()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300, gender: null), S(0)));
        var step = seen.Update(In(0.301, gender: ""), S(3));
        Assert.Equal("", step!.Value.Gender);
        Assert.Equal("", seen.Update(In(0.302, gender: null), S(6))!.Value.Gender);
    }

    [Fact]
    public void NothingCountsAcrossANotInGameReadingEvenWithEqualGrowth()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        Assert.Null(seen.Update(DinoReading.NotInGame, S(3)));
        Assert.Null(seen.Update(In(0.301), S(6)));   // a baseline: nothing across the gap
        Assert.Equal(0.301, seen.Update(In(0.302), S(9))!.Value.From);

        var again = new SeenGrowth();
        Assert.Null(again.Update(In(0.300), S(0)));
        Assert.Null(again.Update(DinoReading.NotInGame, S(3)));
        Assert.Null(again.Update(In(0.300), S(6)));
        Assert.Equal(0.300, again.Update(In(0.301), S(9))!.Value.From);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AReadingInGameWithNoSpeciesForgetsTheLife(string? species)
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        Assert.Null(seen.Update(new DinoReading(true, species, Gender, 0.300), S(3)));
        Assert.Null(seen.Update(In(0.301), S(6)));
    }

    [Fact]
    public void ForgetMakesTheNextReadingAFirstSight()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        seen.Forget();
        Assert.Null(seen.Update(In(0.301), S(3)));
        Assert.Equal(0.301, seen.Update(In(0.302), S(6))!.Value.From);
    }

    // ---- Gaps and rates -------------------------------------------------------------------

    [Fact]
    public void A119SecondGapWithAPlausibleRiseCounts()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.300), S(0));
        Assert.Equal(new SeenStep(Species, Gender, 0.300, 0.310), seen.Update(In(0.310), S(119)));
    }

    [Fact]
    public void AGapOfExactly120SecondsCounts()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.300), S(0));
        Assert.NotNull(seen.Update(In(0.301), S(120)));
    }

    [Fact]
    public void A121SecondGapCountsNothingAndStartsAgain()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.300), S(0));
        Assert.Null(seen.Update(In(0.301), S(121)));
        Assert.Equal(0.301, seen.Update(In(0.302), S(124))!.Value.From);
    }

    [Fact]
    public void ASixtySecondGapAllowsNinePercentButNotEleven()
    {
        var nine = new SeenGrowth();
        nine.Update(In(0.300), S(0));
        Assert.Equal(new SeenStep(Species, Gender, 0.300, 0.390), nine.Update(In(0.390), S(60)));

        var eleven = new SeenGrowth();
        eleven.Update(In(0.300), S(0));
        Assert.Null(eleven.Update(In(0.410), S(60)));
        Assert.Equal(0.410, eleven.Update(In(0.411), S(63))!.Value.From);   // the jump became the baseline
    }

    [Fact]
    public void TwoReadingsAtOneInstantWithARiseDoNotCount()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.300), S(5));
        Assert.Null(seen.Update(In(0.301), S(5)));
        Assert.Equal(0.301, seen.Update(In(0.302), S(8))!.Value.From);
    }

    [Fact]
    public void AClockThatGoesBackIsANewBaseline()
    {
        // The host's clock is monotonic; a fake one that isn't still counts nothing.
        var seen = new SeenGrowth();
        seen.Update(In(0.300), S(10));
        Assert.Null(seen.Update(In(0.301), S(7)));
        Assert.Equal(0.301, seen.Update(In(0.302), S(10))!.Value.From);
    }

    [Fact]
    public void AGrowthThatIsNotANumberIsNeverCounted()
    {
        var seen = new SeenGrowth();
        Assert.Null(seen.Update(In(0.300), S(0)));
        Assert.Null(seen.Update(In(double.NaN), S(3)));
        Assert.Null(seen.Update(In(0.301), S(6)));
        Assert.Null(seen.Update(In(double.NaN), S(9)));
        Assert.Null(seen.Update(In(double.NaN), S(12)));
        Assert.Null(seen.Update(In(0.302), S(15)));
        Assert.Equal(new SeenStep(Species, Gender, 0.302, 0.303), seen.Update(In(0.303), S(18)));
    }

    // ---- Stages ---------------------------------------------------------------------------

    [Fact]
    public void ASeenStepOverTheAdultLineReachesStage3()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.749), S(0));
        var step = seen.Update(In(0.751), S(3));
        Assert.Equal(3, step!.Value.StageReached);
    }

    [Fact]
    public void ASeenStepOntoTheElderLineReachesStage4()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.9990), S(0));
        var step = seen.Update(In(0.9995), S(3));
        Assert.Equal(4, step!.Value.StageReached);
    }

    [Fact]
    public void ALineIsReachedOnceALifeHoweverTheLastDigitWobbles()
    {
        var steps = Feed(new SeenGrowth(), new[] { 0.748, 0.749, 0.750, 0.749, 0.750, 0.749, 0.750, 0.751, 0.752 });
        Assert.Equal(new int?[] { null, 3, null, null }, steps.Select(s => s.StageReached));
    }

    [Fact]
    public void AStepFromExactlyOnALineDoesNotReachItAgain()
    {
        var seen = new SeenGrowth();
        seen.Update(In(0.750), S(0));
        Assert.Null(seen.Update(In(0.751), S(3))!.Value.StageReached);
    }

    [Fact]
    public void AStageSeenOnlyFromItsFirstSightIsNotReached()
    {
        // Spawning in at 75% is not reaching Adult.
        var steps = Feed(new SeenGrowth(), new[] { 0.750, 0.751, 0.752 });
        Assert.All(steps, s => Assert.Null(s.StageReached));
    }
}
