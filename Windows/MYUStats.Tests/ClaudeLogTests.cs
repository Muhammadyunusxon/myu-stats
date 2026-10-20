using System.Text;
using System.Text.Json;

namespace MYUStats.Tests;

public class ClaudePricingTests
{
    [Fact]
    public void ModelIdsAreFoundWithoutTheirDatesAndTheLongestMatchWins()
    {
        Assert.Equal("claude-haiku-4-5", ClaudePricing.Normalized("claude-haiku-4-5-20251001"));
        Assert.Equal("claude-opus-4-1", ClaudePricing.Normalized("us.anthropic.claude-opus-4-1-20250805-v1:0"));
        Assert.Equal("claude-opus-4-5", ClaudePricing.Normalized("claude-opus-4-5@20251101"));
        Assert.Null(ClaudePricing.Normalized("<synthetic>"));
        Assert.Equal(4, ClaudePricing.Price("claude-opus-5-5")?.Input);
        Assert.Equal(5, ClaudePricing.Price("claude-opus-5")?.Input);
        Assert.Equal(15, ClaudePricing.Price("claude-opus-4-0")?.Input);
        Assert.Equal(3, ClaudePricing.Price("claude-sonnet-4-20250514")?.Input);
        Assert.Equal(0.08, ClaudePricing.Price("claude-3-5-haiku-20241022")?.CacheRead);
        Assert.Equal(0.25, ClaudePricing.Price("claude-fable-5-1")?.CacheRead);
        Assert.Null(ClaudePricing.Price("gpt-5"));
    }

    [Fact]
    public void DisplayNames()
    {
        Assert.Equal("Opus 5.5", ClaudePricing.DisplayName("claude-opus-5-5"));
        Assert.Equal("Haiku 4.5", ClaudePricing.DisplayName("claude-haiku-4-5-20251001"));
        Assert.Equal("Haiku 3.5", ClaudePricing.DisplayName("claude-3-5-haiku-20241022"));
        Assert.Equal("Sonnet 5", ClaudePricing.DisplayName("claude-sonnet-5"));
    }

    [Fact]
    public void EachKindOfTokenAtItsOwnRateWebSearchesOnTopFastModeScaled()
    {
        var request = new LoggedRequest("2026-09-25T10:00:00.000Z", "claude-opus-5", "/p")
        {
            Input = 1_000_000, Output = 1_000_000, CacheWrite5m = 1_000_000, CacheWrite1h = 1_000_000, CacheRead = 1_000_000, WebSearches = 3,
        };
        // 5 + 25 + 6.25 + 10 + 0.5, and three searches at a cent each.
        Assert.Equal(46.78, ClaudePricing.Cost(request)!.Value, 9);
        // Fast mode is $10 / $50 on Opus 5, and caching scales with it.
        Assert.Equal(93.53, ClaudePricing.Cost(request with { IsFast = true })!.Value, 9);
        Assert.Null(ClaudePricing.Cost(request with { Model = "claude-mystery-1" }));
    }
}

