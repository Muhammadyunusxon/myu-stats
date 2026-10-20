namespace MYUStats;

/// <summary>
/// Samples every metric on a fixed interval and publishes the latest values. Readings are taken by an
/// <see cref="IStatsSource"/> on a pool thread; only the results land here, on the UI thread.
/// </summary>
public sealed class StatsStore
{
    public const int HistoryLength = 40;
    public const int TopProcessCount = 10;

    private readonly IStatsSource source;
    private readonly IUiTimer timer;
    private readonly HashSet<string> processDemand = [];
    private int tick;
    private bool isSampling;

    public StatsStore(IStatsSource source, IUiTimer timer, ClaudeUsageStore claude)
    {
        this.source = source;
        this.timer = timer;
        Claude = claude;
    }

    /// <summary>Claude Code's usage limits.</summary>
    public ClaudeUsageStore Claude { get; }

    public CpuInfo CpuInfo => source.CpuInfo;
    public CpuUsage Cpu { get; private set; }
    public IReadOnlyList<double> CpuHistory { get; private set; } = [];
    public IReadOnlyList<double> PerCore { get; private set; } = [];
    public GpuState? Gpu { get; private set; }
    public IReadOnlyList<double> GpuHistory { get; private set; } = [];
    public SystemCounts Counts { get; private set; }
    public MemoryUsage Memory { get; private set; }
    public IReadOnlyList<double> MemoryHistory { get; private set; } = [];
    public NetworkRate Network { get; private set; }
    public IReadOnlyList<double> NetworkHistory { get; private set; } = [];
    public NetworkTotals NetworkTotals { get; private set; }
    public NetworkInterfaceInfo? NetworkInterface { get; private set; }
    public DiskUsage? Disk { get; private set; }
    public BatteryState? Battery { get; private set; }
    public ThermalState? Thermals { get; private set; }
    public IReadOnlyList<ProcessUsage> TopByCpu { get; private set; } = [];
    public IReadOnlyList<ProcessUsage> TopByMemory { get; private set; } = [];
    public double Interval { get; private set; } = 1;

    /// <summary>Whether the first reading has landed: until then the battery and sensors are unknown.</summary>
    public bool HasSampled { get; private set; }

    public double NetworkPeak => NetworkHistory.Count == 0 ? 0 : NetworkHistory.Max();

    /// <summary>How far back the sparklines reach, in seconds.</summary>
    public double HistoryDuration => HistoryLength * Interval;

    /// <summary>Raised on the UI thread after each reading is published.</summary>
    public event Action? Changed;

    /// <summary>Raised when the GPU, the battery or the thermal sensors appear or disappear, so the pill can re-lay itself out.</summary>
    public event Action? SensorsChanged;

    /// <summary>Whether a ring has anything to show on this PC: no battery ring on a desktop, no Claude ring without Claude Code.</summary>
    public bool IsAvailable(StatMetric metric) => metric switch
    {
        StatMetric.Gpu => Gpu?.Headline is not null,
        StatMetric.Battery => Battery is not null,
        StatMetric.Thermals => Thermals is not null,
        StatMetric.Claude => Claude.IsInstalled,
        _ => true,
    };

    /// <summary>
    /// Process scanning is the costliest sampler, so it only runs while something shows processes.
    /// Each caller (the hover card, the Settings detail page) holds its own claim.
    /// </summary>
    public void SetProcessSampling(bool enabled, string client)
    {
        var wasEmpty = processDemand.Count == 0;
        if (enabled) processDemand.Add(client);
        else processDemand.Remove(client);
        if (wasEmpty && processDemand.Count > 0) _ = RefreshProcessesAsync();
    }

    public void Start(double interval)
    {
        Interval = interval;
        timer.Start(TimeSpan.FromSeconds(interval), Tick);
        Tick();
    }

    public void Stop() => timer.Stop();

    private async void Tick()
    {
        // A slow tick (the first thermal probe, a busy disk) must not pile up behind itself.
        if (isSampling) return;
        isSampling = true;
        try
        {
            await SampleNowAsync();
        }
        catch (Exception error)
        {
            Log.Sampling.Error($"Sampling failed: {error}");
        }
        finally
        {
            isSampling = false;
        }
    }

    /// <summary>Takes one reading and publishes it. The timer drives this; tests call it directly.</summary>
    public async Task SampleNowAsync()
    {
        // Disk, battery and addresses change slowly; sensors move over seconds.
        var request = new SampleRequest(
            IncludeSlow: tick % 5 == 0,
            IncludeThermals: tick % 2 == 0,
            IncludeProcesses: processDemand.Count > 0,
            // Kept off the thermal ticks, so the two sets of counters are never read together.
            IncludeGpu: tick % 2 == 1);
        tick++;
        var sample = await Task.Run(() => source.Sample(request));
        Apply(sample, request);
    }

    /// <summary>For the alerts: every process, with its CPU averaged since the previous call.</summary>
    public Task<IReadOnlyList<ProcessUsage>> WatchProcessesAsync() => Task.Run(source.WatchProcesses);

    private (bool, bool, bool) Sensors() => (IsAvailable(StatMetric.Gpu), Battery is not null, Thermals is not null);

    private void Apply(StatsSample sample, SampleRequest request)
    {
        var hadSensors = Sensors();

        if (sample.Cpu is { } cpu)
        {
            Cpu = cpu;
            CpuHistory = Appending(cpu.Total, CpuHistory);
        }
        PerCore = sample.PerCore;
        if (sample.Counts is { } counts) Counts = counts;
        if (sample.Memory is { } memory)
        {
            Memory = memory;
            MemoryHistory = Appending(memory.Fraction, MemoryHistory);
        }
        Network = sample.Network;
        NetworkHistory = Appending(sample.Network.Total, NetworkHistory);
        NetworkTotals = sample.NetworkTotals;

        if (request.IncludeSlow)
        {
            Disk = sample.Disk;
            Battery = sample.Battery;
            NetworkInterface = sample.NetworkInterface;
        }
        if (request.IncludeThermals)
        {
            Thermals = sample.Thermals;
        }
        if (request.IncludeGpu)
        {
            Gpu = sample.Gpu;
        }
        // One point per tick, whichever tick read the counters, so the chart keeps the others' time scale.
        if (Gpu?.Headline is { } busiest) GpuHistory = Appending(busiest.Utilization, GpuHistory);
        // Demand may have ended while the reading was in flight.
        if (sample.Processes is { } processes && processDemand.Count > 0)
        {
            PublishTop(processes);
        }

        HasSampled = true;
        Changed?.Invoke();
        if (Sensors() != hadSensors) SensorsChanged?.Invoke();
    }

    private async Task RefreshProcessesAsync()
    {
        try
        {
            var processes = await Task.Run(() => source.Processes(reset: true));
            if (processDemand.Count == 0) return;
            PublishTop(processes);
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            Log.Sampling.Error($"Process list failed: {error}");
        }
    }

    private void PublishTop(IReadOnlyList<ProcessUsage> processes)
    {
        TopByCpu = [.. processes.OrderByDescending(process => process.Cpu).Take(TopProcessCount)];
        TopByMemory = [.. processes.OrderByDescending(process => process.Memory).Take(TopProcessCount)];
    }

    public static IReadOnlyList<double> Appending(double value, IReadOnlyList<double> history)
    {
        var next = new List<double>(history.Count + 1);
        next.AddRange(history.Skip(Math.Max(0, history.Count + 1 - HistoryLength)));
        next.Add(value);
        return next;
    }
}
