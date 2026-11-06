using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The route's entries in the map menu: which show when, and that each acts (PLAN.md, The screens).</summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class MapMenuRouteTests
{
    private const double Km = 100_000;

    private readonly OverlayConfig _config = new();
    private readonly WaypointLibrary _library = new();
    private readonly MapActions _actions;
    private readonly List<string> _notices = new();

    public MapMenuRouteTests() => _actions = new MapActions(_config, _library, new FriendBook(), name => name);

    private Waypoint Spot(string name, double x) => _library.Add(name, x, 0, 0)!;

    /// <summary>Fills a menu for a spot: the entries, as the map would have them.</summary>
    private StackPanel Menu(Waypoint? on = null, (double X, double Y)? world = null)
    {
        var items = new StackPanel();
        var builder = new MapMenuBuilder(items, new Style(typeof(Button)), () => Brushes.Gray, _actions, _ => Brushes.White,
                                         () => null, () => { }, _notices.Add);
        builder.Build(new MapMenuSpot(world ?? (on is null ? (5, 5) : (on.X, on.Y)), on, null));
        return items;
    }

    private static string Text(Button button) => ((StackPanel)button.Content).Children.OfType<TextBlock>().Single().Text;

    private static List<string> Texts(StackPanel menu) => menu.Children.OfType<Button>().Select(Text).ToList();

    private static Button Entry(StackPanel menu, string text) => menu.Children.OfType<Button>().Single(b => Text(b) == text);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void EmptyGroundWithNoRouteOffersOnlyToStartOne() => TheThemeOnScreen.OnSta(() =>
    {
        Assert.Equal(new[] { "Waypoint here", "Add stop here", "Copy this spot", "Copy my position", "Paste waypoint" }, Texts(Menu()));
    });

    [Fact]
    public void AWaypointNotOnTheRouteCanBeAddedEitherWay() => TheThemeOnScreen.OnSta(() =>
    {
        var w = Spot("A", Km);

        var texts = Texts(Menu(on: w));

        Assert.Contains("Add to route", texts);
        Assert.Contains("Add as next stop", texts);
        Assert.DoesNotContain("Remove from route", texts);
        Assert.DoesNotContain("Go to A", texts);
    });

    [Fact]
    public void AnUpcomingStopCanBeGoneToOrRemoved() => TheThemeOnScreen.OnSta(() =>
    {
        var (a, b) = (Spot("A", Km), Spot("B", 2 * Km));
        _actions.AddToRoute(a);
        _actions.AddToRoute(b);

        var texts = Texts(Menu(on: b));

        Assert.Contains("Go to B", texts);
        Assert.Contains("Remove from route", texts);
        Assert.Contains("Add to route", texts); // a waypoint may be on a route twice
    });

    [Fact]
    public void TheCurrentStopHasNoGoToItIsAlreadyThere() => TheThemeOnScreen.OnSta(() =>
    {
        var a = Spot("A", Km);
        _actions.AddToRoute(a);

        var texts = Texts(Menu(on: a));

        Assert.DoesNotContain("Go to A", texts);
        Assert.Contains("Remove from route", texts);
    });

    [Fact]
    public void WithARouteRunningAnywhereOffersToSkipTheCurrentStop() => TheThemeOnScreen.OnSta(() =>
    {
        _actions.AddToRoute(Spot("A", Km));

        var texts = Texts(Menu());

        Assert.Contains("Skip A", texts);
        Assert.DoesNotContain("Resume route", texts);
    });

    [Fact]
    public void APausedRouteAlsoOffersToResume() => TheThemeOnScreen.OnSta(() =>
    {
        var a = Spot("A", Km);
        _actions.AddToRoute(a);
        _actions.Track(null);

        Click(Entry(Menu(), "Resume route"));

        Assert.Equal(a.Id, _config.TrackedWaypointId);
    });

    [Fact]
    public void AFinishedRouteHasNothingToSkipOrResume() => TheThemeOnScreen.OnSta(() =>
    {
        _actions.AddToRoute(Spot("A", Km));
        _actions.SkipStop();

        var texts = Texts(Menu());

        Assert.DoesNotContain(texts, t => t.StartsWith("Skip"));
        Assert.DoesNotContain("Resume route", texts);
    });

    [Fact]
    public void AFullRouteGreysTheEntriesThatAdd() => TheThemeOnScreen.OnSta(() =>
    {
        var a = Spot("A", Km);
        for (var i = 0; i < Route.Capacity; i++) _actions.AddToRoute(a);

        var menu = Menu(on: a);

        Assert.False(Entry(menu, "Add to route").IsEnabled);
        Assert.False(Entry(menu, "Add as next stop").IsEnabled);
        Assert.False(Entry(menu, "Add stop here").IsEnabled);
        Assert.True(Entry(menu, "Waypoint here").IsEnabled);
    });

    [Fact]
    public void AFullLibraryGreysAddStopHereToo() => TheThemeOnScreen.OnSta(() =>
    {
        for (var i = 0; i < WaypointLibrary.Capacity; i++) _library.Add($"w{i}", i * 100, 0, 0);

        Assert.False(Entry(Menu(), "Add stop here").IsEnabled);
    });

    [Fact]
    public void AddToRouteActsAndSaysWhichStopItIs() => TheThemeOnScreen.OnSta(() =>
    {
        var a = Spot("A", Km);

        Click(Entry(Menu(on: a), "Add to route"));

        Assert.Equal(a.Id, _actions.Route.CurrentStop);
        Assert.Equal("A is stop 1", Assert.Single(_notices));
    });

    [Fact]
    public void AddStopHereMakesAWaypointAtTheSpot() => TheThemeOnScreen.OnSta(() =>
    {
        Click(Entry(Menu(world: (1000, 2000)), "Add stop here"));

        var added = Assert.Single(_library.Items);
        Assert.Equal((1000.0, 2000.0), (added.X, added.Y));
        Assert.Equal(added.Id, _actions.Route.CurrentStop);
    });

    [Fact]
    public void GoToAndSkipAndRemoveAct() => TheThemeOnScreen.OnSta(() =>
    {
        var (a, b, c) = (Spot("A", Km), Spot("B", 2 * Km), Spot("C", 3 * Km));
        foreach (var w in new[] { a, b, c }) _actions.AddToRoute(w);

        Click(Entry(Menu(on: c), "Go to C"));
        Assert.Equal(c.Id, _config.TrackedWaypointId);

        Click(Entry(Menu(on: a), "Go to A"));
        Assert.Equal(a.Id, _config.TrackedWaypointId);

        Click(Entry(Menu(), "Skip A"));
        Assert.Equal(b.Id, _config.TrackedWaypointId);

        Click(Entry(Menu(on: b), "Remove from route"));
        Assert.Equal(c.Id, _config.TrackedWaypointId);
    });
}
