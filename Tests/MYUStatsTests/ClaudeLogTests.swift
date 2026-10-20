import Foundation
import Testing
@testable import MYUStats

@Suite("Claude Code pricing")
struct ClaudePricingTests {
    @Test("model IDs are found without their dates, and the longest match wins")
    func matching() {
        #expect(ClaudePricing.normalized("claude-haiku-4-5-20251001") == "claude-haiku-4-5")
        #expect(ClaudePricing.normalized("us.anthropic.claude-opus-4-1-20250805-v1:0") == "claude-opus-4-1")
        #expect(ClaudePricing.normalized("claude-opus-4-5@20251101") == "claude-opus-4-5")
        #expect(ClaudePricing.normalized("<synthetic>") == nil)
        #expect(ClaudePricing.price(for: "claude-opus-5-5")?.input == 4)
        #expect(ClaudePricing.price(for: "claude-opus-5")?.input == 5)
        #expect(ClaudePricing.price(for: "claude-opus-4-0")?.input == 15)
        #expect(ClaudePricing.price(for: "claude-opus-4-1-20250805")?.output == 75)
        #expect(ClaudePricing.price(for: "claude-sonnet-4-20250514")?.input == 3)
        #expect(ClaudePricing.price(for: "claude-3-5-haiku-20241022")?.cacheRead == 0.08)
        #expect(ClaudePricing.price(for: "claude-fable-5-1")?.cacheRead == 0.25)
        #expect(ClaudePricing.price(for: "gpt-5") == nil)
    }

    @Test func displayNames() {
        #expect(ClaudePricing.displayName("claude-opus-5-5") == "Opus 5.5")
        #expect(ClaudePricing.displayName("claude-haiku-4-5-20251001") == "Haiku 4.5")
        #expect(ClaudePricing.displayName("claude-3-5-haiku-20241022") == "Haiku 3.5")
        #expect(ClaudePricing.displayName("claude-sonnet-5") == "Sonnet 5")
    }

    @Test("each kind of token at its own rate, web searches on top, fast mode scaled")
    func cost() throws {
        var request = LoggedRequest(timestamp: "2026-09-25T10:00:00.000Z", model: "claude-opus-5", project: "/p",
                                    input: 1_000_000, output: 1_000_000, cacheWrite5m: 1_000_000, cacheWrite1h: 1_000_000,
                                    cacheRead: 1_000_000, webSearches: 3)
        // 5 + 25 + 6.25 + 10 + 0.5, and three searches at a cent each.
        #expect(abs(try #require(ClaudePricing.cost(of: request)) - 46.78) < 1e-9)
        request.isFast = true
        // Twice the rates: fast mode is $10 / $50 on Opus 5, and caching scales with it.
        #expect(abs(try #require(ClaudePricing.cost(of: request)) - 93.53) < 1e-9)
        request.model = "claude-mystery-1"
        #expect(ClaudePricing.cost(of: request) == nil)
    }
}

