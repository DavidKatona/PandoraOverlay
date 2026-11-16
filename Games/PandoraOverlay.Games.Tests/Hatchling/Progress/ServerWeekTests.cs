using PandoraOverlay.Games.Hatchling;
using Xunit;
using Xunit.Abstractions;
using static PandoraOverlay.Games.Tests.ProgressSamples;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// B10, a week on the server, by <see cref="ServerPlay"/> alone on a manual
/// calendar: what the numbers do to a Rex main and to a player who rotates (G6,
/// the public promise "playing something different just pays more", and "Rex and
/// Deino players lose nothing"). Seven days; each day's steps (0.1% each) at one
/// moment, the calendar one day on between two days; a life that reaches 100% is
/// followed by a new one at 0. Each sum is printed, so a failure shows the numbers.
/// </summary>
public class ServerWeekTests
{
    private const int Days = 7;

    private readonly ITestOutputHelper _output;

    public ServerWeekTests(ITestOutputHelper output) => _output = output;

    /// <summary>What a week paid, split: growth, the journal's rewards, the achievements.</summary>
    private sealed record Week(long Growth, long Journal, long Achievements)
    {
        public long Total => Growth + Journal + Achievements;
    }

    /// <summary>
    /// Plays a week from <paramref name="state"/>: each day <paramref name="perMilleADay"/>
    /// thousandths of growth, of <paramref name="speciesFor"/>(life) (the life counted
    /// from 0), at the day's moment. What every step paid is split by what the step
    /// wrote: the journal's lines are the newest of the step's lines, one per entry
    /// finished, and the achievements are those it added.
    /// </summary>
    private static Week Play(GameState state, int perMilleADay, Func<int, string> speciesFor)
    {
        long growth = 0, journal = 0, achievements = 0;
        int life = 0, grownPerMille = 0;
        for (var day = 0; day < Days; day++)
        {
            var moment = Moment.AddDays(day);
            for (var i = 0; i < perMilleADay; i++)
            {
                var result = Server.Apply(state, Server.Step(speciesFor(life), grownPerMille / 1000.0, (grownPerMille + 1) / 1000.0), moment);
                var next = result.State;

                var finished = next.Journal.Done - state.Journal.Done;
                long fromJournal = next.Lately.Take(finished).Sum(l => (long)l.Amount);
                Assert.All(next.Lately.Take(finished), l => Assert.Equal(LatelyKind.Journal, l.Kind));
                long fromAchievements = next.Achievements.Unlocked.Skip(state.Achievements.Unlocked.Count).Sum(id => (long)PayOf(id));
                journal += fromJournal;
                achievements += fromAchievements;
                growth += next.Wallet.Earned - state.Wallet.Earned - fromJournal - fromAchievements;

                state = next;
                if (++grownPerMille == 1000)
                {
                    grownPerMille = 0;
                    life++;
                }
            }
        }
        return new Week(growth, journal, achievements);
    }

    /// <summary>(a) Only ever a Tyrannosaurus, its record worn to x1.0 when the week begins, 30% a day.</summary>
    private static Week RexMain()
    {
        var state = GameState.Fresh(Moment);
        state.RecordToChange("Tyrannosaurus").Fresh = Tuning.FreshFloor;
        state.RecordToChange("Tyrannosaurus").Grown = 1000;   // a long Rex career before this week
        return Play(state, 300, _ => "Tyrannosaurus");
    }

    /// <summary>(b) A new species each grow, in the table's order, each fresh, 65% a day.</summary>
    private static Week Rotating() =>
        Play(GameState.Fresh(Moment), 650, life => SpeciesTable.All[life].Name);

    /// <summary>(c) The same Tyrannosaurus, 30% a day, from a fresh save.</summary>
    private static Week RexFromFresh() => Play(GameState.Fresh(Moment), 300, _ => "Tyrannosaurus");

    [Fact]
    public void AWeekOnTheServer()
    {
        var rex = RexMain();
        var rotating = Rotating();
        var fresh = RexFromFresh();

        _output.WriteLine($"(a) a Tyrannosaurus worn to x1.0, 30% a day: {rex.Growth} from growth ({rex.Total} in all)");
        _output.WriteLine($"(b) a new species each grow, 65% a day: {rotating.Growth} from growth, {rotating.Journal} from the journal, {rotating.Achievements} from achievements ({rotating.Total} in all)");
        _output.WriteLine($"(c) the same Tyrannosaurus from a fresh save: {fresh.Growth} from growth ({fresh.Total} in all)");
        _output.WriteLine($"(b) / (a), growth: {rotating.Growth / (double)rex.Growth:F2}");

        Assert.InRange(rex.Growth, 860, 895);          // 877 by the rules
        Assert.Equal(0, rex.Journal);                  // the journal never asks for it
        Assert.InRange(rotating.Growth, 3940, 4010);   // 3,975 by the rules
        Assert.True(rotating.Growth >= 4 * rex.Growth, "playing something different pays at least four times as much");
        Assert.True(fresh.Growth > rex.Growth, "a Rex player is never paid less than the worn rate");
    }

    [Fact]
    public void AWeekIsTheSameTwice()
    {
        // Nothing but the steps and the calendar's moments go in: the same week pays the same.
        Assert.Equal(Rotating(), Rotating());
        Assert.Equal(RexMain(), RexMain());
    }
}
