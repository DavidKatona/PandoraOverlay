using Xunit;

namespace PandoraOverlay.Tests;

public class FriendFeedTests
{
    private static readonly DateTime T0 = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    // Friends stand a kilometre away unless a test says otherwise, so proximity stays out of the picture.
    private static FriendState F(string id, bool inGame = true, string? dino = "Deinosuchus", double growth = 0.4,
                                 double? x = 100_000, double? y = 0, bool legs = false, bool hide = false, string? name = null) =>
        new(id, name ?? $"player-{id}", dino, "Male", growth, 1, 1, 1, 1, 0, false, false, legs, x, y, inGame, hide);

    private static PlayerState Me(double x = 0, double y = 0) =>
        new("me", "Dave", "Deinosuchus", "Male", 0.5, 1, 1, 1, 1, 0, false, false, false, x, y, 0);

    private static IReadOnlyList<FeedLine> Feed(FriendFeed feed, DateTime at, PlayerState? me, params FriendState[] roster) =>
        feed.Update(roster, me, (id, site) => site ?? id, _ => true, at);

    [Fact]
    public void FirstRosterSeedsOneLineNamingWhoIsOn()
    {
        var feed = new FriendFeed();
        var lines = Feed(feed, T0, Me(), F("a"), F("b", dino: "Ceratosaurus"), F("c", inGame: false));

        var line = Assert.Single(lines);
        Assert.Equal(FeedKind.Roster, line.Kind);
        Assert.Equal("In game: player-a (Deinosuchus), player-b (Ceratosaurus)", line.Text);
        Assert.Equal(3, feed.Total);
        Assert.Equal(2, feed.InGame);
        Assert.Equal(new[] { "player-a", "player-b" }, feed.InGameNames);
    }

    [Fact]
    public void NobodyOnSeedsNothing()
    {
        var feed = new FriendFeed();
        Assert.Empty(Feed(feed, T0, Me(), F("a", inGame: false)));
        Assert.Empty(feed.Lines);
        Assert.Equal(1, feed.Total);
        Assert.Equal(0, feed.InGame);
    }

    [Fact]
    public void SpawnAndLeaveAreReportedNewestFirst()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", inGame: false));

        var spawned = Assert.Single(Feed(feed, T0.AddSeconds(6), Me(), F("a", growth: 0.42)));
        Assert.Equal(FeedKind.Spawned, spawned.Kind);
        Assert.Equal("player-a spawned as Deinosuchus 42%", spawned.Text);
        Assert.Equal("a", spawned.SteamId);

        var left = Assert.Single(Feed(feed, T0.AddSeconds(12), Me(), F("a", inGame: false)));
        Assert.Equal(FeedKind.Left, left.Kind);
        Assert.Equal("player-a is no longer in game", left.Text);

