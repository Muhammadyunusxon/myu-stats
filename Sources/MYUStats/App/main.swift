import AppKit

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    // NSApplication holds its delegate weakly; run() never returns, so `delegate` stays alive.
    withExtendedLifetime(delegate) {
        app.run()
    }
}
