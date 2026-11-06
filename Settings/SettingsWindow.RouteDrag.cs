using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
/// By hand with the mouse's capture, not WPF's DragDrop: that runs a modal
/// loop with stock cursors and would let a stop be dropped on other windows.
/// The capture also means a drag can't read as a click on the game: the press
/// was on the dialog, a release anywhere comes back here, and no other window
/// was clicked, so the focus never moves. Whatever takes the capture away (Alt+Tab,
/// the dialog closing) ends the drag where it stood, with nothing moved, and Esc
/// does the same.
/// </para>
/// <para>
/// A drop line in the gap and a copy of the row just under the cursor, on a layer
/// over the rows that takes no clicks. The rows themselves stay put until the
/// drop, which rewrites their slots. Near the list's edges it scrolls, on a timer
/// that lives only for the drag: the mouse may be held still there, and a 64-stop
/// route is five screens long. The wheel works too, since the capture is inside
/// the list.
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
    private int _routeDragFrom = -1;   // the row being dragged; -1 = no drag
    private int _routeDragSlot;        // the gap it would land in: N is above row N, the row count below the last
    private Border? _routeDragGhost;
    private DispatcherTimer? _routeDragScroll;

    /// <summary>Which gap a height in the list means: the nearest, never above the current stop.</summary>
    internal static int RouteDropSlot(double y, double rowHeight, int current, int count) =>
        Math.Clamp((int)Math.Round(y / rowHeight), Math.Min(current, count), count);

    /// <summary>The gap as Route.Move's target, which counts with the stop already lifted out. A gap beside the stop lands it where it was.</summary>
    internal static int RouteMoveTarget(int from, int slot) => slot > from ? slot - 1 : slot;

    private void InitRouteDrag()
    {
        // Esc on a drag is the drag's: handled here, the dialog's own Esc never sees the press, so its release closes nothing.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _routeDragFrom < 0) return;
            e.Handled = true;
            EndRouteDrag();
        };
        Closed += (_, _) => EndRouteDrag();
    }

    private void WireRouteDrag(RouteRow row, int index)
    {
        row.Grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (index < _actions.Route.Current || row.Remove.IsMouseOver) return;
            _routePressed = index;
            _routePressedAt = e.GetPosition(RouteRows);
        };
        row.Grid.PreviewMouseMove += (_, e) =>
        {
            var at = e.GetPosition(RouteRows);
            if (_routeDragFrom >= 0)
            {
                if (e.LeftButton == MouseButtonState.Pressed) MoveRouteDrag(at.Y);
                else EndRouteDrag(); // the release was missed: never leave a stop stuck to the mouse
                return;
            }
            if (_routePressed != index) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _routePressed = -1;
                return;
            }
            if (Math.Abs(at.Y - _routePressedAt.Y) < SystemParameters.MinimumVerticalDragDistance
                && Math.Abs(at.X - _routePressedAt.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            BeginRouteDrag(index);
            MoveRouteDrag(at.Y);
        };
        row.Grid.PreviewMouseLeftButtonUp += (_, e) =>
        {
            _routePressed = -1;
            if (_routeDragFrom < 0) return; // a press that never moved: the name's click goes ahead
            e.Handled = true;
            DropRouteDrag();
        };
        row.Grid.LostMouseCapture += (_, _) => EndRouteDrag();
    }

    private void BeginRouteDrag(int index)
    {
        _routePressed = -1;
        _routeDragFrom = index;
        _routeDragSlot = index;
        DisarmRouteClear();
        var row = _routeRows[index];
        row.Grid.Opacity = RouteDraggedOpacity;
        row.Grid.CaptureMouse(); // takes it from the name's button, which then never clicks
        Mouse.OverrideCursor = Cursors.SizeNS;
        RouteDropLine.Width = RouteRows.ActualWidth;
        RouteDropLine.Visibility = Visibility.Visible;
        _routeDragGhost = BuildRouteGhost(index);
        RouteDragLayer.Children.Add(_routeDragGhost);
        _routeDragScroll = new DispatcherTimer { Interval = RouteDragScrollEvery };
        _routeDragScroll.Tick += (_, _) => ScrollRouteDrag();
        _routeDragScroll.Start();
    }

    /// <summary>A height in the list: the gap it means, the line there and the copy by the cursor.</summary>
    private void MoveRouteDrag(double y)
    {
        var rowHeight = RouteRows.ActualHeight / Math.Max(1, _routeRows.Count);
        if (rowHeight <= 0) return;
        _routeDragSlot = RouteDropSlot(y, rowHeight, _actions.Route.Current, _routeRows.Count);
        Canvas.SetTop(RouteDropLine, _routeDragSlot * rowHeight - RouteDropLine.Height / 2);
        // Just under the cursor, not on it: the gap it means is never more than half a row
        // away, so centred on the cursor it would always cover the line. (Round the list's
        // end, where it has to stay on the rows, the line is drawn over it: XAML's ZIndex.)
        if (_routeDragGhost is { } ghost) Canvas.SetTop(ghost, Math.Clamp(y + rowHeight / 2 + 1, 0, RouteRows.ActualHeight - rowHeight));
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
        var (from, slot) = (_routeDragFrom, _routeDragSlot);
        EndRouteDrag();
        _actions.MoveStop(from, RouteMoveTarget(from, slot)); // a gap beside the stop: Route.Move ignores it
    }

    /// <summary>The one way out of a drag, dropped or not. Safe to call with none: letting go of the capture below comes straight back here.</summary>
    private void EndRouteDrag()
    {
        if (_routeDragFrom < 0) return;
        var from = _routeDragFrom;
        _routeDragFrom = -1;
        _routeDragScroll?.Stop();
        _routeDragScroll = null;
        Mouse.OverrideCursor = null;
        RouteDropLine.Visibility = Visibility.Collapsed;
        if (_routeDragGhost is { } ghost) RouteDragLayer.Children.Remove(ghost);
        _routeDragGhost = null;
        if (from >= _routeRows.Count) return;
        _routeRows[from].Grid.Opacity = 1;
        _routeRows[from].Grid.ReleaseMouseCapture();
    }

    /// <summary>The row being carried: its grip, number, disc and name on the rows' own grid, lit as what stands out in a list. Two surfaces, so a theme's translucent selected fill doesn't show the rows through it.</summary>
    private Border BuildRouteGhost(int index)
    {
        var source = _routeRows[index];
        var grid = NewRouteRowGrid();
        var bright = Theme.Current.Text.Bright.ToBrush();

        void Add(UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        Add(new TextBlock { Text = RouteGrip, Foreground = bright, FontSize = Theme.Current.Size(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 0);
        Add(new TextBlock { Text = source.Number.Text, Foreground = bright, FontSize = Theme.Current.Size(11), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 2);
        Add(new Ellipse { Width = 9, Height = 9, Fill = source.Disc.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 3);
        Add(new TextBlock
        {
            Text = source.NameText.Text, Foreground = bright, FontSize = Theme.Current.Size(12), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0)
        }, 4);
        grid.Background = Theme.Current.Nav.Selected.Fill.ToBrush();

        var rowHeight = RouteRows.ActualHeight / Math.Max(1, _routeRows.Count);
        return new Border
        {
            Child = grid, Width = RouteRows.ActualWidth, Height = rowHeight,
            Background = CardSurface, BorderBrush = Theme.Current.Text.Highlight.ToBrush(), BorderThickness = new Thickness(1),
        };
    }
}
