namespace PandoraOverlay;

/// <summary>
/// Estimates how long a draining stat (hunger, thirst) lasts at the current
/// rate, from the poll stream — GrowthTracker's endpoint-slope idea over a
/// shorter window, so a changed rate shows within minutes. (What changes
/// the rate in-game is unverified; the tracker just measures it.)
/// The drain RATE survives a refill: eating or drinking moves the level, not
/// the metabolism, so the estimate is back on the next poll instead of after
/// a fresh baseline (which then replaces it). Session-only; resets on
/// death/dino swap and on leaving the game.
/// </summary>
public sealed class DrainTracker
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinBaseline = TimeSpan.FromMinutes(3);
    private const double MinDelta = 0.002;   // the API carries 3 decimals — below this the slope is quantization noise
    private const double RefillStep = 0.002; // a rise this large between two polls = ate / drank

    // Far-out estimates are clutter, not information (owner's call, Sep 2026:
    // 3 h was tried first): the label appears once the stat has under
    // ShowBelow left and only leaves again above HideAbove — the gap keeps
    // it from blinking while the estimate hovers at the edge.
    private static readonly TimeSpan ShowBelow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan HideAbove = TimeSpan.FromMinutes(65);

    private readonly Func<PlayerState, double> _stat;
    private readonly List<(DateTime At, double Value)> _samples = new();
    private string? _identity;
    private double _lastGrowth;
    private double? _perHour;
    private bool _shown;

    public DrainTracker(Func<PlayerState, double> stat) => _stat = stat;

    /// <summary>Time until the stat hits zero at the measured rate; null while unknown or not draining.</summary>
    public TimeSpan? TimeLeft { get; private set; }

    /// <summary>Bar label ("~40m", "&lt;1m"), or null when there is nothing worth showing.</summary>
    public string? Label { get; private set; }

    /// <summary>Call when leaving the game (or on death/dino swap): the rate belonged to that life.</summary>
    public void Reset()
    {
        _samples.Clear();
        _identity = null;
        _perHour = null;
        _shown = false;
        TimeLeft = null;
        Label = null;
    }

    public void Add(PlayerState p) => Add(p, DateTime.UtcNow);

    /// <summary>Timestamped overload so tests can simulate minutes in microseconds.</summary>
    internal void Add(PlayerState p, DateTime now)
    {
        var identity = $"{p.SteamId}|{p.Dino}|{p.Gender}";
        if (identity != _identity || p.Growth < _lastGrowth - 0.001)
        {
            Reset(); // died, rerolled, or swapped dinos
        }
        _identity = identity;
        _lastGrowth = p.Growth;

        var value = Math.Clamp(_stat(p), 0, 1);
        if (_samples.Count > 0 && value > _samples[^1].Value + RefillStep)
        {
            _samples.Clear(); // refilled: new baseline, the known rate carries over meanwhile
        }

        _samples.Add((now, value));
        while (now - _samples[0].At > Window)
        {
            _samples.RemoveAt(0);
        }

        var span = _samples[^1].At - _samples[0].At;
        if (span >= MinBaseline)
        {
            var delta = _samples[0].Value - value;
            _perHour = delta >= MinDelta ? delta / span.TotalHours : null; // a full flat baseline = not draining
        }

        TimeLeft = _perHour is { } rate && value > 0 ? TimeSpan.FromHours(value / rate) : null;
        _shown = TimeLeft is { } left && left <= (_shown ? HideAbove : ShowBelow);
        Label = _shown ? Format(TimeLeft!.Value) : null;
    }

    private static string Format(TimeSpan left)
    {
        if (left.TotalMinutes < 1) return "<1m";
        return left.TotalHours < 1
            ? $"~{(int)left.TotalMinutes}m"
            : $"~{(int)left.TotalHours}h {left.Minutes:00}m";
    }
}
