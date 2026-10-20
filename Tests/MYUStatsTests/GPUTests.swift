import Foundation
import Testing
@testable import MYUStats

@Suite("GPU statistics")
struct GPUTests {
    @Test("Apple silicon reports device, renderer and tiler load and the memory in use")
    func appleSilicon() throws {
        let statistics: [String: Any] = [
            "Device Utilization %": 37, "Renderer Utilization %": 30, "Tiler Utilization %": 12,
            "In use system memory": 982_204_416, "Alloc system memory": 4_959_649_792,
        ]
        let reading = try #require(GPUSampler.reading(id: 7, name: "Apple M3 Pro", cores: 14, memoryTotal: nil,
                                                      statistics: statistics))
        #expect(reading.utilization == 0.37)
        #expect(reading.renderer == 0.3)
        #expect(reading.tiler == 0.12)
        #expect(reading.memoryUsed == 982_204_416)
        #expect(reading.memoryTotal == nil)
        #expect(GPUText.subtitle(reading) == "Apple M3 Pro · 14 cores")
        #expect(GPUText.caption(reading) == "Renderer 30% · Tiler 12%")
    }

    @Test("a discrete GPU reports its video memory")
    func discrete() throws {
        let statistics: [String: Any] = ["Device Utilization %": 140, "vramUsedBytes": 1 << 30]
        let reading = try #require(GPUSampler.reading(id: 2, name: "AMD Radeon Pro 5500M", cores: nil,
                                                      memoryTotal: 8 << 30, statistics: statistics))
        #expect(reading.utilization == 1)
        #expect(reading.memoryUsed == 1 << 30)
        #expect(GPUText.caption(reading) == "100% busy")
    }

    @Test("an accelerator without a load figure is not a GPU to show")
    func noLoad() {
        #expect(GPUSampler.reading(id: 1, name: "x", cores: nil, memoryTotal: nil, statistics: ["recoveryCount": 0]) == nil)
    }

    @Test("this Mac's GPU can be read")
    func live() {
        let readings = GPUSampler().sample()
        #expect(readings.allSatisfy { (0...1).contains($0.utilization) && !$0.name.isEmpty })
    }
}
