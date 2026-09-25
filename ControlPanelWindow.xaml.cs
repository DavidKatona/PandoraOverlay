using System.Windows;
using System.Windows.Input;

namespace PandoraOverlay;

/// <summary>
/// The edit-mode control panel: appears with edit mode, vanishes on lock. It
/// hosts the labeled controls that used to live on per-widget banners
/// (Settings, minimap toggles, Lock, Exit) plus the hint line — which is what
/// lets the stats panel and minimap stay static-size in both modes. Draggable
/// and snapping like every overlay window, but never a snap TARGET (transient
/// chrome), and permanently interactive while visible. First show defaults to
/// bottom-center; the position persists via config.
/// </summary>
public partial class ControlPanelWindow : OverlayWindowBase
{
    private readonly OverlayConfig _config;
    private readonly Action _openSettings;
    private readonly Action _toggleStats;
    private readonly Action _toggleMinimap;
    private readonly Action _toggleMinimapView;
    private readonly Action _toggleHeatmap;
    private readonly Action _togglePrime;
    private readonly Action _checkPrime;
    private readonly Action _lockOverlay;
    private readonly Action _exit;

    protected override bool IsSnapTarget => false;

    public ControlPanelWindow(OverlayConfig config, Action openSettings, Action toggleStats,
                              Action toggleMinimap, Action toggleMinimapView, Action toggleHeatmap,
                              Action togglePrime, Action checkPrime,
                              Action lockOverlay, Action exit)
    {
        InitializeComponent();

        _config = config;
        _openSettings = openSettings;
        _toggleStats = toggleStats;
        _toggleMinimap = toggleMinimap;
        _toggleMinimapView = toggleMinimapView;
        _toggleHeatmap = toggleHeatmap;
        _togglePrime = togglePrime;
        _checkPrime = checkPrime;
        _lockOverlay = lockOverlay;
        _exit = exit;

        ApplyAppearance(config);

        if (config.ControlPanelX is { } x && config.ControlPanelY is { } y)
        {
            Left = x;
            Top = y;
        }
        else
        {
            Loaded += (_, _) => PlaceBottomCenter();
        }
    }

    private void PlaceBottomCenter()
    {
        var bounds = GetScreenBoundsDips();
        Left = bounds.Left + (bounds.Width - ActualWidth) / 2;
        Top = bounds.Bottom - ActualHeight - 60;
    }

    /// <summary>Reapplies scale/opacity after a settings save.</summary>
    public void ApplySettingsFromConfig() => ApplyAppearance(_config);

    public void SetHotkeyLabel(string hotkey) =>
        HintText.Text = $"drag panels to move · right-click map = waypoints · {hotkey} locks";

    protected override void OnEditModeChanged(bool editMode)
    {
        // Nothing to restyle — the panel only exists in edit mode and always
        // wears the edit accent border.
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    private void Settings_Click(object sender, RoutedEventArgs e) => _openSettings();

    private void Stats_Click(object sender, RoutedEventArgs e) => _toggleStats();

    private void Minimap_Click(object sender, RoutedEventArgs e) => _toggleMinimap();

    private void MapView_Click(object sender, RoutedEventArgs e) => _toggleMinimapView();

    private void Heatmap_Click(object sender, RoutedEventArgs e) => _toggleHeatmap();

    private void Prime_Click(object sender, RoutedEventArgs e) => _togglePrime();

    private void CheckPrime_Click(object sender, RoutedEventArgs e) => _checkPrime();

    private void Lock_Click(object sender, RoutedEventArgs e) => _lockOverlay();

    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();
}
