import AppKit
import SMCKit
import SwiftUI

/// Full page for one metric in Settings — what the hover card shows, plus what does not fit in it.
struct MetricDetailPane: View {
    let metric: StatMetric
    @ObservedObject var store: StatsStore
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
private struct HistoryRow: View {
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

private struct MeterRow: View {
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

private struct ProcessSection: View {
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
                            Text("\(process.pid)")
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

// MARK: - CPU

private struct CPUDetail: View {
    @ObservedObject var store: StatsStore
    var showProcesses: Bool

    var body: some View {
        let cpu = store.cpu
        let info = store.cpuInfo
        Section("Usage") {
            MeterRow(label: "Total", value: Format.percent(cpu.total), fraction: cpu.total,
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
            LabeledContent("Cores", value: "\(info.coresDescription) · \(info.logicalCores) threads")
            LabeledContent("Load average (1 · 5 · 15 min)",
                           value: store.loadAverage.map { String(format: "%.2f", $0) }.joined(separator: " · "))
            LabeledContent("Uptime", value: Format.uptime(ProcessInfo.processInfo.systemUptime))
        }
        if showProcesses {
            ProcessSection(title: "Top processes by CPU", processes: store.topByCPU) { Format.percent($0.cpu) }
        }
    }

    private func coreName(_ index: Int, info: CPUInfo) -> String {
        guard info.efficiencyCores > 0 else { return "Core \(index + 1)" }
        return index < info.efficiencyCores
            ? "Efficiency \(index + 1)"
            : "Performance \(index - info.efficiencyCores + 1)"
    }
}

// MARK: - Memory

private struct MemoryDetail: View {
    @ObservedObject var store: StatsStore
    var showProcesses: Bool

    var body: some View {
        let memory = store.memory
        let color = Level.color(for: memory.pressure)
        Section("Usage") {
            MeterRow(label: "Used", value: "\(Format.bytes(memory.used)) of \(Format.bytes(memory.total))",
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
            ProcessSection(title: "Top processes by memory", processes: store.topByMemory) { Format.bytes($0.memory) }
        }
    }
}

// MARK: - Network

private struct NetworkDetail: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        Section("Now") {
            LabeledContent("Download", value: Format.rate(store.network.downBytesPerSecond))
            LabeledContent("Upload", value: Format.rate(store.network.upBytesPerSecond))
            LabeledContent("Peak", value: Format.rate(store.networkPeak))
            HistoryRow(values: store.networkHistory, color: Level.accent, ceiling: nil,
                       duration: store.historyDuration)
        }
        Section("Connection") {
            LabeledContent("Interface", value: store.networkInterface?.name ?? "Offline")
            LabeledContent("IPv4 address", value: store.networkInterface?.address ?? "–")
        }
        Section {
            LabeledContent("Received", value: Format.bytes(store.networkTotals.received))
            LabeledContent("Sent", value: Format.bytes(store.networkTotals.sent))
        } header: {
            Text("Since boot")
        } footer: {
            Text("Wi-Fi, Ethernet and cellular only; VPN tunnels are left out so traffic is not counted twice.")
        }
    }
}

// MARK: - Disk

private struct DiskDetail: View {
    @ObservedObject var store: StatsStore
    @ObservedObject var storage: StorageAnalyzer
    @State private var volumes: [DiskUsage] = []
    @State private var showAll = false

    private static let collapsedCount = 20

