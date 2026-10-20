import Foundation

/// Tokens by kind, as Claude Code's logs count them.
struct TokenCounts: Equatable, Sendable {
    var input = 0
    var output = 0
    var cacheWrite = 0
    var cacheRead = 0

    var total: Int { input + output + cacheWrite + cacheRead }

    static func += (lhs: inout TokenCounts, rhs: TokenCounts) {
        lhs.input += rhs.input
        lhs.output += rhs.output
        lhs.cacheWrite += rhs.cacheWrite
        lhs.cacheRead += rhs.cacheRead
    }
}

/// One API request Claude Code logged: when, which model, in which project, and its token counts.
struct LoggedRequest: Equatable, Sendable {
    /// ISO 8601 in UTC, as the logs write it: "2026-09-25T09:41:07.815Z".
    var timestamp: String
    var model: String
    /// The working directory Claude Code ran in.
    var project: String
    var input = 0
    var output = 0
    var cacheWrite5m = 0
    var cacheWrite1h = 0
    var cacheRead = 0
    var webSearches = 0
    var isFast = false

    var tokens: TokenCounts {
        TokenCounts(input: input, output: output, cacheWrite: cacheWrite5m + cacheWrite1h, cacheRead: cacheRead)
    }
}

/// What Claude Code used on one day, counted from its session logs.
struct ClaudeDayUsage: Equatable, Sendable {
    /// A project's or a model's part of the day.
    struct Share: Equatable, Sendable, Identifiable {
        let id: String
        let name: String
        var tokens = 0
        /// At API prices; nil when none of its models has a known price.
        var cost: Double?
    }

    /// Midnight at the start of the day.
    var day: Date
    var readAt: Date
    var tokens = TokenCounts()
    var requests = 0
    /// What the same tokens would cost on the API at list prices; nil when no model had a known price.
    var cost: Double?
    /// Largest first.
    var projects: [Share] = []
    var models: [Share] = []

    static func summarize(_ requests: some Sequence<LoggedRequest>, day: Date, readAt: Date) -> ClaudeDayUsage {
        var usage = ClaudeDayUsage(day: day, readAt: readAt)
        var projects: [String: Share] = [:]
        var models: [String: Share] = [:]
        func add(_ shares: inout [String: Share], id: String, name: @autoclosure () -> String, tokens: Int, cost: Double?) {
            var share = shares[id] ?? Share(id: id, name: name())
            share.tokens += tokens
            if let cost { share.cost = (share.cost ?? 0) + cost }
            shares[id] = share
        }
        for request in requests where request.tokens.total > 0 {
            let cost = ClaudePricing.cost(of: request)
            usage.requests += 1
            usage.tokens += request.tokens
            if let cost { usage.cost = (usage.cost ?? 0) + cost }
            add(&projects, id: request.project, name: projectName(request.project), tokens: request.tokens.total, cost: cost)
            let model = ClaudePricing.normalized(request.model) ?? request.model
            add(&models, id: model, name: ClaudePricing.displayName(request.model), tokens: request.tokens.total, cost: cost)
        }
        func largestFirst(_ shares: [String: Share]) -> [Share] {
            shares.values.sorted { ($0.cost ?? -1, $0.tokens) > ($1.cost ?? -1, $1.tokens) }
        }
        usage.projects = largestFirst(projects)
        usage.models = largestFirst(models)
        return usage
    }

    private static func projectName(_ path: String) -> String {
        let name = URL(fileURLWithPath: path).lastPathComponent
        return path.isEmpty || name.isEmpty || name == "/" ? String(localized: "Unknown") : name
    }
}

/// A model's API list prices, in dollars per million tokens.
struct ModelPrice: Equatable, Sendable {
    var input: Double
    var output: Double
    var cacheWrite5m: Double
    var cacheWrite1h: Double
    var cacheRead: Double
    /// Fast mode's rates, on the models that have it.
    var fastInput: Double?
    var fastOutput: Double?
}

