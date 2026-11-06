using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>
/// Settings → Route, the + on the Waypoints page and the tick on General
/// (PLAN.md, The screens), through the dialog's real controls: a click is a
/// click, and every dialog here has a counting fake behind it, so a page that
/// asked the website would fail on the count (the route asks nothing of it).
/// </summary>
[Collection(TheThemeOnScreen.Name)]
public sealed class SettingsRouteTests
{
    private const double Km = 100_000;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Page
    {
        public required OverlayConfig Config { get; init; }
        public required WaypointLibrary Library { get; init; }
        public required MapActions Actions { get; init; }
        public required SettingsWindow Dialog { get; init; }
        public required CountingSite Site { get; init; }

        public Route Route => Actions.Route;

        public T Named<T>(string name) where T : class => (T)Dialog.FindName(name);

        public void Show(string page) => Call("SetPage", page);

        public void Call(string method, params object[] args) =>
            (typeof(SettingsWindow).GetMethods(Any).Single(m => m.Name == method && m.GetParameters().Length == args.Length)).Invoke(Dialog, args);

        public T Field<T>(string name) => (T)(typeof(SettingsWindow).GetField(name, Any) ?? throw new MissingFieldException(name)).GetValue(Dialog)!;

        /// <summary>The batches still waiting: what a real dialog does in its idle moments.</summary>
        public void Idle() => Dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        public List<Grid> Rows => Named<StackPanel>("RouteRows").Children.OfType<Grid>().ToList();

        public static TextBlock Cell(Grid row, int column) => row.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == column);

        public static string Mark(Grid row) => Cell(row, 1).Text;

        public static string Number(Grid row) => Cell(row, 2).Text;

        public static string Distance(Grid row) => Cell(row, 5).Text;

        public static Button NameButton(Grid row) => row.Children.OfType<Button>().Single(b => Grid.GetColumn(b) == 4);

        public static string Name(Grid row) => ((TextBlock)NameButton(row).Content).Text;

        public static Button Remove(Grid row) => row.Children.OfType<Button>().Single(b => Grid.GetColumn(b) == 6);

        public string Band => Named<TextBlock>("RouteBandText").Text;
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    /// <summary>A dialog over a library of these names, with the first ones on the route, on the Route page.</summary>
    private static Page Open(string[] names, int stops, string page = "Route")
    {
        var config = new OverlayConfig();
        var library = new WaypointLibrary();
        for (var i = 0; i < names.Length; i++) library.Add(names[i], (i + 1) * Km, 0, i);
        var book = new FriendBook();
        var actions = new MapActions(config, library, book, name => name);
        foreach (var w in library.Items.Take(stops)) actions.AddToRoute(w);
        var poll = new PollService(config);
        var site = CountingSite.Install(poll);
        var dialog = new SettingsWindow(config, library, book, poll, actions: actions);
        var opened = new Page { Config = config, Library = library, Actions = actions, Dialog = dialog, Site = site };
        opened.Show(page); // no session opens on Account, whichever page is asked for
        return opened;
    }

    private static readonly string[] AToD = { "A", "B", "C", "D" };

    [Fact]
    public void TheRowsSayWhichStopIsWhich() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        page.Actions.SkipStop();
        var rows = page.Rows;

