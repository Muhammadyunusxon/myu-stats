using System.Globalization;

namespace MYUStats.Tests;

/// <summary>Sets the formatting culture for one test and puts the previous one back.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo previous = CultureInfo.CurrentCulture;

    public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

    public void Dispose() => CultureInfo.CurrentCulture = previous;
}

/// <summary>A timer that only runs when the test says so.</summary>
internal sealed class FakeTimer : IUiTimer
{
    public TimeSpan? Interval { get; private set; }
    public Action? Tick { get; private set; }

    public void Start(TimeSpan interval, Action tick)
    {
        Interval = interval;
        Tick = tick;
    }

    public void Stop()
    {
        Interval = null;
        Tick = null;
    }
}

/// <summary>Hands out a canned answer and counts how often it was asked.</summary>
internal sealed class FakeClaudeSource(Func<ClaudeReading> answer) : IClaudeUsageSource
{
    public bool IsInstalled { get; set; } = true;
    public Func<ClaudeReading> Answer { get; set; } = answer;
    public int Reads { get; private set; }

    public Task<ClaudeReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(Answer());
    }
}

/// <summary>Hands out canned readings and records what the store asked for.</summary>
internal sealed class FakeStatsSource : IStatsSource
{
    private readonly Lock gate = new();

    public List<SampleRequest> Requests { get; } = [];
    public int Resets { get; private set; }
    public StatsSample Next { get; set; } = new();
    public IReadOnlyList<ProcessUsage> ProcessList { get; set; } = [];

    public CpuInfo CpuInfo => CpuInfo.Unknown;

    public StatsSample Sample(SampleRequest request)
    {
        lock (gate)
        {
            Requests.Add(request);
            var next = Next;
            return new StatsSample
            {
                Cpu = next.Cpu,
                PerCore = next.PerCore,
                Counts = next.Counts,
                Memory = next.Memory,
                Network = next.Network,
                NetworkTotals = next.NetworkTotals,
                Disk = request.IncludeSlow ? next.Disk : null,
                Battery = request.IncludeSlow ? next.Battery : null,
                NetworkInterface = request.IncludeSlow ? next.NetworkInterface : null,
                Thermals = request.IncludeThermals ? next.Thermals : null,
                Gpu = request.IncludeGpu ? next.Gpu : null,
                Processes = request.IncludeProcesses ? ProcessList : null,
            };
        }
    }

    public IReadOnlyList<ProcessUsage> Processes(bool reset)
    {
        lock (gate)
        {
            if (reset) Resets++;
            return ProcessList;
        }
    }

    public IReadOnlyList<ProcessUsage> WatchProcesses()
    {
        lock (gate)
        {
            Watches++;
            return ProcessList;
        }
    }

    public int Watches { get; private set; }
}

/// <summary>Keeps what would have been shown as notifications.</summary>
internal sealed class FakePoster : IAlertPoster
{
    public List<Alert> Posted { get; } = [];

    public void Post(Alert alert) => Posted.Add(alert);
}

/// <summary>Moves "recycled" items into a folder of its own instead of the Recycle Bin.</summary>
internal sealed class FakeRecycler(string bin) : IRecycler
{
    public List<string> Recycled { get; } = [];

    public void Recycle(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            var target = Path.Combine(bin, Guid.NewGuid().ToString());
            if (Directory.Exists(path)) Directory.Move(path, target);
            else if (File.Exists(path)) File.Move(path, target);
            else continue;
            Recycled.Add(path);
        }
    }
}
