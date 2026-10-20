namespace MYUStats;

public readonly record struct CpuUsage(double User, double System)
{
    public double Total => User + System;
    public double Idle => Math.Max(0, 1 - Total);
}

/// <summary>Static facts about the processor, read once at launch.</summary>
public sealed record CpuInfo(string Brand, int PerformanceCores, int EfficiencyCores, int PhysicalCores, int LogicalCores)
{
    public static CpuInfo Unknown { get; } = new("", 0, 0, 0, Environment.ProcessorCount);

    public string DisplayBrand => Brand.Length > 0 ? Brand : L.T("Unknown CPU");

    /// <summary>"8P + 16E" on a hybrid chip, otherwise the core count.</summary>
    public string CoresDescription => PerformanceCores > 0 && EfficiencyCores > 0
        ? L.F("{0}P + {1}E", PerformanceCores, EfficiencyCores)
        : L.F("{0} cores", PhysicalCores > 0 ? PhysicalCores : LogicalCores);
}

/// <summary>What Windows counts in the Performance tab: processes, threads and handles.</summary>
public readonly record struct SystemCounts(int Processes, int Threads, int Handles);

public enum MemoryPressure
{
    Normal,
    Warning,
    Critical,
}

public static class MemoryPressures
{
    public static string Title(this MemoryPressure pressure) => pressure switch
    {
        MemoryPressure.Normal => L.T("Normal"),
        MemoryPressure.Warning => L.T("Warning"),
        MemoryPressure.Critical => L.T("Critical"),
        _ => throw new ArgumentOutOfRangeException(nameof(pressure)),
    };

    /// <summary>
    /// Windows has no pressure level like macOS; this reads it from what is left. Critical once the
    /// memory manager signals low memory or under 5 % is available; a warning under 15 % available,
    /// or when commitments near the commit limit (RAM plus page file).
    /// </summary>
    public static MemoryPressure From(ulong total, ulong available, ulong committed, ulong commitLimit, bool lowMemorySignalled)
    {
        var availableShare = total == 0 ? 1 : (double)available / total;
        if (lowMemorySignalled || availableShare < 0.05) return MemoryPressure.Critical;
        var commitShare = commitLimit == 0 ? 0 : (double)committed / commitLimit;
        return availableShare < 0.15 || commitShare > 0.9 ? MemoryPressure.Warning : MemoryPressure.Normal;
    }
}

/// <summary>Physical memory the way Task Manager counts it: in use is everything not available.</summary>
public readonly record struct MemoryUsage(
    ulong Total,
    ulong Available,
    ulong Committed,
    ulong CommitLimit,
    ulong Cached,
    ulong PagedPool,
    ulong NonPagedPool,
    MemoryPressure Pressure)
{
    public ulong Used => Total > Available ? Total - Available : 0;
    public double Fraction => Total == 0 ? 0 : (double)Used / Total;
}

public sealed record DiskUsage(string Name, long Free, long Total)
{
    public long Used => Math.Max(0, Total - Free);
    public double UsedFraction => Total == 0 ? 0 : (double)Used / Total;
}

public readonly record struct NetworkRate(double DownBytesPerSecond, double UpBytesPerSecond)
{
    public double Total => DownBytesPerSecond + UpBytesPerSecond;
}

public readonly record struct NetworkTotals(ulong Received, ulong Sent);

/// <summary>The connection that carries traffic out: its name in Windows and its IPv4 address.</summary>
public sealed record NetworkInterfaceInfo(string Name, string Address);

public sealed record BatteryState(int Percent, bool IsCharging, bool IsOnAC)
{
    /// <summary>Minutes until empty, or until full while charging, when Windows has an estimate.</summary>
    public int? MinutesRemaining { get; init; }
    public int? CycleCount { get; init; }
    /// <summary>Full charge capacity over design capacity.</summary>
    public int? HealthPercent { get; init; }
    public double? TemperatureCelsius { get; init; }
    /// <summary>Watts flowing in (positive, charging) or out (negative), when the battery reports it.</summary>
    public double? RateWatts { get; init; }
    public bool IsSaverOn { get; init; }
}

/// <summary>One process, with CPU as a share of all cores, like Task Manager.</summary>
public sealed record ProcessUsage(int Pid, string Name, double Cpu, ulong Memory);

