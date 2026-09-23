import AppKit

/// Which display the pill sits on. Displays are remembered by UUID, which survives reboots and
/// reconnects (display IDs do not). A missing display falls back to the main one until it returns.
enum DisplayChoice {
    /// Index of the chosen display in `available`, where the first entry is the main display.
    static func index(of saved: String, in available: [String?]) -> Int? {
        guard !available.isEmpty else { return nil }
        guard !saved.isEmpty else { return 0 }
        return available.firstIndex(of: saved) ?? 0
    }

    /// The display chosen in Settings, or the main display.
    @MainActor
    static func screen(defaults: UserDefaults = .standard) -> NSScreen? {
        let screens = NSScreen.screens
        let saved = defaults.string(forKey: SettingsKey.display) ?? ""
        return index(of: saved, in: screens.map(\.displayUUID)).map { screens[$0] }
    }
}

extension NSScreen {
    /// Stable identifier for this display, as saved under `SettingsKey.display`.
    var displayUUID: String? {
        guard let number = deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber,
              let uuid = CGDisplayCreateUUIDFromDisplayID(number.uint32Value)?.takeRetainedValue()
        else { return nil }
        return CFUUIDCreateString(nil, uuid) as String
    }
}
