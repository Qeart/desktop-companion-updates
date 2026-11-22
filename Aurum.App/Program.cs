using Velopack;

namespace Aurum;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Installer hooks must finish before WPF, capture, elevation or the app mutex.
        // Updates are applied only through the user's explicit restart action.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
