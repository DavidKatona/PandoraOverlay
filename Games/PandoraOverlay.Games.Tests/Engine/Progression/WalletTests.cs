using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A game's wallet (G3): it is made from a save's two numbers, earns, spends
/// what it holds and never goes below zero or overflows. A07.
/// </summary>
public class WalletTests
{
    [Fact]
    public void ANewWalletIsEmpty()
    {
        var w = new Wallet();
        Assert.Equal(0, w.Balance);
        Assert.Equal(0, w.Earned);
    }

    [Fact]
    public void ASavesNumbersComeBack()
    {
        var w = new Wallet(balance: 120, earned: 900);
        Assert.Equal(120, w.Balance);
        Assert.Equal(900, w.Earned);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(long.MinValue, long.MinValue)]
    public void ANegativeNumberFromASaveThrows(long balance, long earned)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Wallet(balance, earned));
    }

    [Fact]
    public void EarningAddsToTheBalanceAndToTheLifetimeTotal()
    {
        var w = new Wallet(10, 50);
        w.Earn(5);
        Assert.Equal(15, w.Balance);
        Assert.Equal(55, w.Earned);
        w.Earn(0);
        Assert.Equal(15, w.Balance);
        Assert.Equal(55, w.Earned);
    }

    [Fact]
    public void EarningANegativeAmountThrowsAndChangesNothing()
    {
        var w = new Wallet(10, 50);
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Earn(-1));
        Assert.Equal(10, w.Balance);
        Assert.Equal(50, w.Earned);
    }

    [Fact]
    public void EarningStopsAtTheLargestNumberInsteadOfOverflowing()
    {
        var w = new Wallet(long.MaxValue - 3, long.MaxValue - 1);
        w.Earn(2);
        Assert.Equal(long.MaxValue - 1, w.Balance);
        Assert.Equal(long.MaxValue, w.Earned);   // stopped here: one more would have wrapped negative

        w.Earn(long.MaxValue);
        Assert.Equal(long.MaxValue, w.Balance);
        Assert.Equal(long.MaxValue, w.Earned);
    }

    [Fact]
    public void EachTotalStopsOnItsOwn()
    {
        // After spending, the balance has room the lifetime total no longer has.
        var w = new Wallet(0, long.MaxValue);
        w.Earn(7);
        Assert.Equal(7, w.Balance);
        Assert.Equal(long.MaxValue, w.Earned);
    }

    [Fact]
    public void SpendingWhatItHoldsSubtractsAndLeavesTheLifetimeTotal()
    {
        var w = new Wallet(100, 400);
        Assert.True(w.TrySpend(30));
        Assert.Equal(70, w.Balance);
        Assert.Equal(400, w.Earned);

        Assert.True(w.TrySpend(70));   // all of it
        Assert.Equal(0, w.Balance);
        Assert.Equal(400, w.Earned);
    }

    [Fact]
    public void SpendingMoreThanItHoldsAnswersFalseAndChangesNothing()
    {
        var w = new Wallet(100, 400);
        Assert.False(w.TrySpend(101));
        Assert.Equal(100, w.Balance);
        Assert.Equal(400, w.Earned);

        Assert.False(w.TrySpend(long.MaxValue));
        Assert.Equal(100, w.Balance);
    }

    [Fact]
    public void SpendingNothingAlwaysSucceeds()
    {
        var w = new Wallet();
        Assert.True(w.TrySpend(0));
        Assert.Equal(0, w.Balance);
    }

    [Fact]
    public void SpendingANegativeAmountThrowsAndChangesNothing()
    {
        var w = new Wallet(100, 400);
        Assert.Throws<ArgumentOutOfRangeException>(() => w.TrySpend(-5));
        Assert.Equal(100, w.Balance);
        Assert.Equal(400, w.Earned);
    }

    [Fact]
    public void TwoWalletsNeverTouch()
    {
        // G3: one per game. Nothing done to one shows in the other.
        var a = new Wallet(10, 10);
        var b = new Wallet(10, 10);
        a.Earn(5);
        Assert.True(a.TrySpend(15));
        Assert.Equal(10, b.Balance);
        Assert.Equal(10, b.Earned);
    }

    [Fact]
    public void AWalletHasNoWayToMoveOrBuyCurrency()
    {
        // G3, G4: its whole public surface is the two numbers, Earn and TrySpend.
        // A transfer, a purchase or a setter would have to be added here, and
        // this test is where it would show.
        var members = typeof(Wallet)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(n => n != ".ctor")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Balance", "Earn", "Earned", "TrySpend", "get_Balance", "get_Earned" }, members);
    }
}
