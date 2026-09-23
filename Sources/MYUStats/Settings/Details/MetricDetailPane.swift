import SwiftUI

/// Full page for one metric in Settings — what the hover card shows, plus what does not fit in it.
struct MetricDetailPane: View {
    let metric: StatMetric
    var store: StatsStore
    @AppStorage(SettingsKey.showProcesses) private var showProcesses = true

    private var needsProcesses: Bool { (metric == .cpu || metric == .memory) && showProcesses }

    var body: some View {
        Group {
            switch metric {
            case .cpu: CPUDetail(store: store, showProcesses: showProcesses)
            case .memory: MemoryDetail(store: store, showProcesses: showProcesses)
            case .network: NetworkDetail(store: store)
            case .disk: DiskDetail(store: store, storage: store.storage)
            case .thermals: ThermalsDetail(store: store, fanController: store.fans)
            case .battery: BatteryDetail(store: store)
            }
        }
        .onAppear { store.setProcessSampling(needsProcesses, for: "details") }
        .onChange(of: showProcesses) { _, _ in store.setProcessSampling(needsProcesses, for: "details") }
        .onDisappear {
            store.setProcessSampling(false, for: "details")
            store.storage.cancel()
        }
    }
}

// MARK: - Shared rows

/// Chart row with the window's time span underneath.
struct HistoryRow: View {
    var values: [Double]
    var color: Color
    var ceiling: Double?
    var duration: TimeInterval

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Sparkline(values: values, color: color, ceiling: ceiling)
                .frame(height: 64)
            Text("Last \(Int(duration)) seconds")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(.vertical, 4)
    }
}

struct MeterRow: View {
    var label: String
    var value: String
    var fraction: Double
    var color: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            LabeledContent(label) { Text(value).monospacedDigit() }
            BarGauge(fraction: fraction, color: color)
        }
        .padding(.vertical, 2)
    }
}

struct ProcessSection: View {
    var title: String
    var processes: [ProcessUsage]
    var value: (ProcessUsage) -> String

    var body: some View {
        Section {
            if processes.isEmpty {
                Text("Measuring…").foregroundStyle(.secondary)
            } else {
                ForEach(processes) { process in
                    LabeledContent {
                        Text(value(process)).monospacedDigit()
                    } label: {
                        HStack(spacing: 6) {
                            Text(process.name).lineLimit(1).truncationMode(.tail)
                            Text(verbatim: "\(process.pid)")
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(.tertiary)
                        }
                    }
                }
            }
        } header: {
            Text(title)
        } footer: {
            Text("Your own processes only; system daemons need administrator rights to read.")
        }
    }
}
