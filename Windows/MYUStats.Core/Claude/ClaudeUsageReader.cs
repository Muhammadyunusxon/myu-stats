using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MYUStats;

/// <summary>
/// Claude Code's own <c>/usage</c>, asked of the installed <c>claude</c> command.
/// </summary>
/// <remarks>
/// The figures come off the sign-in Claude Code already holds, so MYU STATS never touches the
/// credential: no token file is read and nothing is asked of Windows' credential store. In print mode
/// <c>/usage</c> is a local command that asks Anthropic for the limits; it sends no prompt to a model.
/// </remarks>
public sealed partial class ClaudeUsageReader : IClaudeUsageSource
{
    /// <summary>Print mode, no transcript and no MCP servers: without <c>--strict-mcp-config</c> every read
    /// would start whatever MCP servers the user has configured, none of which <c>/usage</c> needs.</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["--print", "--no-session-persistence", "--strict-mcp-config", "/usage"];

    /// <summary>Long enough for a cold start on a busy machine; a stuck process is killed.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    public bool IsInstalled => Locate() is not null;

    public async Task<ClaudeReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var binary = Locate() ?? throw new ClaudeUsageException(ClaudeUsageError.NoAnswer);
        var text = await RunAsync(binary, cancellationToken);
        return new ClaudeReading(Parse(text, DateTimeOffset.Now), Plan(text));
    }

    // MARK: - Finding it

    /// <summary>Where Claude Code installs itself on Windows, checked first, then every folder on PATH.</summary>
    public static string? Locate() => Locate(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("PATH"),
        File.Exists);

    /// <param name="home">%USERPROFILE%.</param>
    /// <param name="local">%LOCALAPPDATA%.</param>
    /// <param name="roaming">%APPDATA%.</param>
    /// <param name="path">The PATH variable, folders separated by semicolons.</param>
    /// <param name="exists">Whether a file exists; a fake in tests.</param>
    internal static string? Locate(string home, string local, string roaming, string? path, Func<string, bool> exists)
    {
        string[] folders =
        [
            Join(home, ".local", "bin"), // the native installer
            Join(roaming, "npm"), // npm install -g
            Join(home, ".bun", "bin"),
            Join(local, "Volta", "bin"),
            Join(local, "pnpm"),
            Join(home, "scoop", "shims"),
            Join(local, "Microsoft", "WinGet", "Links"),
            .. (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];
        foreach (var folder in folders)
        {
            foreach (var name in (string[])["claude.exe", "claude.cmd"])
            {
                var candidate = Join(folder, name);
                if (!IsExcluded(candidate) && exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Copies that are not the command line: the Claude desktop app keeps its own, signed in elsewhere,
    /// and a WindowsApps alias would open the desktop app instead of answering.
    /// </summary>
    private static bool IsExcluded(string candidate) =>
        ((string[])[@"\WindowsApps\", @"\AnthropicClaude\", @"\AppData\Roaming\Claude\"])
            .Any(part => candidate.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static string Join(params string[] parts) =>
        string.Join('\\', parts.Select((part, index) => index == 0 ? part.TrimEnd('\\', '/') : part.Trim('\\', '/')));

    private static bool IsScript(string binary) =>
        binary.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || binary.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    // MARK: - Asking it

    private static async Task<string> RunAsync(string binary, CancellationToken cancellationToken)
    {
        // One fixed folder: Claude Code keys what it writes on the working directory, and a new one per
        // read would leave a trail of them behind.
        Directory.CreateDirectory(AppPaths.ClaudeScratch);
        var info = new ProcessStartInfo
        {
            WorkingDirectory = AppPaths.ClaudeScratch,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (IsScript(binary))
        {
            // npm installs a .cmd shim, which only cmd.exe can run. The arguments are fixed, so the
            // command line needs no escaping beyond the quoted path.
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = $"/d /s /c \"\"{binary}\" {string.Join(' ', Arguments)}\"";
        }
        else
        {
            info.FileName = binary;
            foreach (var argument in Arguments) info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception error)
        {
            Log.Claude.Notice($"claude could not be started: {error.Message}");
            throw new ClaudeUsageException(ClaudeUsageError.NoAnswer);
        }
        // Never a terminal: with an open stdin it would wait for input until the timeout.
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        // Read and dropped, so a full pipe never stalls the command.
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited in the meantime.
            }
            Log.Claude.Notice("claude /usage did not finish in time and was stopped");
            throw new ClaudeUsageException(ClaudeUsageError.NoAnswer);
        }

        var text = await output;
        await errors;
        if (process.ExitCode != 0)
        {
            Log.Claude.Notice($"claude /usage exited with status {process.ExitCode}");
            throw new ClaudeUsageException(ClaudeUsageError.NoAnswer);
        }
        return text;
    }

    // MARK: - Reading what it said

    /// <summary>
    /// The lines /usage leads with, for example
    /// <code>
    /// Current session: 10% used · resets Sep 24 at 12am (Asia/Tashkent)
    /// Current week (all models): 45% used · resets Sep 26 at 6am (Asia/Tashkent)
    /// </code>
    /// Everything after them is prose about what drove the usage, and is ignored.
    /// </summary>
    [GeneratedRegex(@"^Current (?:(session)|week \(([^)]+)\)):\s*(\d+(?:\.\d+)?)%\s*used(?:\s*·\s*resets\s*(.+?))?\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex UsageLine();

    public static IReadOnlyList<UsageWindow> Parse(string text, DateTimeOffset now)
    {
        var windows = new List<UsageWindow>();
        foreach (Match match in UsageLine().Matches(text))
        {
            if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) continue;
            string id;
            string label;
            if (match.Groups[1].Success)
            {
                id = "session";
                label = L.T("Current session");
            }
            else
            {
                var scope = match.Groups[2].Value;
                id = "week-" + scope.ToLowerInvariant().Replace(' ', '-');
                label = scope.Equals("all models", StringComparison.OrdinalIgnoreCase)
                    ? L.T("This week")
                    : L.F("This week · {0}", scope);
            }
            if (windows.Any(window => window.Id == id)) continue;
            var resets = match.Groups[4].Success ? ResetDate(match.Groups[4].Value, now) : null;
            windows.Add(new UsageWindow(id, label, percent / 100, resets));
        }
        // Without the session line there is no reading worth drawing.
        if (!windows.Any(window => window.Id == "session")) throw new ClaudeUsageException(ClaudeUsageError.Unreadable);
        return windows;
    }

    /// <summary>
    /// <c>Sep 24 at 12am (Asia/Tashkent)</c> to a date. No year is printed, so the one nearest
    /// <paramref name="now"/> is chosen, which gets the turn of the year right in both directions.
    /// </summary>
    public static DateTimeOffset? ResetDate(string text, DateTimeOffset now)
    {
        var stamp = text.Trim();
        var zone = TimeZoneInfo.Local;
        var open = stamp.LastIndexOf('(');
        if (open >= 0 && stamp.EndsWith(')'))
        {
            if (FindZone(stamp[(open + 1)..^1]) is { } named) zone = named;
            stamp = stamp[..open].Trim();
        }
        stamp = stamp.Replace("am", "AM", StringComparison.Ordinal).Replace("pm", "PM", StringComparison.Ordinal);

        // Minutes are left out on the hour: "Sep 7 at 2:59pm" but "Sep 7 at 3pm". A leap year stands in
        // for the missing one, so 29 February parses.
        if (!DateTime.TryParseExact(stamp + " 2000", ["MMM d 'at' h:mmtt yyyy", "MMM d 'at' htt yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        var year = TimeZoneInfo.ConvertTime(now, zone).Year;
        DateTimeOffset? nearest = null;
        foreach (var candidate in (int[])[year - 1, year, year + 1])
        {
            if (parsed is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(candidate)) continue;
            var wall = new DateTime(candidate, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(wall)) continue;
            var date = new DateTimeOffset(wall, zone.GetUtcOffset(wall));
            if (nearest is null || (date - now).Duration() < (nearest.Value - now).Duration()) nearest = date;
        }
        return nearest;
    }

    /// <summary>The plan /usage names at the top, when it names one. Whole words, as written: "Pro" must not be found inside "approximately".</summary>
    public static string? Plan(string text)
    {
        var head = string.Join('\n', text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .Take(4));
        return ((string[])["Max 20x", "Max 5x", "Max", "Pro", "Team", "Enterprise"])
            .FirstOrDefault(plan => Regex.IsMatch(head, $@"\b{Regex.Escape(plan)}\b", RegexOptions.CultureInvariant));
    }

    private static TimeZoneInfo? FindZone(string id)
    {
        try
        {
            // IANA names work on Windows too: .NET converts them through ICU.
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}