public sealed class ClaudeLogTests : IDisposable
{
    /// <summary>25 September 2026, noon UTC.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(Path.GetTempPath(), "MYUStatsClaude-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    /// <summary>One assistant turn as Claude Code writes it, on one line.</summary>
    private static string Line(string id, string time, string request = "req_1", string model = "claude-opus-5-5",
        string cwd = "/Users/me/projects/shop", long output = 100, long read = 1_000) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "assistant",
            ["timestamp"] = time,
            ["requestId"] = request,
            ["cwd"] = cwd,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["message"] = new Dictionary<string, object>
            {
                ["id"] = id,
                ["model"] = model,
                ["role"] = "assistant",
                ["content"] = new[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = "the \"type\":\"assistant\" of it" } },
                ["usage"] = new Dictionary<string, object>
                {
                    ["input_tokens"] = 10,
                    ["output_tokens"] = output,
                    ["cache_read_input_tokens"] = read,
                    ["cache_creation_input_tokens"] = 300,
                    ["cache_creation"] = new Dictionary<string, object> { ["ephemeral_5m_input_tokens"] = 100, ["ephemeral_1h_input_tokens"] = 200 },
                    ["server_tool_use"] = new Dictionary<string, object> { ["web_search_requests"] = 0 },
                    ["speed"] = "standard",
                },
            },
        });

    private void Write(string relative, bool append, params string[] lines)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = string.Concat(lines.Select(line => line + "\n"));
        if (append) File.AppendAllText(path, text);
        else File.WriteAllText(path, text);
    }

    private ClaudeDayUsage Today(ClaudeLogReader reader, DateTimeOffset? now = null) => reader.Today(now ?? Noon, TimeZoneInfo.Utc);

    [Fact]
    public void ALineIsReadForItsCountsSplitCacheWritesIncluded()
    {
        var parsed = ClaudeLogReader.Parse(Encoding.UTF8.GetBytes(Line("msg_1", "2026-09-25T10:00:00.000Z")));
        Assert.NotNull(parsed);
        Assert.Equal("msg_1|req_1", parsed.Value.Key);
        Assert.Equal("claude-opus-5-5", parsed.Value.Request.Model);
        Assert.Equal(new TokenCounts(10, 100, 300, 1_000), parsed.Value.Request.Tokens);
        Assert.Equal(200, parsed.Value.Request.CacheWrite1h);
        Assert.Null(ClaudeLogReader.Parse("""{"type":"user","timestamp":"2026-09-25T10:00:00.000Z"}"""u8));
        Assert.Null(ClaudeLogReader.Parse("not json"u8));
    }

    [Fact]
    public void OnlyTodayCountsAReplyLoggedTwiceCountsOnceAndSubagentsCountToo()
    {
        Write(Path.Combine("-Users-me-projects-shop", "session.jsonl"), false,
            Line("msg_0", "2026-09-24T23:59:59.000Z"),
            """{"type":"user","timestamp":"2026-09-25T09:00:00.000Z","message":{"content":"hello"}}""",
            Line("msg_1", "2026-09-25T09:00:01.000Z", output: 40),
            Line("msg_1", "2026-09-25T09:00:02.000Z", output: 100),
            Line("msg_2", "2026-09-25T09:05:00.000Z", model: "claude-sonnet-5", cwd: @"C:\Users\me\blog"));
        Write(Path.Combine("-Users-me-projects-shop", "session", "subagents", "agent-a1.jsonl"), false,
            Line("msg_3", "2026-09-25T09:06:00.000Z", request: "req_9"));

        var today = Today(new ClaudeLogReader([root]));
        Assert.Equal(3, today.Requests);
        Assert.Equal(300, today.Tokens.Output);
        Assert.Equal(3_000, today.Tokens.CacheRead);
        Assert.Equal(["shop", "blog"], today.Projects.Select(project => project.Name));
        Assert.Equal(["Opus 5.5", "Sonnet 5"], today.Models.Select(model => model.Name));
        Assert.True(today.Cost > 0);
        Assert.Equal(today.Cost!.Value, today.Projects.Sum(project => project.Cost ?? 0), 9);
    }

    [Fact]
    public void AfterTheFirstReadOnlyWhatWasAddedIsReadALineCutShortIncluded()
    {
        var file = Path.Combine("-Users-me-shop", "session.jsonl");
        Write(file, false, Line("msg_1", "2026-09-25T09:00:00.000Z"));
        var reader = new ClaudeLogReader([root]);
        Assert.Equal(1, Today(reader).Requests);

        // A line still being written is kept until its end arrives.
        var next = Line("msg_2", "2026-09-25T09:10:00.000Z");
        File.AppendAllText(Path.Combine(root, file), next[..(next.Length / 2)]);
        Assert.Equal(1, Today(reader).Requests);
        File.AppendAllText(Path.Combine(root, file), next[(next.Length / 2)..] + "\n");
        Assert.Equal(2, Today(reader).Requests);
    }

    [Fact]
    public void ANewDayStartsFromNothing()
    {
        Write(Path.Combine("p", "s.jsonl"), false, Line("msg_1", "2026-09-25T09:00:00.000Z"));
        var reader = new ClaudeLogReader([root]);
        Assert.Equal(1, Today(reader).Requests);
        var tomorrow = Today(reader, Noon.AddDays(1));
        Assert.Equal(0, tomorrow.Requests);
        Assert.Null(tomorrow.Cost);
    }

    [Fact]
    public void TheLogsAreFoundWhereClaudeCodeKeepsThem()
    {
        var home = Path.Combine("home", "me");
        Assert.Equal([Path.Combine(home, ".config", "claude", "projects"), Path.Combine(home, ".claude", "projects")],
            ClaudeLogReader.DefaultRoots(null, home));
        Assert.Equal([Path.Combine("a", "projects"), Path.Combine("b", "projects")], ClaudeLogReader.DefaultRoots("a, b", home));
        Assert.Equal("2026-09-25T00:00:00.000Z", ClaudeLogReader.Timestamp(new DateTimeOffset(2026, 9, 25, 5, 0, 0, TimeSpan.FromHours(5))));
    }

    [Fact]
    public async Task TheStoreCountsTodayWhenItRefreshes()
    {
        var now = DateTimeOffset.UtcNow;
        Write(Path.Combine("p", "s.jsonl"), false, "{\"type\":\"assistant\",\"timestamp\":\"" + ClaudeLogReader.Timestamp(now)
            + "\",\"requestId\":\"r\",\"cwd\":\"/Users/me/shop\",\"message\":{\"id\":\"m\",\"model\":\"claude-sonnet-5\","
            + "\"usage\":{\"input_tokens\":1000000,\"output_tokens\":0}}}");
        var store = new ClaudeUsageStore(new FakeClaudeSource(() => new ClaudeReading([], null)), new AppSettings(), new FakeTimer(),
            () => now, new ClaudeLogReader([root]));
        await store.RefreshAsync();
        Assert.Equal(1, store.Today?.Requests);
        Assert.Equal(2, store.Today?.Cost);
        Assert.Equal("shop", store.Today?.Projects[0].Name);
    }
}
