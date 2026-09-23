import AppKit
import Foundation
import Observation

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
@Observable
final class StorageAnalyzer {
    /// Folders opened from the overview, outermost first. Empty means the overview.
    private(set) var trail: [StorageItem] = []
    private(set) var items: [StorageItem] = []
    private(set) var developerCaches: [StorageItem] = []
    private(set) var isScanning = false
    private(set) var hasScanned = false

    @ObservationIgnored private var sizes: [URL: Int64] = [:]
    @ObservationIgnored private var task: Task<Void, Never>?
    /// Pending re-sort while results stream in; see `record`.
    @ObservationIgnored private var sortTask: Task<Void, Never>?
    @ObservationIgnored private let home: URL
    @ObservationIgnored private let extraRoots: [URL]
    private static let parallelism = 4
    /// Results arriving closer together than this are sorted once, so rows do not jump under the pointer.
    static let sortDelay: Duration = .milliseconds(500)

    /// - Parameters:
    ///   - home: Folder whose top-level items make up the overview.
    ///   - extraRoots: Measured alongside them (/Applications by default).
    init(
        home: URL = FileManager.default.homeDirectoryForCurrentUser,
        extraRoots: [URL] = [URL(fileURLWithPath: "/Applications")]
    ) {
        self.home = home
        self.extraRoots = extraRoots
    }

    var measuredTotal: Int64 { items.compactMap(\.size).reduce(0, +) }

    // MARK: - Navigation

    func scanOverview() {
        trail = []
        hasScanned = true
        items = overviewRoots().map(item(for:))
        developerCaches = Self.developerCacheRoots(home: home).map { url, note in
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
            items = overviewRoots().map(item(for:))
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

    /// Waits for the current measurement to finish; for tests.
    func waitForScan() async {
        await task?.value
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
        sortItemsNow()
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
            self?.sortItemsNow()
            self?.isScanning = false
        }
    }

    private func record(_ size: Int64, for url: URL) {
        sizes[url] = size
        if let index = items.firstIndex(where: { $0.url == url }) { items[index].size = size }
        if let index = developerCaches.firstIndex(where: { $0.url == url }) { developerCaches[index].size = size }
        scheduleSort()
    }

    /// Sorts once results pause for `sortDelay`, instead of on every result.
    private func scheduleSort() {
        sortTask?.cancel()
        sortTask = Task { [weak self] in
            try? await Task.sleep(for: Self.sortDelay)
            guard !Task.isCancelled else { return }
            self?.sortItemsNow()
        }
    }

    /// Largest first; anything still being measured sinks to the bottom.
    private func sortItemsNow() {
        sortTask?.cancel()
        sortTask = nil
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
            at: url, includingPropertiesForKeys: Array(keys), options: [], errorHandler: { file, error in
                // Protected or vanished entries are expected; skip them and keep walking.
                Log.storage.debug("Skipped \(file.path, privacy: .private): \(error.localizedDescription, privacy: .public)")
                return true
            }
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

    /// Everything directly in the home folder (hidden ones too — .gradle and friends are often the
    /// biggest), plus /Applications.
    private func overviewRoots() -> [URL] {
        Self.children(of: home) + extraRoots
    }

    private static func children(of folder: URL) -> [URL] {
        do {
            return try FileManager.default.contentsOfDirectory(
                at: folder, includingPropertiesForKeys: [.isDirectoryKey], options: []
            )
        } catch {
            Log.storage.notice("Could not list \(folder.path, privacy: .private): \(error.localizedDescription, privacy: .public)")
            return []
        }
    }

    /// Well-known tool caches that are safe to clear; only the ones present on this Mac are listed.
    private static func developerCacheRoots(home: URL) -> [(URL, String)] {
        let candidates: [(String, String)] = [
            ("Library/Developer/Xcode/DerivedData", String(localized: "Xcode build products · rebuilt on next build")),
            ("Library/Developer/Xcode/Archives", String(localized: "Xcode archives · keep the ones you may need to re-export")),
            ("Library/Developer/Xcode/iOS DeviceSupport", String(localized: "Debug symbols per iOS version · re-copied from devices")),
            ("Library/Developer/CoreSimulator/Devices", String(localized: "Simulator devices · `xcrun simctl delete unavailable`")),
            ("Library/Developer/CoreSimulator/Caches", String(localized: "Simulator caches")),
            ("Library/Caches", String(localized: "App caches · rebuilt as needed")),
            (".gradle", String(localized: "Gradle caches and wrappers")),
            ("Library/Android/sdk", String(localized: "Android SDK, system images and emulators")),
            (".android/avd", String(localized: "Android emulator images")),
            (".pub-cache", String(localized: "Flutter / Dart packages · `flutter pub cache clean`")),
            ("Library/Caches/CocoaPods", String(localized: "CocoaPods cache · `pod cache clean --all`")),
            (".npm", String(localized: "npm cache · `npm cache clean --force`")),
            ("Library/Caches/Yarn", String(localized: "Yarn cache")),
            ("Library/Caches/Homebrew", String(localized: "Homebrew downloads · `brew cleanup`")),
            ("Library/Containers/com.docker.docker/Data", String(localized: "Docker images and volumes")),
            (".Trash", String(localized: "Trash · empty it in Finder")),
        ]
        return candidates.compactMap { path, note in
            let url = home.appendingPathComponent(path)
            return FileManager.default.fileExists(atPath: url.path) ? (url, note) : nil
        }
    }
}
