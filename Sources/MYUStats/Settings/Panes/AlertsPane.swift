import SwiftUI

struct AlertsPane: View {
    var alerts: AlertMonitor
    var store: StatsStore
    @AppStorage(SettingsKey.alertClaude) private var claude = false
    @AppStorage(SettingsKey.alertClaudeThreshold) private var claudeThreshold = AlertSettings.defaultClaudeThreshold
    @AppStorage(SettingsKey.alertDisk) private var disk = false
    @AppStorage(SettingsKey.alertDiskFreeGB) private var diskFree = AlertSettings.defaultDiskFreeGB
    @AppStorage(SettingsKey.alertTemperature) private var temperature = false
    @AppStorage(SettingsKey.alertTemperatureLimit) private var temperatureLimit = AlertSettings.defaultTemperatureLimit
    @AppStorage(SettingsKey.alertProcess) private var process = false
    @AppStorage(SettingsKey.alertProcessMinutes) private var processMinutes = AlertSettings.defaultProcessMinutes
    @AppStorage(SettingsKey.alertMemory) private var memory = false

    private var anyOn: Bool { claude || disk || temperature || process || memory }

    var body: some View {
        Section {
            HStack {
                permissionText
                Spacer()
                if alerts.permission == .denied {
                    Button("Open System Settings") { alerts.openNotificationSettings() }
                }
                Button("Send a test") { alerts.sendTest() }
                    .disabled(alerts.permission == .unavailable || alerts.permission == .denied)
            }
        } header: {
            Text("Notifications")
        } footer: {
            Text("A notification opens the page it is about. Each alert comes once, and again only after things have gone back to normal.")
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .onAppear { Task { await alerts.refreshPermission() } }
        .onChange(of: anyOn) { _, on in
            if on { Task { await alerts.requestPermission() } }
        }

        Section {
            Toggle("When a limit gets close", isOn: $claude)
            Picker("Limit used", selection: $claudeThreshold) {
                ForEach(AlertSettings.claudeThresholds, id: \.self) { Text(Format.percent($0)).tag($0) }
            }
            .disabled(!claude)
        } header: {
            Text(verbatim: "Claude Code")
        } footer: {
            Text(store.claude.isInstalled
                 ? String(localized: "Once for the session and once for each weekly limit, until it resets. Read every 5 minutes.")
                 : String(localized: "Claude Code is not installed on this Mac."))
        }

        Section {
            Toggle("When the startup disk fills up", isOn: $disk)
            Picker("Free space below", selection: $diskFree) {
                ForEach(AlertSettings.diskFreeChoices, id: \.self) { Text(Format.diskBytes(Int64($0 * 1_000_000_000))).tag($0) }
            }
            .disabled(!disk)
        } header: {
            Text("Disk")
        }

        Section {
            Toggle("When the Mac runs hot", isOn: $temperature)
            Picker("Above", selection: $temperatureLimit) {
                ForEach(AlertSettings.temperatureChoices, id: \.self) { Text(Format.temperature($0)).tag($0) }
            }
            .disabled(!temperature)
        } header: {
            Text("Temperature")
        } footer: {
            Text(store.isAvailable(.thermals)
                 ? String(localized: "Only after a minute above the limit, so short spikes pass quietly.")
                 : String(localized: "No temperature sensors could be read on this Mac."))
        }

        Section {
            Toggle("When a process keeps a core busy", isOn: $process)
            Picker("For longer than", selection: $processMinutes) {
                ForEach(AlertSettings.processMinuteChoices, id: \.self) { Text("\(Int($0)) min").tag($0) }
            }
            .disabled(!process)
        } header: {
            Text("Processes")
        } footer: {
            Text("Busy means 90% of a core or more, the way a stuck build or a runaway script behaves. Your own processes only; checked every 30 seconds.")
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }

        Section {
            Toggle("When memory pressure turns critical", isOn: $memory)
        } header: {
            Text("Memory")
        } footer: {
            Text("After a minute at critical, when macOS is short of memory and the Mac slows down.")
        }
    }

    @ViewBuilder
    private var permissionText: some View {
        switch alerts.permission {
        case .allowed:
            Label("Notifications are allowed.", systemImage: "checkmark.circle.fill")
                .foregroundStyle(Level.color(for: 0))
        case .denied:
            Label("Notifications are off for MYU STATS in System Settings.", systemImage: "exclamationmark.triangle.fill")
                .foregroundStyle(Level.color(for: 0.7))
        case .notAsked:
            Text("macOS asks for permission when you turn on the first alert.").foregroundStyle(.secondary)
        case .unavailable:
            Text("Notifications work in the MYU STATS app, not in a bare build.").foregroundStyle(.secondary)
        }
    }
}
