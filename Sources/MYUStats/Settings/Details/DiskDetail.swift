import AppKit
import SwiftUI

struct DiskDetail: View {
    var store: StatsStore
    var storage: StorageAnalyzer
    @State private var volumes: [DiskUsage] = []
    @State private var showAll = false
    /// A clean-up waiting for the user to confirm it.
    @State private var confirming: CleanRequest?

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
                               onReveal: { storage.reveal(item.url) })
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

        if let report = storage.cleanupReport {
            Section {
                Label(report, systemImage: "checkmark.circle")
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }

        if storage.hasScanned, !storage.developerCaches.isEmpty {
            Section {
                let reference = storage.developerCaches.compactMap(\.size).max() ?? 1
                ForEach(storage.developerCaches) { item in
                    StorageRow(item: item, reference: reference, onOpen: nil, onReveal: { storage.reveal(item.url) },
                               onClean: item.cleanup == nil ? nil : { confirming = .cache(item) },
                               isCleaning: storage.cleaning.contains(item.url))
                }
            } header: {
                Text("Developer and app caches")
            } footer: {
                Text("Clean moves a cache to the Trash, or runs its tool's own command, and the tool rebuilds what it needs. Items without it hold your own data, such as archives and emulators.")
                    .multilineTextAlignment(.leading)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }

        if storage.hasScanned, storage.isFindingProjects || !storage.projects.isEmpty {
            FlutterProjectsSection(storage: storage, confirming: $confirming)
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
        .alert(confirming?.title ?? "", isPresented: Binding(get: { confirming != nil }, set: { if !$0 { confirming = nil } }),
               presenting: confirming) { request in
            Button(request.action, role: .destructive) { perform(request) }
            Button("Cancel", role: .cancel) {}
        } message: { request in
            Text(request.message)
        }
    }

    private func perform(_ request: CleanRequest) {
        Task {
            switch request {
            case .cache(let item): await storage.clean(item)
            case .project(let project): await storage.clean(project)
            case .stale: await storage.cleanStaleProjects()
            }
        }
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

/// A clean-up to confirm, with the words for the confirmation.
private enum CleanRequest {
    case cache(StorageItem)
    case project(FlutterProject)
    case stale([FlutterProject])

    var title: String {
        switch self {
        case .cache(let item): String(localized: "Clean \(item.name)?")
        case .project(let project): String(localized: "Clean \(project.name)?")
        case .stale(let projects): String(localized: "Clean \(projects.count) unused projects?")
        }
    }

    var message: String {
        switch self {
        case .cache(let item):
            if case .command(let executable, let arguments) = item.cleanup {
                let command = ([URL(fileURLWithPath: executable).lastPathComponent] + arguments).joined(separator: " ")
                return String(localized: "Runs \(command), which deletes the simulators of iOS versions no longer installed. They do not go to the Trash.")
            }
            return String(localized: "It moves to the Trash, and its tool rebuilds what it needs, so the next build or install may take longer.")
        case .project(let project):
            return String(localized: "Its build and .dart_tool folders move to the Trash (\(Format.diskBytes(project.size ?? 0))). The next run builds them again.")
        case .stale(let projects):
            let size = projects.compactMap(\.size).reduce(0, +)
            return String(localized: "Their build and .dart_tool folders move to the Trash (\(Format.diskBytes(size))). Each project builds them again on its next run.")
        }
    }

    var action: String {
        if case .cache(let item) = self, case .command = item.cleanup { return String(localized: "Clean") }
        return String(localized: "Move to Trash")
    }
}

/// Flutter projects with build output, and the ones untouched for a month that can go in one step.
private struct FlutterProjectsSection: View {
    var storage: StorageAnalyzer
    @Binding var confirming: CleanRequest?

    var body: some View {
        let stale = storage.projects.filter { $0.isStale() && $0.size != nil }
        Section {
            if storage.projects.isEmpty {
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Looking for Flutter projects…").foregroundStyle(.secondary)
                }
            }
            let reference = storage.projects.compactMap(\.size).max() ?? 1
            ForEach(storage.projects) { project in
                ProjectRow(project: project, reference: reference,
                           isCleaning: storage.cleaning.contains(project.url),
                           onReveal: { storage.reveal(project.url) },
                           onClean: { confirming = .project(project) })
            }
        } header: {
            HStack {
                Text("Flutter projects")
                Spacer()
                if !stale.isEmpty {
                    Button(String(localized: "Clean unused (\(stale.count))")) { confirming = .stale(stale) }
                        .disabled(!storage.cleaning.isDisjoint(with: stale.map(\.url)))
                }
            }
        } footer: {
            Text("Clean moves only build and .dart_tool to the Trash, what flutter clean removes. A project counts as unused after 30 days without a build or an edit. Searched in folders such as Projects, Developer and Documents.")
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

private struct ProjectRow: View {
    let project: FlutterProject
    let reference: Int64
    let isCleaning: Bool
    let onReveal: () -> Void
    let onClean: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: project.url.path))
                .resizable()
                .frame(width: 18, height: 18)
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(project.name).lineLimit(1).truncationMode(.middle)
                    if project.isStale() {
                        Text("Unused")
                            .font(.caption2.weight(.semibold))
                            .foregroundStyle(Level.color(for: 0.7))
                    }
                    Spacer()
                    if let size = project.size {
                        Text(Format.diskBytes(size)).monospacedDigit().foregroundStyle(.secondary)
                    } else {
                        ProgressView().controlSize(.mini)
                    }
                }
                Text(caption).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.head)
                BarGauge(fraction: Double(project.size ?? 0) / Double(max(reference, 1)), color: Level.accent)
            }
            Button(action: onReveal) { Image(systemName: "magnifyingglass") }
                .buttonStyle(SettingsIconButtonStyle())
                .help("Show in Finder")
            CleanButton(isCleaning: isCleaning, action: onClean)
                .disabled(project.size == nil)
        }
        .padding(.vertical, 2)
    }

    /// "~/Projects/app · used 3 months ago".
    private var caption: String {
        let folder = (project.url.deletingLastPathComponent().path as NSString).abbreviatingWithTildeInPath
        let used = RelativeDateTimeFormatter().localizedString(for: project.lastUsed, relativeTo: Date())
        return String(localized: "\(folder) · used \(used)")
    }
}

/// The trash can on a cleanable row, or a spinner while it cleans.
private struct CleanButton: View {
    let isCleaning: Bool
    let action: () -> Void

    var body: some View {
        if isCleaning {
            ProgressView().controlSize(.small).frame(width: 22, height: 22)
        } else {
            Button(action: action) { Image(systemName: "trash") }
                .buttonStyle(SettingsIconButtonStyle())
                .help("Clean")
        }
    }
}

/// One file or folder with its size, a bar relative to the largest sibling, and actions.
private struct StorageRow: View {
    let item: StorageItem
    let reference: Int64
    let onOpen: (() -> Void)?
    let onReveal: () -> Void
    var onClean: (() -> Void)? = nil
    var isCleaning = false

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
            } else if let onClean {
                CleanButton(isCleaning: isCleaning, action: onClean)
                    .disabled(item.size == nil)
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
