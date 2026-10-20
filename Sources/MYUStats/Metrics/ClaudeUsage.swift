import Foundation

/// One of Claude Code's usage limits: how much of it is used, and when it starts over.
struct UsageWindow: Equatable, Sendable, Identifiable {
    let id: String
    let label: String
    /// 0 to 1.
    let usedFraction: Double
    let resetsAt: Date?
}

/// Claude Code's limits as `/usage` last reported them.
struct ClaudeUsage: Equatable, Sendable {
    enum Status: Equatable, Sendable {
        case ok
        /// Nothing to show: Claude Code did not answer. The message says what to do.
        case unavailable(String)
        /// The last read failed; the windows are from an earlier one.
        case stale(String)
    }

    var status: Status
    var windows: [UsageWindow] = []
    var plan: String?
    /// When the windows were read.
    var updatedAt: Date?

    /// The ring shows the limit closest to running out: a weekly limit at 95 % matters more than
    /// a session at 10 %.
    var headline: UsageWindow? { windows.max { $0.usedFraction < $1.usedFraction } }

    var message: String? {
        switch status {
        case .ok: nil
        case .unavailable(let message), .stale(let message): message
        }
    }
}

enum ClaudeUsageError: Error, Equatable {
    /// The command exited with an error or timed out; in practice, no sign-in.
    case noAnswer
    /// It answered, but without the lines this reads.
    case unreadable
}

/// Where readings come from; a fake stands in for it in tests.
protocol ClaudeUsageSource: Sendable {
    /// Whether Claude Code is installed where this can run it.
    var isInstalled: Bool { get }
    func read() async throws -> (windows: [UsageWindow], plan: String?)
}
