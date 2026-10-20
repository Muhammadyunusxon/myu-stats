using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MYUStats;

/// <summary>Tokens by kind, as Claude Code's logs count them.</summary>
public readonly record struct TokenCounts(long Input, long Output, long CacheWrite, long CacheRead)
{
    public long Total => Input + Output + CacheWrite + CacheRead;

    public static TokenCounts operator +(TokenCounts left, TokenCounts right) => new(
        left.Input + right.Input, left.Output + right.Output, left.CacheWrite + right.CacheWrite, left.CacheRead + right.CacheRead);
}

/// <summary>One API request Claude Code logged: when (ISO 8601 in UTC, as the logs write it), which model,
/// in which project (the working directory it ran in), and its token counts.</summary>
public sealed record LoggedRequest(string Timestamp, string Model, string Project)
{
    public long Input { get; init; }
    public long Output { get; init; }
    public long CacheWrite5m { get; init; }
    public long CacheWrite1h { get; init; }
    public long CacheRead { get; init; }
    public long WebSearches { get; init; }
    public bool IsFast { get; init; }

    public TokenCounts Tokens => new(Input, Output, CacheWrite5m + CacheWrite1h, CacheRead);
}

/// <summary>A project's or a model's part of the day; <see cref="Cost"/> is null when none of its models has a known price.</summary>
public sealed record ClaudeShare(string Id, string Name, long Tokens, double? Cost);

/// <summary>What Claude Code used on one day, counted from its session logs.</summary>
public sealed record ClaudeDayUsage(DateTimeOffset Day, DateTimeOffset ReadAt)
{
    public TokenCounts Tokens { get; init; }
    public int Requests { get; init; }
    /// <summary>What the same tokens would cost on the API at list prices; null when no model had a known price.</summary>
    public double? Cost { get; init; }
    /// <summary>Largest first.</summary>
    public IReadOnlyList<ClaudeShare> Projects { get; init; } = [];
    public IReadOnlyList<ClaudeShare> Models { get; init; } = [];

    public static ClaudeDayUsage Summarize(IEnumerable<LoggedRequest> requests, DateTimeOffset day, DateTimeOffset readAt)
    {
        var tokens = default(TokenCounts);
        var count = 0;
        double? cost = null;
        var projects = new Dictionary<string, ClaudeShare>();
        var models = new Dictionary<string, ClaudeShare>();
        static void Add(Dictionary<string, ClaudeShare> shares, string id, Func<string> name, long tokens, double? cost)
        {
            var share = shares.TryGetValue(id, out var found) ? found : new ClaudeShare(id, name(), 0, null);
            shares[id] = share with
            {
                Tokens = share.Tokens + tokens,
                Cost = cost is { } price ? (share.Cost ?? 0) + price : share.Cost,
            };
        }
        foreach (var request in requests)
        {
            var total = request.Tokens.Total;
            if (total == 0) continue;
            var price = ClaudePricing.Cost(request);
            count++;
            tokens += request.Tokens;
            if (price is { } value) cost = (cost ?? 0) + value;
            Add(projects, request.Project, () => ProjectName(request.Project), total, price);
            Add(models, ClaudePricing.Normalized(request.Model) ?? request.Model, () => ClaudePricing.DisplayName(request.Model), total, price);
        }
        static IReadOnlyList<ClaudeShare> LargestFirst(Dictionary<string, ClaudeShare> shares) =>
            [.. shares.Values.OrderByDescending(share => share.Cost ?? -1).ThenByDescending(share => share.Tokens)];
        return new ClaudeDayUsage(day, readAt)
        {
            Tokens = tokens,
            Requests = count,
            Cost = cost,
            Projects = LargestFirst(projects),
            Models = LargestFirst(models),
        };
    }

    /// <summary>The folder a project ran in, whichever separators its path uses.</summary>
    private static string ProjectName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.Length == 0 || name.EndsWith(':') ? L.T("Unknown") : name;
    }
}

