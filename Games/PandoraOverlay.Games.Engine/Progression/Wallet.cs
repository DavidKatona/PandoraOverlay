namespace PandoraOverlay.Games.Engine;

/// <summary>
/// A game's currency (G3): one wallet per game, kept inside that game's own
/// save, and never shared with another game.
///
/// <para>What this class can't do is the point of it, because three public
/// promises rest on it: nothing to buy, nothing that looks like real money,
/// and one wallet per game (G3, G4). So there is deliberately no way to move
/// currency from one wallet to another, no way to buy any, and no way to go
/// below zero: a wallet only earns (<see cref="Earn"/>, from play) and spends
/// what it holds (<see cref="TrySpend"/>, inside its own game). It holds no
/// reference to any other wallet, any game or any host, so a game can't reach
/// another game's currency through it. A game that wanted a transfer would have
/// to write one, in plain sight, against these decisions.</para>
///
/// <para>The currency's name is the game's, not the engine's: a wallet holds a
/// number and knows no word for it (A21).</para>
///
/// <para>Overflow stops, it never wraps: a sum past <see cref="long.MaxValue"/>
/// stays at <see cref="long.MaxValue"/>. No real play gets near it; a wrap would
/// turn the richest save negative, which is the one thing a wallet may never be.</para>
/// </summary>
public sealed class Wallet
{
    /// <summary>
    /// A wallet as a save keeps it (G3): what it holds and what it has earned
    /// in all. A new game's wallet is the default, empty one. Either number
    /// negative throws: a wallet is never below zero, and a save that says so
    /// is a defect for the game's own save check to catch, not a number to
    /// carry on with.
    /// </summary>
    public Wallet(long balance = 0, long earned = 0)
    {
        if (balance < 0)
            throw new ArgumentOutOfRangeException(nameof(balance), balance, "A wallet is never below zero.");
        if (earned < 0)
            throw new ArgumentOutOfRangeException(nameof(earned), earned, "A wallet never earned less than nothing.");
        Balance = balance;
        Earned = earned;
    }

    /// <summary>What it holds now: never below zero.</summary>
    public long Balance { get; private set; }

    /// <summary>
    /// Everything it ever earned, its lifetime total: spending never lowers it.
    /// A game can show it, or pace an achievement on it.
    /// </summary>
    public long Earned { get; private set; }

    /// <summary>
    /// Adds <paramref name="amount"/> to <see cref="Balance"/> and to
    /// <see cref="Earned"/>, each stopping at <see cref="long.MaxValue"/>
    /// instead of overflowing. Zero is allowed and changes nothing. A negative
    /// amount throws and changes nothing: taking currency away is
    /// <see cref="TrySpend"/>'s, which checks there is enough.
    /// </summary>
    public void Earn(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "An amount earned is never negative.");
        Balance = Saturating(Balance, amount);
        Earned = Saturating(Earned, amount);
    }

    /// <summary>
    /// Spends <paramref name="amount"/> when the wallet holds that much:
    /// subtracts it and answers true. More than <see cref="Balance"/> answers
    /// false and changes nothing, so a game can try a purchase and show "not
    /// enough" without checking first. Zero always succeeds. <see cref="Earned"/>
    /// never falls. A negative amount throws and changes nothing: a negative
    /// spend would be an earning that skipped <see cref="Earn"/>'s lifetime count.
    /// </summary>
    public bool TrySpend(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "An amount spent is never negative.");
        if (amount > Balance) return false;
        Balance -= amount;
        return true;
    }

    /// <summary>
    /// <paramref name="total"/> + <paramref name="amount"/>, both 0 or more,
    /// stopped at <see cref="long.MaxValue"/> (the subtraction can't overflow
    /// because <paramref name="total"/> is never negative).
    /// </summary>
    private static long Saturating(long total, long amount) =>
        amount > long.MaxValue - total ? long.MaxValue : total + amount;
}
