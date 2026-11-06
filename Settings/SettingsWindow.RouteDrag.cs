using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
/// <b>What a drag looks like, and the two looks it replaced (the owner, Nov 6
/// 2026).</b> While a stop is carried the list reads as it would if you let go,
/// and the target is simply the row under the cursor. The carried stop sits ON
/// that row, moving a row at a time, and each stop it passes glides into the place
/// it left: you watch the pair trade places. First there was a drop line in a gap,
/// which read oddly at the end of the list, where nothing moved to say where the
/// stop would land. Then the rows made room, but by rewriting their text at once,
/// under a copy centred on the cursor: at the very moment of a swap the copy hid
/// half of both rows, and nothing moved, so "it's a bit hard to see which items are
/// swapping". It stays a picture until the drop: the route moves once, on the
/// release. The rows are slots (SettingsWindow.Route.cs), so a row that glides
/// shows its new stop from the start and slides in from where that stop was.
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
    private const double RouteDragEdge = 18;         // how near an edge of the list starts it scrolling
    private const double RouteDragScrollStep = 6;    // px per tick: about six rows a second
    private static readonly TimeSpan RouteDragScrollEvery = TimeSpan.FromMilliseconds(40);

    /// <summary>Long enough to follow with the eye: most of an ease-out's travel is in its first third (150 ms was gone in about 60, on film). It can't lag behind a hand: a new swap glides on from wherever this one has got to.</summary>
    private static readonly Duration RouteGlideTime = TimeSpan.FromMilliseconds(200);

    private static readonly IEasingFunction RouteGlideEase = FrozenGlideEase();

    private int _routePressed = -1;    // the row the left button went down on, until it moves or lifts
    private Point _routePressedAt;     // in the list's own view: see OnRoutePress
    private int _routeDragFrom = -1;   // the stop being carried; -1 = no drag
    private Guid _routeDragStop;       // and its waypoint: the index alone would quietly mean another stop if the list changed above it
    private int _routeDragTo;          // the place it would take if let go now
    private DispatcherTimer? _routeDragScroll;

    // Frozen: a dialog's thread is not the only one that reads a static, and the tests run several.
    private static IEasingFunction FrozenGlideEase()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

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

    /// <summary>The other way round: which row shows a stop while one is carried. What a row glides in from.</summary>
    internal static int RouteRowOf(int stop, int from, int to)
    {
        if (stop == from) return to;
        if (from < to && stop > from && stop <= to) return stop - 1;
        if (to < from && stop >= to && stop < from) return stop + 1;
        return stop;
    }

    /// <summary>The rows are all of a height: what the 2 px between them belong to doesn't matter.</summary>
    private double RouteRowHeight => RouteRows.ActualHeight / Math.Max(1, _routeRows.Count);

    /// <summary>Only an upcoming stop moves, and one that is still there. Asked at the press and again at the lift: the route moves by itself in between, behind the dialog.</summary>
    private bool CanCarryRouteStop(int row) => row >= _actions.Route.Current && row < Math.Min(_routeRows.Count, _actions.Route.Count);

    private void InitRouteDrag()
    {
        RouteDragRow.RenderTransform = new TranslateTransform();
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

    /// <summary>
    /// The press is kept in the list's view, not in its content: what counts as
    /// the mouse moving must not count the list scrolling under a mouse held still
    /// (the wheel during a press), which would start a drag by itself.
    /// </summary>
    private void OnRoutePress(object sender, MouseButtonEventArgs e)
    {
        _routePressed = -1;
        if (_routeRows.Count == 0 || RouteRowHeight <= 0) return;
        var row = RouteRowAt(e.GetPosition(RouteRows).Y, RouteRowHeight, _routeRows.Count);
        if (!CanCarryRouteStop(row) || _routeRows[row].Remove.IsMouseOver) return;
        _routePressed = row;
        _routePressedAt = e.GetPosition(RouteList);
    }

    private void OnRouteMouseMove(object sender, MouseEventArgs e)
    {
        if (_routeDragFrom >= 0)
        {
            if (e.LeftButton == MouseButtonState.Pressed) MoveRouteDrag(e.GetPosition(RouteRows).Y);
            else Dispatcher.BeginInvoke(DispatcherPriority.Input, EndRouteDragIfReleaseWasMissed);
            return;
        }
        if (_routePressed < 0) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _routePressed = -1;
            return;
        }
        var at = e.GetPosition(RouteList);
        if (Math.Abs(at.Y - _routePressedAt.Y) < SystemParameters.MinimumVerticalDragDistance
            && Math.Abs(at.X - _routePressedAt.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        var row = _routePressed;
        _routePressed = -1;
        // First, and all or nothing. Taking the capture also tells the name's button this is no click.
        if (!CanCarryRouteStop(row) || !RouteRows.CaptureMouse()) return;
        StartRouteDrag(row);
        MoveRouteDrag(e.GetPosition(RouteRows).Y);
    }

    /// <summary>
    /// A move with the button already up: never leave a stop stuck to the mouse. Not
    /// decided on the spot, because it may BE the release: when a release arrives at another
    /// position than the last move (the chaos driver saw it; remote desktop or a pen could do
    /// it), WPF raises a move as the first part of it, with the button up, and the release
    /// straight after. Ended there, the drop would be lost and the stop would jump back.
    /// By this time a real release has dropped it, and this finds no drag.
    /// </summary>
    private void EndRouteDragIfReleaseWasMissed()
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed) EndRouteDrag();
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
        RouteDragMark.Foreground = row.Disc.Fill;
        RouteDragName.Text = row.NameText.Text;
        RouteDragRow.Width = RouteRows.ActualWidth;
        RouteDragRow.Height = RouteRowHeight;
        RouteDragRow.Visibility = Visibility.Visible;
        _routeDragFrom = index;
        _routeDragStop = _actions.Route.Stops[index];
        _routeDragTo = index;
        DisarmRouteClear();
        Mouse.OverrideCursor = Cursors.SizeAll;
        FillRouteRows(); // the place on offer empties, under the carried copy
        PlaceRouteDragRow();
        if (_routeDragScroll is null)
        {
            _routeDragScroll = new DispatcherTimer { Interval = RouteDragScrollEvery };
            _routeDragScroll.Tick += (_, _) => ScrollRouteDrag();
        }
        _routeDragScroll.Start();
    }

    /// <summary>A height in the list: the place it means. Past the list's edge it means the row at that edge, or the stop would be carried out of sight.</summary>
    private void MoveRouteDrag(double y)
    {
        var rowHeight = RouteRowHeight;
        if (rowHeight <= 0) return;
        var top = RouteList.VerticalOffset;
        var inView = Math.Clamp(y, top, top + Math.Max(0, RouteList.ViewportHeight - 1));
        var to = RouteDragTarget(inView, rowHeight, _actions.Route.Current, _routeRows.Count);
        if (to != _routeDragTo) SwapRouteDragTo(to, rowHeight);
    }

    /// <summary>
    /// The carried stop takes another place: the stops in between take theirs,
    /// each gliding from where it is on screen right now, which may be part-way
    /// through the last glide, so a fast hand sees no jump. A fast hand also crosses
    /// many rows between two mouse messages: they all glide together.
    /// </summary>
    private void SwapRouteDragTo(int to, double rowHeight)
    {
        var (from, was) = (_routeDragFrom, _routeDragTo);
        var onScreen = _routeRows.Select((row, i) => i * rowHeight + GlideOf(row.Grid).Y).ToList();
        var carriedOnScreen = Canvas.GetTop(RouteDragRow) + GlideOf(RouteDragRow).Y;

        _routeDragTo = to;
        FillRouteRows();
        for (var row = Math.Min(was, to); row <= Math.Max(was, to) && row < _routeRows.Count; row++)
        {
            if (row == to) continue; // empty: the carried copy is over it
            var cameFrom = RouteRowOf(RouteStopAt(row, from, to), from, was);
            if (cameFrom != row) Glide(_routeRows[row].Grid, onScreen[cameFrom] - row * rowHeight);
        }
        PlaceRouteDragRow();
        Glide(RouteDragRow, carriedOnScreen - to * rowHeight);
    }

    /// <summary>The carried copy on the place on offer, saying what that place is: its number, and ▶ if it would make it the current stop.</summary>
    private void PlaceRouteDragRow()
    {
        Canvas.SetTop(RouteDragRow, _routeDragTo * RouteRowHeight);
        RouteDragNumber.Text = (_routeDragTo + 1).ToString(CultureInfo.InvariantCulture);
        RouteDragMark.Text = _routeDragTo == _actions.Route.Current ? "▶" : "";
    }

    private static TranslateTransform GlideOf(UIElement element) => (TranslateTransform)element.RenderTransform;

    /// <summary>Slides an element to where it belongs from this far away. Stops by itself and leaves nothing behind: the offset's base value stays 0.</summary>
    private static void Glide(UIElement element, double from) =>
        GlideOf(element).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, RouteGlideTime)
        {
            EasingFunction = RouteGlideEase,
            FillBehavior = FillBehavior.Stop
        });

    private static void StopGlide(UIElement element) => GlideOf(element).BeginAnimation(TranslateTransform.YProperty, null);

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
        StopGlide(RouteDragRow);
        foreach (var row in _routeRows) StopGlide(row.Grid);
        FillRouteRows();
        RouteRows.ReleaseMouseCapture();
    }
}
