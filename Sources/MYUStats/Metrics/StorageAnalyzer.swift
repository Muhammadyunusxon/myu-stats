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
    /// How the Disk page can clear it; nil for anything that holds the user's own data.
    var cleanup: Cleanup?

    var id: URL { url }
}

/// How a cache is cleared.
enum Cleanup: Equatable, Sendable {
    /// Everything inside the folder goes to the Trash; the folder itself stays.
    case trashContents
    /// Only these sub-folders go to the Trash, so the tool's settings and downloads it cannot fetch
    /// again stay.
    case trashItems([String])
    /// The tool's own command, for what moving files would not clean up properly.
    case command(executable: String, arguments: [String])
}

/// A Flutter project with build output that `flutter clean` would remove.
struct FlutterProject: Identifiable, Equatable, Sendable {
    let url: URL
    /// The project's newest build, fetch or edit.
    let lastUsed: Date
    /// Its build and .dart_tool folders, whichever exist.
    let artifacts: [URL]
    /// Bytes in `artifacts`; nil while still being measured.
    var size: Int64?

    var id: URL { url }
    var name: String { url.lastPathComponent }

    /// Untouched for a month: its build output is only taking space.
    func isStale(now: Date = Date()) -> Bool {
        now.timeIntervalSince(lastUsed) > StorageAnalyzer.staleAfter
    }
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
    /// Flutter projects under the usual project folders, largest build output first.
    private(set) var projects: [FlutterProject] = []
    private(set) var isFindingProjects = false
    /// Caches and projects being cleaned right now.
    private(set) var cleaning: Set<URL> = []
    /// What the last clean-up did, shown until the next one.
    private(set) var cleanupReport: String?
    /// Called after a clean-up, so the free space shown elsewhere is read again.
    @ObservationIgnored var onCleaned: (() -> Void)?

    @ObservationIgnored private var sizes: [URL: Int64] = [:]
    @ObservationIgnored private var task: Task<Void, Never>?
    /// Pending re-sort while results stream in; see `record`.
    @ObservationIgnored private var sortTask: Task<Void, Never>?
    @ObservationIgnored private var projectTask: Task<Void, Never>?
    @ObservationIgnored private let home: URL
    @ObservationIgnored private let extraRoots: [URL]
    @ObservationIgnored private let trash: @Sendable (URL) throws -> Void
    private static let parallelism = 4
    /// Results arriving closer together than this are sorted once, so rows do not jump under the pointer.
    static let sortDelay: Duration = .milliseconds(500)
    /// A project untouched for this long counts as unused.
    nonisolated static let staleAfter: TimeInterval = 30 * 24 * 60 * 60

    /// - Parameters:
    ///   - home: Folder whose top-level items make up the overview.
    ///   - extraRoots: Measured alongside them (/Applications by default).
    ///   - trash: Moves an item to the Trash; tests move it somewhere of their own.
    init(
        home: URL = FileManager.default.homeDirectoryForCurrentUser,
        extraRoots: [URL] = [URL(fileURLWithPath: "/Applications")],
        trash: @escaping @Sendable (URL) throws -> Void = { try FileManager.default.trashItem(at: $0, resultingItemURL: nil) }
    ) {
        self.home = home
        self.extraRoots = extraRoots
        self.trash = trash
    }

    var measuredTotal: Int64 { items.compactMap(\.size).reduce(0, +) }

    // MARK: - Navigation

    func scanOverview() {
        trail = []
        hasScanned = true
        items = overviewRoots().map(item(for:))
        developerCaches = Self.developerCacheRoots(home: home).map { url, note, cleanup in
            var cache = item(for: url)
            cache.note = note
            cache.cleanup = cleanup
            return cache
        }
        measure(items.map(\.url) + developerCaches.map(\.url))
        findProjects()
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
        projectTask?.cancel()
        projectTask = nil
        isFindingProjects = false
    }

    /// Waits for the current measurement, and the project search, to finish; for tests.
    func waitForScan() async {
        await task?.value
        await projectTask?.value
    }

