import Foundation

struct CPUUsage: Equatable {
    var user: Double
    var system: Double
    var total: Double { user + system }
    var idle: Double { max(0, 1 - total) }

    static let zero = CPUUsage(user: 0, system: 0)
}

/// Static facts about the processor, read once at launch.
struct CPUInfo {
    let brand: String
    let performanceCores: Int
    let efficiencyCores: Int
    let logicalCores: Int

    static let current = CPUInfo(
        brand: sysctlString("machdep.cpu.brand_string") ?? "Unknown CPU",
        performanceCores: sysctlInt("hw.perflevel0.physicalcpu") ?? 0,
        efficiencyCores: sysctlInt("hw.perflevel1.physicalcpu") ?? 0,
        logicalCores: ProcessInfo.processInfo.activeProcessorCount
    )

    var coresDescription: String {
        performanceCores > 0 && efficiencyCores > 0
            ? "\(performanceCores)P + \(efficiencyCores)E"
            : "\(logicalCores) cores"
    }
}

enum MemoryPressure: Int {
    case normal = 1
    case warning = 2
    case critical = 4

    var title: String {
        switch self {
        case .normal: "Normal"
        case .warning: "Warning"
        case .critical: "Critical"
        }
    }
}

struct MemoryUsage: Equatable {
    var used: UInt64
    var total: UInt64
    var app: UInt64
    var wired: UInt64
    var compressed: UInt64
    var cached: UInt64
    var swapUsed: UInt64
    var pressure: MemoryPressure

    var fraction: Double { total == 0 ? 0 : Double(used) / Double(total) }
    var available: UInt64 { total > used ? total - used : 0 }

    static let zero = MemoryUsage(
        used: 0, total: 0, app: 0, wired: 0, compressed: 0, cached: 0, swapUsed: 0, pressure: .normal
    )
}

struct DiskUsage: Equatable {
    var name: String
    var free: Int64
    var total: Int64

    var used: Int64 { max(0, total - free) }
    var usedFraction: Double { total == 0 ? 0 : Double(used) / Double(total) }
}

struct NetworkRate: Equatable {
    var downBytesPerSecond: Double
    var upBytesPerSecond: Double
    var total: Double { downBytesPerSecond + upBytesPerSecond }

    static let zero = NetworkRate(downBytesPerSecond: 0, upBytesPerSecond: 0)
}

struct NetworkTotals: Equatable {
    var received: UInt64
    var sent: UInt64
}

struct NetworkInterface: Equatable {
    var name: String
    var address: String
}

struct BatteryState: Equatable {
    var percent: Int
    var isCharging: Bool
    var isOnAC: Bool
    /// Minutes until empty or full, when macOS has an estimate.
    var minutesRemaining: Int?
    var cycleCount: Int?
    var healthPercent: Int?
    var temperatureCelsius: Double?
    var adapterWatts: Int?
}

struct ProcessUsage: Identifiable, Equatable {
    let pid: Int32
    let name: String
    /// Share of one core, like Activity Monitor (can exceed 1).
    let cpu: Double
    let memory: UInt64

    var id: Int32 { pid }
}

func sysctlString(_ name: String) -> String? {
    var size = 0
    guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return nil }
    var buffer = [CChar](repeating: 0, count: size)
    guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else { return nil }
    return String(cString: buffer)
}

func sysctlInt(_ name: String) -> Int? {
    var value: Int32 = 0
    var size = MemoryLayout<Int32>.size
    guard sysctlbyname(name, &value, &size, nil, 0) == 0 else { return nil }
    return Int(value)
}
