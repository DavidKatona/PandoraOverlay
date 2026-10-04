using Xunit;

namespace PandoraOverlay.Tests;

public class SelfActivityTests
{
    private static readonly DateTime T0 = new(2026, 9, 28, 18, 0, 0, DateTimeKind.Utc);

    private static MyLocationResponse Live(string dino = "Deinosuchus", double growth = 0.4, double health = 1,
                                           bool legs = false, string gender = "Male") =>
        new(true, new PlayerState("me", "Dave", dino, gender, growth, health, 1, 1, 1, 0, false, false, legs, 0, 0, 0));

    private static readonly MyLocationResponse Off = new(false, null);

    [Fact]
    public void BeingInGameAtLaunchIsABaselineNotASpawn()
    {
        var self = new SelfActivity();
        Assert.Empty(self.Update(Live(), damageLines: true, T0));
        Assert.Empty(self.Update(Live(growth: 0.401), damageLines: true, T0.AddSeconds(3)));
    }

    [Fact]
    public void SpawningAfterNotInGameIsReported()
    {
        var self = new SelfActivity();
        self.Update(Off, true, T0);
        var line = Assert.Single(self.Update(Live(growth: 0.42), true, T0.AddSeconds(15)));
        Assert.Equal(FeedKind.Spawned, line.Kind);
        Assert.True(line.Mine);
        Assert.Null(line.SteamId);
        Assert.Equal("You spawned as Deinosuchus 42%", line.Text);
    }

    [Fact]
    public void SameSpeciesLowerGrowthIsAFreshLife()
    {
        var self = new SelfActivity();
        self.Update(Live(growth: 0.61), true, T0);
        self.Update(Off, true, T0.AddSeconds(3));
        var line = Assert.Single(self.Update(Live(growth: 0.2), true, T0.AddSeconds(30)));
        Assert.Equal(FeedKind.NewLife, line.Kind);
        Assert.Equal("You started a fresh Deinosuchus 20%", line.Text);

        self.Update(Off, true, T0.AddSeconds(60));
        var other = Assert.Single(self.Update(Live(dino: "Ceratosaurus", growth: 0.1), true, T0.AddSeconds(90)));
        Assert.Equal("You spawned as Ceratosaurus 10%", other.Text);
    }

    [Fact]
    public void FracturesAndDamageAreReportedWithinALife()
    {
        var self = new SelfActivity();
        self.Update(Live(health: 1), true, T0);
        var lines = self.Update(Live(health: 0.9, legs: true), true, T0.AddSeconds(3));
        Assert.Equal(2, lines.Count);
        Assert.Equal("You fractured your legs", lines[0].Text);
        Assert.Equal(FeedKind.Fracture, lines[0].Kind);
        Assert.Equal("Took damage · HP 90%", lines[1].Text);
        Assert.Equal(FeedKind.Damage, lines[1].Kind);

        Assert.Empty(self.Update(Live(health: 0.9, legs: true), true, T0.AddSeconds(6))); // still fractured: not news
    }

    [Fact]
    public void DamageNeedsARealDropIsRateLimitedAndCanBeSwitchedOff()
    {
        var self = new SelfActivity();
        self.Update(Live(health: 1), true, T0);
        Assert.Empty(self.Update(Live(health: 0.97), true, T0.AddSeconds(3)));              // 3%: the fade's cue, not a line
        Assert.Single(self.Update(Live(health: 0.9), true, T0.AddSeconds(6)));              // 7%: a hit
        Assert.Empty(self.Update(Live(health: 0.8), true, T0.AddSeconds(9)));               // within the quiet half minute
        Assert.Single(self.Update(Live(health: 0.7), true, T0.AddSeconds(40)));             // quiet over, next hit reported

        var off = new SelfActivity();
        off.Update(Live(health: 1), false, T0);
        Assert.Empty(off.Update(Live(health: 0.5), false, T0.AddSeconds(3)));
    }

    [Fact]
    public void ANewLifeComparesNothing()
    {
        var self = new SelfActivity();
        self.Update(Live(growth: 0.6, health: 1), true, T0);
        // Same dino, lower growth without a not-in-game poll between (a missed poll): new life, no damage/fracture line.
        Assert.Empty(self.Update(Live(growth: 0.1, health: 0.3, legs: true), true, T0.AddSeconds(3)));
    }

    [Fact]
    public void BuildersWordTheOtherEvents()
    {
        Assert.Equal("You are now a subadult", SelfActivity.GrowthLine(50, T0).Text);
        Assert.Equal(FeedKind.Growth, SelfActivity.GrowthLine(50, T0).Kind);
        Assert.Equal("Hunger under 20% · ~40m left", SelfActivity.LowStatLine("Hunger", "~40m", T0).Text);
        Assert.Equal("Thirst under 20%", SelfActivity.LowStatLine("Thirst", null, T0).Text);
        Assert.True(SelfActivity.LowStatLine("Thirst", null, T0).Mine);
    }

    [Fact]
    public void PrimeLinesReportTheChangeElseASummary()
    {
        bool[] Flags(params int[] met) => Enumerable.Range(1, 10).Select(met.Contains).ToArray();
        var before = new PrimeSnapshot(false, false, Flags(1, 2, 7), T0, "Deinosuchus");
        var fresh = new PrimeSnapshot(false, true, Flags(1, 2, 4, 5, 6), T0.AddMinutes(5), "Deinosuchus");

        var diff = SelfActivity.PrimeLines(before, fresh, T0.AddMinutes(5));
        Assert.Equal(new[]
        {
            "Prime · now met: Visit Mass Migration zone",
            "Prime · now met: Visit 2 Migration zones",
            "Prime · now met: Visit 4 Patrol zones",
            "Prime · lost: Never be Infertile"
        }, diff.Select(l => l.Text));
        Assert.All(diff, l => Assert.Equal(FeedKind.Prime, l.Kind));

        var first = Assert.Single(SelfActivity.PrimeLines(null, fresh, T0));
        Assert.Equal("Prime check · 5/10 · ready", first.Text);
        var same = Assert.Single(SelfActivity.PrimeLines(fresh, fresh, T0));
        Assert.Equal("Prime check · 5/10 · ready", same.Text);
        var elder = Assert.Single(SelfActivity.PrimeLines(null, new PrimeSnapshot(true, true, Flags(1, 2, 3, 4, 5, 6), T0), T0));
        Assert.Equal("Prime check · 6/10 · Prime Elder", elder.Text);
    }

    [Fact]
    public void YourSpawnLineSaysWhereWhenTheAreaIsKnown()
    {
        var self = new SelfActivity();
        self.Update(Off, true, T0);

        var line = Assert.Single(self.Update(Live(growth: 0.42), true, T0.AddSeconds(15), area: "Somewhere"));

        Assert.Equal("You spawned as Deinosuchus 42% · Somewhere", line.Text);
    }

    [Fact]
    public void AFreshLifeSaysWhereAndNoAreaLeavesTheLineAsItWas()
    {
        var self = new SelfActivity();
        self.Update(Live(growth: 0.61), true, T0);
        self.Update(Off, true, T0.AddSeconds(3));
        Assert.Equal("You started a fresh Deinosuchus 20% · Somewhere",
            Assert.Single(self.Update(Live(growth: 0.2), true, T0.AddSeconds(30), area: "Somewhere")).Text);

        self.Update(Off, true, T0.AddSeconds(60));
        Assert.Equal("You spawned as Deinosuchus 30%",
            Assert.Single(self.Update(Live(growth: 0.3), true, T0.AddSeconds(90), area: null)).Text);
    }
}
