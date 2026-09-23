import Foundation
import Testing
@testable import MYUStats

@MainActor
@Suite("Storage navigation")
struct StorageNavigationTests {
    /// home/big (300 KB in a subfolder), home/small (20 KB), home/note.txt (5 KB).
    private func makeHome() throws -> URL {
        let home = FileManager.default.temporaryDirectory.appendingPathComponent("MYUStatsHome-\(UUID().uuidString)")
        let files: [(String, Int)] = [("big/inner/a.bin", 200_000), ("big/b.bin", 100_000), ("small/c.bin", 20_000), ("note.txt", 5_000)]
        for (path, size) in files {
            let url = home.appendingPathComponent(path)
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try Data((0..<size).map { _ in UInt8.random(in: 0...255) }).write(to: url)
        }
        return home
    }

    @Test("the overview is measured and sorted largest first")
    func overview() async throws {
        let home = try makeHome()
        defer { try? FileManager.default.removeItem(at: home) }
        let storage = StorageAnalyzer(home: home, extraRoots: [])

        storage.scanOverview()
        await storage.waitForScan()

        #expect(!storage.isScanning)
        #expect(storage.items.map(\.name) == ["big", "small", "note.txt"])
        #expect(storage.items.allSatisfy { $0.size != nil })
        #expect(storage.developerCaches.isEmpty)
    }

    @Test("opening a folder and going back keeps the sizes already measured")
    func openAndBack() async throws {
        let home = try makeHome()
        defer { try? FileManager.default.removeItem(at: home) }
        let storage = StorageAnalyzer(home: home, extraRoots: [])
        storage.scanOverview()
        await storage.waitForScan()
        let big = try #require(storage.items.first)

        storage.open(big)
        await storage.waitForScan()
        #expect(storage.trail.map(\.name) == ["big"])
        #expect(storage.items.map(\.name) == ["inner", "b.bin"])

        storage.back()
        // Sizes are cached, so nothing is left to measure.
        #expect(!storage.isScanning)
        #expect(storage.trail.isEmpty)
        #expect(storage.items.first?.name == "big")
        #expect(storage.items.first?.size == big.size)
    }

    @Test("rescanning a folder measures it again and stays in it")
    func rescan() async throws {
        let home = try makeHome()
        defer { try? FileManager.default.removeItem(at: home) }
        let storage = StorageAnalyzer(home: home, extraRoots: [])
        storage.scanOverview()
        await storage.waitForScan()
        storage.open(try #require(storage.items.first))
        await storage.waitForScan()

        try Data(count: 1).write(to: home.appendingPathComponent("big/new.bin"))
        storage.rescan()
        await storage.waitForScan()
        #expect(storage.trail.map(\.name) == ["big"])
        #expect(storage.items.contains { $0.name == "new.bin" })
    }
}
