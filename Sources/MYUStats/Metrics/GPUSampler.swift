import Foundation
import IOKit

/// Load and memory of each GPU, from the "PerformanceStatistics" every IOAccelerator publishes. Readable
/// without privileges on Apple silicon and on Intel Macs with AMD or Intel graphics.
final class GPUSampler {
    /// Names and core counts never change while a GPU is attached, and finding them walks the registry.
    private var identities: [UInt64: (name: String, cores: Int?, memoryTotal: UInt64?)] = [:]

    func sample() -> [GPUReading] {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"), &iterator) == KERN_SUCCESS
        else { return [] }
        defer { IOObjectRelease(iterator) }

        var readings: [GPUReading] = []
        var seen: Set<UInt64> = []
        while case let service = IOIteratorNext(iterator), service != 0 {
            defer { IOObjectRelease(service) }
            var entryID: UInt64 = 0
            IORegistryEntryGetRegistryEntryID(service, &entryID)
            guard let statistics = Self.property(service, "PerformanceStatistics") as? [String: Any] else { continue }
            let identity = identities[entryID] ?? Self.identity(of: service)
            identities[entryID] = identity
            seen.insert(entryID)
            if let reading = Self.reading(id: entryID, name: identity.name, cores: identity.cores,
                                          memoryTotal: identity.memoryTotal, statistics: statistics) {
                readings.append(reading)
            }
        }
        identities = identities.filter { seen.contains($0.key) }
        return readings
    }

    /// Reads the statistics dictionary. Apple silicon reports "Device", "Renderer" and "Tiler Utilization %"
    /// and "In use system memory"; AMD reports "Device Utilization %" and "vramUsedBytes". Without a
    /// utilization figure the entry is not a GPU worth showing.
    static func reading(id: UInt64, name: String, cores: Int?, memoryTotal: UInt64?, statistics: [String: Any]) -> GPUReading? {
        func percent(_ key: String) -> Double? {
            (statistics[key] as? NSNumber).map { max(0, min(1, $0.doubleValue / 100)) }
        }
        func bytes(_ key: String) -> UInt64? {
            (statistics[key] as? NSNumber).map(\.uint64Value)
        }
        guard let utilization = percent("Device Utilization %") ?? percent("GPU Activity(%)") else { return nil }
        return GPUReading(
            id: id,
            name: name,
            cores: cores,
            utilization: utilization,
            renderer: percent("Renderer Utilization %"),
            tiler: percent("Tiler Utilization %"),
            memoryUsed: bytes("In use system memory") ?? bytes("vramUsedBytes"),
            memoryTotal: memoryTotal
        )
    }

    /// The model name sits on the accelerator on Apple silicon ("Apple M3 Pro") and on its PCI device,
    /// as bytes, on Intel Macs; so does the video memory size of a discrete GPU.
    private static func identity(of service: io_service_t) -> (name: String, cores: Int?, memoryTotal: UInt64?) {
        let options = IOOptionBits(kIORegistryIterateRecursively | kIORegistryIterateParents)
        func search(_ key: String) -> Any? {
            IORegistryEntrySearchCFProperty(service, kIOServicePlane, key as CFString, kCFAllocatorDefault, options)
        }
        var name = String(localized: "Graphics")
        switch search("model") {
        case let text as String: name = text
        case let data as Data: name = String(decoding: data.prefix { $0 != 0 }, as: UTF8.self)
        default: break
        }
        let cores = (property(service, "gpu-core-count") as? NSNumber)?.intValue
        let megabytes = (search("VRAM,totalMB") as? NSNumber)?.uint64Value
        return (name, cores, megabytes.map { $0 << 20 })
    }

    private static func property(_ service: io_service_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(service, key as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue()
    }
}
