namespace MYUStats;

/// <summary>The alerts turned on in Settings › Alerts, with their limits; null or false means off.</summary>
public sealed record AlertSettings
{
    /// <summary>Share of a Claude Code limit.</summary>
    public double? ClaudeThreshold { get; init; }
    /// <summary>Free space on the Windows drive, in bytes as Explorer counts them (1 GB = 2³⁰ bytes).</summary>
    public long? DiskFreeBytes { get; init; }
    public double? TemperatureLimit { get; init; }
    public double? ProcessMinutes { get; init; }
    public bool Memory { get; init; }

    public bool IsAnyOn => ClaudeThreshold is not null || DiskFreeBytes is not null || TemperatureLimit is not null
        || ProcessMinutes is not null || Memory;

    public static AlertSettings From(AppSettings settings) => new()
    {
        ClaudeThreshold = settings.AlertClaude ? settings.AlertClaudeThreshold : null,
        DiskFreeBytes = settings.AlertDisk ? (long)(settings.AlertDiskFreeGB * (1L << 30)) : null,
        TemperatureLimit = settings.AlertTemperature ? settings.AlertTemperatureLimit : null,
        ProcessMinutes = settings.AlertProcess ? settings.AlertProcessMinutes : null,
        Memory = settings.AlertMemory,
    };
}

/// <summary>What the alerts look at in one check.</summary>
public sealed record AlertSnapshot
{
    public ClaudeUsage? Claude { get; init; }
    public DiskUsage? Disk { get; init; }
    public double? Temperature { get; init; }
    public MemoryPressure? MemoryPressure { get; init; }
    /// <summary>Every process with its CPU averaged over <see cref="ProcessWindow"/>; null when the
    /// processes were not read in this check.</summary>
    public IReadOnlyList<ProcessUsage>? Processes { get; init; }
    public TimeSpan ProcessWindow { get; init; }
}

public enum AlertKind
{
    Claude,
    Disk,
    Temperature,
    Process,
    Memory,
    Test,
}

/// <summary>One notification. A later one with the same <see cref="Id"/> takes its place; a click opens
/// <see cref="Metric"/>'s page, or the Alerts page when there is none.</summary>
public sealed record Alert(AlertKind Kind, string Id, string Title, string Body, StatMetric? Metric);

/// <summary>
/// Decides when to speak up, as the macOS app does. Each alert fires once when its condition starts and
/// stays quiet until the condition has cleared; heat and memory pressure must last a minute first, and
/// the same alert is not repeated within half an hour, so a value hovering at a limit does not nag.
/// </summary>
public sealed class AlertEngine(int processors)
{
    /// <summary>How long heat and critical memory pressure must last: a short spike is normal.</summary>
    public static TimeSpan Sustain { get; } = TimeSpan.FromMinutes(1);
    public static TimeSpan Cooldown { get; } = TimeSpan.FromMinutes(30);
    /// <summary>Checks further apart than this mean the PC slept, so anything that must last starts over.</summary>
    public static TimeSpan MaxGap { get; } = TimeSpan.FromMinutes(2);
    /// <summary>A process counts as busy while it keeps at least this share of one logical processor.</summary>
    public const double BusyShare = 0.9;
    /// <summary>Heat must drop this far below the limit before the alert can fire again.</summary>
    public const double TemperatureMargin = 5;

    private readonly HashSet<string> firedClaude = [];
    private readonly HashSet<int> firedProcesses = [];
    private readonly Dictionary<string, DateTimeOffset> lastSent = [];
    private Dictionary<int, DateTimeOffset> busySince = [];
    private bool diskFired;
    private DateTimeOffset? hotSince;
    private bool hotFired;
    private DateTimeOffset? criticalSince;
    private bool memoryFired;
    private DateTimeOffset? lastCheck;

    public AlertEngine() : this(Environment.ProcessorCount)
    {
    }

    public IReadOnlyList<Alert> Evaluate(AlertSnapshot snapshot, AlertSettings settings, DateTimeOffset now)
    {
        if (lastCheck is { } last && now - last > MaxGap)
        {
            hotSince = null;
            criticalSince = null;
            busySince.Clear();
        }
        lastCheck = now;
        return
        [
            .. Claude(snapshot.Claude, settings.ClaudeThreshold, now),
            .. Disk(snapshot.Disk, settings.DiskFreeBytes, now),
            .. Temperature(snapshot.Temperature, settings.TemperatureLimit, now),
            .. Memory(snapshot.MemoryPressure, settings.Memory, now),
            .. Processes(snapshot.Processes, snapshot.ProcessWindow, settings.ProcessMinutes, now),
        ];
    }

