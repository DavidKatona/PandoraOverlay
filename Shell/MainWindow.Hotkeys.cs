using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PandoraOverlay;

/// <summary>
/// MainWindow, hotkeys part: the five global hotkeys — ids, late-key
/// candidates, registration with fallback, the settings-dialog suspension,
/// and the WM_HOTKEY dispatch. Same class as MainWindow.xaml.cs, split for
/// reading; see CLAUDE.md.
/// </summary>
public partial class MainWindow
{
    // ---- Global hotkeys (registered once, on this window's hwnd) ----------
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA11C;        // edit mode (0xA11D is the settings dialog's test id)
    private const int HideAllHotkeyId = 0xA11E;
    private const int ViewHotkeyId = 0xA11F;
    private const int HeatmapHotkeyId = 0xA120;
    private const int PrimeHotkeyId = 0xA121;

    // Tried in order when a late-added key's combo collides with one the
    // user already gave another action; always one more candidate than
    // takers, so one is always free.
    private static readonly HotkeySpec[] HeatmapHotkeyCandidates =
    {
        new(ModifierKeys.Control, Key.F6),
        new(ModifierKeys.Control, Key.F8),
        new(ModifierKeys.Control, Key.F9),
        new(ModifierKeys.Control, Key.F11)
    };

    private static readonly HotkeySpec[] PrimeHotkeyCandidates =
    {
        new(ModifierKeys.Control, Key.F8),
        new(ModifierKeys.Control, Key.F9),
        new(ModifierKeys.Control, Key.F11),
        new(ModifierKeys.Control, Key.F12),
        new(ModifierKeys.Control, Key.F6)
    };

    /// <summary>
    /// Re-registers all hotkeys after a settings change. Everything is
    /// unregistered first so swapped combos can't collide with themselves;
    /// the dialog availability-checked each combo, but another app can still
    /// grab one in the meantime — then that hotkey falls back to its old,
    /// still-working combo.
    /// </summary>
    private void ApplyHotkeysFromConfig()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HotkeySpec.Unregister(hwnd, HotkeyId);
        HotkeySpec.Unregister(hwnd, HideAllHotkeyId);
        HotkeySpec.Unregister(hwnd, ViewHotkeyId);
        HotkeySpec.Unregister(hwnd, HeatmapHotkeyId);
        HotkeySpec.Unregister(hwnd, PrimeHotkeyId);

        _registerFailures.Clear();
        _hotkey = RegisterWithFallback(hwnd, HotkeyId, _config.Hotkey, _hotkey, v => _config.Hotkey = v);
        _hotkeyHide = RegisterWithFallback(hwnd, HideAllHotkeyId, _config.HotkeyHideAll, _hotkeyHide, v => _config.HotkeyHideAll = v);
        _hotkeyView = RegisterWithFallback(hwnd, ViewHotkeyId, _config.HotkeyMinimapView, _hotkeyView, v => _config.HotkeyMinimapView = v);
        _hotkeyHeatmap = RegisterWithFallback(hwnd, HeatmapHotkeyId, _config.HotkeyHeatmap, _hotkeyHeatmap, v => _config.HotkeyHeatmap = v);
        _hotkeyPrime = RegisterWithFallback(hwnd, PrimeHotkeyId, _config.HotkeyPrimeCheck, _hotkeyPrime, v => _config.HotkeyPrimeCheck = v);
        _config.Save();
        UpdateHotkeyTexts();

        if (_registerFailures.Count == 0) _tray.ClearHotkeyConflict();
        else _tray.ShowHotkeyConflict(string.Join(", ", _registerFailures));
    }

    /// <summary>
    /// The heatmap and Prime hotkeys arrived after people had already
    /// customized the earlier ones, so their defaults can collide with one of
    /// those — and a duplicate inside our own app would fail to register and
    /// read as "in use by another app". Then the first free candidate wins
    /// and is written back. (The settings dialog rejects duplicates at
    /// capture time, so this only matters for that upgrade case and
    /// hand-edited configs.)
    /// </summary>
    private static HotkeySpec ResolveLateHotkey(string configured, HotkeySpec[] candidates, HotkeySpec[] taken, Action<string> writeBack)
    {
        var wanted = HotkeySpec.TryParse(configured) ?? candidates[0];
        if (!taken.Contains(wanted)) return wanted;

        var free = candidates.First(c => !taken.Contains(c));
        writeBack(free.ToString());
        return free;
    }

    private HotkeySpec RegisterWithFallback(IntPtr hwnd, int id, string configured, HotkeySpec fallback, Action<string> writeBack)
    {
        var wanted = HotkeySpec.TryParse(configured) ?? fallback;
        if (HotkeySpec.Register(hwnd, id, wanted)) return wanted;

        _registerFailures.Add(wanted.ToString());
        HotkeySpec.Register(hwnd, id, fallback);
        writeBack(fallback.ToString());
        StatusText.Text = $"Hotkey {wanted} unavailable — keeping {fallback}";
        return fallback;
    }

    private void UpdateHotkeyTexts()
    {
        _controlPanel?.SetHotkeyLabel(_hotkey.ToString());
        _tray.UpdateHotkeyLabels(_hotkey.ToString(), _hotkeyHide.ToString());
    }

    // ---- Window setup ------------------------------------------------------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); // applies the click-through styles
        var hwnd = new WindowInteropHelper(this).Handle;

        // Surface registration failures instead of swallowing them: another
        // app holding a combo at our launch would otherwise leave that hotkey
        // silently dead for the whole session.
        _registerFailures.Clear();
        if (!HotkeySpec.Register(hwnd, HotkeyId, _hotkey)) _registerFailures.Add(_hotkey.ToString());
        if (!HotkeySpec.Register(hwnd, HideAllHotkeyId, _hotkeyHide)) _registerFailures.Add(_hotkeyHide.ToString());
        if (!HotkeySpec.Register(hwnd, ViewHotkeyId, _hotkeyView)) _registerFailures.Add(_hotkeyView.ToString());
        if (!HotkeySpec.Register(hwnd, HeatmapHotkeyId, _hotkeyHeatmap)) _registerFailures.Add(_hotkeyHeatmap.ToString());
        if (!HotkeySpec.Register(hwnd, PrimeHotkeyId, _hotkeyPrime)) _registerFailures.Add(_hotkeyPrime.ToString());
        if (_registerFailures.Count > 0)
        {
            var combos = string.Join(", ", _registerFailures);
            StatusText.Text = $"Hotkey {combos} in use by another app — rebind in Settings";
            _tray.ShowHotkeyConflict(combos);
        }

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case HotkeyId:
                    ToggleEditMode();
                    handled = true;
                    break;
                case HideAllHotkeyId:
                    ToggleOverlayVisibility();
                    handled = true;
                    break;
                case ViewHotkeyId:
                    _minimap?.ToggleView();
                    handled = true;
                    break;
                case HeatmapHotkeyId:
                    ToggleHeatmap();
                    handled = true;
                    break;
                case PrimeHotkeyId:
                    CheckPrime();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

}
