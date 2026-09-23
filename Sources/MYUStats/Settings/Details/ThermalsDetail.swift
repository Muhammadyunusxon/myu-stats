import SMCKit
import SwiftUI

struct ThermalsDetail: View {
    var store: StatsStore
    var fanController: FanController

    var body: some View {
        if let thermals = store.thermals {
            Section("Temperatures") {
                row("CPU average", thermals.cpuAverage)
                row("Hottest core", thermals.cpuHottest)
                row("Efficiency cores", thermals.efficiencyAverage)
                row("GPU", thermals.gpuAverage)
                row("Battery", thermals.battery)
            }
            Section {
                if thermals.fans.isEmpty {
                    Text("This Mac has no fans.").foregroundStyle(.secondary)
                } else {
                    ForEach(thermals.fans) { fan in
                        MeterRow(label: String(localized: "Fan \(fan.index + 1)"),
                                 value: fan.isSpinning ? Format.rpm(fan.rpm) : String(localized: "Off"),
                                 fraction: fan.fraction, color: Level.accent)
                        LabeledContent("Mode", value: fan.isManual
                            ? fan.targetRPM.map { String(localized: "Manual · target \(Format.rpm($0))") }
                                ?? String(localized: "Manual")
                            : String(localized: "Automatic"))
                        LabeledContent("Range", value: Format.rpmRange(fan.minRPM, fan.maxRPM))
                    }
                }
            } header: {
                Text("Fans")
            } footer: {
                Text("Apple silicon keeps its fans off until they are needed. Readings come from the SMC.")
            }
            if !thermals.fans.isEmpty {
                FanControlSection(fans: thermals.fans, controller: fanController)
            }
        } else {
            Section {
                Text("No temperature sensors could be read on this Mac.").foregroundStyle(.secondary)
            }
        }
    }

    private func row(_ label: LocalizedStringKey, _ value: Double?) -> some View {
        LabeledContent(label) {
            Text(Format.temperature(value))
                .monospacedDigit()
                .foregroundStyle(value.map(Level.temperatureColor) ?? .secondary)
        }
    }
}

/// Auto / Manual / Max, applied through the privileged helper after one confirmation.
private struct FanControlSection: View {
    enum Mode: String, CaseIterable, Identifiable {
        case automatic, manual, maximum
        var id: String { rawValue }
        var title: String {
            switch self {
            case .automatic: String(localized: "Auto")
            case .manual: String(localized: "Manual")
            case .maximum: String(localized: "Max")
            }
        }
    }

    let fans: [FanReading]
    var controller: FanController
    @State private var mode: Mode = .automatic
    @State private var rpm: Double = 0
    @State private var fanIndex: Int? = nil
    @State private var didLoad = false

    private var selectedFans: [FanReading] { fanIndex.map { index in fans.filter { $0.index == index } } ?? fans }
    private var range: ClosedRange<Double> {
        let low = selectedFans.map(\.minRPM).max() ?? 0
        let high = selectedFans.map(\.maxRPM).min() ?? low
        return low...max(low, high)
    }

    var body: some View {
        Section {
            Picker("Mode", selection: $mode) {
                ForEach(Mode.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)

            if fans.count > 1 {
                Picker("Fans", selection: $fanIndex) {
                    Text("All fans").tag(Int?.none)
                    ForEach(fans) { Text("Fan \($0.index + 1)").tag(Int?.some($0.index)) }
                }
            }

            if mode == .manual {
                LabeledContent("Speed") {
                    HStack {
                        Slider(value: $rpm, in: range, step: 100)
                        Text(Format.rpm(rpm)).monospacedDigit().frame(width: 72, alignment: .trailing)
                    }
                }
            }

            HStack {
                statusText
                Spacer()
                Button("Apply") { apply() }
                    .buttonStyle(SettingsButtonStyle(prominent: true))
                    .disabled(controller.isApplying || !controller.isAvailable)
            }

            if controller.isHelperInstalled {
                HStack {
                    Label("Fan helper installed; changes need no password.", systemImage: "checkmark.shield")
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button("Remove", role: .destructive) { Task { await controller.removeHelper() } }
                        .disabled(controller.isApplying)
                }
            }
        } header: {
            Text("Fan control")
        } footer: {
            Text("The first change asks for your password or Touch ID once, to install a small helper; after that, changes apply straight away. Manual speeds stay until you switch back to Auto, the Mac restarts, or you quit MYU STATS (it offers to restore Auto). Speeds cannot go below a fan's minimum, and macOS still throttles the chip if it runs hot.")
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .onAppear(perform: loadCurrentState)
        .onChange(of: fanIndex) { _, _ in rpm = min(max(rpm, range.lowerBound), range.upperBound) }
    }

    @ViewBuilder
    private var statusText: some View {
        switch controller.status {
        case .idle:
            Text(fans.contains(where: \.isManual) ? String(localized: "Fans are under manual control.") : String(localized: "Fans are automatic."))
                .foregroundStyle(.secondary)
        case .applying:
            HStack(spacing: 6) {
                ProgressView().controlSize(.small)
                Text(controller.isHelperInstalled ? String(localized: "Applying…") : String(localized: "Waiting for permission…"))
                    .foregroundStyle(.secondary)
            }
        case .applied(let message):
            Label(message, systemImage: "checkmark.circle.fill").foregroundStyle(Level.color(for: 0))
        case .cancelled:
            Text("Cancelled — nothing changed.").foregroundStyle(.secondary)
        case .failed(let message):
            Label(message, systemImage: "exclamationmark.triangle.fill")
                .foregroundStyle(Level.color(for: 1))
                .lineLimit(2)
        }
    }

    private func loadCurrentState() {
        guard !didLoad else { return }
        didLoad = true
        let manual = fans.filter(\.isManual)
        if let target = manual.compactMap(\.targetRPM).first {
            mode = target >= (manual.first?.maxRPM ?? .infinity) ? .maximum : .manual
            rpm = target
        } else {
            mode = .automatic
            rpm = range.lowerBound + (range.upperBound - range.lowerBound) / 2
        }
        rpm = min(max(rpm.rounded(), range.lowerBound), range.upperBound)
    }

    private func apply() {
        let command: FanCommand
        switch mode {
        case .automatic: command = .auto(fan: fanIndex)
        case .manual: command = .manual(fan: fanIndex, rpm: rpm)
        case .maximum: command = .max(fan: fanIndex)
        }
        Task { await controller.apply(command) }
    }
}
