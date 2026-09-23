import Foundation

/// Free space on the boot volume, including purgeable space macOS can reclaim.
enum DiskSampler {
    static func sample() -> DiskUsage? {
        let keys: Set<URLResourceKey> = [
            .volumeLocalizedNameKey,
            .volumeTotalCapacityKey,
            .volumeAvailableCapacityForImportantUsageKey,
        ]
        let values: URLResourceValues
        do {
            values = try URL(fileURLWithPath: "/").resourceValues(forKeys: keys)
        } catch {
            Log.sampling.error("Startup disk capacity unavailable: \(error.localizedDescription, privacy: .public)")
            return nil
        }
        guard let total = values.volumeTotalCapacity, let free = values.volumeAvailableCapacityForImportantUsage
        else { return nil }
        return DiskUsage(name: values.volumeLocalizedName ?? "Startup Disk", free: free, total: Int64(total))
    }
}
