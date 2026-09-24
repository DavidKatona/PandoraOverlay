using Xunit;

namespace PandoraOverlay.Tests;

public class StatsAttentionTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double health = 1, double hunger = 1, double thirst = 1,
                                      bool legs = false, double growth = 0.5, string dino = "Pteranodon") =>
        new("765611", "Tester", dino, "Male", growth, health, 1, hunger, thirst, 0, false, false, legs, 0, 0, 0);

    [Fact]
    public void CalmsWhenEverythingIsFine()
    {
        var a = new StatsAttention();
        Assert.True(a.Lit); // nothing known yet
        Assert.False(a.Update(Player(), null, null, T0));
    }

    [Fact]
    public void LowStatWakesAndHysteresisHolds()
    {
        var a = new StatsAttention();
        a.Update(Player(), null, null, T0);
        Assert.True(a.Update(Player(thirst: 0.49), null, null, T0));
        Assert.True(a.Update(Player(thirst: 0.53), null, null, T0));  // between the lines: stays lit
        Assert.False(a.Update(Player(thirst: 0.56), null, null, T0)); // clear of the calm line
        Assert.False(a.Update(Player(thirst: 0.52), null, null, T0)); // between the lines: stays calm
    }

    [Fact]
    public void FractureWakesUntilHealed()
    {
        var a = new StatsAttention();
        a.Update(Player(), null, null, T0);
        Assert.True(a.Update(Player(legs: true), null, null, T0));
        Assert.False(a.Update(Player(), null, null, T0));
    }

    [Fact]
    public void DamageWakesForAFewSecondsOnly()
    {
        var a = new StatsAttention();
        a.Update(Player(health: 0.90), null, null, T0);
        Assert.True(a.Update(Player(health: 0.80), null, null, T0 + TimeSpan.FromSeconds(3)));
        Assert.True(a.Update(Player(health: 0.80), null, null, T0 + TimeSpan.FromSeconds(9)));
        Assert.False(a.Update(Player(health: 0.80), null, null, T0 + TimeSpan.FromSeconds(14)));
    }

    [Fact]
    public void QuantizationWobbleIsNotDamage()
    {
        var a = new StatsAttention();
        a.Update(Player(health: 0.900), null, null, T0);
        Assert.False(a.Update(Player(health: 0.897), null, null, T0 + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void LowerHealthOnANewDinoIsNotDamage()
    {
        var a = new StatsAttention();
        a.Update(Player(health: 1.0), null, null, T0);
        Assert.False(a.Update(Player(health: 0.7, dino: "Deinosuchus"), null, null, T0 + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void RespawnAfterDeathIsNotDamage() // growth dropped: a fresh life
    {
        var a = new StatsAttention();
        a.Update(Player(health: 1.0, growth: 0.6), null, null, T0);
        Assert.False(a.Update(Player(health: 0.7, growth: 0.1), null, null, T0 + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void ImminentDrainWakesEvenAboveHalf()
    {
        var a = new StatsAttention();
        a.Update(Player(), null, null, T0);
        Assert.True(a.Update(Player(hunger: 0.8), TimeSpan.FromMinutes(12), null, T0));
        Assert.True(a.Update(Player(hunger: 0.8), TimeSpan.FromMinutes(18), null, T0));  // in the gap: stays lit
        Assert.False(a.Update(Player(hunger: 0.8), TimeSpan.FromMinutes(25), null, T0)); // clear
    }

    [Fact]
    public void ANotedEventWakesForAFewSeconds()
    {
        var a = new StatsAttention();
        a.Update(Player(), null, null, T0);
        a.NoteEvent(T0 + TimeSpan.FromSeconds(3));
        Assert.True(a.Update(Player(), null, null, T0 + TimeSpan.FromSeconds(3)));
        Assert.True(a.Update(Player(), null, null, T0 + TimeSpan.FromSeconds(12)));
        Assert.False(a.Update(Player(), null, null, T0 + TimeSpan.FromSeconds(14)));
    }

    [Fact]
    public void ResetDropsTheDamageBaseline()
    {
        var a = new StatsAttention();
        a.Update(Player(health: 1.0), null, null, T0);
        a.Reset();
        Assert.False(a.Update(Player(health: 0.6), null, null, T0 + TimeSpan.FromSeconds(3)));
    }
}