    var body: some View {
        if let disk = store.disk {
            Section("Startup disk") {
                MeterRow(label: disk.name,
                         value: "\(Format.diskBytes(disk.used)) of \(Format.diskBytes(disk.total))",
                         fraction: disk.usedFraction, color: Level.color(for: disk.usedFraction))
                LabeledContent("Free", value: Format.diskBytes(disk.free))
                if storage.hasScanned, storage.trail.isEmpty, storage.measuredTotal > 0 {
                    CompositionBar(items: storage.items, used: disk.used)
                }
                if disk.usedFraction >= 0.9 {
                    Label("Low disk space — macOS may slow down and updates may fail.",
                          systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(Level.color(for: disk.usedFraction))
                }
            }
        }

        Section {
            if !storage.hasScanned {
                HStack {
                    Text("Find out which folders take the most space.")
                    Spacer()
                    Button("Analyze") { storage.scanOverview() }
                }
            } else {
                if !storage.trail.isEmpty {
                    HStack(spacing: 8) {
                        Button(action: storage.back) { Image(systemName: "chevron.left") }
                            .buttonStyle(SettingsIconButtonStyle())
                            .help("Back")
                        Text((["Overview"] + storage.trail.map(\.name)).joined(separator: "  ›  "))
                            .font(.callout)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .truncationMode(.head)
                    }
                }
                let reference = storage.items.compactMap(\.size).max() ?? 1
                let visible = showAll ? storage.items : Array(storage.items.prefix(Self.collapsedCount))
                if visible.isEmpty {
                    Text("This folder is empty or could not be read.").foregroundStyle(.secondary)
                }
                ForEach(visible) { item in
                    StorageRow(item: item, reference: reference,
                               onOpen: item.isDirectory ? { storage.open(item) } : nil,
                               onReveal: { storage.reveal(item) })
                }
                if storage.items.count > Self.collapsedCount {
                    Button(showAll ? "Show fewer" : "Show all \(storage.items.count) items") { showAll.toggle() }
                }
            }
        } header: {
            HStack {
                Text("What's using space")
                Spacer()
                if storage.isScanning {
                    ProgressView().controlSize(.small)
                }
                if storage.hasScanned {
                    Button("Rescan") { storage.rescan() }
                        .disabled(storage.isScanning)
                }
            }
        } footer: {
            Text("Measured file by file, so large folders take a moment. macOS may ask MYU STATS for access to protected folders such as Documents, Downloads or Desktop. Clones and hard links count once per path, so totals are approximate.")
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }

        if storage.hasScanned, !storage.developerCaches.isEmpty {
            Section {
                let reference = storage.developerCaches.compactMap(\.size).max() ?? 1
                ForEach(storage.developerCaches) { item in
                    StorageRow(item: item, reference: reference, onOpen: nil, onReveal: { storage.reveal(item) })
                }
            } header: {
                Text("Developer and app caches")
            } footer: {
                Text("All of these are rebuilt when needed. Clear them with the command shown, or delete the folder in Finder.")
            }
        }

        Section {
            if volumes.isEmpty {
                Text("No other volumes mounted.").foregroundStyle(.secondary)
            } else {
                ForEach(volumes, id: \.name) { volume in
                    MeterRow(label: volume.name,
                             value: "\(Format.diskBytes(volume.free)) free of \(Format.diskBytes(volume.total))",
                             fraction: volume.usedFraction, color: Level.color(for: volume.usedFraction))
                }
            }
        } header: {
            Text("Other volumes")
        }
        .onAppear { volumes = Self.otherVolumes() }
    }

    /// Mounted, visible volumes other than the startup disk.
    private static func otherVolumes() -> [DiskUsage] {
        let keys: [URLResourceKey] = [
            .volumeLocalizedNameKey, .volumeTotalCapacityKey, .volumeAvailableCapacityKey, .volumeIsRootFileSystemKey,
        ]
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys,
                                                         options: [.skipHiddenVolumes]) ?? []
        return urls.compactMap { url in
            guard let values = try? url.resourceValues(forKeys: Set(keys)),
                  values.volumeIsRootFileSystem != true,
                  let total = values.volumeTotalCapacity, total > 0,
                  let free = values.volumeAvailableCapacity
            else { return nil }
            return DiskUsage(name: values.volumeLocalizedName ?? url.lastPathComponent,
                             free: Int64(free), total: Int64(total))
        }
    }
}

/// One file or folder with its size, a bar relative to the largest sibling, and actions.
private struct StorageRow: View {
    let item: StorageItem
    let reference: Int64
    let onOpen: (() -> Void)?
    let onReveal: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: item.url.path))
                .resizable()
                .frame(width: 18, height: 18)
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(item.name).lineLimit(1).truncationMode(.middle)
                    Spacer()
                    if let size = item.size {
                        Text(Format.diskBytes(size)).monospacedDigit().foregroundStyle(.secondary)
                    } else {
                        ProgressView().controlSize(.mini)
                    }
                }
                if let note = item.note {
                    Text(note).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
                BarGauge(fraction: Double(item.size ?? 0) / Double(max(reference, 1)), color: Level.accent)
            }
            Button(action: onReveal) { Image(systemName: "magnifyingglass") }
                .buttonStyle(SettingsIconButtonStyle())
                .help("Show in Finder")
            if let onOpen {
                Button(action: onOpen) { Image(systemName: "chevron.right") }
                    .buttonStyle(SettingsIconButtonStyle())
                    .help("Look inside")
            } else {
                Color.clear.frame(width: 22, height: 22)
            }
        }
        .padding(.vertical, 2)
    }
}

/// Used space split into the biggest items, the rest of what was measured, and what could not be
/// attributed (macOS itself, other users, snapshots, purgeable space).
private struct CompositionBar: View {
    let items: [StorageItem]
    let used: Int64

