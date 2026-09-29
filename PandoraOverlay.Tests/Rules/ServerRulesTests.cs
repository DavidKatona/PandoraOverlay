using Xunit;

namespace PandoraOverlay.Tests;

public class ServerRulesTests
{
    [Fact]
    public void TheBundledSeedLoadsAndIsComplete()
    {
        var doc = ServerRules.LoadBundled();

        Assert.NotNull(doc);
        Assert.Equal("https://islapandora.eu/rules", doc!.Source);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", doc.CopiedOn);
        Assert.Contains("Discord", doc.Note);
        Assert.Equal(17, doc.Rules.Count);
        Assert.Equal(new[] { "Herbivore", "Carnivore", "Omnivore" }, doc.PackLimits.Select(c => c.Category));
        Assert.Equal(21, doc.PackLimits.Sum(c => c.Species.Count));
        Assert.All(doc.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }

    [Fact]
    public void LimitForFindsASpeciesRegardlessOfCase()
    {
        var doc = ServerRules.LoadBundled()!;
        Assert.Equal(2, doc.LimitFor("Deinosuchus"));
        Assert.Equal(2, doc.LimitFor("deinosuchus"));
        Assert.Equal(12, doc.LimitFor("Beipiaosaurus"));
        Assert.Null(doc.LimitFor("Spinosaurus"));
        Assert.Null(doc.LimitFor(null));
        Assert.Null(doc.LimitFor(""));
    }

    [Fact]
    public void ParseDropsBlanksAndEmptyCategories()
    {
        var doc = ServerRules.Parse("""
            {"source":"s","copiedOn":"2026-09-29","note":"n",
             "packLimits":[{"category":"Herbivore","species":[{"name":"Dryosaurus","limit":10},{"name":"","limit":3},{"name":"Zero","limit":0}]},
                           {"category":"Empty","species":[]},{"category":"","species":[{"name":"x","limit":1}]}],
             "rules":["  keep it fair ","","   "]}
            """);

        Assert.NotNull(doc);
        Assert.Equal(new[] { "keep it fair" }, doc!.Rules);
        var only = Assert.Single(doc.PackLimits);
        Assert.Equal("Herbivore", only.Category);
        Assert.Equal(new[] { "Dryosaurus" }, only.Species.Select(s => s.Name));
    }

    [Fact]
    public void UnusableInputIsNull()
    {
        Assert.Null(ServerRules.Parse("not json"));
        Assert.Null(ServerRules.Parse("null"));
        Assert.Null(ServerRules.Parse("""{"rules":[],"packLimits":[]}"""));
        Assert.Null(ServerRules.Parse("""{"source":"only"}"""));
    }
}
