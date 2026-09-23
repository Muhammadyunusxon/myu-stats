import Foundation
import Observation
import os
import Security
import SMCKit

/// Changes fan speed through the `myustats-fan` helper, running as root.
///
/// The first change installs the helper once, through macOS's administrator prompt (password or
/// Touch ID), as a root-owned setuid copy in /Library/PrivilegedHelperTools. Later changes run that
/// copy directly, with no prompt and no long-lived privileged process. The helper accepts only the
/// three fan verbs and clamps speeds to each fan's range, so whoever runs it can do no more than this
/// app can.
///
/// Trust: the helper inside the bundle is writable by the user, so it is never run as root in place.
/// Its code hash comes from the app's own verified signature; the root shell copies it into a
/// root-only folder, checks that hash with `codesign`, and installs the checked copy. Before each
/// prompt-free run the installed copy must be root-owned, writable by nobody else, validly signed and
/// carry that same hash, so a new build of the app reinstalls it once and a swapped file is never run.
@MainActor
@Observable
final class FanController {
    enum Status: Equatable {
        case idle
        case applying
        case applied(String)
        case cancelled
        case failed(String)
    }

    private(set) var status: Status = .idle
    /// A root-owned setuid helper is installed, so changes need no password.
    private(set) var isHelperInstalled = FanController.installLooksPresent()

    var isApplying: Bool { status == .applying }