    func reveal(_ url: URL) {
        NSWorkspace.shared.activateFileViewerSelecting([url])
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

    // MARK: - Cleaning up

    /// Clears a cache the way its `cleanup` says, then measures it, and the Trash, again.
    func clean(_ item: StorageItem) async {
        guard let cleanup = item.cleanup, !cleaning.contains(item.url) else { return }
        cleaning.insert(item.url)
        defer { cleaning.remove(item.url) }
        switch cleanup {
        case .command(let executable, let arguments):
            let before = item.size ?? Self.allocatedSize(of: item.url)
            let succeeded = await Self.run(executable, arguments)
            let after = await Self.measured(item.url)
            cleanupReport = succeeded
                ? String(localized: "\(item.name): freed \(Format.diskBytes(max(0, before - after))).")
                : String(localized: "\(item.name) could not be cleaned: the command failed. Is Xcode installed?")
            Log.storage.notice("Cleaned \(item.name, privacy: .public) by command: \(succeeded)")
        case .trashContents, .trashItems:
            let result = await Self.moveToTrash(Self.targets(of: cleanup, in: item.url), trash: trash)
            cleanupReport = Self.report(item.name, result)
            Log.storage.notice("Cleaned \(item.name, privacy: .public): \(result.moved) bytes, \(result.failed) failed")
        }
        onCleaned?()
        await remeasure([item.url, home.appendingPathComponent(".Trash")])
    }

    /// Moves a project's build output to the Trash.
    func clean(_ project: FlutterProject) async {
        await clean([project])
    }

    /// Moves the build output of every project untouched for a month to the Trash.
    func cleanStaleProjects() async {
        await clean(projects.filter { $0.isStale() })
    }

    private func clean(_ chosen: [FlutterProject]) async {
        let pending = chosen.filter { !cleaning.contains($0.url) }
        guard !pending.isEmpty else { return }
        cleaning.formUnion(pending.map(\.url))
        defer { cleaning.subtract(pending.map(\.url)) }
        let result = await Self.moveToTrash(pending.flatMap(\.artifacts), trash: trash)
        let name = pending.count == 1 ? pending[0].name : String(localized: "\(pending.count) projects")
        cleanupReport = Self.report(name, result)
        Log.storage.notice("Cleaned \(pending.count) Flutter projects: \(result.moved) bytes, \(result.failed) failed")
        // What could not be moved is still there.
        let left = pending.filter { $0.artifacts.contains { FileManager.default.fileExists(atPath: $0.path) } }.map(\.url)
        projects.removeAll { project in pending.contains { $0.url == project.url } && !left.contains(project.url) }
        onCleaned?()
        await remeasure([home.appendingPathComponent(".Trash")])
    }

    /// What goes to the Trash for a cleanup of the folder at `url`.
    nonisolated static func targets(of cleanup: Cleanup, in url: URL) -> [URL] {
        switch cleanup {
        case .trashContents:
            (try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil)) ?? []
        case .trashItems(let names):
            names.map { url.appendingPathComponent($0) }.filter { FileManager.default.fileExists(atPath: $0.path) }
        case .command:
            []
        }
    }

    /// Measures each item, then moves it; an item that will not move (in use, or protected) is skipped.
    nonisolated static func moveToTrash(_ urls: [URL], trash: @Sendable (URL) throws -> Void) async -> (moved: Int64, failed: Int) {
        var moved: Int64 = 0
        var failed = 0
        for url in urls {
            let size = allocatedSize(of: url)
            do {
                try trash(url)
                moved += size
            } catch {
                failed += 1
                Log.storage.notice("Not moved to the Trash: \(error.localizedDescription, privacy: .public)")
            }
        }
        return (moved, failed)
    }

    private static func report(_ name: String, _ result: (moved: Int64, failed: Int)) -> String {
        if result.failed > 0 {
            return String(localized: "\(name): moved \(Format.diskBytes(result.moved)) to the Trash. Some items could not be moved; they may be in use.")
        }
        if result.moved == 0 { return String(localized: "\(name): nothing to clean.") }
        return String(localized: "\(name): moved \(Format.diskBytes(result.moved)) to the Trash. Empty the Trash to get the space back.")
    }

