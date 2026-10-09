using System.Windows;
using Xunit;

namespace PandoraOverlay.Tests;

public sealed class LabelLayoutTests
{
    private static LabelRequest Label(int id, double priority, params Rect[] spots) => new(id, priority, spots);

    private static int[] Ids(IReadOnlyList<PlacedLabel> placed) => placed.Select(p => p.Id).ToArray();

    [Fact]
    public void LabelsThatDontMeetAreAllPlaced()
    {
        var placed = LabelLayout.Place(new[]
        {
            Label(1, 1, new Rect(0, 0, 50, 12)),
            Label(2, 1, new Rect(60, 0, 50, 12)),
            Label(3, 1, new Rect(0, 20, 50, 12)),
        });

        Assert.Equal(new[] { 1, 2, 3 }, Ids(placed));
    }

    [Fact]
    public void OfTwoOverlappingLabelsTheHigherPriorityStays()
    {
        var placed = LabelLayout.Place(new[]
        {
            Label(1, 1, new Rect(0, 0, 50, 12)),
            Label(2, 5, new Rect(30, 5, 50, 12)),
        });

        Assert.Equal(new[] { 2 }, Ids(placed));
    }

    [Fact]
    public void EqualPrioritiesKeepTheCallersOrder()
    {
        var placed = LabelLayout.Place(new[]
        {
            Label(7, 2, new Rect(0, 0, 50, 12)),
            Label(3, 2, new Rect(10, 0, 50, 12)),
        });

        Assert.Equal(new[] { 7 }, Ids(placed));
    }

    [Fact]
    public void ALabelTakesItsNextSpotWhenTheFirstIsTaken()
    {
        var right = new Rect(10, 0, 50, 12);
        var left = new Rect(-60, 0, 50, 12);

        var placed = LabelLayout.Place(new[]
        {
            Label(1, 9, new Rect(20, 2, 40, 12)),
            Label(2, 1, right, left),
        });

        Assert.Equal(new[] { 1, 2 }, Ids(placed));
        Assert.Equal(left, placed[1].Box);
    }

    [Fact]
    public void ALabelWithNoFreeSpotIsLeftOut()
    {
        var placed = LabelLayout.Place(new[]
        {
            Label(1, 9, new Rect(0, 0, 200, 40)),
            Label(2, 1, new Rect(10, 5, 30, 12), new Rect(100, 20, 30, 12)),
        });

        Assert.Equal(new[] { 1 }, Ids(placed));
    }

    [Fact]
    public void ALabelLeftOutBlocksNothing()
    {
        // A overlaps B, B overlaps C, A and C are apart: B goes, and C stays.
        var placed = LabelLayout.Place(new[]
        {
            Label(1, 3, new Rect(0, 0, 50, 12)),
            Label(2, 2, new Rect(40, 0, 50, 12)),
            Label(3, 1, new Rect(80, 0, 50, 12)),
        });

        Assert.Equal(new[] { 1, 3 }, Ids(placed));
    }

    [Fact]
    public void BoxesThatOnlyTouchDontOverlapButTheGapKeepsThemApart()
    {
        var a = Label(1, 2, new Rect(0, 0, 50, 12));
        var b = Label(2, 1, new Rect(50, 0, 50, 12)); // edge to edge

        Assert.Equal(new[] { 1, 2 }, Ids(LabelLayout.Place(new[] { a, b })));
        Assert.Equal(new[] { 1 }, Ids(LabelLayout.Place(new[] { a, b }, gap: 3)));
        Assert.Equal(new[] { 1, 2 }, Ids(LabelLayout.Place(new[] { a, Label(2, 1, new Rect(53, 0, 50, 12)) }, gap: 3))); // exactly the gap apart
    }

    [Fact]
    public void ATopPriorityBoxKeepsNamesOffAMarker()
    {
        var marker = Label(-1, double.MaxValue, new Rect(45, 0, 6, 6));

        var placed = LabelLayout.Place(new[] { Label(1, 5, new Rect(0, 0, 50, 12), new Rect(0, 20, 50, 12)), marker });

        Assert.Equal(new[] { -1, 1 }, Ids(placed));
        Assert.Equal(new Rect(0, 20, 50, 12), placed[1].Box);
    }

    [Fact]
    public void ThePlacedLabelsComeBackInPriorityOrder()
    {
        var placed = LabelLayout.Place(new[]
        {
            Label(1, 1, new Rect(0, 0, 10, 10)),
            Label(2, 3, new Rect(20, 0, 10, 10)),
            Label(3, 2, new Rect(40, 0, 10, 10)),
        });

        Assert.Equal(new[] { 2, 3, 1 }, Ids(placed));
    }

    [Fact]
    public void NothingToPlaceAndEmptySpotsGiveNothing()
    {
        Assert.Empty(LabelLayout.Place(Array.Empty<LabelRequest>()));
        Assert.Empty(LabelLayout.Place(new[] { Label(1, 1, Rect.Empty), Label(2, 1) }));
    }
}
