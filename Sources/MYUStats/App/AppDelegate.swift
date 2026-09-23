import AppKit
import SMCKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let store = StatsStore()
    private var edgeController: EdgeController?
    private var settingsController: SettingsWindowController?
    private var statusItem: NSStatusItem?
    private var defaultsObserver: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let defaults = UserDefaults.standard
        defaults.migrateLegacySettings()
        defaults.registerMYUStatsDefaults()
        store.start(interval: defaults.double(forKey: SettingsKey.sampleInterval))

        let settings = SettingsWindowController(store: store)
        settingsController = settings
        let controller = EdgeController(
            store: store,
            onOpenSettings: { settings.show() },
            onOpenDetails: { settings.show(section: .detail($0)) }
        )
        controller.show()
        edgeController = controller
        setUpStatusItem()

        // Restart sampling only when the interval itself changes, not on every settings write.
        defaultsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                let interval = UserDefaults.standard.double(forKey: SettingsKey.sampleInterval)
                if interval > 0, interval != self.store.interval { self.store.start(interval: interval) }
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        store.stop()
    }

    /// Fans held at a manual speed stay there after the app is gone, so offer to hand them back first.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard store.thermals?.fans.contains(where: \.isManual) == true, store.fans.isAvailable else {
            return .terminateNow
        }
        let alert = NSAlert()
        alert.messageText = String(localized: "Restore automatic fan control?")
        alert.informativeText = String(localized: "The fans are held at a manual speed. They keep that speed after MYU STATS quits, until the Mac restarts.")
        alert.addButton(withTitle: String(localized: "Restore and Quit"))
        alert.addButton(withTitle: String(localized: "Quit Anyway"))
        alert.addButton(withTitle: String(localized: "Cancel"))
        NSApp.activate()
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            Task {
                let restored = await store.fans.apply(.auto(fan: nil))
                if !restored, case .failed(let message) = store.fans.status {
                    let failure = NSAlert()
                    failure.alertStyle = .warning
                    failure.messageText = String(localized: "Could not restore automatic fan control")
                    failure.informativeText = message
                    failure.runModal()
                }
                // Stay open unless the fans really are back on auto, so they are never left pinned by accident.
                Log.fans.notice("Quit with fans restored: \(restored)")
                NSApp.reply(toApplicationShouldTerminate: restored)
            }
            return .terminateLater
        case .alertSecondButtonReturn:
            return .terminateNow
        default:
            return .terminateCancel
        }
    }

    // Launching the app again from Finder or Spotlight opens Settings, since there is no Dock icon.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openSettings()
        return false
    }

    // MARK: - Status item

    private func setUpStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(
            systemSymbolName: "gauge.with.dots.needle.50percent",
            accessibilityDescription: "MYU STATS"
        )
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
    }

    // Rebuilt on every open so check marks always reflect current settings.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        let settings = NSMenuItem(title: String(localized: "Settings…"), action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)

        let autoHide = NSMenuItem(title: String(localized: "Auto-hide"), action: #selector(toggleAutoHide), keyEquivalent: "")
        autoHide.target = self
        autoHide.state = UserDefaults.standard.bool(forKey: SettingsKey.autoHide) ? .on : .off
        menu.addItem(autoHide)

        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: String(localized: "Quit MYU STATS"), action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    @objc private func openSettings() {
        settingsController?.show(section: .metrics)
    }

    @objc private func toggleAutoHide() {
        let defaults = UserDefaults.standard
        defaults.set(!defaults.bool(forKey: SettingsKey.autoHide), forKey: SettingsKey.autoHide)
    }
}
