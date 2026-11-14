using System.Collections.Concurrent;
using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The careers of B7 and B8, and the plan's table of them: three players, nine
/// careers each, played by <see cref="Bot.Career"/> through the game's own rules
/// (settling, achievements, buying, server play) at danger 1, the bot buying the
/// cheapest rank after every run.
/// <list type="bullet">
/// <item><b>Never grows anything</b>: the two starters in turn, no server play (B7).</item>
/// <item><b>The Rex main</b>: the same runs, and between them a Tyrannosaurus grown
/// 30% a day on the server (B10's Rex main, from a fresh save).</item>
/// <item><b>Rotates</b>: every species it has unlocked in turn (the starters, then
/// each one its server play grows to Adult), and between runs a new species each
/// grow, in the table's order, 65% a day (B10's rotating player).</item>
/// </list>
/// Seven runs a day: the server's day is cut into seven equal slices of 0.1% steps,
/// one after each run, on a manual calendar a day on every seven runs. Career
/// <c>c</c> (1 to 9) starts from a fresh save whose first seed is
/// <c>Rng.Mix(CareerSeed, c)</c>, then the save's own seeds; played once per
/// process, side by side, each from its own seed alone.
/// </summary>
internal static class Careers
{
    /// <summary>Careers a player.</summary>
    public const int Count = 9;

    /// <summary>The most runs a career may take before it is reported unfinished.</summary>
    public const int MostRuns = 2000;

    /// <summary>Runs in a day of play (the plan's seven).</summary>
    public const int RunsADay = 7;

    /// <summary>The careers' seeds' stream: apart from the sweeps' (<see cref="Sweeps.BaseSeed"/> salted).</summary>
    public static readonly uint CareerSeed = Rng.Mix(Sweeps.BaseSeed, 0xCA2EE2u);

    /// <summary>The three players.</summary>
    public enum Player { NeverGrows, RexMain, Rotates }

    private static readonly ConcurrentDictionary<Player, Lazy<Bot.CareerResult[]>> Cache = new();

    /// <summary>The nine careers of <paramref name="player"/>, played once per process.</summary>
    public static Bot.CareerResult[] Of(Player player) =>
        Cache.GetOrAdd(player, p => new Lazy<Bot.CareerResult[]>(() => Play(p))).Value;

    private static Bot.CareerResult[] Play(Player player)
    {
        var results = new Bot.CareerResult[Count];
        Parallel.For(0, Count, i =>
        {
            var start = GameState.Fresh(ProgressSamples.Moment);
            start.NextSeed = Rng.Mix(CareerSeed, (uint)(i + 1));
            results[i] = player switch
            {
                Player.NeverGrows => Bot.Career(start, Starters, Dangers.Lowest, MostRuns),
                Player.RexMain => Bot.Career(start, Starters, Dangers.Lowest, MostRuns,
                    new ServerFeed(300, _ => "Tyrannosaurus").Between),
                _ => Bot.Career(start, Unlocked, Dangers.Lowest, MostRuns,
                    new ServerFeed(650, life => SpeciesTable.All[life % SpeciesTable.All.Count].Name).Between),
            };
        });
        return results;
    }

    /// <summary>The two starters in turn.</summary>
    public static string Starters(GameState state, int k) => SpeciesTable.Starters[k % SpeciesTable.Starters.Count];

    /// <summary>Every species unlocked, in the order they were, in turn.</summary>
    public static string Unlocked(GameState state, int k) => state.Unlocked[k % state.Unlocked.Count];

    /// <summary>The median of nine (the fifth), or of any count: the lower middle.</summary>
    public static int Median(IEnumerable<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }

    /// <summary>
    /// Server play fed between runs (B8): <paramref name="perMilleADay"/> steps of 0.1%
    /// a day of <paramref name="speciesFor"/>(life), a life followed by a new one at 0
    /// when it reaches 100%; the day cut into <see cref="RunsADay"/> slices, one after
    /// each run, every step of a day at that day's moment. Through
    /// <see cref="ServerPlay.Apply"/>, the only way server play reaches the game. One
    /// instance a career: it counts the lives and the growth itself.
    /// </summary>
    internal sealed class ServerFeed
    {
        private readonly int _perMilleADay;
        private readonly Func<int, string> _speciesFor;
        private int _life;
        private int _grown;   // thousandths of this life

        public ServerFeed(int perMilleADay, Func<int, string> speciesFor)
        {
            _perMilleADay = perMilleADay;
            _speciesFor = speciesFor;
        }

        /// <summary>The slice of the day after run <paramref name="k"/> (0 first).</summary>
        public GameState Between(GameState state, int k)
        {
            var day = k / RunsADay;
            var slice = k % RunsADay;
            var steps = (slice + 1) * _perMilleADay / RunsADay - slice * _perMilleADay / RunsADay;
            var moment = ProgressSamples.Moment.AddDays(day);
            for (var i = 0; i < steps; i++)
            {
                var step = new SeenStep(_speciesFor(_life), "", _grown / 1000.0, (_grown + 1) / 1000.0);
                state = ServerPlay.Apply(state, step, moment).State;
                if (++_grown == 1000)
                {
                    _grown = 0;
                    _life++;
                }
            }
            return state;
        }
    }
}
