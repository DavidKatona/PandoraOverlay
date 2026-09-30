using Xunit;

namespace PandoraOverlay.Tests;

public class DamageTrackerTests
{
    private static readonly DateTime T0 = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double health, double growth = 0.5, string dino = "Deinosuchus") =>
        new("765611", "Tester", dino, "Male", growth, health, 1, 1, 1, 0, false, false, false, 0, 0, 0);

    [Fact]
    public void NothingBeforeAHit()
    {
        var t = new DamageTracker();
        t.Add(Player(1.0), T0);
        t.Add(Player(1.0), T0.AddSeconds(3));
        Assert.Equal(0, t.Total);
        Assert.False(t.InFight);
    }

    [Fact]
    public void HitsAddUpAndRegenerationIsNotSubtracted()
    {
        var t = new DamageTracker();
        t.Add(Player(1.00), T0);
        t.Add(Player(0.85), T0.AddSeconds(3));   // -15%
        t.Add(Player(0.88), T0.AddSeconds(6));   // regenerated a little
        t.Add(Player(0.70), T0.AddSeconds(9));   // -18%
        Assert.Equal(0.33, t.Total, 3);
        Assert.True(t.InFight);
    }

    [Fact]
    public void ABleedCountsAndWobbleDoesNot()
    {
        var t = new DamageTracker();
        t.Add(Player(0.900), T0);
        t.Add(Player(0.899), T0.AddSeconds(3));  // one-step wobble: not a hit
        Assert.Equal(0, t.Total);
        t.Add(Player(0.894), T0.AddSeconds(6));  // a bleed tick
        t.Add(Player(0.889), T0.AddSeconds(9));
        Assert.Equal(0.010, t.Total, 3);
    }

    [Fact]
    public void TheFightEndsHalfAMinuteAfterTheLastHit()
    {
        var t = new DamageTracker();
        t.Add(Player(1.0), T0);
        t.Add(Player(0.8), T0.AddSeconds(3));
        t.Add(Player(0.8), T0.AddSeconds(30));   // 27 s since the hit: still the same fight
        Assert.Equal(0.2, t.Total, 3);
        t.Add(Player(0.8), T0.AddSeconds(33));   // 30 s: over
        Assert.Equal(0, t.Total);
        Assert.False(t.InFight);

        t.Add(Player(0.7), T0.AddSeconds(36));   // a new fight starts from zero
        Assert.Equal(0.1, t.Total, 3);
    }

    [Fact]
    public void ALongFightCanPassTheWholePool()
    {
        var t = new DamageTracker();
        t.Add(Player(1.0), T0);
        for (var i = 1; i <= 6; i++)
        {
            t.Add(Player(0.7), T0.AddSeconds(i * 6 - 3)); // -30%
            t.Add(Player(1.0), T0.AddSeconds(i * 6));     // healed back up
        }
        Assert.Equal(1.8, t.Total, 3);
    }

    [Fact]
    public void ANewLifeStartsClean()
    {
        var t = new DamageTracker();
        t.Add(Player(1.0), T0);
        t.Add(Player(0.4), T0.AddSeconds(3));
        t.Add(Player(1.0, growth: 0.1), T0.AddSeconds(6));         // growth fell: a new life
        Assert.Equal(0, t.Total);
        t.Add(Player(0.9, growth: 0.1), T0.AddSeconds(9));
        Assert.Equal(0.1, t.Total, 3);

        t.Add(Player(0.5, dino: "Ceratosaurus"), T0.AddSeconds(12)); // another dino: no "hit" from the swap
        Assert.Equal(0, t.Total);
    }

    [Fact]
    public void ResetClears()
    {
        var t = new DamageTracker();
        t.Add(Player(1.0), T0);
        t.Add(Player(0.5), T0.AddSeconds(3));
        t.Reset();
        Assert.Equal(0, t.Total);
        t.Add(Player(0.2), T0.AddSeconds(6)); // first sample after a reset is only a baseline
        Assert.Equal(0, t.Total);
    }
}
