import Foundation
import Testing
@testable import MYUStats

@Suite("Storage sizes")
struct StorageAnalyzerTests {
    private func makeFolder(files: [Int]) throws -> URL {
        let folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("MYUStatsTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder.appendingPathComponent("nested"),
                                                withIntermediateDirectories: true)
        for (index, size) in files.enumerated() {
            let name = index.isMultiple(of: 2) ? "file\(index)" : "nested/file\(index)"
            // Random bytes so APFS cannot compress or share them.
            let bytes = Data((0..<size).map { _ in UInt8.random(in: 0...255) })
            try bytes.write(to: folder.appendingPathComponent(name))
        }
        return folder
    }

    @Test("a folder's size covers every file beneath it")
    func folderSize() throws {
        let folder = try makeFolder(files: [100_000, 250_000, 50_000])
        defer { try? FileManager.default.removeItem(at: folder) }
        let size = StorageAnalyzer.allocatedSize(of: folder)
        // Allocated size rounds each file up to whole blocks.
        #expect(size >= 400_000)
        #expect(size <= 400_000 + 3 * 16_384)
    }

    @Test("a single file measures itself")
    func fileSize() throws {
        let folder = try makeFolder(files: [70_000])
        defer { try? FileManager.default.removeItem(at: folder) }
        let size = StorageAnalyzer.allocatedSize(of: folder.appendingPathComponent("file0"))
        #expect(size >= 70_000 && size <= 70_000 + 16_384)
    }

    @Test("missing paths measure as zero rather than failing")
    func missingPath() {
        #expect(StorageAnalyzer.allocatedSize(of: URL(fileURLWithPath: "/nonexistent/\(UUID().uuidString)")) == 0)
    }
}
