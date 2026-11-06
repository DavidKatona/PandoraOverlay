using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>A carried stop held over a row, past the capture a window never shown can't take.</summary>
    private static void Carry(Page page, int from, int over, double rowHeight)
    {
        page.Call("StartRouteDrag", from);
        page.Call("MoveRouteDrag", (over + 0.5) * rowHeight);
    }

    /// <summary>A drag of one stop over a row, dropped there.</summary>
    private static void Drag(Page page, int from, int over, double rowHeight)
    {
        Carry(page, from, over, rowHeight);
        page.Call("DropRouteDrag");
    }

    /// <summary>The dialog really on screen, off to one side and never activated: the mouse's capture needs a live window. Moves no mouse and clicks nothing.</summary>
    private static void ShowOffScreen(Page page)
    {
        page.Dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        page.Dialog.Left = -5000;
        page.Dialog.Top = 0;
        page.Dialog.ShowActivated = false;
        page.Dialog.ShowInTaskbar = false;
        page.Dialog.Show();
        page.Dialog.UpdateLayout();
    }

    private static void RaiseCaptureLost(UIElement from) =>
        from.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.LostMouseCaptureEvent });

    /// <summary>
    /// The owner's bug (Nov 6 2026), for real: a press on a name leaves its button
    /// holding the capture, the drag takes it, and the button's loss must not end
    /// the drag. It used to, half-way through the set-up, which left the carried
    /// row and the cursor on screen with no drag to take them down.
    /// </summary>
    [Fact]
    public void TakingTheCaptureFromTheNamesButtonDoesNotEndTheDrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        ShowOffScreen(page);
        var rows = page.Named<StackPanel>("RouteRows");
        var name = Page.NameButton(page.Rows[1]);
        try
        {
            Assert.True(name.CaptureMouse()); // what the button does for itself on a press

            Assert.True(rows.CaptureMouse()); // this raises the button's loss, routed up through the panel
            page.Call("StartRouteDrag", 1);

            Assert.False(name.IsMouseCaptured);
            Assert.True(rows.IsMouseCaptured);
            Assert.Equal(1, page.Field<int>("_routeDragFrom"));

            page.Call("EndRouteDrag");

            Assert.False(rows.IsMouseCaptured);
            Assert.Equal(Visibility.Collapsed, page.Named<FrameworkElement>("RouteDragRow").Visibility);
            Assert.Null(System.Windows.Input.Mouse.OverrideCursor);
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
            System.Windows.Input.Mouse.Capture(null);
            page.Dialog.Close();
        }
    });

    [Fact]
    public void OnlyThePanelsOwnLossOfTheCaptureEndsADrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        LaidOut(page);
        page.Call("StartRouteDrag", 1);

        RaiseCaptureLost(Page.NameButton(page.Rows[1])); // bubbles through the panel
        Assert.Equal(1, page.Field<int>("_routeDragFrom"));

        RaiseCaptureLost(page.Named<StackPanel>("RouteRows"));
        Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
        page.Dialog.Close();
    });

    [Fact]
    public void ThereIsOnlyEverOneCarriedRow() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var layer = page.Named<Canvas>("RouteDragLayer");
        var carried = Assert.Single(layer.Children.OfType<UIElement>());

        for (var i = 0; i < 3; i++)
        {
            Carry(page, 1, over: 3, height);
            Assert.Same(carried, Assert.Single(layer.Children.OfType<UIElement>()));
            page.Call("EndRouteDrag");
            Assert.Equal(Visibility.Collapsed, carried.Visibility);
        }
        page.Dialog.Close();
    });

    [Fact]
    public void TheRowsMakeRoomForTheCarriedStopAndTheRouteWaitsForTheDrop() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var order = page.Route.Stops.ToList();

        Carry(page, 1, over: 2, height);

        Assert.Equal(new[] { "A", "C", "B", "D" }, page.Rows.Select(Page.Name)); // as it would read if let go
        Assert.Equal(new[] { "1", "2", "3", "4" }, page.Rows.Select(Page.Number)); // the numbers are the places
        Assert.Equal(new[] { Visibility.Visible, Visibility.Visible, Visibility.Hidden, Visibility.Visible }, page.Rows.Select(r => r.Visibility)); // the place on offer
        Assert.Equal(order, page.Route.Stops); // a picture until the drop
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    /// <summary>The owner's report (Nov 6 2026): "a bit hard to see which items are swapping". The carried stop covers its own place and nothing else, and the stop it passes glides.</summary>
    [Fact]
    public void TheCarriedStopSitsOnThePlaceOnOfferAndCoversNoOtherRow() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var carried = page.Named<FrameworkElement>("RouteDragRow");

        page.Call("StartRouteDrag", 1);
        Assert.Equal(1 * height, Canvas.GetTop(carried), 3);

        page.Call("MoveRouteDrag", 2.9 * height); // near the row's bottom: a copy on the cursor would hang over the next one

        Assert.Equal(2 * height, Canvas.GetTop(carried), 3);
        Assert.Equal(height, carried.Height, 3);
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    private static bool Gliding(UIElement element) => ((TranslateTransform)element.RenderTransform).HasAnimatedProperties;

    [Fact]
    public void TheStopPassedGlidesAndTheOthersStayPut() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var carried = page.Named<FrameworkElement>("RouteDragRow");

        Carry(page, 1, over: 2, height);

        Assert.Equal(new[] { false, true, false, false }, page.Rows.Select(Gliding)); // C, now in the second row
        Assert.True(Gliding(carried));

        page.Call("EndRouteDrag");

        Assert.All(page.Rows, r => Assert.False(Gliding(r)));
        Assert.False(Gliding(carried));
        page.Dialog.Close();
    });

    [Fact]
    public void ANewPlaceManyRowsAwayGlidesEveryStopBetween() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Carry(page, 0, over: 3, height);

        Assert.Equal(new[] { true, true, true, false }, page.Rows.Select(Gliding));
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    [Fact]
    public void PastTheListsEdgeThePlaceIsTheRowAtTheEdge() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 0);
        var a = page.Library.Items[0];
        for (var i = 0; i < 30; i++) page.Actions.AddToRoute(a);
        while (page.Rows.Count < 30) page.Idle();
        var height = LaidOut(page);
        var list = page.Named<ScrollViewer>("RouteList");
        Assert.True(list.ViewportHeight < list.ExtentHeight);

        page.Call("StartRouteDrag", 0);
        page.Call("MoveRouteDrag", 9999.0); // far below: not stop 30, out of sight

        var place = page.Field<int>("_routeDragTo");
        Assert.True(place * height < list.ViewportHeight, $"place {place} is out of view");
        Assert.True(place > 0);
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    // ---- What the fresh review found (Nov 6 2026) ----------------------------------

    [Fact]
    public void AStopPassedBetweenThePressAndTheMoveCantBeCarried() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        LaidOut(page);
        bool CanCarry(int row) => (bool)typeof(SettingsWindow).GetMethod("CanCarryRouteStop", Any)!.Invoke(page.Dialog, new object[] { row })!;
        Assert.True(CanCarry(0));

        page.Actions.SkipStop(); // reached behind the dialog, with the button still down

        Assert.False(CanCarry(0));
        Assert.True(CanCarry(1));
        page.Actions.RemoveStop(3);
        Assert.False(CanCarry(3)); // gone: its row would throw
        page.Dialog.Close();
    });

    [Fact]
    public void ANameCantJumpTheListUnderTheMouse() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 2);

        Assert.False(Page.NameButton(page.Rows[0]).Focusable); // focus scrolls a half-hidden row into view
        page.Dialog.Close();
    });

    /// <summary>A key event as the window's own input path raises it: the press, or Windows' repeat while it is held.</summary>
    private static void RaiseEscape(SettingsWindow dialog, RoutedEvent routed, bool repeat = false)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, 0, Key.Escape) { RoutedEvent = routed };
        if (repeat) typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(args, new object[] { true });
        dialog.RaiseEvent(args);
    }

    [Fact]
    public void EscHeldDownToCallOffADragDoesNotCloseTheDialog() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        ShowOffScreen(page);
        var closed = false;
        page.Dialog.Closed += (_, _) => closed = true;
        try
        {
            page.Call("StartRouteDrag", 1);

            RaiseEscape(page.Dialog, Keyboard.PreviewKeyDownEvent);
            Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
            RaiseEscape(page.Dialog, Keyboard.KeyDownEvent, repeat: true); // held: Windows' repeat, with no drag left to take it
            RaiseEscape(page.Dialog, Keyboard.KeyUpEvent);

            Assert.False(closed);

            // And a real press still closes it, on its release.
            RaiseEscape(page.Dialog, Keyboard.KeyDownEvent);
            RaiseEscape(page.Dialog, Keyboard.KeyUpEvent);
            Assert.True(closed);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (!closed) page.Dialog.Close();
        }
    });

    /// <summary>The owner's ask (Nov 6 2026): dropping at the end read oddly with nothing moving to say so.</summary>
    [Fact]
    public void CarriedToTheBottomTheLastStopMovesUp() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Carry(page, 1, over: 3, height);

        Assert.Equal(new[] { "A", "C", "D", "B" }, page.Rows.Select(Page.Name));
        Assert.Equal(Visibility.Hidden, page.Rows[3].Visibility);
        Assert.Equal("4", page.Named<TextBlock>("RouteDragNumber").Text); // the copy says the place it would take

        page.Call("DropRouteDrag");

        Assert.Equal(new[] { "A", "C", "D", "B" }, page.Rows.Select(Page.Name));
        Assert.All(page.Rows, r => Assert.Equal(Visibility.Visible, r.Visibility));
        page.Dialog.Close();
    });

    [Fact]
    public void CarriedToTheTopTheCurrentMarkStaysOnThePlace() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Carry(page, 3, over: 0, height);

        Assert.Equal(new[] { "D", "A", "B", "C" }, page.Rows.Select(Page.Name));
        Assert.Equal(new[] { "▶", "", "", "" }, page.Rows.Select(Page.Mark));
        Assert.Equal("▶", page.Named<TextBlock>("RouteDragMark").Text); // the copy over that place says what it would make it

        page.Call("MoveRouteDrag", 1.5 * height);
        Assert.Equal("", page.Named<TextBlock>("RouteDragMark").Text);
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    [Fact]
    public void TheDistanceStaysOffTheCurrentPlaceDuringADrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        page.Actions.RouteAt(0, 0);
        page.Call("RenderRoute");
        Assert.NotEqual("", Page.Distance(page.Rows[0]));

        Carry(page, 3, over: 0, height); // D shows in the current place, and that distance is A's

        Assert.Equal("", Page.Distance(page.Rows[0]));
        page.Call("EndRouteDrag");
        Assert.NotEqual("", Page.Distance(page.Rows[0]));
        page.Dialog.Close();
    });

    [Fact]
    public void TheDragAreaShowsTheMoveCursorButNotOnTheCrossOrAPassedStop() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        page.Actions.SkipStop();
        var rows = page.Rows;

        Assert.Null(rows[0].Cursor);
        Assert.Same(System.Windows.Input.Cursors.SizeAll, rows[1].Cursor);
        Assert.Same(System.Windows.Input.Cursors.SizeAll, rows[2].Cursor);
        Assert.Same(System.Windows.Input.Cursors.Arrow, Page.Remove(rows[1]).Cursor); // or it would take the row's
        Assert.Same(System.Windows.Input.Cursors.Hand, Page.NameButton(rows[1]).Cursor); // a click
        page.Dialog.Close();
    });

    [Fact]
    public void TheCarriedRowShowsTheStopBeingCarried() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        LaidOut(page);

        page.Call("StartRouteDrag", 2);

        Assert.Equal(Visibility.Visible, page.Named<FrameworkElement>("RouteDragRow").Visibility);
        Assert.Equal("3", page.Named<TextBlock>("RouteDragNumber").Text);
        Assert.Equal("C", page.Named<TextBlock>("RouteDragName").Text);
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(23, 0)]
    [InlineData(24, 1)]
    [InlineData(-5, 0)]    // above the first
    [InlineData(999, 3)]   // below the last
    public void AHeightIsOnOneRow(double y, int row) =>
        Assert.Equal(row, SettingsWindow.RouteRowAt(y, rowHeight: 24, count: 4));

    [Theory]
    [InlineData(0, 2, 2)]    // above the passed stops: the current place
    [InlineData(30, 2, 2)]   // over a passed stop
    [InlineData(55, 2, 2)]
    [InlineData(80, 2, 3)]
    [InlineData(999, 2, 3)]  // below the last: the last place
    [InlineData(999, 4, 3)]  // a finished route has no upcoming place, and no drag either: still a row
    public void AHeightMeansThePlaceItIsOver(double y, int current, int place) =>
        Assert.Equal(place, SettingsWindow.RouteDragTarget(y, rowHeight: 24, current, count: 4));

    [Theory]
    [InlineData(1, 3, new[] { 0, 2, 3, 1, 4 })] // down: those between move up
    [InlineData(3, 1, new[] { 0, 3, 1, 2, 4 })] // up: those between move down
    [InlineData(2, 2, new[] { 0, 1, 2, 3, 4 })] // where it was
    [InlineData(3, 4, new[] { 0, 1, 2, 4, 3 })] // to the end: the last one moves up
    public void ARowShowsTheStopItWouldHold(int from, int to, int[] stops) =>
        Assert.Equal(stops, Enumerable.Range(0, 5).Select(row => SettingsWindow.RouteStopAt(row, from, to)));

    [Fact]
    public void WhichRowShowsAStopIsTheOtherWayRound()
    {
        for (var from = 0; from < 6; from++)
        {
            for (var to = 0; to < 6; to++)
            {
                for (var row = 0; row < 6; row++)
                {
                    Assert.Equal(row, SettingsWindow.RouteRowOf(SettingsWindow.RouteStopAt(row, from, to), from, to));
                }
            }
        }
    }

    [Fact]
    public void DraggingAStopDownMovesIt() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Drag(page, from: 1, over: 2, height);

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

        Drag(page, from: 3, over: 0, height);

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

        Drag(page, from: 0, over: 2, height);

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

        Drag(page, from: 3, over: 0, height);

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
        Assert.Equal(Visibility.Hidden, Page.Cell(first, 0).Visibility);
        Assert.Equal(Visibility.Visible, Page.Cell(page.Rows[1], 0).Visibility);
        page.Dialog.Close();
    });

    [Fact]
    public void AnEndedDragMovesNothingAndLeavesNothingBehind() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        var carried = page.Named<FrameworkElement>("RouteDragRow");

        Carry(page, 1, over: 3, height);
        Assert.Equal(Visibility.Visible, carried.Visibility);
        Assert.Equal(new[] { "A", "C", "D", "B" }, page.Rows.Select(Page.Name));

        page.Call("EndRouteDrag"); // Esc, Alt+Tab, the dialog closing

        Assert.Equal(new[] { "A", "B", "C", "D" }, page.Rows.Select(Page.Name));
        Assert.Equal(Visibility.Collapsed, carried.Visibility);
        Assert.All(page.Rows, r => Assert.Equal(Visibility.Visible, r.Visibility));
        Assert.Null(System.Windows.Input.Mouse.OverrideCursor);
        page.Dialog.Close();
    });

    [Fact]
    public void AStopPassedWhileBeingCarriedEndsTheDrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Carry(page, 0, over: 3, height);
        page.Actions.SkipStop(); // reached behind the dialog

        Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "B", "C", "D" }, page.Rows.Select(Page.Name));
        page.Dialog.Close();
    });

    [Fact]
    public void AnotherStopPassedWhileCarryingPushesThePlaceDownBelowIt() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Carry(page, 3, over: 0, height); // D offered A's place
        page.Actions.SkipStop();         // A is reached behind the dialog

        Assert.Equal(3, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "D", "B", "C" }, page.Rows.Select(Page.Name));
        Assert.Equal(new[] { "✓", "▶", "", "" }, page.Rows.Select(Page.Mark));
        Assert.Equal("2", page.Named<TextBlock>("RouteDragNumber").Text); // the copy moved with it, and says so
        Assert.Equal(height, Canvas.GetTop(page.Named<FrameworkElement>("RouteDragRow")), 3);

        page.Call("DropRouteDrag");

        Assert.Equal(new[] { "A", "D", "B", "C" }, page.Rows.Select(Page.Name));
        Assert.Equal(1, page.Route.Current);
        page.Dialog.Close();
    });

    [Fact]
    public void ARouteShorteningUnderADragDoesNotThrow() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);

        Carry(page, 3, over: 3, height);
        page.Actions.RemoveStop(3); // the carried stop itself: nothing on the page can do this mid-drag, but the row must not outlive it

        Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "B", "C" }, page.Rows.Select(Page.Name));

        Carry(page, 1, over: 2, height);
        page.Actions.RemoveStop(2); // the place on offer is gone: it gives way

        Assert.Equal(new[] { "A", "B" }, page.Rows.Select(Page.Name));
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    private static void RaiseMoveWithTheButtonUp(UIElement on) =>
        on.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });

    /// <summary>Found by the chaos driver (Nov 6 2026): a release at another position than the last move arrives as a move with the button up, then the release. It used to cancel the drop.</summary>
    [Fact]
    public void AMoveThatIsPartOfTheReleaseDoesNotCancelTheDrop() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        Carry(page, 0, over: 2, height);

        RaiseMoveWithTheButtonUp(page.Named<StackPanel>("RouteRows")); // no button is held in a test run
        page.Call("DropRouteDrag"); // the release, straight after it
        page.Idle();

        Assert.Equal(new[] { "B", "C", "A", "D" }, page.Rows.Select(Page.Name));
        page.Dialog.Close();
    });

    [Fact]
    public void AMoveWithTheButtonUpAndNoReleaseEndsTheDragWithNothingMoved() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        Carry(page, 0, over: 2, height);

        RaiseMoveWithTheButtonUp(page.Named<StackPanel>("RouteRows"));
        page.Idle();

        Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "B", "C", "D" }, page.Rows.Select(Page.Name));
        Assert.Null(Mouse.OverrideCursor);
        page.Dialog.Close();
    });

    /// <summary>Found by the chaos driver (Nov 6 2026): the index alone would have gone on carrying whichever stop slid into it.</summary>
    [Fact]
    public void AStopInsertedAboveTheCarriedOneEndsTheDrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var height = LaidOut(page);

        Carry(page, 2, over: 1, height);
        page.Actions.AddAsNextStop(page.Library.Items[3]); // straight after the current stop, above the carried one

        Assert.Equal(-1, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "D", "B", "C" }, page.Rows.Select(Page.Name));
        Assert.Equal(Visibility.Collapsed, page.Named<FrameworkElement>("RouteDragRow").Visibility);
        page.Dialog.Close();
    });

    [Fact]
    public void AStopAddedAtTheEndKeepsTheDrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 3);
        var height = LaidOut(page);

        Carry(page, 1, over: 2, height);
        page.Actions.AddToRoute(page.Library.Items[3]);

        Assert.Equal(1, page.Field<int>("_routeDragFrom"));
        Assert.Equal(new[] { "A", "C", "B", "D" }, page.Rows.Select(Page.Name));
        page.Call("EndRouteDrag");
        page.Dialog.Close();
    });

    [Fact]
    public void ClosingTheDialogEndsADrag() => TheThemeOnScreen.OnSta(() =>
    {
        var page = Open(AToD, 4);
        var height = LaidOut(page);
        Carry(page, 1, over: 3, height);

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
