import Darwin

/// Whole-machine CPU load, computed from tick deltas between two samples.
final class CPUSampler {
    private let host = mach_host_self()
    private var previous: host_cpu_load_info?

    func sample() -> CPUUsage? {
        var info = host_cpu_load_info()
        var count = mach_msg_type_number_t(
            MemoryLayout<host_cpu_load_info>.stride / MemoryLayout<integer_t>.stride
        )
        let result = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics(host, HOST_CPU_LOAD_INFO, $0, &count)
            }
        }
        guard result == KERN_SUCCESS else {
            Log.sampling.error("host_statistics(HOST_CPU_LOAD_INFO) failed: \(result)")
            return nil
        }
        defer { previous = info }
        guard let prev = previous else { return nil }

        // Tick order: user, system, idle, nice.
        let user = Double(info.cpu_ticks.0 &- prev.cpu_ticks.0)
        let system = Double(info.cpu_ticks.1 &- prev.cpu_ticks.1)
        let idle = Double(info.cpu_ticks.2 &- prev.cpu_ticks.2)
        let nice = Double(info.cpu_ticks.3 &- prev.cpu_ticks.3)
        let total = user + system + idle + nice
        guard total > 0 else { return nil }
        return CPUUsage(user: (user + nice) / total, system: system / total)
    }
}

/// Load of each logical core, from per-processor tick deltas.
final class PerCoreSampler {
    private let host = mach_host_self()
    private var previous: [[UInt32]] = []

    func sample() -> [Double] {
        var cpuCount: natural_t = 0
        var info: processor_info_array_t?
        var infoCount: mach_msg_type_number_t = 0
        let result = host_processor_info(host, PROCESSOR_CPU_LOAD_INFO, &cpuCount, &info, &infoCount)
        guard result == KERN_SUCCESS, let info else {
            Log.sampling.error("host_processor_info failed: \(result)")
            return []
        }
        defer {
            vm_deallocate(mach_task_self_, vm_address_t(bitPattern: info),
                          vm_size_t(Int(infoCount) * MemoryLayout<integer_t>.stride))
        }

        let states = Int(CPU_STATE_MAX)
        let current: [[UInt32]] = (0..<Int(cpuCount)).map { core in
            (0..<states).map { UInt32(bitPattern: info[core * states + $0]) }
        }
        defer { previous = current }
        guard previous.count == current.count else { return [] }

        return zip(current, previous).map { now, before in
            let ticks = (0..<states).map { Double(now[$0] &- before[$0]) }
            let total = ticks.reduce(0, +)
            let idle = ticks[Int(CPU_STATE_IDLE)]
            return total > 0 ? (total - idle) / total : 0
        }
    }
}
