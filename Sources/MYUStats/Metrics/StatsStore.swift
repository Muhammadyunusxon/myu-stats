import Foundation
import Observation

/// Samples every metric on a fixed interval and publishes the latest values.
///
/// Readings are taken by a `StatsSource` off the main thread; only the results land here. With
/// Observation, each view re-renders only when a value it actually reads changes.
@MainActor
@Observable
final class StatsStore {
    static let historyLength = 40
    static let topProcessCount = 10

    let cpuInfo = CPUInfo.current
    /// On-demand "what takes space" analysis for the Disk details page.
    let storage = StorageAnalyzer()
    /// Changes fan speed through the privileged helper.
    let fans = FanController()

    private(set) var cpu: CPUUsage = .zero
    private(set) var cpuHistory: [Double] = []
    private(set) var perCore: [Double] = []
    private(set) var loadAverage: [Double] = [0, 0, 0]
    private(set) var memory: MemoryUsage = .zero
    private(set) var memoryHistory: [Double] = []
    private(set) var network: NetworkRate = .zero
    private(set) var networkHistory: [Double] = []
    private(set) var networkTotals = NetworkTotals(received: 0, sent: 0)
    private(set) var networkInterface: NetworkInterface?
    private(set) var disk: DiskUsage?
    private(set) var battery: BatteryState?
    private(set) var thermals: ThermalState?
    private(set) var topByCPU: [ProcessUsage] = []
    private(set) var topByMemory: [ProcessUsage] = []
    private(set) var interval: TimeInterval = 1

    var networkPeak: Double { networkHistory.max() ?? 0 }

    /// How far back the sparklines reach.
    var historyDuration: TimeInterval { Double(Self.historyLength) * interval }

    /// Called when the battery or the thermal sensors appear or disappear, so the pill can re-lay itself out.
    @ObservationIgnored var onSensorsChanged: (() -> Void)?

    @ObservationIgnored private let source: StatsSource
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var tick = 0
    @ObservationIgnored private var isSampling = false

    /// Process scanning is the costliest sampler, so it only runs while something shows processes.
    /// Each caller (the hover card, the Settings detail pane) holds its own claim.
    @ObservationIgnored private var processDemand: Set<String> = [] {
        didSet {
            guard processDemand.isEmpty != oldValue.isEmpty, !processDemand.isEmpty else { return }
            Task { await refreshProcesses() }
        }
    }

    init(source: StatsSource = SamplingEngine()) {
        self.source = source
    }

    func setProcessSampling(_ enabled: Bool, for client: String) {
        if enabled { processDemand.insert(client) } else { processDemand.remove(client) }
    }

    func start(interval: TimeInterval = 1) {
        self.interval = interval
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.timerFired() }
        }
        timer?.tolerance = interval * 0.2
        timerFired()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    private func timerFired() {
        // A slow tick (the first thermal probe, a busy disk) must not pile up behind itself.
        guard !isSampling else { return }
        isSampling = true
        Task {
            await sampleNow()
            isSampling = false
        }
    }

    /// Takes one reading and publishes it. The timer drives this; tests call it directly.
    func sampleNow() async {
        // Disk, battery and addresses change slowly; sensors move over seconds.
        let request = SampleRequest(
            includeSlow: tick % 5 == 0,
            includeThermals: tick % 2 == 0,
            includeProcesses: !processDemand.isEmpty
        )
        tick += 1
        apply(await source.sample(request), for: request)
    }

    private func apply(_ sample: StatsSample, for request: SampleRequest) {
        let hadSensors = (battery != nil, thermals != nil)

        if let value = sample.cpu {
            cpu = value
            cpuHistory = Self.appending(value.total, to: cpuHistory)
        }
        update(\.perCore, to: sample.perCore)
        if let loads = sample.loadAverage { update(\.loadAverage, to: loads) }
        if let value = sample.memory {
            memory = value
            memoryHistory = Self.appending(value.fraction, to: memoryHistory)
        }
        network = sample.network
        networkHistory = Self.appending(sample.network.total, to: networkHistory)
        update(\.networkTotals, to: sample.networkTotals)

        if request.includeSlow {
            update(\.disk, to: sample.disk)
            update(\.battery, to: sample.battery)
            update(\.networkInterface, to: sample.networkInterface)
        }
        if request.includeThermals {
            update(\.thermals, to: sample.thermals)
        }
        // Demand may have ended while the reading was in flight.
        if let processes = sample.processes, !processDemand.isEmpty {
            publishTop(processes)
        }

        if (battery != nil, thermals != nil) != hadSensors { onSensorsChanged?() }
    }

    private func refreshProcesses() async {
        let processes = await source.processes(reset: true)
        if !processDemand.isEmpty { publishTop(processes) }
    }

    private func publishTop(_ processes: [ProcessUsage]) {
        update(\.topByCPU, to: Array(processes.sorted { $0.cpu > $1.cpu }.prefix(Self.topProcessCount)))
        update(\.topByMemory, to: Array(processes.sorted { $0.memory > $1.memory }.prefix(Self.topProcessCount)))
    }

    /// Writes only real changes: every write notifies the views reading that value.
    private func update<Value: Equatable>(_ keyPath: ReferenceWritableKeyPath<StatsStore, Value>, to value: Value) {
        if self[keyPath: keyPath] != value { self[keyPath: keyPath] = value }
    }

    static func appending(_ value: Double, to history: [Double]) -> [Double] {
        var next = history
        next.append(value)
        if next.count > historyLength { next.removeFirst(next.count - historyLength) }
        return next
    }
}
