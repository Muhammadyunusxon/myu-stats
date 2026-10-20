using System.Runtime.Versioning;

namespace MYUStats.Sampling;

/// <summary>
/// Runs every sampler. Samplers keep state between ticks (previous times and byte counts), and the
/// lock keeps them to one caller at a time. A sampler that throws loses its own value for that tick,
/// not the whole reading, and is logged once.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SamplingEngine : IStatsSource, IDisposable
{
    private readonly Lock gate = new();
    private readonly CpuSampler cpu = new();
    private readonly PerCoreSampler perCore = new();
    private readonly MemorySampler memory = new();
    private readonly NetworkSampler network = new();
    private readonly ProcessSampler processes = new();
    /// <summary>The alerts' own process readings, minutes apart rather than a second.</summary>
    private readonly ProcessSampler watch = new();
    private readonly HashSet<string> reported = [];
    /// <summary>Opened on the first thermal read.</summary>
    private ThermalSampler? thermal;
    private bool didProbeThermals;
    /// <summary>Opened on the first GPU read.</summary>
    private GpuSampler? gpu;
    private bool didProbeGpu;

    public CpuInfo CpuInfo { get; } = CpuInfoReader.Read();

    public StatsSample Sample(SampleRequest request)
    {
        lock (gate)
        {
            var sample = new StatsSample
            {
                Cpu = Try("cpu", cpu.Sample, null),
                PerCore = Try("per-core", perCore.Sample, []),
            };
            var (usage, counts) = Try("memory", memory.Sample, (null, null));
            sample.Memory = usage;
            sample.Counts = counts;
            sample.Network = Try("network", network.Sample, default);
            sample.NetworkTotals = network.Totals;

            if (request.IncludeSlow)
            {
                sample.Disk = Try("disk", DiskSampler.Sample, null);
                sample.Battery = Try("battery", BatterySampler.Sample, null);
                sample.NetworkInterface = Try("interface", NetworkSampler.PrimaryInterface, null);
            }
            if (request.IncludeThermals)
            {
                sample.Thermals = Try("thermals", () => Thermals()?.Sample(), null);
            }
            if (request.IncludeGpu)
            {
                sample.Gpu = Try("gpu", () => Gpu()?.Sample(), null);
            }
            if (request.IncludeProcesses)
            {
                sample.Processes = Try("processes", processes.Sample, []);
            }
            return sample;
        }
    }

    public IReadOnlyList<ProcessUsage> Processes(bool reset)
    {
        lock (gate)
        {
            if (reset) processes.Reset();
            return Try("processes", processes.Sample, []);
        }
    }

    public IReadOnlyList<ProcessUsage> WatchProcesses()
    {
        lock (gate)
        {
            return Try("watch", watch.Sample, []);
        }
    }

    private GpuSampler? Gpu()
    {
        if (didProbeGpu) return gpu;
        didProbeGpu = true;
        gpu = GpuSampler.Open();
        if (gpu is null) Log.Sampling.Notice("No GPU counters found; the GPU ring stays hidden");
        return gpu;
    }

    private ThermalSampler? Thermals()
    {
        if (didProbeThermals) return thermal;
        didProbeThermals = true;
        thermal = ThermalSampler.Open();
        if (thermal is null) Log.Sampling.Notice("No thermal zones found; the Thermals ring stays hidden");
        return thermal;
    }

    private T Try<T>(string name, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception error)
        {
            if (reported.Add(name)) Log.Sampling.Error($"The {name} sampler failed: {error}");
            return fallback;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            thermal?.Dispose();
            thermal = null;
            gpu?.Dispose();
            gpu = null;
        }
    }
}
