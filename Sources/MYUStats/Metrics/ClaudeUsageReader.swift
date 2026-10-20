import Foundation

/// Claude Code's own `/usage`, asked of the installed `claude` command.
///
/// The figures come off the sign-in Claude Code already holds, so MYU STATS never touches the
/// credential: no keychain read, no file of tokens, no access prompt. In print mode `/usage` is a
/// local command that asks Anthropic for the limits; it sends no prompt to a model.
struct ClaudeUsageReader: ClaudeUsageSource {
    /// Print mode, no transcript and no MCP servers: without `--strict-mcp-config` every read would
    /// start whatever MCP servers the user has configured, none of which `/usage` needs.
    static let arguments = ["--print", "--no-session-persistence", "--strict-mcp-config", "/usage"]
    /// Long enough for a cold start on a busy machine; a stuck process is killed.
    static let timeout: TimeInterval = 30

    var isInstalled: Bool { Self.locate() != nil }

    func read() async throws -> (windows: [UsageWindow], plan: String?) {
        guard let binary = Self.locate() else { throw ClaudeUsageError.noAnswer }
        // Reading a pipe to the end blocks its thread, so the command runs off the cooperative pool.
        let text = try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                continuation.resume(with: Result { try Self.run(binary) })
            }
        }
        return (try Self.parse(text, now: Date()), Self.plan(in: text))
    }

    // MARK: - Finding it

    /// Where Claude Code installs itself. The app is started from Finder with a bare PATH, so these
    /// are checked directly. The copy inside the Claude desktop app is left out: it keeps its
    /// sign-in elsewhere and does not answer for the command line.
    static func locate(home: URL = FileManager.default.homeDirectoryForCurrentUser,
                       root: URL = URL(fileURLWithPath: "/"),
                       fileManager: FileManager = .default) -> URL? {
        let homePaths = [".local/bin/claude", ".claude/local/claude", ".bun/bin/claude", ".volta/bin/claude",
                         "Library/pnpm/claude", ".npm-global/bin/claude"]
        // npm under nvm installs into a folder named for the Node version; newest first.
        let nodeVersions = home.appendingPathComponent(".nvm/versions/node")
        let nvm = ((try? fileManager.contentsOfDirectory(atPath: nodeVersions.path)) ?? [])
            .sorted { $0.compare($1, options: .numeric) == .orderedDescending }
            .map { nodeVersions.appendingPathComponent($0).appendingPathComponent("bin/claude") }
        let candidates = homePaths.map { home.appendingPathComponent($0) } + nvm
            + ["opt/homebrew/bin/claude", "usr/local/bin/claude"].map { root.appendingPathComponent($0) }
        return candidates.first { url in
            fileManager.isExecutableFile(atPath: url.path)
                && !url.resolvingSymlinksInPath().path.contains("/Library/Application Support/Claude/")
        }
    }

    // MARK: - Asking it

    private static func run(_ binary: URL) throws -> String {
        // One fixed folder: Claude Code keys what it writes on the working directory, and a new one
        // per read would leave a trail of them behind.
        let scratch = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MYU STATS/claude-usage", isDirectory: true)
        try FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)

        let process = Process()
        process.executableURL = binary
        process.arguments = arguments
        process.currentDirectoryURL = scratch
        // Never a terminal: with the app's stdin it would wait for input until the timeout.
        process.standardInput = FileHandle.nullDevice
        let output = Pipe()
        process.standardOutput = output
        // Discarded rather than piped: a pipe nobody reads fills up and stalls the command.
        process.standardError = FileHandle.nullDevice
        try process.run()

        let watchdog = DispatchWorkItem { if process.isRunning { process.terminate() } }
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + timeout, execute: watchdog)
        defer { watchdog.cancel() }

        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            Log.claude.notice("claude /usage exited with status \(process.terminationStatus)")
            throw ClaudeUsageError.noAnswer
        }
        return String(decoding: data, as: UTF8.self)
    }

    // MARK: - Reading what it said

    /// The lines `/usage` leads with, for example
    ///
    ///     Current session: 10% used · resets Sep 24 at 12am (Asia/Tashkent)
    ///     Current week (all models): 45% used · resets Sep 26 at 6am (Asia/Tashkent)
    ///
    /// Everything after them is prose about what drove the usage, and is ignored.
    private static let line = try! NSRegularExpression(
        pattern: #"^Current (?:(session)|week \(([^)]+)\)):\s*(\d+(?:\.\d+)?)%\s*used(?:\s*·\s*resets\s*(.+?))?\s*$"#,
        options: [.anchorsMatchLines]
    )

    static func parse(_ text: String, now: Date) throws -> [UsageWindow] {
        var windows: [UsageWindow] = []
        for match in line.matches(in: text, range: NSRange(text.startIndex..., in: text)) {
            func group(_ index: Int) -> String? {
                Range(match.range(at: index), in: text).map { String(text[$0]) }
            }
            guard let percent = group(3).flatMap(Double.init) else { continue }
            let id: String
            let label: String
            if group(1) != nil {
                id = "session"
                label = String(localized: "Current session")
            } else {
                let scope = group(2) ?? ""
                id = "week-" + scope.lowercased().replacingOccurrences(of: " ", with: "-")
                label = scope.lowercased() == "all models"
                    ? String(localized: "This week")
                    : String(localized: "This week · \(scope)")
            }
            guard !windows.contains(where: { $0.id == id }) else { continue }
            windows.append(UsageWindow(id: id, label: label, usedFraction: percent / 100,
                                       resetsAt: group(4).flatMap { resetDate(from: $0, now: now) }))
        }
        // Without the session line there is no reading worth drawing.
        guard windows.contains(where: { $0.id == "session" }) else { throw ClaudeUsageError.unreadable }
        return windows
    }

    /// `Sep 24 at 12am (Asia/Tashkent)` → a date. No year is printed, so the one nearest `now` is
    /// chosen, which gets the turn of the year right in both directions.
    static func resetDate(from text: String, now: Date) -> Date? {
        var stamp = text.trimmingCharacters(in: .whitespaces)
        var zone = TimeZone.current
        if let open = stamp.lastIndex(of: "("), stamp.hasSuffix(")") {
            zone = TimeZone(identifier: String(stamp[stamp.index(after: open)...].dropLast())) ?? zone
            stamp = String(stamp[..<open]).trimmingCharacters(in: .whitespaces)
        }
        stamp = stamp.replacingOccurrences(of: "am", with: "AM").replacingOccurrences(of: "pm", with: "PM")

        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = zone
        // Minutes are left out on the hour: "Sep 7 at 2:59pm" but "Sep 7 at 3pm".
        guard let parsed = ["MMM d 'at' h:mma", "MMM d 'at' ha"].lazy.compactMap({ format -> Date? in
            formatter.dateFormat = format
            return formatter.date(from: stamp)
        }).first else { return nil }

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        var parts = calendar.dateComponents([.month, .day, .hour, .minute], from: parsed)
        let year = calendar.component(.year, from: now)
        return [year - 1, year, year + 1].compactMap { candidate -> Date? in
            parts.year = candidate
            return calendar.date(from: parts)
        }.min { abs($0.timeIntervalSince(now)) < abs($1.timeIntervalSince(now)) }
    }

    /// The plan `/usage` names at the top, when it names one. Whole words, as written: "Pro" must
    /// not be found inside "approximately".
    static func plan(in text: String) -> String? {
        let head = text.split(whereSeparator: \.isNewline).prefix(4).joined(separator: "\n")
        return ["Max 20x", "Max 5x", "Max", "Pro", "Team", "Enterprise"].first {
            head.range(of: "\\b\($0)\\b", options: .regularExpression) != nil
        }
    }
}
