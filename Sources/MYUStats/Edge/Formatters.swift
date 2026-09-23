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

    /// Compact form for the narrow wings: "840K", "12M", "1.2G". A trickle below 1 KB/s reads "<1K".
    static func shortRate(_ bytesPerSecond: Double) -> String {
        let units: [(Double, String)] = [(1e9, "G"), (1e6, "M"), (1e3, "K")]
        for (scale, suffix) in units where bytesPerSecond >= scale {
            let value = bytesPerSecond / scale
            // One decimal below 10, unless it would round up to "10.0".
            if (value * 10).rounded() < 100 { return String(format: "%.1f%@", value, suffix) }
            return "\(Int(value.rounded()))\(suffix)"
        }
        return bytesPerSecond > 0 ? "<1K" : "0K"
    }

    static func temperature(_ celsius: Double?) -> String {
        celsius.map { String(format: "%.0f °C", $0) } ?? "–"
    }

    /// Plain digits, no grouping separator, so the narrow card columns stay compact.
    static func rpm(_ value: Double) -> String {
        String(localized: "\(plain(value)) rpm")
    }

    static func rpmRange(_ low: Double, _ high: Double) -> String {
        String(localized: "\(plain(low))–\(plain(high)) rpm")
    }

    private static func plain(_ value: Double) -> String {
        String(Int(value.rounded()))
    }

    static func duration(minutes: Int) -> String {
        let hours = minutes / 60
        return hours > 0 ? hoursMinutes(hours, minutes % 60) : String(localized: "\(minutes)m", comment: "Minutes, abbreviated")
    }

    static func uptime(_ seconds: TimeInterval) -> String {
        let minutes = Int(seconds) / 60
        let days = minutes / (60 * 24)
        let hours = (minutes / 60) % 24
        return days > 0
            ? String(localized: "\(days)d \(hours)h", comment: "Days and hours, abbreviated")
            : hoursMinutes(hours, minutes % 60)
    }

    private static func hoursMinutes(_ hours: Int, _ minutes: Int) -> String {
        String(localized: "\(hours)h \(minutes)m", comment: "Hours and minutes, abbreviated")
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