@Suite("Claude Code logs")
struct ClaudeLogTests {
    private let fileManager = FileManager.default
    /// 25 September 2026, noon UTC.
    private let noon = Date(timeIntervalSince1970: 1_790_337_600)
    private var utc: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }

    /// One assistant turn as Claude Code writes it, on one line.
    private func line(id: String, request: String = "req_1", time: String, model: String = "claude-opus-5-5",
                      cwd: String = "/Users/me/projects/shop", output: Int = 100, read: Int = 1_000) -> String {
        let object: [String: Any] = [
            "type": "assistant", "timestamp": time, "requestId": request, "cwd": cwd, "uuid": UUID().uuidString,
            "message": [
                "id": id, "model": model, "role": "assistant",
                "content": [["type": "text", "text": "Done: the \"type\":\"assistant\" of it"]],
                "usage": [
                    "input_tokens": 10, "output_tokens": output, "cache_read_input_tokens": read,
                    "cache_creation_input_tokens": 300,
                    "cache_creation": ["ephemeral_5m_input_tokens": 100, "ephemeral_1h_input_tokens": 200],
                    "server_tool_use": ["web_search_requests": 0], "speed": "standard",
                ],
            ],
        ]
        let data = try! JSONSerialization.data(withJSONObject: object)
        return String(decoding: data, as: UTF8.self).replacingOccurrences(of: "\\/", with: "/")
    }

    private func root() throws -> URL {
        let url = fileManager.temporaryDirectory.appendingPathComponent("MYUStatsClaude-\(UUID().uuidString)")
        try fileManager.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func write(_ lines: [String], to url: URL, append: Bool = false) throws {
        try fileManager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let text = lines.map { $0 + "\n" }.joined()
        if append, let handle = try? FileHandle(forWritingTo: url) {
            try handle.seekToEnd()
            try handle.write(contentsOf: Data(text.utf8))
            try handle.close()
        } else {
            try Data(text.utf8).write(to: url)
        }
    }

    @Test("a line is read for its counts, split cache writes included")
    func parsesALine() throws {
        let parsed = try #require(ClaudeLogReader.request(from: Data(line(id: "msg_1", time: "2026-09-25T10:00:00.000Z").utf8)))
        #expect(parsed.key == "msg_1|req_1")
        #expect(parsed.request.model == "claude-opus-5-5")
        #expect(parsed.request.project == "/Users/me/projects/shop")
        #expect(parsed.request.tokens == TokenCounts(input: 10, output: 100, cacheWrite: 300, cacheRead: 1_000))
        #expect(parsed.request.cacheWrite1h == 200)
        #expect(ClaudeLogReader.request(from: Data(#"{"type":"user","timestamp":"2026-09-25T10:00:00.000Z"}"#.utf8)) == nil)
    }

    @Test("only today counts, a reply logged twice counts once, and subagents count too")
    func countsToday() async throws {
        let root = try root()
        defer { try? fileManager.removeItem(at: root) }
        let session = root.appendingPathComponent("-Users-me-projects-shop/session.jsonl")
        try write([
            line(id: "msg_0", time: "2026-09-24T23:59:59.000Z"),
            #"{"type":"user","timestamp":"2026-09-25T09:00:00.000Z","message":{"content":"hello"}}"#,
            line(id: "msg_1", time: "2026-09-25T09:00:01.000Z", output: 40),
            line(id: "msg_1", time: "2026-09-25T09:00:02.000Z", output: 100),
            line(id: "msg_2", time: "2026-09-25T09:05:00.000Z", model: "claude-sonnet-5", cwd: "/Users/me/projects/blog"),
        ], to: session)
        try write([line(id: "msg_3", request: "req_9", time: "2026-09-25T09:06:00.000Z")],
                  to: root.appendingPathComponent("-Users-me-projects-shop/session/subagents/agent-a1.jsonl"))

        let reader = ClaudeLogReader(roots: [root])
        let today = await reader.today(now: noon, calendar: utc)
        #expect(today.requests == 3)
        #expect(today.tokens.output == 300)
        #expect(today.tokens.cacheRead == 3_000)
        #expect(today.projects.map(\.name) == ["shop", "blog"])
        #expect(today.models.map(\.name) == ["Opus 5.5", "Sonnet 5"])
        let cost = try #require(today.cost)
        #expect(cost > 0)
        #expect(abs(cost - (today.projects.compactMap(\.cost).reduce(0, +))) < 1e-9)
    }

    @Test("after the first read only what was added is read, a line cut short included")
    func readsOnlyWhatWasAdded() async throws {
        let root = try root()
        defer { try? fileManager.removeItem(at: root) }
        let session = root.appendingPathComponent("-Users-me-shop/session.jsonl")
        try write([line(id: "msg_1", time: "2026-09-25T09:00:00.000Z")], to: session)
        let reader = ClaudeLogReader(roots: [root])
        #expect(await reader.today(now: noon, calendar: utc).requests == 1)

        // A line still being written is kept until its end arrives.
        let next = line(id: "msg_2", time: "2026-09-25T09:10:00.000Z")
        let half = next.index(next.startIndex, offsetBy: next.count / 2)
        let handle = try FileHandle(forWritingTo: session)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data(next[..<half].utf8))
        try handle.close()
        #expect(await reader.today(now: noon, calendar: utc).requests == 1)
        try write([String(next[half...])], to: session, append: true)
        #expect(await reader.today(now: noon, calendar: utc).requests == 2)
    }

    @Test("a new day starts from nothing")
    func newDay() async throws {
        let root = try root()
        defer { try? fileManager.removeItem(at: root) }
        try write([line(id: "msg_1", time: "2026-09-25T09:00:00.000Z")], to: root.appendingPathComponent("p/s.jsonl"))
        let reader = ClaudeLogReader(roots: [root])
        #expect(await reader.today(now: noon, calendar: utc).requests == 1)
        let tomorrow = noon.addingTimeInterval(86_400)
        let next = await reader.today(now: tomorrow, calendar: utc)
        #expect(next.requests == 0)
        #expect(next.cost == nil)
    }

    @Test("the logs are found where Claude Code keeps them")
    func roots() {
        let home = URL(fileURLWithPath: "/Users/me")
        #expect(ClaudeLogReader.defaultRoots(environment: [:], home: home).map(\.path)
                == ["/Users/me/.config/claude/projects", "/Users/me/.claude/projects"])
        #expect(ClaudeLogReader.defaultRoots(environment: ["CLAUDE_CONFIG_DIR": "/a, /b"], home: home).map(\.path)
                == ["/a/projects", "/b/projects"])
        #expect(ClaudeLogReader.timestamp(Date(timeIntervalSince1970: 1_790_294_400)) == "2026-09-25T00:00:00.000Z")
    }
}
