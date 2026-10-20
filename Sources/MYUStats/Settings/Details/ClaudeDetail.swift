import SwiftUI

struct ClaudeDetail: View {
    var claude: ClaudeUsageStore

    var body: some View {
        let usage = claude.usage
        Section {
            if let usage, !usage.windows.isEmpty {
                ForEach(usage.windows) { window in
                    MeterRow(label: window.label, value: Format.percent(window.usedFraction),
                             fraction: window.usedFraction, color: Level.color(for: window.usedFraction))
                    if let resets = window.resetsAt {
                        LabeledContent("Resets", value: resetText(resets))
                    }
                }
            } else if usage == nil {
                Text("Reading Claude Code's limits…").foregroundStyle(.secondary)
            }
            if let message = usage?.message {
                Label(message, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(Level.color(for: 0.7))
            }
        } header: {
            Text("Limits")
        } footer: {
            Text("The ring shows whichever limit is closest to running out.")
        }
        .onAppear { claude.refreshIfStale() }

        if let today = claude.today {
            Section {
                if today.requests == 0 {
                    Text("Claude Code has not been used today.").foregroundStyle(.secondary)
                } else {
                    LabeledContent("Tokens", value: Format.tokens(today.tokens.total))
                    LabeledContent("Input", value: Format.tokens(today.tokens.input))
                    LabeledContent("Output", value: Format.tokens(today.tokens.output))
                    LabeledContent("Cache writes", value: Format.tokens(today.tokens.cacheWrite))
                    LabeledContent("Cache reads", value: Format.tokens(today.tokens.cacheRead))
                    LabeledContent("Requests", value: today.requests.formatted())
                    LabeledContent("At API prices", value: today.cost.map { Format.cost($0) } ?? "–")
                }
            } header: {
                Text("Today")
            } footer: {
                Text("Counted from Claude Code's session logs on this Mac since midnight, subagents included. The price is what the same tokens would cost on the API at list prices, not what a subscription pays.")
                    .multilineTextAlignment(.leading)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            if !today.projects.isEmpty {
                Section("Projects today") {
                    ForEach(today.projects.prefix(8)) { ShareRow(share: $0) }
                }
                Section("Models today") {
                    ForEach(today.models) { ShareRow(share: $0) }
                }
            }
        }

        Section {
            if let plan = usage?.plan {
                LabeledContent("Plan", value: plan)
            }
            LabeledContent("Last read", value: usage?.updatedAt.map { $0.formatted(date: .omitted, time: .shortened) } ?? "–")
            HStack {
                Text("Read every 5 minutes while the ring is shown or its alert is on.")
                    .foregroundStyle(.secondary)
                Spacer()
                if claude.isRefreshing {
                    ProgressView().controlSize(.small)
                }
                Button("Refresh") { Task { await claude.refresh(force: true) } }
                    .disabled(claude.isRefreshing)
            }
        } header: {
            Text("Source")
        } footer: {
            Text("Limits are asked of the installed claude command with /usage, the same figures Claude Code shows, and no prompt is sent to a model. Today's counts come from its session logs in ~/.claude/projects: only the token counts are taken from them, what was said is not kept, and nothing leaves this Mac. MYU STATS never reads Claude Code's sign-in. Hide the ring in Metrics, and turn its alert off, to stop the reads.")
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    /// A project or model: its name, its tokens under it, and what they would cost.
    private struct ShareRow: View {
        let share: ClaudeDayUsage.Share

        var body: some View {
            LabeledContent {
                Text(share.cost.map { Format.cost($0) } ?? "–").monospacedDigit()
            } label: {
                Text(verbatim: share.name)
                Text("\(Format.tokens(share.tokens)) tokens")
            }
        }
    }

    /// "Fri 06:00, in 2d 5h".
    private func resetText(_ date: Date) -> String {
        let when = date.formatted(.dateTime.weekday(.abbreviated).hour().minute())
        return String(localized: "\(when), in \(Format.timeUntil(date))")
    }
}
