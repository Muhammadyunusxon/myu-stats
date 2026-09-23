import SwiftUI

/// Content of the hover card for one metric.
struct DetailCard: View {
    var metric: StatMetric
    @ObservedObject var store: StatsStore
    var isDetailsHovered = false

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            switch metric {
            case .cpu: CPUCard(store: store)
            case .memory: MemoryCard(store: store)
            case .network: NetworkCard(store: store)
            case .disk: DiskCard(store: store)
            case .thermals: ThermalsCard(store: store)
            case .battery: BatteryCard(store: store)
            }
            CardDivider()
            DetailsButton(isHovered: isDetailsHovered)
        }
        .padding(EdgeLayout.cardPadding)
        .frame(width: EdgeLayout.cardWidth, alignment: .topLeading)
    }
}

/// Footer row that opens this metric's page in Settings. Clicks are routed by EdgeController,
/// since the panel only takes the mouse over its interactive spots.
private struct DetailsButton: View {
    var isHovered: Bool

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "chart.xyaxis.line")
                .font(.system(size: 10, weight: .semibold))
            Text("Details")
                .font(.system(size: 11, weight: .semibold))
            Spacer()
            Image(systemName: "chevron.right")
                .font(.system(size: 9, weight: .bold))
                .offset(x: isHovered ? 2 : 0)
        }
        .foregroundStyle(.white.opacity(isHovered ? 1 : 0.75))
        .padding(.horizontal, 8)
        .frame(height: EdgeLayout.detailsButtonHeight)
        .background(
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(Color.white.opacity(isHovered ? 0.14 : 0.06))
        )
        .animation(.easeOut(duration: 0.12), value: isHovered)
    }
}

private struct CPUCard: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        let cpu = store.cpu
        let color = Level.color(for: cpu.total)
        CardHeader(symbol: "cpu", title: "CPU", subtitle: store.cpuInfo.brand)
        UsageSection(
            title: "Usage",
            trailing: Format.percent(cpu.total),
            fraction: cpu.total,
            color: color,
            caption: "User \(Format.percent(cpu.user)) · System \(Format.percent(cpu.system)) · Idle \(Format.percent(cpu.idle))"
        )
        Sparkline(values: store.cpuHistory, color: color, ceiling: 1).frame(height: 26)
        CardDivider()
        DetailRow(label: "Load average", value: store.loadAverage.map { String(format: "%.2f", $0) }.joined(separator: " · "))
        DetailRow(label: "Cores", value: store.cpuInfo.coresDescription)
        DetailRow(label: "Uptime", value: Format.uptime(ProcessInfo.processInfo.systemUptime))
        if UserDefaults.standard.bool(forKey: SettingsKey.showProcesses) {
            CardDivider()
            ProcessList(title: "Top processes", processes: Array(store.topByCPU.prefix(4))) { Format.percent($0.cpu) }
        }
    }
}

private struct MemoryCard: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        let memory = store.memory
        let color = Level.color(for: memory.pressure)
        CardHeader(
            symbol: "memorychip",
            title: "Memory",
            subtitle: "\(Format.bytes(memory.total)) · pressure \(memory.pressure.title)"
        )
        UsageSection(
            title: "Used",
            trailing: Format.bytes(memory.used),
            fraction: memory.fraction,
            color: color,
            caption: "\(Format.percent(memory.fraction)) used · \(Format.bytes(memory.available)) available"
        )
        CardDivider()
        DetailRow(label: "App memory", value: Format.bytes(memory.app))
        DetailRow(label: "Wired", value: Format.bytes(memory.wired))
        DetailRow(label: "Compressed", value: Format.bytes(memory.compressed))
        DetailRow(label: "Cached files", value: Format.bytes(memory.cached))
        DetailRow(
            label: "Swap used",
            value: Format.bytes(memory.swapUsed),
            valueColor: memory.swapUsed > 1 << 30 ? Level.color(for: 0.7) : .white.opacity(0.6)
        )
        if UserDefaults.standard.bool(forKey: SettingsKey.showProcesses) {
            CardDivider()
            ProcessList(title: "Top processes", processes: Array(store.topByMemory.prefix(4))) { Format.bytes($0.memory) }
        }
    }
}

private struct NetworkCard: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        let subtitle = store.networkInterface.map { "\($0.name) · \($0.address)" } ?? "Offline"
        CardHeader(symbol: "network", title: "Network", subtitle: subtitle)
        HStack(spacing: 8) {
            RateTile(symbol: "arrow.down", label: "Download", value: Format.rate(store.network.downBytesPerSecond))
            RateTile(symbol: "arrow.up", label: "Upload", value: Format.rate(store.network.upBytesPerSecond))
        }
        Sparkline(values: store.networkHistory, color: Level.accent, ceiling: nil).frame(height: 28)
        CardDivider()
        DetailRow(label: "Peak (\(Int(store.historyDuration)) s)", value: Format.rate(store.networkPeak))
        DetailRow(label: "Received since boot", value: Format.bytes(store.networkTotals.received))
        DetailRow(label: "Sent since boot", value: Format.bytes(store.networkTotals.sent))
    }
}

