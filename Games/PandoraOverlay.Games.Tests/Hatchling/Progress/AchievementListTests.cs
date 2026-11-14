using PandoraOverlay.Games.Engine;
using PandoraOverlay.Games.Hatchling;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The 24 achievements (H26) and G22 as a test: every reward is a little of the
/// game's own currency or nothing, never power, 3,490 in all.
/// </summary>
public class AchievementListTests
{
    [Fact]
    public void TheIdsAreTheTextsInTheTablesOrderTwentyFourOfThemUnique()
    {
        var ids = AchievementList.All.Select(a => a.Id).ToList();
        Assert.Equal(HatchlingText.AchievementIds, ids);
        Assert.Equal(24, ids.Count);
        Assert.Equal(24, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryRewardIsCurrencyOrNothingAndTheySumToTheTotal()
    {
        Assert.All(AchievementList.All, a =>
            Assert.True(a.Reward is AchievementReward.Currency or AchievementReward.None, $"{a.Id} pays {a.Reward}"));
        var sum = AchievementList.All.Sum(a => a.Reward is AchievementReward.Currency c ? c.Amount : 0);
        Assert.Equal(3490, sum);
        Assert.Equal(AchievementList.TotalPay, sum);
        Assert.Equal(0.08, (double)sum / Lineage.WholePrice, 2);   // 8% of the lineage
    }

    [Fact]
    public void EachPaysWhatThePlanSays()
    {
        var plan = new (string Id, int Pay)[]
        {
            ("first", 20), ("juvenile", 20), ("adult", 40), ("elder", 60), ("win", 100), ("slayer", 150),
            ("hunter", 50), ("underdog", 100), ("heavy", 100), ("shapes", 300), ("danger3", 150), ("danger5", 400),
            ("unseen", 100), ("gentle", 100), ("six", 60), ("island", 300), ("roots", 60), ("blood", 0),
            ("researcher", 80), ("naturalist", 250), ("diverse", 150), ("star21", 400), ("mastery", 0), ("all", 500),
        };
        Assert.Equal(plan.Select(p => p.Id), AchievementList.All.Select(a => a.Id));
        Assert.Equal(plan.Select(p => p.Pay), AchievementList.All.Select(a => ProgressSamples.PayOf(a.Id)));
        Assert.IsType<AchievementReward.None>(AchievementList.All.Single(a => a.Id == "blood").Reward);
        Assert.IsType<AchievementReward.None>(AchievementList.All.Single(a => a.Id == "mastery").Reward);
    }

    [Fact]
    public void TheWordsAreTheTextClasss()
    {
        Assert.All(AchievementList.All, a =>
        {
            Assert.Equal(HatchlingText.AchievementName(a.Id), a.Title);
            Assert.Equal(HatchlingText.AchievementText(a.Id), a.Text);
        });
        Assert.Equal("First steps", AchievementList.All[0].Title);
        Assert.Equal("Win a run with every species", AchievementList.All[^1].Text);
    }

    [Fact]
    public void TheEngineTakesTheList()
    {
        var achievements = new Achievements(AchievementList.All);
        Assert.Equal(24, achievements.All.Count);
        Assert.Empty(achievements.Unlocked);
    }

    [Fact]
    public void TheThresholdsAreTheTexts()
    {
        Assert.Equal(25, AchievementList.HunterKills);
        Assert.Equal(3, AchievementList.IntoDanger);
        Assert.Equal(6, AchievementList.WideLineage);
        Assert.Equal(10, AchievementList.RootsRanks);
        Assert.Equal(3, AchievementList.ResearcherEntries);
        Assert.Equal(15, AchievementList.NaturalistEntries);
        Assert.Equal(5, AchievementList.DiverseSpecies);
        Assert.Contains("25", HatchlingText.AchievementText(AchievementList.Hunter));
        Assert.Contains("3", HatchlingText.AchievementText(AchievementList.Danger3));
        Assert.Contains("6", HatchlingText.AchievementText(AchievementList.Six));
        Assert.Contains("10", HatchlingText.AchievementText(AchievementList.Roots));
        Assert.Contains("3", HatchlingText.AchievementText(AchievementList.Researcher));
        Assert.Contains("15", HatchlingText.AchievementText(AchievementList.Naturalist));
        Assert.Contains("5", HatchlingText.AchievementText(AchievementList.Diverse));
    }
}