    private static let colors: [Color] = [.blue, .purple, .pink, .orange, .teal]

    private struct Segment: Identifiable {
        let name: String
        let size: Int64
        let color: Color
        var id: String { name }
    }

    private var segments: [Segment] {
        let measured = items.filter { ($0.size ?? 0) > 0 }
        let top = measured.prefix(Self.colors.count)
        var result = zip(top, Self.colors).map { Segment(name: $0.name, size: $0.size ?? 0, color: $1) }
        let rest = measured.dropFirst(Self.colors.count).compactMap(\.size).reduce(0, +)
        if rest > 0 { result.append(Segment(name: "Everything else", size: rest, color: .gray)) }
        let unattributed = used - measured.compactMap(\.size).reduce(0, +)
        if unattributed > 0 {
            result.append(Segment(name: "System & other", size: unattributed, color: Color(white: 0.3)))
        }
        return result
    }

    var body: some View {
        let segments = self.segments
        let total = max(Double(segments.map(\.size).reduce(0, +)), 1)
        VStack(alignment: .leading, spacing: 8) {
            GeometryReader { proxy in
                HStack(spacing: 1.5) {
                    ForEach(segments) { segment in
                        segment.color
                            .frame(width: max(2, proxy.size.width * Double(segment.size) / total))
                    }
                }
                .clipShape(Capsule())
            }
            .frame(height: 10)
            FlowLegend(segments: segments.map { ($0.name, Format.diskBytes($0.size), $0.color) })
        }
        .padding(.vertical, 4)
    }
}

private struct FlowLegend: View {
    let segments: [(name: String, size: String, color: Color)]

    var body: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), alignment: .leading)], alignment: .leading, spacing: 4) {
            ForEach(segments, id: \.name) { segment in
                HStack(spacing: 5) {
                    Circle().fill(segment.color).frame(width: 7, height: 7)
                    Text(segment.name).lineLimit(1)
                    Text(segment.size).foregroundStyle(.secondary).monospacedDigit()
                }
                .font(.caption)
            }
        }
    }
}

// MARK: - Thermals

private struct ThermalsDetail: View {
    @ObservedObject var store: StatsStore
    @ObservedObject var fanController: FanController

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
                        MeterRow(label: "Fan \(fan.index + 1)",
                                 value: fan.isSpinning ? Format.rpm(fan.rpm) : "Off",
                                 fraction: fan.fraction, color: Level.accent)
                        LabeledContent("Mode", value: fan.isManual
                            ? "Manual" + (fan.targetRPM.map { " · target \(Format.rpm($0))" } ?? "")
                            : "Automatic")
                        LabeledContent("Range", value: "\(Int(fan.minRPM))–\(Int(fan.maxRPM)) rpm")
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

    private func row(_ label: String, _ value: Double?) -> some View {
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
            case .automatic: "Auto"
            case .manual: "Manual"
            case .maximum: "Max"
            }
        }
    }

    let fans: [FanReading]
    @ObservedObject var controller: FanController
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
        } header: {
            Text("Fan control")
        } footer: {
            Text("Each change asks for your password or Touch ID. Manual speeds stay until you switch back to Auto, the Mac restarts, or you quit MYU STATS (it offers to restore Auto). Speeds cannot go below a fan's minimum, and macOS still throttles the chip if it runs hot.")
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
            Text(fans.contains(where: \.isManual) ? "Fans are under manual control." : "Fans are automatic.")
                .foregroundStyle(.secondary)
        case .applying:
            HStack(spacing: 6) {
                ProgressView().controlSize(.small)
                Text("Waiting for permission…").foregroundStyle(.secondary)
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

// MARK: - Battery

private struct BatteryDetail: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        if let battery = store.battery {
            let color = Level.batteryColor(battery)
            Section("Charge") {
                MeterRow(label: "Level", value: "\(battery.percent)%",
                         fraction: Double(battery.percent) / 100, color: color)
                LabeledContent("Status", value: status(battery))
                if let minutes = battery.minutesRemaining {
                    LabeledContent(battery.isCharging ? "Full in" : "Remaining",
                                   value: Format.duration(minutes: minutes))
                }
                LabeledContent("Power source", value: battery.isOnAC
                    ? (battery.adapterWatts.map { "Power adapter · \($0) W" } ?? "Power adapter")
                    : "Battery")
            }
            Section("Health") {
                if let health = battery.healthPercent {
                    MeterRow(label: "Maximum capacity", value: "\(health)%", fraction: Double(health) / 100,
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
        if battery.isCharging { return "Charging" }
        return battery.isOnAC ? "Not charging" : "On battery"
    }
}
