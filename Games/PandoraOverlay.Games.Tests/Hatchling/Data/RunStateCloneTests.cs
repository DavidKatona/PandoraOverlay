using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// <see cref="RunState.Clone"/> is deep: a rule changes a copy and returns it,
/// and must never reach back into the state it was given (A20). Every list,
/// array, dictionary and nested object of a copy is changed here, and the
/// original must read as before. (The run's shape is in <c>Rules/</c>; its test
/// sits with the data tests, the files this task owns.)
/// </summary>
public class RunStateCloneTests
{
    /// <summary>A state with something in every field, built by hand (no rule exists yet to build one).</summary>
    private static RunState Filled()
    {
        var run = new RunState
        {
            Species = "Pachycephalosaurus",
            Danger = 3,
            Stars = 2,
            Seed = 17,
            Rng = 12345,
            Turn = 23,
            NextId = 4,
            Pace = Pace.Sneak,
            Offer = new List<Instinct> { Instinct.Fierce, Instinct.Wary, Instinct.Hardy },
            PicksOwed = 1,
            Redraws = 1,
            FreeRedrawUsed = true,
            ApexAt = -1,
            Kills = 2,
            Over = new RunOver
            {
                Won = false,
                Reason = EndReason.Caught,
                ByType = PredatorType.Carnotaurus,
                ByOrdinal = 2,
                Stage = 2,
                Lines = new List<PayLine> { new(PayKind.Grew, 45, 22), new(PayKind.Reached, 1, 5) },
                Total = 27,
                Earned = new List<string> { "first", "juvenile" },
            },
        };
        run.Ranks[Trait.DeepStomach] = 2;
        run.Ranks[Trait.StrongStart] = 1;
        run.Tiles[0] = Terrain.Forest;
        run.Seen[1] = true;
        run.Plants[2] = true;
        run.Meat[3] = 2;
        run.Creatures.Add(new Creature
        {
            Id = 1, Kind = CreatureKind.Predator, Type = PredatorType.Troodon, Ordinal = 1, At = 40, Health = 15,
            Alert = 3, Target = 60, Dazed = true, DazeFreeAt = 26, Slowed = 1, Bleed = 2, BleedDamage = 3, HitByYou = true,
        });
        run.Creatures.Add(new Creature { Id = 2, Kind = CreatureKind.SmallGame, At = 41, Health = 6 });
        run.You.At = 60;
        run.You.Growth = 55.5;
        run.You.Start = 10;
        run.You.Best = 55.5;
        run.You.Health = 0.75;
        run.You.Food = 61.2;
        run.You.Water = 48;
        run.You.Wind = true;
        run.You.LastMove = MoveKind.Sneak;
        run.You.LatchOn = 1;
        run.You.LatchStreak = 2;
        run.You.Bitten = true;
        run.Instincts.Add(Instinct.Eyes);
        run.Instincts.Add(Instinct.Ambush);
        run.LastTurn.Add(new TurnRow(RowActor.You, default, 0, 0, RowCode.Sneaked, RowCode.None, 1, 0, default, default, 0, false));
        return run;
    }

    /// <summary>Everything a state holds, as one text: equal texts are equal states.</summary>
    private static string Fingerprint(RunState r) => string.Join("|",
        r.Species, r.Danger, string.Join(",", r.Ranks.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")), r.Stars, r.Seed, r.Rng,
        r.Turn, string.Join(",", r.Tiles), string.Join(",", r.Seen), string.Join(",", r.Plants), string.Join(",", r.Meat),
        string.Join(";", r.Creatures.Select(c => string.Join(",", c.Id, c.Kind, c.Type, c.Ordinal, c.At, c.Health, c.Alert, c.Target,
            c.Relentless, c.SawYou, c.Dazed, c.DazeFreeAt, c.Slowed, c.Bleed, c.BleedDamage, c.HitByYou))),
        r.NextId, r.Pace,
        string.Join(",", r.You.At, r.You.Growth, r.You.Start, r.You.Best, r.You.Health, r.You.Food, r.You.Water, r.You.Wind,
            r.You.LastMove, r.You.LatchOn, r.You.LatchStreak, r.You.Bitten),
        string.Join(",", r.Instincts), r.Offer is null ? "none" : string.Join(",", r.Offer), r.PicksOwed, r.Redraws, r.FreeRedrawUsed,
        r.ApexAt, r.Kills,
        r.Over is null ? "none" : string.Join(",", r.Over.Won, r.Over.Reason, r.Over.ByType, r.Over.ByOrdinal, r.Over.Stage,
            string.Join(";", r.Over.Lines), r.Over.Total, string.Join(";", r.Over.Earned), r.Over.DangerUnlocked, r.Over.Settled),
        string.Join(";", r.LastTurn));

    [Fact]
    public void ACloneEqualsTheOriginal()
    {
        var run = Filled();
        Assert.Equal(Fingerprint(run), Fingerprint(run.Clone()));
    }

    [Fact]
    public void ChangingEveryPartOfACloneLeavesTheOriginalAsItWas()
    {
        var run = Filled();
        var before = Fingerprint(run);
        var copy = run.Clone();

        copy.Ranks[Trait.DeepStomach] = 5;
        copy.Ranks[Trait.Legacy] = 1;
        copy.Tiles[0] = Terrain.Rock;
        copy.Seen[1] = false;
        copy.Plants[2] = false;
        copy.Meat[3] = 0;
        copy.Creatures[0].Health = 1;
        copy.Creatures[0].Alert = 4;
        copy.Creatures[0].Dazed = false;
        copy.Creatures[1].At = 99;
        copy.Creatures.RemoveAt(1);
        copy.Creatures.Add(new Creature { Id = 9 });
        copy.You.At = 0;
        copy.You.Health = 0.1;
        copy.You.Food = 0;
        copy.You.Wind = false;
        copy.You.LatchStreak = 0;
        copy.Instincts.Add(Instinct.Hardy);
        copy.Offer!.Clear();
        copy.Over!.Lines.Add(new PayLine(PayKind.Danger, 3, 14));
        copy.Over.Earned.Add("hunter");
        copy.Over.Total = 0;
        copy.Over.Settled = true;
        copy.LastTurn.Clear();

        Assert.Equal(before, Fingerprint(run));
    }

    [Fact]
    public void CloningKeepsANullOfferAndANullEnd()
    {
        var run = Filled();
        run.Offer = null;
        run.Over = null;
        var copy = run.Clone();
        Assert.Null(copy.Offer);
        Assert.Null(copy.Over);
    }

    [Fact]
    public void ANewStateIsTheBoardsSizeAndHasNoApexYet()
    {
        var run = new RunState();
        Assert.Equal(Tuning.Cells, run.Tiles.Length);
        Assert.Equal(Tuning.Cells, run.Seen.Length);
        Assert.Equal(Tuning.Cells, run.Plants.Length);
        Assert.Equal(Tuning.Cells, run.Meat.Length);
        Assert.Equal(Tuning.ApexNotYet, run.ApexAt);
        Assert.Equal(1, run.NextId);
        Assert.Null(run.Offer);
        Assert.Null(run.Over);
    }

    [Fact]
    public void RankIsZeroForATraitNotOwnedAndHasAsksTheInstincts()
    {
        var run = Filled();
        Assert.Equal(2, run.Rank(Trait.DeepStomach));
        Assert.Equal(0, run.Rank(Trait.Legacy));
        Assert.True(run.Has(Instinct.Ambush));
        Assert.False(run.Has(Instinct.Hardy));
    }
}
