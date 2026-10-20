import CoreGraphics
import Foundation
import Testing
@testable import MYUStats

@Suite("Formatting")
struct FormatTests {
    @Test func percent() {
        #expect(Format.percent(0) == "0%")
        #expect(Format.percent(0.124) == "12%")
        #expect(Format.percent(0.125) == "13%")
        #expect(Format.percent(1) == "100%")
    }

    @Test func shortRate() {
        #expect(Format.shortRate(0) == "0K")
        #expect(Format.shortRate(999) == "<1K")
        #expect(Format.shortRate(9_960) == "10K")
        #expect(Format.shortRate(9_940) == "9.9K")
        #expect(Format.shortRate(1_500) == "1.5K")
        #expect(Format.shortRate(12_000) == "12K")
        #expect(Format.shortRate(3_400_000) == "3.4M")
        #expect(Format.shortRate(2_000_000_000) == "2.0G")
    }

    @Test func durations() {
        #expect(Format.duration(minutes: 45) == "45m")
        #expect(Format.duration(minutes: 125) == "2h 5m")
        #expect(Format.uptime(59 * 60) == "0h 59m")
        #expect(Format.uptime(3 * 86_400 + 2 * 3_600 + 30) == "3d 2h")
    }

    @Test func tokens() {
        #expect(Format.tokens(845) == "845")
        #expect(Format.tokens(12_345) == "12K")
        #expect(Format.tokens(9_949) == "9.9K")
        #expect(Format.tokens(3_400_000) == "3.4M")
        #expect(Format.tokens(740_850_039) == "741M")
        #expect(Format.tokens(999_700) == "1.0M")
        #expect(Format.tokens(1_200_000_000) == "1.2B")
    }

    @Test func cost() {
        let english = Locale(identifier: "en_US")
        #expect(Format.cost(278.614, locale: english) == "$278.61")
        #expect(Format.cost(0, locale: english) == "$0.00")
        #expect(Format.cost(0.001, locale: english) == "<$0.01")
    }

    @Test func temperatureAndRPM() {
        #expect(Format.temperature(nil) == "–")
        #expect(Format.temperature(54.6) == "55 °C")
        #expect(Format.rpm(2316.7) == "2317 rpm")
    }
}

@Suite("Models")
struct ModelTests {
    @Test func cpuIdle() {
        let usage = CPUUsage(user: 0.3, system: 0.2)
        #expect(abs(usage.total - 0.5) < 0.0001)
        #expect(abs(usage.idle - 0.5) < 0.0001)
        #expect(CPUUsage(user: 0.8, system: 0.4).idle == 0)
    }

    @Test func memoryDerived() {
        let memory = MemoryUsage(used: 12, total: 16, app: 4, wired: 4, compressed: 4, cached: 2,
                                 swapUsed: 0, pressure: .normal)
        #expect(memory.fraction == 0.75)
        #expect(memory.available == 4)
        #expect(MemoryUsage.zero.fraction == 0)
        #expect(MemoryUsage(used: 20, total: 16, app: 0, wired: 0, compressed: 0, cached: 0,
                            swapUsed: 0, pressure: .warning).available == 0)
    }

    @Test func diskDerived() {
        let disk = DiskUsage(name: "Macintosh HD", free: 25, total: 100)
        #expect(disk.used == 75)
        #expect(disk.usedFraction == 0.75)
        #expect(DiskUsage(name: "x", free: 0, total: 0).usedFraction == 0)
    }

    @Test func fanFraction() {
        let range = (min: 2_000.0, max: 6_000.0)
        #expect(FanReading(index: 0, rpm: 0, minRPM: range.min, maxRPM: range.max).fraction == 0)
        #expect(!FanReading(index: 0, rpm: 0, minRPM: range.min, maxRPM: range.max).isSpinning)
        #expect(FanReading(index: 0, rpm: 4_000, minRPM: range.min, maxRPM: range.max).fraction == 0.5)
        #expect(FanReading(index: 0, rpm: 9_000, minRPM: range.min, maxRPM: range.max).fraction == 1)
        #expect(FanReading(index: 0, rpm: 1_000, minRPM: 0, maxRPM: 0).fraction == 0)
    }

    @Test func thermalHeadlineFallsBack() {
        var state = ThermalState(cpuAverage: 50, cpuHottest: 70, efficiencyAverage: 45,
                                 gpuAverage: 40, battery: 30, fans: [])
        #expect(state.headline == 50)
        state.cpuAverage = nil
        #expect(state.headline == 40)
        state.gpuAverage = nil
        #expect(state.headline == 30)
    }
}

@Suite("Settings")
struct SettingsTests {
    private func freshDefaults() -> UserDefaults {
        let name = "MYUStatsTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: name)!
        defaults.removePersistentDomain(forName: name)
        defaults.registerMYUStatsDefaults()
        return defaults
    }

