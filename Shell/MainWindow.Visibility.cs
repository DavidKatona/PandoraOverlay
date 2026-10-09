using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// MainWindow, visibility part: per-widget show/hide, the hide-all toggle,
/// the not-in-game auto-hide, edit mode and the control panel. Same class
/// as MainWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class MainWindow
{
    // ---- Auto-hide while not in-game ---------------------------------------
    // Consecutive not-in-game polls this long before the overlay hides itself:
    // enough to ride out a single spurious inGame:false, short enough that the
    // spawn menu and a server restart clear the screen. Coming back costs
    // nothing (the first in-game poll does it), so there is nothing else to
    // wait for. A constant on purpose — not a preference anyone should tune.
    private static readonly TimeSpan AutoHideGrace = TimeSpan.FromSeconds(30);
    private bool _autoHidden;
    private DateTime? _notInGameSince;

    // ---- Widget visibility -------------------------------------------------

    /// <summary>Control panel: hides or shows the stats panel itself — the app keeps running via tray + hotkeys.</summary>
    // Hide, never Close: the global hotkeys are registered on this window's
    // hwnd, and its Closed handler shuts the whole overlay down.
    private void ToggleStats()
    {
        _config.StatsEnabled = !_config.StatsEnabled;
        if (_overlayHidden || _autoHidden) return; // takes effect when the overlay is shown again
        if (_config.StatsEnabled) Show(); else Hide();
    }

    /// <summary>
    /// Heatmap hotkey / control panel: flips the minimap's heatmap layer
    /// (persisted with the next Save). Ignored while the minimap is hidden —
    /// flipping an invisible layer would only surprise whoever shows the map
    /// again later.
    /// </summary>
    private void ToggleHeatmap()
    {
        if (_bigMap is not null)
        {
            _bigMap.ToggleHeatmap(); // the map on screen is the big map: its own layer (D8)
            return;
        }
        if (_minimap is null || _overlayHidden || _autoHidden) return;
        _config.HeatmapEnabled = !_config.HeatmapEnabled;
        _ = _poll.RefreshHeatmapAsync(); // the latest picture (fetched only if a minute old), or null to clear the layer
    }

    /// <summary>
    /// The control panel's Areas button: flips the minimap's area-border
    /// layer (persisted with the next Save). A layer you switch, like the
    /// heatmap, so it lives on the control panel and not in Settings; no
    /// hotkey until it proves to be flipped mid-game often. Local drawing
    /// from the bundled area map — no request.
    /// </summary>
    private void ToggleAreaBorders() => _minimap?.ToggleAreaBorders();

    private void Minimap_Click(object sender, RoutedEventArgs e) => ToggleMinimap();

    private void ToggleMinimap()
    {
        if (_minimap is null)
        {
            ShowMinimap();
        }
        else
        {
            _config.MinimapEnabled = false;
            _minimap.Close();
            _ = _poll.RefreshFriendsAsync(); // stops the friends fetch if the widget is hidden too
        }
    }

    private void ShowMinimap()
    {
        if (_minimap is null)
        {
            _minimap = new MinimapWindow(_config, _poll, _library, _book, _trail, MapActions); // the trail and the menu actions shared with the big map
            _minimap.Closed += (_, _) => _minimap = null;
            _minimap.SetArea(_areaJournal.Current); // shown mid-session: it starts from where you already are
            _minimap.Show();
        }
        _config.MinimapEnabled = true;
        _ = _poll.RefreshHeatmapAsync(); // a fresh window starts without the layer
        _ = _poll.RefreshFriendsAsync(); // and the friend arrows may have been off with it
        if (EditMode) _minimap.SetEditMode(true);
    }

    private void TogglePrime()
    {
        if (_prime is null)
        {
            ShowPrime();
        }
        else
        {
            _config.PrimeEnabled = false;
            _config.PrimeX = _prime.Left; // a hidden widget comes back where it was
            _config.PrimeY = _prime.Top;
            _prime.Close();
        }
    }

    private void ShowPrime()
    {
        if (_prime is null)
        {
            _prime = new PrimeWindow(_config, _poll);
            _prime.Closed += (_, _) => _prime = null;
            _prime.Show();
        }
        _config.PrimeEnabled = true;
        if (EditMode) _prime.SetEditMode(true);
    }

    private void ToggleActivity()
    {
        if (_activity is null)
        {
            ShowActivity();
        }
        else
        {
            _config.ActivityEnabled = false;
            _config.ActivityX = _activity.Left; // a hidden widget comes back where it was
            _config.ActivityY = _activity.Top;
            _activity.Close();
            _ = _poll.RefreshFriendsAsync(); // stops the friends fetch unless the minimap still draws them
        }
    }

    private void ShowActivity()
    {
        if (_activity is null)
        {
            _activity = new ActivityWindow(_config, _poll, _book, _feed, _log);
            _activity.Closed += (_, _) => _activity = null;
            _activity.Show();
        }
        _config.ActivityEnabled = true;
        _ = _poll.RefreshFriendsAsync(); // the widget wants a roster
        if (EditMode) _activity.SetEditMode(true);
    }

    /// <summary>
    /// The control panel's Check and the Check Prime hotkey (the tray's entry
    /// gave way to the hotkey): the ONLY trigger for a prime check —
    /// user-triggered, never timed (the endpoint is approved on that
    /// condition). Asking for a check implies wanting to see the answer, so
    /// the widget is shown first.
    /// </summary>
    private void CheckPrime()
    {
        CloseBigMap(); // the answer shows on the Prime widget
        if (_overlayHidden || _autoHidden) ToggleOverlayVisibility();
        ShowPrime();
        _ = _poll.CheckPrimeAsync();
    }

    /// <summary>
    /// Hide-all hotkey / tray: hides all four widgets for screenshots or cutscenes.
    /// Polling continues (the state stays warm); hidden is never persisted —
    /// the app always starts visible.
    /// </summary>
    private void ToggleOverlayVisibility()
    {
        if (_bigMap is not null)
        {
            // The map is what is on screen: hide-all closes it and leaves the
            // screen clear (the widgets stay hidden). Auto-hidden stays that
            // way rather than turning into a manual hide: never both flags.
            if (!_autoHidden) _overlayHidden = true;
            CloseBigMap();
            return;
        }
        if (_autoHidden)
        {
            RevealAutoHidden(); // the user wants it back: show it with a fresh grace (UpdateAutoHide hides it again if still not in game when that runs out)
            return;
        }
        if (!_overlayHidden)
        {
            if (EditMode) ToggleEditMode(); // lock + persist before vanishing
            _overlayHidden = true;
            HideWindows();
        }
        else
        {
            _overlayHidden = false;
            ShowWindows();
            _poll.Nudge(); // someone is looking again — don't wait out an idle poll interval
        }
    }

    private void HideWindows()
    {
        Hide();
        _minimap?.Hide();
        _prime?.Hide();
        _activity?.Hide();
    }

    private void ShowWindows()
    {
        if (_bigMap is not null) return; // the big map hides the widgets; closing it brings them back (OnBigMapClosed)
        if (_config.StatsEnabled) Show(); // a deliberately hidden stats panel stays hidden
        _minimap?.Show();
        _prime?.Show();
        _activity?.Show();
    }

    /// <summary>
    /// The not-in-game auto-hide (`HideWhenNotInGame`), driven by the poll
    /// stream. Hides after AutoHideGrace of consecutive not-in-game polls and
    /// shows again on the first in-game one. Never while editing, and never
    /// over a manual hide (that one is the user's word). A reveal by the user
    /// (hotkey, tray, edit mode, Check Prime) just restarts the grace — a
    /// "stay until you spawn" rule was tried and read as a bug: after
    /// unlocking and locking again the overlay refused to hide. Hidden state
    /// is runtime-only, like the manual hide.
    /// </summary>
    private void UpdateAutoHide(bool inGame)
    {
        if (inGame)
        {
            _notInGameSince = null;
            if (_autoHidden)
            {
                _autoHidden = false;
                ShowWindows();
            }
            return;
        }

        _notInGameSince ??= DateTime.UtcNow;
        if (!_config.HideWhenNotInGame || _autoHidden || _overlayHidden || EditMode) return;
        if (DateTime.UtcNow - _notInGameSince < AutoHideGrace) return;

        _autoHidden = true;
        HideWindows();
    }

    /// <summary>The user asked for the overlay while it had hidden itself: show it, with a fresh grace period.</summary>
    private void RevealAutoHidden()
    {
        _autoHidden = false;
        _notInGameSince = null;
        ShowWindows();
        _tray.SetStatus("Pandora Overlay — not in-game");
        _poll.Nudge();
    }

    // ---- Edit mode ----------------------------------------------------------
    private void ToggleEditMode()
    {
        CloseBigMap(); // editing is about the widgets: they come back first
        if (_overlayHidden || _autoHidden) ToggleOverlayVisibility(); // un-hide first, then edit as usual

        var on = !EditMode;
        SetEditMode(on);
        _minimap?.SetEditMode(on);
        _prime?.SetEditMode(on);
        _activity?.SetEditMode(on);

        if (on)
        {
            ShowControlPanel();
            _poll.Nudge();
        }
        else
        {
            _controlPanel?.Hide();
            PersistState();
            _notInGameSince = null; // a fresh grace period: time to look at the locked layout before it auto-hides
        }
    }

    private void ShowControlPanel()
    {
        if (_controlPanel is null)
        {
            _controlPanel = new ControlPanelWindow(
                _config,
                openSettings: OpenSettings,
                toggleStats: ToggleStats,
                toggleStatsView: ToggleStatsView,
                toggleMinimap: ToggleMinimap,
                toggleMinimapView: () => _minimap?.ToggleView(),
                toggleHeatmap: ToggleHeatmap,
                toggleAreaBorders: ToggleAreaBorders,
                togglePrime: TogglePrime,
                checkPrime: CheckPrime,
                toggleActivity: ToggleActivity,
                lockOverlay: ToggleEditMode,
                exit: () => Application.Current.Shutdown());
        }
        _controlPanel.Show();
        _controlPanel.SetEditMode(true); // permanently interactive while visible
        _controlPanel.SetHotkeyLabel(_hotkey.ToString());

        // Take focus away from the game so it releases its mouse capture and
        // the cursor becomes visible. Windows grants us foreground rights
        // here because our registered hotkey (or a tray click) triggered
        // this; only OUR window is activated — the game is never touched.
        _controlPanel.Activate();
    }

    protected override void OnEditModeChanged(bool editMode)
    {
        RootPanel.BorderBrush = editMode ? BorderEdit : BorderLocked;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragIfEditing(e);

}
