using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PandoraOverlay;

/// <summary>
/// The big map (the big map plan, phase 3; the owner's idea of Oct 2 2026):
/// the island large in the middle of the screen over a dimmed backdrop,
/// opened and closed by a hotkey, with zoom and drag-to-pan "like a normal
/// map in a game". It shows what the minimap shows — your arrow, the
/// waypoints, friends, the trail, the areas, the heatmap — at a size where
/// names fit, each behind its own header button (remembered in config,
/// independent of the minimap's). Interactive while open: it takes focus,
/// so the game lets go of the cursor (only OUR window is activated; the game
/// is never touched). Not a widget: no edit-mode position, no snapping, no
/// scale slider; it opens fitted to the land every time. The dim is a
/// separate ScrimWindow that owns this one (the perf spike's choice); this
/// window is opaque and caches its still layers. A pure consumer of the
/// shared PollService stream: the heatmap and the roster it shows ride the
/// same requests the minimap's do (PollService.Maps.cs).
/// Closing: the hotkey (MainWindow), M or Esc, a click on the backdrop, or
/// focus going elsewhere. Drawing and layers: BigMapWindow.Layers.cs.
/// </summary>
public partial class BigMapWindow : Window
{
    private const double SideOfScreen = 0.88;       // the window: a square this share of the monitor's shorter side (D2)
    private const int GlideFrameRate = 30;          // the spike: halves the cost, invisible on a 2.7 s glide
    private const double ScreenPixelsPerMapPixel = 3; // the sharpness cap: past this the 1000 px picture is just blur
    private const double ZoomStep = 1.15;           // the minimap's wheel step
    private const double SnapRadius = 12;           // a click / hover this close to a marker means it
    private const string Hint = "wheel = zoom  ·  drag = pan  ·  right-click = waypoint menu  ·  Space = centre on me";
    private static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(3);

    private readonly OverlayConfig _config;
    private readonly PollService _poll;
    private readonly WaypointLibrary _library;
    private readonly FriendBook _book;
    private readonly BreadcrumbTrail _trail;
    private readonly MapActions _actions;
    private readonly ScrimWindow _scrim;
    private readonly MapMenuBuilder _menuBuilder;
    private readonly DispatcherTimer _noticeTimer;

    private MapViewport _view;        // the panel, the map's rendered side and where its top-left sits (panel coordinates)
    private bool _hasView;            // false until the map's panel has a size: the view is fitted then
    private Rect _land = new(0, 0, 1, 1); // LandBounds: the opening fit and the pan limit
    private double _maxZoom = 3;
    private Point? _dragFrom;         // the cursor where a drag began, and the map's offset then
    private Point _dragOffset;
    private bool _closing;

    public BigMapWindow(OverlayConfig config, PollService poll, WaypointLibrary library, FriendBook book,
                        BreadcrumbTrail trail, MapActions actions, Rect monitor)
    {
        InitializeComponent();
        _config = config;
        _poll = poll;
        _library = library;
        _book = book;
        _trail = trail;
        _actions = actions;

        var side = Math.Round(Math.Min(monitor.Width, monitor.Height) * SideOfScreen);
        Width = Height = side;
        Left = monitor.Left + (monitor.Width - side) / 2;
        Top = monitor.Top + (monitor.Height - side) / 2;
        _scrim = new ScrimWindow(monitor);
        _scrim.Clicked += Close;

        Map.UseMapSpace(GlideFrameRate);
        Readout.RenderTransform = _readoutAt;
        HintText.Text = Hint;
        _noticeTimer = new DispatcherTimer { Interval = NoticeHold };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            HintText.Text = Hint;
            HintText.Foreground = HintBrush;
        };
        _menuBuilder = new MapMenuBuilder(MapMenuItems, (Style)FindResource("MenuButton"), MenuSeparator, _actions, FriendBrush,
                                          () => _lastWorld, () => MapMenu.IsOpen = false, ShowNotice);

