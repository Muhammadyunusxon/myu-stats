import Foundation
import SMCKit

/// Changes fan speed by running the bundled `myustats-fan` helper as root.
///
/// Each change goes through macOS's own administrator prompt (password or Touch ID), so there is
/// no long-lived privileged process. The helper validates everything it is given and clamps speeds
/// to each fan's range, so the app cannot ask for anything outside what the hardware allows.
@MainActor
final class FanController: ObservableObject {
    enum Status: Equatable {
        case idle
        case applying
        case applied(String)
        case cancelled
        case failed(String)
    }

    @Published private(set) var status: Status = .idle

    var isApplying: Bool { status == .applying }

    private var helperURL: URL? {
        let url = Bundle.main.executableURL?.deletingLastPathComponent().appendingPathComponent("myustats-fan")
        return url.flatMap { FileManager.default.isExecutableFile(atPath: $0.path) ? $0 : nil }
    }

    /// Whether the helper shipped with this build (it is missing when running from `swift run`).
    var isAvailable: Bool { helperURL != nil }

    /// Runs the helper; returns true once the change is in place.
    @discardableResult
    func apply(_ command: FanCommand) async -> Bool {
        guard let helper = helperURL else {
            status = .failed("The fan helper is missing from this build.")
            return false
        }
        status = .applying
        let script = Self.appleScript(helper: helper, arguments: command.arguments)
        let result = await Task.detached { Self.runOSAScript(script) }.value

        switch result {
        case .success:
            status = .applied(Self.summary(of: command))
            return true
        case .cancelled:
            status = .cancelled
            return false
        case .failure(let message):
            status = .failed(message)
            return false
        }
    }

    // MARK: - Running the helper

    private enum RunResult: Sendable {
        case success
        case cancelled
        case failure(String)
    }

    /// `do shell script … with administrator privileges`: the standard, user-visible way to run one
    /// command as root. Every argument is single-quoted for the shell, then escaped for AppleScript.
    nonisolated static func appleScript(helper: URL, arguments: [String]) -> String {
        let shellCommand = ([helper.path] + arguments).map(shellQuoted).joined(separator: " ")
        let prompt = "MYU STATS needs your permission to change the fan speed."
        return "do shell script \(appleScriptString(shellCommand)) with administrator privileges with prompt \(appleScriptString(prompt))"
    }

    nonisolated static func shellQuoted(_ text: String) -> String {
        "'" + text.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    nonisolated static func appleScriptString(_ text: String) -> String {
        "\"" + text.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    private nonisolated static func runOSAScript(_ script: String) -> RunResult {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = ["-e", script]
        let errors = Pipe()
        process.standardError = errors
        process.standardOutput = Pipe()
        do {
            try process.run()
        } catch {
            return .failure(error.localizedDescription)
        }
        process.waitUntilExit()
        guard process.terminationStatus != 0 else { return .success }

        let message = String(data: errors.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
        // -128 is "User canceled." from the authorization prompt.
        if message.contains("(-128)") { return .cancelled }
        return .failure(cleaned(message))
    }

    /// osascript wraps the helper's message as "… execution error: <message> (1)".
    private nonisolated static func cleaned(_ message: String) -> String {
        var text = message.trimmingCharacters(in: .whitespacesAndNewlines)
        if let range = text.range(of: "execution error: ") { text = String(text[range.upperBound...]) }
        if let range = text.range(of: #" \(-?\d+\)$"#, options: .regularExpression) { text.removeSubrange(range) }
        return text.isEmpty ? "The fan helper failed." : text
    }

    private static func summary(of command: FanCommand) -> String {
        switch command {
        case .auto: "Fans are back under automatic control."
        case .manual(_, let rpm): "Fans held at \(Int(rpm)) rpm."
        case .max: "Fans running at full speed."
        }
    }
}
