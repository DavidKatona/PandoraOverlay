using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The Pack's wake rule: the stats panel's lines, reused, on the rows with bars only.</summary>
public class PackAttentionTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);

    private static PackRow Row(string id, double health, PackRowKind kind = PackRowKind.Live, bool head = false, string dino = "Deinosuchus") =>
        new(id, id, 0, kind, PackRowsTests.F(id, dino: dino, health: health, head: head), null);

    [Fact]
    public void ADropIntoALowerQuarterWakesForTheDamageHold()
    {
        var a = new PackAttention();
        Assert.False(a.Update(new[] { Row("a", 0.80) }, Now));
        Assert.True(a.Update(new[] { Row("a", 0.70) }, Now.AddSeconds(6))); // under 75: a quarter down
        Assert.True(a.Update(new[] { Row("a", 0.70) }, Now.AddSeconds(12)));
        Assert.False(a.Update(new[] { Row("a", 0.70) }, Now.AddSeconds(6) + StatsAttention.DamageHold)); // the hold is over, above 55%
    }

    [Fact]
    public void AFirstSampleIsNeverADrop()
    {
        var a = new PackAttention();
        Assert.False(a.Update(new[] { Row("a", 0.6) }, Now));
    }

    [Fact]
    public void UnderHalfStaysLitAndCalmsAboveFiftyFive()
    {
        var a = new PackAttention();
        Assert.True(a.Update(new[] { Row("a", 0.45) }, Now));
        var later = Now.AddMinutes(1);
        Assert.True(a.Update(new[] { Row("a", 0.52) }, later));  // between the lines: stays lit
        Assert.True(a.Update(new[] { Row("a", StatsAttention.CalmAbove) }, later.AddSeconds(6)));
        Assert.False(a.Update(new[] { Row("a", 0.56) }, later.AddSeconds(12)));
    }

    [Fact]
    public void AFractureKeepsItLit()
    {
        var a = new PackAttention();
        Assert.True(a.Update(new[] { Row("a", 0.95, head: true) }, Now));
        Assert.True(a.Update(new[] { Row("a", 0.95, head: true) }, Now.AddMinutes(2)));
        Assert.False(a.Update(new[] { Row("a", 0.95) }, Now.AddMinutes(3)));
    }

    [Fact]
    public void OtherSpeciesHeldAndAwayRowsNeverWakeIt()
    {
        var a = new PackAttention();
        Assert.False(a.Update(new[] { Row("o", 0.1, PackRowKind.OtherSpecies, head: true), Row("n", 0.1, PackRowKind.NoStats),
                                      Row("h", 0.1, PackRowKind.Held), Row("w", 0.1, PackRowKind.Away) }, Now));
    }

    [Fact]
    public void ANewDinoStartsOver()
    {
        var a = new PackAttention();
        a.Update(new[] { Row("a", 0.9) }, Now);
        Assert.False(a.Update(new[] { Row("a", 0.6, dino: "Carnotaurus") }, Now.AddSeconds(6))); // a fresh life, not a hit
    }
}
