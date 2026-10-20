import Foundation

/// The alerts turned on in Settings › Alerts, with their limits; nil or false means off.
struct AlertSettings: Equatable {
    static let defaultClaudeThreshold = 0.8
    static let defaultDiskFreeGB = 10.0
    static let defaultTemperatureLimit = 90.0
    static let defaultProcessMinutes = 5.0

    static let claudeThresholds = [0.7, 0.8, 0.9, 0.95]
    static let diskFreeChoices = [5.0, 10, 20, 50]
    static let temperatureChoices = [80.0, 85, 90, 95]
    static let processMinuteChoices = [2.0, 5, 10, 15]

    /// Share of a Claude Code limit.
    var claudeThreshold: Double?
    /// Free space on the startup disk, in bytes as Finder counts them (1 GB = 10⁹ bytes).
    var diskFreeBytes: Int64?
    var temperatureLimit: Double?
    var processMinutes: Double?
    var memory = false

    var isAnyOn: Bool {
        claudeThreshold != nil || diskFreeBytes != nil || temperatureLimit != nil || processMinutes != nil || memory
    }

    init(claudeThreshold: Double? = nil, diskFreeBytes: Int64? = nil, temperatureLimit: Double? = nil,
         processMinutes: Double? = nil, memory: Bool = false) {
        self.claudeThreshold = claudeThreshold
        self.diskFreeBytes = diskFreeBytes
        self.temperatureLimit = temperatureLimit
        self.processMinutes = processMinutes
        self.memory = memory
    }

    init(_ defaults: UserDefaults) {
        func value(_ toggle: String, _ key: String) -> Double? {
            defaults.bool(forKey: toggle) ? defaults.double(forKey: key) : nil
        }
        claudeThreshold = value(SettingsKey.alertClaude, SettingsKey.alertClaudeThreshold)
        diskFreeBytes = value(SettingsKey.alertDisk, SettingsKey.alertDiskFreeGB).map { Int64($0 * 1_000_000_000) }
        temperatureLimit = value(SettingsKey.alertTemperature, SettingsKey.alertTemperatureLimit)
        processMinutes = value(SettingsKey.alertProcess, SettingsKey.alertProcessMinutes)
        memory = defaults.bool(forKey: SettingsKey.alertMemory)
    }
}

/// What the alerts look at in one check.
struct AlertSnapshot {
    var claude: ClaudeUsage?
    var disk: DiskUsage?
    var temperature: Double?
    var memoryPressure: MemoryPressure?
    /// Every process with its CPU (a share of one core) averaged over `processWindow`; nil when the
    /// processes were not read in this check.
    var processes: [ProcessUsage]?
    var processWindow: TimeInterval = 0
}

/// One notification.
struct Alert: Equatable {
    enum Kind: String {
        case claude, disk, temperature, process, memory, test
    }

    let kind: Kind
    /// A later alert with the same ID takes this one's place in Notification Centre.
    let id: String
    let title: String
    let body: String
    /// The Settings page a click on the notification opens.
    let section: SettingsSection
}

/// Decides when to speak up. Each alert fires once when its condition starts and stays quiet until the
/// condition has cleared; heat and memory pressure must last a minute first, and the same alert is
/// not repeated within half an hour, so a value hovering at a limit does not nag.
struct AlertEngine {
    /// How long heat and critical memory pressure must last: a short spike is normal.
    static let sustain: TimeInterval = 60
    static let cooldown: TimeInterval = 30 * 60
    /// A process counts as busy while it keeps at least this share of one core.
    static let busyShare = 0.9
    /// Checks further apart than this mean the Mac slept, so anything that must last starts over.
    static let maxGap: TimeInterval = 120
    /// Heat must drop this far below the limit before the alert can fire again.
    static let temperatureMargin = 5.0

    private var firedClaude: Set<String> = []
    private var diskFired = false
    private var hotSince: Date?
    private var hotFired = false
    private var criticalSince: Date?
    private var memoryFired = false
    private var busySince: [Int32: Date] = [:]
    private var firedProcesses: Set<Int32> = []
    private var lastSent: [String: Date] = [:]
    private var lastCheck: Date?

    mutating func evaluate(_ snapshot: AlertSnapshot, settings: AlertSettings, now: Date) -> [Alert] {
        if let lastCheck, now.timeIntervalSince(lastCheck) > Self.maxGap {
            hotSince = nil
            criticalSince = nil
            busySince.removeAll()
        }
        lastCheck = now
        return claude(snapshot.claude, threshold: settings.claudeThreshold, now: now)
            + disk(snapshot.disk, limit: settings.diskFreeBytes, now: now)
            + temperature(snapshot.temperature, limit: settings.temperatureLimit, now: now)
            + memory(snapshot.memoryPressure, isOn: settings.memory, now: now)
            + processes(snapshot.processes, window: snapshot.processWindow, minutes: settings.processMinutes, now: now)
    }

    // MARK: - Each alert

