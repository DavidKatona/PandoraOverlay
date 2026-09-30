using Xunit;

namespace PandoraOverlay.Tests;

public class StatsAttentionTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerState Player(double health = 1, double hunger = 1, double thirst = 1,
                                      bool legs = false, double growth = 0.5, string dino = "Pteranodon",
                                      double stamina = 1) =>
        new("765611", "Tester", dino, "Male", growth, health, stamina, hunger, thirst, 0, false, false, legs, 0, 0, 0);

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

    [Fact]
    public void LowStaminaDoesNotWakeSurvival() // it drains by design
    {
        var a = new StatsAttention();
        Assert.False(a.Update(Player(stamina: 0.05), null, null, T0));
    }

    // ---- Combat view ---------------------------------------------------------

    private static bool Combat(StatsAttention a, PlayerState p, bool inFight = false, double seconds = 0,
                               TimeSpan? hungerLeft = null) =>
        a.Update(p, hungerLeft, null, T0 + TimeSpan.FromSeconds(seconds), combatView: true, inFight: inFight);

    [Fact]
    public void CombatCalmsWhenHealthAndStaminaAreHigh()
    {
        var a = new StatsAttention();
        Assert.False(Combat(a, Player()));
    }

    [Fact]
    public void CombatWakesOnStaminaUnderThreeQuarters()
    {
        var a = new StatsAttention();
        Combat(a, Player());
        Assert.True(Combat(a, Player(stamina: 0.74)));
        Assert.True(Combat(a, Player(stamina: 0.78)));  // between the lines: stays lit
        Assert.False(Combat(a, Player(stamina: 0.81))); // clear of the calm line
        Assert.False(Combat(a, Player(stamina: 0.77))); // between the lines: stays calm
    }

    [Fact]
    public void CombatWakesOnHealthUnderThreeQuarters() // survival would stay calm at 70%
    {
        var survival = new StatsAttention();
        Assert.False(survival.Update(Player(health: 0.70), null, null, T0)); // 70% is fine there

        var combat = new StatsAttention();
        Assert.True(Combat(combat, Player(health: 0.70)));   // not at fighting strength
        Assert.True(Combat(combat, Player(health: 0.78)));   // between the lines: stays lit
        Assert.False(Combat(combat, Player(health: 0.81)));  // clear of the calm line
    }

    [Fact]
    public void CombatIgnoresHungerThirstAndTheirEstimates() // none of them is on that view
    {
        var a = new StatsAttention();
        Assert.False(Combat(a, Player(hunger: 0.10, thirst: 0.10), hungerLeft: TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void CombatIgnoresNotedEvents() // the growth readout is hidden there
    {
        var a = new StatsAttention();
        Combat(a, Player());
        a.NoteEvent(T0);
        Assert.False(Combat(a, Player(), seconds: 3));
    }

    [Fact]
    public void CombatStaysLitForAsLongAsTheFightLasts()
    {
        var a = new StatsAttention();
        Combat(a, Player());
        Assert.True(Combat(a, Player(health: 0.95), inFight: true, seconds: 3));
        Assert.True(Combat(a, Player(health: 0.95), inFight: true, seconds: 25)); // long past survival's 10 s hold
        Assert.False(Combat(a, Player(health: 0.95), inFight: false, seconds: 36));
    }

    [Fact]
    public void CombatFractureWakes()
    {
        var a = new StatsAttention();
        Combat(a, Player());
        Assert.True(Combat(a, Player(legs: true)));
        Assert.False(Combat(a, Player()));
    }

    [Fact]
    public void AFlippedViewDecidesAfresh()
    {
        var a = new StatsAttention();
        // Lit in survival by thirst; combat does not show thirst, so the flip calms it at once.
        Assert.True(a.Update(Player(thirst: 0.30), null, null, T0));
        Assert.False(Combat(a, Player(thirst: 0.30)));

        // A value between the new view's lines has no verdict to inherit: it reads as calm.
        Assert.False(a.Update(Player(stamina: 0.78), null, null, T0));
        Assert.False(Combat(a, Player(stamina: 0.78)));

        // And a flip onto a view with a reason to wake lights it.
        Assert.False(a.Update(Player(stamina: 0.60), null, null, T0));
        Assert.True(Combat(a, Player(stamina: 0.60)));
    }

    [Fact]
    public void AFlipMidFightKeepsSurvivalsDamageHold() // the baseline runs in both views
    {
        var a = new StatsAttention();
        Combat(a, Player(health: 0.95));
        Combat(a, Player(health: 0.85), inFight: true, seconds: 3);
        Assert.True(a.Update(Player(health: 0.85), null, null, T0 + TimeSpan.FromSeconds(6)));   // within the 10 s hold
        Assert.False(a.Update(Player(health: 0.85), null, null, T0 + TimeSpan.FromSeconds(15))); // hold over, 85% is fine in survival
    }
}
