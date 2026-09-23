import SwiftUI

struct BatteryDetail: View {
    var store: StatsStore

    var body: some View {
        if let battery = store.battery {
            let color = Level.batteryColor(battery)
            Section("Charge") {
                MeterRow(label: String(localized: "Level"), value: "\(battery.percent)%",
                         fraction: Double(battery.percent) / 100, color: color)
                LabeledContent("Status", value: status(battery))
                if let minutes = battery.minutesRemaining {
                    LabeledContent(battery.isCharging ? String(localized: "Full in") : String(localized: "Remaining"),
                                   value: Format.duration(minutes: minutes))
                }
                LabeledContent("Power source", value: battery.isOnAC
                    ? (battery.adapterWatts.map { String(localized: "Power adapter · \($0) W") } ?? String(localized: "Power adapter"))
                    : String(localized: "Battery"))
            }
            Section("Health") {
                if let health = battery.healthPercent {
                    MeterRow(label: String(localized: "Maximum capacity"), value: "\(health)%", fraction: Double(health) / 100,
                             color: health < 80 ? Level.color(for: 0.7) : Level.color(for: 0))
                }
                if let cycles = battery.cycleCount {
                    LabeledContent("Cycle count", value: "\(cycles)")
                }
                LabeledContent("Temperature",
                               value: Format.temperature(battery.temperatureCelsius ?? store.thermals?.battery))
            }
        } else {
            Section {
                Text("This Mac has no battery.").foregroundStyle(.secondary)
            }
        }
    }

    private func status(_ battery: BatteryState) -> String {
        if battery.isCharging { return String(localized: "Charging") }
        return battery.isOnAC ? String(localized: "Not charging") : String(localized: "On battery")
    }
}
