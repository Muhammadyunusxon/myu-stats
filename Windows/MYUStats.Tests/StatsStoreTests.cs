namespace MYUStats.Tests;

public class StatsStoreTests
{
    private static readonly DiskUsage Disk = new("Local Disk (C:)", 100, 400);
    private static readonly BatteryState Battery = new(80, IsCharging: false, IsOnAC: false);

    private static StatsSample Sample(double cpu = 0.5) => new()
    {
        Cpu = new CpuUsage(cpu, 0),
        Memory = new MemoryUsage(8, 4, 0, 0, 0, 0, 0, MemoryPressure.Normal),
        Network = new NetworkRate(1_000, 0),
        Disk = Disk,
        Battery = Battery,
        Thermals = new ThermalState([new ThermalZone("TZ00", 50)]),
    };

    private static (StatsStore Store, FakeStatsSource Source, FakeTimer Timer) Make()
    {
        var source = new FakeStatsSource();
        var timer = new FakeTimer();
        var claude = new ClaudeUsageStore(new FakeClaudeSource(() => new ClaudeReading([], null)) { IsInstalled = false },
            new AppSettings(), new FakeTimer(), logs: new ClaudeLogReader([]));
        return (new StatsStore(source, timer, claude), source, timer);
    }

    [Fact]
    public async Task SlowMetricsAreReadEveryFifthTickAndThermalsEverySecondOne()
    {
        var (store, source, _) = Make();
        for (var tick = 0; tick < 6; tick++) await store.SampleNowAsync();
        Assert.Equal([true, false, false, false, false, true], source.Requests.Select(request => request.IncludeSlow));
        Assert.Equal([true, false, true, false, true, false], source.Requests.Select(request => request.IncludeThermals));
    }

    [Fact]
    public async Task ValuesReadOnlyOnSlowTicksStayPutInBetween()
    {
        var (store, source, _) = Make();
        source.Next = Sample();
        await store.SampleNowAsync();
        await store.SampleNowAsync();
        Assert.Equal(Disk, store.Disk);
        Assert.Equal(Battery, store.Battery);
        Assert.Equal(50, store.Thermals?.Headline);
    }

    [Fact]
    public async Task HistoryKeepsOnlyTheMostRecentSamples()
    {
        var (store, source, _) = Make();
        for (var index = 0; index < StatsStore.HistoryLength + 5; index++)
        {
            source.Next = Sample(index / 100.0);
            await store.SampleNowAsync();
        }
        Assert.Equal(StatsStore.HistoryLength, store.CpuHistory.Count);
        Assert.Equal((StatsStore.HistoryLength + 4) / 100.0, store.CpuHistory[^1]);
        Assert.Equal(StatsStore.HistoryLength, store.NetworkHistory.Count);
        Assert.Equal(StatsStore.HistoryLength, store.MemoryHistory.Count);
    }

    [Fact]
    public async Task ProcessesAreOnlyReadWhileSomethingShowsThem()
    {
        var (store, source, _) = Make();
        source.ProcessList = [.. Enumerable.Range(1, 15).Select(index => new ProcessUsage(index, $"p{index}", index / 100.0, (ulong)(16 - index)))];

        await store.SampleNowAsync();
        Assert.False(source.Requests[^1].IncludeProcesses);
        Assert.Empty(store.TopByCpu);

        store.SetProcessSampling(true, "card");
        // Turning demand on reads processes straight away, from a clean baseline.
        await WaitUntil(() => source.Resets == 1 && store.TopByCpu.Count > 0);
        await store.SampleNowAsync();
        Assert.True(source.Requests[^1].IncludeProcesses);
        Assert.Equal(StatsStore.TopProcessCount, store.TopByCpu.Count);
        Assert.Equal(15, store.TopByCpu[0].Pid);
        Assert.Equal(1, store.TopByMemory[0].Pid);

        store.SetProcessSampling(false, "card");
        await store.SampleNowAsync();
        Assert.False(source.Requests[^1].IncludeProcesses);
    }

    [Fact]
    public async Task TheGpuIsReadEveryOtherTickAndItsChartEveryTick()
    {
        var (store, source, _) = Make();
        var sample = Sample();
        sample.Gpu = new GpuState(
            [new GpuAdapter("0x00000000_0x0000A1B2", "Intel(R) UHD Graphics") { Utilization = 0.1 },
             new GpuAdapter("0x00000000_0x0000C3D4", "NVIDIA GeForce RTX 3060") { Utilization = 0.6 }],
            []);
        source.Next = sample;
        await store.SampleNowAsync();
        Assert.False(source.Requests[^1].IncludeGpu);
        Assert.False(store.IsAvailable(StatMetric.Gpu));
        await store.SampleNowAsync();
        Assert.True(source.Requests[^1].IncludeGpu);
        Assert.True(store.IsAvailable(StatMetric.Gpu));
        Assert.Equal("NVIDIA GeForce RTX 3060", store.Gpu?.Headline?.Name);
        await store.SampleNowAsync();
        Assert.Equal([0.6, 0.6], store.GpuHistory);
    }

    [Fact]
    public async Task ThePillHearsWhenTheBatteryOrThermalSensorsAppear()
    {
        var (store, source, _) = Make();
        var changes = 0;
        store.SensorsChanged += () => changes++;
        await store.SampleNowAsync(); // nothing yet
        Assert.Equal(0, changes);
        source.Next = Sample();
        for (var tick = 0; tick < 5; tick++) await store.SampleNowAsync(); // the fifth is the next slow tick
        Assert.True(changes >= 1);
        var settled = changes;
        for (var tick = 0; tick < 5; tick++) await store.SampleNowAsync();
        Assert.Equal(settled, changes);
    }

    [Fact]
    public async Task AvailabilityFollowsWhatWasRead()
    {
        var (store, source, _) = Make();
        Assert.True(store.IsAvailable(StatMetric.Cpu));
        Assert.False(store.IsAvailable(StatMetric.Battery));
        Assert.False(store.IsAvailable(StatMetric.Claude));
        source.Next = Sample();
        await store.SampleNowAsync();
        Assert.True(store.IsAvailable(StatMetric.Battery));
        Assert.True(store.IsAvailable(StatMetric.Thermals));
    }

    [Fact]
    public void StartingRunsTheTimerAtTheChosenInterval()
    {
        var (store, _, timer) = Make();
        store.Start(2);
        Assert.Equal(TimeSpan.FromSeconds(2), timer.Interval);
        Assert.Equal(40 * 2, store.HistoryDuration);
        store.Stop();
        Assert.Null(timer.Interval);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(5);
        Assert.True(condition());
    }
}
