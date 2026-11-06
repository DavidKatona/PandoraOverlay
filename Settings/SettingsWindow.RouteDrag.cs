using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, Route page, the dragging: the first list in the overlay
/// that does it (the owner's call over ↑ ↓ buttons, PLAN.md decision 17). A
/// stop takes a press anywhere on its row but its ✕, and only becomes a drag
/// once the mouse has moved: a press and release on the name is still the click
/// that goes there. Only an upcoming stop moves, and only among the upcoming
/// ones, the current stop's place included: landing there makes it current
/// (Route.Move).
/// <para>
/// <b>The rows make room.</b> While a stop is carried the list reads as it would
/// if you let go now: the stops it has passed have slid into their new places and
/// the place it would take is dimmed under a copy that follows the cursor (the
/// owner, Nov 6 2026: a drop line in a gap read oddly at the end of the list, with
/// nothing moving to say where the stop would land, so the last stop now moves up
/// to make room). So the target is simply the row under the cursor. It is only a
/// picture until the drop: the route itself moves once, on the release. The rows
/// are slots (SettingsWindow.Route.cs), so this is RouteStopAt in the one fill.
/// </para>
/// <para>
/// By hand with the mouse's capture, not WPF's DragDrop: that runs a modal
/// loop with stock cursors and would let a stop be dropped on other windows.
/// The capture also means a drag can't read as a click on the game: the press
/// was on the dialog, a release anywhere comes back here, and no other window
/// was clicked, so the focus never moves. Whatever takes the capture away (Alt+Tab,
/// the page or the dialog closing) ends the drag where it stood, with nothing
/// moved, and Esc does the same.
/// </para>
/// <para>
/// <b>The shape of it, after the first version fell apart in the owner's hands
/// (Nov 6 2026: rows left stuck on the list, piling up with each drag).</b> A
/// press on a name leaves the name's button holding the capture. Taking it from
/// the button raises LostMouseCapture, a ROUTED event that bubbled up through
/// the row, whose handler took it for the drag's own and ended the drag while it
/// was still being set up; the set-up then carried on and showed its parts with
/// nothing left to take them down. So now: one owner of the capture and of the
/// mouse handlers, the rows' panel, which only listens to a loss that is its
/// own; the capture taken FIRST, and nothing shown unless it was got; one carried
/// copy, made once in the XAML, which a drag can at the very worst leave showing,
/// never add to; and one way out, <see cref="EndRouteDrag"/>.
/// </para>
/// <para>
/// Near the list's edges it scrolls, on a timer that runs only during a drag:
/// the mouse may be held still there, and a 64-stop route is five screens long.
/// The wheel works too, since the capture is inside the list. Windows' Move
/// cursor throughout, on the row and on the carried stop: the up-and-down arrow it
/// had first said "resize" (the owner, Nov 6 2026).
/// </para>
/// </summary>
public partial class SettingsWindow
{
    private const double RouteDraggedOpacity = 0.4;
    private const double RouteDragEdge = 18;         // how near an edge of the list starts it scrolling
    private const double RouteDragScrollStep = 6;    // px per tick: about six rows a second
    private static readonly TimeSpan RouteDragScrollEvery = TimeSpan.FromMilliseconds(40);

    private int _routePressed = -1;    // the row the left button went down on, until it moves or lifts
    private Point _routePressedAt;
    private int _routeDragFrom = -1;   // the stop being carried; -1 = no drag
    private int _routeDragTo;          // the place it would take if let go now
    private DispatcherTimer? _routeDragScroll;

    /// <summary>Which row a height in the list is on. A list with rows: there is no answer for none.</summary>
    internal static int RouteRowAt(double y, double rowHeight, int count) =>
        Math.Clamp((int)Math.Floor(y / rowHeight), 0, count - 1);

    /// <summary>The place a height means for a carried stop: the row it is over, never one of the passed stops'.</summary>
    internal static int RouteDragTarget(double y, double rowHeight, int current, int count) =>
        Math.Max(RouteRowAt(y, rowHeight, count), Math.Min(current, count - 1));

    /// <summary>Which stop a row shows while one is carried from one place to another: the rows between have slid over by one to make room.</summary>
    internal static int RouteStopAt(int row, int from, int to)
    {
        if (row == to) return from;
        if (from < to && row >= from && row < to) return row + 1;
        if (to < from && row > to && row <= from) return row - 1;
        return row;
    }

    /// <summary>The rows are all of a height: what the 2 px between them belong to doesn't matter.</summary>
    private double RouteRowHeight => RouteRows.ActualHeight / Math.Max(1, _routeRows.Count);