    /// Once per limit until it drops back under the threshold, which happens when it resets.
    private mutating func claude(_ usage: ClaudeUsage?, threshold: Double?, now: Date) -> [Alert] {
        guard let threshold else {
            firedClaude.removeAll()
            return []
        }
        // Stale figures were already looked at when they were fresh.
        guard let usage, usage.status == .ok else { return [] }
        var alerts: [Alert] = []
        for window in usage.windows {
            guard window.usedFraction >= threshold else {
                firedClaude.remove(window.id)
                continue
            }
            guard firedClaude.insert(window.id).inserted else { continue }
            let body = window.resetsAt.map { String(localized: "\(window.label) · resets in \(Format.timeUntil($0, now: now))") }
                ?? window.label
            alerts.append(Alert(kind: .claude, id: "claude.\(window.id)",
                                title: String(localized: "Claude Code limit at \(Format.percent(window.usedFraction))"),
                                body: body, section: .detail(.claude)))
        }
        return alerts
    }

    /// Once when free space falls under the limit; again only after a quarter more has been freed.
    private mutating func disk(_ disk: DiskUsage?, limit: Int64?, now: Date) -> [Alert] {
        guard let limit else {
            diskFired = false
            return []
        }
        guard let disk else { return [] }
        if disk.free >= limit + limit / 4 { diskFired = false }
        guard disk.free < limit, !diskFired, cooled("disk", now) else { return [] }
        diskFired = true
        lastSent["disk"] = now
        return [Alert(kind: .disk, id: "disk", title: String(localized: "Disk almost full"),
                      body: String(localized: "\(Format.diskBytes(disk.free)) free on \(disk.name). Disk details show what takes the space and which caches can go."),
                      section: .detail(.disk))]
    }

    private mutating func temperature(_ celsius: Double?, limit: Double?, now: Date) -> [Alert] {
        guard let limit else {
            hotSince = nil
            hotFired = false
            return []
        }
        guard let celsius else {
            hotSince = nil
            return []
        }
        if celsius < limit - Self.temperatureMargin { hotFired = false }
        guard celsius >= limit else {
            hotSince = nil
            return []
        }
        let since = hotSince ?? now
        hotSince = since
        guard now.timeIntervalSince(since) >= Self.sustain, !hotFired, cooled("temperature", now) else { return [] }
        hotFired = true
        lastSent["temperature"] = now
        return [Alert(kind: .temperature, id: "temperature",
                      title: String(localized: "Running hot: \(Format.temperature(celsius))"),
                      body: String(localized: "Above \(Format.temperature(limit)) for over a minute."),
                      section: .detail(.thermals))]
    }

    private mutating func memory(_ pressure: MemoryPressure?, isOn: Bool, now: Date) -> [Alert] {
        guard isOn else {
            criticalSince = nil
            memoryFired = false
            return []
        }
        guard let pressure else { return [] }
        if pressure == .normal { memoryFired = false }
        guard pressure == .critical else {
            criticalSince = nil
            return []
        }
        let since = criticalSince ?? now
        criticalSince = since
        guard now.timeIntervalSince(since) >= Self.sustain, !memoryFired, cooled("memory", now) else { return [] }
        memoryFired = true
        lastSent["memory"] = now
        return [Alert(kind: .memory, id: "memory", title: String(localized: "Memory pressure is critical"),
                      body: String(localized: "Quit apps you are not using, or the Mac will keep slowing down."),
                      section: .detail(.memory))]
    }

    /// A process busy through every reading for `minutes`. The first busy reading counts from the start
    /// of its window, since the average says it was busy all along.
    private mutating func processes(_ list: [ProcessUsage]?, window: TimeInterval, minutes: Double?, now: Date) -> [Alert] {
        guard let minutes else {
            busySince.removeAll()
            firedProcesses.removeAll()
            return []
        }
        guard let list else { return [] }
        var alerts: [Alert] = []
        var stillBusy: [Int32: Date] = [:]
        for process in list where process.cpu >= Self.busyShare {
            let since = busySince[process.pid] ?? now.addingTimeInterval(-window)
            stillBusy[process.pid] = since
            // A few seconds' slack for timer drift.
            guard now.timeIntervalSince(since) >= minutes * 60 - 5, !firedProcesses.contains(process.pid) else { continue }
            let key = "process.\(process.name)"
            guard cooled(key, now) else { continue }
            firedProcesses.insert(process.pid)
            lastSent[key] = now
            alerts.append(Alert(kind: .process, id: "process.\(process.pid)",
                                title: String(localized: "\(process.name) is keeping a core busy"),
                                body: String(localized: "\(Format.percent(process.cpu)) CPU for over \(Int(minutes)) minutes."),
                                section: .detail(.cpu)))
        }
        busySince = stillBusy
        firedProcesses.formIntersection(stillBusy.keys)
        return alerts
    }

    private func cooled(_ key: String, _ now: Date) -> Bool {
        lastSent[key].map { now.timeIntervalSince($0) >= Self.cooldown } ?? true
    }
}
