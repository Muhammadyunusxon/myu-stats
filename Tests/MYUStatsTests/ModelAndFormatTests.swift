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
        #expect(Format.shortRate(999) == "0K")
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

    @Test func metricOrderKeepsUserOrderAndAppendsNewMetrics() {
        let defaults = freshDefaults()
        // As if saved by a version that did not know about thermals yet.
        defaults.set(["battery", "cpu", "memory", "network", "disk"], forKey: SettingsKey.metricOrder)
        #expect(defaults.metricOrder == [.battery, .cpu, .memory, .network, .disk, .thermals])
    }

    @Test func metricOrderDropsUnknownNames() {
        let defaults = freshDefaults()
        defaults.set(["gpu", "cpu"], forKey: SettingsKey.metricOrder)
        #expect(defaults.metricOrder.first == .cpu)
        #expect(defaults.metricOrder.count == StatMetric.allCases.count)
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
