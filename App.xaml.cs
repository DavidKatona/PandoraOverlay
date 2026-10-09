using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// Started by Program.Main (Velopack's hook runs first), not by the generated
/// App.Main; StartupUri in App.xaml still opens MainWindow, after OnStartup.
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance guard: a second copy would silently double the
        // poll rate (hard constraint #3), so it announces itself and exits.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\PandoraOverlay.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Pandora Overlay is already running — look for its icon in the system tray.",
                "Pandora Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        // v1.30: settings live in %AppData%\PandoraOverlay now. The first launch
        // after the move copies an older copy's files over (MainWindow loads
        // them next) — a copy, so the old folder keeps working as it was.
        DataFolder.MigrateLegacy();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { /* not owned — fine */ }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
