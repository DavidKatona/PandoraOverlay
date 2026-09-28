using Xunit;

namespace PandoraOverlay.Tests;

public class ActivityLogTests
{
    private static readonly DateTime T0 = new(2026, 9, 28, 18, 0, 0, DateTimeKind.Utc);

    private static FeedLine Line(string text, DateTime at, bool mine = false) => new(at, text, null, FeedKind.Roster, mine);

    [Fact]
    public void NewestFirstAndABatchKeepsItsOrder()
    {
        var log = new ActivityLog();
        log.Post(Line("first", T0));
        log.Post(new[] { Line("batch-a", T0.AddSeconds(6)), Line("batch-b", T0.AddSeconds(6)) });
        log.Post(Line("last", T0.AddSeconds(12)));

        Assert.Equal(new[] { "last", "batch-a", "batch-b", "first" }, log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void PostedRaisesWithTheBatchAndNotForEmptyOnes()
    {
        var log = new ActivityLog();
        var raised = new List<IReadOnlyList<FeedLine>>();
        log.Posted += batch => raised.Add(batch);

        log.Post(Array.Empty<FeedLine>());
        Assert.Empty(raised);
        Assert.Empty(log.Lines);

        log.Post(new[] { Line("a", T0), Line("b", T0) });
        var batch = Assert.Single(raised);
        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void LinesExpireAndFadeWithAge()
    {
        var log = new ActivityLog();
        log.Post(Line("a", T0));
        var line = log.Lines[0];
        Assert.Equal(1.0, ActivityLog.AgeOpacity(line, T0), 3);
        Assert.Equal(0.675, ActivityLog.AgeOpacity(line, T0.AddMinutes(5)), 3);
        Assert.Equal(0.35, ActivityLog.AgeOpacity(line, T0.AddMinutes(30)), 3); // never below the floor
        Assert.False(log.Expire(T0.AddMinutes(9)));
        Assert.True(log.Expire(T0.AddMinutes(10)));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void KeepsAtMostFiftyLines()
    {
        var log = new ActivityLog();
        for (var i = 0; i < 60; i++) log.Post(Line($"l{i}", T0.AddSeconds(i)));
        Assert.Equal(ActivityLog.Capacity, log.Lines.Count);
        Assert.Equal("l59", log.Lines[0].Text);
        Assert.Equal("l10", log.Lines[^1].Text); // the oldest ten fell off
    }
}
