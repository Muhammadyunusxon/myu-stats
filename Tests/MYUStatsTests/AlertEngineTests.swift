import Foundation
import Testing
@testable import MYUStats

@Suite("Alerts")
struct AlertEngineTests {
    private let start = Date(timeIntervalSince1970: 1_800_000_000)

    private func claude(_ session: Double, week: Double = 0.1, status: ClaudeUsage.Status = .ok) -> ClaudeUsage {
        ClaudeUsage(status: status, windows: [
            UsageWindow(id: "session", label: "Current session", usedFraction: session, resetsAt: start.addingTimeInterval(3_600)),
            UsageWindow(id: "week-all-models", label: "This week", usedFraction: week, resetsAt: nil),
        ])
    }

    /// Runs one check `seconds` after `start`.
    private func check(_ engine: inout AlertEngine, at seconds: TimeInterval, _ snapshot: AlertSnapshot,
                       _ settings: AlertSettings) -> [Alert] {
        engine.evaluate(snapshot, settings: settings, now: start.addingTimeInterval(seconds))
    }

    @Test("a Claude Code limit alerts once, and again only after it resets")
    func claudeLimit() {
        var engine = AlertEngine()
        let settings = AlertSettings(claudeThreshold: 0.8)
        #expect(check(&engine, at: 0, AlertSnapshot(claude: claude(0.5)), settings).isEmpty)
        let alerts = check(&engine, at: 10, AlertSnapshot(claude: claude(0.85)), settings)
        #expect(alerts.map(\.id) == ["claude.session"])
        #expect(alerts.first?.section == .detail(.claude))
        #expect(check(&engine, at: 20, AlertSnapshot(claude: claude(0.9)), settings).isEmpty)
        // The session resets, then fills up again.
        #expect(check(&engine, at: 30, AlertSnapshot(claude: claude(0.02)), settings).isEmpty)
        #expect(check(&engine, at: 40, AlertSnapshot(claude: claude(0.81)), settings).count == 1)
        // The weekly limit is its own alert.
        #expect(check(&engine, at: 50, AlertSnapshot(claude: claude(0.81, week: 0.95)), settings).map(\.id) == ["claude.week-all-models"])
    }

    @Test("stale Claude Code figures and a switched-off alert stay quiet")
    func claudeQuiet() {
        var engine = AlertEngine()
        #expect(check(&engine, at: 0, AlertSnapshot(claude: claude(0.99, status: .stale("x"))), AlertSettings(claudeThreshold: 0.8)).isEmpty)
        #expect(check(&engine, at: 10, AlertSnapshot(claude: claude(0.99)), AlertSettings()).isEmpty)
    }

    @Test("low disk space alerts once, and again after space was freed and half an hour passed")
    func disk() {
        var engine = AlertEngine()
        let settings = AlertSettings(diskFreeBytes: 10_000)
        func snapshot(_ free: Int64) -> AlertSnapshot { AlertSnapshot(disk: DiskUsage(name: "Macintosh HD", free: free, total: 100_000)) }
        #expect(check(&engine, at: 0, snapshot(20_000), settings).isEmpty)
        #expect(check(&engine, at: 10, snapshot(9_000), settings).map(\.kind) == [.disk])
        #expect(check(&engine, at: 20, snapshot(8_000), settings).isEmpty)
        // A little freed is not enough to start over.
        #expect(check(&engine, at: 30, snapshot(11_000), settings).isEmpty)
        #expect(check(&engine, at: 40, snapshot(9_000), settings).isEmpty)
        // A quarter more than the limit is, but the half-hour pause still holds.
        #expect(check(&engine, at: 50, snapshot(13_000), settings).isEmpty)
        #expect(check(&engine, at: 60, snapshot(9_000), settings).isEmpty)
        #expect(check(&engine, at: 60 + AlertEngine.cooldown, snapshot(9_000), settings).map(\.kind) == [.disk])
    }

