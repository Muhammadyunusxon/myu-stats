import Foundation
import Observation

/// Claude Code's usage limits, read every few minutes by running `claude /usage`.
///
/// Only read while the Claude ring is on screen or its alert is on: each read runs Claude Code for
/// several seconds, so a folded pill, a hidden ring and the alert off stop the subprocess.
@MainActor
@Observable
final class ClaudeUsageStore {
    /// The figures move slowly, and every read starts a Claude Code process for a few seconds.
    static let refreshInterval: TimeInterval = 300
    /// Today's counts come from local logs, read from where the last read stopped, so they can be fresher.
    static let todayRefreshInterval: TimeInterval = 60

    private(set) var usage: ClaudeUsage?
    private(set) var isRefreshing = false
    private(set) var isInstalled: Bool
    /// Today's tokens and their price, from Claude Code's session logs; nil until first read, or without logs.
    private(set) var today: ClaudeDayUsage?

    /// Called when Claude Code is installed or removed, so the pill can re-lay itself out.
    @ObservationIgnored var onAvailabilityChanged: (() -> Void)?

    /// Whether the ring can be seen: the pill is unfolded and shows it. Coming into view catches up
    /// on a read skipped while out of sight.
    @ObservationIgnored var isOnScreen = true {
        didSet { if isOnScreen, !oldValue { refreshIfStale() } }
    }

    @ObservationIgnored private let source: ClaudeUsageSource
    @ObservationIgnored private let logs: ClaudeLogReader
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var isReadingLogs = false

    init(source: ClaudeUsageSource = ClaudeUsageReader(), logs: ClaudeLogReader = ClaudeLogReader(),
         defaults: UserDefaults = .standard) {
        self.source = source
        self.logs = logs
        self.defaults = defaults
        isInstalled = source.isInstalled
    }

    func start() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: Self.refreshInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refreshInBackground() }
        }
        timer?.tolerance = 30
        refreshInBackground()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    /// For a card or page that just opened: reads unless the figures are still fresh.
    func refreshIfStale() {
        if Date().timeIntervalSince(today?.readAt ?? .distantPast) > Self.todayRefreshInterval {
            Task { await refreshToday() }
        }
        let updated = usage?.updatedAt ?? .distantPast
        guard Date().timeIntervalSince(updated) > Self.refreshInterval else { return }
        refreshInBackground(force: true)
    }

    /// Counts today's tokens from the logs; quick after the first time, which reads each of today's logs once.
    func refreshToday() async {
        guard !isReadingLogs, logs.hasLogs else { return }
        isReadingLogs = true
        defer { isReadingLogs = false }
        let started = ContinuousClock.now
        let counted = await logs.today()
        if today == nil {
            Log.claude.info("Counted today's Claude Code logs in \((ContinuousClock.now - started).formatted(.units(allowed: [.milliseconds])), privacy: .public)")
        }
        today = counted
    }

    private func refreshInBackground(force: Bool = false) {
        Task { await refresh(force: force) }
    }

    /// Reads the limits, unless nothing needs them: the ring is out of sight and its alert is off.
    /// `force` reads anyway, for the Refresh button and for the Details page, which can be opened with
    /// the ring hidden.
    func refresh(force: Bool = false) async {
        let installed = source.isInstalled
        if installed != isInstalled {
            isInstalled = installed
            onAvailabilityChanged?()
        }
        let isWanted = (isOnScreen && !defaults.hiddenMetrics.contains(.claude))
            || defaults.bool(forKey: SettingsKey.alertClaude)
        guard installed, !isRefreshing, force || isWanted else { return }
        await refreshToday()

        isRefreshing = true
        defer { isRefreshing = false }
        Log.claude.info("Reading Claude Code usage (forced: \(force))")
        do {
            let reading = try await source.read()
            usage = ClaudeUsage(status: .ok, windows: reading.windows, plan: reading.plan, updatedAt: Date())
        } catch {
            Log.claude.notice("Claude Code usage not read: \(String(describing: error), privacy: .public)")
            let message = Self.message(for: error)
            // A failed read keeps the last good figures, dated, rather than blanking the ring.
            if let previous = usage, !previous.windows.isEmpty {
                usage = ClaudeUsage(status: .stale(message), windows: previous.windows, plan: previous.plan,
                                    updatedAt: previous.updatedAt)
            } else {
                usage = ClaudeUsage(status: .unavailable(message), updatedAt: Date())
            }
        }
    }

    static func message(for error: Error) -> String {
        switch error as? ClaudeUsageError {
        case .noAnswer:
            String(localized: "Claude Code did not answer. Run claude in Terminal and sign in.")
        case .unreadable:
            String(localized: "Claude Code answered in a form MYU STATS does not understand yet.")
        case nil:
            error.localizedDescription
        }
    }
}
