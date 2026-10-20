import Foundation

/// What one sampling tick should read. Slow-moving values are only read every few ticks.
struct SampleRequest: Equatable, Sendable {
    /// Battery and the active network interface.
    var includeSlow: Bool
    /// Free space on the startup disk: asking for it wakes the purgeable-space service, so it is read least often.
    var includeDisk = false
    var includeThermals: Bool
    var includeProcesses: Bool
}

/// Everything one tick read. Optional values are nil when they were not asked for or could not be read.
struct StatsSample: Equatable, Sendable {
    var cpu: CPUUsage?
    var perCore: [Double] = []
    var gpus: [GPUReading] = []
    var loadAverage: [Double]?
    var memory: MemoryUsage?
    var network: NetworkRate = .zero
    var networkTotals = NetworkTotals(received: 0, sent: 0)
    var disk: DiskUsage?
    var battery: BatteryState?
    var networkInterface: NetworkInterface?
    var thermals: ThermalState?
    var processes: [ProcessUsage]?
}

/// Where `StatsStore` gets its readings; a fake stands in for it in tests.
protocol StatsSource: Sendable {
    func sample(_ request: SampleRequest) async -> StatsSample
    /// Current processes; `reset` drops the previous CPU times, so the first reading after it is zero.
    func processes(reset: Bool) async -> [ProcessUsage]
    /// Processes with their CPU averaged since the previous call, for the alerts; kept apart from
    /// `processes`, whose readings are a second apart.
    func watchProcesses() async -> [ProcessUsage]
}

/// Runs every sampler off the main thread. Samplers keep state between ticks (tick counters,
/// previous byte counts), and the actor keeps them to one caller at a time.
actor SamplingEngine: StatsSource {
    private let cpuSampler = CPUSampler()
    private let perCoreSampler = PerCoreSampler()
    private let memorySampler = MemorySampler()
    private let networkSampler = NetworkSampler()
    private let processSampler = ProcessSampler()
    private let watchSampler = ProcessSampler()
    private let gpuSampler = GPUSampler()
    /// Opened on the first thermal read: discovering sensor keys walks thousands of SMC keys.
    private var thermalSampler: ThermalSampler?
    private var didProbeThermals = false

    func sample(_ request: SampleRequest) -> StatsSample {
        var sample = StatsSample()
        sample.cpu = cpuSampler.sample()
        sample.perCore = perCoreSampler.sample()
        sample.gpus = gpuSampler.sample()
        var loads = [Double](repeating: 0, count: 3)
        if getloadavg(&loads, 3) == 3 { sample.loadAverage = loads }
        sample.memory = memorySampler.sample()
        sample.network = networkSampler.sample()
        sample.networkTotals = networkSampler.totals

        if request.includeDisk {
            sample.disk = DiskSampler.sample()
        }
        if request.includeSlow {
            sample.battery = BatterySampler.sample()
            sample.networkInterface = NetworkSampler.primaryInterface()
        }
        if request.includeThermals {
            sample.thermals = thermals()?.sample()
        }
        if request.includeProcesses {
            sample.processes = processSampler.sample()
        }
        return sample
    }

    func processes(reset: Bool) -> [ProcessUsage] {
        if reset { processSampler.reset() }
        return processSampler.sample()
    }

    func watchProcesses() -> [ProcessUsage] {
        watchSampler.sample()
    }

    private func thermals() -> ThermalSampler? {
        if !didProbeThermals {
            didProbeThermals = true
            let start = ContinuousClock.now
            thermalSampler = ThermalSampler()
            let elapsed = ContinuousClock.now - start
            if thermalSampler == nil {
                Log.sampling.notice("No thermal sensors or fans found; the Thermals ring stays hidden")
            } else {
                Log.sampling.info("Thermal sensors discovered in \(elapsed.formatted(.units(allowed: [.milliseconds])), privacy: .public)")
            }
        }
        return thermalSampler
    }
}
