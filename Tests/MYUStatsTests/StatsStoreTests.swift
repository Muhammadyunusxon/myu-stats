import Foundation
import Testing
@testable import MYUStats

/// Hands out canned readings and records what the store asked for.
private actor FakeSource: StatsSource {
    private(set) var requests: [SampleRequest] = []
    private(set) var resets = 0
    var next = StatsSample()
    var processList: [ProcessUsage] = []

    func setNext(_ sample: StatsSample) { next = sample }
    func setProcesses(_ list: [ProcessUsage]) { processList = list }

    func sample(_ request: SampleRequest) -> StatsSample {
        requests.append(request)
        var sample = next
        if !request.includeSlow { sample.disk = nil; sample.battery = nil; sample.networkInterface = nil }
        if !request.includeThermals { sample.thermals = nil }
        sample.processes = request.includeProcesses ? processList : nil
        return sample
    }

    func processes(reset: Bool) -> [ProcessUsage] {
        if reset { resets += 1 }
        return processList
    }
}

@MainActor
@Suite("Stats store")
struct StatsStoreTests {
    private static let disk = DiskUsage(name: "Macintosh HD", free: 100, total: 400)
    private static let battery = BatteryState(percent: 80, isCharging: false, isOnAC: false)

    private func sample(cpu: Double = 0.5) -> StatsSample {
        var sample = StatsSample()
        sample.cpu = CPUUsage(user: cpu, system: 0)
        sample.memory = MemoryUsage(used: 4, total: 8, app: 0, wired: 0, compressed: 0, cached: 0, swapUsed: 0,
                                    pressure: .normal)
        sample.network = NetworkRate(downBytesPerSecond: 1_000, upBytesPerSecond: 0)
        sample.disk = Self.disk
        sample.battery = Self.battery
        sample.thermals = ThermalState(cpuAverage: 50, fans: [])
        return sample
    }

    @Test("slow metrics are read every fifth tick and thermals every second one")
    func cadence() async {
        let source = FakeSource()
        let store = StatsStore(source: source)
        for _ in 0..<6 { await store.sampleNow() }
        let requests = await source.requests
        #expect(requests.map(\.includeSlow) == [true, false, false, false, false, true])
        #expect(requests.map(\.includeThermals) == [true, false, true, false, true, false])
    }

    @Test("values read only on slow ticks stay put in between")
    func slowValuesPersist() async {
        let source = FakeSource()
        await source.setNext(sample())
        let store = StatsStore(source: source)
        await store.sampleNow()
        await store.sampleNow()
        #expect(store.disk == Self.disk)
        #expect(store.battery == Self.battery)
        #expect(store.thermals?.cpuAverage == 50)
    }

    @Test("history keeps only the most recent samples")
    func historyIsCapped() async {
        let source = FakeSource()
        let store = StatsStore(source: source)
        for index in 0..<(StatsStore.historyLength + 5) {
            await source.setNext(sample(cpu: Double(index) / 100))
            await store.sampleNow()
        }
        #expect(store.cpuHistory.count == StatsStore.historyLength)
        #expect(store.cpuHistory.last == Double(StatsStore.historyLength + 4) / 100)
        #expect(store.networkHistory.count == StatsStore.historyLength)
    }

    @Test("processes are only read while something shows them")
    func processDemand() async throws {
        let source = FakeSource()
        let processes = (1...15).map { ProcessUsage(pid: Int32($0), name: "p\($0)", cpu: Double($0), memory: UInt64(16 - $0)) }
        await source.setProcesses(processes)
        let store = StatsStore(source: source)

        await store.sampleNow()
        #expect(await source.requests.last?.includeProcesses == false)
        #expect(store.topByCPU.isEmpty)

        store.setProcessSampling(true, for: "card")
        // Turning demand on reads processes straight away, from a clean baseline.
        try await waitUntil { await source.resets == 1 }
        await store.sampleNow()
        #expect(await source.requests.last?.includeProcesses == true)
        #expect(store.topByCPU.count == StatsStore.topProcessCount)
        #expect(store.topByCPU.first?.pid == 15)
        #expect(store.topByMemory.first?.pid == 1)

        store.setProcessSampling(false, for: "card")
        await store.sampleNow()
        #expect(await source.requests.last?.includeProcesses == false)
    }

    @Test("the pill hears when the battery or thermal sensors appear")
    func sensorsChanged() async {
        let source = FakeSource()
        let store = StatsStore(source: source)
        var changes = 0
        store.onSensorsChanged = { changes += 1 }
        await store.sampleNow() // nothing yet
        #expect(changes == 0)
        await source.setNext(sample())
        for _ in 0..<5 { await store.sampleNow() } // the fifth call is the next slow tick
        #expect(changes >= 1)
        let settled = changes
        for _ in 0..<5 { await store.sampleNow() }
        #expect(changes == settled)
    }

    private func waitUntil(_ condition: () async -> Bool) async throws {
        for _ in 0..<200 where !(await condition()) {
            try await Task.sleep(for: .milliseconds(5))
        }
        #expect(await condition())
    }
}