        AreasButton.IsChecked = config.BigMapAreas;
        NamesButton.IsChecked = config.BigMapNames;
        WaypointsButton.IsChecked = config.BigMapWaypoints;
        FriendsButton.IsChecked = config.BigMapFriends;
        TrailButton.IsChecked = config.BigMapTrail;
        HeatmapButton.IsChecked = config.BigMapHeatmap;

        // Start from what is known now: your last position, the last roster, a kept heatmap.
        _friends = poll.Friends;
        if (poll.LastPlayer is { } me && poll.Calibration is { } cal) TakeFix(me, cal);
        if (config.BigMapHeatmap && poll.Heatmap is { } kept) Map.ShowHeatmap(kept);

        _poll.SnapshotReceived += OnSnapshot;
        _poll.FriendsChanged += OnFriends;
        _poll.HeatmapChanged += OnHeatmap;
        _poll.CalibrationChanged += OnCalibrationChanged;
        _library.Changed += OnLibraryChanged;
        _book.Changed += OnBookChanged;
        _actions.WaypointTracked += OnWaypointTracked;
        _actions.FriendTracked += OnFriendTracked;
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            _poll.SnapshotReceived -= OnSnapshot;
            _poll.FriendsChanged -= OnFriends;
            _poll.HeatmapChanged -= OnHeatmap;
            _poll.CalibrationChanged -= OnCalibrationChanged;
            _library.Changed -= OnLibraryChanged;
            _book.Changed -= OnBookChanged;
            _actions.WaypointTracked -= OnWaypointTracked;
            _actions.FriendTracked -= OnFriendTracked;
            _noticeTimer.Stop();
            MapMenu.IsOpen = false;
            _scrim.Close();
        };
    }

    private static readonly Brush HintBrush = Frozen(Color.FromRgb(0x7B, 0x87, 0x90));
    private static readonly Brush NoticeBrush = Frozen(Color.FromRgb(0xEC, 0xF2, 0xF8));
    private static readonly Brush MenuSeparator = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Shows the backdrop, then the map over it (owned by it, so it stays
    /// above), and takes focus: the hotkey press grants foreground rights,
    /// as for the control panel. The game releases the cursor.
    /// </summary>
    public void Open()
    {
        _scrim.Show();
        Owner = _scrim;
        Show();
        Activate();
        MapHost.Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW)); // no Alt-Tab entry
        // Windows 11 rounds the corners; Windows 10 doesn't know the attribute and keeps them square.
        var round = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    // ---- Closing --------------------------------------------------------------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return; // the combos are the global hotkeys' business
        switch (e.Key)
        {
            case Key.Escape:
            case Key.M:
                Close();
                e.Handled = true;
                break;
            case Key.Space:
                CentreOnMe();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Focus went elsewhere (Alt-Tab, a click on another monitor): the map is a glance, it closes.</summary>
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing && !MapMenu.IsOpen) Close();
    }

    // ---- The view -------------------------------------------------------------

    /// <summary>The first size the map's panel gets: the view is fitted to the land then, and every layer placed.</summary>
    private void MapHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_hasView || MapHost.ActualWidth <= 0 || MapHost.ActualHeight <= 0) return;
        var panel = new Size(MapHost.ActualWidth, MapHost.ActualHeight);
        if (AreaMapAsset.Shared is { } areas && LandBounds.Of(areas) is { } land) _land = land;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _maxZoom = Math.Max(1, MapViewport.ZoomForDensity(Math.Min(panel.Width, panel.Height), MapCanvas.PicturePixels, ScreenPixelsPerMapPixel / dpi));
        _view = Snapped(MapViewport.Fit(panel, _land, 1, _maxZoom)); // D4: fitted to the land; the wheel can go out to the whole picture
        _hasView = true;
        ApplyView(zoomChanged: true);
    }

    /// <summary>
    /// The offset on whole device pixels: the cached layers are pictures, and
    /// a picture moved by a fraction of a pixel is resampled — names and lines
    /// would soften after every drag.
    /// </summary>
    private MapViewport Snapped(MapViewport view)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return view with { Offset = new Point(Math.Round(view.Offset.X * dpi) / dpi, Math.Round(view.Offset.Y * dpi) / dpi) };
    }

    /// <summary>
    /// Puts the view on screen. A pan only moves the map: everything rides
    /// its translate. A zoom resizes the map — re-rendering the cached
    /// pictures sharp, never stretching them — and re-places every layer.
    /// </summary>
    private void ApplyView(bool zoomChanged)
    {
        Map.MoveMap(_view.Offset, glide: null);
        if (!zoomChanged) return;
        Map.SetMapSize(_view.MapSize);
        DrawLayers();
        ZoomText.Text = $"{_view.Zoom:0.0}×";
        UpdateScaleBar();
    }

    private void Pan(Vector by)
    {
        _view = Snapped((_view with { Offset = _dragOffset + by }).ClampPan(_land));
        ApplyView(zoomChanged: false);
    }

    private void MapHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_hasView) return;
        var zoomed = _view.ZoomAround(e.GetPosition(MapHost), e.Delta > 0 ? ZoomStep : 1 / ZoomStep, 1, _maxZoom);
        if (zoomed == _view) return;
        _view = Snapped(zoomed.ClampPan(_land));
        ApplyView(zoomChanged: true);
        ShowReadout(e.GetPosition(MapHost));
        e.Handled = true;
    }

    /// <summary>Space: your arrow to the middle, the zoom kept.</summary>
    private void CentreOnMe()
    {
        if (!_hasView || _lastFix is not { } fix) return;
        var at = new Point(fix.Fx * _view.MapSize, fix.Fy * _view.MapSize);
        _view = Snapped((_view with { Offset = new Point(_view.Center.X - at.X, _view.Center.Y - at.Y) }).ClampPan(_land));
        ApplyView(zoomChanged: false);
    }

    // ---- Mouse ----------------------------------------------------------------

    private void MapHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasView) return;
        _dragFrom = e.GetPosition(MapHost);
        _dragOffset = _view.Offset;
        MapHost.CaptureMouse();
        e.Handled = true;
    }

    private void MapHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFrom is null) return;
        _dragFrom = null;
        MapHost.ReleaseMouseCapture();
        ShowReadout(e.GetPosition(MapHost));
    }

    private void MapHost_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(MapHost);
        if (_dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed)
        {
            Readout.Visibility = Visibility.Collapsed;
            Pan(pos - from);
            return;
        }
        ShowReadout(pos);
    }

    private void MapHost_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not null) return; // captured: the drag goes on
        Readout.Visibility = Visibility.Collapsed;
        SetHover(null);
    }

    /// <summary>
    /// The shared map menu, for the spot under the cursor: on a friend or a
    /// waypoint its own entries first, then "Waypoint here" and share-a-spot
    /// (MapMenuBuilder, MapActions — the minimap's very menu).
    /// </summary>
    private void MapHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_hasView || _poll.Calibration is not { } cal) return;
        var pos = e.GetPosition(MapHost);
        var (fx, fy) = _view.FractionAt(pos);
        (double X, double Y)? world = cal.ToWorld(Math.Clamp(fx, 0, 1), Math.Clamp(fy, 0, 1));
        var friend = Map.FriendAt(pos, SnapRadius);
        var hit = friend is null ? Map.WaypointAt(pos, SnapRadius) : null;
        if (friend is { X: { } friendX, Y: { } friendY }) world = (friendX, friendY);
        else if (hit is not null) world = (hit.X, hit.Y);

        _menuBuilder.Build(new MapMenuSpot(world, hit, friend));
        MapMenu.IsOpen = true;
        Dispatcher.BeginInvoke(() => MapMenuPanel.Focus(), DispatcherPriority.Input); // so Escape reaches the menu, not the map
        e.Handled = true;
    }

    private void MapMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        MapMenu.IsOpen = false;
        e.Handled = true;
    }

    /// <summary>A short message in place of the hint line (added / copied / removed); the timer restores the hint.</summary>
    private void ShowNotice(string text)
    {
        HintText.Text = text;
        HintText.Foreground = NoticeBrush;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    // ---- Win32 ----------------------------------------------------------------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