/// Anthropic's API list prices (platform.claude.com, Pricing, September 2026), for putting a price on
/// Claude Code's tokens. A subscription pays differently; this is a yardstick, not a bill.
enum ClaudePricing {
    /// Keyed by model ID without its date. The longest matching key wins, so Opus 5.5 is not priced as Opus 5.
    static let prices: [String: ModelPrice] = {
        func price(_ input: Double, _ output: Double, _ write5m: Double, _ write1h: Double, _ read: Double,
                   fast: (Double, Double)? = nil) -> ModelPrice {
            ModelPrice(input: input, output: output, cacheWrite5m: write5m, cacheWrite1h: write1h, cacheRead: read,
                       fastInput: fast?.0, fastOutput: fast?.1)
        }
        let fable51 = price(10, 50, 12.5, 20, 0.25)
        let fable5 = price(10, 50, 12.5, 20, 1)
        let opus4 = price(5, 25, 6.25, 10, 0.5)
        let opus4Early = price(15, 75, 18.75, 30, 1.5)
        let sonnet4 = price(3, 15, 3.75, 6, 0.3)
        return [
            "claude-fable-5-1": fable51,
            "claude-mythos-5-1": fable51,
            "claude-fable-5": fable5,
            "claude-mythos-5": fable5,
            "claude-opus-5-5": price(4, 20, 5, 8, 0.2, fast: (8, 40)),
            "claude-opus-5": price(5, 25, 6.25, 10, 0.5, fast: (10, 50)),
            "claude-opus-4-8": price(5, 25, 6.25, 10, 0.5, fast: (10, 50)),
            "claude-opus-4-7": opus4,
            "claude-opus-4-6": opus4,
            "claude-opus-4-5": opus4,
            "claude-opus-4-1": opus4Early,
            "claude-opus-4": opus4Early,
            "claude-sonnet-5": price(2, 10, 2.5, 4, 0.2),
            "claude-sonnet-4-6": sonnet4,
            "claude-sonnet-4-5": sonnet4,
            "claude-sonnet-4": sonnet4,
            "claude-haiku-4-5": price(1, 5, 1.25, 2, 0.1),
            "claude-3-5-haiku": price(0.8, 4, 1, 1.6, 0.08),
        ]
    }()

    /// Each web search is charged on top of its tokens.
    static let webSearch = 0.01

