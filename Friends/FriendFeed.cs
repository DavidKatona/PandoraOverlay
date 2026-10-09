namespace PandoraOverlay;

/// <summary>
/// The friends half of the Activity feed — pure and tested: diffs each
/// friends roster against the previous one and turns what changed into
/// short lines for the ActivityLog. A friend spawned in (or started a fresh
/// dino of the same species — growth lower than last seen, a fact, not a
/// death claim), left the game (neutral wording: logout, restart and death
/// all look the same from outside; several at once become one line),
/// changed dino, crossed a growth stage, took a fracture, came within a
/// couple of hundred metres, joined or left the roster. Friends muted in
/// the FriendBook produce no lines but are tracked all the same, so
/// unmuting later starts from the truth. Also the roster facts the
/// widget's header shows. Nothing here knows about WPF.
/// </summary>
public sealed class FriendFeed
{
    private const double NearMeters = 200;   // "is nearby" once inside this…
    private const double FarMeters = 300;    // …re-armed once outside this, so a friend at the line can't spam
    private const int NamesInRosterLine = 3;

    /// <summary>Roster facts, as derived from the last roster the feed saw — the header's numbers.</summary>
    public int Total { get; private set; }
    public int InGame { get; private set; }

    /// <summary>Names of the friends in game right now (display names) — the quiet-state line.</summary>
    public IReadOnlyList<string> InGameNames => _inGameNames;

    /// <summary>True once a roster has been seen this session.</summary>
    public bool HasRoster => _last is not null;

    private readonly List<string> _inGameNames = new();
    private Dictionary<string, FriendState>? _last;                 // the previous roster, by steamId; null before the first
    private readonly Dictionary<string, FriendState> _lastLive = new(); // each friend's last IN-GAME state (dino/growth for the fresh-life rule)
    private readonly Dictionary<string, GrowthMilestones> _growth = new();
    private readonly HashSet<string> _near = new();

    /// <summary>Forget the comparison baseline (not the lines): the next roster seeds again instead of reporting everyone as new.</summary>
    public void ResetBaseline()
    {
        _last = null;
        _near.Clear();
    }

    /// <summary>
    /// Feeds one roster (self already filtered out by PollService). Returns
    /// the lines this roster produced, oldest first, for the caller to post
    /// to the ActivityLog. With areaAt (world cm → the named area there, or
    /// null) a spawn line also says WHERE: "… spawned as Deino 42% · Swamps"
    /// — only for a friend who shares their location, and only where an
    /// area is named.
    /// </summary>
    public IReadOnlyList<FeedLine> Update(IReadOnlyList<FriendState> roster, PlayerState? me,
                                          Func<string, string?, string> nameOf, Func<string, bool> notify, DateTime now,
                                          Func<double, double, string?>? areaAt = null)
    {
        var current = new Dictionary<string, FriendState>();
        foreach (var f in roster)
        {
            if (!string.IsNullOrWhiteSpace(f.SteamId)) current.TryAdd(f.SteamId, f);
        }
        Total = current.Count;
        InGame = current.Values.Count(f => f.InGame);
        _inGameNames.Clear();
        _inGameNames.AddRange(current.Values.Where(f => f.InGame).Select(f => nameOf(f.SteamId!, f.Name)));

        var fresh = new List<FeedLine>();
        var left = new List<(string Id, string Name)>();

        if (_last is null)
        {
            // First sight: one line saying who is on, so the widget never starts blank.
            if (InGame > 0)
            {
                fresh.Add(new FeedLine(now, RosterLine(current.Values.Where(f => f.InGame), nameOf), null, FeedKind.Roster));
            }
        }
        else
        {
            foreach (var (id, c) in current)
            {
                var speak = notify(id);
                var name = nameOf(id, c.Name);
                if (!_last.TryGetValue(id, out var p))
                {
                    if (speak) fresh.Add(new FeedLine(now, $"New friend: {name}", id, FeedKind.Roster));
                    continue;
                }

                if (c.InGame && !p.InGame)
                {
                    if (speak) fresh.Add(SpawnLine(id, name, c, now, areaAt));
                }
                else if (!c.InGame && p.InGame)
                {
                    if (speak) left.Add((id, name));
                }
                else if (c.InGame && p.InGame)
                {
                    if (c.Dino is not null && p.Dino is not null && !string.Equals(c.Dino, p.Dino, StringComparison.OrdinalIgnoreCase))
                    {
                        if (speak) fresh.Add(new FeedLine(now, $"{name} is now a {c.Dino} {Pct(c.Growth)}", id, FeedKind.DinoChanged));
                    }
                    else if (speak)
                    {
                        if (!p.HeadFractured && c.HeadFractured) fresh.Add(new FeedLine(now, $"{name} fractured their head", id, FeedKind.Fracture));
                        if (!p.BodyFractured && c.BodyFractured) fresh.Add(new FeedLine(now, $"{name} fractured their body", id, FeedKind.Fracture));
                        if (!p.LegsFractured && c.LegsFractured) fresh.Add(new FeedLine(now, $"{name} fractured their legs", id, FeedKind.Fracture));
                    }
                }
            }
            foreach (var (id, p) in _last)
            {
                if (!current.ContainsKey(id) && notify(id))
                {
                    fresh.Add(new FeedLine(now, $"{nameOf(id, p.Name)} is no longer on your friends list", id, FeedKind.Roster));
                }
            }
            // Several at once (a server restart) become one line, so the feed isn't flooded.
            if (left.Count > 0) fresh.Add(new FeedLine(now, LeftLine(left.Select(l => l.Name).ToList()), left.Count == 1 ? left[0].Id : null, FeedKind.Left));
        }

        // Growth stages and proximity run for every in-game friend, muted or
        // not, so the baseline is right the moment they are unmuted — and a
        // friend already beside you at launch, or spawning in next to you, is
        // primed rather than announced as "nearby" one fetch later.
        foreach (var (id, c) in current)
        {
            if (!c.InGame) continue;
            var wasLive = _last is not null && _last.TryGetValue(id, out var was) && was.InGame;
            var speak = wasLive && notify(id);
            if (!_growth.TryGetValue(id, out var tracker)) _growth[id] = tracker = new GrowthMilestones();
            if (tracker.Update($"{id}|{c.Dino}|{c.Gender}", c.Growth) is { } stage && speak)
            {
                fresh.Add(new FeedLine(now, $"{nameOf(id, c.Name)}'s {c.Dino ?? "dino"} is now {GrowthMilestones.StageName(stage)}", id, FeedKind.Growth));
            }
            if (Proximity(id, c, me) is { } meters && speak)
            {
                fresh.Add(new FeedLine(now, $"{nameOf(id, c.Name)} is nearby · {meters:0} m", id, FeedKind.Nearby));
            }
        }

        foreach (var (id, c) in current)
        {
            if (c.InGame) _lastLive[id] = c;
            else _near.Remove(id);
        }
        foreach (var id in _lastLive.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _lastLive.Remove(id);
            _growth.Remove(id);
            _near.Remove(id);
        }
        _last = current;
        return fresh;
    }

