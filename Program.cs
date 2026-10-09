using Velopack;

namespace PandoraOverlay;

/// <summary>
/// The entry point (v1.30). Velopack's start-up call comes before anything
/// else: it answers the installer's and the updater's own calls into this exe
/// (install, update, uninstall hooks) and exits at once for those, so the
/// overlay never starts polling or shows a window during an update. Then WPF
/// starts exactly as the generated App.Main would have started it.
/// The call must be the first line of a real Main() (vpk pack warns
/// otherwise), which is why the entry point is this class and not App's.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
