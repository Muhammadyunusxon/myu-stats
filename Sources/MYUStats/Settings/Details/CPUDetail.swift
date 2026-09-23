import SwiftUI

struct CPUDetail: View {
    var store: StatsStore
    var showProcesses: Bool

    var body: some View {
        let cpu = store.cpu
        let info = store.cpuInfo
        Section("Usage") {
            MeterRow(label: String(localized: "Total"), value: Format.percent(cpu.total), fraction: cpu.total,
                     color: Level.color(for: cpu.total))
            LabeledContent("User", value: Format.percent(cpu.user))
            LabeledContent("System", value: Format.percent(cpu.system))
            LabeledContent("Idle", value: Format.percent(cpu.idle))
            HistoryRow(values: store.cpuHistory, color: Level.color(for: cpu.total), ceiling: 1,
                       duration: store.historyDuration)
        }
        Section {
            ForEach(Array(store.perCore.enumerated()), id: \.offset) { index, load in
                MeterRow(label: coreName(index, info: info), value: Format.percent(load), fraction: load,
                         color: Level.color(for: load))
            }
        } header: {
            Text("Cores")
        } footer: {
            Text("Efficiency cores are listed first on Apple silicon.")
        }
        Section("System") {
            LabeledContent("Processor", value: info.brand)
            LabeledContent("Cores", value: String(localized: "\(info.coresDescription) · \(info.logicalCores) threads"))
            LabeledContent("Load average (1 · 5 · 15 min)",
                           value: store.loadAverage.map { String(format: "%.2f", $0) }.joined(separator: " · "))
            LabeledContent("Uptime", value: Format.uptime(ProcessInfo.processInfo.systemUptime))
        }
        if showProcesses {
            ProcessSection(title: String(localized: "Top processes by CPU"), processes: store.topByCPU) { Format.percent($0.cpu) }
        }
    }

    private func coreName(_ index: Int, info: CPUInfo) -> String {
        guard info.efficiencyCores > 0 else { return String(localized: "Core \(index + 1)") }
        return index < info.efficiencyCores
            ? String(localized: "Efficiency \(index + 1)")
            : String(localized: "Performance \(index - info.efficiencyCores + 1)")
    }
}
