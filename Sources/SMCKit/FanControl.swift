import Foundation

/// What the fan helper is asked to do. The helper runs as root, so this is the whole of its
/// interface: three verbs, a fan index or "all", and a speed that is clamped to the fan's range.
public enum FanCommand: Equatable, Sendable {
    /// Hand the fan back to macOS.
    case auto(fan: Int?)
    /// Hold a fixed speed, clamped to the fan's own minimum and maximum.
    case manual(fan: Int?, rpm: Double)
    /// Run at the fan's maximum speed.
    case max(fan: Int?)

    public var arguments: [String] {
        switch self {
        case .auto(let fan): ["auto", Self.fanArgument(fan)]
        case .manual(let fan, let rpm): ["manual", Self.fanArgument(fan), String(Int(rpm.rounded()))]
        case .max(let fan): ["max", Self.fanArgument(fan)]
        }
    }

    /// Parses helper arguments (without the program name). Anything unexpected is rejected.
    public static func parse(_ arguments: [String]) -> FanCommand? {
        guard let verb = arguments.first, arguments.count >= 2, let fan = parseFan(arguments[1]) else { return nil }
        switch (verb, arguments.count) {
        case ("auto", 2): return .auto(fan: fan)
        case ("max", 2): return .max(fan: fan)
        case ("manual", 3):
            guard let rpm = Int(arguments[2]), (0...20_000).contains(rpm) else { return nil }
            return .manual(fan: fan, rpm: Double(rpm))
        default: return nil
        }
    }

    private static func fanArgument(_ fan: Int?) -> String { fan.map(String.init) ?? "all" }

    /// "all" → nil (every fan); a small non-negative integer → that fan; anything else → rejected.
    private static func parseFan(_ text: String) -> Int?? {
        if text == "all" { return .some(nil) }
        guard let index = Int(text), (0..<16).contains(index) else { return nil }
        return .some(index)
    }
}

public enum FanControlError: Error, CustomStringConvertible {
    case noFans
    case unknownFan(Int)
    case writeFailed(String)

    public var description: String {
        switch self {
        case .noFans: "This Mac reports no fans."
        case .unknownFan(let index): "There is no fan \(index + 1)."
        case .writeFailed(let key): "The SMC refused to change \(key)."
        }
    }
}

/// Applies a `FanCommand` through the SMC. Must run as root.
///
/// Keys: `FNum` fan count, `F<n>Mn`/`F<n>Mx` range, `F<n>Md` mode (1 manual; 0 or 3 automatic), `F<n>Tg` target.
/// Apple silicon also has `Ftst`, a force-test flag that must be raised before the mode key accepts
/// manual control; it is lowered again once every fan is back on auto.
///
/// A change is all or nothing: if one fan refuses, every fan this call took over is handed back to
/// macOS, so a half-applied command never leaves fans pinned.
public struct FanControl {
    private let smc: SMCAccess
    /// Pause between attempts to take a fan over; zero in tests.
    private let retryDelay: useconds_t

    public init?() {
        guard let smc = SMC() else { return nil }
        self.init(smc: smc)
    }

    public init(smc: SMCAccess, retryDelay: useconds_t = 100_000) {
        self.smc = smc
        self.retryDelay = retryDelay
    }

    public var fanCount: Int { Int(smc.number("FNum") ?? 0) }

    public func apply(_ command: FanCommand) throws {
        let count = fanCount
        guard count > 0 else { throw FanControlError.noFans }

        switch command {
        case .auto(let fan):
            // Try every fan even if one refuses, so as many as possible go back to macOS.
            var refused: [String] = []
            for index in try fans(fan, count: count) where !smc.write("F\(index)Md", 0) {
                refused.append("F\(index)Md")
            }
            releaseForceTestIfAllAuto(count: count)
            if let key = refused.first {
                SMCLog.fans.error("Could not return \(refused.count) fan(s) to automatic control")
                throw FanControlError.writeFailed(key)
            }

        case .manual(let fan, let rpm):
            try setManual(try fans(fan, count: count), count: count) { minimum, maximum in min(max(rpm, minimum), maximum) }

        case .max(let fan):
            try setManual(try fans(fan, count: count), count: count) { _, maximum in maximum }
        }
    }

    private func fans(_ fan: Int?, count: Int) throws -> [Int] {
        guard let fan else { return Array(0..<count) }
        guard fan < count else { throw FanControlError.unknownFan(fan) }
        return [fan]
    }

    private func isManual(_ index: Int) -> Bool { smc.number("F\(index)Md") == 1 }

    private func setManual(_ fans: [Int], count: Int, speed: (Double, Double) -> Double) throws {
        if smc.hasKey("Ftst") { smc.write("Ftst", 1) }
        // Fans that were already manual stay manual on failure; only the ones taken over here go back.
        var takenOver: [Int] = []
        do {
            for index in fans {
                let minimum = smc.number("F\(index)Mn") ?? 0
                let maximum = smc.number("F\(index)Mx") ?? minimum
                let wasManual = isManual(index)
                // thermalmonitord can take a moment to let go after Ftst is raised, so retry the mode switch.
                var switched = false
                for _ in 0..<20 {
                    if smc.write("F\(index)Md", 1), isManual(index) {
                        switched = true
                        break
                    }
                    if retryDelay > 0 { usleep(retryDelay) }
                }
                guard switched else { throw FanControlError.writeFailed("F\(index)Md") }
                if !wasManual { takenOver.append(index) }
                try write("F\(index)Tg", speed(minimum, maximum))
            }
        } catch {
            SMCLog.fans.error("Manual fan change failed (\(String(describing: error), privacy: .public)); restoring \(takenOver.count) fan(s)")
            for index in takenOver { smc.write("F\(index)Md", 0) }
            releaseForceTestIfAllAuto(count: count)
            throw error
        }
    }

    /// Lowers `Ftst` once no fan is manual, so thermalmonitord takes the fans back.
    /// Only 1 means manual: automatic reads as 0 on some Macs and as 3 on recent Apple silicon.
    private func releaseForceTestIfAllAuto(count: Int) {
        guard smc.hasKey("Ftst"), (0..<count).allSatisfy({ !isManual($0) }) else { return }
        smc.write("Ftst", 0)
    }

    private func write(_ key: String, _ value: Double) throws {
        guard smc.write(key, value) else { throw FanControlError.writeFailed(key) }
    }
}
