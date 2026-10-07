using System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace PandoraOverlay;

/// <summary>
/// Notification-area icon — the overlay's only always-visible affordance (the
/// windows themselves are click-through, absent from the taskbar and Alt-Tab).
/// The right-click menu is deliberately small: the lifelines (edit mode,
/// hide/show overlay, settings, exit) and alerts — layout controls belong to
/// the control panel, and mid-game actions get a hotkey (Check Prime moved
/// to one in v1.19; the slot for hotkey-less actions holds "Server rules…",
/// which opens Settings on the Rules page). A double-click
/// toggles edit mode. The hover tooltip carries live stats and,
/// when a newer release exists, an update note plus a menu entry that installs
/// it for a click (v1.30) — or opens the download page where this copy can't
/// update itself. WinForms interop, since NotifyIcon has no WPF counterpart.
/// Must be disposed on shutdown or the icon lingers in the tray until hovered.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _editItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _conflictItem;
    private readonly ToolStripMenuItem _signInItem;
    private readonly ToolStripSeparator _updateSeparator;
    private string _status = "Pandora Overlay";
    private string _updateSuffix = "";
    private string _conflictSuffix = "";
    private string _signInSuffix = "";
    private Action? _updateClick;
    private Action? _signInClick;

    public TrayIcon(Action toggleEditMode, Action toggleOverlay, Action openSettings, Action openRules, Action exit)
    {
        _updateItem = new ToolStripMenuItem { Visible = false };
        _updateItem.Click += (_, _) => _updateClick?.Invoke();
        _conflictItem = new ToolStripMenuItem { Visible = false };
        _conflictItem.Click += (_, _) => openSettings();
        // The third alert (v1.31): the website session ended — one click opens the sign-in window.
        _signInItem = new ToolStripMenuItem("Sign in again…") { Visible = false };
        _signInItem.Click += (_, _) => _signInClick?.Invoke();
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
        menu.Items.Add(_signInItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(_conflictItem);
        menu.Items.Add(_updateSeparator);
        // Lifelines (must work while locked, hidden, or with dead hotkeys) ·
        // the app itself. Per-widget show/hide deliberately lives on the
        // control panel only, so this menu never grows with the number of
        // widgets, and mid-game actions earn a hotkey instead of a line here.
        // "Server rules…" is the one hotkey-less mid-game action: it opens
        // the Settings dialog straight on the Rules page.
        menu.Items.Add(_editItem);
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(new ToolStripMenuItem("Server rules…", null, (_, _) => openRules()));
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

    /// <summary>Reveals the update menu entry — its text and what a click does are the caller's (install, or open the download page) — and appends the tag to the tooltip for the session.</summary>
    public void ShowUpdateAvailable(string tag, string label, Action onClick)
    {
        _updateItem.Text = label;
        _updateItem.Enabled = true;
        _updateClick = onClick;
        _updateItem.Visible = true;
        _updateSeparator.Visible = true;
        _updateSuffix = $" · {tag} available";
        RefreshTooltip();
    }

    /// <summary>While an update downloads: the entry says so and takes no clicks.</summary>
    public void SetUpdateBusy(string label)
    {
        _updateItem.Text = label;
        _updateItem.Enabled = false;
        _updateClick = null;
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
        _conflictSuffix = "";
        RefreshSeparator();
        RefreshTooltip();
    }

    /// <summary>
    /// The website session ended (v1.31): a persistent entry that opens the
    /// sign-in window — the status line alone would be overwritten, and the
    /// tray is where the fix must live while every widget is locked.
    /// </summary>
    public void ShowSignedOut(Action onClick)
    {
        _signInClick = onClick;
        _signInItem.Visible = true;
        _signInSuffix = " · signed out";
        RefreshSeparator();
        RefreshTooltip();
    }

    public void ClearSignedOut()
    {
        _signInItem.Visible = false;
        _signInSuffix = "";
        RefreshSeparator();
        RefreshTooltip();
    }

    private void RefreshSeparator() =>
        _updateSeparator.Visible = _updateItem.Visible || _conflictItem.Visible || _signInItem.Visible;

    private void RefreshTooltip()
    {
        var text = _status + _signInSuffix + _conflictSuffix + _updateSuffix;
        _icon.Text = text.Length <= 127 ? text : text[..127];
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
