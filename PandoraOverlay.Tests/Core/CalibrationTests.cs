using System.IO;
using Xunit;

namespace PandoraOverlay.Tests;

public class CalibrationTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstAttemptIsDue()
    {
        Assert.True(PollService.CalibrationDue(fetched: false, inFlight: false, DateTime.MinValue, Now));
    }

    [Fact]
    public void NothingIsDueOnceTheSiteHasAnswered()
    {
        Assert.False(PollService.CalibrationDue(fetched: true, inFlight: false, DateTime.MinValue, Now));
    }

    [Fact]
    public void NothingIsDueWhileAnAttemptIsInFlight()
    {
        Assert.False(PollService.CalibrationDue(fetched: false, inFlight: true, DateTime.MinValue, Now));
    }

    [Fact]
    public void AFailedAttemptIsRetriedOnlyAfterTheFloor()
    {
        Assert.False(PollService.CalibrationDue(false, false, Now - TimeSpan.FromSeconds(3), Now)); // the next poll is too soon
        Assert.False(PollService.CalibrationDue(false, false, Now - TimeSpan.FromSeconds(59), Now));
        Assert.True(PollService.CalibrationDue(false, false, Now - TimeSpan.FromSeconds(60), Now));
    }

    [Fact]
    public void TheBundledSeedCanPlaceMarkers()
    {
        var seed = PollService.BundledCalibration();

        Assert.NotNull(seed);
        Assert.True(seed!.MapSize > 0);
        Assert.NotEqual(0, seed.ScaleX);
        Assert.NotEqual(0, seed.ScaleY);
    }

    [Fact]
    public void TheBundledSeedMatchesTheAreaMapsCalibration()
    {
        // Both describe the bundled map picture: a seed for another map would
        // put the arrow in the wrong place and the area lookup out of step.
        using var stream = typeof(PollService).Assembly.GetManifestResourceStream("areas.json")!;
        using var reader = new StreamReader(stream);
        var legend = AreaLegend.Parse(reader.ReadToEnd());

        Assert.Equal(legend!.Calibration, PollService.BundledCalibration());
    }
}