/// <summary>A model's API list prices, in dollars per million tokens, with fast mode's on the models that have it.</summary>
public sealed record ModelPrice(double Input, double Output, double CacheWrite5m, double CacheWrite1h, double CacheRead,
    double? FastInput = null, double? FastOutput = null);

/// <summary>
/// Anthropic's API list prices (platform.claude.com, Pricing, September 2026), for putting a price on Claude
/// Code's tokens. A subscription pays differently; this is a yardstick, not a bill.
/// </summary>
public static partial class ClaudePricing
{
    /// <summary>Each web search is charged on top of its tokens.</summary>
    public const double WebSearch = 0.01;

    private static readonly ModelPrice Fable51 = new(10, 50, 12.5, 20, 0.25);
    private static readonly ModelPrice Fable5 = new(10, 50, 12.5, 20, 1);
    private static readonly ModelPrice Opus4 = new(5, 25, 6.25, 10, 0.5);
    private static readonly ModelPrice Opus4Early = new(15, 75, 18.75, 30, 1.5);
    private static readonly ModelPrice Sonnet4 = new(3, 15, 3.75, 6, 0.3);

    /// <summary>Keyed by model ID without its date. The longest matching key wins, so Opus 5.5 is not priced as Opus 5.</summary>
    public static IReadOnlyDictionary<string, ModelPrice> Prices { get; } = new Dictionary<string, ModelPrice>
    {
        ["claude-fable-5-1"] = Fable51,
        ["claude-mythos-5-1"] = Fable51,
        ["claude-fable-5"] = Fable5,
        ["claude-mythos-5"] = Fable5,
        ["claude-opus-5-5"] = new(4, 20, 5, 8, 0.2, FastInput: 8, FastOutput: 40),
        ["claude-opus-5"] = new(5, 25, 6.25, 10, 0.5, FastInput: 10, FastOutput: 50),
        ["claude-opus-4-8"] = new(5, 25, 6.25, 10, 0.5, FastInput: 10, FastOutput: 50),
        ["claude-opus-4-7"] = Opus4,
        ["claude-opus-4-6"] = Opus4,
        ["claude-opus-4-5"] = Opus4,
        ["claude-opus-4-1"] = Opus4Early,
        ["claude-opus-4"] = Opus4Early,
        ["claude-sonnet-5"] = new(2, 10, 2.5, 4, 0.2),
        ["claude-sonnet-4-6"] = Sonnet4,
        ["claude-sonnet-4-5"] = Sonnet4,
        ["claude-sonnet-4"] = Sonnet4,
        ["claude-haiku-4-5"] = new(1, 5, 1.25, 2, 0.1),
        ["claude-3-5-haiku"] = new(0.8, 4, 1, 1.6, 0.08),
    };

    [GeneratedRegex(@"-v\d+$")]
    private static partial Regex BedrockVersion();

    [GeneratedRegex(@"-\d{8}$")]
    private static partial Regex Date();

    /// <summary>The model ID alone: "claude-haiku-4-5-20251001" → "claude-haiku-4-5", and likewise for the Bedrock
    /// ("us.anthropic.claude-opus-4-1-20250805-v1:0") and Vertex ("claude-opus-4-5@20251101") forms.</summary>
    public static string? Normalized(string model)
    {
        var lower = model.ToLowerInvariant();
        var start = lower.IndexOf("claude-", StringComparison.Ordinal);
        if (start < 0) return null;
        var id = lower[start..];
        var cut = id.IndexOfAny(['@', ':', '[']);
        if (cut >= 0) id = id[..cut];
        return Date().Replace(BedrockVersion().Replace(id, ""), "");
    }

    public static ModelPrice? Price(string model) =>
        Normalized(model) is { } id
            ? Prices.Keys.Where(key => id == key || id.StartsWith(key + "-", StringComparison.Ordinal)).MaxBy(key => key.Length) is { } match
                ? Prices[match]
                : null
            : null;

