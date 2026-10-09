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

public class SignedOutRuleTests
{
    /// <summary>A dead cookie is final, but one odd answer is not a verdict: two refusals in a row end the session, a success in between resets (the counter lives in PollOnceAsync).</summary>
    [Fact]
    public void TwoRefusalsInARowEndTheSession()
    {
        Assert.False(PollService.SignedOutAfter(0));
        Assert.False(PollService.SignedOutAfter(1));
        Assert.True(PollService.SignedOutAfter(2));
        Assert.True(PollService.SignedOutAfter(5));
    }
}

public class FriendsCadenceTests
{
    private static readonly TimeSpan Active = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LongAgo = TimeSpan.FromSeconds(15);

    [Fact]
    public void FriendsRideEverySecondLivePoll()
    {
        Assert.False(PollService.FriendsDue(1, idling: false, hotTrigger: false, LongAgo, Active));
        Assert.True(PollService.FriendsDue(2, idling: false, hotTrigger: false, LongAgo, Active));
        Assert.True(PollService.FriendsDue(7, idling: false, hotTrigger: false, LongAgo, Active));
    }

    [Fact]
    public void FriendsRideEveryIdlePoll()
    {
        Assert.True(PollService.FriendsDue(1, idling: true, hotTrigger: false, LongAgo, Active));
    }

    [Fact]
    public void AHotTriggerMakesTheNextPollFetch()
    {
        Assert.True(PollService.FriendsDue(0, idling: false, hotTrigger: true, LongAgo, Active));
    }

    /// <summary>A hot fetch just before an idle tick: the tick's own roster waits (it went out under a second after, before Oct 2026).</summary>
    [Fact]
    public void NoRosterWithinOneIntervalOfTheLast()
    {
        Assert.False(PollService.FriendsDue(1, idling: true, hotTrigger: false, TimeSpan.FromSeconds(0.8), Active));
        Assert.False(PollService.FriendsDue(5, idling: false, hotTrigger: true, TimeSpan.FromSeconds(2.9), Active));
        Assert.True(PollService.FriendsDue(1, idling: true, hotTrigger: false, Active, Active));
    }
}

public class PollFloorTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Active = TimeSpan.FromSeconds(3);

    /// <summary>The floor under off-schedule polls and a new session's first one (Start).</summary>
    [Fact]
    public void APollIsStaleOneIntervalAfterTheLast()
    {
        Assert.False(PollService.IsStale(Now - TimeSpan.FromSeconds(0.5), Now, Active)); // a sign-in right after a poll: the timer's tick polls instead
        Assert.False(PollService.IsStale(Now - TimeSpan.FromSeconds(2.9), Now, Active));
        Assert.True(PollService.IsStale(Now - Active, Now, Active));
        Assert.True(PollService.IsStale(DateTime.MinValue, Now, Active)); // launch: never polled
    }
}
