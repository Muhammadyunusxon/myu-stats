import AppKit
import SwiftUI

/// Single reusable Settings window: transparent, so SettingsView draws the rounded dark panel itself.
@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    private let store: StatsStore
    private let navigation = SettingsNavigation()
    private var window: NSWindow?

    private static let firstLightCentreX: CGFloat = 26
    private static let lightSpacing: CGFloat = 22.5

    init(store: StatsStore) {
        self.store = store
    }

    /// Opens Settings, optionally on a given page (a card's "Details" button passes its metric).
    func show(section: SettingsSection? = nil) {
        if let section { navigation.selection = section }
        if let window {
            surface(window)
            return
        }

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: SettingsMetrics.width, height: SettingsMetrics.height),
            styleMask: [.titled, .closable, .miniaturizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.title = String(localized: "MYU STATS Settings")
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.isOpaque = false
        window.backgroundColor = .clear
        window.appearance = NSAppearance(named: .darkAqua)
        window.hasShadow = true
        window.isMovableByWindowBackground = true
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.contentView = NSHostingView(rootView: SettingsView(store: store, navigation: navigation, quit: { NSApp.terminate(nil) }))
        window.center()
        self.window = window
        layoutTrafficLights(in: window)
        surface(window)
    }

    /// Becomes a regular app while Settings is open so it can come forward and show in ⌘-Tab.
    private func surface(_ window: NSWindow) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()
    }

    func windowWillClose(_ notification: Notification) {
        store.setProcessSampling(false, for: "details")
        NSApp.setActivationPolicy(.accessory)
    }

    func windowDidBecomeKey(_ notification: Notification) {
        guard let window = notification.object as? NSWindow else { return }
        layoutTrafficLights(in: window)
    }

    func windowDidResize(_ notification: Notification) {
        guard let window = notification.object as? NSWindow else { return }
        layoutTrafficLights(in: window)
    }

    /// Centres the traffic lights in the sidebar's header band instead of the default title bar spot.
    private func layoutTrafficLights(in window: NSWindow) {
        let buttons = [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton]
            .compactMap { window.standardWindowButton($0) }
        guard let container = buttons.first?.superview else { return }
        for (index, button) in buttons.enumerated() {
            var frame = button.frame
            frame.origin.x = Self.firstLightCentreX + CGFloat(index) * Self.lightSpacing - frame.width / 2
            frame.origin.y = container.bounds.height - SettingsMetrics.headerHeight / 2 - frame.height / 2
            button.frame = frame
        }
    }
}