    /// <summary>Null for a model without a known price.</summary>
    public static double? Cost(LoggedRequest request)
    {
        if (Price(request.Model) is not { } price) return null;
        var rates = price;
        if (request.IsFast && price.FastInput is { } input && price.FastOutput is { } output)
        {
            // Caching's multipliers apply on top of fast mode's rates.
            var scale = input / price.Input;
            rates = new ModelPrice(input, output, price.CacheWrite5m * scale, price.CacheWrite1h * scale, price.CacheRead * scale);
        }
        var perMillion = request.Input * rates.Input + request.Output * rates.Output + request.CacheWrite5m * rates.CacheWrite5m
            + request.CacheWrite1h * rates.CacheWrite1h + request.CacheRead * rates.CacheRead;
        return perMillion / 1_000_000 + request.WebSearches * WebSearch;
    }

    /// <summary>"claude-opus-5-5" → "Opus 5.5", "claude-3-5-haiku" → "Haiku 3.5".</summary>
    public static string DisplayName(string model)
    {
        if (Normalized(model) is not { } id) return model;
        var parts = id["claude-".Length..].Split('-');
        string[] families = ["opus", "sonnet", "haiku", "fable", "mythos"];
        if (parts.FirstOrDefault(part => families.Contains(part)) is not { } family) return id;
        var version = string.Join('.', parts.Where(part => part.Length > 0 && part.All(char.IsAsciiDigit)));
        var title = char.ToUpperInvariant(family[0]) + family[1..];
        return version.Length == 0 ? title : $"{title} {version}";
    }
}

/// <summary>
/// Reads Claude Code's own session logs for the day's token counts. The logs are JSON lines, one per event,
/// and grow to hundreds of megabytes a day, so each file is read once and then only from where the last read
/// stopped; only assistant turns from today are decoded, and only their counts are kept, never what was said.
/// One call at a time: the store runs it on a pool thread.
/// </summary>
public sealed class ClaudeLogReader
{
    private sealed class FileLog
    {
        public long Offset;
        /// <summary>The start of a line still being written.</summary>
        public byte[] Tail = [];
        /// <summary>By message and request ID: a reply logged in several lines, or copied into a resumed session, counts once.</summary>
        public Dictionary<string, LoggedRequest> Requests = [];
    }

    private delegate void LineHandler(ReadOnlySpan<byte> line);

    private const int ChunkSize = 4 << 20;
    private readonly Dictionary<string, FileLog> files = [];
    private readonly Lock gate = new();
    private string cutoff = "";