private struct RateTile: View {
    var symbol: String
    var label: String
    var value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Label(label, systemImage: symbol)
                .font(.system(size: 9.5, weight: .medium))
                .foregroundStyle(.white.opacity(0.6))
            Text(value)
                .font(.system(size: 12.5, weight: .semibold, design: .rounded).monospacedDigit())
                .foregroundStyle(Level.accent)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.white.opacity(0.07)))
    }
}

private struct DiskCard: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        if let disk = store.disk {
            let color = Level.color(for: disk.usedFraction)
            CardHeader(symbol: "internaldrive", title: "Disk", subtitle: disk.name)
            UsageSection(
                title: "Used",
                trailing: "\(Format.diskBytes(disk.used)) of \(Format.diskBytes(disk.total))",
                fraction: disk.usedFraction,
                color: color,
                caption: "\(Format.percent(disk.usedFraction)) used · \(Format.diskBytes(disk.free)) free"
            )
            CardDivider()
            DetailRow(label: "Free", value: Format.diskBytes(disk.free), valueColor: color)
            DetailRow(label: "Capacity", value: Format.diskBytes(disk.total))
            if disk.usedFraction >= 0.9 {
                CardDivider()
                Label("Low disk space — macOS may slow down", systemImage: "exclamationmark.triangle.fill")
                    .font(.system(size: 10, weight: .medium))
                    .foregroundStyle(color)
            }
        } else {
            CardHeader(symbol: "internaldrive", title: "Disk", subtitle: "Unavailable")
        }
    }
}

private struct ThermalsCard: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        if let thermals = store.thermals {
            let headline = thermals.headline ?? 0
            let spinning = thermals.fans.filter(\.isSpinning).count
            CardHeader(
                symbol: "fan",
                title: "Thermals",
                subtitle: thermals.fans.isEmpty
                    ? "Fanless"
                    : "\(thermals.fans.count) fans · " + (spinning == 0 ? "idle" : "\(spinning) spinning")
            )
            UsageSection(
                title: "CPU",
                trailing: Format.temperature(thermals.cpuAverage),
                fraction: headline / 110,
                color: Level.temperatureColor(headline),
                caption: "Hottest core \(Format.temperature(thermals.cpuHottest))"
            )
            CardDivider()
            DetailRow(label: "Efficiency cores", value: Format.temperature(thermals.efficiencyAverage))
            DetailRow(label: "GPU", value: Format.temperature(thermals.gpuAverage))
            DetailRow(label: "Battery", value: Format.temperature(thermals.battery))
            if !thermals.fans.isEmpty {
                CardDivider()
                VStack(alignment: .leading, spacing: 8) {
                    SectionTitle(text: "Fans")
                    ForEach(thermals.fans) { fan in
                        FanRow(fan: fan)
                    }
                }
            }
        } else {
            CardHeader(symbol: "fan", title: "Thermals", subtitle: "Sensors unavailable")
        }
    }
}

private struct FanRow: View {
    var fan: FanReading

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            DetailRow(
                label: "Fan \(fan.index + 1)",
                value: fan.isManual
                    ? "Manual · \(Format.rpm(fan.rpm))"
                    : (fan.isSpinning ? Format.rpm(fan.rpm) : "Off"),
                valueColor: fan.isSpinning ? Level.accent : .white.opacity(0.6)
            )
            BarGauge(fraction: fan.fraction, color: Level.accent)
            Text("\(Int(fan.minRPM))–\(Int(fan.maxRPM)) rpm"
                 + (fan.isManual ? " · held by MYU STATS" : fan.isSpinning ? "" : " · passive cooling"))
                .font(.system(size: 9.5).monospacedDigit())
                .foregroundStyle(.white.opacity(0.5))
        }
    }
}

private struct BatteryCard: View {
    @ObservedObject var store: StatsStore

    var body: some View {
        if let battery = store.battery {
            let color = Level.batteryColor(battery)
            CardHeader(
                symbol: battery.isCharging ? "battery.100percent.bolt" : "battery.75percent",
                title: "Battery",
                subtitle: powerSource(battery)
            )
            UsageSection(
                title: "Charge",
                trailing: "\(battery.percent)%",
                fraction: Double(battery.percent) / 100,
                color: color,
                caption: timeCaption(battery)
            )
            CardDivider()
            if let health = battery.healthPercent {
                DetailRow(label: "Health", value: "\(health)%", valueColor: health < 80 ? Level.color(for: 0.7) : .white.opacity(0.6))
            }
            if let cycles = battery.cycleCount {
                DetailRow(label: "Cycle count", value: "\(cycles)")
            }
            if let temperature = battery.temperatureCelsius ?? store.thermals?.battery {
                DetailRow(label: "Temperature", value: String(format: "%.1f °C", temperature))
            }
        } else {
            CardHeader(symbol: "powerplug", title: "Power", subtitle: "No battery")
        }
    }

    private func powerSource(_ battery: BatteryState) -> String {
        guard battery.isOnAC else { return "On battery" }
        return battery.adapterWatts.map { "Power adapter · \($0) W" } ?? "Power adapter"
    }

    private func timeCaption(_ battery: BatteryState) -> String {
        if battery.isCharging {
            return battery.minutesRemaining.map { "Charging · full in \(Format.duration(minutes: $0))" } ?? "Charging"
        }
        if battery.isOnAC { return "Not charging" }
        return battery.minutesRemaining.map { "\(Format.duration(minutes: $0)) remaining" } ?? "Estimating…"
    }
}