        Assert.Equal(new[] { "1", "2", "3" }, rows.Select(Page.Number));
        Assert.Equal(new[] { "A", "B", "C" }, rows.Select(Page.Name));
        Assert.Equal(new[] { "✓", "▶", "" }, rows.Select(Page.Mark));
        Assert.Equal("3 / 64", page.Named<TextBlock>("RouteCount").Text);
        page.Dialog.Close();
    });

    [Fact]
    public void TheBandAndTheCurrentRowCountWhatIsLeftOnlyWithAPosition() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        Assert.Equal("STOP 1 OF 3", page.Band);
        Assert.Equal("", Page.Distance(page.Rows[0]));

        page.Actions.RouteAt(0, 0); // straight along the line of stops: 1 km to A, then 1 and 1
        page.Call("RenderRoute");

        Assert.Equal(FormattableString.Invariant($"STOP 1 OF 3 · {3.0:0.0} KM LEFT"), page.Band.Replace(',', '.'));
        Assert.Equal(FormattableString.Invariant($"{1.0:0.0} km"), Page.Distance(page.Rows[0]).Replace(',', '.'));
        Assert.Equal("", Page.Distance(page.Rows[1]));
        page.Dialog.Close();
    });

    [Fact]
    public void TheBandSaysPausedAndFinishedInItsOwnWords()
    {
        Assert.Equal("STOP 3 OF 7", SettingsWindow.RouteBandWords(2, 7, paused: false, finished: false, metersLeft: null));
        Assert.Equal("STOP 3 OF 7 · 850 M LEFT", SettingsWindow.RouteBandWords(2, 7, paused: false, finished: false, metersLeft: 850));
        Assert.Equal("PAUSED · STOP 3 OF 7", SettingsWindow.RouteBandWords(2, 7, paused: true, finished: false, metersLeft: 850));
        Assert.Equal("ROUTE FINISHED", SettingsWindow.RouteBandWords(7, 7, paused: false, finished: true, metersLeft: null));
    }

    [Fact]
    public void AnEmptyRouteSaysHowToStartAndOffersNothingToActOn() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 0);

        Assert.Equal(Visibility.Visible, page.Named<FrameworkElement>("RouteEmpty").Visibility);
        Assert.Equal(Visibility.Collapsed, page.Named<FrameworkElement>("RouteList").Visibility);
        Assert.False(page.Named<Button>("RouteRestartButton").IsEnabled);
        Assert.False(page.Named<Button>("RouteClearButton").IsEnabled);
        page.Dialog.Close();
    });

    [Fact]
    public void ClickingAStopGoesThereAndTheWaypointsPageFollows() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var c = page.Library.Items.Single(w => w.Name == "C");

        Click(Page.NameButton(page.Rows[2]));

        Assert.Equal(2, page.Route.Current);
        Assert.Equal(c.Id, page.Config.TrackedWaypointId);
        Assert.Equal(c.Id, page.Field<Guid?>("_draftTracked"));
        Assert.Equal(new[] { "✓", "✓", "▶" }, page.Rows.Select(Page.Mark));
        page.Dialog.Close();
    });

    [Fact]
    public void ARowsCrossRemovesTheStopAndNeverTheWaypoint() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);

        Click(Page.Remove(page.Rows[1]));

        Assert.Equal(new[] { "A", "C" }, page.Rows.Select(Page.Name));
        Assert.Equal(new[] { "1", "2" }, page.Rows.Select(Page.Number));
        Assert.Equal(4, page.Library.Items.Count);
        page.Dialog.Close();
    });

    [Fact]
    public void RemovingTheCurrentStopHandsTheLineToTheNext() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);

        Click(Page.Remove(page.Rows[0]));

        Assert.Equal(page.Library.Items.Single(w => w.Name == "B").Id, page.Config.TrackedWaypointId);
        page.Dialog.Close();
    });

    [Fact]
    public void AMoveKeepsTheSameRowsItOnlyRewritesThem() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var before = page.Rows;

        page.Actions.MoveStop(2, 1);

        Assert.Equal(new[] { "A", "C", "B" }, page.Rows.Select(Page.Name));
        Assert.Equal(before, page.Rows); // the same elements: a rebuild would lose the reader's place
        page.Dialog.Close();
    });

    [Fact]
    public void ClearTakesTwoClicksAndAnythingElseDisarmsIt() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var clear = page.Named<Button>("RouteClearButton");

        Click(clear);
        Assert.Equal(3, page.Route.Count);
        Assert.Equal("Clear all?", clear.Content);

        Click(Page.NameButton(page.Rows[1]));
        Assert.Equal("Clear", clear.Content);

        Click(clear);
        Click(clear);
        Assert.True(page.Route.IsEmpty);
        Assert.Equal(4, page.Library.Items.Count);
        Assert.Null(page.Config.TrackedWaypointId); // the tracking was the route's
        page.Dialog.Close();
    });

    [Fact]
    public void RestartWalksItAgainFromTheFirstStop() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2);
        page.Actions.SkipStop();
        page.Actions.SkipStop();
        Assert.Equal("ROUTE FINISHED", page.Band);

        Click(page.Named<Button>("RouteRestartButton"));

        Assert.Equal(0, page.Route.Current);
        Assert.Equal(page.Library.Items[0].Id, page.Config.TrackedWaypointId);
        page.Dialog.Close();
    });

    [Fact]
    public void APausedRouteOffersResumeInTheHintsPlace() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2);
        var resume = page.Named<Button>("RouteResumeButton");
        Assert.Equal(Visibility.Collapsed, resume.Visibility);

        page.Actions.Track(page.Library.Items[3].Id);

        Assert.Equal(Visibility.Visible, resume.Visibility);
        Assert.Equal(Visibility.Collapsed, page.Named<FrameworkElement>("RouteHint").Visibility);
        Assert.StartsWith("PAUSED", page.Band);

        Click(resume);

        Assert.Equal(page.Library.Items[0].Id, page.Config.TrackedWaypointId);
        Assert.Equal(Visibility.Collapsed, resume.Visibility);
        page.Dialog.Close();
    });

    [Fact]
    public void AStopReachedBehindTheDialogShowsAndALaterWaypointEditKeepsTheRoute() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3, page: "Waypoints");
        page.Show("Route");
        var (a, b) = (page.Library.Items[0], page.Library.Items[1]);

        page.Actions.RouteAt(a.X, a.Y); // the poll keeps running while the dialog is open

        Assert.Equal(new[] { "✓", "▶", "" }, page.Rows.Select(Page.Mark));
        Assert.Equal(b.Id, page.Field<Guid?>("_draftTracked"));

        // An edit on the Waypoints page flushes the page's tracked waypoint into the config: it must be the new stop.
        page.Show("Waypoints");
        page.Call("WaypointsEdited");
        page.Call("FlushChanges");

        Assert.Equal(b.Id, page.Config.TrackedWaypointId);
        Assert.True(page.Actions.RouteRunning);
        Assert.True(page.Field<Dictionary<Guid, RadioButton>>("_rowTrackRadios")[b.Id].IsChecked);
        page.Dialog.Close();
    });

    [Fact]
    public void TrackingAnotherWaypointThereKeepsTheRouteWaiting() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2, page: "Waypoints");
        var d = page.Library.Items[3];

        Click(page.Field<Dictionary<Guid, RadioButton>>("_rowTrackRadios")[d.Id]);
        page.Call("FlushChanges");

        Assert.Equal(d.Id, page.Config.TrackedWaypointId);
        Assert.True(page.Actions.RoutePaused);
        page.Dialog.Close();
    });

    [Fact]
    public void DeletingTheCurrentStopsWaypointThereHandsTheLineOn() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2, page: "Waypoints");
        var (a, b) = (page.Library.Items[0], page.Library.Items[1]);
        var cards = page.Named<StackPanel>("WaypointRows");
        var body = (StackPanel)((Border)cards.Children[0]).Child;
        var row = (Grid)body.Children[1]; // the band is child 0
        Click(row.Children.OfType<Button>().Single(x => Grid.GetColumn(x) == 5));
        page.Call("FlushChanges");

        Assert.DoesNotContain(page.Route.Stops, id => id == a.Id);
        Assert.Equal(b.Id, page.Config.TrackedWaypointId);
        Assert.True(page.Field<Dictionary<Guid, RadioButton>>("_rowTrackRadios")[b.Id].IsChecked);
        page.Dialog.Close();
    });

    [Fact]
    public void AfterAPauseThereDeletingTheStopDoesNotTakeTheLine() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2, page: "Waypoints");
        var (a, d) = (page.Library.Items[0], page.Library.Items[3]);
        Click(page.Field<Dictionary<Guid, RadioButton>>("_rowTrackRadios")[d.Id]);
        page.Call("FlushChanges"); // paused, and no poll since: out of game

        var body = (StackPanel)((Border)page.Named<StackPanel>("WaypointRows").Children[0]).Child;
        Click(((Grid)body.Children[1]).Children.OfType<Button>().Single(x => Grid.GetColumn(x) == 5));
        page.Call("FlushChanges");

        Assert.DoesNotContain(page.Route.Stops, id => id == a.Id);
        Assert.Equal(d.Id, page.Config.TrackedWaypointId);
        page.Dialog.Close();
    });

    [Fact]
    public void TheWaypointsPagesPlusAddsAStopAndSaysWhich() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2, page: "Waypoints");
        var body = (StackPanel)((Border)page.Named<StackPanel>("WaypointRows").Children[0]).Child;
        var c = (Grid)body.Children[3]; // the third waypoint's row

        Click(c.Children.OfType<Button>().Single(x => Grid.GetColumn(x) == 4));

        Assert.Equal(page.Library.Items[2].Id, page.Route.Stops[^1]);
        var status = page.Named<TextBlock>("WaypointStatus");
        Assert.Equal("C added to the route: stop 3.", status.Text);
        Assert.Equal(Visibility.Visible, status.Visibility);
        page.Dialog.Close();
    });

    [Fact]
    public void AWaypointNotYetInTheLibraryStaysOnTheRoute() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 0, page: "Waypoints");
        var imported = new Waypoint { Id = Guid.NewGuid(), Name = "New", X = 9 * Km, Y = 0 };
        page.Field<List<Waypoint>>("_draft").Add(imported);
        page.Call("WaypointsEdited"); // what an import does: in the page's copy, not yet in the library

        page.Call("AddWaypointToRoute", imported);
        page.Call("WaypointsEdited");
        page.Call("FlushChanges");

        Assert.Equal(imported.Id, Assert.Single(page.Route.Stops));
        Assert.NotNull(page.Library.Find(imported.Id));
        page.Dialog.Close();
    });

    [Fact]
    public void AFullRouteAddsNothingAndSaysSo() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 0, page: "Waypoints");
        var a = page.Library.Items[0];
        for (var i = 0; i < Route.Capacity; i++) page.Actions.AddToRoute(a);
        var body = (StackPanel)((Border)page.Named<StackPanel>("WaypointRows").Children[0]).Child;

        Click(((Grid)body.Children[2]).Children.OfType<Button>().Single(x => Grid.GetColumn(x) == 4));

        Assert.Equal(Route.Capacity, page.Route.Count);
        Assert.StartsWith("The route is full", page.Named<TextBlock>("WaypointStatus").Text);
        page.Dialog.Close();
    });

    [Fact]
    public void ALongRouteArrivesInBatchesWithAFullListAtTheEnd() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 0, page: "General");
        var a = page.Library.Items[0];
        for (var i = 0; i < Route.Capacity; i++) page.Actions.AddToRoute(a);

        page.Show("Route");
        Assert.True(page.Rows.Count < Route.Capacity);

        while (page.Rows.Count < Route.Capacity) page.Idle();

        Assert.Equal("64", Page.Number(page.Rows[^1]));
        page.Dialog.Close();
    });

    [Fact]
    public void NothingIsBuiltBeforeTheFirstLook() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3, page: "General");

        page.Actions.SkipStop();

        Assert.Empty(page.Rows);
        page.Dialog.Close();
    });

    [Fact]
    public void AClosedDialogHearsNothingMore() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var rows = page.Rows;
        page.Dialog.Close();

        page.Actions.SkipStop();

        Assert.Equal("▶", Page.Mark(rows[0])); // as it was drawn
    });

    // ---- Dragging ---------------------------------------------------------------

    /// <summary>The dialog laid out, so the rows have heights, and the height of one.</summary>
    private static double LaidOut(Page page)
    {
        var root = (FrameworkElement)page.Dialog.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        var rows = page.Named<StackPanel>("RouteRows");
        return rows.ActualHeight / rows.Children.Count;
    }

    /// <summary>A drag of one row to a gap, as the mouse would do it: lifted, moved over the gap, dropped.</summary>
    private static void Drag(Page page, int from, int gap, double rowHeight)
    {
        page.Call("BeginRouteDrag", from);
        page.Call("MoveRouteDrag", gap * rowHeight);
        page.Call("DropRouteDrag");
    }

    [Theory]
    [InlineData(0, 0)]    // above the top: the first gap
    [InlineData(-50, 0)]
    [InlineData(35, 1)]   // nearer the gap below the first row
    [InlineData(55, 2)]
    [InlineData(999, 4)]  // below the last: the last gap
    public void AHeightMeansTheNearestGap(double y, int gap) =>
        Assert.Equal(gap, SettingsWindow.RouteDropSlot(y, rowHeight: 24, current: 0, count: 4));

    [Fact]
    public void AGapAbovePassedStopsLandsUnderThem() =>
        Assert.Equal(2, SettingsWindow.RouteDropSlot(0, rowHeight: 24, current: 2, count: 4));

    [Theory]
    [InlineData(1, 3, 2)] // down: the stop is lifted out first, so what's below moves up
    [InlineData(3, 1, 1)] // up
    [InlineData(2, 2, 2)] // the gap above itself
    [InlineData(2, 3, 2)] // the gap below itself
    public void AGapBecomesAMoveTarget(int from, int gap, int to) =>
        Assert.Equal(to, SettingsWindow.RouteMoveTarget(from, gap));

    [Fact]
    public void DraggingAStopDownMovesIt() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Drag(page, from: 1, gap: 3, height); // between C and D

        Assert.Equal(new[] { "A", "C", "B", "D" }, page.Rows.Select(Page.Name));
        Assert.Equal(0, page.Route.Current);
        page.Dialog.Close();
    });

    [Fact]
    public void DraggingAStopToTheCurrentPlaceMakesItCurrentAndTracked() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var d = page.Library.Items.Single(w => w.Name == "D");

        Drag(page, from: 3, gap: 0, height);

        Assert.Equal(new[] { "D", "A", "B", "C" }, page.Rows.Select(Page.Name));
        Assert.Equal(d.Id, page.Config.TrackedWaypointId);
        page.Dialog.Close();
    });

    [Fact]
    public void DraggingTheCurrentStopDownHandsTheLineToTheNext() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var height = LaidOut(page);
        var b = page.Library.Items.Single(w => w.Name == "B");

        Drag(page, from: 0, gap: 3, height);

        Assert.Equal(new[] { "B", "C", "A" }, page.Rows.Select(Page.Name));
        Assert.Equal(b.Id, page.Config.TrackedWaypointId);
        page.Dialog.Close();
    });

    [Fact]
    public void AStopCantLandAmongThePassedOnes() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        page.Actions.SkipStop();
        page.Actions.SkipStop(); // at C
        var height = LaidOut(page);

        Drag(page, from: 3, gap: 0, height);

        Assert.Equal(new[] { "A", "B", "D", "C" }, page.Rows.Select(Page.Name));
        page.Dialog.Close();
    });

    [Fact]
    public void APassedStopHasNoGripAndTakesNoDrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        page.Actions.SkipStop();
        LaidOut(page);
        var first = page.Rows[0];

        first.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
        });

        Assert.Equal(-1, page.Field<int>("_routePressed"));
        Assert.Equal(Visibility.Hidden, ((Border)first.Children[0]).Child.Visibility);
        Assert.Equal(Visibility.Visible, ((Border)page.Rows[1].Children[0]).Child.Visibility);
        page.Dialog.Close();
    });

    [Fact]
    public void AnEndedDragMovesNothingAndLeavesNothingBehind() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var line = page.Named<FrameworkElement>("RouteDropLine");
        var layer = page.Named<Canvas>("RouteDragLayer");

        page.Call("BeginRouteDrag", 1);
        page.Call("MoveRouteDrag", 3 * height);
        Assert.Equal(Visibility.Visible, line.Visibility);
        Assert.Equal(2, layer.Children.Count); // the line and the carried row
        Assert.Equal(0.4, page.Rows[1].Opacity);

        page.Call("EndRouteDrag"); // Esc, Alt+Tab, the dialog closing

        Assert.Equal(new[] { "A", "B", "C", "D" }, page.Rows.Select(Page.Name));
        Assert.Equal(Visibility.Collapsed, line.Visibility);
        Assert.Single(layer.Children);
        Assert.Equal(1, page.Rows[1].Opacity);
        Assert.Null(System.Windows.Input.Mouse.OverrideCursor);
        page.Dialog.Close();
    });

    [Fact]
    public void AStopPassedWhileBeingCarriedEndsTheDrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        page.Call("BeginRouteDrag", 0);
        page.Call("MoveRouteDrag", 3 * height);
        page.Actions.SkipStop(); // reached behind the dialog

        Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "B", "C", "D" }, page.Rows.Select(Page.Name));
        page.Dialog.Close();
    });

    [Fact]
    public void ClosingTheDialogEndsADrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        page.Call("BeginRouteDrag", 1);
        page.Call("MoveRouteDrag", 3 * height);

        page.Dialog.Close();

        Assert.Equal(new[] { "A", "B", "C", "D" }, page.Rows.Select(Page.Name));
        Assert.Null(System.Windows.Input.Mouse.OverrideCursor);
    });

    [Fact]
    public void TheChimeTickWritesItsSettingAtOnce() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 0, page: "General");
        var tick = page.Named<CheckBox>("RouteChimeCheck");
        Assert.False(tick.IsChecked); // off until ticked

        tick.IsChecked = true;
        Click(tick);

        Assert.True(page.Config.RouteChimeEnabled);
        page.Dialog.Close();
    });

    [Fact]
    public void ThePageAsksTheWebsiteNothing() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);

        Click(Page.NameButton(page.Rows[1]));
        Click(Page.Remove(page.Rows[2]));
        page.Actions.RouteAt(0, 0);
        Click(page.Named<Button>("RouteRestartButton"));

        Assert.Equal(0, page.Site.Requests);
        page.Dialog.Close();
    });
}
