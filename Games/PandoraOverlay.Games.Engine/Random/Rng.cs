namespace PandoraOverlay.Games.Engine;

/// <summary>
/// The games' random numbers: seeded, and its whole state is one number. Save
/// <see cref="State"/> mid-run, set it back later, and every roll from there on
/// is the same as it would have been: a run in a save picks up exactly where it
/// stopped, and a bot replays any run from its seed with no screen (A21).
///
/// <para>The generator is the prototype's (mulberry32, <c>hxRoll</c> in the
/// prototype's <c>hatch-rules.js</c>, in the games folder), bit for bit, so the rules
/// ported from the prototype roll what the prototype rolled and its numbers (the
/// bot's pacing among them) carry over. The tests pin its first values against
/// the prototype's own output.</para>
///
/// <para>It is the ONLY randomness a game may use: <c>System.Random</c> is on the
/// forbidden list (A29), because its sequence for a seed is not promised across
/// .NET versions and its state can't be saved, and a run that can't be replayed
/// can't be tested, saved mid-turn or trusted by a bot's pacing targets.</para>
///
/// <para>Every member that rolls takes exactly one roll (one step of the state),
/// but <see cref="Shuffle{T}"/>, which takes one for every item past the first, and
/// a call that throws for its arguments takes none. Which members roll and how
/// many times is part of the contract: change it and every saved run and every
/// pinned test plays out differently.</para>
/// </summary>
public sealed class Rng
{
    /// <summary>A generator whose first roll is the prototype's first roll for <paramref name="seed"/>.</summary>
    public Rng(uint seed)
    {
        State = seed;
    }

    /// <summary>
    /// The whole state. Reading it and setting it back later gives the same rolls
    /// from that point on; this is what a save keeps. Every value is a good state
    /// (0 included): mulberry32 has no weak seeds to avoid.
    /// </summary>
    public uint State { get; set; }

    /// <summary>
    /// One roll: a number in [0, 1), a whole multiple of 2^-32. The prototype's
    /// <c>hxRoll</c> exactly: JavaScript's <c>Math.imul</c> and <c>&gt;&gt;&gt;</c>
    /// are 32-bit wrapping arithmetic, which unchecked <c>uint</c> is.
    /// </summary>
    public double NextDouble()
    {
        unchecked
        {
            var a = State + 0x6D2B79F5u;
            State = a;
            var t = (a ^ (a >> 15)) * (1u | a);
            t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    /// <summary>
    /// A whole number in [0, <paramref name="exclusiveMax"/>): one roll, scaled and
    /// truncated, as the prototype's <c>Math.floor(hxRoll(...) * n)</c>. Throws,
    /// rolling nothing, when <paramref name="exclusiveMax"/> is 0 or less.
    /// </summary>
    public int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), exclusiveMax, "There must be at least one number to roll.");
        // The roll is k / 2^32 with k < 2^32, so the product stays under exclusiveMax:
        // for any int the gap below it is wider than the double's rounding step.
        return (int)(NextDouble() * exclusiveMax);
    }

    /// <summary>
    /// A whole number from <paramref name="min"/> to <paramref name="maxInclusive"/>,
    /// both included: <c>min + Next(maxInclusive - min + 1)</c>. Throws, rolling
    /// nothing, when the range is empty or holds more numbers than an int can count.
    /// </summary>
    public int Range(int min, int maxInclusive)
    {
        var span = (long)maxInclusive - min + 1;
        if (span <= 0 || span > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxInclusive), maxInclusive, "The range must hold from 1 to int.MaxValue numbers.");
        return min + Next((int)span);
    }

    /// <summary>
    /// True with the chance <paramref name="probability"/>: <c>NextDouble() &lt; p</c>,
    /// so 0 (or less) is never and 1 (or more) is always. It takes its one roll
    /// whatever the chance, even a sure one, so that a tuning change from 1 to 0.99
    /// does not shift every roll after it. NaN is never.
    /// </summary>
    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>One of <paramref name="items"/>, each as likely: <c>items[Next(items.Count)]</c>. An empty list throws, rolling nothing.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("There is nothing to pick from.", nameof(items));
        return items[Next(items.Count)];
    }

    /// <summary>
    /// Puts <paramref name="items"/> in a random order, in place: Fisher-Yates from
    /// the last index down, swapping item <c>i</c> with item <c>Next(i + 1)</c>. It
    /// stops at index 1 (the step for index 0 could only swap the item with itself),
    /// so a list of n items takes n - 1 rolls, and an empty or one-item list none.
    /// </summary>
    public void Shuffle<T>(IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (var i = items.Count - 1; i >= 1; i--)
        {
            var j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// A child seed for <paramref name="salt"/>, drawn from <paramref name="seed"/>
    /// without rolling anything: a pure function, so one run seed can give the
    /// board's layout and what happens on it streams of their own (salts 1, 2, 3 ...)
    /// that never disturb one another, and a stream added later leaves the others'
    /// rolls alone.
    ///
    /// <para>THE FORMULA, which must never change: a saved seed has to mean the same
    /// board for ever, and the tests pin its values.</para>
    /// <code>
    /// Mix(seed, salt) = F(seed + F(salt ^ 0x9E3779B9))     (32-bit wrapping)
    /// F(h): h ^= h &gt;&gt; 16; h *= 0x85EBCA6B; h ^= h &gt;&gt; 13; h *= 0xC2B2AE35; h ^= h &gt;&gt; 16
    /// </code>
    /// <para>F is MurmurHash3's 32-bit finaliser, a bijection that spreads every input
    /// bit over the whole word; 0x9E3779B9 (2^32 over the golden ratio) keeps salt 0
    /// from being F's fixed point 0. Each step is a bijection, so for one seed every
    /// salt gives a different child, and for one salt every seed does. In JavaScript
    /// it is the same with <c>Math.imul</c> and <c>&gt;&gt;&gt; 0</c> after each step.</para>
    /// </summary>
    public static uint Mix(uint seed, uint salt)
    {
        unchecked
        {
            return Finalise(seed + Finalise(salt ^ 0x9E3779B9u));
        }
    }

    /// <summary>MurmurHash3's 32-bit finaliser (<c>fmix32</c>), the F of <see cref="Mix"/>.</summary>
    private static uint Finalise(uint h)
    {
        unchecked
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }
}
