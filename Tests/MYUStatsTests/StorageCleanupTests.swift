import Foundation
import Testing
@testable import MYUStats

@MainActor
@Suite("Storage clean-up")
struct StorageCleanupTests {
    private let fileManager = FileManager.default

    private func folder(_ prefix: String) throws -> URL {
        let url = fileManager.temporaryDirectory.appendingPathComponent("\(prefix)-\(UUID().uuidString)")
        try fileManager.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func write(_ url: URL, bytes: Int = 1_000, modified: Date? = nil) throws {
        try fileManager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data((0..<bytes).map { _ in UInt8.random(in: 0...255) }).write(to: url)
        if let modified { try fileManager.setAttributes([.modificationDate: modified], ofItemAtPath: url.path) }
    }

    private static let flutterPubspec = """
        name: app
        dependencies:
          flutter:
            sdk: flutter
        """

    /// A Flutter project with build output, dated `age` ago.
    private func project(_ url: URL, age: TimeInterval) throws {
        let date = Date().addingTimeInterval(-age)
        try write(url.appendingPathComponent("build/app/outputs/app.apk"), bytes: 50_000, modified: date)
        try write(url.appendingPathComponent(".dart_tool/package_config.json"), bytes: 2_000, modified: date)
        try write(url.appendingPathComponent("lib/main.dart"), bytes: 100, modified: date)
        try Self.flutterPubspec.write(to: url.appendingPathComponent("pubspec.yaml"), atomically: true, encoding: .utf8)
        try fileManager.setAttributes([.modificationDate: date], ofItemAtPath: url.appendingPathComponent("pubspec.yaml").path)
        try fileManager.setAttributes([.modificationDate: date], ofItemAtPath: url.appendingPathComponent("build").path)
    }

    /// Moves "trashed" items into `bin` instead of the real Trash.
    private func fakeTrash(_ bin: URL) -> @Sendable (URL) throws -> Void {
        { url in try FileManager.default.moveItem(at: url, to: bin.appendingPathComponent(UUID().uuidString)) }
    }

    @Test("only Flutter projects count, and plain Dart packages do not")
    func recognisesFlutter() {
        #expect(StorageAnalyzer.isFlutter(pubspec: Self.flutterPubspec))
        #expect(StorageAnalyzer.isFlutter(pubspec: "dev_dependencies:\n  flutter_test:\n    sdk: 'flutter'\n"))
        #expect(!StorageAnalyzer.isFlutter(pubspec: "name: tool\ndependencies:\n  args: ^2.0.0\n"))
    }

    @Test("projects are found in the usual folders, nested ones too, and old ones are marked unused")
    func findsProjects() async throws {
        let home = try folder("MYUStatsHome")
        defer { try? fileManager.removeItem(at: home) }
        try project(home.appendingPathComponent("Projects/shop"), age: 90 * 86_400)
        try project(home.appendingPathComponent("Projects/work/chat"), age: 3_600)
        // A package's example app is a project of its own.
        try project(home.appendingPathComponent("Projects/work/chat/packages/ui/example"), age: 60 * 86_400)
        // Not searched: outside the usual folders, or inside a dependency folder.
        try project(home.appendingPathComponent("Music/band"), age: 90 * 86_400)
        try project(home.appendingPathComponent("Projects/site/node_modules/pkg"), age: 90 * 86_400)
        // A Flutter project without build output has nothing to clean.
        try Self.flutterPubspec.write(to: home.appendingPathComponent("Projects/fresh/pubspec.yaml").creatingParent(),
                                      atomically: true, encoding: .utf8)

        let found = await StorageAnalyzer.flutterProjects(home: home).sorted { $0.name < $1.name }
        #expect(found.map(\.name) == ["chat", "example", "shop"])
        #expect(found.map { $0.isStale() } == [false, true, true])
        #expect(found.allSatisfy { $0.artifacts.map(\.lastPathComponent).sorted() == [".dart_tool", "build"] })
    }

    @Test("the overview lists projects by the size of their build output")
    func overviewMeasuresProjects() async throws {
        let home = try folder("MYUStatsHome")
        defer { try? fileManager.removeItem(at: home) }
        try project(home.appendingPathComponent("dev/app"), age: 0)
        let storage = StorageAnalyzer(home: home, extraRoots: [])
        storage.scanOverview()
        await storage.waitForScan()
        #expect(!storage.isFindingProjects)
        #expect(storage.projects.map(\.name) == ["app"])
        #expect((storage.projects.first?.size ?? 0) >= 52_000)
    }

    @Test("cleaning a project moves its build output away and leaves the code")
    func cleansProject() async throws {
        let home = try folder("MYUStatsHome")
        let bin = try folder("MYUStatsBin")
        defer {
            try? fileManager.removeItem(at: home)
            try? fileManager.removeItem(at: bin)
        }
        let app = home.appendingPathComponent("projects/app")
        try project(app, age: 45 * 86_400)
        let storage = StorageAnalyzer(home: home, extraRoots: [], trash: fakeTrash(bin))
        storage.scanOverview()
        await storage.waitForScan()

        await storage.cleanStaleProjects()
        #expect(!fileManager.fileExists(atPath: app.appendingPathComponent("build").path))
        #expect(!fileManager.fileExists(atPath: app.appendingPathComponent(".dart_tool").path))
        #expect(fileManager.fileExists(atPath: app.appendingPathComponent("lib/main.dart").path))
        #expect(try fileManager.contentsOfDirectory(atPath: bin.path).count == 2)
        #expect(storage.projects.isEmpty)
        #expect(storage.cleanupReport?.contains("app") == true)
        #expect(storage.cleaning.isEmpty)
    }

    @Test("a cache is cleaned the way its entry says: only the named folders of the pub cache go")
    func cleansCache() async throws {
        let home = try folder("MYUStatsHome")
        let bin = try folder("MYUStatsBin")
        defer {
            try? fileManager.removeItem(at: home)
            try? fileManager.removeItem(at: bin)
        }
        let pub = home.appendingPathComponent(".pub-cache")
        try write(pub.appendingPathComponent("hosted/pub.dev/http-1.2.0/lib/http.dart"), bytes: 30_000)
        try write(pub.appendingPathComponent("git/cache/repo/HEAD"), bytes: 1_000)
        try write(pub.appendingPathComponent("bin/flutterfire"), bytes: 500)
        let storage = StorageAnalyzer(home: home, extraRoots: [], trash: fakeTrash(bin))
        storage.scanOverview()
        await storage.waitForScan()
        // Found by name: a URL made for a folder that exists ends in a slash, one made before does not.
        let cache = try #require(storage.developerCaches.first { $0.name == ".pub-cache" })
        #expect(cache.cleanup == .trashItems(["hosted", "git"]))

        await storage.clean(cache)
        #expect(!fileManager.fileExists(atPath: pub.appendingPathComponent("hosted").path))
        #expect(!fileManager.fileExists(atPath: pub.appendingPathComponent("git").path))
        // Globally activated tools stay.
        #expect(fileManager.fileExists(atPath: pub.appendingPathComponent("bin/flutterfire").path))
        let after = try #require(storage.developerCaches.first { $0.name == ".pub-cache" }?.size)
        #expect(after < 20_000)
    }

    @Test("an item that will not move is counted, and the rest still goes")
    func partialFailure() async throws {
        let bin = try folder("MYUStatsBin")
        defer { try? fileManager.removeItem(at: bin) }
        let file = bin.appendingPathComponent("keep.bin")
        try write(file, bytes: 4_000)
        let result = await StorageAnalyzer.moveToTrash([bin.appendingPathComponent("missing"), file]) { url in
            guard FileManager.default.fileExists(atPath: url.path) else { throw CocoaError(.fileNoSuchFile) }
        }
        #expect(result.failed == 1)
        #expect(result.moved >= 4_000)
    }

    @Test("what a clean-up takes: a folder's contents, or only the named sub-folders that exist")
    func targets() throws {
        let root = try folder("MYUStatsCache")
        defer { try? fileManager.removeItem(at: root) }
        try write(root.appendingPathComponent("a/1.bin"))
        try write(root.appendingPathComponent("b.bin"))
        let contents = StorageAnalyzer.targets(of: .trashContents, in: root).map(\.lastPathComponent).sorted()
        #expect(contents == ["a", "b.bin"])
        #expect(StorageAnalyzer.targets(of: .trashItems(["a", "zzz"]), in: root).map(\.lastPathComponent) == ["a"])
        #expect(StorageAnalyzer.targets(of: .command(executable: "/usr/bin/true", arguments: []), in: root).isEmpty)
    }
}

private extension URL {
    /// Creates the folder this file goes in.
    func creatingParent() throws -> URL {
        try FileManager.default.createDirectory(at: deletingLastPathComponent(), withIntermediateDirectories: true)
        return self
    }
}
