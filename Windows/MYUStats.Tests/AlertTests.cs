namespace MYUStats.Tests;

public class AlertEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeUsage Claude(double session, double week = 0.1, ClaudeUsageStatus status = ClaudeUsageStatus.Ok) => new(status,
        [
            new UsageWindow("session", "Current session", session, Start.AddHours(1)),
            new UsageWindow("week-all-models", "This week", week, null),
        ], null, Start);

    private static IReadOnlyList<Alert> Check(AlertEngine engine, double seconds, AlertSnapshot snapshot, AlertSettings settings) =>
        engine.Evaluate(snapshot, settings, Start.AddSeconds(seconds));

    [Fact]
    public void AClaudeLimitAlertsOnceAndAgainOnlyAfterItResets()
    {
        var engine = new AlertEngine(8);
        var settings = new AlertSettings { ClaudeThreshold = 0.8 };
        Assert.Empty(Check(engine, 0, new AlertSnapshot { Claude = Claude(0.5) }, settings));
        var alerts = Check(engine, 10, new AlertSnapshot { Claude = Claude(0.85) }, settings);
        Assert.Equal(["claude.session"], alerts.Select(alert => alert.Id));
        Assert.Equal(StatMetric.Claude, alerts[0].Metric);
        Assert.Empty(Check(engine, 20, new AlertSnapshot { Claude = Claude(0.9) }, settings));
        Assert.Empty(Check(engine, 30, new AlertSnapshot { Claude = Claude(0.02) }, settings));
        Assert.Single(Check(engine, 40, new AlertSnapshot { Claude = Claude(0.81) }, settings));
        Assert.Equal(["claude.week-all-models"],
            Check(engine, 50, new AlertSnapshot { Claude = Claude(0.81, week: 0.95) }, settings).Select(alert => alert.Id));
    }

    [Fact]
    public void StaleFiguresAndASwitchedOffAlertStayQuiet()
    {
        var engine = new AlertEngine(8);
        Assert.Empty(Check(engine, 0, new AlertSnapshot { Claude = Claude(0.99, status: ClaudeUsageStatus.Stale) }, new AlertSettings { ClaudeThreshold = 0.8 }));
        Assert.Empty(Check(engine, 10, new AlertSnapshot { Claude = Claude(0.99) }, new AlertSettings()));
    }

    [Fact]
    public void LowDiskSpaceAlertsOnceAndAgainAfterSpaceWasFreedAndHalfAnHourPassed()
    {
        var engine = new AlertEngine(8);
        var settings = new AlertSettings { DiskFreeBytes = 10_000 };
        AlertSnapshot Disk(long free) => new() { Disk = new DiskUsage("Local Disk (C:)", free, 100_000) };
        Assert.Empty(Check(engine, 0, Disk(20_000), settings));
        Assert.Equal([AlertKind.Disk], Check(engine, 10, Disk(9_000), settings).Select(alert => alert.Kind));
        Assert.Empty(Check(engine, 20, Disk(8_000), settings));
        Assert.Empty(Check(engine, 30, Disk(11_000), settings));
        Assert.Empty(Check(engine, 40, Disk(9_000), settings));
        Assert.Empty(Check(engine, 50, Disk(13_000), settings));
        Assert.Empty(Check(engine, 60, Disk(9_000), settings));
        Assert.Single(Check(engine, 60 + AlertEngine.Cooldown.TotalSeconds, Disk(9_000), settings));
    }

    [Fact]
    public void HeatAlertsOnlyAfterAMinuteAboveTheLimit()
    {
        var settings = new AlertSettings { TemperatureLimit = 90 };
        var engine = new AlertEngine(8);
        Assert.Empty(Check(engine, 0, new AlertSnapshot { Temperature = 95 }, settings));
        Assert.Empty(Check(engine, 30, new AlertSnapshot { Temperature = 96 }, settings));
        Assert.Equal([AlertKind.Temperature], Check(engine, 60, new AlertSnapshot { Temperature = 94 }, settings).Select(alert => alert.Kind));
        Assert.Empty(Check(engine, 70, new AlertSnapshot { Temperature = 99 }, settings));

        var dipping = new AlertEngine(8);
        Assert.Empty(Check(dipping, 0, new AlertSnapshot { Temperature = 95 }, settings));
        Assert.Empty(Check(dipping, 30, new AlertSnapshot { Temperature = 80 }, settings));
        Assert.Empty(Check(dipping, 60, new AlertSnapshot { Temperature = 95 }, settings));
        Assert.Single(Check(dipping, 120, new AlertSnapshot { Temperature = 95 }, settings));
    }

    [Fact]
    public void ACheckLongAfterTheLastOneAsAfterSleepStartsTheMinuteOver()
    {
        var engine = new AlertEngine(8);
        var settings = new AlertSettings { TemperatureLimit = 90 };
        Assert.Empty(Check(engine, 0, new AlertSnapshot { Temperature = 95 }, settings));
        Assert.Empty(Check(engine, 3_600, new AlertSnapshot { Temperature = 95 }, settings));
        Assert.Single(Check(engine, 3_660, new AlertSnapshot { Temperature = 95 }, settings));
    }

    [Fact]
    public void RunningOutOfMemoryAlertsAfterAMinute()
    {
        var engine = new AlertEngine(8);
        var settings = new AlertSettings { Memory = true };
        Assert.Empty(Check(engine, 0, new AlertSnapshot { MemoryPressure = MemoryPressure.Critical }, settings));
        Assert.Equal([AlertKind.Memory], Check(engine, 60, new AlertSnapshot { MemoryPressure = MemoryPressure.Critical }, settings).Select(alert => alert.Kind));
        Assert.Empty(Check(engine, 70, new AlertSnapshot { MemoryPressure = MemoryPressure.Warning }, settings));
        Assert.Empty(Check(engine, 80, new AlertSnapshot { MemoryPressure = MemoryPressure.Critical }, settings));
    }

    [Fact]
    public void AProcessThatKeepsACoreBusyAlertsOnceByName()
    {
        // Eight logical processors: one busy core is 12.5 % of the CPU, as Task Manager counts it.
        var engine = new AlertEngine(8);
        var settings = new AlertSettings { ProcessMinutes = 2 };
        AlertSnapshot Busy(double cpu) => new()
        {
            Processes = [new ProcessUsage(42, "dart", cpu, 0), new ProcessUsage(7, "explorer", 0.01, 0)],
            ProcessWindow = TimeSpan.FromSeconds(30),
        };
        Assert.Empty(Check(engine, 30, Busy(0.125), settings));
        Assert.Empty(Check(engine, 60, Busy(0.12), settings));
        Assert.Empty(Check(engine, 70, new AlertSnapshot(), settings));
        Assert.Empty(Check(engine, 90, Busy(0.125), settings));
        var alerts = Check(engine, 120, Busy(0.13), settings);
        Assert.Equal(["process.42"], alerts.Select(alert => alert.Id));
        Assert.Contains("dart", alerts[0].Title);
        Assert.Equal(StatMetric.Cpu, alerts[0].Metric);
        Assert.Empty(Check(engine, 150, Busy(0.125), settings));
        // It calms down, then gets stuck again: the half-hour pause keeps it quiet for now.
        Assert.Empty(Check(engine, 180, Busy(0.01), settings));
        for (var seconds = 210; seconds <= 330; seconds += 30) Assert.Empty(Check(engine, seconds, Busy(0.125), settings));
    }

    [Fact]
    public void HalfACoreIsNotBusy()
    {
        var engine = new AlertEngine(8);
        var settings = new AlertSettings { ProcessMinutes = 2 };
        var snapshot = new AlertSnapshot { Processes = [new ProcessUsage(42, "dart", 0.06, 0)], ProcessWindow = TimeSpan.FromSeconds(30) };
        for (var seconds = 30; seconds <= 600; seconds += 30) Assert.Empty(Check(engine, seconds, snapshot, settings));
    }

    [Fact]
    public void TurningAnAlertOffForgetsThatItFired()
    {
        var engine = new AlertEngine(8);
        var snapshot = new AlertSnapshot { Disk = new DiskUsage("Local Disk (C:)", 1, 100) };
        Assert.Single(Check(engine, 0, snapshot, new AlertSettings { DiskFreeBytes = 10 }));
        Assert.Empty(Check(engine, 10, snapshot, new AlertSettings()));
        Assert.Empty(Check(engine, 20, snapshot, new AlertSettings { DiskFreeBytes = 10 }));
        Assert.Single(Check(engine, 20 + AlertEngine.Cooldown.TotalSeconds, snapshot, new AlertSettings { DiskFreeBytes = 10 }));
    }
}

