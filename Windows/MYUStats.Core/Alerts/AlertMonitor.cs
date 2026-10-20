namespace MYUStats;

/// <summary>Shows an alert; the app backs it with a Windows notification.</summary>
public interface IAlertPoster
{
    void Post(Alert alert);
}

/// <summary>
/// Checks the store every few seconds against the alerts turned on in Settings, and posts what
/// <see cref="AlertEngine"/> decides is worth saying. Costs nothing while every alert is off.
/// </summary>
public sealed class AlertMonitor
{
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromSeconds(10);
    /// <summary>Processes are read every third check: a scan costs more, and a longer window smooths spikes.</summary>
    public const int ProcessCheckEvery = 3;

    private readonly StatsStore store;
    private readonly AppSettings settings;
    private readonly IUiTimer timer;
    private readonly IAlertPoster poster;
    private readonly Func<DateTimeOffset> clock;
    private AlertEngine engine = new();
    private int checks;
    private DateTimeOffset? lastWatch;
    private bool isChecking;

    public AlertMonitor(StatsStore store, AppSettings settings, IUiTimer timer, IAlertPoster poster, Func<DateTimeOffset>? clock = null)
    {
        this.store = store;
        this.settings = settings;
        this.timer = timer;
        this.poster = poster;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public void Start() => timer.Start(CheckInterval, () => _ = CheckAsync());

    public void Stop() => timer.Stop();

    public void SendTest() => poster.Post(new Alert(AlertKind.Test, "test", L.T("Alerts are on"),
        L.T("This is how MYU STATS lets you know."), null));

    /// <summary>One check; the timer drives this, tests call it directly.</summary>
    public async Task CheckAsync()
    {
        var wanted = AlertSettings.From(settings);
        if (!wanted.IsAnyOn)
        {
            // Everything starts afresh when an alert is turned on again.
            engine = new AlertEngine();
            lastWatch = null;
            return;
        }
        if (isChecking) return;
        isChecking = true;
        try
        {
            var readsProcesses = wanted.ProcessMinutes is not null && checks % ProcessCheckEvery == 0;
            checks++;
            if (wanted.ProcessMinutes is null) lastWatch = null;
            var snapshot = new AlertSnapshot
            {
                Claude = store.Claude.Usage,
                Disk = store.Disk,
                Temperature = store.Thermals?.Headline,
                MemoryPressure = store.Memory.Total > 0 ? store.Memory.Pressure : null,
            };
            if (readsProcesses)
            {
                var processes = await store.WatchProcessesAsync();
                var now = clock();
                // After a pause the averages span the pause, so that reading only starts a new window.
                if (lastWatch is { } last && now - last < 2 * CheckInterval * ProcessCheckEvery)
                {
                    snapshot = snapshot with { Processes = processes, ProcessWindow = now - last };
                }
                lastWatch = now;
            }
            foreach (var alert in engine.Evaluate(snapshot, wanted, clock()))
            {
                Log.App.Notice($"Alert {alert.Id}");
                poster.Post(alert);
            }
        }
        catch (Exception error)
        {
            Log.App.Error($"Alert check failed: {error}");
        }
        finally
        {
            isChecking = false;
        }
    }
}