    /// The model ID alone: "claude-haiku-4-5-20251001" → "claude-haiku-4-5", and likewise for the Bedrock
    /// ("us.anthropic.claude-opus-4-1-20250805-v1:0") and Vertex ("claude-opus-4-5@20251101") forms.
    static func normalized(_ model: String) -> String? {
        let lower = model.lowercased()
        guard let start = lower.range(of: "claude-") else { return nil }
        var id = String(lower[start.lowerBound...])
        if let cut = id.firstIndex(where: { $0 == "@" || $0 == ":" || $0 == "[" }) { id = String(id[..<cut]) }
        id = id.replacingOccurrences(of: #"-v\d+$"#, with: "", options: .regularExpression)
        id = id.replacingOccurrences(of: #"-\d{8}$"#, with: "", options: .regularExpression)
        return id
    }

    static func price(for model: String) -> ModelPrice? {
        guard let id = normalized(model) else { return nil }
        let key = prices.keys.filter { id == $0 || id.hasPrefix($0 + "-") }.max { $0.count < $1.count }
        return key.flatMap { prices[$0] }
    }

    /// Nil for a model without a known price.
    static func cost(of request: LoggedRequest) -> Double? {
        guard let price = price(for: request.model) else { return nil }
        var rates = price
        if request.isFast, let input = price.fastInput, let output = price.fastOutput {
            // Caching's multipliers apply on top of fast mode's rates.
            let scale = input / price.input
            rates = ModelPrice(input: input, output: output, cacheWrite5m: price.cacheWrite5m * scale,
                               cacheWrite1h: price.cacheWrite1h * scale, cacheRead: price.cacheRead * scale)
        }
        let perMillion = Double(request.input) * rates.input + Double(request.output) * rates.output
            + Double(request.cacheWrite5m) * rates.cacheWrite5m + Double(request.cacheWrite1h) * rates.cacheWrite1h
            + Double(request.cacheRead) * rates.cacheRead
        return perMillion / 1_000_000 + Double(request.webSearches) * webSearch
    }

    /// "claude-opus-5-5" → "Opus 5.5", "claude-3-5-haiku" → "Haiku 3.5".
    static func displayName(_ model: String) -> String {
        guard let id = normalized(model) else { return model }
        let parts = id.dropFirst("claude-".count).split(separator: "-").map(String.init)
        guard let family = parts.first(where: { ["opus", "sonnet", "haiku", "fable", "mythos"].contains($0) }) else { return id }
        let version = parts.filter { $0.allSatisfy(\.isNumber) }.joined(separator: ".")
        return version.isEmpty ? family.capitalized : "\(family.capitalized) \(version)"
    }
}

/// Reads Claude Code's own session logs for the day's token counts. The logs are JSON lines, one per
/// event, and grow to hundreds of megabytes a day, so each file is read once and then only from where
/// the last read stopped; only assistant turns from today are decoded, and only their counts are kept,
/// never what was said.
actor ClaudeLogReader {
    private struct FileLog {
        var offset: UInt64 = 0
        /// The start of a line still being written.
        var tail = Data()
        /// By message and request ID: a reply logged in several lines, or copied into a resumed session, counts once.
        var requests: [String: LoggedRequest] = [:]
    }

    nonisolated let roots: [URL]
    private var files: [String: FileLog] = [:]
    private var cutoff = ""

    init(roots: [URL] = ClaudeLogReader.defaultRoots()) {
        self.roots = roots
    }

    /// Where Claude Code keeps its projects: `CLAUDE_CONFIG_DIR`, else `~/.config/claude` and `~/.claude`.
    static func defaultRoots(environment: [String: String] = ProcessInfo.processInfo.environment,
                             home: URL = FileManager.default.homeDirectoryForCurrentUser) -> [URL] {
        if let configured = environment["CLAUDE_CONFIG_DIR"], !configured.isEmpty {
            return configured.split(separator: ",").map {
                URL(fileURLWithPath: $0.trimmingCharacters(in: .whitespaces)).appendingPathComponent("projects")
            }
        }
        return [home.appendingPathComponent(".config/claude/projects"), home.appendingPathComponent(".claude/projects")]
    }

    nonisolated var hasLogs: Bool {
        roots.contains { FileManager.default.fileExists(atPath: $0.path) }
    }

    /// Everything logged from midnight to now.
    func today(now: Date = Date(), calendar: Calendar = .current) -> ClaudeDayUsage {
        let start = calendar.startOfDay(for: now)
        let cutoff = Self.timestamp(start)
        if cutoff != self.cutoff {
            // A new day: what came before it no longer counts.
            self.cutoff = cutoff
            for path in Array(files.keys) {
                let kept = files[path]?.requests.filter { $0.value.timestamp >= cutoff } ?? [:]
                files[path]?.requests = kept
            }
        }
        var current: Set<String> = []
        for root in roots {
            // A file last written before midnight holds nothing from today.
            for (url, size) in Self.logFiles(in: root, modifiedSince: start) {
                current.insert(url.path)
                read(url, size: size)
            }
        }
        files = files.filter { current.contains($0.key) }

        var merged: [String: LoggedRequest] = [:]
        for log in files.values {
            for (key, request) in log.requests where request.timestamp >= cutoff {
                if let seen = merged[key], seen.output >= request.output { continue }
                merged[key] = request
            }
        }
        return ClaudeDayUsage.summarize(merged.values, day: start, readAt: now)
    }

    private func read(_ url: URL, size: UInt64) {
        var log = files[url.path] ?? FileLog()
        // Shorter than where the last read ended: the file was replaced, so start again.
        if size < log.offset { log = FileLog() }
        defer { files[url.path] = log }
        guard size > log.offset, let handle = try? FileHandle(forReadingFrom: url) else { return }
        defer { try? handle.close() }
        let cutoff = Array(self.cutoff.utf8)
        // Taken out while the lines are read, so the dictionary is changed in place rather than copied.
        var requests = log.requests
        log.requests = [:]
        defer { log.requests = requests }
        do {
            try handle.seek(toOffset: log.offset)
            while let chunk = try handle.read(upToCount: 4 << 20), !chunk.isEmpty {
                log.offset += UInt64(chunk.count)
                let data = log.tail.isEmpty ? chunk : log.tail + chunk
                log.tail = Self.scan(data, cutoff: cutoff) { line in
                    guard let (key, request) = Self.request(from: line) else { return }
                    // Streamed replies are logged more than once; the last copy has the final count.
                    if let seen = requests[key], seen.output > request.output { return }
                    requests[key] = request
                }
            }
        } catch {
            Log.claude.notice("A Claude Code log could not be read: \(error.localizedDescription, privacy: .public)")
        }
    }

    // MARK: - Reading lines

    private static let assistant = Array(#""type":"assistant""#.utf8)
    private static let timestampKey = Array(#""timestamp":""#.utf8)

    /// Hands `line` each complete line that may be an assistant turn logged at or after `cutoff`, found by
    /// a byte search rather than by decoding, and returns the unfinished last line.
    static func scan(_ data: Data, cutoff: [UInt8], line: (Data) -> Void) -> Data {
        data.withUnsafeBytes { (buffer: UnsafeRawBufferPointer) -> Data in
            guard let base = buffer.baseAddress else { return Data() }
            var start = 0
            while start < buffer.count, let newline = memchr(base + start, 0x0A, buffer.count - start) {
                let end = base.distance(to: UnsafeRawPointer(newline))
                let length = end - start
                if length > 0, memmem(base + start, length, assistant, assistant.count) != nil,
                   !isBefore(base + start, length: length, cutoff: cutoff) {
                    line(Data(bytes: base + start, count: length))
                }
                start = end + 1
            }
            return start < buffer.count ? Data(bytes: base + start, count: buffer.count - start) : Data()
        }
    }

    /// Whether the line's timestamp sorts before `cutoff`, compared as text: both are ISO 8601 in UTC.
    private static func isBefore(_ line: UnsafeRawPointer, length: Int, cutoff: [UInt8]) -> Bool {
        guard let found = memmem(line, length, timestampKey, timestampKey.count) else { return false }
        let value = line.distance(to: UnsafeRawPointer(found)) + timestampKey.count
        guard value + cutoff.count <= length else { return false }
        return memcmp(line + value, cutoff, cutoff.count) < 0
    }

    /// One line of a log: an assistant turn with its counts, under its message and request IDs; nil for anything else.
    static func request(from line: Data) -> (key: String, request: LoggedRequest)? {
        guard let object = try? JSONSerialization.jsonObject(with: line) as? [String: Any],
              object["type"] as? String == "assistant",
              let message = object["message"] as? [String: Any],
              let usage = message["usage"] as? [String: Any],
              let timestamp = object["timestamp"] as? String
        else { return nil }
        func count(_ dictionary: [String: Any]?, _ key: String) -> Int {
            (dictionary?[key] as? NSNumber)?.intValue ?? 0
        }
        let key = (message["id"] as? String).map { "\($0)|\(object["requestId"] as? String ?? "")" } ?? object["uuid"] as? String
        guard let key else { return nil }

        // Newer logs split cache writes by how long they last; older ones only give the total, at the 5-minute rate.
        let writes = count(usage, "cache_creation_input_tokens")
        let split = usage["cache_creation"] as? [String: Any]
        let write1h = min(count(split, "ephemeral_1h_input_tokens"), writes)
        let request = LoggedRequest(
            timestamp: timestamp,
            model: message["model"] as? String ?? "",
            project: object["cwd"] as? String ?? "",
            input: count(usage, "input_tokens"),
            output: count(usage, "output_tokens"),
            cacheWrite5m: writes - write1h,
            cacheWrite1h: write1h,
            cacheRead: count(usage, "cache_read_input_tokens"),
            webSearches: count(usage["server_tool_use"] as? [String: Any], "web_search_requests"),
            isFast: usage["speed"] as? String == "fast"
        )
        return (key, request)
    }

    // MARK: - Files

    /// "2026-09-24T19:00:00.000Z" for midnight in Tashkent: how the logs write their timestamps.
    static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        formatter.timeZone = TimeZone(identifier: "UTC")
        return formatter.string(from: date)
    }

    /// Every log under `root`, subagents' included, written to since `date`.
    private static func logFiles(in root: URL, modifiedSince date: Date) -> [(URL, UInt64)] {
        let keys: [URLResourceKey] = [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]
        guard let enumerator = FileManager.default.enumerator(at: root, includingPropertiesForKeys: keys, options: [.skipsHiddenFiles])
        else { return [] }
        var result: [(URL, UInt64)] = []
        for case let url as URL in enumerator where url.pathExtension == "jsonl" {
            guard let values = try? url.resourceValues(forKeys: Set(keys)), values.isRegularFile == true,
                  let modified = values.contentModificationDate, modified >= date
            else { continue }
            result.append((url, UInt64(values.fileSize ?? 0)))
        }
        return result
    }
}