public class AlertMonitorTests
{
    private static (AlertMonitor Monitor, StatsStore Store, FakeStatsSource Source, AppSettings Settings, FakePoster Poster, FakeTimer Timer) Make(
        Func<DateTimeOffset> clock)
    {
        var source = new FakeStatsSource();
        var settings = new AppSettings();
        var claude = new ClaudeUsageStore(new FakeClaudeSource(() => new ClaudeReading([], null)) { IsInstalled = false }, settings, new FakeTimer(), logs: new ClaudeLogReader([]));
        var store = new StatsStore(source, new FakeTimer(), claude);
        var poster = new FakePoster();
        var timer = new FakeTimer();
        return (new AlertMonitor(store, settings, timer, poster, clock), store, source, settings, poster, timer);
    }

    [Fact]
    public async Task NothingIsReadWhileEveryAlertIsOff()
    {
        var (monitor, _, source, _, poster, timer) = Make(() => DateTimeOffset.Now);
        monitor.Start();
        Assert.Equal(AlertMonitor.CheckInterval, timer.Interval);
        await monitor.CheckAsync();
        Assert.Empty(poster.Posted);
        Assert.Equal(0, source.Watches);
    }

    [Fact]
    public async Task LowDiskSpaceIsPosted()
    {
        var (monitor, store, source, settings, poster, _) = Make(() => DateTimeOffset.Now);
        source.Next = new StatsSample { Disk = new DiskUsage("Local Disk (C:)", 1L << 30, 256L << 30) };
        await store.SampleNowAsync();
        settings.AlertDisk = true;
        await monitor.CheckAsync();
        Assert.Equal([AlertKind.Disk], poster.Posted.Select(alert => alert.Kind));
        Assert.Contains("Local Disk (C:)", poster.Posted[0].Body);
    }

    [Fact]
    public async Task ProcessesAreReadEveryThirdCheckAndTheFirstReadingOnlyStartsTheWindow()
    {
        var now = DateTimeOffset.Now;
        var (monitor, _, source, settings, poster, _) = Make(() => now);
        source.ProcessList = [new ProcessUsage(42, "dart", 1.0, 0)];
        settings.AlertProcess = true;
        settings.AlertProcessMinutes = 2;
        for (var check = 0; check < 24; check++)
        {
            await monitor.CheckAsync();
            now += AlertMonitor.CheckInterval;
        }
        Assert.Equal(8, source.Watches);
        Assert.Equal(["process.42"], poster.Posted.Select(alert => alert.Id));
    }

    [Fact]
    public void ATestAlertOpensTheAlertsPage()
    {
        var (monitor, _, _, _, poster, _) = Make(() => DateTimeOffset.Now);
        monitor.SendTest();
        Assert.Null(Assert.Single(poster.Posted).Metric);
    }
}
