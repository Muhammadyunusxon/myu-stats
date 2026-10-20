using Microsoft.UI.Xaml;

namespace MYUStats;

public partial class App : Application
{
    private AppController? controller;

    public App()
    {
        InitializeComponent();
        // The app has no main window: Settings can close and the pill can hide while it keeps running.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // A monitor that stays up with one glitch beats one that vanishes: log the error and carry on.
        UnhandledException += (_, args) =>
        {
            Log.App.Error($"Unhandled exception: {args.Exception}");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.App.Error($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        controller = new AppController(Program.Instance);
        controller.Start();
    }
}
