using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MYUStats.Edge;
using MYUStats.Sampling;
using MYUStats.Settings;

namespace MYUStats;

/// <summary>Starts everything and ties it together: settings, sampling, Claude Code's limits, the pill and Settings.</summary>
internal sealed class AppController(SingleInstance instance)
{
    private SettingsService? settingsService;
    private SamplingEngine? engine;
    private StatsStore? store;
    private StorageAnalyzer? storage;
    private EdgeController? edge;
    private AlertMonitor? alerts;
    private Notifier? notifier;
    private SettingsWindow? settingsWindow;

    public void Start()
    {
        Log.Start(AppPaths.Logs);
        var version = typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "dev";
        Log.App.Info($"MYU STATS {version} starting on Windows {Environment.OSVersion.Version}");

        var queue = DispatcherQueue.GetForCurrentThread();
        settingsService = new SettingsService(queue);
        var settings = settingsService.Settings;
        L.Use(settings.Language);

        var claude = new ClaudeUsageStore(new ClaudeUsageReader(), settings, new UiTimer(queue));
        engine = new SamplingEngine();
        store = new StatsStore(engine, new UiTimer(queue), claude);
        store.Start(settings.SampleInterval);
        storage = StorageAnalyzer.ForCurrentUser();

        edge = new EdgeController(store, settings,
            () => OpenSettings(null),
            metric => OpenSettings(SettingsSection.Detail(metric)));
        edge.Show();
        claude.Start();

        notifier = new Notifier(queue);
        notifier.Opened += metric => OpenSettings(metric is { } shown ? SettingsSection.Detail(shown) : SettingsSection.Alerts);
        alerts = new AlertMonitor(store, settings, new UiTimer(queue), notifier);
        alerts.Start();

        settings.Changed += OnSettingsChanged;
        store.Changed += () => settingsWindow?.Refresh();
        claude.Changed += () => settingsWindow?.Refresh();
        // Starting the app again, from the Start menu or Explorer, opens Settings here.
        instance.Listen(queue, () => OpenSettings(SettingsSection.Metrics));
    }

    private void OnSettingsChanged(string name)
    {
        if (store is null || settingsService is null) return;
        var settings = settingsService.Settings;
        if (name is nameof(AppSettings.SampleInterval) or "*" && Math.Abs(store.Interval - settings.SampleInterval) > 0.001)
        {
            store.Start(settings.SampleInterval);
        }
        if (name is nameof(AppSettings.Language) or "*")
        {
            var before = L.Current;
            L.Use(settings.Language);
            if (L.Current == before) return;
            edge?.Rebuild();
            DispatcherQueue.GetForCurrentThread().TryEnqueue(ReopenSettings);
        }
    }

    private void OpenSettings(SettingsSection? section)
    {
        if (store is null || storage is null || settingsService is null || alerts is null || notifier is null) return;
        if (settingsWindow is null)
        {
            var window = new SettingsWindow(new SettingsContext(store, settingsService.Settings, storage, alerts, notifier, Quit));
            window.Closed += () =>
            {
                if (settingsWindow == window) settingsWindow = null;
            };
            settingsWindow = window;
        }
        settingsWindow.Show(section);
    }

    /// <summary>A new language: the window is built again in it, on the same page.</summary>
    private void ReopenSettings()
    {
        if (settingsWindow is not { } window) return;
        var section = window.Selection;
        settingsWindow = null;
        window.Close();
        OpenSettings(section);
    }

    private void Quit()
    {
        Log.App.Info("Quitting");
        store?.Stop();
        store?.Claude.Stop();
        alerts?.Stop();
        storage?.Cancel();
        edge?.Dispose();
        engine?.Dispose();
        settingsService?.Flush();
        settingsWindow?.Close();
        Application.Current.Exit();
    }
}
