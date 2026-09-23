import AppKit
import Foundation

struct StorageItem: Identifiable, Equatable {
    let url: URL
    let name: String
    let isDirectory: Bool
    /// Allocated bytes on disk; nil while still being measured.
    var size: Int64?
    /// Shown under the name for well-known caches.
    var note: String?

    var id: URL { url }
}

/// Measures what takes space on the startup disk, on demand.
///
/// The overview lists every top-level item in the home folder plus /Applications, largest first.
/// Any folder can be opened to measure its own children. Sizes are cached for the session, so going
/// back up is instant. Measuring walks every file, which takes a while on large folders, so it runs
/// off the main thread, a few folders at a time, and is cancelled when the page is left.
@MainActor
final class StorageAnalyzer: ObservableObject {
    /// Folders opened from the overview, outermost first. Empty means the overview.
    @Published private(set) var trail: [StorageItem] = []
    @Published private(set) var items: [StorageItem] = []
    @Published private(set) var developerCaches: [StorageItem] = []
    @Published private(set) var isScanning = false
    @Published private(set) var hasScanned = false

    private var sizes: [URL: Int64] = [:]
    private var task: Task<Void, Never>?
    private static let parallelism = 4

    var measuredTotal: Int64 { items.compactMap(\.size).reduce(0, +) }

    // MARK: - Navigation

    func scanOverview() {
        trail = []
        hasScanned = true
        items = Self.overviewRoots().map(item(for:))
        developerCaches = Self.developerCacheRoots().map { url, note in
            var cache = item(for: url)
            cache.note = note
            return cache
        }
        measure(items.map(\.url) + developerCaches.map(\.url))
    }

    func open(_ folder: StorageItem) {
        guard folder.isDirectory else { return }
        trail.append(folder)
        items = Self.children(of: folder.url).map(item(for:))
        measure(items.map(\.url))
    }

    func back() {
        guard !trail.isEmpty else { return }
        trail.removeLast()
        if let parent = trail.last {
            items = Self.children(of: parent.url).map(item(for:))
            measure(items.map(\.url))
        } else {
            items = Self.overviewRoots().map(item(for:))
            measure(items.map(\.url))
        }
    }

    func rescan() {
        sizes.removeAll()
        if trail.isEmpty { scanOverview() } else if let current = trail.popLast() { open(current) }
    }

    func cancel() {
        task?.cancel()
        task = nil
        isScanning = false
    }

    func reveal(_ item: StorageItem) {
        NSWorkspace.shared.activateFileViewerSelecting([item.url])
    }

    // MARK: - Measuring

    private func item(for url: URL) -> StorageItem {
        let values = try? url.resourceValues(forKeys: [.isDirectoryKey, .localizedNameKey])
        return StorageItem(
            url: url,
            name: values?.localizedName ?? url.lastPathComponent,
            isDirectory: values?.isDirectory ?? false,
            size: sizes[url]
        )
    }

    private func measure(_ urls: [URL]) {
        task?.cancel()
        let pending = urls.filter { sizes[$0] == nil }
        sortItems()
        guard !pending.isEmpty else {
            isScanning = false
            return
        }
        isScanning = true
        task = Task { [weak self] in
            await withTaskGroup(of: (URL, Int64).self) { group in
                var queue = pending[...]
                func enqueue() {
                    guard let url = queue.popFirst() else { return }
                    group.addTask { (url, Self.allocatedSize(of: url)) }
                }
                for _ in 0..<Self.parallelism { enqueue() }
                for await (url, size) in group {
                    guard !Task.isCancelled else { break }
                    self?.record(size, for: url)
                    enqueue()
                }
            }
            guard !Task.isCancelled else { return }
            self?.isScanning = false
        }
    }

    private func record(_ size: Int64, for url: URL) {
        sizes[url] = size
        if let index = items.firstIndex(where: { $0.url == url }) { items[index].size = size }
        if let index = developerCaches.firstIndex(where: { $0.url == url }) { developerCaches[index].size = size }
        sortItems()
    }

    /// Largest first; anything still being measured sinks to the bottom.
    private func sortItems() {
        items.sort { ($0.size ?? -1) > ($1.size ?? -1) }
        developerCaches.sort { ($0.size ?? -1) > ($1.size ?? -1) }
    }

    /// Bytes allocated on disk by a file or everything under a folder. Unreadable entries are skipped.
    /// APFS clones and hard links are counted once per path, so totals can exceed the space really used.
    nonisolated static func allocatedSize(of url: URL) -> Int64 {
        let keys: Set<URLResourceKey> = [.isRegularFileKey, .totalFileAllocatedSizeKey, .fileAllocatedSizeKey]
        if let values = try? url.resourceValues(forKeys: keys), values.isRegularFile == true {
            return Int64(values.totalFileAllocatedSize ?? values.fileAllocatedSize ?? 0)
        }
        guard let enumerator = FileManager.default.enumerator(
            at: url, includingPropertiesForKeys: Array(keys), options: [], errorHandler: { _, _ in true }
        ) else { return 0 }

        var total: Int64 = 0
        var checked = 0
        for case let file as URL in enumerator {
            checked += 1
            if checked % 2_000 == 0, Task.isCancelled { break }
            guard let values = try? file.resourceValues(forKeys: keys), values.isRegularFile == true else { continue }
            total += Int64(values.totalFileAllocatedSize ?? values.fileAllocatedSize ?? 0)
        }
        return total
    }

    // MARK: - What to measure

    private static var home: URL { FileManager.default.homeDirectoryForCurrentUser }

    /// Everything directly in the home folder (hidden ones too — .gradle and friends are often the
    /// biggest), plus /Applications.
    private static func overviewRoots() -> [URL] {
        children(of: home) + [URL(fileURLWithPath: "/Applications")]
    }

    private static func children(of folder: URL) -> [URL] {
        (try? FileManager.default.contentsOfDirectory(
            at: folder, includingPropertiesForKeys: [.isDirectoryKey], options: []
        )) ?? []
    }

    /// Well-known tool caches that are safe to clear; only the ones present on this Mac are listed.
    private static func developerCacheRoots() -> [(URL, String)] {
        let candidates: [(String, String)] = [
            ("Library/Developer/Xcode/DerivedData", "Xcode build products · rebuilt on next build"),
            ("Library/Developer/Xcode/Archives", "Xcode archives · keep the ones you may need to re-export"),
            ("Library/Developer/Xcode/iOS DeviceSupport", "Debug symbols per iOS version · re-copied from devices"),
            ("Library/Developer/CoreSimulator/Devices", "Simulator devices · `xcrun simctl delete unavailable`"),
            ("Library/Developer/CoreSimulator/Caches", "Simulator caches"),
            ("Library/Caches", "App caches · rebuilt as needed"),
            (".gradle", "Gradle caches and wrappers"),
            ("Library/Android/sdk", "Android SDK, system images and emulators"),
            (".android/avd", "Android emulator images"),
            (".pub-cache", "Flutter / Dart packages · `flutter pub cache clean`"),
            ("Library/Caches/CocoaPods", "CocoaPods cache · `pod cache clean --all`"),
            (".npm", "npm cache · `npm cache clean --force`"),
            ("Library/Caches/Yarn", "Yarn cache"),
            ("Library/Caches/Homebrew", "Homebrew downloads · `brew cleanup`"),
            ("Library/Containers/com.docker.docker/Data", "Docker images and volumes"),
            (".Trash", "Trash · empty it in Finder"),
        ]
        return candidates.compactMap { path, note in
            let url = home.appendingPathComponent(path)
            return FileManager.default.fileExists(atPath: url.path) ? (url, note) : nil
        }
    }
}
