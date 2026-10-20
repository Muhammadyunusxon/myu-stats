namespace MYUStats.Tests;

public class ClaudeUsageParsingTests
{
    /// <summary>The shape `claude --print /usage` answers with, prose after the limits included, with Windows line endings.</summary>
    private static readonly string Sample = string.Join("\r\n",
        "You are currently using your subscription to power your Claude Code usage",
        "",
        "Current session: 10% used · resets Sep 24 at 12am (Asia/Tashkent)",
        "Current week (all models): 45% used · resets Sep 26 at 6am (Asia/Tashkent)",
        "Current week (Fable): 0% used · resets Sep 26 at 6:30pm (Asia/Tashkent)",
        "",
        "What's contributing to your limits usage?",
        "Approximate, based on local sessions on this machine.",
        "Last 24h · 644 requests · 4 sessions");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Tashkent is UTC+5 all year.</summary>
    private static DateTimeOffset Tashkent(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(5));

    [Fact]
    public void SessionAndWeeklyLinesBecomeWindowsInOrderWithTheirResets()
    {
        var windows = ClaudeUsageReader.Parse(Sample, Now);
        Assert.Equal(["session", "week-all-models", "week-fable"], windows.Select(window => window.Id));
        Assert.Equal([0.10, 0.45, 0], windows.Select(window => window.UsedFraction));
        Assert.Equal(Tashkent(9, 24, 0), windows[0].ResetsAt);
        Assert.Equal(Tashkent(9, 26, 6), windows[1].ResetsAt);
        Assert.Equal(Tashkent(9, 26, 18, 30), windows[2].ResetsAt);
        Assert.Contains("Fable", windows[2].Label);
        Assert.Equal("Current session", windows[0].Label);
        Assert.Equal("This week", windows[1].Label);
    }

    [Theory]
    [InlineData("Current week (all models): 45% used")]
    [InlineData("Please run /login")]
    [InlineData("")]
    public void WithoutASessionLineTheAnswerIsRefusedRatherThanHalfDrawn(string text)
    {
        var error = Assert.Throws<ClaudeUsageException>(() => ClaudeUsageReader.Parse(text, Now));
        Assert.Equal(ClaudeUsageError.Unreadable, error.Error);
    }

    [Fact]
    public void APercentageIsKeptEvenWhenItsResetDateCannotBeRead()
    {
        var windows = ClaudeUsageReader.Parse("Current session: 38.5% used · resets soon", Now);
        Assert.Equal(0.385, windows[0].UsedFraction, 6);
        Assert.Null(windows[0].ResetsAt);
    }

