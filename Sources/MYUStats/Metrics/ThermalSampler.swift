import Foundation
import SMCKit

struct FanReading: Equatable, Identifiable {
    let index: Int
    let rpm: Double
    let minRPM: Double
    let maxRPM: Double
    /// True when something (MYU STATS' fan helper, or another tool) holds the fan at a fixed speed.
    var isManual = false
    var targetRPM: Double?

    var id: Int { index }
    var isSpinning: Bool { rpm > 0 }
    /// 0 at the fan's minimum speed, 1 at its maximum.
    var fraction: Double { maxRPM > minRPM ? max(0, min(1, (rpm - minRPM) / (maxRPM - minRPM))) : 0 }
}

struct ThermalState: Equatable {
    var cpuAverage: Double?
    var cpuHottest: Double?
    var efficiencyAverage: Double?
    var gpuAverage: Double?
    var battery: Double?
    var fans: [FanReading]

    var headline: Double? { cpuAverage ?? gpuAverage ?? battery }
}

/// Reads temperature sensors and fans from the SMC.
/// Sensor keys differ per chip, so they are discovered by prefix once at launch rather than hard-coded.
final class ThermalSampler {
    private let smc: SMC
    private let performanceKeys: [String]
    private let efficiencyKeys: [String]
    private let gpuKeys: [String]
    private let batteryKeys: [String]
    private let fanCount: Int

    init?() {
        guard let smc = SMC() else { return nil }
        let temperatureKeys = smc.allKeys().filter { $0.hasPrefix("T") }
        // Apple silicon: Tp = performance cores, Te = efficiency cores, Tg = GPU. Intel: TC*/TG*.
        var performance = temperatureKeys.filter { $0.hasPrefix("Tp") }
        if performance.isEmpty { performance = temperatureKeys.filter { $0.hasPrefix("TC") && $0.hasSuffix("C") } }
        var gpu = temperatureKeys.filter { $0.hasPrefix("Tg") }
        if gpu.isEmpty { gpu = temperatureKeys.filter { $0.hasPrefix("TG") } }

        self.smc = smc
        performanceKeys = performance
        efficiencyKeys = temperatureKeys.filter { $0.hasPrefix("Te") }
        gpuKeys = gpu
        batteryKeys = temperatureKeys.filter { $0.hasPrefix("TB") && $0.hasSuffix("T") }
        fanCount = Int(smc.number("FNum") ?? 0)

        if performanceKeys.isEmpty && gpuKeys.isEmpty && fanCount == 0 { return nil }
    }

    func sample() -> ThermalState {
        let performance = readings(performanceKeys)
        let efficiency = readings(efficiencyKeys)
        return ThermalState(
            cpuAverage: Self.average(performance + efficiency),
            cpuHottest: (performance + efficiency).max(),
            efficiencyAverage: Self.average(efficiency),
            gpuAverage: Self.average(readings(gpuKeys)),
            battery: Self.average(readings(batteryKeys)),
            fans: (0..<fanCount).compactMap(fan)
        )
    }

    private func fan(_ index: Int) -> FanReading? {
        guard let rpm = smc.number("F\(index)Ac") else { return nil }
        let isManual = (smc.number("F\(index)Md") ?? 0) == 1
        return FanReading(
            index: index,
            rpm: max(0, rpm),
            minRPM: smc.number("F\(index)Mn") ?? 0,
            maxRPM: smc.number("F\(index)Mx") ?? 0,
            isManual: isManual,
            targetRPM: isManual ? smc.number("F\(index)Tg") : nil
        )
    }

    // Unused or sleeping sensors report 0 or nonsense; keep plausible die temperatures only.
    private func readings(_ keys: [String]) -> [Double] {
        keys.compactMap { smc.number($0) }.filter { $0 > 10 && $0 < 125 }
    }

    private static func average(_ values: [Double]) -> Double? {
        values.isEmpty ? nil : values.reduce(0, +) / Double(values.count)
    }
}
