import Foundation
import IOKit
import os

/// What `FanControl` needs from the SMC, so its logic can run against a fake in tests.
public protocol SMCAccess: AnyObject {
    func hasKey(_ key: String) -> Bool
    func number(_ key: String) -> Double?
    @discardableResult
    func write(_ key: String, _ value: Double) -> Bool
}

enum SMCLog {
    static let smc = Logger(subsystem: "com.muhammadyunusxon.myustats", category: "smc")
    static let fans = Logger(subsystem: "com.muhammadyunusxon.myustats", category: "fans")
}

/// Minimal client for the System Management Controller (fans, temperature sensors).
/// Undocumented interface: layout and selectors match what every open-source monitor uses,
/// but Apple may change key names between chips and releases.
/// Reading works for any user; writing requires root.
public final class SMC: SMCAccess {
    private var connection: io_connect_t = 0
    private var infoCache: [UInt32: (size: UInt32, type: UInt32)] = [:]

    public init?() {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"))
        guard service != 0 else {
            SMCLog.smc.error("AppleSMC service not found")
            return nil
        }
        defer { IOObjectRelease(service) }
        let status = IOServiceOpen(service, mach_task_self_, 0, &connection)
        guard status == kIOReturnSuccess else {
            SMCLog.smc.error("IOServiceOpen(AppleSMC) failed: \(UInt32(bitPattern: status), format: .hex)")
            return nil
        }
    }

    deinit {
        if connection != 0 { IOServiceClose(connection) }
    }

    public func hasKey(_ key: String) -> Bool {
        info(for: Self.fourCC(key)) != nil
    }

    public func number(_ key: String) -> Double? {
        let code = Self.fourCC(key)
        guard let info = info(for: code) else { return nil }
        var input = SMCParamStruct()
        input.key = code
        input.keyInfo.dataSize = info.size
        input.command = Command.readBytes
        guard let output = call(&input) else { return nil }
        let bytes = withUnsafeBytes(of: output.bytes) { Array($0.prefix(Int(info.size))) }
        return Self.decode(bytes, type: info.type)
    }

    /// Writes `value` encoded in the key's own type. Returns false if the key is unknown,
    /// its type is unsupported, or the SMC refuses (for example when not running as root).
    @discardableResult
    public func write(_ key: String, _ value: Double) -> Bool {
        let code = Self.fourCC(key)
        guard let info = info(for: code) else {
            SMCLog.smc.error("SMC write to unknown key \(key, privacy: .public)")
            return false
        }
        guard let bytes = Self.encode(value, type: info.type, size: Int(info.size)) else {
            SMCLog.smc.error("SMC key \(key, privacy: .public) has unsupported type \(Self.string(info.type), privacy: .public)")
            return false
        }
        var input = SMCParamStruct()
        input.key = code
        input.keyInfo.dataSize = info.size
        input.command = Command.writeBytes
        withUnsafeMutableBytes(of: &input.bytes) { buffer in
            for (index, byte) in bytes.prefix(buffer.count).enumerated() { buffer[index] = byte }
        }
        guard call(&input) != nil else {
            SMCLog.smc.error("SMC refused write of \(value) to \(key, privacy: .public)")
            return false
        }
        return true
    }

    /// Every key name the SMC knows. There are a couple of thousand, so call it once.
    public func allKeys() -> [String] {
        guard let raw = number("#KEY") else { return [] }
        return (0..<UInt32(raw)).compactMap { index in
            var input = SMCParamStruct()
            input.command = Command.keyAtIndex
            input.data32 = index
            return call(&input).map { Self.string($0.key) }
        }
    }

    // MARK: - Private

    private enum Command {
        static let readBytes: UInt8 = 5
        static let writeBytes: UInt8 = 6
        static let keyAtIndex: UInt8 = 8
        static let keyInfo: UInt8 = 9
    }

    private func info(for code: UInt32) -> (size: UInt32, type: UInt32)? {
        if let cached = infoCache[code] { return cached }
        var input = SMCParamStruct()
        input.key = code
        input.command = Command.keyInfo
        guard let output = call(&input) else { return nil }
        let info = (output.keyInfo.dataSize, output.keyInfo.dataType)
        infoCache[code] = info
        return info
    }

