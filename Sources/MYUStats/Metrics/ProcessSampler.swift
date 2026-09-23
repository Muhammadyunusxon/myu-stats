import AppKit
import Darwin

/// Per-process CPU and memory for the current user's processes.
/// Processes owned by other users (root daemons, WindowServer) are not readable without privileges.
final class ProcessSampler {
    private var previousCPUTime: [Int32: UInt64] = [:]
    private var previousTimestamp: UInt64 = 0
    private var names: [Int32: String] = [:]

    func reset() {
        previousCPUTime.removeAll()
        previousTimestamp = 0
    }

    func sample() -> [ProcessUsage] {
        let capacity = Int(proc_listallpids(nil, 0)) + 64
        guard capacity > 64 else { return [] }
        var pids = [Int32](repeating: 0, count: capacity)
        let count = Int(proc_listallpids(&pids, Int32(capacity * MemoryLayout<Int32>.size)))

        // ri_*_time and mach_absolute_time share units, so their ratio needs no timebase conversion.
        let now = mach_absolute_time()
        let elapsed = previousTimestamp > 0 ? Double(now - previousTimestamp) : 0
        var cpuTimes: [Int32: UInt64] = [:]
        var result: [ProcessUsage] = []
        // One workspace query per sample instead of an NSRunningApplication lookup per pid.
        let appNames = Dictionary(
            NSWorkspace.shared.runningApplications.compactMap { app in app.localizedName.map { (app.processIdentifier, $0) } },
            uniquingKeysWith: { first, _ in first }
        )

        for pid in pids.prefix(max(0, count)) where pid > 0 {
            var info = rusage_info_v2()
            let status = withUnsafeMutablePointer(to: &info) {
                $0.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) {
                    proc_pid_rusage(pid, RUSAGE_INFO_V2, $0)
                }
            }
            guard status == 0 else { continue }

            let cpuTime = info.ri_user_time + info.ri_system_time
            cpuTimes[pid] = cpuTime
            var cpu = 0.0
            if elapsed > 0, let before = previousCPUTime[pid], cpuTime >= before {
                cpu = Double(cpuTime - before) / elapsed
            }
            result.append(ProcessUsage(pid: pid, name: name(for: pid, appNames: appNames), cpu: cpu, memory: info.ri_phys_footprint))
        }

        previousCPUTime = cpuTimes
        previousTimestamp = now
        names = names.filter { cpuTimes[$0.key] != nil }
        return result
    }

    private func name(for pid: Int32, appNames: [Int32: String]) -> String {
        if let cached = names[pid] { return cached }
        var resolved = appNames[pid]
        if resolved == nil {
            var buffer = [CChar](repeating: 0, count: 256)
            if proc_name(pid, &buffer, UInt32(buffer.count)) > 0 {
                resolved = String(cString: buffer)
            }
        }
        let name = resolved ?? "pid \(pid)"
        names[pid] = name
        return name
    }
}
