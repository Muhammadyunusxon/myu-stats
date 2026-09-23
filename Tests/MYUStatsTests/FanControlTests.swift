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
    private let helper = URL(fileURLWithPath: "/Applications/Notch \"Stats\".app/Contents/MacOS/notch's-fan")

    @Test("paths and arguments are quoted for the shell and AppleScript")
    func quoting() {
        let script = FanController.installScript(helper: helper, helperHash: "abc123", arguments: ["manual", "all", "3000"])
        #expect(script.hasPrefix("do shell script \""))
        #expect(script.contains("with administrator privileges"))
        // The apostrophe is shell-escaped ('\'') and that backslash is then AppleScript-escaped.
        #expect(script.contains(#"'\\''"#))
        #expect(script.contains(#"\"Stats\""#)) // the double quotes are AppleScript-escaped
        #expect(script.hasSuffix(#"'manual' 'all' '3000'" with administrator privileges with prompt "MYU STATS needs your permission once to install its fan helper. After that, fan changes need no password.""#))
    }

    @Test("only a copy checked against the app's signature is installed, root-owned and setuid")
    func installsVerifiedCopy() throws {
        let script = FanController.installScript(helper: helper, helperHash: "abc123", arguments: ["auto", "all"])
        let installed = FanController.installedHelper.path
        #expect(script.contains("/usr/bin/mktemp -d"))
        // The requirement pins the hash; its quotes are AppleScript-escaped inside single shell quotes.
        let check = try #require(script.range(of: #"/usr/bin/codesign --verify -R '=cdhash H\"abc123\"'"#))
        let install = try #require(script.range(of: #"/usr/bin/install -o root -g wheel -m 4755 \"$d/myustats-fan\" '\#(installed)'"#))
        let run = try #require(script.range(of: "'\(installed)' 'auto' 'all'"))
        #expect(check.lowerBound < install.lowerBound)
        #expect(install.lowerBound < run.lowerBound)
        #expect(script.contains(#"/bin/rm -rf \"$d\""#))
    }

    @Test("removing hands the fans back before deleting the helper")
    func removal() throws {
        let script = FanController.removeScript()
        let installed = FanController.installedHelper.path
        let restore = try #require(script.range(of: "'\(installed)' 'auto' 'all'"))
        let delete = try #require(script.range(of: "/bin/rm -f '\(installed)'"))
        #expect(restore.lowerBound < delete.lowerBound)
    }

    @Test("an installed helper runs without a prompt only when root owns it and nobody else can write it",
          arguments: [
              (0, 0o4755, true),
              (0, 0o4555, true),
              (0, 0o0755, false), // not setuid
              (501, 0o4755, false), // owned by a user
              (0, 0o4775, false), // group-writable
              (0, 0o4757, false), // world-writable
          ])
    func trustedInstall(owner: Int, permissions: Int, trusted: Bool) {
        #expect(FanController.isTrustedInstall(owner: owner, permissions: permissions) == trusted)
    }

    @Test("a bundle whose signature cannot be checked yields no hash, so nothing runs")
    func refusesUnverifiableBundle() {
        let missing = URL(fileURLWithPath: "/nonexistent/\(UUID().uuidString).app")
        #expect(FanController.verifiedHelperHash(helper: helper, bundle: missing) == nil)
    }

    @Test("osascript noise is trimmed from helper errors", arguments: [
        ("0:59: execution error: There is no fan 3. (1)", "There is no fan 3."),
        ("execution error: The SMC refused to change F0Md. (-2700)\n", "The SMC refused to change F0Md."),
        ("", "The fan helper failed."),
    ])
    func cleansErrors(raw: String, expected: String) {
        #expect(FanController.cleaned(raw) == expected)
    }
}

/// In-memory SMC: keys that exist have a value; writes to `refused` keys fail.
private final class FakeSMC: SMCAccess {
    var values: [String: Double]
    var refused: Set<String>

    init(fans: Int, modes: [Double]? = nil, refused: Set<String> = []) {
        values = ["FNum": Double(fans), "Ftst": 0]
        for index in 0..<fans {
            values["F\(index)Mn"] = 1_000
            values["F\(index)Mx"] = 5_000
            values["F\(index)Md"] = modes?[index] ?? 3
            values["F\(index)Tg"] = 0
        }
        self.refused = refused
    }

    func hasKey(_ key: String) -> Bool { values[key] != nil }
    func number(_ key: String) -> Double? { values[key] }
    func write(_ key: String, _ value: Double) -> Bool {
        guard values[key] != nil, !refused.contains(key) else { return false }
        values[key] = value
        return true
    }
}

@Suite("Fan control through the SMC")
struct FanControlLogicTests {
    @Test func manualClampsToEachFansRange() throws {
        let smc = FakeSMC(fans: 2)
        try FanControl(smc: smc, retryDelay: 0).apply(.manual(fan: nil, rpm: 9_000))
        #expect(smc.values["Ftst"] == 1)
        #expect(smc.values["F0Md"] == 1 && smc.values["F1Md"] == 1)
        #expect(smc.values["F0Tg"] == 5_000 && smc.values["F1Tg"] == 5_000)
    }

    @Test("a fan that refuses rolls back the fans already taken over")
    func rollsBack() {
        let smc = FakeSMC(fans: 2, refused: ["F1Md"])
        #expect(throws: FanControlError.self) {
            try FanControl(smc: smc, retryDelay: 0).apply(.max(fan: nil))
        }
        #expect(smc.values["F0Md"] == 0)
        #expect(smc.values["F1Md"] == 3)
        #expect(smc.values["Ftst"] == 0)
    }

    @Test("a fan that was already manual stays manual after a rollback")
    func keepsEarlierManualFans() {
        let smc = FakeSMC(fans: 2, modes: [1, 3], refused: ["F1Md"])
        smc.values["Ftst"] = 1
        #expect(throws: FanControlError.self) {
            try FanControl(smc: smc, retryDelay: 0).apply(.manual(fan: nil, rpm: 2_000))
        }
        #expect(smc.values["F0Md"] == 1)
        #expect(smc.values["Ftst"] == 1)
    }

    @Test("a refused target speed also rolls back")
    func targetRefused() {
        let smc = FakeSMC(fans: 1, refused: ["F0Tg"])
        #expect(throws: FanControlError.self) {
            try FanControl(smc: smc, retryDelay: 0).apply(.manual(fan: 0, rpm: 2_000))
        }
        #expect(smc.values["F0Md"] == 0)
        #expect(smc.values["Ftst"] == 0)
    }

    @Test("auto tries every fan and keeps Ftst raised while one is still manual")
    func autoTriesEveryFan() {
        let smc = FakeSMC(fans: 2, modes: [1, 1], refused: ["F0Md"])
        smc.values["Ftst"] = 1
        #expect(throws: FanControlError.self) {
            try FanControl(smc: smc, retryDelay: 0).apply(.auto(fan: nil))
        }
        #expect(smc.values["F1Md"] == 0)
        #expect(smc.values["Ftst"] == 1)
    }

    @Test func autoReleasesForceTest() throws {
        let smc = FakeSMC(fans: 2, modes: [1, 1])
        smc.values["Ftst"] = 1
        try FanControl(smc: smc, retryDelay: 0).apply(.auto(fan: nil))
        #expect(smc.values["F0Md"] == 0 && smc.values["F1Md"] == 0)
        #expect(smc.values["Ftst"] == 0)
    }

    @Test func unknownFanIsRejected() {
        #expect(throws: FanControlError.self) {
            try FanControl(smc: FakeSMC(fans: 1), retryDelay: 0).apply(.auto(fan: 3))
        }
    }
}
