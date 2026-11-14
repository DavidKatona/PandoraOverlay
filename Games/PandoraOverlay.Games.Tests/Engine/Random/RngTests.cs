using System.Numerics;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The games' generator: the prototype's numbers bit for bit, a state that saves
/// and restores, the bounds of every helper, the number of rolls each takes, and
/// the child seeds of <see cref="Rng.Mix"/>. A03.
///
/// <para>Every pinned number here was printed by running the prototype's own code
/// in Node (<c>hxRoll</c> from <c>prototype\hatchling\tools\hatch-rules.js</c>);
/// <see cref="Rng.Mix"/>'s by an independent JavaScript copy of its documented
/// formula. A03's report holds the script.</para>
/// </summary>
public class RngTests
{
    // ---- The prototype's numbers ------------------------------------------------------

    /// <summary>
    /// The first five rolls for three seeds, as the prototype's <c>hxRoll</c> gives
    /// them: each roll is k / 2^32, and these are the k. Exact equality: the port is
    /// bit for bit or it is wrong.
    /// </summary>
    [Theory]
    [InlineData(1u, 2693262067u, 11749833u, 2265367787u, 4213581821u, 4159151403u, 567894474u)]
    [InlineData(123456789u, 1107202814u, 4169434471u, 3372958138u, 885470128u, 1301683845u, 691351262u)]
    [InlineData(4294967295u, 3850105811u, 813802916u, 3073704848u, 4054706436u, 3630262831u, 567894472u)]
    public void TheFirstRollsAreThePrototypes(uint seed, uint k1, uint k2, uint k3, uint k4, uint k5, uint stateAfter)
    {
        var rng = new Rng(seed);

        foreach (var k in new[] { k1, k2, k3, k4, k5 })
            Assert.Equal(k / 4294967296.0, rng.NextDouble());
        Assert.Equal(stateAfter, rng.State);
    }

    /// <summary>Every roll is in [0, 1), over many of them and several seeds.</summary>
    [Fact]
    public void ARollIsAtLeastZeroAndBelowOne()
    {
        foreach (var seed in new uint[] { 0, 1, 0x7FFFFFFF, 0xFFFFFFFF })
        {
            var rng = new Rng(seed);
            for (var i = 0; i < 20_000; i++)
            {
                var roll = rng.NextDouble();
                Assert.InRange(roll, 0.0, 1.0);
                Assert.True(roll < 1.0);
            }
        }
    }

    // ---- The state --------------------------------------------------------------------

    /// <summary>A new generator starts with its seed as its state.</summary>
    [Fact]
    public void TheSeedIsTheFirstState()
    {
        Assert.Equal(987654321u, new Rng(987654321).State);
    }

    /// <summary>
    /// The point of the class: the state saved mid-stream and set back, on the same
    /// generator or another, gives the same rolls from there on.
    /// </summary>
    [Fact]
    public void TheStateRestoredMidStreamGivesTheSameRolls()
    {
        var rng = new Rng(2026);
        for (var i = 0; i < 37; i++)
            rng.NextDouble();

        var saved = rng.State;
        var expected = Enumerable.Range(0, 50).Select(_ => rng.NextDouble()).ToArray();

        rng.State = saved;
        Assert.Equal(expected, Enumerable.Range(0, 50).Select(_ => rng.NextDouble()).ToArray());

        var other = new Rng(1) { State = saved };
        Assert.Equal(expected, Enumerable.Range(0, 50).Select(_ => other.NextDouble()).ToArray());
    }

    /// <summary>Two generators given one seed agree, whatever mix of helpers they are asked.</summary>
    [Fact]
    public void TwoGeneratorsWithOneSeedAgree()
    {
        var a = new Rng(424242);
        var b = new Rng(424242);
        for (var i = 0; i < 2_000; i++)
        {
            Assert.Equal(a.Next(97), b.Next(97));
            Assert.Equal(a.Range(-5, 5), b.Range(-5, 5));
            Assert.Equal(a.Chance(0.3), b.Chance(0.3));
        }
        Assert.Equal(a.State, b.State);
    }

    // ---- Next and Range ---------------------------------------------------------------

    /// <summary>Next stays in [0, n) and, for a small n, gives every value.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100)]
    public void NextStaysInItsBoundsAndGivesEveryValue(int n)
    {
        var rng = new Rng((uint)n * 7919u);
        var seen = new bool[n];
        for (var i = 0; i < 20_000; i++)
        {
            var value = rng.Next(n);
            Assert.InRange(value, 0, n - 1);
            seen[value] = true;
        }
        Assert.All(seen, hit => Assert.True(hit));
    }

