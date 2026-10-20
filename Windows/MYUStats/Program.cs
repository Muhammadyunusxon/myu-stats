using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MYUStats;

public static class Program
{
    /// <summary>This copy's claim on being the only one running; the app listens on it for later launches.</summary>
    internal static SingleInstance Instance { get; private set; } = null!;

    [STAThread]
    private static int Main()
    {
        using var instance = SingleInstance.Claim();
        if (!instance.IsFirst)
        {
            // There is no Dock or tray icon: starting the app again opens Settings in the running copy.
            instance.SignalFirst();
            return 0;
        }
        Instance = instance;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
