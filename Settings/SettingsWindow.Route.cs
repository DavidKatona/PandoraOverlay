using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Route page (the plan and every decision:
/// Desktop\Pandora Overlay Files\pandora-route\PLAN.md): the one route as a
/// list, its state on the card's band, and what can be done to it. Everything
/// the page does goes through MapActions, the same as the map's menu, and acts
/// at once. Unlike the waypoint and friend lists the page edits no copy of its
/// own: the route moves by itself while the dialog is open (the poll keeps
/// running, and a stop can be reached behind the dialog), so a copy would be
/// stale the moment it was made. The names and colours it shows are the
/// Waypoints page's copy, so a name typed there reads the same here.
/// <para>
/// <b>The rows are slots.</b> Row N always shows stop N: a stop passed, removed
/// or moved rewrites what the slots say and never rebuilds them, so the list
/// keeps the reader's place (.claude/rules/settings.md) while the route moves on
/// under it. Only the first look makes rows, in batches like every long list,
/// and a longer route adds more. A slot reads the theme each time it is filled,
/// so a theme switch needs only a fill too.
/// </para>
/// <para>
/// The band sits outside the scrolling list on purpose: it is the route's live
/// state, which should not scroll away on a long route. The page asks nothing
/// of the website and sets no timer: the distances are refilled when a position
/// arrives, from PollService's one stream.
/// </para>
/// </summary>
public partial class SettingsWindow
{
    private readonly MapActions _actions;
    private readonly List<RouteRow> _routeRows = new();
    private bool _routeBuilt;
    private bool _routeFollow;       // the page was just opened: the current stop is scrolled into view once its row exists
    private int _routeRenderVersion; // a newer render stops an older one's batches
    private bool _routeClearArmed;

    private const string RouteGrip = "≡";

    private sealed record RouteRow(Grid Grid, Border GripCell, TextBlock Grip, TextBlock Mark, TextBlock Number, Ellipse Disc, Button Name, TextBlock NameText, TextBlock Distance, Button Remove);

    /// <summary>Follows the route and the tracking while the dialog is open. Called in the constructor; the page itself is drawn on its first look.</summary>
    private void InitRoutePage()
    {
        RouteList.ScrollChanged += (_, _) => AlignRouteHeader();
        RouteList.SizeChanged += (_, _) => AlignRouteHeader();
        _actions.Route.Changed += RenderRoute;
        _actions.WaypointTracked += OnTrackingChanged;
        _poll.SnapshotReceived += OnRoutePosition;
        Closed += (_, _) =>
        {
            _actions.Route.Changed -= RenderRoute;
            _actions.WaypointTracked -= OnTrackingChanged;
            _poll.SnapshotReceived -= OnRoutePosition;
        };
        InitRouteDrag();
    }

    /// <summary>
    /// AlignHeader's measured alignment, without its 6 px: the bar here is inside
    /// the card and only there while there is something to scroll, so the rows
    /// run the band's full width, and a reserved gutter would stand beside them.
    /// </summary>
    private void AlignRouteHeader()
    {
        var bar = Math.Max(0, RouteList.ActualWidth - RouteList.ViewportWidth);
        RouteHeader.Margin = new Thickness(7, 0, 7 + bar, 4);
    }

    /// <summary>
    /// Each look draws it again (a waypoint may have been renamed or recoloured
    /// on the Waypoints page since), which is cheap: it only fills the slots.
    /// The page asks nothing of the website, so unlike Dino storage's this may
    /// run as often as it likes.
    /// </summary>
    private void OpenRoutePage()
    {
        _routeBuilt = true;
        _routeFollow = true;
        DisarmRouteClear();
        RenderRoute();
    }

    /// <summary>
    /// The tracking moved outside the page's copy: a stop reached behind the
    /// dialog, or an action here. The Waypoints page's copy follows, because
    /// its next flush writes that copy into the config: a stale one would
    /// track the old stop again and pause the route with any edit there.
    /// </summary>
    private void OnTrackingChanged()
    {
        _draftTracked = _config.TrackedWaypointId;
        foreach (var (id, radio) in _rowTrackRadios) radio.IsChecked = id == _draftTracked;
        RenderRoute();
    }

    /// <summary>A position arrived (after MainWindow's handler, which subscribed first and moved the route on): the distances, on the page shown.</summary>
    private void OnRoutePosition(MyLocationResponse _)
    {
        if (!_routeBuilt || _lastPage != "Route") return;
        UpdateRouteBand();
        if (_actions.Route.Current < _routeRows.Count) FillRouteDistance(_actions.Route.Current);
    }

    // ---- Drawing ------------------------------------------------------------------

    private void RenderRoute()
    {
        if (!_routeBuilt) return;
        Drawn("Route");
        var route = _actions.Route;
        var hasStops = !route.IsEmpty;
        var paused = _actions.RoutePaused;

        // The stop being carried was passed behind the dialog: it can't move any more.
        if (_routeDragFrom >= 0 && (_routeDragFrom < route.Current || _routeDragFrom >= route.Count)) EndRouteDrag();

        RouteCount.Text = RouteCountText();
        RouteHeader.Visibility = hasStops ? Visibility.Visible : Visibility.Hidden; // Hidden: the card and the buttons stay where they are
        RouteBand.Visibility = hasStops ? Visibility.Visible : Visibility.Collapsed;
        RouteList.Visibility = hasStops ? Visibility.Visible : Visibility.Collapsed;
        RouteEmpty.Visibility = hasStops ? Visibility.Collapsed : Visibility.Visible;
        RouteResumeButton.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        RouteHint.Visibility = hasStops && !paused ? Visibility.Visible : Visibility.Collapsed;
        RouteHint.Text = route.IsFinished
            ? "Restart walks it again. Click a stop to start from there."
            : "Drag a stop to move it. Click a stop to go there next.";
        RouteRestartButton.IsEnabled = hasStops;
        RouteClearButton.IsEnabled = hasStops;
        if (!hasStops) DisarmRouteClear();
        UpdateRouteBand();

        while (_routeRows.Count > route.Count)
        {
            _routeRows.RemoveAt(_routeRows.Count - 1);
            RouteRows.Children.RemoveAt(_routeRows.Count);
        }
        for (var i = 0; i < _routeRows.Count; i++) FillRouteRow(i);
        AddRouteRows(FirstRowBatch, ++_routeRenderVersion);
    }

    private string RouteCountText() => $"{_actions.Route.Count} / {Route.Capacity}";

    /// <summary>The rows a route has and the slots don't yet: the first screenful now, the rest a batch per idle moment, as the Waypoints page does.</summary>
    private void AddRouteRows(int count, int version, long waitingSince = 0)
    {
        if (version != _routeRenderVersion) return;
        var route = _actions.Route;
        for (var n = 0; n < count && _routeRows.Count < route.Count; n++)
        {
            var row = BuildRouteRow(_routeRows.Count);
            _routeRows.Add(row);
            RouteRows.Children.Add(row.Grid);
            FillRouteRow(_routeRows.Count - 1);
        }
        if (_routeRows.Count >= route.Count)
        {
            RouteCount.Text = RouteCountText();
            FollowCurrentStop();
            return;
        }
        if (BuildIsSlow(waitingSince)) RouteCount.Text = $"building… {_routeRows.Count} of {route.Count}";
        Dispatcher.BeginInvoke(DispatcherPriority.Background,
            () => AddRouteRows(RowBatch, version, waitingSince != 0 ? waitingSince : Stopwatch.GetTimestamp()));
    }

    /// <summary>Once per opening, so a long route opens where you are; never as the route moves, which would pull the list from under a reader.</summary>
    private void FollowCurrentStop()
    {
        if (!_routeFollow) return;
        _routeFollow = false;
        if (_routeRows.Count == 0) return;
        var row = _routeRows[Math.Min(_actions.Route.Current, _routeRows.Count - 1)].Grid;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => row.BringIntoView()); // after the layout that places it
    }

    private void UpdateRouteBand()
    {
        var route = _actions.Route;
        var paused = _actions.RoutePaused;
        RouteBandText.Text = RouteBandWords(route.Current, route.Count, paused, route.IsFinished, _actions.RouteMetersLeft());
        ShowTone(RouteBandText, paused ? Tone.Warn : Tone.Neutral);
    }

    /// <summary>The band's words. What is left is counted only while walking: a paused route is not being walked, and without a position there is nothing to count from.</summary>
    internal static string RouteBandWords(int current, int count, bool paused, bool finished, double? metersLeft)
    {
        if (finished) return "ROUTE FINISHED";
        var stop = $"STOP {current + 1} OF {count}";
        if (paused) return $"PAUSED · {stop}";
        return metersLeft is { } left ? $"{stop} · {RouteDistance(left).ToUpperInvariant()} LEFT" : stop;
    }

    /// <summary>As the minimap's footer says a distance.</summary>
    private static string RouteDistance(double meters) => meters >= 1000 ? $"{meters / 1000:0.0} km" : $"{meters:0} m";

    /// <summary>The one grid the rows, the carried row and the XAML header share: grip | mark | number | disc | name | distance | remove.</summary>
    private static Grid NewRouteRowGrid()
    {
        var grid = new Grid();
        foreach (var width in new[] { 18.0, 18, 24, 21 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var width in new[] { 60.0, 46 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        return grid;
    }

    /// <summary>
    /// A row's controls. What a row says and how it looks is FillRouteRow's.
    /// The clicks act on the row's index, which is always its stop's (see the
    /// class's notes on slots).
    /// </summary>
    private RouteRow BuildRouteRow(int index)
    {
        var grid = NewRouteRowGrid();
        grid.Margin = new Thickness(0, 1, 0, 1);

        TextBlock Cell(int column, HorizontalAlignment alignment)
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment };
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
            return text;
        }

        // The grip's whole cell shows the cursor, not just its three lines.
        var grip = new TextBlock { Text = RouteGrip, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var gripCell = new Border { Child = grip, Background = Brushes.Transparent };
        grid.Children.Add(gripCell);

        var mark = Cell(1, HorizontalAlignment.Center);
        var number = Cell(2, HorizontalAlignment.Center);
        var disc = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(disc, 3);
        grid.Children.Add(disc);

        var nameText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        var name = new Button { Content = nameText, Style = (Style)FindResource("ListRowButton") };
        name.Click += (_, _) => RouteAct(() => _actions.GoToStop(index));
        Grid.SetColumn(name, 4);
        grid.Children.Add(name);

        var distance = Cell(5, HorizontalAlignment.Right);
        distance.Margin = new Thickness(0, 0, 4, 0);

        var remove = new Button
        {
            Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0),
            Style = (Style)FindResource("NeutralButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Remove this stop"
        };
        remove.Click += (_, _) => RouteAct(() => _actions.RemoveStop(index));
        Grid.SetColumn(remove, 6);
        grid.Children.Add(remove);

        var row = new RouteRow(grid, gripCell, grip, mark, number, disc, name, nameText, distance, remove);
        WireRouteDrag(row, index);
        return row;
    }

    /// <summary>What a slot shows now, in the theme on screen: ✓ and dim for a passed stop, ▶ for the current one.</summary>
    private void FillRouteRow(int index)
    {
        var row = _routeRows[index];
        var route = _actions.Route;
        var id = route.Stops[index];
        var waypoint = _draft.Find(w => w.Id == id) ?? _library.Find(id);
        var passed = index < route.Current;
        var current = index == route.Current;
        var colour = PaletteBrushes[WaypointPalette.Wrap(waypoint?.Colour ?? 0)];
        var text = (passed ? Theme.Current.Text.Off : current ? Theme.Current.Text.Bright : Theme.Current.Text.Text2).ToBrush();

        row.Grid.Background = current ? Theme.Current.Nav.Selected.Fill.ToBrush() : index % 2 == 1 ? RowStripe : Brushes.Transparent;
        row.Grip.Visibility = passed ? Visibility.Hidden : Visibility.Visible; // only upcoming stops move
        row.GripCell.Cursor = passed ? null : Cursors.SizeNS;
        row.Grip.Foreground = Theme.Current.Text.Dim.ToBrush();
        row.Grip.FontSize = Theme.Current.Size(12);
        row.Mark.Text = passed ? "✓" : current ? "▶" : "";
        row.Mark.Foreground = passed ? text : colour;
        row.Mark.FontSize = Theme.Current.Size(10);
        row.Number.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
        row.Number.Foreground = passed ? text : Theme.Current.Text.Text2.ToBrush();
        row.Number.FontSize = Theme.Current.Size(11);
        row.Disc.Fill = colour;
        row.Disc.Opacity = passed ? 0.4 : 1;
        row.NameText.Text = waypoint?.Name ?? "";
        row.NameText.Foreground = text;
        row.NameText.FontSize = Theme.Current.Size(12);
        row.Remove.Foreground = RowDeleteGlyph;
        row.Remove.FontSize = Theme.Current.Size(10);
        row.Distance.Foreground = Theme.Current.Text.Label.ToBrush();
        row.Distance.FontSize = Theme.Current.Size(11);
        FillRouteDistance(index);
    }

    /// <summary>The distance on the current stop's row, while walking it.</summary>
    private void FillRouteDistance(int index)
    {
        var text = "";
        if (index == _actions.Route.Current && _actions.RouteRunning && _actions.RouteMetersToStop() is { } meters) text = RouteDistance(meters);
        _routeRows[index].Distance.Text = text;
    }

    // ---- What the page does -----------------------------------------------------------

    /// <summary>Anything else done on the page disarms Clear: it asked about the route as it stood.</summary>
    private void RouteAct(Action act)
    {
        DisarmRouteClear();
        act();
    }

    private void RouteResume_Click(object sender, RoutedEventArgs e) => RouteAct(_actions.ResumeRoute);

    private void RouteRestart_Click(object sender, RoutedEventArgs e) => RouteAct(_actions.RestartRoute);

    /// <summary>Two clicks, armed in place: no Cancel brings a route back.</summary>
    private void RouteClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_routeClearArmed)
        {
            _routeClearArmed = true;
            RouteClearButton.Content = "Clear all?";
            return;
        }
        RouteAct(() => _actions.ClearRoute());
    }

    private void DisarmRouteClear()
    {
        _routeClearArmed = false;
        RouteClearButton.Content = "Clear";
    }

    /// <summary>
    /// A row's + on the Waypoints page. The page's copy reaches the library
    /// first: a waypoint just imported is only in the copy until the next flush,
    /// and a stop whose waypoint the library lacks is dropped at the next
    /// change there.
    /// </summary>
    private void AddWaypointToRoute(Waypoint waypoint)
    {
        if (_actions.Route.IsFull)
        {
            SetWaypointStatus($"The route is full ({Route.Capacity} stops). Remove one on the Route page first.", Tone.Warn);
            return;
        }
        if (_waypointsDirty) FlushChanges();
        _actions.AddToRoute(waypoint);
        SetWaypointStatus($"{waypoint.Name} added to the route: stop {_actions.Route.Count}.", Tone.Good);
    }
}