    /// Where the helper is installed on first use.
    nonisolated static let installedHelper = URL(fileURLWithPath: "/Library/PrivilegedHelperTools/com.muhammadyunusxon.myustats.fan")

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
            status = .failed(String(localized: "The fan helper is missing from this build."))
            return false
        }
        status = .applying
        let bundle = Bundle.main.bundleURL
        let result = await Task.detached { () -> RunResult in
            guard let hash = Self.verifiedHelperHash(helper: helper, bundle: bundle) else {
                return .failure(String(localized: "The fan helper failed its signature check. Reinstall MYU STATS."))
            }
            if Self.installedHelperMatches(hash) {
                return Self.runInstalled(command.arguments)
            }
            // Not installed yet, or installed by another build: install (one prompt) and apply in one go.
            Log.fans.notice("Installing the fan helper")
            return Self.runOSAScript(Self.installScript(helper: helper, helperHash: hash, arguments: command.arguments))
        }.value
        isHelperInstalled = Self.installLooksPresent()

        switch result {
        case .success:
            status = .applied(Self.summary(of: command))
            return true
        case .cancelled:
            status = .cancelled
            return false
        case .failure(let message):
            Log.fans.error("Fan change \(command.arguments.joined(separator: " "), privacy: .public) failed: \(message, privacy: .public)")
            status = .failed(message)
            return false
        }
    }

    /// Hands the fans back to macOS and deletes the installed helper (one administrator prompt).
    func removeHelper() async {
        status = .applying
        let result = await Task.detached { Self.runOSAScript(Self.removeScript()) }.value
        isHelperInstalled = Self.installLooksPresent()
        switch result {
        case .success: status = .applied(String(localized: "Fan helper removed. Fans are automatic."))
        case .cancelled: status = .cancelled
        case .failure(let message):
            Log.fans.error("Removing the fan helper failed: \(message, privacy: .public)")
            status = .failed(message)
        }
    }

    // MARK: - The installed helper

    /// Owner and mode an installed helper must have before it is run without a prompt: owned by root,
    /// setuid, and writable by no one but root.
    nonisolated static func isTrustedInstall(owner: Int, permissions: Int) -> Bool {
        owner == 0 && permissions & 0o4000 != 0 && permissions & 0o022 == 0
    }

    /// Cheap check for the UI; `installedHelperMatches` is the one that gates running it.
    nonisolated static func installLooksPresent() -> Bool {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: installedHelper.path),
              let owner = attributes[.ownerAccountID] as? Int,
              let permissions = attributes[.posixPermissions] as? Int
        else { return false }
        return isTrustedInstall(owner: owner, permissions: permissions)
    }

    /// The installed helper is safe to run: root-owned, not writable by others, validly signed, and
    /// the very code this app ships (same code-directory hash).
    nonisolated static func installedHelperMatches(_ expectedHash: String) -> Bool {
        guard installLooksPresent() else { return false }
        var code: SecStaticCode?
        guard SecStaticCodeCreateWithPath(installedHelper as CFURL, [], &code) == errSecSuccess, let code,
              SecStaticCodeCheckValidity(code, SecCSFlags(rawValue: kSecCSStrictValidate), nil) == errSecSuccess,
              let hash = uniqueHash(of: code, flags: [])
        else {
            Log.fans.notice("Installed fan helper failed its signature check; it will be reinstalled")
            return false
        }
        let matches = hash.map { String(format: "%02x", $0) }.joined() == expectedHash
        if !matches { Log.fans.notice("Installed fan helper is from another build; it will be reinstalled") }
        return matches
    }

    private nonisolated static func runInstalled(_ arguments: [String]) -> RunResult {
        let process = Process()
        process.executableURL = installedHelper
        process.arguments = arguments
        process.environment = [:]
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
        let message = String(data: errors.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return .failure(message.isEmpty ? String(localized: "The fan helper failed.") : message)
    }

    // MARK: - Verifying the helper

    /// The helper's code-directory hash, trusted only when the app on disk is the app that is running
    /// and its signature, which seals the nested helper, is intact. Nil means: do not run it.
    nonisolated static func verifiedHelperHash(helper: URL, bundle: URL) -> String? {
        var running: SecCode?
        guard SecCodeCopySelf([], &running) == errSecSuccess, let running,
              let runningHash = uniqueHash(of: running, flags: SecCSFlags(rawValue: kSecCSDynamicInformation))
        else {
            Log.fans.error("Could not read the running app's code signature")
            return nil
        }

        var app: SecStaticCode?
        guard SecStaticCodeCreateWithPath(bundle as CFURL, [], &app) == errSecSuccess, let app else { return nil }
        let strict = SecCSFlags(rawValue: kSecCSCheckNestedCode | kSecCSStrictValidate | kSecCSCheckAllArchitectures)
        let validity = SecStaticCodeCheckValidity(app, strict, nil)
        guard validity == errSecSuccess else {
            Log.fans.error("App bundle signature is not valid (\(validity)); refusing to run the fan helper")
            return nil
        }
        // The bundle on disk must be the one this process was launched from, or its seal proves nothing.
        guard uniqueHash(of: app, flags: []) == runningHash else {
            Log.fans.error("App bundle on disk differs from the running app; refusing to run the fan helper")
            return nil
        }

        var code: SecStaticCode?
        guard SecStaticCodeCreateWithPath(helper as CFURL, [], &code) == errSecSuccess, let code,
              let hash = uniqueHash(of: code, flags: [])
        else { return nil }
        return hash.map { String(format: "%02x", $0) }.joined()
    }

    private nonisolated static func uniqueHash(of code: SecStaticCode, flags: SecCSFlags) -> Data? {
        var info: CFDictionary?
        guard SecCodeCopySigningInformation(code, flags, &info) == errSecSuccess,
              let info = info as? [String: Any]
        else { return nil }
        return info[kSecCodeInfoUnique as String] as? Data
    }

    private nonisolated static func uniqueHash(of code: SecCode, flags: SecCSFlags) -> Data? {
        // A running SecCode is also a SecStaticCode as far as the signing-information call is concerned.
        uniqueHash(of: unsafeBitCast(code, to: SecStaticCode.self), flags: flags)
    }

    // MARK: - Running the helper

    private enum RunResult: Sendable {
        case success
        case cancelled
        case failure(String)
    }

    /// `do shell script … with administrator privileges`: the standard, user-visible way to run
    /// commands as root. As root it copies the bundled helper into a fresh root-only folder, checks
    /// the copy against `helperHash`, installs it as a root-owned setuid binary, cleans up, and runs
    /// the installed copy once with `arguments`. Every argument is single-quoted for the shell, then
    /// the whole command is escaped for AppleScript.
    nonisolated static func installScript(helper: URL, helperHash: String, arguments: [String]) -> String {
        let requirement = "=cdhash H\"\(helperHash)\""
        let installed = shellQuoted(installedHelper.path)
        let shellCommand = [
            "d=$(/usr/bin/mktemp -d /tmp/myustats-fan.XXXXXX) || exit 1",
            "/bin/cp \(shellQuoted(helper.path)) \"$d/myustats-fan\" || { /bin/rm -rf \"$d\"; exit 1; }",
            "if ! /usr/bin/codesign --verify -R \(shellQuoted(requirement)) \"$d/myustats-fan\" 2>/dev/null; then "
                + "/bin/rm -rf \"$d\"; echo \(shellQuoted(signatureMismatch)) >&2; exit 1; fi",
            "/bin/mkdir -p -m 755 \(shellQuoted(installedHelper.deletingLastPathComponent().path)) || { /bin/rm -rf \"$d\"; exit 1; }",
            "/usr/bin/install -o root -g wheel -m 4755 \"$d/myustats-fan\" \(installed) || { /bin/rm -rf \"$d\"; exit 1; }",
            "/bin/rm -rf \"$d\"",
            ([installed] + arguments.map(shellQuoted)).joined(separator: " "),
        ].joined(separator: "; ")
        let prompt = String(localized: "MYU STATS needs your permission once to install its fan helper. After that, fan changes need no password.")
        return adminScript(shellCommand, prompt: prompt)
    }

    /// Returns the fans to macOS (best effort) and deletes the installed helper.
    nonisolated static func removeScript() -> String {
        let installed = shellQuoted(installedHelper.path)
        let shellCommand = "if [ -x \(installed) ]; then \(installed) 'auto' 'all' || true; fi; /bin/rm -f \(installed)"
        return adminScript(shellCommand, prompt: String(localized: "MYU STATS needs your permission to remove its fan helper."))
    }

    private nonisolated static func adminScript(_ shellCommand: String, prompt: String) -> String {
        "do shell script \(appleScriptString(shellCommand)) with administrator privileges with prompt \(appleScriptString(prompt))"
    }

    nonisolated static var signatureMismatch: String {
        String(localized: "The fan helper does not match this app's signature. Reinstall MYU STATS.")
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
    nonisolated static func cleaned(_ message: String) -> String {
        var text = message.trimmingCharacters(in: .whitespacesAndNewlines)
        if let range = text.range(of: "execution error: ") { text = String(text[range.upperBound...]) }
        if let range = text.range(of: #" \(-?\d+\)$"#, options: .regularExpression) { text.removeSubrange(range) }
        return text.isEmpty ? String(localized: "The fan helper failed.") : text
    }

    private static func summary(of command: FanCommand) -> String {
        switch command {
        case .auto: String(localized: "Fans are back under automatic control.")
        case .manual(_, let rpm): String(localized: "Fans held at \(Format.rpm(rpm)).")
        case .max: String(localized: "Fans running at full speed.")
        }
    }
}
