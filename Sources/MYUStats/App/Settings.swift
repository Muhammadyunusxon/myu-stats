import Foundation

/// One ring on the edge pill.
enum StatMetric: String, CaseIterable, Identifiable {
    case cpu, memory, network, disk, thermals, battery

    var id: String { rawValue }

    var title: String {
        switch self {
        case .cpu: String(localized: "CPU")
        case .memory: String(localized: "Memory")
        case .network: String(localized: "Network")
        case .disk: String(localized: "Disk")
        case .thermals: String(localized: "Thermals")
        case .battery: String(localized: "Battery")
        }
    }

    var symbol: String {
        switch self {
        case .cpu: "cpu"
        case .memory: "memorychip"
        case .network: "arrow.up.arrow.down"
        case .disk: "internaldrive"
        case .thermals: "fan"
        case .battery: "battery.75percent"
        }
    }
}

enum ScreenEdge: String, CaseIterable {
    case left, right, top, bottom

    var title: String {
        switch self {
        case .left: String(localized: "Left")
        case .right: String(localized: "Right")
        case .top: String(localized: "Top")
        case .bottom: String(localized: "Bottom")
        }
    }

    var isVertical: Bool { self == .left || self == .right }
}

/// How the pill and cards are painted.
enum SurfaceStyle: String, CaseIterable, Identifiable {
    case glass, darkGlass, solid

    var id: String { rawValue }

    var title: String {
        switch self {
        case .glass: String(localized: "Liquid Glass")
        case .darkGlass: String(localized: "Dark glass")
        case .solid: String(localized: "Solid black")
        }
    }

    var explanation: String {
        switch self {
        case .glass: String(localized: "Blurs what is behind it and adapts to the wallpaper.")
        case .darkGlass: String(localized: "Clearer glass over a dark wash — more see-through, still readable.")
        case .solid: String(localized: "Opaque, like the hardware notch. Best contrast.")
        }
    }
}

enum SettingsKey {
    static let edge = "edge"
    static let hiddenMetrics = "hiddenMetrics"
    static let metricOrder = "metricOrder"
    static let autoHide = "autoHide"
    static let hideDelay = "hideDelay"
    static let surfaceStyle = "surfaceStyle"
    static let glassDarkness = "glassDarkness"
    static let sampleInterval = "sampleInterval"
    static let showProcesses = "showProcesses"
    /// Nudge of the pill along its edge, in points: up on the side edges, right on top and bottom.
    static let verticalOffset = "verticalOffset"
    /// UUID of the display the pill sits on; empty means the main display (the one with the menu bar).
    static let display = "display"

    /// Every user setting, for migration and "Reset to defaults".
    static let all = [
        edge, hiddenMetrics, metricOrder, autoHide, hideDelay, surfaceStyle, glassDarkness,
        sampleInterval, showProcesses, verticalOffset, display,
    ]
}

extension UserDefaults {
    /// Bundle ID the app used before it was renamed from NotchStats to MYU STATS.
    static let legacyDomain = "com.muhammadyunusxon.notchstats"
    private static let migratedKey = "migratedFromNotchStats"

    /// Carries settings over from the NotchStats build once, so the rename does not reset them.
    /// Reads the old app's saved values only (not registered defaults), and runs before anything
    /// is saved under the new name, so nothing newer can be overwritten.
    func migrateLegacySettings(
        from legacy: [String: Any]? = UserDefaults.standard.persistentDomain(forName: legacyDomain)
    ) {
        guard !bool(forKey: Self.migratedKey) else { return }
        for key in SettingsKey.all {
            if let value = legacy?[key] { set(value, forKey: key) }
        }
        set(true, forKey: Self.migratedKey)
    }

    func registerMYUStatsDefaults() {
        register(defaults: [
            SettingsKey.edge: ScreenEdge.right.rawValue,
            SettingsKey.hiddenMetrics: [String](),
            SettingsKey.metricOrder: [String](),
            SettingsKey.autoHide: true,
            SettingsKey.hideDelay: 0.45,
            SettingsKey.surfaceStyle: SurfaceStyle.glass.rawValue,
            SettingsKey.glassDarkness: 0.28,
            SettingsKey.sampleInterval: 1.0,
            SettingsKey.showProcesses: true,
            SettingsKey.verticalOffset: 0.0,
            SettingsKey.display: "",
        ])
    }

    var screenEdge: ScreenEdge {
        get { ScreenEdge(rawValue: string(forKey: SettingsKey.edge) ?? "") ?? .right }
        set { set(newValue.rawValue, forKey: SettingsKey.edge) }
    }

    var hiddenMetrics: Set<StatMetric> {
        get { Set((stringArray(forKey: SettingsKey.hiddenMetrics) ?? []).compactMap(StatMetric.init(rawValue:))) }
        set { set(newValue.map(\.rawValue).sorted(), forKey: SettingsKey.hiddenMetrics) }
    }

    /// Clears every user setting, so the registered defaults apply again.
    func resetMYUStatsSettings() {
        SettingsKey.all.forEach(removeObject(forKey:))
    }

    /// User's ring order; metrics added in later versions are appended in their default place.
    var metricOrder: [StatMetric] {
        get {
            let saved = (stringArray(forKey: SettingsKey.metricOrder) ?? []).compactMap(StatMetric.init(rawValue:))
            return saved + StatMetric.allCases.filter { !saved.contains($0) }
        }
        set { set(newValue.map(\.rawValue), forKey: SettingsKey.metricOrder) }
    }
}
