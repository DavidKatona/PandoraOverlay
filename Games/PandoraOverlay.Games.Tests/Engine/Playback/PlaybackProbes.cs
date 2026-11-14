using System.Globalization;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A step that writes every call it receives into a shared log
/// ("A.Begin", "A.Advance", "A.Finish"), finishes by itself on its n-th
/// advance (never, when n is null), and can run a test's own code inside any
/// of its calls: for the call-order, overrun, re-entrancy and fault tests.
/// It draws nothing, so it is not held to IStep's rule; the stock steps are,
/// in <see cref="SkipEqualsRunTests"/>.
/// </summary>
internal sealed class ProbeStep : IStep
{
    private readonly List<string> _log;
    private readonly int? _finishOn;

    public ProbeStep(string name, List<string> log, int? finishOn = 1)
    {
        Name = name;
        _log = log;
        _finishOn = finishOn;
    }

    /// <summary>A probe that never finishes by itself: the broken step.</summary>
    public static ProbeStep Never(string name, List<string> log) => new(name, log, null);

    public string Name { get; }
    public int Begins { get; private set; }
    public int Advances { get; private set; }
    public int Finishes { get; private set; }

    public Action? OnBegin { get; set; }
    public Action? OnAdvance { get; set; }
    public Action? OnFinish { get; set; }

    public void Begin()
    {
        _log.Add(Name + ".Begin");
        Begins++;
        OnBegin?.Invoke();
    }

    public bool Advance()
    {
        _log.Add(Name + ".Advance");
        Advances++;
        OnAdvance?.Invoke();
        return _finishOn is int n && Advances >= n;
    }

    public void Finish()
    {
        _log.Add(Name + ".Finish");
        Finishes++;
        OnFinish?.Invoke();
    }
}

/// <summary>
/// What the steps under test "draw": named numbers, as a game's steps set a
/// sprite's place or a bar's value. <see cref="Picture"/> is the whole of it
/// as one comparable string, and <see cref="Writes"/> counts every write, so
/// a test can tell that nothing was drawn after a step's end.
/// </summary>
internal sealed class Scene
{
    private const string CallPrefix = "call ";

    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);

    public int Writes { get; private set; }

    public void Set(string key, double value)
    {
        _values[key] = value;
        Writes++;
    }

    /// <summary>One more run of the call named <paramref name="key"/>: a call that ran twice shows 2.</summary>
    public void Bump(string key)
    {
        var name = CallPrefix + key;
        Set(name, _values.TryGetValue(name, out var n) ? n + 1 : 1);
    }

    /// <summary>How many times each call ran: every one must be 1.</summary>
    public IEnumerable<double> CallRuns => _values.Where(kv => kv.Key.StartsWith(CallPrefix, StringComparison.Ordinal)).Select(kv => kv.Value);

    public double Get(string key) => _values[key];

    public string Picture => string.Join("; ", _values
        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
}

/// <summary>Running a playback tick by tick and reading what each tick did.</summary>
internal static class PlaybackRun
{
    /// <summary>Far beyond any test's playback: a runaway loop fails instead of hanging the run.</summary>
    private const int SafetyTicks = 10_000;

    /// <summary>
    /// Advances until idle, and answers what each tick wrote to the log, one
    /// string per tick ("A.Begin A.Advance").
    /// </summary>
    public static List<string> ToEnd(Playback playback, List<string> log)
    {
        var ticks = new List<string>();
        for (var i = 0; i < SafetyTicks && !playback.Idle; i++)
            ticks.Add(OneTick(playback, log));
        Assert.True(playback.Idle, "the playback never ran empty");
        return ticks;
    }

    /// <summary>One <see cref="Playback.Advance"/>, and what it wrote to the log.</summary>
    public static string OneTick(Playback playback, List<string> log)
    {
        var before = log.Count;
        playback.Advance();
        return string.Join(" ", log.Skip(before));
    }

    /// <summary>Advances until idle and answers how many ticks it took.</summary>
    public static int TicksToEnd(Playback playback)
    {
        var ticks = 0;
        while (!playback.Idle && ticks < SafetyTicks)
        {
            playback.Advance();
            ticks++;
        }
        Assert.True(playback.Idle, "the playback never ran empty");
        return ticks;
    }

    /// <summary>A playback whose <see cref="Playback.Drained"/> and <see cref="Playback.StepOverran"/> write to the log.</summary>
    public static Playback Logged(List<string> log)
    {
        var playback = new Playback();
        playback.Drained += () => log.Add("Drained");
        playback.StepOverran += () => log.Add("Overran");
        return playback;
    }
}
