import Combine
import Foundation

/// Samples every metric on a fixed interval and publishes the latest values.
@MainActor
final class StatsStore: ObservableObject {
    static let historyLength = 40
    static let topProcessCount = 10

    let cpuInfo = CPUInfo.current
    /// On-demand "what takes space" analysis for the Disk details page.
    let storage = StorageAnalyzer()
    /// Changes fan speed through the privileged helper.
    let fans = FanController()

    @Published private(set) var cpu: CPUUsage = .zero
    @Published private(set) var cpuHistory: [Double] = []
    @Published private(set) var perCore: [Double] = []
    @Published private(set) var loadAverage: [Double] = [0, 0, 0]
    @Published private(set) var memory: MemoryUsage = .zero
    @Published private(set) var memoryHistory: [Double] = []
    @Published private(set) var network: NetworkRate = .zero
    @Published private(set) var networkHistory: [Double] = []
    @Published private(set) var networkTotals = NetworkTotals(received: 0, sent: 0)
    @Published private(set) var networkInterface: NetworkInterface?
    @Published private(set) var disk: DiskUsage?
    @Published private(set) var battery: BatteryState?
    @Published private(set) var thermals: ThermalState?
    @Published private(set) var topByCPU: [ProcessUsage] = []
    @Published private(set) var topByMemory: [ProcessUsage] = []

    var networkPeak: Double { networkHistory.max() ?? 0 }

    private let cpuSampler = CPUSampler()
    private let perCoreSampler = PerCoreSampler()
    private let memorySampler = MemorySampler()
    private let networkSampler = NetworkSampler()
    private let processSampler = ProcessSampler()
    private let thermalSampler = ThermalSampler()
    private var timer: Timer?
    private var tick = 0

    /// Process scanning is the costliest sampler, so it only runs while something shows processes.
    /// Each caller (the hover card, the Settings detail pane) holds its own claim.
    private var processDemand: Set<String> = [] {
        didSet {
            guard processDemand.isEmpty != oldValue.isEmpty else { return }
            processSampler.reset()
            if !processDemand.isEmpty { sampleProcesses() }
        }
    }

    func setProcessSampling(_ enabled: Bool, for client: String) {
        if enabled { processDemand.insert(client) } else { processDemand.remove(client) }
    }

    private(set) var interval: TimeInterval = 1

    /// How far back the sparklines reach.
    var historyDuration: TimeInterval { Double(Self.historyLength) * interval }

    func start(interval: TimeInterval = 1) {
        self.interval = interval
        sample()
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sample() }
        }
        timer?.tolerance = interval * 0.2
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    private func sample() {
        if let value = cpuSampler.sample() {
            cpu = value
            cpuHistory = Self.appending(value.total, to: cpuHistory)
        }
        perCore = perCoreSampler.sample()
        var loads = [Double](repeating: 0, count: 3)
        if getloadavg(&loads, 3) == 3 { loadAverage = loads }

        if let value = memorySampler.sample() {
            memory = value
            memoryHistory = Self.appending(value.fraction, to: memoryHistory)
        }

        network = networkSampler.sample()
        networkTotals = networkSampler.totals
        networkHistory = Self.appending(network.total, to: networkHistory)

        // Disk, battery and addresses change slowly; no need to hit them every second.
        if tick % 5 == 0 {
            disk = DiskSampler.sample()
            battery = BatterySampler.sample()
            networkInterface = NetworkSampler.primaryInterface()
        }
        // SMC reads are cheap but sensors only move over seconds.
        if tick % 2 == 0, let thermalSampler {
            thermals = thermalSampler.sample()
        }
        if !processDemand.isEmpty { sampleProcesses() }
        tick += 1
    }

    private func sampleProcesses() {
        let processes = processSampler.sample()
        topByCPU = Array(processes.sorted { $0.cpu > $1.cpu }.prefix(Self.topProcessCount))
        topByMemory = Array(processes.sorted { $0.memory > $1.memory }.prefix(Self.topProcessCount))
    }

    private static func appending(_ value: Double, to history: [Double]) -> [Double] {
        var next = history
        next.append(value)
        if next.count > historyLength { next.removeFirst(next.count - historyLength) }
        return next
    }
}
