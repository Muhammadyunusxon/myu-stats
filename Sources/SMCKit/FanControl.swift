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
public struct FanControl {
    private let smc: SMC

    public init?() {
        guard let smc = SMC() else { return nil }
        self.smc = smc
    }

    public var fanCount: Int { Int(smc.number("FNum") ?? 0) }

    public func apply(_ command: FanCommand) throws {
        let count = fanCount
        guard count > 0 else { throw FanControlError.noFans }

        switch command {
        case .auto(let fan):
            for index in try fans(fan, count: count) {
                try write("F\(index)Md", 0)
            }
            // Only 1 means manual. Automatic reads as 0 on some Macs and as 3 on recent Apple silicon,
            // where thermalmonitord takes the fans back once Ftst is lowered.
            let allAuto = (0..<count).allSatisfy { smc.number("F\($0)Md") != 1 }
            if allAuto, smc.hasKey("Ftst") { smc.write("Ftst", 0) }

        case .manual(let fan, let rpm):
            try setManual(try fans(fan, count: count)) { minimum, maximum in min(max(rpm, minimum), maximum) }

        case .max(let fan):
            try setManual(try fans(fan, count: count)) { _, maximum in maximum }
        }
    }

    private func fans(_ fan: Int?, count: Int) throws -> [Int] {
        guard let fan else { return Array(0..<count) }
        guard fan < count else { throw FanControlError.unknownFan(fan) }
        return [fan]
    }

    private func setManual(_ fans: [Int], speed: (Double, Double) -> Double) throws {
        if smc.hasKey("Ftst") { smc.write("Ftst", 1) }
        for index in fans {
            let minimum = smc.number("F\(index)Mn") ?? 0
            let maximum = smc.number("F\(index)Mx") ?? minimum
            // thermalmonitord can take a moment to let go after Ftst is raised, so retry the mode switch.
            var switched = false
            for _ in 0..<20 {
                if smc.write("F\(index)Md", 1), smc.number("F\(index)Md") == 1 {
                    switched = true
                    break
                }
                usleep(100_000)
            }
            guard switched else { throw FanControlError.writeFailed("F\(index)Md") }
            try write("F\(index)Tg", speed(minimum, maximum))
        }
    }

    private func write(_ key: String, _ value: Double) throws {
        guard smc.write(key, value) else { throw FanControlError.writeFailed(key) }
    }
}
