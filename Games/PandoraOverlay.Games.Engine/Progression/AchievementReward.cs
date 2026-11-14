namespace PandoraOverlay.Games.Engine;

/// <summary>
/// What an achievement pays (G22): nothing, a little currency in the game's own
/// wallet, or a cosmetic. Never power, and that is a promise made in public: an
/// achievement must not make a run easier.
///
/// <para><b>A closed set.</b> These three are the only kinds that can exist, so
/// no code anywhere can invent a fourth (an "extra life", a "+1 to hits") and
/// hand it out as an achievement's reward. Two things close it, and both are
/// needed:</para>
/// <list type="bullet">
/// <item>The base's only constructor is private, so only the records nested in
/// it can call it.</item>
/// <item>A record also gets a protected copy constructor, which C# will not let
/// a non-sealed record make private, and a record in another assembly can
/// derive through it (tried: <c>record Power() : AchievementReward(new
/// AchievementReward.None())</c> compiles against the private constructor
/// alone). So the base also has a <c>private protected abstract</c> member that
/// every kind must override: no type outside the Engine can see it to override
/// it, so a kind declared in a game can never be made concrete (the compiler
/// refuses it, CS0534), and none can ever be created.</item>
/// </list>
/// <para>Inside the Engine, a test holds the set to exactly these three by
/// reflection, so a fourth can't arrive here quietly either.</para>
///
/// <para>A <see cref="Cosmetic"/> is only an id: what it looks like and how it is
/// applied is the game's. The engine only guarantees that it is a cosmetic.</para>
/// </summary>
public abstract record AchievementReward
{
    /// <summary>Private: only the three kinds nested here derive from it.</summary>
    private AchievementReward() { }

    /// <summary>
    /// The seal (see the summary): every kind overrides it with nothing, and
    /// only types inside the Engine can see it, so only they can be concrete.
    /// It does nothing and is never called.
    /// </summary>
    private protected abstract void OnlyTheEnginesKinds();

    /// <summary>The achievement is its own reward: it pays nothing.</summary>
    public sealed record None : AchievementReward
    {
        private protected override void OnlyTheEnginesKinds() { }
    }

    /// <summary>
    /// A little currency, paid once into the game's own wallet when the
    /// achievement is granted (G3: never another game's). The amount is above
    /// 0: a reward of nothing is <see cref="None"/>, and a negative one would
    /// take currency away.
    /// </summary>
    public sealed record Currency(int Amount) : AchievementReward
    {
        /// <summary>Above 0; anything else throws when the reward is made (a build defect in the game's definitions).</summary>
        public int Amount { get; } = Amount > 0
            ? Amount
            : throw new ArgumentOutOfRangeException(nameof(Amount), Amount, "A currency reward pays more than nothing.");

        private protected override void OnlyTheEnginesKinds() { }
    }

    /// <summary>
    /// A cosmetic, by the game's own id for it (a colour, a pattern, a title):
    /// the game applies it, and it changes how something looks, never what it
    /// can do. The id is not empty or blank.
    /// </summary>
    public sealed record Cosmetic(string Id) : AchievementReward
    {
        /// <summary>Not null, empty or blank; anything else throws when the reward is made (a build defect in the game's definitions).</summary>
        public string Id { get; } = !string.IsNullOrWhiteSpace(Id)
            ? Id
            : throw new ArgumentException("A cosmetic reward needs the game's id for it.", nameof(Id));

        private protected override void OnlyTheEnginesKinds() { }
    }
}