    // ---- Line builders ---------------------------------------------------------

    private FeedLine SpawnLine(string id, string name, FriendState c, DateTime now, Func<double, double, string?>? areaAt)
    {
        // Where: only for a friend the site lets us place (in game, location shared), and only a named area.
        // It goes last in the line, so on a long name it is what the slot's ellipsis cuts.
        var where = areaAt is not null && c.OnMap && areaAt(c.X!.Value, c.Y!.Value) is { } area ? $" · {area}" : "";

        // Same species, lower growth than the last time we saw them alive:
        // a new life of that dino. Not "died" — only the growth drop is known.
        if (_lastLive.TryGetValue(id, out var live) && c.Dino is not null &&
            string.Equals(live.Dino, c.Dino, StringComparison.OrdinalIgnoreCase) && c.Growth < live.Growth - 0.001)
        {
            return new FeedLine(now, $"{name} started a fresh {c.Dino} {Pct(c.Growth)}{where}", id, FeedKind.NewLife);
        }
        return new FeedLine(now, (c.Dino is null ? $"{name} is in game" : $"{name} spawned as {c.Dino} {Pct(c.Growth)}") + where, id, FeedKind.Spawned);
    }

    private double? Proximity(string id, FriendState c, PlayerState? me)
    {
        // HideLocation is the friend's consent, given on the site: their
        // coordinates, should any arrive, are never used — not even for a
        // distance that names no spot.
        if (me is null || c.X is not { } x || c.Y is not { } y || c.HideLocation)
        {
            return null;
        }
        var meters = WaypointLibrary.Distance(x, y, me.X, me.Y) / 100;
        if (_near.Contains(id))
        {
            if (meters > FarMeters) _near.Remove(id);
            return null;
        }
        if (meters >= NearMeters) return null;
        _near.Add(id);
        return meters;
    }

    private static string RosterLine(IEnumerable<FriendState> inGame, Func<string, string?, string> nameOf)
    {
        var names = inGame.Select(f => $"{nameOf(f.SteamId!, f.Name)}{(f.Dino is null ? "" : $" ({f.Dino})")}").ToList();
        var shown = string.Join(", ", names.Take(NamesInRosterLine));
        var more = names.Count - NamesInRosterLine;
        return more > 0 ? $"In game: {shown} and {more} more" : $"In game: {shown}";
    }

    private static string LeftLine(List<string> names)
    {
        // Neutral on purpose: a logout, a server restart and a death look the
        // same from outside, so the line never says which.
        return names.Count switch
        {
            1 => $"{names[0]} is no longer in game",
            2 => $"{names[0]} and {names[1]} are no longer in game",
            _ => $"{names[0]}, {names[1]} and {names.Count - 2} more are no longer in game"
        };
    }

    private static string Pct(double growth) => $"{Math.Clamp(growth, 0, 1) * 100:0}%";
}
