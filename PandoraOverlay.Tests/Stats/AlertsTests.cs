using Xunit;

namespace PandoraOverlay.Tests;

public class LowStatAlertTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double hunger = 1, double thirst = 1, double growth = 0.5, string dino = "Pteranodon") =>
        new("765611", "Tester", dino, "Male", growth, 1, 1, hunger, thirst, 0, false, false, false, 0, 0, 0);

    [Fact]
    public void ChimesOnceWhenCrossingTheLine()
    {
        var a = new LowStatAlert();
        Assert.False(a.Update(Player(hunger: 0.25), T0));
        Assert.True(a.Update(Player(hunger: 0.19), T0 + TimeSpan.FromSeconds(3)));
        Assert.False(a.Update(Player(hunger: 0.18), T0 + TimeSpan.FromSeconds(6)));
        Assert.False(a.Update(Player(hunger: 0.10), T0 + TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void RepeatsEveryFiveMinutesWhileLow()
    {
        var a = new LowStatAlert();
        a.Update(Player(hunger: 0.19), T0);
        Assert.False(a.Update(Player(hunger: 0.15), T0 + TimeSpan.FromMinutes(4.9)));
        Assert.True(a.Update(Player(hunger: 0.15), T0 + TimeSpan.FromMinutes(5)));
        Assert.False(a.Update(Player(hunger: 0.12), T0 + TimeSpan.FromMinutes(6)));
        Assert.True(a.Update(Player(hunger: 0.12), T0 + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void HoveringAtTheLineDoesNotReChime()
    {
        var a = new LowStatAlert();
        a.Update(Player(hunger: 0.19), T0);
        Assert.False(a.Update(Player(hunger: 0.22), T0 + TimeSpan.FromSeconds(3))); // back over 20, not over 30
        Assert.False(a.Update(Player(hunger: 0.19), T0 + TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public void ReArmsAfterEating()
    {
        var a = new LowStatAlert();
        a.Update(Player(hunger: 0.19), T0);
        Assert.False(a.Update(Player(hunger: 0.80), T0 + TimeSpan.FromSeconds(3)));
        Assert.True(a.Update(Player(hunger: 0.19), T0 + TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public void StatsAreIndependent()
    {
        var a = new LowStatAlert();
        Assert.True(a.Update(Player(hunger: 0.19), T0));
        Assert.True(a.Update(Player(hunger: 0.19, thirst: 0.19), T0 + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void NewLifeStartsArmed()
    {
        var a = new LowStatAlert();
        a.Update(Player(hunger: 0.19), T0);
        Assert.True(a.Update(Player(hunger: 0.10, dino: "Deinosuchus"), T0 + TimeSpan.FromSeconds(3)));
    }
}

public class GrowthMilestonesTests
{
    private static PlayerState Player(double growth, string dino = "Pteranodon") =>
        new("765611", "Tester", dino, "Male", growth, 1, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    [Fact]
    public void FirstSampleNeverAlerts()
    {
        var m = new GrowthMilestones();
        Assert.Null(m.Update(Player(0.60)));
    }

    [Fact]
    public void CrossingALineAlertsOnce()
    {
        var m = new GrowthMilestones();
        m.Update(Player(0.49));
        Assert.Equal(50, m.Update(Player(0.50)));
        Assert.Null(m.Update(Player(0.51)));
    }

    [Fact]
    public void JumpingTwoLinesReportsTheHigher()
    {
        var m = new GrowthMilestones();
        m.Update(Player(0.20));
        Assert.Equal(50, m.Update(Player(0.55)));
    }

    [Fact]
    public void FullyGrownIsTheHundred()
    {
        var m = new GrowthMilestones();
        m.Update(Player(0.99));
        Assert.Null(m.Update(Player(0.999)));
        Assert.Equal(100, m.Update(Player(1.0)));
    }

    [Fact]
    public void DeathResetsSoTheNextLifeCrossesAgain()
    {
        var m = new GrowthMilestones();
        m.Update(Player(0.24));
        Assert.Equal(25, m.Update(Player(0.26)));
        Assert.Null(m.Update(Player(0.10)));   // died: baseline only
        Assert.Equal(25, m.Update(Player(0.25)));
    }

    [Fact]
    public void DinoSwapResets()
    {
        var m = new GrowthMilestones();
        m.Update(Player(0.24));
        Assert.Null(m.Update(Player(0.80, dino: "Deinosuchus")));
    }
}
