using PandoraOverlay.Games.Engine;
using Xunit;

namespace PandoraOverlay.Games.Tests;

/// <summary>An animation's frame at a tick: looping, held at its end, before its start; and the rules it is made under.</summary>
public class SpriteAnimationTests
{
    private static readonly PixelBuffer Sheet = new(3, 1);
    private static readonly Sprite A = new("a", Sheet, new PixelRect(0, 0, 1, 1), 0, 0);
    private static readonly Sprite B = new("b", Sheet, new PixelRect(1, 0, 1, 1), 0, 0);
    private static readonly Sprite C = new("c", Sheet, new PixelRect(2, 0, 1, 1), 0, 0);

    /// <summary>A for 3 ticks, then B for 5, round and round: a pass is 8 ticks.</summary>
    [Theory]
    [InlineData(0, "a")]
    [InlineData(2, "a")]
    [InlineData(3, "b")]
    [InlineData(7, "b")]
    [InlineData(8, "a")]
    [InlineData(10, "a")]
    [InlineData(11, "b")]
    [InlineData(15, "b")]
    [InlineData(16, "a")]
    [InlineData(int.MaxValue, "b")] // 2147483647 % 8 = 7
    [InlineData(-1, "a")]           // not started yet: the first frame
    [InlineData(int.MinValue, "a")]
    public void ALoopingAnimationComesRoundAgain(int tick, string frame)
    {
        var walk = new SpriteAnimation("x/walk", new[] { A, B }, new[] { 3, 5 }, Loops: true);

        Assert.Equal(8, walk.TotalTicks);
        Assert.Equal(frame, walk.At(tick).Name);
    }

    /// <summary>B for 2 ticks, then A for 2, then A for ever.</summary>
    [Theory]
    [InlineData(0, "b")]
    [InlineData(1, "b")]
    [InlineData(2, "a")]
    [InlineData(3, "a")]
    [InlineData(4, "a")]
    [InlineData(1000, "a")]
    [InlineData(int.MaxValue, "a")]
    [InlineData(-5, "b")]
    public void AHeldAnimationStopsOnItsLastFrame(int tick, string frame)
    {
        var blink = new SpriteAnimation("x/blink", new[] { B, A }, new[] { 2, 2 }, Loops: false);

        Assert.Equal(4, blink.TotalTicks);
        Assert.Equal(frame, blink.At(tick).Name);
    }

    [Fact]
    public void EveryFrameShowsForItsOwnTicks()
    {
        var run = new SpriteAnimation("x/run", new[] { A, B, C }, new[] { 1, 1, 1 }, Loops: true);

        Assert.Equal(new[] { "a", "b", "c", "a", "b" }, Enumerable.Range(0, 5).Select(t => run.At(t).Name));
    }

    [Fact]
    public void OneFrameIsAStillPicture()
    {
        var stand = new SpriteAnimation("x/stand", new[] { C }, new[] { 1 }, Loops: true);

        Assert.All(new[] { -1, 0, 1, 99 }, t => Assert.Same(C, stand.At(t)));
    }

    [Fact]
    public void ItsListsAreItsOwn()
    {
        var frames = new[] { A, B };
        var ticks = new[] { 3, 5 };
        var walk = new SpriteAnimation("x/walk", frames, ticks, Loops: true);

        frames[0] = C;
        ticks[0] = 100;

        Assert.Same(A, walk.Frames[0]);
        Assert.Equal(3, walk.Ticks[0]);
        Assert.Equal("a", walk.At(0).Name);
        Assert.Equal(8, walk.TotalTicks);
    }

    [Fact]
    public void TheRulesAreCheckedWhenItIsMade()
    {
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("none", Array.Empty<Sprite>(), Array.Empty<int>(), true));
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("short", new[] { A, B }, new[] { 3 }, true));
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("long", new[] { A }, new[] { 3, 5 }, true));
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("zero", new[] { A, B }, new[] { 3, 0 }, true));
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("negative", new[] { A }, new[] { -1 }, false));
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("too long", new[] { A, B }, new[] { int.MaxValue, 1 }, true));
        Assert.Throws<ArgumentException>(() => new SpriteAnimation("a hole", new Sprite[] { A, null! }, new[] { 1, 1 }, true));
        Assert.Throws<ArgumentNullException>(() => new SpriteAnimation("no frames", null!, new[] { 1 }, true));
        Assert.Throws<ArgumentNullException>(() => new SpriteAnimation("no ticks", new[] { A }, null!, true));
    }
}