    private void InitRouteDrag()
    {
        RouteRows.PreviewMouseLeftButtonDown += OnRoutePress;
        RouteRows.PreviewMouseMove += OnRouteMouseMove;
        RouteRows.PreviewMouseLeftButtonUp += OnRouteRelease;
        RouteRows.LostMouseCapture += OnRouteCaptureLost;
        // Esc on a drag is the drag's: handled here, the dialog's own Esc never sees the press, so its release closes nothing.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _routeDragFrom < 0) return;
            e.Handled = true;
            EndRouteDrag();
        };
        Closed += (_, _) => EndRouteDrag();
    }

    /// <summary>
    /// Routed: a child's loss comes through here too, and the name's button loses
    /// the capture to the panel at the very start of a drag. Only the panel's own
    /// loss ends one (see the class's notes).
    /// </summary>
    private void OnRouteCaptureLost(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, RouteRows)) EndRouteDrag();
    }

    private void OnRoutePress(object sender, MouseButtonEventArgs e)
    {
        _routePressed = -1;
        if (_routeRows.Count == 0 || RouteRowHeight <= 0) return;
        var at = e.GetPosition(RouteRows);
        var row = RouteRowAt(at.Y, RouteRowHeight, _routeRows.Count);
        if (row < _actions.Route.Current || _routeRows[row].Remove.IsMouseOver) return;
        _routePressed = row;
        _routePressedAt = at;
    }

    private void OnRouteMouseMove(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(RouteRows);
        if (_routeDragFrom >= 0)
        {
            if (e.LeftButton == MouseButtonState.Pressed) MoveRouteDrag(at.Y);
            else EndRouteDrag(); // the release was missed: never leave a stop stuck to the mouse
            return;
        }
        if (_routePressed < 0) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _routePressed = -1;
            return;
        }
        if (Math.Abs(at.Y - _routePressedAt.Y) < SystemParameters.MinimumVerticalDragDistance
            && Math.Abs(at.X - _routePressedAt.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        var row = _routePressed;
        _routePressed = -1;
        // First, and all or nothing. Taking the capture also tells the name's button this is no click.
        if (!RouteRows.CaptureMouse()) return;
        StartRouteDrag(row);
        MoveRouteDrag(at.Y);
    }

    private void OnRouteRelease(object sender, MouseButtonEventArgs e)
    {
        _routePressed = -1;
        if (_routeDragFrom < 0) return; // a press that never moved: the name's click goes ahead
        e.Handled = true;
        DropRouteDrag();
    }

    /// <summary>The capture is held: the drag's state and what shows of it.</summary>
    private void StartRouteDrag(int index)
    {
        var row = _routeRows[index];
        RouteDragDisc.Fill = row.Disc.Fill;
        RouteDragName.Text = row.NameText.Text;
        RouteDragNumber.Text = row.Number.Text;
        RouteDragRow.Width = RouteRows.ActualWidth;
        RouteDragRow.Height = RouteRowHeight;
        RouteDragRow.Visibility = Visibility.Visible;
        _routeDragFrom = index;
        _routeDragTo = index;
        DisarmRouteClear();
        Mouse.OverrideCursor = Cursors.SizeAll;
        FillRouteRows(); // its own place dims
        if (_routeDragScroll is null)
        {
            _routeDragScroll = new DispatcherTimer { Interval = RouteDragScrollEvery };
            _routeDragScroll.Tick += (_, _) => ScrollRouteDrag();
        }
        _routeDragScroll.Start();
    }

    /// <summary>A height in the list: the place it means, the rows making room for it, and the copy under the cursor.</summary>
    private void MoveRouteDrag(double y)
    {
        var rowHeight = RouteRowHeight;
        if (rowHeight <= 0) return;
        var to = RouteDragTarget(y, rowHeight, _actions.Route.Current, _routeRows.Count);
        if (to != _routeDragTo)
        {
            _routeDragTo = to;
            RouteDragNumber.Text = (to + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            FillRouteRows();
        }
        // Kept in the part of the list on screen, or it would vanish past the edge just as the
        // list scrolls. Bottom never under top: Clamp throws on that, and a viewport can be
        // shorter than a row on a small screen.
        var top = RouteList.VerticalOffset;
        var bottom = Math.Max(top, Math.Min(RouteRows.ActualHeight, top + RouteList.ViewportHeight) - rowHeight);
        Canvas.SetTop(RouteDragRow, Math.Clamp(y - rowHeight / 2, top, bottom));
    }

    private void ScrollRouteDrag()
    {
        var y = Mouse.GetPosition(RouteList).Y;
        var step = y < RouteDragEdge ? -RouteDragScrollStep : y > RouteList.ActualHeight - RouteDragEdge ? RouteDragScrollStep : 0;
        if (step == 0) return;
        RouteList.ScrollToVerticalOffset(RouteList.VerticalOffset + step);
        MoveRouteDrag(Mouse.GetPosition(RouteRows).Y);
    }

    private void DropRouteDrag()
    {
        var (from, to) = (_routeDragFrom, _routeDragTo);
        EndRouteDrag();
        _actions.MoveStop(from, to); // landed where it was: Route.Move ignores it
    }

    /// <summary>The one way out of a drag, dropped or not: the rows read the route as it is again. Safe with none: letting go of the capture below comes straight back here.</summary>
    private void EndRouteDrag()
    {
        if (_routeDragFrom < 0) return;
        _routeDragFrom = -1;
        _routeDragScroll?.Stop();
        Mouse.OverrideCursor = null;
        RouteDragRow.Visibility = Visibility.Collapsed;
        FillRouteRows();
        RouteRows.ReleaseMouseCapture();
    }
}
