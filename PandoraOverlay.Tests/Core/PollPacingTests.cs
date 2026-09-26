using Xunit;

namespace PandoraOverlay.Tests;

public class PollPacingTests
{
    private static readonly TimeSpan Active = TimeSpan.FromSeconds(3);

    [Fact]
    public void LiveUsesTheConfiguredCadence()
    {
        Assert.Equal(Active, PollService.NextInterval(Active, live: true, TimeSpan.Zero));
        Assert.Equal(Active, PollService.NextInterval(Active, live: true, TimeSpan.FromHours(5)));
    }

    [Fact]
    public void NotLiveIdlesAtFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), PollService.NextInterval(Active, live: false, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(15), PollService.NextInterval(Active, live: false, TimeSpan.FromMinutes(9.9)));
    }

    [Fact]
    public void LongIdleDropsToOneMinute()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), PollService.NextInterval(Active, live: false, TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromSeconds(60), PollService.NextInterval(Active, live: false, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void PacingNeverSpeedsUpASlowConfiguredCadence()
    {
        var slow = TimeSpan.FromSeconds(30);
        Assert.Equal(slow, PollService.NextInterval(slow, live: false, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(60), PollService.NextInterval(slow, live: false, TimeSpan.FromMinutes(30)));

        var verySlow = TimeSpan.FromSeconds(120);
        Assert.Equal(verySlow, PollService.NextInterval(verySlow, live: false, TimeSpan.FromMinutes(30)));
    }
}
