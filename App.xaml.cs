using System.Windows;

namespace PandoraOverlay;

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