    @Test func defaults() {
        let defaults = freshDefaults()
        #expect(defaults.screenEdge == .right)
        #expect(defaults.hiddenMetrics.isEmpty)
        #expect(defaults.metricOrder == StatMetric.allCases)
        #expect(defaults.bool(forKey: SettingsKey.autoHide))
        #expect(defaults.double(forKey: SettingsKey.verticalOffset) == 0)
    }

    @Test func edgeRoundTrip() {
        let defaults = freshDefaults()
        for edge in ScreenEdge.allCases {
            defaults.screenEdge = edge
            #expect(defaults.screenEdge == edge)
        }
        defaults.set("diagonal", forKey: SettingsKey.edge)
        #expect(defaults.screenEdge == .right)
    }

    @Test("a metric the saved order lacks goes after the metrics that come before it by default")
    func metricOrderPlacesNewMetrics() {
        let defaults = freshDefaults()
        // As if saved by a version that did not know about the GPU, thermals and Claude Code yet.
        defaults.set(["battery", "cpu", "memory", "network", "disk"], forKey: SettingsKey.metricOrder)
        #expect(defaults.metricOrder == [.battery, .cpu, .gpu, .memory, .network, .disk, .thermals, .claude])
        // Someone who moved the CPU ring down still gets the GPU ring next to it.
        defaults.set(["memory", "network", "cpu", "disk", "thermals", "battery", "claude"], forKey: SettingsKey.metricOrder)
        #expect(defaults.metricOrder == [.memory, .network, .cpu, .gpu, .disk, .thermals, .battery, .claude])
    }

    @Test func metricOrderDropsUnknownNames() {
        let defaults = freshDefaults()
        defaults.set(["fan", "cpu", "cpu"], forKey: SettingsKey.metricOrder)
        #expect(defaults.metricOrder.first == .cpu)
        #expect(defaults.metricOrder.count == StatMetric.allCases.count)
    }

    @Test("alerts are off until turned on, and reset turns them off again")
    func alertSettings() {
        let defaults = freshDefaults()
        #expect(!AlertSettings(defaults).isAnyOn)
        defaults.set(true, forKey: SettingsKey.alertDisk)
        defaults.set(20.0, forKey: SettingsKey.alertDiskFreeGB)
        defaults.set(true, forKey: SettingsKey.alertClaude)
        let settings = AlertSettings(defaults)
        #expect(settings.diskFreeBytes == 20_000_000_000)
        #expect(settings.claudeThreshold == AlertSettings.defaultClaudeThreshold)
        #expect(settings.temperatureLimit == nil)
        defaults.resetMYUStatsSettings()
        #expect(!AlertSettings(defaults).isAnyOn)
    }

    @Test("reset clears every user setting back to its default")
    func resetClearsEverything() {
        let defaults = freshDefaults()
        defaults.screenEdge = .left
        defaults.hiddenMetrics = [.disk]
        defaults.set(2.0, forKey: SettingsKey.sampleInterval)
        defaults.set("some-display", forKey: SettingsKey.display)
        defaults.resetMYUStatsSettings()
        #expect(defaults.screenEdge == .right)
        #expect(defaults.hiddenMetrics.isEmpty)
        #expect(defaults.double(forKey: SettingsKey.sampleInterval) == 1)
        #expect(defaults.string(forKey: SettingsKey.display) == "")
    }

    @Test func hiddenMetricsRoundTrip() {
        let defaults = freshDefaults()
        defaults.hiddenMetrics = [.disk, .battery]
        #expect(defaults.hiddenMetrics == [.disk, .battery])
    }
}

@Suite("Rename migration")
struct MigrationTests {
    private func suite() -> UserDefaults {
        let name = "MYUStatsTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: name)!
        defaults.removePersistentDomain(forName: name)
        return defaults
    }

    @Test("settings from the NotchStats build carry over once")
    func carriesOver() {
        let legacy: [String: Any] = [
            SettingsKey.edge: "left",
            SettingsKey.autoHide: false,
            SettingsKey.hiddenMetrics: ["cpu", "battery"],
        ]

        let current = suite()
        current.set(0.9, forKey: SettingsKey.hideDelay) // not in the legacy domain, so it stays
        current.migrateLegacySettings(from: legacy)
        current.registerMYUStatsDefaults()

        #expect(current.screenEdge == .left)
        #expect(current.bool(forKey: SettingsKey.autoHide) == false)
        #expect(current.hiddenMetrics == [.cpu, .battery])
        #expect(current.double(forKey: SettingsKey.hideDelay) == 0.9)

        // A second run must not pull legacy values back over later changes.
        current.screenEdge = .top
        current.migrateLegacySettings(from: legacy)
        #expect(current.screenEdge == .top)
    }
}
