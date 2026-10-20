import SwiftUI

/// Hover card for the Claude Code ring: each limit with its reset, or why there is no reading.
struct ClaudeCard: View {
    var claude: ClaudeUsageStore

    var body: some View {
        let usage = claude.usage
        CardHeader(symbol: StatMetric.claude.symbol, title: StatMetric.claude.title, subtitle: subtitle(usage))
            .onAppear { claude.refreshIfStale() }
        if let usage, !usage.windows.isEmpty {
            ForEach(usage.windows) { window in
                UsageSection(
                    title: window.label,
                    trailing: Format.percent(window.usedFraction),
                    fraction: window.usedFraction,
                    color: Level.color(for: window.usedFraction),
                    caption: window.resetsAt.map { String(localized: "Resets in \(Format.timeUntil($0))") }
                        ?? String(localized: "No reset time given")
                )
            }
        } else if usage == nil {
            Text("Reading Claude Code's limits…")
                .font(.system(size: 11))
                .foregroundStyle(.white.opacity(0.6))
        }
        if let today = claude.today, today.requests > 0 {
            CardDivider()
            SectionTitle(text: String(localized: "Today"))
            DetailRow(label: String(localized: "Tokens"), value: Format.tokens(today.tokens.total))
            if let cost = today.cost {
                DetailRow(label: String(localized: "At API prices"), value: Format.cost(cost))
            }
            if let project = today.projects.first {
                DetailRow(label: String(localized: "Busiest project"), value: project.name, valueTruncates: true)
            }
        }
        if let message = usage?.message {
            CardDivider()
            Label(message, systemImage: "exclamationmark.triangle.fill")
                .font(.system(size: 10, weight: .medium))
                .foregroundStyle(Level.color(for: 0.7))
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func subtitle(_ usage: ClaudeUsage?) -> String {
        guard let usage, let updated = usage.updatedAt else { return String(localized: "Session and weekly limits") }
        let ago = Format.updatedAgo(updated)
        return usage.plan.map { "\($0) · \(ago)" } ?? ago
    }
}
