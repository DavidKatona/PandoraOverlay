namespace PandoraOverlay;

public enum FeedKind { Roster, Spawned, Left, NewLife, DinoChanged, Growth, Fracture, Nearby, LowStat, Damage, Prime }

/// <summary>One feed line: when, what, whose (a friend's steamId, or Mine for your own events; neither for roster-wide lines).</summary>
public sealed record FeedLine(DateTime AtUtc, string Text, string? SteamId, FeedKind Kind, bool Mine = false);

/// <summary>
/// The Activity widget's line store — pure and tested. Anyone with a line
/// posts it (FriendFeed for friends, SelfActivity for you); the widget
/// renders newest first and ages the lines: ten minutes, then gone. Owned
/// by MainWindow, so the last ten minutes survive the widget being hidden
/// and shown again.
/// </summary>
public sealed class ActivityLog
{
    public const int Capacity = 50;
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    private readonly List<FeedLine> _lines = new();

    /// <summary>Lines, newest first, unexpired as of the last Expire.</summary>
    public IReadOnlyList<FeedLine> Lines => _lines;

    /// <summary>Raised after each Post with the lines just added, oldest first — the widget wakes and chimes on it.</summary>
    public event Action<IReadOnlyList<FeedLine>>? Posted;

    public void Post(FeedLine line) => Post(new[] { line });

    /// <summary>Adds a batch, keeping its order: the batch's first line ends up on top.</summary>
    public void Post(IReadOnlyList<FeedLine> batch)
    {
        if (batch.Count == 0) return;
        for (var i = batch.Count - 1; i >= 0; i--) _lines.Insert(0, batch[i]);
        if (_lines.Count > Capacity) _lines.RemoveRange(Capacity, _lines.Count - Capacity);
        Posted?.Invoke(batch);
    }

    /// <summary>Drops lines older than Expiry; true when something was removed.</summary>
    public bool Expire(DateTime now) => _lines.RemoveAll(l => now - l.AtUtc >= Expiry) > 0;

    /// <summary>1 for a fresh line, easing to 0.35 as it approaches expiry — the widget's per-line opacity.</summary>
    public static double AgeOpacity(FeedLine line, DateTime now)
    {
        var age = Math.Clamp((now - line.AtUtc).TotalSeconds / Expiry.TotalSeconds, 0, 1);
        return 1 - 0.65 * age;
    }
}
