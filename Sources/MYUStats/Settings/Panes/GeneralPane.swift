import ServiceManagement
import SwiftUI

struct GeneralPane: View {
    @AppStorage(SettingsKey.showProcesses) private var showProcesses = true
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var loginError: String?

    var body: some View {
        Section {
            Toggle("Open MYU STATS at login", isOn: $launchAtLogin)
                .onChange(of: launchAtLogin) { _, enabled in setLaunchAtLogin(enabled) }
            if let loginError {
                Text(loginError).font(.caption).foregroundStyle(SettingsPalette.destructive)
            }
        }
        Section("Performance") {
            Toggle("Show top processes", isOn: $showProcesses)
            Text("Scanning processes costs a few percent CPU while the CPU or Memory card is open.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        Section {
            HStack {
                Text("Reset every setting to its default")
                Spacer()
                Button("Reset", role: .destructive) { resetSettings() }
            }
        }
    }

    private func setLaunchAtLogin(_ enabled: Bool) {
        guard enabled != (SMAppService.mainApp.status == .enabled) else { return }
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            loginError = nil
        } catch {
            Log.settings.error("Launch at login \(enabled ? "register" : "unregister") failed: \(error.localizedDescription, privacy: .public)")
            loginError = error.localizedDescription
            launchAtLogin = SMAppService.mainApp.status == .enabled
        }
    }

    private func resetSettings() {
        UserDefaults.standard.resetMYUStatsSettings()
    }
}
