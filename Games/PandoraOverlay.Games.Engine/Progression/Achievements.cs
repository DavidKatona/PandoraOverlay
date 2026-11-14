namespace PandoraOverlay.Games.Engine;

/// <summary>
/// One achievement, as the game defines it in its code (G22: achievements are
/// per game, and shown only inside the Games window).
/// </summary>
/// <param name="Id">The game's id for it: what the save keeps. Never change a shipped one, or the players who had it lose it.</param>
/// <param name="Title">Its name, for the game to show.</param>
/// <param name="Text">What it asks, for the game to show.</param>
/// <param name="Reward">What granting it pays: nothing, a little currency or a cosmetic, never power (<see cref="AchievementReward"/>).</param>
public sealed record AchievementDef(string Id, string Title, string Text, AchievementReward Reward);

/// <summary>
/// A game's achievements (G22): its definitions, and which ones the player has,
/// in the order they were granted.
///
/// <para>The engine's because every game must keep the same two rules, and
/// keeping them in one place is how they stay kept (A21): an achievement pays
/// only what <see cref="AchievementReward"/> allows, and it pays once. Which
/// achievements a game has and when it grants them are the game's.</para>
///
/// <para>The definitions are the game's code, so a mistake in them (two with one
/// id, a missing reward, a grant of an id that has no definition) is a build
/// defect and throws, for the game's tests to find. The unlocked list is the
/// save's, so it is taken as it comes: an id no definition has any more (an
/// achievement a later version dropped) is kept, so the save round-trips and a
/// version that brings the achievement back finds it still had; it just isn't
/// <see cref="Has">had</see> meanwhile.</para>
/// </summary>
public sealed class Achievements
{
    private readonly AchievementDef[] _all;
    private readonly Dictionary<string, AchievementDef> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _unlocked = new();
    private readonly HashSet<string> _unlockedSet = new(StringComparer.Ordinal);

    /// <summary>
    /// The game's <paramref name="definitions"/> and the save's
    /// <paramref name="unlocked"/> list (none for a new game).
    /// <para>Throws when a definition is null, has a blank id or no reward, or
    /// shares its id with another (ids are compared exactly, as the save keeps
    /// them). From <paramref name="unlocked"/>, a blank entry is dropped and a
    /// repeated id is kept once, at its first place: a save can't be fixed by
    /// throwing, and neither can mean anything.</para>
    /// </summary>
    public Achievements(IReadOnlyList<AchievementDef> definitions, IEnumerable<string>? unlocked = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _all = new AchievementDef[definitions.Count];
        for (var i = 0; i < definitions.Count; i++)
        {
            var d = definitions[i]
                ?? throw new ArgumentException($"Achievement definition {i} is null.", nameof(definitions));
            if (string.IsNullOrWhiteSpace(d.Id))
                throw new ArgumentException($"Achievement definition {i} has no id.", nameof(definitions));
            if (d.Reward is null)
                throw new ArgumentException($"Achievement \"{d.Id}\" has no reward (AchievementReward.None is the reward of nothing).", nameof(definitions));
            if (!_byId.TryAdd(d.Id, d))
                throw new ArgumentException($"Two achievements have the id \"{d.Id}\".", nameof(definitions));
            _all[i] = d;
        }

        if (unlocked is null) return;
        foreach (var id in unlocked)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (_unlockedSet.Add(id)) _unlocked.Add(id);
        }
    }

    /// <summary>Every definition, in the game's order.</summary>
    public IReadOnlyList<AchievementDef> All => _all;

    /// <summary>
    /// The ids the player has, in the order granted: what the save keeps. It
    /// includes any id the save held that no definition has now (see the
    /// summary).
    /// </summary>
    public IReadOnlyList<string> Unlocked => _unlocked;

    /// <summary>
    /// True when the player has the achievement: it is unlocked AND the game
    /// still defines it. An unknown or null id is false, never an error (a
    /// view may ask about anything).
    /// </summary>
    public bool Has(string id) =>
        id is not null && _unlockedSet.Contains(id) && _byId.ContainsKey(id);

    /// <summary>
    /// Grants the achievement <paramref name="id"/>.
    /// <list type="bullet">
    /// <item>No definition has the id: throws <see cref="ArgumentException"/>
    /// naming it, and nothing changes. A grant is the game's code, so this is a
    /// build defect, not a save to forgive.</item>
    /// <item>Already had: answers null, and nothing changes. This is how a
    /// reward pays once: a game may grant on every turn its condition holds.</item>
    /// <item>Otherwise the id is appended to <see cref="Unlocked"/>, a
    /// <see cref="AchievementReward.Currency"/> reward is earned into
    /// <paramref name="wallet"/> (once, here), and the definition is answered
    /// for the game to show. A <see cref="AchievementReward.Cosmetic"/> is the
    /// game's to apply; the engine pays only currency.</item>
    /// </list>
    /// The wallet is the game's own (G3). It is checked before anything changes,
    /// and needed even when the reward is not currency, so a game can't grow two
    /// ways of granting.
    /// </summary>
    public AchievementDef? Grant(string id, Wallet wallet)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(wallet);
        if (!_byId.TryGetValue(id, out var definition))
            throw new ArgumentException($"No achievement has the id \"{id}\".", nameof(id));
        if (!_unlockedSet.Add(id)) return null;

        _unlocked.Add(id);
        if (definition.Reward is AchievementReward.Currency pay) wallet.Earn(pay.Amount);
        return definition;
    }
}
