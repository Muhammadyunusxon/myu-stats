import AppKit
import SwiftUI

struct DiskDetail: View {
    var store: StatsStore
    var storage: StorageAnalyzer
    @State private var volumes: [DiskUsage] = []
    @State private var showAll = false

    private static let collapsedCount = 20

    var body: some View {
        if let disk = store.disk {
            Section("Startup disk") {
                MeterRow(label: disk.name,
                         value: String(localized: "\(Format.diskBytes(disk.used)) of \(Format.diskBytes(disk.total))"),
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
                        Text(([String(localized: "Overview")] + storage.trail.map(\.name)).joined(separator: "  ›  "))
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
                    Button(showAll ? String(localized: "Show fewer") : String(localized: "Show all \(storage.items.count) items")) { showAll.toggle() }
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
                             value: String(localized: "\(Format.diskBytes(volume.free)) free of \(Format.diskBytes(volume.total))"),
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
        if rest > 0 { result.append(Segment(name: String(localized: "Everything else"), size: rest, color: .gray)) }
        let unattributed = used - measured.compactMap(\.size).reduce(0, +)
        if unattributed > 0 {
            result.append(Segment(name: String(localized: "System & other"), size: unattributed, color: Color(white: 0.3)))
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
