import Foundation
import Testing
@testable import MYUStats

@Suite("Claude Code /usage")
struct ClaudeUsageParsingTests {
    /// The shape `claude --print /usage` answers with, prose after the limits included.
    private static let sample = """
        You are currently using your subscription to power your Claude Code usage

        Current session: 10% used · resets Sep 24 at 12am (Asia/Tashkent)
        Current week (all models): 45% used · resets Sep 26 at 6am (Asia/Tashkent)
        Current week (Fable): 0% used · resets Sep 26 at 6:30pm (Asia/Tashkent)

        What's contributing to your limits usage?
        Approximate, based on local sessions on this machine.
        Last 24h · 644 requests · 4 sessions
        """

    private static let now = ISO8601DateFormatter().date(from: "2026-09-23T14:00:00Z")!
    private static let tashkent = TimeZone(identifier: "Asia/Tashkent")!

    private func date(_ text: String) -> Date {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = Self.tashkent
        formatter.dateFormat = "yyyy-MM-dd HH:mm"
        return formatter.date(from: text)!
    }

    @Test("session and weekly lines become windows, in order, with their resets")
    func parsesWindows() throws {
        let windows = try ClaudeUsageReader.parse(Self.sample, now: Self.now)
        #expect(windows.map(\.id) == ["session", "week-all-models", "week-fable"])
        #expect(windows.map(\.usedFraction) == [0.10, 0.45, 0])
        #expect(windows[0].resetsAt == date("2026-09-24 00:00"))
        #expect(windows[1].resetsAt == date("2026-09-26 06:00"))
        #expect(windows[2].resetsAt == date("2026-09-26 18:30"))
        #expect(windows[2].label.contains("Fable"))
    }