    /// <param name="roots">Folders of Claude Code projects; <see cref="DefaultRoots"/> when null.</param>
    public ClaudeLogReader(IReadOnlyList<string>? roots = null)
    {
        Roots = roots ?? DefaultRoots(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public IReadOnlyList<string> Roots { get; }

    public bool HasLogs => Roots.Any(Directory.Exists);

    /// <summary>Where Claude Code keeps its projects: <c>CLAUDE_CONFIG_DIR</c>, else <c>.config\claude</c> and <c>.claude</c> in the user folder.</summary>
    public static IReadOnlyList<string> DefaultRoots(string? configured, string home) =>
        !string.IsNullOrWhiteSpace(configured)
            ? [.. configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(folder => Path.Combine(folder, "projects"))]
            : [Path.Combine(home, ".config", "claude", "projects"), Path.Combine(home, ".claude", "projects")];

    /// <summary>"2026-09-24T19:00:00.000Z" for midnight in Tashkent: how the logs write their timestamps.</summary>
    public static string Timestamp(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Everything logged from midnight in <paramref name="zone"/> (the PC's own by default) to now.</summary>
    public ClaudeDayUsage Today(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var midnight = TimeZoneInfo.ConvertTime(now, zone).Date;
        var start = new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
        lock (gate)
        {
            var dayStart = Timestamp(start);
            if (dayStart != cutoff)
            {
                // A new day: what came before it no longer counts.
                cutoff = dayStart;
                foreach (var log in files.Values)
                {
                    log.Requests = log.Requests.Where(entry => string.CompareOrdinal(entry.Value.Timestamp, dayStart) >= 0)
                        .ToDictionary(entry => entry.Key, entry => entry.Value);
                }
            }
            var current = new HashSet<string>();
            foreach (var root in Roots)
            {
                // A file last written before midnight holds nothing from today.
                foreach (var file in LogFiles(root, start.UtcDateTime))
                {
                    current.Add(file.FullName);
                    Read(file);
                }
            }
            foreach (var path in files.Keys.Where(path => !current.Contains(path)).ToList()) files.Remove(path);

            var merged = new Dictionary<string, LoggedRequest>();
            foreach (var log in files.Values)
            {
                foreach (var (key, request) in log.Requests)
                {
                    if (string.CompareOrdinal(request.Timestamp, dayStart) < 0) continue;
                    if (merged.TryGetValue(key, out var seen) && seen.Output >= request.Output) continue;
                    merged[key] = request;
                }
            }
            return ClaudeDayUsage.Summarize(merged.Values, start, now);
        }
    }

    private void Read(FileInfo file)
    {
        if (!files.TryGetValue(file.FullName, out var log))
        {
            log = new FileLog();
            files[file.FullName] = log;
        }
        // Shorter than where the last read ended: the file was replaced, so start again.
        if (file.Length < log.Offset)
        {
            log.Offset = 0;
            log.Tail = [];
            log.Requests = [];
        }
        if (file.Length <= log.Offset) return;
        try
        {
            // Claude Code keeps the file open for writing while it runs.
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(log.Offset, SeekOrigin.Begin);
            var chunk = new byte[ChunkSize];
            var dayStart = System.Text.Encoding.ASCII.GetBytes(cutoff);
            var requests = log.Requests;
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                log.Offset += read;
                var data = log.Tail.Length == 0 ? chunk.AsSpan(0, read) : Join(log.Tail, chunk.AsSpan(0, read));
                var end = Scan(data, dayStart, line =>
                {
                    if (Parse(line) is not { } parsed) return;
                    // Streamed replies are logged more than once; the last copy has the final count.
                    if (requests.TryGetValue(parsed.Key, out var seen) && seen.Output > parsed.Request.Output) return;
                    requests[parsed.Key] = parsed.Request;
                });
                log.Tail = data[end..].ToArray();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Claude.Notice($"A Claude Code log could not be read: {error.GetType().Name}");
        }
    }

    private static Span<byte> Join(byte[] tail, ReadOnlySpan<byte> chunk)
    {
        var joined = new byte[tail.Length + chunk.Length];
        tail.CopyTo(joined, 0);
        chunk.CopyTo(joined.AsSpan(tail.Length));
        return joined;
    }

    // MARK: - Reading lines

    private static ReadOnlySpan<byte> Assistant => "\"type\":\"assistant\""u8;
    private static ReadOnlySpan<byte> TimestampKey => "\"timestamp\":\""u8;

    /// <summary>Hands <paramref name="handle"/> each complete line that may be an assistant turn logged at or after
    /// <paramref name="cutoff"/>, found by a byte search rather than by decoding; returns where the unfinished last line starts.</summary>
    private static int Scan(ReadOnlySpan<byte> data, ReadOnlySpan<byte> cutoff, LineHandler handle)
    {
        var start = 0;
        while (start < data.Length)
        {
            var length = data[start..].IndexOf((byte)'\n');
            if (length < 0) break;
            var line = data.Slice(start, length);
            if (line.IndexOf(Assistant) >= 0 && !IsBefore(line, cutoff)) handle(line);
            start += length + 1;
        }
        return start;
    }

    /// <summary>Whether the line's timestamp sorts before <paramref name="cutoff"/>, compared as text: both are ISO 8601 in UTC.</summary>
    private static bool IsBefore(ReadOnlySpan<byte> line, ReadOnlySpan<byte> cutoff)
    {
        var found = line.IndexOf(TimestampKey);
        if (found < 0) return false;
        var value = line[(found + TimestampKey.Length)..];
        return value.Length >= cutoff.Length && value[..cutoff.Length].SequenceCompareTo(cutoff) < 0;
    }

    /// <summary>One line of a log: an assistant turn with its counts, under its message and request IDs; null for anything else.</summary>
    internal static (string Key, LoggedRequest Request)? Parse(ReadOnlySpan<byte> line)
    {
        string? type = null, timestamp = null, requestId = null, cwd = null, uuid = null;
        var message = default(MessageFields);
        try
        {
            var reader = new Utf8JsonReader(line);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "type": type = Text(ref reader); break;
                    case "timestamp": timestamp = Text(ref reader); break;
                    case "requestId": requestId = Text(ref reader); break;
                    case "cwd": cwd = Text(ref reader); break;
                    case "uuid": uuid = Text(ref reader); break;
                    case "message" when reader.TokenType == JsonTokenType.StartObject: message = ReadMessage(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }
        if (type != "assistant" || timestamp is null || !message.HasUsage) return null;
        var key = message.Id is { } id ? $"{id}|{requestId}" : uuid;
        if (key is null) return null;
        var write1h = Math.Min(message.Write1h, message.Writes);
        return (key, new LoggedRequest(timestamp, message.Model ?? "", cwd ?? "")
        {
            Input = message.Input,
            Output = message.Output,
            // Newer logs split cache writes by how long they last; older ones only give the total, at the 5-minute rate.
            CacheWrite5m = message.Writes - write1h,
            CacheWrite1h = write1h,
            CacheRead = message.Reads,
            WebSearches = message.Searches,
            IsFast = message.Speed == "fast",
        });
    }

    private struct MessageFields
    {
        public string? Id;
        public string? Model;
        public string? Speed;
        public bool HasUsage;
        public long Input;
        public long Output;
        public long Writes;
        public long Write1h;
        public long Reads;
        public long Searches;
    }

    private static MessageFields ReadMessage(ref Utf8JsonReader reader)
    {
        var fields = default(MessageFields);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "id": fields.Id = Text(ref reader); break;
                case "model": fields.Model = Text(ref reader); break;
                case "usage" when reader.TokenType == JsonTokenType.StartObject: ReadUsage(ref reader, ref fields); break;
                default: reader.Skip(); break;
            }
        }
        return fields;
    }

    private static void ReadUsage(ref Utf8JsonReader reader, ref MessageFields fields)
    {
        fields.HasUsage = true;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "input_tokens": fields.Input = Number(ref reader); break;
                case "output_tokens": fields.Output = Number(ref reader); break;
                case "cache_creation_input_tokens": fields.Writes = Number(ref reader); break;
                case "cache_read_input_tokens": fields.Reads = Number(ref reader); break;
                case "speed": fields.Speed = Text(ref reader); break;
                case "cache_creation" when reader.TokenType == JsonTokenType.StartObject:
                    fields.Write1h = Field(ref reader, "ephemeral_1h_input_tokens");
                    break;
                case "server_tool_use" when reader.TokenType == JsonTokenType.StartObject:
                    fields.Searches = Field(ref reader, "web_search_requests");
                    break;
                default: reader.Skip(); break;
            }
        }
    }

    /// <summary>One number out of a flat object, skipping the rest of it.</summary>
    private static long Field(ref Utf8JsonReader reader, string wanted)
    {
        long value = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            if (name == wanted) value = Number(ref reader);
            else reader.Skip();
        }
        return value;
    }

    private static string? Text(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        reader.Skip();
        return null;
    }

    private static long Number(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var value) ? value : 0;

    // MARK: - Files

    /// <summary>Every log under <paramref name="root"/>, subagents' included, written to since <paramref name="since"/>.</summary>
    private static List<FileInfo> LogFiles(string root, DateTime since)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return
            [
                .. new DirectoryInfo(root)
                    .EnumerateFiles("*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(file => file.LastWriteTimeUtc >= since),
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
