using System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace PandoraOverlay;

/// <summary>
/// Notification-area icon — the overlay's only always-visible affordance (the
/// windows themselves are click-through, absent from the taskbar and Alt-Tab).
/// The right-click menu drives the same actions as the edit banners; a
/// double-click toggles edit mode. WinForms interop, since NotifyIcon has no
/// WPF counterpart. Must be disposed on shutdown or the icon lingers in the
/// tray until hovered.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action toggleEditMode, Action toggleMinimap, Action openSettings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Edit mode", null, (_, _) => toggleEditMode())
        {
            ShortcutKeyDisplayString = "Ctrl+F8"
        });
        menu.Items.Add(new ToolStripMenuItem("Show/hide minimap", null, (_, _) => toggleMinimap()));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => exit()));

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Pandora Overlay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => toggleEditMode();
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