    @Test("without a session line the answer is refused rather than half drawn")
    func refusesWithoutSession() {
        #expect(throws: ClaudeUsageError.unreadable) {
            try ClaudeUsageReader.parse("Current week (all models): 45% used", now: Self.now)
        }
        #expect(throws: ClaudeUsageError.unreadable) {
            try ClaudeUsageReader.parse("Please run /login", now: Self.now)
        }
    }

    @Test("a percentage is kept even when its reset date cannot be read")
    func keepsPercentWithoutDate() throws {
        let windows = try ClaudeUsageReader.parse("Current session: 38% used · resets soon", now: Self.now)
        #expect(windows.first?.usedFraction == 0.38)
        #expect(windows.first?.resetsAt == nil)
    }

    @Test("a reset without a year lands in the year nearest to now")
    func yearRollover() {
        let newYearsEve = ISO8601DateFormatter().date(from: "2026-12-31T12:00:00Z")!
        let reset = ClaudeUsageReader.resetDate(from: "Jan 2 at 3pm (UTC)", now: newYearsEve)
        #expect(reset == ISO8601DateFormatter().date(from: "2027-01-02T15:00:00Z"))
    }

    @Test("the plan is read from the top, as a whole word")
    func plan() {
        #expect(ClaudeUsageReader.plan(in: "Claude Max 5x plan\nCurrent session: 1% used") == "Max 5x")
        #expect(ClaudeUsageReader.plan(in: "Pro plan\nCurrent session: 1% used") == "Pro")
        #expect(ClaudeUsageReader.plan(in: Self.sample) == nil)
        #expect(ClaudeUsageReader.plan(in: "Approximately progress\nCurrent session: 1% used") == nil)
    }

    @Test("the ring shows the limit closest to running out")
    func headline() {
        let usage = ClaudeUsage(status: .ok, windows: [
            UsageWindow(id: "session", label: "", usedFraction: 0.1, resetsAt: nil),
            UsageWindow(id: "week", label: "", usedFraction: 0.45, resetsAt: nil),
        ])
        #expect(usage.headline?.id == "week")
    }

    @Test func locatesTheCommandLineInstallOnly() throws {
        let home = FileManager.default.temporaryDirectory.appendingPathComponent("ClaudeHome-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: home) }
        #expect(ClaudeUsageReader.locate(home: home, root: home.appendingPathComponent("root")) == nil)

        let binary = home.appendingPathComponent(".local/bin/claude")
        try FileManager.default.createDirectory(at: binary.deletingLastPathComponent(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: binary.path, contents: Data("#!/bin/sh\n".utf8),
                                       attributes: [.posixPermissions: 0o755])
        #expect(ClaudeUsageReader.locate(home: home, root: home.appendingPathComponent("root")) == binary)
    }
}

/// Hands out a canned answer and counts how often it was asked.
private final class FakeClaudeSource: ClaudeUsageSource, @unchecked Sendable {
    var isInstalled = true
    var result: Result<(windows: [UsageWindow], plan: String?), Error>
    private(set) var reads = 0

    init(_ result: Result<(windows: [UsageWindow], plan: String?), Error>) {
        self.result = result
    }

    func read() async throws -> (windows: [UsageWindow], plan: String?) {
        reads += 1
        return try result.get()
    }
}

@MainActor
@Suite("Claude usage store")
struct ClaudeUsageStoreTests {
    private static let windows = [UsageWindow(id: "session", label: "Current session", usedFraction: 0.3, resetsAt: nil)]
    /// Keeps the tests off this Mac's own Claude Code logs.
    private static let noLogs = ClaudeLogReader(roots: [])

    private func defaults() -> UserDefaults {
        let name = "MYUStatsTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: name)!
        defaults.removePersistentDomain(forName: name)
        defaults.registerMYUStatsDefaults()
        return defaults
    }

    @Test("a refresh also counts today's use from the logs")
    func countsToday() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("MYUStatsClaude-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("-Users-me-shop/session.jsonl")
        try FileManager.default.createDirectory(at: log.deletingLastPathComponent(), withIntermediateDirectories: true)
        let now = ClaudeLogReader.timestamp(Date())
        let line = #"{"type":"assistant","timestamp":"\#(now)","requestId":"r","cwd":"/Users/me/shop","message":{"id":"m","model":"claude-sonnet-5","usage":{"input_tokens":1000000,"output_tokens":0}}}"#
        try Data((line + "\n").utf8).write(to: log)

        let store = ClaudeUsageStore(source: FakeClaudeSource(.success((Self.windows, nil))),
                                     logs: ClaudeLogReader(roots: [root]), defaults: defaults())
        await store.refresh()
        #expect(store.today?.requests == 1)
        #expect(store.today?.cost == 2)
        #expect(store.today?.projects.first?.name == "shop")
    }

    @Test func publishesAReading() async {
        let source = FakeClaudeSource(.success((Self.windows, "Pro")))
        let store = ClaudeUsageStore(source: source, logs: Self.noLogs, defaults: defaults())
        await store.refresh()
        #expect(store.usage?.status == .ok)
        #expect(store.usage?.windows == Self.windows)
        #expect(store.usage?.plan == "Pro")
        #expect(store.usage?.updatedAt != nil)
    }

    @Test("no answer before any reading says what to do")
    func unavailable() async {
        let store = ClaudeUsageStore(source: FakeClaudeSource(.failure(ClaudeUsageError.noAnswer)), logs: Self.noLogs, defaults: defaults())
        await store.refresh()
        guard case .unavailable(let message) = store.usage?.status else {
            Issue.record("expected unavailable, got \(String(describing: store.usage?.status))")
            return
        }
        #expect(message.contains("claude"))
        #expect(store.usage?.windows.isEmpty == true)
    }

    @Test("a failed read keeps the last good figures, marked stale")
    func keepsLastGoodReading() async {
        let source = FakeClaudeSource(.success((Self.windows, nil)))
        let store = ClaudeUsageStore(source: source, logs: Self.noLogs, defaults: defaults())
        await store.refresh()
        let firstRead = store.usage?.updatedAt
        source.result = .failure(ClaudeUsageError.unreadable)
        await store.refresh()
        guard case .stale = store.usage?.status else {
            Issue.record("expected stale, got \(String(describing: store.usage?.status))")
            return
        }
        #expect(store.usage?.windows == Self.windows)
        #expect(store.usage?.updatedAt == firstRead)
    }

    @Test("a hidden ring is not read, unless asked for directly")
    func hiddenRingIsNotRead() async {
        let defaults = defaults()
        defaults.hiddenMetrics = [.claude]
        let source = FakeClaudeSource(.success((Self.windows, nil)))
        let store = ClaudeUsageStore(source: source, logs: Self.noLogs, defaults: defaults)
        await store.refresh()
        #expect(source.reads == 0)
        await store.refresh(force: true)
        #expect(source.reads == 1)
    }

    @Test("a folded pill is not read for, unless the alert needs it, and unfolding catches up")
    func offScreenRingIsNotRead() async throws {
        let defaults = defaults()
        let source = FakeClaudeSource(.success((Self.windows, nil)))
        let store = ClaudeUsageStore(source: source, logs: Self.noLogs, defaults: defaults)
        store.isOnScreen = false
        await store.refresh()
        #expect(source.reads == 0)

        defaults.set(true, forKey: SettingsKey.alertClaude)
        await store.refresh()
        #expect(source.reads == 1)

        defaults.set(false, forKey: SettingsKey.alertClaude)
        let fresh = ClaudeUsageStore(source: source, logs: Self.noLogs, defaults: defaults)
        fresh.isOnScreen = false
        fresh.isOnScreen = true
        for _ in 0..<100 where source.reads < 2 { try await Task.sleep(for: .milliseconds(10)) }
        #expect(source.reads == 2)
    }

    @Test("installing or removing Claude Code is noticed and reported")
    func availabilityChanges() async {
        let source = FakeClaudeSource(.success((Self.windows, nil)))
        source.isInstalled = false
        let store = ClaudeUsageStore(source: source, logs: Self.noLogs, defaults: defaults())
        var changes = 0
        store.onAvailabilityChanged = { changes += 1 }
        await store.refresh()
        #expect(!store.isInstalled)
        #expect(source.reads == 0)

        source.isInstalled = true
        await store.refresh()
        #expect(store.isInstalled)
        #expect(changes == 1)
        #expect(source.reads == 1)
    }
}

@Suite("Reset and update times")
struct TimeFormatTests {
    private let now = Date(timeIntervalSince1970: 1_000_000)

    @Test func timeUntil() {
        #expect(Format.timeUntil(now.addingTimeInterval(45 * 60), now: now) == "45m")
        #expect(Format.timeUntil(now.addingTimeInterval(3 * 3600 + 20 * 60), now: now) == "3h 20m")
        #expect(Format.timeUntil(now.addingTimeInterval(2 * 86_400 + 5 * 3600), now: now) == "2d 5h")
        #expect(Format.timeUntil(now.addingTimeInterval(-60), now: now) == "0m")
    }

    @Test func updatedAgo() {
        #expect(Format.updatedAgo(now, now: now) == "Updated just now")
        #expect(Format.updatedAgo(now.addingTimeInterval(-4 * 60), now: now) == "Updated 4 min ago")
        #expect(Format.updatedAgo(now.addingTimeInterval(-2 * 3600), now: now) == "Updated 2 h ago")
    }
}
