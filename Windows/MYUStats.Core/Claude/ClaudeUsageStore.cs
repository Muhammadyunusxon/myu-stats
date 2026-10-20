namespace MYUStats;

/// <summary>
/// Claude Code's usage limits, read every few minutes by running <c>claude /usage</c>.
/// Only read while the Claude ring is on screen or its alert is on: each read runs Claude Code for several
/// seconds, so a folded pill, a hidden ring and the alert off stop the subprocess.
/// </summary>
public sealed class ClaudeUsageStore
{
    /// <summary>The figures move slowly, and every read starts a Claude Code process for a few seconds.</summary>
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Today's counts come from local logs, read from where the last read stopped, so they can be fresher.</summary>
    public static TimeSpan TodayRefreshInterval { get; } = TimeSpan.FromMinutes(1);

    private readonly IClaudeUsageSource source;
    private readonly AppSettings settings;
    private readonly IUiTimer timer;
    private readonly Func<DateTimeOffset> clock;
    private readonly ClaudeLogReader logs;
    private bool isReadingLogs;
    private bool isOnScreen = true;

    public ClaudeUsageStore(IClaudeUsageSource source, AppSettings settings, IUiTimer timer, Func<DateTimeOffset>? clock = null,
        ClaudeLogReader? logs = null)
    {
        this.source = source;
        this.settings = settings;
        this.timer = timer;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.logs = logs ?? new ClaudeLogReader();
        IsInstalled = source.IsInstalled;
    }

    public ClaudeUsage? Usage { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsInstalled { get; private set; }

    /// <summary>Today's tokens and their price, from Claude Code's session logs; null until first read, or without logs.</summary>
    public ClaudeDayUsage? Today { get; private set; }

    /// <summary>Raised on the UI thread when a read starts or ends.</summary>
    public event Action? Changed;

    /// <summary>Raised when Claude Code is installed or removed, so the pill can re-lay itself out.</summary>
    public event Action? AvailabilityChanged;

    /// <summary>Whether the ring can be seen: the pill is unfolded and shows it. Coming into view catches up on a
    /// read skipped while out of sight.</summary>
    public bool IsOnScreen
    {
        get => isOnScreen;
        set
        {
            if (isOnScreen == value) return;
            isOnScreen = value;
            if (value) RefreshIfStale();
        }
    }

    public void Start()
    {
        timer.Start(RefreshInterval, () => _ = RefreshAsync());
        _ = RefreshAsync();
    }

    public void Stop() => timer.Stop();

    /// <summary>For a card or page that just opened: reads unless the figures are still fresh.</summary>
    public void RefreshIfStale()
    {
        if (clock() - (Today?.ReadAt ?? DateTimeOffset.MinValue) > TodayRefreshInterval) _ = RefreshTodayAsync();
        var updated = Usage?.UpdatedAt ?? DateTimeOffset.MinValue;
        if (clock() - updated <= RefreshInterval) return;
        _ = RefreshAsync(force: true);
    }

    /// <summary>Counts today's tokens from the logs, on a pool thread; quick after the first time, which reads
    /// each of today's logs once.</summary>
    public async Task RefreshTodayAsync()
    {
        if (isReadingLogs || !logs.HasLogs) return;
        isReadingLogs = true;
        try
        {
            var now = clock();
            var first = Today is null;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            Today = await Task.Run(() => logs.Today(now));
            if (first) Log.Claude.Info($"Counted today's Claude Code logs in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            Log.Claude.Error($"Today's Claude Code logs could not be counted: {error}");
        }
        finally
        {
            isReadingLogs = false;
        }
    }

    /// <summary>Reads the limits, unless nothing needs them: the ring is out of sight and its alert is off.
    /// <paramref name="force"/> reads anyway, for the Refresh button and for the Details page, which can be
    /// opened with the ring hidden.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        var installed = source.IsInstalled;
        if (installed != IsInstalled)
        {
            IsInstalled = installed;
            AvailabilityChanged?.Invoke();
        }
        var isWanted = (IsOnScreen && !settings.Hidden.Contains(StatMetric.Claude)) || settings.AlertClaude;
        if (!installed || IsRefreshing || (!force && !isWanted)) return;
        // Local and quick, so it runs alongside the read rather than holding it up.
        var today = RefreshTodayAsync();

        IsRefreshing = true;
        Changed?.Invoke();
        Log.Claude.Info($"Reading Claude Code usage (forced: {force})");
        try
        {
            var reading = await source.ReadAsync();
            Usage = new ClaudeUsage(ClaudeUsageStatus.Ok, reading.Windows, reading.Plan, clock());
        }
        catch (Exception error)
        {
            Log.Claude.Notice($"Claude Code usage not read: {error.Message}");
            var message = MessageFor(error);
            // A failed read keeps the last good figures, dated, rather than blanking the ring.
            Usage = Usage is { Windows.Count: > 0 } previous
                ? previous with { Status = ClaudeUsageStatus.Stale, Message = message }
                : new ClaudeUsage(ClaudeUsageStatus.Unavailable, [], null, clock(), message);
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke();
        }
        await today;
    }

    public static string MessageFor(Exception error) => error switch
    {
        ClaudeUsageException { Error: ClaudeUsageError.NoAnswer } =>
            L.T("Claude Code did not answer. Run claude in a terminal and sign in."),
        ClaudeUsageException { Error: ClaudeUsageError.Unreadable } =>
            L.T("Claude Code answered in a form MYU STATS does not understand yet."),
        _ => error.Message,
    };
}