/// <summary>How busy one kind of engine is ("3D", "Copy", "Video Decode"), 0 to 1.</summary>
public readonly record struct GpuEngineLoad(string Engine, double Load);

/// <summary>A process's share of its busiest GPU engine, the figure Task Manager's GPU column shows.</summary>
public sealed record GpuProcess(int Pid, string Name, double Load);

/// <summary>One graphics adapter, as Task Manager's GPU page shows it.</summary>
public sealed record GpuAdapter(string Id, string Name)
{
    /// <summary>The busiest engine's load, 0 to 1, as Task Manager's GPU graph shows it.</summary>
    public double Utilization { get; init; }

    /// <summary>Load per kind of engine, busiest first.</summary>
    public IReadOnlyList<GpuEngineLoad> Engines { get; init; } = [];

    public ulong? DedicatedUsed { get; init; }
    /// <summary>Video memory of its own; small or none on an integrated GPU, which uses shared memory.</summary>
    public ulong? DedicatedTotal { get; init; }
    public ulong? SharedUsed { get; init; }
    /// <summary>What drivers report for dedicated GPUs (WDDM 2.4 and later); null when they do not.</summary>
    public double? TemperatureCelsius { get; init; }
    public int? FanRpm { get; init; }
}

public sealed record GpuState(IReadOnlyList<GpuAdapter> Adapters, IReadOnlyList<GpuProcess> Processes)
{
    /// <summary>The adapter the ring shows: the busiest.</summary>
    public GpuAdapter? Headline => Adapters.Count == 0 ? null : Adapters.MaxBy(adapter => adapter.Utilization);
}

/// <summary>An ACPI thermal zone, as Windows' "Thermal Zone Information" counters report it.</summary>
public sealed record ThermalZone(string Name, double Celsius);

public sealed record ThermalState(IReadOnlyList<ThermalZone> Zones)
{
    public double? Hottest => Zones.Count == 0 ? null : Zones.Max(zone => zone.Celsius);
    public double? Average => Zones.Count == 0 ? null : Zones.Average(zone => zone.Celsius);

    /// <summary>The ring shows the hottest zone: Windows does not say which zone is the CPU.</summary>
    public double? Headline => Hottest;

    public bool Equals(ThermalState? other) => other is not null && Zones.SequenceEqual(other.Zones);

    public override int GetHashCode() => Zones.Aggregate(0, (hash, zone) => HashCode.Combine(hash, zone));
}

/// <summary>What one sampling tick should read. Slow-moving values are only read every few ticks, and the
/// GPU counters, which cost more than the rest together, every other tick.</summary>
public sealed record SampleRequest(bool IncludeSlow, bool IncludeThermals, bool IncludeProcesses, bool IncludeGpu = true);

/// <summary>Everything one tick read. Values are null when they were not asked for or could not be read.</summary>
public sealed class StatsSample
{
    public CpuUsage? Cpu { get; set; }
    public IReadOnlyList<double> PerCore { get; set; } = [];
    public SystemCounts? Counts { get; set; }
    public MemoryUsage? Memory { get; set; }
    public NetworkRate Network { get; set; }
    public NetworkTotals NetworkTotals { get; set; }
    public DiskUsage? Disk { get; set; }
    public BatteryState? Battery { get; set; }
    public NetworkInterfaceInfo? NetworkInterface { get; set; }
    public ThermalState? Thermals { get; set; }
    public GpuState? Gpu { get; set; }
    public IReadOnlyList<ProcessUsage>? Processes { get; set; }
}

/// <summary>Where <see cref="StatsStore"/> gets its readings; a fake stands in for it in tests.</summary>
public interface IStatsSource
{
    CpuInfo CpuInfo { get; }

    /// <summary>Called off the UI thread, one call at a time.</summary>
    StatsSample Sample(SampleRequest request);

    /// <summary>Current processes; <paramref name="reset"/> drops the previous CPU times, so the next CPU reading starts afresh.</summary>
    IReadOnlyList<ProcessUsage> Processes(bool reset);

    /// <summary>Processes with their CPU averaged since the previous call, for the alerts; kept apart from
    /// <see cref="Processes"/>, whose readings are a second apart.</summary>
    IReadOnlyList<ProcessUsage> WatchProcesses();
}

/// <summary>A repeating timer on the UI thread; the app backs it with a DispatcherQueueTimer.</summary>
public interface IUiTimer
{
    void Start(TimeSpan interval, Action tick);
    void Stop();
}
