using System.Windows;
using System.Windows.Input;

namespace PandoraOverlay;

/// <summary>
/// The edit-mode control panel: appears with edit mode, vanishes on lock. It
/// hosts the labeled controls that used to live on per-widget banners
/// (Settings, minimap toggles, Lock, Exit) plus the hint line — which is what
/// lets the stats panel and minimap stay static-size in both modes. Since 1.39
/// it has a fixed frame of its own (ControlPanelFrame), the same in every
/// theme, so no font can grow it. Draggable
/// and snapping like every overlay window, but never a snap TARGET (transient
/// chrome), and permanently interactive while visible. First show defaults to
/// bottom-center; the position persists via config.
/// What belongs here: arranging the HUD right now, one widget's controls per
/// captioned group. A preference set once goes to Settings; a lifeline that
/// must work while locked goes to the tray; a mid-game action used often
/// earns a hotkey. Being unreachable while locked is fine for this panel.
/// </summary>
public partial class ControlPanelWindow : OverlayWindowBase
{
    private readonly OverlayConfig _config;
    private readonly Action _openSettings;
    private readonly Action _toggleStats;
    private readonly Action _toggleStatsView;
    private readonly Action _toggleMinimap;
    private readonly Action _toggleMinimapView;
    private readonly Action _toggleHeatmap;
    private readonly Action _toggleAreaBorders;
    private readonly Action _togglePrime;
    private readonly Action _checkPrime;
    private readonly Action _toggleActivity;
    private readonly Action _togglePack;
    private readonly Func<PanelLayers> _layers;
    private readonly Action _lockOverlay;
    private readonly Action _exit;

    protected override bool IsSnapTarget => false;

    /// <summary>Esc in the open Layers menu is the menu's (it closes it); the next locks edit mode.</summary>
    protected override bool EscapeIsMine => !LayersMenu.IsOpen;

    /// <summary>Its own Scale (Settings → Widgets) since 1.37; it rode the stats panel's before.</summary>
    protected override double AppearanceScale(OverlayConfig config) => config.ControlPanelScale;

    /// <param name="layers">What the Layers menu marks when it opens and after each row: the minimap's state and the two hotkeys' labels.</param>
    public ControlPanelWindow(OverlayConfig config, Action openSettings, Action toggleStats, Action toggleStatsView,
                              Action toggleMinimap, Action toggleMinimapView, Action toggleHeatmap, Action toggleAreaBorders,
                              Func<PanelLayers> layers,
                              Action togglePrime, Action checkPrime, Action toggleActivity, Action togglePack,
                              Action lockOverlay, Action exit)
    {
        InitializeComponent();

        _config = config;
        _openSettings = openSettings;
        _toggleStats = toggleStats;
        _toggleStatsView = toggleStatsView;
        _toggleMinimap = toggleMinimap;
        _toggleMinimapView = toggleMinimapView;
        _toggleHeatmap = toggleHeatmap;
        _toggleAreaBorders = toggleAreaBorders;
        _togglePrime = togglePrime;
        _checkPrime = checkPrime;
        _toggleActivity = toggleActivity;
        _togglePack = togglePack;
        _layers = layers;
        _lockOverlay = lockOverlay;
        _exit = exit;

        ApplyAppearance(config);
        LayersMenu.PlacementTarget = LayersButton;
        LayersMenu.CustomPopupPlacementCallback = PlaceLayersMenu;

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

    /// <summary>Bottom-centre of the current screen — the first-show spot, and where Reset positions puts it back.</summary>
    public void PlaceDefault() => PlaceBottomCenter();

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
        // Nothing to restyle — the panel only exists in edit mode, and its
        // frame is Lit for good (its XAML): the theme's control-panel look.
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

    private void Settings_Click(object sender, RoutedEventArgs e) => _openSettings();

    private void Stats_Click(object sender, RoutedEventArgs e) => _toggleStats();

    private void StatsView_Click(object sender, RoutedEventArgs e) => _toggleStatsView();

    private void Minimap_Click(object sender, RoutedEventArgs e) => _toggleMinimap();

    private void Prime_Click(object sender, RoutedEventArgs e) => _togglePrime();

    private void CheckPrime_Click(object sender, RoutedEventArgs e) => _checkPrime();

    private void Activity_Click(object sender, RoutedEventArgs e) => _toggleActivity();

    private void Pack_Click(object sender, RoutedEventArgs e) => _togglePack();

    /// <summary>
    /// The PACK button follows whether anyone is watched: with nobody, there is
    /// no Pack window to show or hide, so it is greyed and says where the Watch
    /// ticks are.
    /// </summary>
    public void SetPackAvailable(bool anyWatched)
    {
        PackButton.IsEnabled = anyWatched;
        PackButton.ToolTip = anyWatched ? "Show or hide the Pack: your watched friends' stats" : "Tick Watch beside a friend on Settings → Friends";
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => _lockOverlay();

    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();
}
