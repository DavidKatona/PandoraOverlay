using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// Started by Program.Main (Velopack's hook runs first), not by the generated
/// App.Main; StartupUri in App.xaml still opens MainWindow, after OnStartup.
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private Exception? _crash; // the one Fatal line per crash: the UI thread's handler writes it, the domain's would repeat it

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

        // The log (1.36) opens only now, past the guard: a second copy must not
        // turn the running copy's file into a backup. MainWindow sets the
        // levels from config.json once it has loaded it.
        AppLog.Start();
        AppLog.App.Log($"Pandora Overlay {Updater.ShortVersion(Updater.InformationalVersion)}, Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");
        ListenForCrashes();

        // v1.30: settings live in %AppData%\PandoraOverlay now. The first launch
        // after the move copies an older copy's files over (MainWindow loads
        // them next) — a copy, so the old folder keeps working as it was.
        DataFolder.MigrateLegacy();
        if (DataFolder.Migration is { } moved) AppLog.Config.Log($"Settings brought over from an older copy: {moved.Message}");
        base.OnStartup(e);
    }

    /// <summary>
    /// A crash leaves a Fatal line with its stack trace before the process
    /// goes. The overlay still crashes as it did before the log existed:
    /// nothing here marks an exception handled. A task's exception that nobody
    /// awaited doesn't end the process, so it is an Error.
    /// </summary>
    private void ListenForCrashes()
    {
        DispatcherUnhandledException += (_, args) => LogCrash("Unhandled exception on the UI thread", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogCrash("Unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
            AppLog.App.Exception(LogVerbosity.Error, "A background task failed and nobody saw it", args.Exception);
    }

    private void LogCrash(string what, Exception ex)
    {
        if (ReferenceEquals(ex, _crash)) return;
        _crash = ex;
        AppLog.App.Exception(LogVerbosity.Fatal, what, ex);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            AppLog.App.Log("Exit");
            AppLog.Stop();
            try { _instanceMutex.ReleaseMutex(); } catch { /* not owned — fine */ }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
