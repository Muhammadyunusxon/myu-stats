import SwiftUI

struct MemoryDetail: View {
    var store: StatsStore
    var showProcesses: Bool

    var body: some View {
        let memory = store.memory
        let color = Level.color(for: memory.pressure)
        Section("Usage") {
            MeterRow(label: String(localized: "Used"), value: String(localized: "\(Format.bytes(memory.used)) of \(Format.bytes(memory.total))"),
                     fraction: memory.fraction, color: color)
            LabeledContent("Available", value: Format.bytes(memory.available))
            LabeledContent("Pressure") {
                Text(memory.pressure.title).foregroundStyle(color)
            }
            HistoryRow(values: store.memoryHistory, color: color, ceiling: 1, duration: store.historyDuration)
        }
        Section("Breakdown") {
            LabeledContent("App memory", value: Format.bytes(memory.app))
            LabeledContent("Wired", value: Format.bytes(memory.wired))
            LabeledContent("Compressed", value: Format.bytes(memory.compressed))
            LabeledContent("Cached files", value: Format.bytes(memory.cached))
            LabeledContent("Swap used") {
                Text(Format.bytes(memory.swapUsed))
                    .foregroundStyle(memory.swapUsed > 1 << 30 ? Level.color(for: 0.7) : .secondary)
            }
        }
        if showProcesses {
            ProcessSection(title: String(localized: "Top processes by memory"), processes: store.topByMemory) { Format.bytes($0.memory) }
        }
    }
}