        Assert.Equal(new[] { left, spawned }, feed.Lines); // newest first
    }

    [Fact]
    public void SeveralLeavingAtOnceBecomeOneLine()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a"), F("b"), F("c"), F("d"));
        var lines = Feed(feed, T0.AddSeconds(6), Me(), F("a", inGame: false), F("b", inGame: false), F("c", inGame: false), F("d"));

        var line = Assert.Single(lines);
        Assert.Equal("player-a, player-b and 1 more are no longer in game", line.Text);
        Assert.Null(line.SteamId); // no single friend to colour it by

        var two = Assert.Single(Feed(feed, T0.AddSeconds(12), Me(), F("a", inGame: false), F("b", inGame: false), F("c", inGame: false), F("d", inGame: false)));
        Assert.Equal("player-d is no longer in game", two.Text);
        Assert.Equal("d", two.SteamId);
    }

    [Fact]
    public void SameSpeciesLowerGrowthIsAFreshLifeNotADeathClaim()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", growth: 0.61));
        Feed(feed, T0.AddSeconds(6), Me(), F("a", inGame: false));
        var line = Assert.Single(Feed(feed, T0.AddSeconds(12), Me(), F("a", growth: 0.2)));

        Assert.Equal(FeedKind.NewLife, line.Kind);
        Assert.Equal("player-a started a fresh Deinosuchus 20%", line.Text);
    }

    [Fact]
    public void RespawningWithHigherGrowthOrAnotherDinoIsJustASpawn()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", growth: 0.61));
        Feed(feed, T0.AddSeconds(6), Me(), F("a", inGame: false));
        Assert.Equal(FeedKind.Spawned, Assert.Single(Feed(feed, T0.AddSeconds(12), Me(), F("a", growth: 0.62))).Kind);
        Feed(feed, T0.AddSeconds(18), Me(), F("a", inGame: false));
        var other = Assert.Single(Feed(feed, T0.AddSeconds(24), Me(), F("a", dino: "Ceratosaurus", growth: 0.1)));
        Assert.Equal("player-a spawned as Ceratosaurus 10%", other.Text);
    }

    [Fact]
    public void GrowthStagesAreReportedOnlyForCrossingsSeenLive()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", growth: 0.49)); // baseline, no line about the stage
        var line = Assert.Single(Feed(feed, T0.AddSeconds(6), Me(), F("a", growth: 0.501)));
        Assert.Equal(FeedKind.Growth, line.Kind);
        Assert.Equal("player-a's Deinosuchus is now a subadult", line.Text);
        Assert.Empty(Feed(feed, T0.AddSeconds(12), Me(), F("a", growth: 0.51))); // nothing crossed
    }

    [Fact]
    public void FractureAndDinoChangeAreReported()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a"));
        var fracture = Assert.Single(Feed(feed, T0.AddSeconds(6), Me(), F("a", legs: true)));
        Assert.Equal(FeedKind.Fracture, fracture.Kind);
        Assert.Equal("player-a fractured their legs", fracture.Text);
        Assert.Empty(Feed(feed, T0.AddSeconds(12), Me(), F("a", legs: true))); // still fractured: not news

        var swap = Assert.Single(Feed(feed, T0.AddSeconds(18), Me(), F("a", dino: "Ceratosaurus", growth: 0.3, legs: true)));
        Assert.Equal(FeedKind.DinoChanged, swap.Kind);
        Assert.Equal("player-a is now a Ceratosaurus 30%", swap.Text);
    }

    [Fact]
    public void NearbyFiresOnceWithHysteresis()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", x: 100_000, y: 0)); // 1 km away
        var near = Assert.Single(Feed(feed, T0.AddSeconds(6), Me(), F("a", x: 15_000, y: 0))); // 150 m
        Assert.Equal(FeedKind.Nearby, near.Kind);
        Assert.Equal("player-a is nearby · 150 m", near.Text);

        Assert.Empty(Feed(feed, T0.AddSeconds(12), Me(), F("a", x: 25_000, y: 0))); // 250 m: still "near", no repeat
        Assert.Empty(Feed(feed, T0.AddSeconds(18), Me(), F("a", x: 35_000, y: 0))); // 350 m: re-armed quietly
        Assert.Single(Feed(feed, T0.AddSeconds(24), Me(), F("a", x: 10_000, y: 0)));  // back inside: fires again
    }

    [Fact]
    public void AFriendAlreadyBesideYouIsNotAnnouncedLater()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", x: 1000, y: 0), F("b", inGame: false)); // a: 10 m away at launch
        Assert.Empty(Feed(feed, T0.AddSeconds(6), Me(), F("a", x: 1200, y: 0), F("b", inGame: false)));

        // b spawns next to me: one spawn line, no "nearby" now or on the next fetch.
        var spawn = Assert.Single(Feed(feed, T0.AddSeconds(12), Me(), F("a", x: 1200, y: 0), F("b", x: 500, y: 0)));
        Assert.Equal(FeedKind.Spawned, spawn.Kind);
        Assert.Empty(Feed(feed, T0.AddSeconds(18), Me(), F("a", x: 1200, y: 0), F("b", x: 600, y: 0)));
    }

    [Fact]
    public void NearbyNeedsBothPositionsAndSharedLocation()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", x: 100_000, y: 0), F("b", x: 100_000, y: 0, hide: true));
        Assert.Empty(Feed(feed, T0.AddSeconds(6), me: null, F("a", x: 1000, y: 0), F("b", x: 1000, y: 0, hide: true)));
        var lines = Feed(feed, T0.AddSeconds(12), Me(), F("a", x: 1000, y: 0), F("b", x: 1000, y: 0, hide: true));
        Assert.Equal("a", Assert.Single(lines).SteamId); // the hidden friend's coordinates are never used
    }

    [Fact]
    public void RosterChangesAreReported()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a"));
        var added = Assert.Single(Feed(feed, T0.AddSeconds(6), Me(), F("a"), F("b", inGame: false)));
        Assert.Equal("New friend: player-b", added.Text);
        var removed = Assert.Single(Feed(feed, T0.AddSeconds(12), Me(), F("b", inGame: false)));
        Assert.Equal("player-a is no longer on your friends list", removed.Text);
        Assert.Equal(1, feed.Total);
    }

    [Fact]
    public void MutedFriendsProduceNoLinesButStayTracked()
    {
        var feed = new FriendFeed();
        feed.Update(new[] { F("a", inGame: false) }, Me(), (id, site) => site!, _ => false, T0);
        Assert.Empty(feed.Update(new[] { F("a", growth: 0.49) }, Me(), (id, site) => site!, _ => false, T0.AddSeconds(6)));
        // Unmuted now: the stage crossing is reported from the muted baseline, not re-seeded.
        var line = Assert.Single(feed.Update(new[] { F("a", growth: 0.5) }, Me(), (id, site) => site!, _ => true, T0.AddSeconds(12)));
        Assert.Equal(FeedKind.Growth, line.Kind);
    }

    [Fact]
    public void NicknamesComeFromTheCallback()
    {
        var feed = new FriendFeed();
        feed.Update(new[] { F("a", inGame: false) }, Me(), (id, _) => "Bestie", _ => true, T0);
        var line = Assert.Single(feed.Update(new[] { F("a") }, Me(), (id, _) => "Bestie", _ => true, T0.AddSeconds(6)));
        Assert.StartsWith("Bestie spawned", line.Text);
    }

    [Fact]
    public void LinesExpireAndFadeWithAge()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a"));
        var line = feed.Lines[0];
        Assert.Equal(1.0, FriendFeed.AgeOpacity(line, T0), 3);
        Assert.Equal(0.675, FriendFeed.AgeOpacity(line, T0.AddMinutes(5)), 3);
        Assert.False(feed.Expire(T0.AddMinutes(9)));
        Assert.True(feed.Expire(T0.AddMinutes(10)));
        Assert.Empty(feed.Lines);
    }

    [Fact]
    public void ResetBaselineSeedsAgainInsteadOfReportingEveryoneAsNew()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a"));
        feed.ResetBaseline();
        var line = Assert.Single(Feed(feed, T0.AddMinutes(1), Me(), F("a"), F("b")));
        Assert.Equal(FeedKind.Roster, line.Kind);
        Assert.StartsWith("In game:", line.Text);
    }

    [Fact]
    public void KeepsAtMostFiftyLines()
    {
        var feed = new FriendFeed();
        Feed(feed, T0, Me(), F("a", inGame: false));
        for (var i = 1; i <= 60; i++)
        {
            Feed(feed, T0.AddSeconds(i * 6), Me(), F("a", inGame: i % 2 == 1));
        }
        Assert.Equal(FriendFeed.Capacity, feed.Lines.Count);
    }
}
