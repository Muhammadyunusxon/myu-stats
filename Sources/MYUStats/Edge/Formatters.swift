import Foundation
import SwiftUI

enum Format {
    static func percent(_ fraction: Double) -> String {
        "\(Int((fraction * 100).rounded()))%"
    }

    static func bytes(_ value: UInt64) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(clamping: value), countStyle: .memory)
    }

    static func diskBytes(_ value: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: value, countStyle: .file)
    }

    static func rate(_ bytesPerSecond: Double) -> String {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        formatter.allowedUnits = [.useKB, .useMB, .useGB]
        formatter.zeroPadsFractionDigits = false
        return formatter.string(fromByteCount: Int64(bytesPerSecond)) + "/s"
    }

    /// Compact form for the narrow wings: "840K", "12M", "1.2G".
    static func shortRate(_ bytesPerSecond: Double) -> String {
        let units: [(Double, String)] = [(1e9, "G"), (1e6, "M"), (1e3, "K")]
        for (scale, suffix) in units where bytesPerSecond >= scale {
            let value = bytesPerSecond / scale
            return value < 10 ? String(format: "%.1f%@", value, suffix) : "\(Int(value))\(suffix)"
        }
        return "0K"
    }

    static func temperature(_ celsius: Double?) -> String {
        celsius.map { String(format: "%.0f °C", $0) } ?? "–"
    }

    static func rpm(_ value: Double) -> String {
        "\(Int(value.rounded())) rpm"
    }

    static func duration(minutes: Int) -> String {
        let hours = minutes / 60
        return hours > 0 ? "\(hours)h \(minutes % 60)m" : "\(minutes)m"
    }

    static func uptime(_ seconds: TimeInterval) -> String {
        let minutes = Int(seconds) / 60
        let days = minutes / (60 * 24)
        let hours = (minutes / 60) % 24
        return days > 0 ? "\(days)d \(hours)h" : "\(hours)h \(minutes % 60)m"
    }
}

enum Level {
    /// Green, amber, red — for anything where higher is worse.
    static func color(for fraction: Double) -> Color {
        switch fraction {
        case ..<0.6: Color(red: 0.36, green: 0.84, blue: 0.52)
        case ..<0.85: Color(red: 1.0, green: 0.76, blue: 0.28)
        default: Color(red: 1.0, green: 0.38, blue: 0.36)
        }
    }

    static func color(for pressure: MemoryPressure) -> Color {
        switch pressure {
        case .normal: color(for: 0)
        case .warning: color(for: 0.7)
        case .critical: color(for: 1)
        }
    }

    static func batteryColor(_ battery: BatteryState) -> Color {
        if battery.isCharging || battery.isOnAC { return color(for: 0) }
        return color(for: 1 - Double(battery.percent) / 100)
    }

    static let accent = Color(red: 0.42, green: 0.66, blue: 1.0)

    /// Apple silicon idles around 40–55 °C and throttles in the high 90s.
    static func temperatureColor(_ celsius: Double) -> Color {
        switch celsius {
        case ..<70: color(for: 0)
        case ..<90: color(for: 0.7)
        default: color(for: 1)
        }
    }
}
