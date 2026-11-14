using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;
using static PandoraOverlay.Games.Tests.Sweeps;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The balance's targets B1 to B13 as tests (H32), on the bot's runs (<see cref="Bot"/>,
/// <see cref="Sweeps"/>, <see cref="Careers"/>). Each prints its whole table before it
/// asserts anything, so a failure shows every number, and each failure names the rows
/// that missed. The bands are the planner's; the knobs that may move to meet them are
/// the species' threat and, a little, the dangers' threat and turns (H33), and
/// nothing else.
///
/// <para>B13 holds the middle of the climb (H68). Trait <c>Category=Balance</c>, to run them alone
/// (<c>dotnet test Games/PandoraOverlay.Games.Tests --filter Category=Balance</c>);
/// they run in CI with the rest. About 64,000 runs and 45 careers, side by side.</para>
/// </summary>
[Trait("Category", "Balance")]
public class BalanceTests
{
    /// <summary>B1, B2 and B6: fresh, danger 1, runs a species.</summary>
    private const int FreshRuns = 400;

    /// <summary>B3: full, danger 1, runs a species.</summary>
    private const int FullRuns = 400;

    /// <summary>B4 and B5: full, each danger, runs a species.</summary>
    private const int DangerRuns = 200;

    /// <summary>B12: runs of each species with each instinct forced at turn 0.</summary>
    private const int ForcedRuns = 20;

    private readonly ITestOutputHelper _output;

    public BalanceTests(ITestOutputHelper output) => _output = output;

    private void Line(string text) => _output.WriteLine(text);

    /// <summary>Asserts no row missed, naming every one that did.</summary>
    private static void NoMisses(List<string> misses, string what) =>
        Assert.True(misses.Count == 0, $"{what}: {string.Join("; ", misses)}");

    // ---- B1, B2, B6: fresh, danger 1 ----------------------------------------------------

    [Fact]
    public void B1_NoBuildIsTheEasyPick()
    {
        var runs = Of("fresh", 1, FreshRuns);
        Line($"B1: fresh, danger 1, {FreshRuns} runs a species: reaches Adult 30 to 50%, wins 0.5 to 12%");
        Line("species              build     threat   adult    won");
        var misses = new List<string>();
        foreach (var s in SpeciesTable.All)
        {
            var r = runs[s.Name];
            var adult = Share(r, x => x.ReachedAdult);
            var won = Share(r, x => x.Won);
            var miss = adult < 0.30 || adult > 0.50 || won < 0.005 || won > 0.12;
            Line($"{s.Name,-20} {s.Build,-8} {s.Threat,6:F2}  {Pct(adult)} {Pct(won)}{(miss ? "  MISS" : "")}");
            if (miss) misses.Add($"{s.Name} adult {Pct(adult)} won {Pct(won)}");
        }
        var all = runs.Values.SelectMany(r => r).ToList();
        Line($"all                                    {Pct(Share(all, x => x.ReachedAdult))} {Pct(Share(all, x => x.Won))}");
        NoMisses(misses, "B1 out of its bands");
    }

    [Fact]
    public void B2_NobodyStarves()
    {
        var runs = Of("fresh", 1, FreshRuns);
        Line($"B2: the same runs: at most 5% end by hunger or thirst");
        Line("species              hunger  thirst");
        var misses = new List<string>();
        foreach (var s in SpeciesTable.All)
        {
            var r = runs[s.Name];
            var hunger = Share(r, x => x.Reason == EndReason.Hunger);
            var thirst = Share(r, x => x.Reason == EndReason.Thirst);
            var miss = hunger + thirst > 0.05;
            Line($"{s.Name,-20} {Pct(hunger)} {Pct(thirst)}{(miss ? "  MISS" : "")}");
            if (miss) misses.Add($"{s.Name} {Pct(hunger + thirst)}");
        }
        NoMisses(misses, "B2: more than 5% starved");
    }

    [Fact]
    public void B6_ARunIsASitting()
    {
        var runs = Of("fresh", 1, FreshRuns);
        Line($"B6: the same runs: a species' mean run is 35 to 65 turns; no run reaches {Bot.TurnLimit}");
        Line("species              mean turns  longest  at the limit");
        var misses = new List<string>();
        foreach (var s in SpeciesTable.All)
        {
            var r = runs[s.Name];
            var mean = r.Average(x => x.Turns);
            var limit = r.Count(x => x.TimedOut);
            var miss = mean < 35 || mean > 65 || limit > 0;
            Line($"{s.Name,-20} {mean,10:F1} {r.Max(x => x.Turns),8} {limit,13}{(miss ? "  MISS" : "")}");
            if (miss) misses.Add($"{s.Name} mean {mean:F1}, {limit} at the limit");
        }
        NoMisses(misses, "B6 out of its band");
    }

    // ---- B3, B4, B5: the full lineage ---------------------------------------------------

    [Fact]
    public void B3_TheLineageIsPower()
    {
        var runs = Of("full", 1, FullRuns);
        Line($"B3: full, danger 1, {FullRuns} runs a species: every species wins at least 75%");
        Line("species                 won");
        var misses = new List<string>();
        foreach (var s in SpeciesTable.All)
        {
            var won = Share(runs[s.Name], x => x.Won);
            var miss = won < 0.75;
            Line($"{s.Name,-20} {Pct(won)}{(miss ? "  MISS" : "")}");
            if (miss) misses.Add($"{s.Name} {Pct(won)}");
        }
        NoMisses(misses, "B3: under 75%");
    }

    [Fact]
    public void B4_Danger5IsHard()
    {
        var runs = Of("full", 5, DangerRuns);
        var d = Dangers.Of(5);
        Line($"B4: full, danger 5 (threat x{d.Threat:F2}, outlast {d.Outlast}), {DangerRuns} runs a species: every species wins 3 to 60%, their mean 35% or less");
        Line("species                 won");
        var misses = new List<string>();
        var shares = new List<double>();
        foreach (var s in SpeciesTable.All)
        {
            var won = Share(runs[s.Name], x => x.Won);
            shares.Add(won);
            var miss = won < 0.03 || won > 0.60;
            Line($"{s.Name,-20} {Pct(won)}{(miss ? "  MISS" : "")}");
            if (miss) misses.Add($"{s.Name} {Pct(won)}");
        }
        var mean = shares.Average();
        Line($"mean                 {Pct(mean)}{(mean > 0.35 ? "  MISS" : "")}");
        if (mean > 0.35) misses.Add($"the mean {Pct(mean)}");
        NoMisses(misses, "B4 out of its bands");
    }

    [Fact]
    public void B5_EachDangerIsHarder()
    {
        // Danger 1's runs are the first 200 of B3's seeds, danger 5's are B4's.
        var byDanger = Enumerable.Range(1, Dangers.Highest)
            .Select(d => d == 1
                ? Of("full", 1, FullRuns).ToDictionary(p => p.Key, p => p.Value.Take(DangerRuns).ToArray())
                : Of("full", d, DangerRuns).ToDictionary(p => p.Key, p => p.Value))
            .ToList();
        Line($"B5: full, {DangerRuns} runs a species at each danger: the mean win rate falls by at least 5 points from each danger to the next");
        Line("species                 d1     d2     d3     d4     d5");
        foreach (var s in SpeciesTable.All)
            Line($"{s.Name,-20} " + string.Join(" ", byDanger.Select(r => Pct(Share(r[s.Name], x => x.Won)))));
        var means = byDanger.Select(r => SpeciesTable.All.Average(s => Share(r[s.Name], x => x.Won))).ToList();
        Line($"mean                 " + string.Join(" ", means.Select(Pct)));
        Line("danger threat x      " + string.Join(" ", Dangers.All.Select(d => $"{d.Threat,6:F2}")));
        Line("danger outlast       " + string.Join(" ", Dangers.All.Select(d => $"{d.Outlast,6}")));
        var misses = new List<string>();
        for (var d = 1; d < means.Count; d++)
            if (means[d - 1] - means[d] < 0.05 - 1e-12)
                misses.Add($"danger {d} to {d + 1}: {Pct(means[d - 1])} to {Pct(means[d])}");
        NoMisses(misses, "B5: a step under 5 points");
    }

    // ---- B7, B8: careers -----------------------------------------------------------------

    [Fact]
    public void B7_TheClimb()
    {
        var careers = Careers.Of(Careers.Player.NeverGrows);
        Line($"B7: {Careers.Count} careers, the two starters in turn, danger 1, no server play: a first trait by run 3 in every career; the median first win by run 40; the median run at which the lineage is complete between 220 and 420");
        CareerTable(careers);
        var misses = new List<string>();
        var lateTrait = careers.Where(c => c.FirstTrait is 0 or > 3).ToList();
        if (lateTrait.Count > 0) misses.Add($"a first trait after run 3 in {lateTrait.Count} careers");
        var firstWin = Careers.Median(careers.Select(c => c.FirstWin == 0 ? int.MaxValue : c.FirstWin));
        if (firstWin > 40) misses.Add($"the median first win at run {firstWin}");
        var complete = Careers.Median(careers.Select(c => c.Complete == 0 ? int.MaxValue : c.Complete));
        if (complete < 220 || complete > 420) misses.Add($"the median complete at run {complete}");
        NoMisses(misses, "B7 out of its bands");
    }

    [Fact]
    public void B8_RotatingIsQuicker()
    {
        var none = Careers.Of(Careers.Player.NeverGrows);
        var rex = Careers.Of(Careers.Player.RexMain);
        var rotates = Careers.Of(Careers.Player.Rotates);
        var tyrannosaurus = Careers.Of(Careers.Player.TyrannosaurusMain);
        var deinosuchus = Careers.Of(Careers.Player.DeinosuchusMain);
        Line($"B8: the same careers with a week's server play fed between runs (seven runs a day): the Rex main is done no later than with no server play, the player who rotates in at most 75% of the Rex main's runs, and a Tyrannosaurus main and a Deinosuchus main who play their own species are each done no later than the player who never grows anything (H67)");
        Line("Rex main (a Tyrannosaurus grown 30% a day, from a fresh save):");
        CareerTable(rex);
        Line("Rotates (a new species each grow, 65% a day; plays every species it has unlocked in turn):");
        CareerTable(rotates);
        Line("Tyrannosaurus main (a Tyrannosaurus grown 30% a day; plays it once unlocked, the starters until then):");
        CareerTable(tyrannosaurus);
        Line("Deinosuchus main (a Deinosuchus grown 30% a day; plays it once unlocked, the starters until then):");
        CareerTable(deinosuchus);
        int Done(Bot.CareerResult[] c) => Careers.Median(c.Select(r => r.Complete == 0 ? int.MaxValue : r.Complete));
        var (n, r, o) = (Done(none), Done(rex), Done(rotates));
        var (t, d) = (Done(tyrannosaurus), Done(deinosuchus));
        Line($"median runs to the whole lineage: never grows {n}, Rex main {r}, rotates {o} ({o / (double)r * 100:F0}% of the Rex main's)");
        Line($"the mains: Tyrannosaurus {t} ({tyrannosaurus.Min(c => c.Complete)} to {tyrannosaurus.Max(c => c.Complete)}), Deinosuchus {d} ({deinosuchus.Min(c => c.Complete)} to {deinosuchus.Max(c => c.Complete)})");
        var misses = new List<string>();
        if (r > n) misses.Add($"the Rex main at {r}, later than {n}");
        if (o > 0.75 * r) misses.Add($"the rotating player at {o}, over 75% of {r}");
        if (t > n) misses.Add($"the Tyrannosaurus main at {t}, later than {n}");
        if (d > n) misses.Add($"the Deinosuchus main at {d}, later than {n}");
        NoMisses(misses, "B8");
    }

    /// <summary>One line a career and the medians: what 5.4's table shows.</summary>
    private void CareerTable(IReadOnlyList<Bot.CareerResult> careers)
    {
        var shares = new[] { 0.10, 0.25, 0.50, 0.75, 1.00 };
        Line("career  first trait  first win  runs at 10/25/50/75/100% of the lineage's cost   wins  pay a run  amber a run");
        for (var i = 0; i < careers.Count; i++)
        {
            var c = careers[i];
            Line($"{i + 1,6} {c.FirstTrait,12} {c.FirstWin,10}   {string.Join(" / ", shares.Select(s => c.RunAtShare(s).ToString()))}{"",-10} {c.Runs.Count(x => x.Won),5} {c.Runs.Average(x => x.Pay),10:F0} {c.Final.Wallet.Earned / (double)c.Runs.Count,12:F0}");
        }
        string Med(Func<Bot.CareerResult, int> f) =>
            $"{Careers.Median(careers.Select(f))} ({careers.Min(f)} to {careers.Max(f)})";
        Line($"median: first trait {Med(c => c.FirstTrait)}, first win {Med(c => c.FirstWin)}, runs at 10/25/50/75/100%: "
            + string.Join(" / ", shares.Select(s => Careers.Median(careers.Select(c => c.RunAtShare(s))))) + $", complete {Med(c => c.Complete)}"
            + $", unfinished {careers.Count(c => c.Complete == 0)}, runs left at the turn limit {careers.Sum(c => c.Runs.Count(x => x.TimedOut))}");
    }

    // ---- B9 to B12 ------------------------------------------------------------------------

    [Fact]
    public void B9_Pay()
    {
        // A run left at turn 0 pays nothing, with Strong start at 3 too (H8).
        var fresh = Island.NewRun(new RunSetup("Dryosaurus", 1, Fresh, 0), SeedOf(0));
        var strong = Island.NewRun(new RunSetup("Dryosaurus", 1, new Dictionary<Trait, int> { [Trait.StrongStart] = 3 }, 0), SeedOf(0));
        var leftFresh = TurnRules.Apply(fresh, new PlayerAction.Leave())!.After.Over!.Total;
        var leftStrong = TurnRules.Apply(strong, new PlayerAction.Leave())!.After.Over!.Total;

        // A fresh win at danger 1 with four predators defeated: grown 0 to 100, every
        // line crossed, four kills, the apex outlasted.
        var won = fresh.Clone();
        won.You.Growth = won.You.Best = Tuning.GrowthMost;
        won.Kills = 4;
        World.End(won, won: true, EndReason.Outlasted, by: null, new List<TurnEvent>());

        // And no line of any of the bot's runs is negative.
        var sweeps = new[] { Of("fresh", 1, FreshRuns), Of("full", 1, FullRuns), Of("full", 5, DangerRuns) };
        var negative = sweeps.Sum(s => s.Values.Sum(r => r.Count(x => !x.NoNegativeLine)));
        var counted = sweeps.Sum(s => s.Values.Sum(r => r.Length));

        Line("B9: a run left at turn 0 pays 0, with Strong start at 3 too; a fresh win at danger 1 with four predators defeated pays 197; no line is negative");
        Line($"left at turn 0: {leftFresh}; with Strong start 3: {leftStrong}");
        Line($"a fresh win, four defeated: {won.Over!.Total} ({string.Join(", ", won.Over.Lines.Select(l => $"{l.Kind} {l.Amount}"))})");
        Line($"runs with a negative line: {negative} of {counted}");
        Assert.Equal(0, leftFresh);
        Assert.Equal(0, leftStrong);
        Assert.Equal(197, won.Over.Total);
        Assert.Equal(0, negative);
    }

    [Fact]
    public void B10_AWeekOnTheServer()
    {
        // B10 is ServerWeekTests' (task H06): run here so the balance's table has it.
        Line("B10: a week on the server, by ServerPlay alone (ServerWeekTests.AWeekOnTheServer):");
        new ServerWeekTests(_output).AWeekOnTheServer();
    }

    [Fact]
    public void B11_TheSameRunTwice()
    {
        Line("B11: a seed and the bot give the same final state twice, for every species (3 seeds, fresh and full)");
        foreach (var s in SpeciesTable.All)
        foreach (var ranks in new[] { Fresh, Full })
        for (var k = 0; k < 3; k++)
        {
            var setup = new RunSetup(s.Name, 1, ranks, 0);
            var once = Runs.Fingerprint(Bot.Play(setup, SeedOf(k)).Final);
            var twice = Runs.Fingerprint(Bot.Play(setup, SeedOf(k)).Final);
            Assert.Equal(once, twice);
        }
        // And played side by side, a sweep is the run played alone.
        var side = Play(Fresh, 1, 8, species: SpeciesTable.All.Take(3).ToList());
        foreach (var s in SpeciesTable.All.Take(3))
            for (var k = 0; k < 8; k++)
                Assert.Equal(Summary(Bot.Play(new RunSetup(s.Name, 1, Fresh, 0), SeedOf(k))), side[s.Name][k]);
        Line("the same, every time");
    }

    [Fact]
    public void B12_NothingBreaks()
    {
        Line($"B12: every species with every single instinct forced at turn 0: {ForcedRuns} runs each end, with no exception");
        Line("instinct    ended  at the limit  adult (all species)");
        var misses = new List<string>();
        foreach (var instinct in Instincts.All)
        {
            var runs = Play(Fresh, 1, ForcedRuns, run =>
            {
                run.Instincts.Add(instinct);   // the run is this test's own: forced, as a pick at turn 0 would
                return run;
            });
            var all = runs.Values.SelectMany(r => r).ToList();
            var limit = all.Count(x => x.TimedOut);
            Line($"{instinct,-10} {all.Count - limit,6} {limit,13}  {Pct(Share(all, x => x.ReachedAdult))}");
            if (limit > 0) misses.Add($"{instinct}: {limit} runs did not end");
        }
        NoMisses(misses, "B12");
    }

    // ---- B13: the middle of the climb ---------------------------------------------------

    /// <summary>B13: runs a species with a third of the lineage.</summary>
    private const int ThirdRuns = 200;

    /// <summary>
    /// B13, nobody is left behind (H68). With a third of the lineage
    /// (<see cref="Sweeps.Third"/>) the builds are not level: the big ones win nearly
    /// every run, the small ones half or fewer. That is ruled as it is; this test holds
    /// the floor under it, so no species falls back below it unseen.
    /// </summary>
    [Fact]
    public void B13_NobodyIsLeftBehind()
    {
        var runs = Of("third", 1, ThirdRuns);
        Line($"B13: a third of the lineage (14,780 of 42,180), danger 1, {ThirdRuns} runs a species: every species reaches Adult in at least 85% and wins at least 30%");
        Line("species              build     adult    won   mean pay");
        var misses = new List<string>();
        foreach (var s in SpeciesTable.All)
        {
            var r = runs[s.Name];
            var adult = Share(r, x => x.ReachedAdult);
            var won = Share(r, x => x.Won);
            var miss = adult < 0.85 || won < 0.30;
            Line($"{s.Name,-20} {s.Build,-8} {Pct(adult)} {Pct(won)} {r.Average(x => x.Pay),10:F1}{(miss ? "  MISS" : "")}");
            if (miss) misses.Add($"{s.Name} adult {Pct(adult)} won {Pct(won)}");
        }
        NoMisses(misses, "B13 under its floor");
    }
}
