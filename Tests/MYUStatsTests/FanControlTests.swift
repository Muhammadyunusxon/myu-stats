import Foundation
import Testing
@testable import MYUStats
@testable import SMCKit

@Suite("Fan helper commands")
struct FanCommandTests {
    @Test("valid commands parse", arguments: [
        (["auto", "all"], FanCommand.auto(fan: nil)),
        (["auto", "1"], FanCommand.auto(fan: 1)),
        (["max", "all"], FanCommand.max(fan: nil)),
        (["manual", "0", "3200"], FanCommand.manual(fan: 0, rpm: 3200)),
    ])
    func parses(arguments: [String], expected: FanCommand) {
        #expect(FanCommand.parse(arguments) == expected)
    }

    @Test("anything else is rejected — the helper runs as root", arguments: [
        [String](),
        ["auto"],
        ["auto", "-1"],
        ["auto", "99"],
        ["auto", "0; rm -rf /"],
        ["manual", "0"],
        ["manual", "0", "fast"],
        ["manual", "0", "-500"],
        ["manual", "0", "999999"],
        ["max", "all", "extra"],
        ["off", "all"],
    ])
    func rejects(arguments: [String]) {
        #expect(FanCommand.parse(arguments) == nil)
    }

    @Test("arguments round-trip through the parser", arguments: [
        FanCommand.auto(fan: nil), .auto(fan: 1), .max(fan: 0), .manual(fan: nil, rpm: 2500.4),
    ])
    func roundTrip(command: FanCommand) {
        let parsed = FanCommand.parse(command.arguments)
        if case .manual(let fan, let rpm) = command {
            #expect(parsed == .manual(fan: fan, rpm: rpm.rounded()))
        } else {
            #expect(parsed == command)
        }
    }
}

@Suite("SMC value encoding")
struct SMCEncodingTests {
    private func type(_ text: String) -> UInt32 { SMC.fourCC(text) }

    @Test("values survive encode then decode", arguments: [
        ("flt ", 2317.0), ("fpe2", 6000.0), ("ui8 ", 1.0), ("ui16", 4096.0),
    ])
    func roundTrip(typeName: String, value: Double) throws {
        let bytes = try #require(SMC.encode(value, type: type(typeName), size: 4))
        #expect(SMC.decode(bytes, type: type(typeName)) == value)
    }

    @Test func unsupportedTypesAreRefused() {
        #expect(SMC.encode(1, type: type("ch8*"), size: 4) == nil)
    }

    @Test func fourCCRoundTrip() {
        #expect(SMC.string(SMC.fourCC("F0Md")) == "F0Md")
    }
}

@Suite("Admin prompt script")
struct FanScriptTests {
    @Test("paths and arguments are quoted for the shell and AppleScript")
    func quoting() {
        let helper = URL(fileURLWithPath: "/Applications/Notch \"Stats\".app/Contents/MacOS/notch's-fan")
        let script = FanController.appleScript(helper: helper, arguments: ["manual", "all", "3000"])
        #expect(script.hasPrefix("do shell script \""))
        #expect(script.contains("with administrator privileges"))
        // The apostrophe is shell-escaped ('\'') and that backslash is then AppleScript-escaped.
        #expect(script.contains(#"'\\''"#))
        #expect(script.contains(#"\"Stats\""#)) // the double quotes are AppleScript-escaped
        #expect(script.contains("'manual' 'all' '3000'"))
    }
}
