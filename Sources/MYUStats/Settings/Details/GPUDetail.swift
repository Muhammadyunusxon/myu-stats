import SwiftUI

struct GPUDetail: View {
    var store: StatsStore

    var body: some View {
        if let busiest = store.gpu {
            Section("Usage") {
                MeterRow(label: String(localized: "Total"), value: Format.percent(busiest.utilization),
                         fraction: busiest.utilization, color: Level.color(for: busiest.utilization))
                if let renderer = busiest.renderer {
                    LabeledContent("Renderer", value: Format.percent(renderer))
                }
                if let tiler = busiest.tiler {
                    LabeledContent("Tiler", value: Format.percent(tiler))
                }
                if let celsius = store.thermals?.gpuAverage {
                    LabeledContent("Temperature") {
                        Text(Format.temperature(celsius)).monospacedDigit().foregroundStyle(Level.temperatureColor(celsius))
                    }
                }
                HistoryRow(values: store.gpuHistory, color: Level.color(for: busiest.utilization), ceiling: 1,
                           duration: store.historyDuration)
            }
            ForEach(store.gpus) { gpu in
                Section {
                    if store.gpus.count > 1 {
                        MeterRow(label: String(localized: "Usage"), value: Format.percent(gpu.utilization),
                                 fraction: gpu.utilization, color: Level.color(for: gpu.utilization))
                    }
                    if let cores = gpu.cores {
                        LabeledContent("Cores", value: "\(cores)")
                    }
                    if let used = gpu.memoryUsed, let total = gpu.memoryTotal, total > 0 {
                        MeterRow(label: String(localized: "Video memory"), value: GPUText.memory(used: used, total: total),
                                 fraction: Double(used) / Double(total), color: Level.accent)
                    } else if let used = gpu.memoryUsed {
                        LabeledContent("Memory in use", value: Format.bytes(used))
                    }
                } header: {
                    Text(verbatim: gpu.name)
                } footer: {
                    if gpu.memoryTotal == nil, gpu.memoryUsed != nil {
                        Text("The GPU shares the Mac's unified memory, so what it uses counts toward Memory too.")
                    }
                }
            }
        } else {
            Section {
                Text("No GPU statistics could be read on this Mac.").foregroundStyle(.secondary)
            }
        }
    }
}
