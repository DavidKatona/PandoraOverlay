using System.Diagnostics;
using System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace PandoraOverlay;

/// <summary>
/// Notification-area icon — the overlay's only always-visible affordance (the
/// windows themselves are click-through, absent from the taskbar and Alt-Tab).
/// The right-click menu drives the same actions as the edit banners; a
/// double-click toggles edit mode. The hover tooltip carries live stats and,
/// when a newer release exists, an update note plus a menu entry opening the
/// download page. WinForms interop, since NotifyIcon has no WPF counterpart.
/// Must be disposed on shutdown or the icon lingers in the tray until hovered.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _editItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _conflictItem;
    private readonly ToolStripSeparator _updateSeparator;
    private string _status = "Pandora Overlay";
    private string _updateSuffix = "";
    private string _conflictSuffix = "";

    public TrayIcon(Action toggleEditMode, Action toggleOverlay, Action toggleStats, Action toggleMinimap, Action openSettings, Action exit)
    {
        _updateItem = new ToolStripMenuItem { Visible = false };
        _updateItem.Click += (_, _) => OpenReleasesPage();
        _conflictItem = new ToolStripMenuItem { Visible = false };
        _conflictItem.Click += (_, _) => openSettings();
        _updateSeparator = new ToolStripSeparator { Visible = false };
        _editItem = new ToolStripMenuItem("Edit mode", null, (_, _) => toggleEditMode())
        {
            ShortcutKeyDisplayString = "Ctrl+F7"
        };
        _overlayItem = new ToolStripMenuItem("Hide/show overlay", null, (_, _) => toggleOverlay())
        {
            ShortcutKeyDisplayString = "Ctrl+F4"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_updateItem);
        menu.Items.Add(_conflictItem);
        menu.Items.Add(_updateSeparator);
        menu.Items.Add(_editItem);
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new ToolStripMenuItem("Show/hide stats panel", null, (_, _) => toggleStats()));
        menu.Items.Add(new ToolStripMenuItem("Show/hide minimap", null, (_, _) => toggleMinimap()));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => exit()));

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = _status,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => toggleEditMode();
    }

    /// <summary>Shows the current hotkeys next to their menu entries.</summary>
    public void UpdateHotkeyLabels(string editLabel, string overlayLabel)
    {
        _editItem.ShortcutKeyDisplayString = editLabel;
        _overlayItem.ShortcutKeyDisplayString = overlayLabel;
    }

    /// <summary>Hover tooltip base text (live stats); NotifyIcon caps the total at 127 chars.</summary>
    public void SetStatus(string text)
    {
        _status = text;
        RefreshTooltip();
    }

    /// <summary>Reveals the update menu entry and appends the tag to the tooltip for the session.</summary>
    public void ShowUpdateAvailable(string tag)
    {
        _updateItem.Text = $"Update available ({tag}) — open download page";
        _updateItem.Visible = true;
        _updateSeparator.Visible = true;
        _updateSuffix = $" · {tag} available";
        RefreshTooltip();
    }

    /// <summary>
    /// Persistently flags hotkeys another app holds (the status-line note is
    /// overwritten by the next poll within seconds): a tray menu entry that
    /// opens Settings, plus a tooltip note for the session.
    /// </summary>
    public void ShowHotkeyConflict(string combos)
    {
        _conflictItem.Text = $"Hotkey {combos} in use elsewhere — open Settings";
        _conflictItem.Visible = true;
        _updateSeparator.Visible = true;
        _conflictSuffix = " · hotkey conflict";
        RefreshTooltip();
    }

    public void ClearHotkeyConflict()
    {
        _conflictItem.Visible = false;
        _updateSeparator.Visible = _updateItem.Visible;
        _conflictSuffix = "";
        RefreshTooltip();
    }

    private void RefreshTooltip()
    {
        var text = _status + _conflictSuffix + _updateSuffix;
        _icon.Text = text.Length <= 127 ? text : text[..127];
    }

    private static void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true });
        }
        catch
        {
            // Fail soft — worst case the user browses to the repo manually.
        }
    }

    private static Icon LoadAppIcon()
    {
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
                  ?? throw new InvalidOperationException("Assets/app.ico resource missing");
        using var stream = res.Stream;
        return new Icon(stream);
    }

    public void Dispose() => _icon.Dispose();
}
