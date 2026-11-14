using System.Reflection;
using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A game's achievements (G22): the closed set of rewards (never power), the
/// definitions' defects, the save's list kept as it came, and a grant that
/// pays once. A07.
/// </summary>
public class AchievementsTests
{
    private static readonly AchievementDef First = new("first-steps", "First steps", "Take a step.", new AchievementReward.None());
    private static readonly AchievementDef Paid = new("paid", "Paid", "Do the paid thing.", new AchievementReward.Currency(25));
    private static readonly AchievementDef Pretty = new("pretty", "Pretty", "Do the pretty thing.", new AchievementReward.Cosmetic("stripes"));

    private static Achievements Three(IEnumerable<string>? unlocked = null) => new(new[] { First, Paid, Pretty }, unlocked);

    // ---- The rewards: a closed set ------------------------------------------------------

    [Fact]
    public void TheEngineHasExactlyThreeKindsOfReward()
    {
        var kinds = typeof(AchievementReward).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AchievementReward)))
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[]
        {
            typeof(AchievementReward.Cosmetic).FullName,
            typeof(AchievementReward.Currency).FullName,
            typeof(AchievementReward.None).FullName,
        }, kinds);
        Assert.All(kinds, k => Assert.StartsWith(typeof(AchievementReward).FullName + "+", k));   // all three nested in it
    }

    [Fact]
    public void NoLoadedAssemblyAddsAKind()
    {
        // The games' tests load every game, so a kind declared in any of them
        // shows here (and could never be made concrete anyway: see the seal below).
        var outside = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.GetName().Name!.StartsWith("PandoraOverlay", StringComparison.Ordinal))
            .Where(a => a != typeof(AchievementReward).Assembly)
            .SelectMany(TypesOf)
            .Where(t => t.IsSubclassOf(typeof(AchievementReward)))
            .Select(t => t.FullName)
            .ToArray();
        Assert.Empty(outside);
    }

    [Fact]
    public void TheBaseCanOnlyBeDerivedInsideTheEngine()
    {
        var t = typeof(AchievementReward);
        Assert.True(t.IsAbstract);

        // Its own constructor is private; the only other is the record's copy
        // constructor, which C# keeps protected on a record that isn't sealed.
        var ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Contains(ctors, c => c.IsPrivate && c.GetParameters().Length == 0);
        Assert.DoesNotContain(ctors, c => c.IsPublic || c.IsAssembly || c.IsFamilyOrAssembly);
        Assert.All(ctors.Where(c => !c.IsPrivate), c =>
        {
            Assert.True(c.IsFamily);
            Assert.Equal(new[] { t }, c.GetParameters().Select(p => p.ParameterType));
        });

        // So the seal: an abstract member only the Engine's types can see. A
        // record in a game deriving through the copy constructor can't override
        // it, so it can never be concrete (CS0534; tried by hand, A07's report).
        var seals = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => m.IsAbstract && m.IsFamilyAndAssembly)
            .ToArray();
        Assert.Single(seals);
    }

    [Fact]
    public void ACurrencyRewardPaysMoreThanNothing()
    {
        Assert.Equal(1, new AchievementReward.Currency(1).Amount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AchievementReward.Currency(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AchievementReward.Currency(-10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ACosmeticRewardNeedsAnId(string? id)
    {
        Assert.Throws<ArgumentException>(() => new AchievementReward.Cosmetic(id!));
    }

    [Fact]
    public void RewardsCompareByValue()
    {
        Assert.Equal(new AchievementReward.None(), new AchievementReward.None());
        Assert.Equal(new AchievementReward.Currency(5), new AchievementReward.Currency(5));
        Assert.NotEqual<AchievementReward>(new AchievementReward.Currency(5), new AchievementReward.Currency(6));
        Assert.Equal(new AchievementReward.Cosmetic("x"), new AchievementReward.Cosmetic("x"));
        Assert.Equal(5, (new AchievementReward.Currency(5) with { }).Amount);   // a copy keeps the checked value
    }

    // ---- The definitions: build defects throw -------------------------------------------

    [Fact]
    public void TwoDefinitionsWithOneIdThrow()
    {
        var twin = new AchievementDef("paid", "Paid again", "Again.", new AchievementReward.None());
        var e = Assert.Throws<ArgumentException>(() => new Achievements(new[] { First, Paid, twin }));
        Assert.Contains("\"paid\"", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IdsAreComparedExactly()
    {
        // Two ids that differ only in case are two achievements, as the save keeps them.
        var upper = new AchievementDef("PAID", "Loud", "Loudly.", new AchievementReward.None());
        var a = new Achievements(new[] { Paid, upper }, new[] { "PAID" });
        Assert.True(a.Has("PAID"));
        Assert.False(a.Has("paid"));
    }

    [Fact]
    public void ADefinitionWithoutAnIdOrARewardThrows()
    {
        Assert.Throws<ArgumentException>(() => new Achievements(new[] { new AchievementDef(" ", "t", "x", new AchievementReward.None()) }));
        Assert.Throws<ArgumentException>(() => new Achievements(new[] { new AchievementDef("a", "t", "x", null!) }));
        Assert.Throws<ArgumentException>(() => new Achievements(new AchievementDef[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new Achievements(null!));
    }

    [Fact]
    public void AllKeepsTheGamesOrder()
    {
        var a = new Achievements(new[] { Pretty, First, Paid });
        Assert.Equal(new[] { Pretty, First, Paid }, a.All);
    }

    // ---- The save's list --------------------------------------------------------------

    [Fact]
    public void ANewGameHasNone()
    {
        var a = Three();
        Assert.Empty(a.Unlocked);
        Assert.False(a.Has("first-steps"));
    }

    [Fact]
    public void TheSavesListComesBackInItsOrder()
    {
        var a = Three(new[] { "pretty", "first-steps" });
        Assert.Equal(new[] { "pretty", "first-steps" }, a.Unlocked);
        Assert.True(a.Has("pretty"));
        Assert.True(a.Has("first-steps"));
        Assert.False(a.Has("paid"));
    }

    [Fact]
    public void AnIdNoDefinitionHasIsKeptButNotHad()
    {
        // An achievement a later version dropped: the save round-trips with it.
        var a = Three(new[] { "first-steps", "dropped-one" });
        Assert.Equal(new[] { "first-steps", "dropped-one" }, a.Unlocked);
        Assert.False(a.Has("dropped-one"));

        // and a version that brings it back finds it still had
        var back = new AchievementDef("dropped-one", "Back", "Back again.", new AchievementReward.Currency(9));
        var later = new Achievements(new[] { First, back }, a.Unlocked);
        Assert.True(later.Has("dropped-one"));
        var w = new Wallet();
        Assert.Null(later.Grant("dropped-one", w));
        Assert.Equal(0, w.Balance);   // already had: not paid a second time
    }

    [Fact]
    public void BlankAndRepeatedEntriesInTheSaveAreDropped()
    {
        var a = Three(new[] { "paid", "", null!, "  ", "paid", "pretty", "paid" });
        Assert.Equal(new[] { "paid", "pretty" }, a.Unlocked);
    }

    [Fact]
    public void HasAnythingUnknownIsFalse()
    {
        var a = Three(new[] { "paid" });
        Assert.False(a.Has("nope"));
        Assert.False(a.Has(""));
        Assert.False(a.Has(null!));
    }

    // ---- Granting ---------------------------------------------------------------------

    [Fact]
    public void AGrantAppendsAndAnswersTheDefinition()
    {
        var a = Three(new[] { "pretty" });
        var w = new Wallet();
        Assert.Same(First, a.Grant("first-steps", w));
        Assert.Equal(new[] { "pretty", "first-steps" }, a.Unlocked);
        Assert.True(a.Has("first-steps"));
        Assert.Equal(0, w.Balance);   // None pays nothing
    }

    [Fact]
    public void ACurrencyRewardIsPaidOnce()
    {
        var a = Three();
        var w = new Wallet(3, 3);
        Assert.Same(Paid, a.Grant("paid", w));
        Assert.Equal(28, w.Balance);
        Assert.Equal(28, w.Earned);

        for (var i = 0; i < 5; i++) Assert.Null(a.Grant("paid", w));
        Assert.Equal(28, w.Balance);
        Assert.Equal(28, w.Earned);
        Assert.Equal(new[] { "paid" }, a.Unlocked);
    }

    [Fact]
    public void ARewardTheSaveAlreadyHadIsNotPaid()
    {
        var a = Three(new[] { "paid" });
        var w = new Wallet();
        Assert.Null(a.Grant("paid", w));
        Assert.Equal(0, w.Balance);
    }

    [Fact]
    public void ACosmeticIsAnsweredForTheGameToApplyAndPaysNothing()
    {
        var a = Three();
        var w = new Wallet(1, 1);
        var granted = a.Grant("pretty", w);
        Assert.Equal(new AchievementReward.Cosmetic("stripes"), granted!.Reward);
        Assert.Equal(1, w.Balance);
        Assert.Equal(1, w.Earned);
    }

    [Fact]
    public void GrantingAnUnknownIdThrowsNamingItAndChangesNothing()
    {
        var a = Three(new[] { "pretty" });
        var w = new Wallet();
        var e = Assert.Throws<ArgumentException>(() => a.Grant("typo", w));
        Assert.Contains("\"typo\"", e.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "pretty" }, a.Unlocked);
    }

    [Fact]
    public void GrantingADroppedIdFromTheSaveStillThrows()
    {
        // It is in Unlocked, but the game has no definition: a grant of it is the
        // game's own code naming an id it doesn't define.
        var a = Three(new[] { "dropped-one" });
        Assert.Throws<ArgumentException>(() => a.Grant("dropped-one", new Wallet()));
    }

    [Fact]
    public void AGrantNeedsAWalletAndChangesNothingWithout()
    {
        var a = Three();
        Assert.Throws<ArgumentNullException>(() => a.Grant("paid", null!));
        Assert.Throws<ArgumentNullException>(() => a.Grant(null!, new Wallet()));
        Assert.Empty(a.Unlocked);
        Assert.False(a.Has("paid"));
    }

    [Fact]
    public void APaymentStopsAtTheWalletsLimit()
    {
        var a = Three();
        var w = new Wallet(long.MaxValue - 1, long.MaxValue - 1);
        a.Grant("paid", w);
        Assert.Equal(long.MaxValue, w.Balance);
        Assert.Equal(long.MaxValue, w.Earned);
    }

    [Fact]
    public void TheListGivenIsCopied()
    {
        var defs = new List<AchievementDef> { First };
        var saved = new List<string> { "first-steps" };
        var a = new Achievements(defs, saved);
        defs.Add(Paid);
        saved.Add("paid");
        Assert.Single(a.All);
        Assert.Equal(new[] { "first-steps" }, a.Unlocked);
    }

    private static IEnumerable<Type> TypesOf(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
    }
}
