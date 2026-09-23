import Foundation

/// Free space on the boot volume, including purgeable space macOS can reclaim.
enum DiskSampler {
    static func sample() -> DiskUsage? {
        let keys: Set<URLResourceKey> = [
            .volumeLocalizedNameKey,
            .volumeTotalCapacityKey,
            .volumeAvailableCapacityForImportantUsageKey,
        ]
        guard let values = try? URL(fileURLWithPath: "/").resourceValues(forKeys: keys),
              let total = values.volumeTotalCapacity,
              let free = values.volumeAvailableCapacityForImportantUsage
        else { return nil }
        return DiskUsage(name: values.volumeLocalizedName ?? "Startup Disk", free: free, total: Int64(total))
    }
}