    private func call(_ input: inout SMCParamStruct) -> SMCParamStruct? {
        var output = SMCParamStruct()
        var size = MemoryLayout<SMCParamStruct>.stride
        let status = IOConnectCallStructMethod(
            connection, 2, &input, MemoryLayout<SMCParamStruct>.stride, &output, &size
        )
        guard status == kIOReturnSuccess, output.result == 0 else {
            // Missing keys are routine while probing sensors, so this stays at debug level.
            let (key, command, result) = (Self.string(input.key), input.command, output.result)
            SMCLog.smc.debug("SMC call \(command) for \(key, privacy: .public) failed: status \(UInt32(bitPattern: status), format: .hex), result \(result)")
            return nil
        }
        return output
    }

    static func decode(_ bytes: [UInt8], type: UInt32) -> Double? {
        switch string(type) {
        case "flt ":
            guard bytes.count >= 4 else { return nil }
            return Double(bytes.withUnsafeBytes { $0.loadUnaligned(as: Float.self) })
        case "sp78":
            guard bytes.count >= 2 else { return nil }
            return Double(Int16(bitPattern: UInt16(bytes[0]) << 8 | UInt16(bytes[1]))) / 256
        case "fpe2":
            guard bytes.count >= 2 else { return nil }
            return Double(Int(bytes[0]) << 6 | Int(bytes[1]) >> 2)
        case "ui8 ":
            return bytes.first.map(Double.init)
        case "ui16":
            guard bytes.count >= 2 else { return nil }
            return Double(Int(bytes[0]) << 8 | Int(bytes[1]))
        case "ui32":
            guard bytes.count >= 4 else { return nil }
            return Double(bytes.prefix(4).reduce(UInt32(0)) { $0 << 8 | UInt32($1) })
        default:
            return nil
        }
    }

    static func encode(_ value: Double, type: UInt32, size: Int) -> [UInt8]? {
        switch string(type) {
        case "flt ":
            // The SMC stores floats in the host's (little-endian) order.
            return withUnsafeBytes(of: Float(value)) { Array($0) }
        case "fpe2":
            let raw = UInt16(max(0, min(value, 16_383)) * 4)
            return [UInt8(raw >> 8), UInt8(raw & 0xFF)]
        case "ui8 ":
            return [UInt8(max(0, min(value, 255)))]
        case "ui16":
            let raw = UInt16(max(0, min(value, 65_535)))
            return [UInt8(raw >> 8), UInt8(raw & 0xFF)]
        default:
            return nil
        }
    }

    static func fourCC(_ text: String) -> UInt32 {
        text.utf8.prefix(4).reduce(0) { $0 << 8 | UInt32($1) }
    }

    static func string(_ code: UInt32) -> String {
        String(bytes: [24, 16, 8, 0].map { UInt8((code >> $0) & 0xFF) }, encoding: .ascii) ?? ""
    }
}

/// Mirrors the kernel's SMCParamStruct (80 bytes). Swift packs nested structs by size, not stride,
/// so KeyInfo carries its C padding explicitly.
private struct SMCParamStruct {
    struct Version { var major: UInt8 = 0, minor: UInt8 = 0, build: UInt8 = 0, reserved: UInt8 = 0, release: UInt16 = 0 }
    struct PowerLimit { var version: UInt16 = 0, length: UInt16 = 0, cpu: UInt32 = 0, gpu: UInt32 = 0, memory: UInt32 = 0 }
    struct KeyInfo { var dataSize: UInt32 = 0, dataType: UInt32 = 0, attributes: UInt8 = 0, pad0: UInt8 = 0, pad1: UInt8 = 0, pad2: UInt8 = 0 }

    var key: UInt32 = 0
    var version = Version()
    var powerLimit = PowerLimit()
    var keyInfo = KeyInfo()
    var result: UInt8 = 0
    var status: UInt8 = 0
    var command: UInt8 = 0
    var data32: UInt32 = 0
    var bytes: (UInt64, UInt64, UInt64, UInt64) = (0, 0, 0, 0)
}