    [Fact]
    public void AResetWithoutAYearLandsInTheYearNearestToNow()
    {
        var newYearsEve = DateTimeOffset.Parse("2026-12-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(
            DateTimeOffset.Parse("2027-01-02T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            ClaudeUsageReader.ResetDate("Jan 2 at 3pm (UTC)", newYearsEve));
    }

    [Fact]
    public void TheTwentyNinthOfFebruaryParsesInALeapYear()
    {
        var now = DateTimeOffset.Parse("2028-02-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(
            DateTimeOffset.Parse("2028-02-29T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            ClaudeUsageReader.ResetDate("Feb 29 at 9am (UTC)", now));
    }

    [Fact]
    public void AnUnknownZoneFallsBackToLocalTimeRatherThanNothing() =>
        Assert.NotNull(ClaudeUsageReader.ResetDate("Sep 24 at 12am (Not/AZone)", Now));

    [Fact]
    public void ThePlanIsReadFromTheTopAsAWholeWord()
    {
        Assert.Equal("Max 5x", ClaudeUsageReader.Plan("Claude Max 5x plan\nCurrent session: 1% used"));
        Assert.Equal("Pro", ClaudeUsageReader.Plan("Pro plan\r\nCurrent session: 1% used"));
        Assert.Null(ClaudeUsageReader.Plan(Sample));
        Assert.Null(ClaudeUsageReader.Plan("Approximately progress\nCurrent session: 1% used"));
    }

    [Fact]
    public void TheRingShowsTheLimitClosestToRunningOut()
    {
        var usage = new ClaudeUsage(ClaudeUsageStatus.Ok,
            [new UsageWindow("session", "", 0.1, null), new UsageWindow("week", "", 0.45, null)], null, null);
        Assert.Equal("week", usage.Headline?.Id);
        Assert.Null(new ClaudeUsage(ClaudeUsageStatus.Unavailable, [], null, null).Headline);
    }
}

public class ClaudeLocateTests
{
    private const string Home = @"C:\Users\a";
    private const string Local = @"C:\Users\a\AppData\Local";
    private const string Roaming = @"C:\Users\a\AppData\Roaming";

    private static string? Locate(string? path, params string[] files) =>
        ClaudeUsageReader.Locate(Home, Local, Roaming, path, new HashSet<string>(files, StringComparer.OrdinalIgnoreCase).Contains);

    [Fact]
    public void NothingInstalled() => Assert.Null(Locate(@"C:\Windows\system32;C:\Windows"));

    [Fact]
    public void TheNativeInstallerComesFirst() => Assert.Equal(
        @"C:\Users\a\.local\bin\claude.exe",
        Locate(null, @"C:\Users\a\AppData\Roaming\npm\claude.cmd", @"C:\Users\a\.local\bin\claude.exe"));

    [Fact]
    public void AnNpmShimIsFound() => Assert.Equal(
        @"C:\Users\a\AppData\Roaming\npm\claude.cmd",
        Locate(null, @"C:\Users\a\AppData\Roaming\npm\claude.cmd"));

    [Fact]
    public void PathIsSearchedLast() => Assert.Equal(
        @"D:\tools\claude.exe",
        Locate(@"C:\Windows; D:\tools\ ;", @"D:\tools\claude.exe"));

    [Fact]
    public void TheDesktopAppsCopiesAreLeftOut() => Assert.Null(Locate(
        @"C:\Users\a\AppData\Local\Microsoft\WindowsApps;C:\Users\a\AppData\Local\AnthropicClaude\app-1.0",
        @"C:\Users\a\AppData\Local\Microsoft\WindowsApps\claude.exe",
        @"C:\Users\a\AppData\Local\AnthropicClaude\app-1.0\claude.exe"));
}

public class ClaudeUsageStoreTests
{
    private static readonly IReadOnlyList<UsageWindow> Windows = [new UsageWindow("session", "Current session", 0.3, null)];

    /// <summary>Keeps the tests off this computer's own Claude Code logs.</summary>
    private static readonly ClaudeLogReader NoLogs = new([]);

    private static ClaudeUsageStore Store(FakeClaudeSource source, AppSettings? settings = null, FakeTimer? timer = null) =>
        new(source, settings ?? new AppSettings(), timer ?? new FakeTimer(), logs: NoLogs);

    [Fact]
    public async Task PublishesAReading()
    {
        var store = Store(new FakeClaudeSource(() => new ClaudeReading(Windows, "Pro")));
        await store.RefreshAsync();
        Assert.Equal(ClaudeUsageStatus.Ok, store.Usage?.Status);
        Assert.Equal(Windows, store.Usage?.Windows);
        Assert.Equal("Pro", store.Usage?.Plan);
        Assert.NotNull(store.Usage?.UpdatedAt);
        Assert.False(store.IsRefreshing);
    }

    [Fact]
    public async Task NoAnswerBeforeAnyReadingSaysWhatToDo()
    {
        var store = Store(new FakeClaudeSource(() => throw new ClaudeUsageException(ClaudeUsageError.NoAnswer)));
        await store.RefreshAsync();
        Assert.Equal(ClaudeUsageStatus.Unavailable, store.Usage?.Status);
        Assert.Contains("claude", store.Usage?.Message);
        Assert.Empty(store.Usage!.Windows);
    }

    [Fact]
    public async Task AFailedReadKeepsTheLastGoodFiguresMarkedStale()
    {
        var source = new FakeClaudeSource(() => new ClaudeReading(Windows, null));
        var store = Store(source);
        await store.RefreshAsync();
        var firstRead = store.Usage?.UpdatedAt;
        source.Answer = () => throw new ClaudeUsageException(ClaudeUsageError.Unreadable);
        await store.RefreshAsync();
        Assert.Equal(ClaudeUsageStatus.Stale, store.Usage?.Status);
        Assert.Equal(Windows, store.Usage?.Windows);
        Assert.Equal(firstRead, store.Usage?.UpdatedAt);
    }

    [Fact]
    public async Task AHiddenRingIsNotReadUnlessAskedForDirectly()
    {
        var source = new FakeClaudeSource(() => new ClaudeReading(Windows, null));
        var store = Store(source, new AppSettings { Hidden = new HashSet<StatMetric> { StatMetric.Claude } });
        await store.RefreshAsync();
        Assert.Equal(0, source.Reads);
        await store.RefreshAsync(force: true);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task AFoldedPillIsNotReadForUnlessTheAlertNeedsItAndUnfoldingCatchesUp()
    {
        var source = new FakeClaudeSource(() => new ClaudeReading(Windows, null));
        var settings = new AppSettings();
        var store = Store(source, settings);
        store.IsOnScreen = false;
        await store.RefreshAsync();
        Assert.Equal(0, source.Reads);

        settings.AlertClaude = true;
        await store.RefreshAsync();
        Assert.Equal(1, source.Reads);

        settings.AlertClaude = false;
        var fresh = Store(source, settings);
        fresh.IsOnScreen = false;
        fresh.IsOnScreen = true;
        for (var i = 0; i < 100 && source.Reads < 2; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task InstallingOrRemovingClaudeCodeIsNoticedAndReported()
    {
        var source = new FakeClaudeSource(() => new ClaudeReading(Windows, null)) { IsInstalled = false };
        var store = Store(source);
        var changes = 0;
        store.AvailabilityChanged += () => changes++;
        await store.RefreshAsync();
        Assert.False(store.IsInstalled);
        Assert.Equal(0, source.Reads);

        source.IsInstalled = true;
        await store.RefreshAsync();
        Assert.True(store.IsInstalled);
        Assert.Equal(1, changes);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task FreshFiguresAreNotReadAgain()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        var source = new FakeClaudeSource(() => new ClaudeReading(Windows, null));
        var store = new ClaudeUsageStore(source, new AppSettings(), new FakeTimer(), () => now, NoLogs);
        await store.RefreshAsync();
        store.RefreshIfStale();
        Assert.Equal(1, source.Reads);
        now = now.AddMinutes(6);
        store.RefreshIfStale();
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public void StartingReadsNowAndEveryFiveMinutes()
    {
        var timer = new FakeTimer();
        var source = new FakeClaudeSource(() => new ClaudeReading(Windows, null));
        var store = Store(source, timer: timer);
        store.Start();
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Interval);
        Assert.Equal(1, source.Reads);
        store.Stop();
        Assert.Null(timer.Interval);
    }
}