    /// Runs a tool's own clean-up command off the main thread; false when it fails or hangs.
    nonisolated private static func run(_ executable: String, _ arguments: [String]) async -> Bool {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                let process = Process()
                process.executableURL = URL(fileURLWithPath: executable)
                process.arguments = arguments
                process.standardInput = FileHandle.nullDevice
                process.standardOutput = FileHandle.nullDevice
                process.standardError = FileHandle.nullDevice
                do {
                    try process.run()
                } catch {
                    Log.storage.error("\(executable, privacy: .public) did not start: \(error.localizedDescription, privacy: .public)")
                    continuation.resume(returning: false)
                    return
                }
                let watchdog = DispatchWorkItem { if process.isRunning { process.terminate() } }
                DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 120, execute: watchdog)
                process.waitUntilExit()
                watchdog.cancel()
                continuation.resume(returning: process.terminationStatus == 0)
            }
        }
    }

    nonisolated private static func measured(_ url: URL) async -> Int64 {
        allocatedSize(of: url)
    }

    /// Measures these again, whatever else is being measured.
    private func remeasure(_ urls: [URL]) async {
        for url in urls {
            sizes[url] = nil
            let size = await Self.measured(url)
            record(size, for: url)
        }
    }

    // MARK: - Flutter projects

    /// Folders in the home folder where projects usually live, matched without case.
    nonisolated static let projectFolders: Set<String> = [
        "projects", "project", "developer", "dev", "code", "src", "source", "sources", "repos", "repositories",
        "workspace", "work", "github", "gitlab", "flutter", "flutter_projects", "flutterprojects",
        "studioprojects", "androidstudioprojects", "documents", "desktop",
    ]
    nonisolated static let projectSearchDepth = 6
    /// Never hold a project of their own, or are too big to walk for one.
    nonisolated private static let skippedFolders: Set<String> = [
        "build", "node_modules", "Pods", "ios", "android", "macos", "windows", "linux", "web", "lib", "test",
        "assets", "DerivedData", "Library",
    ]

    private func findProjects() {
        projectTask?.cancel()
        projects = []
        isFindingProjects = true
        let home = self.home
        projectTask = Task { [weak self] in
            let found = await Self.flutterProjects(home: home)
            guard !Task.isCancelled, let self else { return }
            projects = found
            await withTaskGroup(of: (URL, Int64).self) { group in
                var queue = found[...]
                func enqueue() {
                    guard let project = queue.popFirst() else { return }
                    group.addTask { (project.url, project.artifacts.map(Self.allocatedSize(of:)).reduce(0, +)) }
                }
                for _ in 0..<Self.parallelism { enqueue() }
                for await (url, size) in group {
                    guard !Task.isCancelled else { break }
                    if let index = self.projects.firstIndex(where: { $0.url == url }) { self.projects[index].size = size }
                    enqueue()
                }
            }
            guard !Task.isCancelled else { return }
            projects = projects.filter { ($0.size ?? 0) > 0 }.sorted { ($0.size ?? 0) > ($1.size ?? 0) }
            isFindingProjects = false
        }
    }

    /// Flutter projects with build output, searched for a few levels down the usual project folders.
    nonisolated static func flutterProjects(home: URL) async -> [FlutterProject] {
        let fileManager = FileManager.default
        let keys: [URLResourceKey] = [.isDirectoryKey, .isSymbolicLinkKey, .isPackageKey]
        func folders(in folder: URL) -> [URL] {
            let children = (try? fileManager.contentsOfDirectory(at: folder, includingPropertiesForKeys: keys,
                                                                   options: [.skipsHiddenFiles])) ?? []
            return children.filter { child in
                let values = try? child.resourceValues(forKeys: Set(keys))
                return values?.isDirectory == true && values?.isSymbolicLink != true && values?.isPackage != true
            }
        }

        var pending = folders(in: home)
            .filter { projectFolders.contains($0.lastPathComponent.lowercased()) }
            .map { ($0, 1) }
        var found: [FlutterProject] = []
        while let (folder, depth) = pending.popLast() {
            guard !Task.isCancelled else { break }
            if let project = flutterProject(at: folder) { found.append(project) }
            guard depth < projectSearchDepth else { continue }
            // Projects nest (a package's example app, a monorepo's packages), so the search goes on inside them.
            pending += folders(in: folder).filter { !skippedFolders.contains($0.lastPathComponent) }.map { ($0, depth + 1) }
        }
        return found
    }

    nonisolated static func flutterProject(at folder: URL) -> FlutterProject? {
        guard let pubspec = try? String(contentsOf: folder.appendingPathComponent("pubspec.yaml"), encoding: .utf8),
              isFlutter(pubspec: pubspec)
        else { return nil }
        let artifacts = ["build", ".dart_tool"].map { folder.appendingPathComponent($0) }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
        guard !artifacts.isEmpty else { return nil }
        return FlutterProject(url: folder, lastUsed: lastUsed(folder), artifacts: artifacts)
    }

    /// A Flutter app or plugin depends on the Flutter SDK; a plain Dart package does not.
    nonisolated static func isFlutter(pubspec: String) -> Bool {
        pubspec.range(of: #"(?m)^\s+sdk:\s*["']?flutter["']?\s*$"#, options: .regularExpression) != nil
    }

    /// The newest of the project's builds, package fetches and source edits.
    nonisolated static func lastUsed(_ folder: URL) -> Date {
        var newest = Date.distantPast
        func consider(_ url: URL) {
            if let date = try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate, date > newest {
                newest = date
            }
        }
        for path in ["pubspec.yaml", "pubspec.lock", ".dart_tool/package_config.json", "build"] {
            consider(folder.appendingPathComponent(path))
        }
        if let files = FileManager.default.enumerator(at: folder.appendingPathComponent("lib"),
                                                      includingPropertiesForKeys: [.contentModificationDateKey],
                                                      options: [.skipsHiddenFiles]) {
            for case let file as URL in files.prefix(2_000) { consider(file) }
        }
        return newest
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

    /// Well-known tool caches; only the ones present on this Mac are listed. Those with a clean-up are
    /// rebuilt by their tool; the others (archives, emulators, Docker's disk) hold the user's own data.
    private static func developerCacheRoots(home: URL) -> [(URL, String, Cleanup?)] {
        let candidates: [(String, String, Cleanup?)] = [
            ("Library/Developer/Xcode/DerivedData", String(localized: "Xcode build products · rebuilt on next build"), .trashContents),
            ("Library/Developer/Xcode/Archives", String(localized: "Xcode archives · keep the ones you may need to re-export"), nil),
            ("Library/Developer/Xcode/iOS DeviceSupport", String(localized: "Debug symbols per iOS version · re-copied from devices"), .trashContents),
            ("Library/Developer/CoreSimulator/Devices", String(localized: "Simulator devices · `xcrun simctl delete unavailable`"),
             .command(executable: "/usr/bin/xcrun", arguments: ["simctl", "delete", "unavailable"])),
            ("Library/Developer/CoreSimulator/Caches", String(localized: "Simulator caches"), .trashContents),
            ("Library/Caches", String(localized: "App caches · rebuilt as needed"), nil),
            // The wrapper's Gradle downloads stay: only the dependency and build caches go.
            (".gradle", String(localized: "Gradle caches · Clean keeps the downloaded Gradle versions"), .trashItems(["caches"])),
            ("Library/Android/sdk", String(localized: "Android SDK, system images and emulators"), nil),
            (".android/avd", String(localized: "Android emulator images"), nil),
            // Globally activated tools live in bin and global_packages, and stay.
            (".pub-cache", String(localized: "Flutter / Dart packages · downloaded again by pub get"), .trashItems(["hosted", "git"])),
            ("Library/Caches/CocoaPods", String(localized: "CocoaPods cache · `pod cache clean --all`"), .trashContents),
            (".npm", String(localized: "npm cache · `npm cache clean --force`"), .trashItems(["_cacache"])),
            ("Library/Caches/Yarn", String(localized: "Yarn cache"), .trashContents),
            ("Library/Caches/Homebrew", String(localized: "Homebrew downloads · `brew cleanup`"), .trashContents),
            ("Library/Containers/com.docker.docker/Data", String(localized: "Docker images and volumes"), nil),
            (".Trash", String(localized: "Trash · empty it in Finder"), nil),
        ]
        return candidates.compactMap { path, note, cleanup in
            let url = home.appendingPathComponent(path)
            return FileManager.default.fileExists(atPath: url.path) ? (url, note, cleanup) : nil
        }
    }
}