    @Test("heat alerts only after a minute above the limit")
    func temperature() {
        var engine = AlertEngine()
        let settings = AlertSettings(temperatureLimit: 90)
        #expect(check(&engine, at: 0, AlertSnapshot(temperature: 95), settings).isEmpty)
        #expect(check(&engine, at: 30, AlertSnapshot(temperature: 96), settings).isEmpty)
        #expect(check(&engine, at: 60, AlertSnapshot(temperature: 94), settings).map(\.kind) == [.temperature])
        #expect(check(&engine, at: 70, AlertSnapshot(temperature: 99), settings).isEmpty)
        // A dip restarts the minute.
        var fresh = AlertEngine()
        #expect(check(&fresh, at: 0, AlertSnapshot(temperature: 95), settings).isEmpty)
        #expect(check(&fresh, at: 30, AlertSnapshot(temperature: 80), settings).isEmpty)
        #expect(check(&fresh, at: 60, AlertSnapshot(temperature: 95), settings).isEmpty)
        #expect(check(&fresh, at: 120, AlertSnapshot(temperature: 95), settings).count == 1)
    }

    @Test("a check long after the last one, as after sleep, starts the minute over")
    func sleepResets() {
        var engine = AlertEngine()
        let settings = AlertSettings(temperatureLimit: 90)
        #expect(check(&engine, at: 0, AlertSnapshot(temperature: 95), settings).isEmpty)
        #expect(check(&engine, at: 3_600, AlertSnapshot(temperature: 95), settings).isEmpty)
        #expect(check(&engine, at: 3_660, AlertSnapshot(temperature: 95), settings).count == 1)
    }

    @Test("critical memory pressure alerts after a minute")
    func memory() {
        var engine = AlertEngine()
        let settings = AlertSettings(memory: true)
        #expect(check(&engine, at: 0, AlertSnapshot(memoryPressure: .critical), settings).isEmpty)
        #expect(check(&engine, at: 60, AlertSnapshot(memoryPressure: .critical), settings).map(\.kind) == [.memory])
        #expect(check(&engine, at: 70, AlertSnapshot(memoryPressure: .warning), settings).isEmpty)
        #expect(check(&engine, at: 80, AlertSnapshot(memoryPressure: .critical), settings).isEmpty)
    }

    @Test("a process that keeps a core busy for the chosen minutes alerts once, by name")
    func busyProcess() {
        var engine = AlertEngine()
        let settings = AlertSettings(processMinutes: 2)
        func snapshot(_ cpu: Double) -> AlertSnapshot {
            AlertSnapshot(processes: [ProcessUsage(pid: 42, name: "dart", cpu: cpu, memory: 0),
                                      ProcessUsage(pid: 7, name: "Finder", cpu: 0.01, memory: 0)],
                          processWindow: 30)
        }
        #expect(check(&engine, at: 30, snapshot(1.0), settings).isEmpty)
        #expect(check(&engine, at: 60, snapshot(0.95), settings).isEmpty)
        // Checks without a process reading keep the count going.
        #expect(check(&engine, at: 70, AlertSnapshot(), settings).isEmpty)
        #expect(check(&engine, at: 90, snapshot(1.0), settings).isEmpty)
        let alerts = check(&engine, at: 120, snapshot(1.2), settings)
        #expect(alerts.map(\.id) == ["process.42"])
        #expect(alerts.first?.title.contains("dart") == true)
        #expect(alerts.first?.section == .detail(.cpu))
        #expect(check(&engine, at: 150, snapshot(1.0), settings).isEmpty)
        // It calms down, then gets stuck again: the half-hour pause keeps it quiet for now.
        #expect(check(&engine, at: 180, snapshot(0.1), settings).isEmpty)
        for seconds in stride(from: 210.0, through: 330, by: 30) {
            #expect(check(&engine, at: seconds, snapshot(1.0), settings).isEmpty)
        }
    }

    @Test("turning an alert off forgets that it fired")
    func switchingOff() {
        var engine = AlertEngine()
        let snapshot = AlertSnapshot(disk: DiskUsage(name: "Macintosh HD", free: 1, total: 100))
        #expect(check(&engine, at: 0, snapshot, AlertSettings(diskFreeBytes: 10)).count == 1)
        #expect(check(&engine, at: 10, snapshot, AlertSettings()).isEmpty)
        // Back on: the condition still holds, but the half-hour pause is still running.
        #expect(check(&engine, at: 20, snapshot, AlertSettings(diskFreeBytes: 10)).isEmpty)
        #expect(check(&engine, at: 20 + AlertEngine.cooldown, snapshot, AlertSettings(diskFreeBytes: 10)).count == 1)
    }

    @Test("a notification names the page it opens")
    func sections() {
        #expect(SettingsSection(id: SettingsSection.detail(.disk).id) == .detail(.disk))
        #expect(SettingsSection(id: "alerts") == .alerts)
        #expect(SettingsSection(id: "detail.fan") == nil)
    }
}