    /// <summary>Once per limit until it drops back under the threshold, which happens when it resets.</summary>
    private List<Alert> Claude(ClaudeUsage? usage, double? threshold, DateTimeOffset now)
    {
        var alerts = new List<Alert>();
        if (threshold is not { } limit)
        {
            firedClaude.Clear();
            return alerts;
        }
        // Stale figures were already looked at when they were fresh.
        if (usage is not { Status: ClaudeUsageStatus.Ok }) return alerts;
        foreach (var window in usage.Windows)
        {
            if (window.UsedFraction < limit)
            {
                firedClaude.Remove(window.Id);
                continue;
            }
            if (!firedClaude.Add(window.Id)) continue;
            var body = window.ResetsAt is { } resets
                ? L.F("{0} · resets in {1}", window.Label, Format.TimeUntil(resets, now))
                : window.Label;
            alerts.Add(new Alert(AlertKind.Claude, "claude." + window.Id,
                L.F("Claude Code limit at {0}", Format.Percent(window.UsedFraction)), body, StatMetric.Claude));
        }
        return alerts;
    }

    /// <summary>Once when free space falls under the limit; again only after a quarter more has been freed.</summary>
    private List<Alert> Disk(DiskUsage? disk, long? limit, DateTimeOffset now)
    {
        if (limit is not { } bytes)
        {
            diskFired = false;
            return [];
        }
        if (disk is null) return [];
        if (disk.Free >= bytes + bytes / 4) diskFired = false;
        if (disk.Free >= bytes || diskFired || !Cooled("disk", now)) return [];
        diskFired = true;
        lastSent["disk"] = now;
        return
        [
            new Alert(AlertKind.Disk, "disk", L.T("Disk almost full"),
                L.F("{0} free on {1}. Disk details show what takes the space and which caches can go.", Format.Bytes(disk.Free), disk.Name),
                StatMetric.Disk),
        ];
    }

    private List<Alert> Temperature(double? celsius, double? limit, DateTimeOffset now)
    {
        if (limit is not { } ceiling)
        {
            hotSince = null;
            hotFired = false;
            return [];
        }
        if (celsius is not { } value)
        {
            hotSince = null;
            return [];
        }
        if (value < ceiling - TemperatureMargin) hotFired = false;
        if (value < ceiling)
        {
            hotSince = null;
            return [];
        }
        hotSince ??= now;
        if (now - hotSince < Sustain || hotFired || !Cooled("temperature", now)) return [];
        hotFired = true;
        lastSent["temperature"] = now;
        return
        [
            new Alert(AlertKind.Temperature, "temperature", L.F("Running hot: {0}", Format.Temperature(value)),
                L.F("Above {0} for over a minute.", Format.Temperature(ceiling)), StatMetric.Thermals),
        ];
    }

    private List<Alert> Memory(MemoryPressure? pressure, bool isOn, DateTimeOffset now)
    {
        if (!isOn)
        {
            criticalSince = null;
            memoryFired = false;
            return [];
        }
        if (pressure is not { } level) return [];
        if (level == MemoryPressure.Normal) memoryFired = false;
        if (level != MemoryPressure.Critical)
        {
            criticalSince = null;
            return [];
        }
        criticalSince ??= now;
        if (now - criticalSince < Sustain || memoryFired || !Cooled("memory", now)) return [];
        memoryFired = true;
        lastSent["memory"] = now;
        return
        [
            new Alert(AlertKind.Memory, "memory", L.T("Memory is running out"),
                L.T("Close apps you are not using, or Windows will keep slowing down."), StatMetric.Memory),
        ];
    }

    /// <summary>A process busy through every reading for the chosen minutes. The first busy reading counts
    /// from the start of its window, since the average says it was busy all along.</summary>
    private List<Alert> Processes(IReadOnlyList<ProcessUsage>? list, TimeSpan window, double? minutes, DateTimeOffset now)
    {
        var alerts = new List<Alert>();
        if (minutes is not { } span)
        {
            busySince.Clear();
            firedProcesses.Clear();
            return alerts;
        }
        if (list is null) return alerts;
        var stillBusy = new Dictionary<int, DateTimeOffset>();
        foreach (var process in list)
        {
            // Task Manager counts CPU as a share of every processor; one busy core is 1/processors of it.
            if (process.Cpu * processors < BusyShare) continue;
            var since = busySince.TryGetValue(process.Pid, out var start) ? start : now - window;
            stillBusy[process.Pid] = since;
            // A few seconds' slack for timer drift.
            if ((now - since).TotalSeconds < span * 60 - 5 || firedProcesses.Contains(process.Pid)) continue;
            var key = "process." + process.Name;
            if (!Cooled(key, now)) continue;
            firedProcesses.Add(process.Pid);
            lastSent[key] = now;
            alerts.Add(new Alert(AlertKind.Process, "process." + process.Pid,
                L.F("{0} is keeping a core busy", process.Name),
                L.F("{0} CPU for over {1} minutes.", Format.Percent(process.Cpu), (int)span), StatMetric.Cpu));
        }
        busySince = stillBusy;
        firedProcesses.IntersectWith(stillBusy.Keys);
        return alerts;
    }

    private bool Cooled(string key, DateTimeOffset now) => !lastSent.TryGetValue(key, out var sent) || now - sent >= Cooldown;
}
