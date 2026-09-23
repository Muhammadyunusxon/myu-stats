import SwiftUI

struct MetricsPane: View {
    var store: StatsStore
    @State private var order = UserDefaults.standard.metricOrder
    @State private var hidden = UserDefaults.standard.hiddenMetrics
    @AppStorage(SettingsKey.sampleInterval) private var interval: Double = 1

    var body: some View {
        Section("Rings") {
            ForEach(Array(order.enumerated()), id: \.element) { index, metric in
                MetricRow(
                    metric: metric,
                    isAvailable: isAvailable(metric),
                    isShown: Binding(
                        get: { !hidden.contains(metric) },
                        set: { setShown(metric, $0) }
                    ),
                    moveUp: index > 0 ? { move(from: index, to: index - 1) } : nil,
                    moveDown: index < order.count - 1 ? { move(from: index, to: index + 1) } : nil
                )
            }
        }
        Section {
            Picker("Update every", selection: $interval) {
                Text("1 s").tag(1.0)
                Text("2 s").tag(2.0)
                Text("5 s").tag(5.0)
            }
            .pickerStyle(.segmented)
            Text("Slower updates use less energy. Charts keep the last \(StatsStore.historyLength) samples.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        } header: {
            Text("Sampling")
        }
    }

    private func isAvailable(_ metric: StatMetric) -> Bool {
        switch metric {
        case .battery: store.battery != nil
        case .thermals: store.thermals != nil
        default: true
        }
    }

    private func setShown(_ metric: StatMetric, _ shown: Bool) {
        if shown {
            hidden.remove(metric)
        } else if hidden.count < StatMetric.allCases.count - 1 {
            hidden.insert(metric)
        }
        UserDefaults.standard.hiddenMetrics = hidden
    }

    private func move(from source: Int, to destination: Int) {
        withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { order.swapAt(source, destination) }
        UserDefaults.standard.metricOrder = order
    }
}

private struct MetricRow: View {
    var metric: StatMetric
    var isAvailable: Bool
    @Binding var isShown: Bool
    var moveUp: (() -> Void)?
    var moveDown: (() -> Void)?

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: metric.symbol)
                .font(.system(size: 12))
                .foregroundStyle(.white.opacity(isAvailable ? 0.85 : 0.35))
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 1) {
                Text(metric.title)
                if !isAvailable {
                    Text("Not available on this Mac").font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            Button(action: { moveUp?() }) { Image(systemName: "chevron.up") }
                .buttonStyle(SettingsIconButtonStyle())
                .disabled(moveUp == nil)
                .help("Move up")
            Button(action: { moveDown?() }) { Image(systemName: "chevron.down") }
                .buttonStyle(SettingsIconButtonStyle())
                .disabled(moveDown == nil)
                .help("Move down")
            Toggle(metric.title, isOn: $isShown)
                .labelsHidden()
                .toggleStyle(.switch)
                .controlSize(.small)
        }
    }
}
