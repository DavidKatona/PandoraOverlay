namespace PandoraOverlay;

/// <summary>
/// When to chime for low hunger or thirst — pure and tested; MainWindow plays
/// the sound. A stat chimes once as it drops under the threshold, then again
/// every few minutes while it stays there (the AFK grower who missed the first
/// one), and re-arms only once it has climbed clear of the line, so a stat
/// hovering there can't chime on every poll. Each stat is tracked on its own.
/// Resets on death/dino swap: a new life starts armed, so spawning in hungry
/// chimes at once.
/// </summary>
public sealed class LowStatAlert
{
    private const double Threshold = 0.20;
    private const double Rearm = 0.30;
    private static readonly TimeSpan Repeat = TimeSpan.FromMinutes(5);

    private sealed class Tracked
    {
        public Tracked(Func<PlayerState, double> read) => Read = read;
        public Func<PlayerState, double> Read { get; }
        public bool Below;          // under the threshold and already chimed
        public DateTime NextRepeat;
    }

    private readonly Tracked[] _stats = { new(p => p.Hunger), new(p => p.Thirst) };
    private string? _identity;
    private double _lastGrowth;

    public void Reset()
    {
        _identity = null;
        foreach (var s in _stats) s.Below = false;
    }

    public bool Update(PlayerState p) => Update(p, DateTime.UtcNow);

    /// <summary>Timestamped overload for the tests. True = chime now.</summary>
    internal bool Update(PlayerState p, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001) Reset();
        _identity = identity;
        _lastGrowth = p.Growth;

        var chime = false;
        foreach (var s in _stats)
        {
            var value = s.Read(p);
            if (!s.Below)
            {
                if (value >= Threshold) continue;
                s.Below = true;
                s.NextRepeat = now + Repeat;
                chime = true;
            }
            else if (value >= Rearm)
            {
                s.Below = false; // ate / drank: the next drop is news again
            }
            else if (now >= s.NextRepeat)
            {
                s.NextRepeat = now + Repeat;
                chime = true;
            }
        }
        return chime;
    }
}