    /// <summary>Next is the roll scaled and truncated, the prototype's <c>Math.floor(roll * n)</c>, up to the largest n.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(121)]
    [InlineData(int.MaxValue)]
    public void NextIsTheRollScaledAndTruncated(int n)
    {
        var a = new Rng(77);
        var b = new Rng(77);
        for (var i = 0; i < 5_000; i++)
        {
            var value = a.Next(n);
            Assert.Equal((int)Math.Floor(b.NextDouble() * n), value);
            Assert.InRange(value, 0, n - 1);
        }
    }

    /// <summary>Nothing to roll from throws, and takes no roll.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NextOfNothingThrowsAndRollsNothing(int n)
    {
        var rng = new Rng(5);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(n));
        Assert.Equal(5u, rng.State);
    }

    /// <summary>Range holds both ends and nothing outside them, and is <c>min + Next(span)</c>.</summary>
    [Theory]
    [InlineData(-3, 3)]
    [InlineData(0, 0)]
    [InlineData(10, 11)]
    [InlineData(-100, -90)]
    public void RangeHoldsBothEnds(int min, int maxInclusive)
    {
        var rng = new Rng(31337);
        var twin = new Rng(31337);
        var seen = new HashSet<int>();
        for (var i = 0; i < 10_000; i++)
        {
            var value = rng.Range(min, maxInclusive);
            Assert.InRange(value, min, maxInclusive);
            Assert.Equal(min + twin.Next(maxInclusive - min + 1), value);
            seen.Add(value);
        }
        Assert.Equal(maxInclusive - min + 1, seen.Count);
    }

    /// <summary>The widest range an int can count works; an empty one, or one too wide to count, throws and takes no roll.</summary>
    [Fact]
    public void RangeRefusesWhatItCantCount()
    {
        var rng = new Rng(9);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(int.MinValue, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(-1, int.MaxValue));
        Assert.Equal(9u, rng.State);

        for (var i = 0; i < 1_000; i++)
            Assert.InRange(rng.Range(int.MinValue, -2), int.MinValue, -2); // int.MaxValue numbers: the most there can be
    }

    // ---- Chance -----------------------------------------------------------------------

    /// <summary>0 never, 1 always, and each takes its one roll all the same.</summary>
    [Fact]
    public void ChanceOfZeroIsNeverAndOfOneIsAlwaysAndBothRoll()
    {
        var rng = new Rng(11);
        var twin = new Rng(11);
        for (var i = 0; i < 5_000; i++)
        {
            Assert.False(rng.Chance(0));
            Assert.True(rng.Chance(1));
            twin.NextDouble();
            twin.NextDouble();
            Assert.Equal(twin.State, rng.State);
        }
        Assert.False(rng.Chance(double.NaN));
        Assert.False(rng.Chance(-0.5));
        Assert.True(rng.Chance(1.5));
    }

    /// <summary>Chance is <c>NextDouble() &lt; p</c>, roll for roll.</summary>
    [Fact]
    public void ChanceIsTheRollBelowTheProbability()
    {
        var rng = new Rng(12);
        var twin = new Rng(12);
        var hits = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var hit = rng.Chance(0.4);
            Assert.Equal(twin.NextDouble() < 0.4, hit);
            if (hit)
                hits++;
        }
        Assert.InRange(hits, 3_700, 4_300); // about 40%: a sanity check of the spread, far from its edges
    }

    // ---- Pick and Shuffle -------------------------------------------------------------

    /// <summary>Pick is <c>items[Next(items.Count)]</c>; an empty list throws and takes no roll.</summary>
    [Fact]
    public void PickTakesTheItemAtNext()
    {
        var items = new[] { "a", "b", "c", "d", "e" };
        var rng = new Rng(13);
        var twin = new Rng(13);
        for (var i = 0; i < 1_000; i++)
            Assert.Equal(items[twin.Next(items.Length)], rng.Pick(items));

        var state = rng.State;
        Assert.Throws<ArgumentException>(() => rng.Pick(Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => rng.Pick<string>(null!));
        Assert.Equal(state, rng.State);
    }

    /// <summary>A shuffle keeps every item exactly once.</summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(999u)]
    public void AShuffleKeepsEveryItem(uint seed)
    {
        var rng = new Rng(seed);
        for (var round = 0; round < 50; round++)
        {
            var items = Enumerable.Range(0, 50).ToList();
            rng.Shuffle(items);
            Assert.Equal(Enumerable.Range(0, 50), items.OrderBy(x => x));
        }
    }

    /// <summary>
    /// The shuffle is Fisher-Yates from the last index down with <c>j = Next(i + 1)</c>,
    /// stopping at index 1: the same order as written out by hand from a twin, and
    /// n - 1 rolls for n items.
    /// </summary>
    [Fact]
    public void AShuffleIsFisherYatesFromTheTop()
    {
        var rng = new Rng(14);
        var twin = new Rng(14);
        var items = Enumerable.Range(0, 30).ToArray();
        var expected = (int[])items.Clone();

        rng.Shuffle(items);
        for (var i = expected.Length - 1; i >= 1; i--)
        {
            var j = twin.Next(i + 1);
            (expected[i], expected[j]) = (expected[j], expected[i]);
        }

        Assert.Equal(expected, items);
        Assert.Equal(twin.State, rng.State);
    }

    /// <summary>An empty or one-item list has nothing to shuffle: no roll is taken.</summary>
    [Fact]
    public void AShuffleOfNoneOrOneTakesNoRoll()
    {
        var rng = new Rng(15);
        rng.Shuffle(new List<int>());
        rng.Shuffle(new List<int> { 7 });
        Assert.Equal(15u, rng.State);
        Assert.Throws<ArgumentNullException>(() => rng.Shuffle<int>(null!));
    }

    /// <summary>Every order of five items comes up: the shuffle reaches all 120 of them.</summary>
    [Fact]
    public void AShuffleReachesEveryOrder()
    {
        var rng = new Rng(16);
        var orders = new HashSet<string>();
        for (var i = 0; i < 5_000; i++)
        {
            var items = new[] { 'a', 'b', 'c', 'd', 'e' };
            rng.Shuffle(items);
            orders.Add(new string(items));
        }
        Assert.Equal(120, orders.Count);
    }

    // ---- Mix --------------------------------------------------------------------------

    /// <summary>
    /// Mix's values are pinned for ever: a saved seed must mean the same board for
    /// ever. These were printed by a JavaScript copy of the formula in Mix's summary.
    /// </summary>
    [Theory]
    [InlineData(0u, 0u, 2856213345u)]
    [InlineData(0u, 1u, 2571226413u)]
    [InlineData(1u, 0u, 1863002342u)]
    [InlineData(1u, 1u, 1435675793u)]
    [InlineData(123456789u, 7u, 1353798329u)]
    [InlineData(4294967295u, 4294967295u, 3664794242u)]
    public void MixIsPinned(uint seed, uint salt, uint expected)
    {
        Assert.Equal(expected, Rng.Mix(seed, salt));
    }

    /// <summary>No two children collide over a grid of seeds and salts, and none is its own parent seed.</summary>
    [Fact]
    public void MixDoesNotCollideOverASmallGrid()
    {
        var seeds = Enumerable.Range(0, 64).Select(i => (uint)i)
            .Concat(new uint[] { 0x7FFFFFFF, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF, 123456789 }).ToArray();
        var children = new HashSet<uint>();
        foreach (var seed in seeds)
        {
            for (uint salt = 0; salt < 64; salt++)
            {
                var child = Rng.Mix(seed, salt);
                Assert.True(children.Add(child), $"Mix({seed}, {salt}) collided");
            }
        }
    }

    /// <summary>For one seed every salt gives a different child (each step of the formula is a bijection), and so for one salt does every seed.</summary>
    [Fact]
    public void MixIsOneToOneInEachArgument()
    {
        var bySalt = new HashSet<uint>();
        var bySeed = new HashSet<uint>();
        for (uint i = 0; i < 20_000; i++)
        {
            Assert.True(bySalt.Add(Rng.Mix(2026, i)));
            Assert.True(bySeed.Add(Rng.Mix(i, 3)));
        }
    }

    /// <summary>
    /// Well spread: neighbouring salts (or seeds) give children that differ in about
    /// half their 32 bits, not in the last few, so streams with salts 1, 2, 3 don't
    /// start alike.
    /// </summary>
    [Fact]
    public void MixSpreadsNeighbouringInputs()
    {
        long saltBits = 0, seedBits = 0;
        const int pairs = 2_000;
        for (uint i = 0; i < pairs; i++)
        {
            saltBits += BitOperations.PopCount(Rng.Mix(99, i) ^ Rng.Mix(99, i + 1));
            seedBits += BitOperations.PopCount(Rng.Mix(i, 99) ^ Rng.Mix(i + 1, 99));
        }
        Assert.InRange(saltBits / (double)pairs, 15.0, 17.0);
        Assert.InRange(seedBits / (double)pairs, 15.0, 17.0);
    }

    /// <summary>A child stream's rolls are not its parent's: a generator on Mix(seed, 1) does not repeat the one on seed.</summary>
    [Fact]
    public void AChildStreamIsNotItsParent()
    {
        var parent = new Rng(500);
        var child = new Rng(Rng.Mix(500, 1));
        var same = Enumerable.Range(0, 100).Count(_ => parent.NextDouble() == child.NextDouble());
        Assert.Equal(0, same);
    }
}
