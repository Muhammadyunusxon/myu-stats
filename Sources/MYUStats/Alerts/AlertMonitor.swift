import AppKit
import Foundation
import Observation
import UserNotifications

/// Checks the store every few seconds against the alerts turned on in Settings, and posts what
/// `AlertEngine` decides is worth saying. Costs nothing while every alert is off.
@MainActor
@Observable
final class AlertMonitor {
    enum Permission {
        /// macOS has not asked yet; it asks when the first alert is turned on.
        case notAsked
        case allowed
        case denied
        /// A bare binary run outside the app bundle, which Notification Centre does not know.
        case unavailable
    }

    static let checkInterval: TimeInterval = 10
    /// Processes are read every third check: a scan costs more, and a longer window smooths spikes.
    static let processCheckEvery = 3

    private(set) var permission: Permission

    @ObservationIgnored private let store: StatsStore
    @ObservationIgnored private let notifier: Notifier
    @ObservationIgnored private var engine = AlertEngine()
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var checks = 0
    @ObservationIgnored private var lastWatch: Date?
    @ObservationIgnored private var isChecking = false

    init(store: StatsStore, notifier: Notifier) {
        self.store = store
        self.notifier = notifier
        permission = notifier.isAvailable ? .notAsked : .unavailable
    }

    func start() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: Self.checkInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.check() }
        }
        timer?.tolerance = 2
        Task { await refreshPermission() }
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    func refreshPermission() async {
        permission = await notifier.permission()
    }

    /// macOS asks once; after that the choice lives in System Settings › Notifications.
    func requestPermission() async {
        await refreshPermission()
        guard permission == .notAsked else { return }
        await notifier.requestPermission()
        await refreshPermission()
    }

    func sendTest() {
        Task {
            await requestPermission()
            notifier.post(Alert(kind: .test, id: "test", title: String(localized: "Alerts are on"),
                                body: String(localized: "This is how MYU STATS lets you know."), section: .alerts))
        }
    }

    func openNotificationSettings() {
        let address = "x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=\(Bundle.main.bundleIdentifier ?? "")"
        if let url = URL(string: address) { NSWorkspace.shared.open(url) }
    }

    private func check() {
        let settings = AlertSettings(.standard)
        guard settings.isAnyOn else {
            // Everything starts afresh when an alert is turned on again.
            engine = AlertEngine()
            lastWatch = nil
            return
        }
        guard !isChecking else { return }
        isChecking = true
        let readsProcesses = settings.processMinutes != nil && checks % Self.processCheckEvery == 0
        checks += 1
        if settings.processMinutes == nil { lastWatch = nil }

        Task {
            defer { isChecking = false }
            var snapshot = AlertSnapshot(
                claude: store.claude.usage,
                disk: store.disk,
                temperature: store.thermals?.headline,
                memoryPressure: store.memory.total > 0 ? store.memory.pressure : nil
            )
            if readsProcesses {
                let processes = await store.watchProcesses()
                let now = Date()
                // After a pause the averages span the pause, so that reading only starts a new window.
                if let lastWatch, now.timeIntervalSince(lastWatch) < 2 * Self.checkInterval * Double(Self.processCheckEvery) {
                    snapshot.processes = processes
                    snapshot.processWindow = now.timeIntervalSince(lastWatch)
                }
                lastWatch = now
            }
            for alert in engine.evaluate(snapshot, settings: settings, now: Date()) {
                Log.app.notice("Alert \(alert.id, privacy: .public)")
                notifier.post(alert)
            }
        }
    }
}

/// Notification Centre, for the alerts. A click on a notification opens its Settings page.
@MainActor
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    /// Called with the Settings page a clicked notification points at.
    var onOpen: ((SettingsSection) -> Void)?

    private let center: UNUserNotificationCenter?

    override init() {
        // Notification Centre knows apps by their bundle; `swift run` and the tests have none.
        center = Bundle.main.bundleURL.pathExtension == "app" ? UNUserNotificationCenter.current() : nil
        super.init()
        // Set before launch finishes, so a click that starts the app is not missed.
        center?.delegate = self
    }

    var isAvailable: Bool { center != nil }

    func permission() async -> AlertMonitor.Permission {
        guard let center else { return .unavailable }
        switch await center.notificationSettings().authorizationStatus {
        case .authorized, .provisional: return .allowed
        case .denied: return .denied
        default: return .notAsked
        }
    }

    func requestPermission() async {
        do {
            _ = try await center?.requestAuthorization(options: [.alert, .sound])
        } catch {
            Log.app.error("Notification permission request failed: \(error.localizedDescription, privacy: .public)")
        }
    }

    func post(_ alert: Alert) {
        guard let center else { return }
        let content = UNMutableNotificationContent()
        content.title = alert.title
        content.body = alert.body
        content.sound = .default
        content.threadIdentifier = alert.kind.rawValue
        content.userInfo = ["section": alert.section.id]
        let request = UNNotificationRequest(identifier: alert.id, content: content, trigger: nil)
        center.add(request) { error in
            if let error { Log.app.error("Notification not posted: \(error.localizedDescription, privacy: .public)") }
        }
    }

    // While Settings is open the app is in front, and macOS would otherwise keep the banner to itself.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter,
                                            willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .list, .sound]
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter,
                                            didReceive response: UNNotificationResponse) async {
        let id = response.notification.request.content.userInfo["section"] as? String
        await MainActor.run {
            self.onOpen?(id.flatMap(SettingsSection.init(id:)) ?? .alerts)
        }
    }
}
