import SwiftUI

/// A page of the Settings window: one of the settings panes, or a metric's detail page.
enum SettingsSection: Hashable, Identifiable {
    case metrics, appearance, general
    case detail(StatMetric)

    static let settingsPanes: [SettingsSection] = [.metrics, .appearance, .general]

    var id: String {
        switch self {
        case .metrics: "metrics"
        case .appearance: "appearance"
        case .general: "general"
        case .detail(let metric): "detail.\(metric.rawValue)"
        }
    }

    var title: String {
        switch self {
        case .metrics: String(localized: "Metrics")
        case .appearance: String(localized: "Appearance")
        case .general: String(localized: "General")
        case .detail(let metric): metric.title
        }
    }

    var subtitle: String {
        switch self {
        case .metrics: String(localized: "Choose which rings the pill shows, and in what order.")
        case .appearance: String(localized: "How the pill looks and where it sits.")
        case .general: String(localized: "Startup, performance and everything else.")
        case .detail(.cpu): String(localized: "Load per core, history and the busiest processes.")
        case .detail(.memory): String(localized: "Where the memory goes and who is using it.")
        case .detail(.network): String(localized: "Throughput, history and the active interface.")
        case .detail(.disk): String(localized: "Space on every mounted volume.")
        case .detail(.thermals): String(localized: "Every sensor group and each fan.")
        case .detail(.battery): String(localized: "Charge, health and power source.")
        }
    }

    var icon: String {
        switch self {
        case .metrics: "chart.bar"
        case .appearance: "paintbrush"
        case .general: "gearshape"
        case .detail(let metric): metric.symbol
        }
    }
}

/// Which page Settings shows; set from outside when a card's "Details" button is clicked.
@MainActor
@Observable
final class SettingsNavigation {
    var selection: SettingsSection = .metrics
}

struct SettingsView: View {
    var store: StatsStore
    var navigation: SettingsNavigation
    let quit: () -> Void

    @Namespace private var selectionSpace
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
    }

    private var detailSections: [SettingsSection] {
        StatMetric.allCases.filter { metric in
            switch metric {
            case .battery: store.battery != nil
            case .thermals: store.thermals != nil
            default: true
            }
        }
        .map(SettingsSection.detail)
    }

    var body: some View {
        // A plain HStack rather than NavigationSplitView: the sidebar never collapses,
        // and a split view would add a sidebar toggle to the title bar.
        HStack(spacing: 0) {
            sidebar
            ZStack {
                pane(for: navigation.selection)
                    .id(navigation.selection)
                    .transition(reduceMotion ? .opacity : .blurFade)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            .animation(.easeOut(duration: reduceMotion ? 0.12 : 0.24), value: navigation.selection)
            .clipped()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        // The window itself is transparent; this is the whole visible surface,
        // and clipping it rounds all four corners.
        .background(SettingsPalette.window)
        .clipShape(RoundedRectangle(cornerRadius: SettingsMetrics.cornerRadius, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: SettingsMetrics.cornerRadius, style: .continuous)
                .strokeBorder(SettingsPalette.edge, lineWidth: 1)
        }
        .environment(\.colorScheme, .dark)
        .tint(Level.accent)
        .ignoresSafeArea()
    }

    // MARK: - Sidebar

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            Color.clear.frame(height: SettingsMetrics.headerHeight)

            HStack(spacing: 10) {
                Image(systemName: "gauge.with.dots.needle.50percent")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(.white)
                    .frame(width: 22, height: 22)
                    .background(
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .fill(LinearGradient(colors: [Level.accent, Level.accent.opacity(0.6)],
                                                 startPoint: .top, endPoint: .bottom))
                    )
                Text(verbatim: "MYU STATS")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.white)
            }
            .padding(.horizontal, 18)
            .padding(.bottom, 16)

            ScrollView {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(SettingsSection.settingsPanes) { sidebarRow($0) }

                    Text("DETAILS")
                        .font(.system(size: 10, weight: .semibold))
                        .tracking(0.6)
                        .foregroundStyle(.white.opacity(0.35))
                        .padding(.horizontal, 10)
                        .padding(.top, 16)
                        .padding(.bottom, 4)

                    ForEach(detailSections) { sidebarRow($0) }
                }
                .padding(.horizontal, 10)
            }
            .scrollIndicators(.never)

            Spacer(minLength: 0)

            VStack(alignment: .leading, spacing: 6) {
                QuitRow(quit: quit)
                Text("MYU STATS \(version)")
                    .font(.system(size: 11))
                    .foregroundStyle(.white.opacity(0.32))
                    .padding(.horizontal, 10)
            }
            .padding(.horizontal, 10)
            .padding(.bottom, 16)
        }
        .frame(width: SettingsMetrics.sidebarWidth)
        .frame(maxHeight: .infinity)
        .background(SettingsPalette.sidebar)
        .overlay(alignment: .trailing) { SettingsPalette.hairline.frame(width: 1) }
    }

    private func sidebarRow(_ section: SettingsSection) -> some View {
        SidebarRow(section: section, isSelected: navigation.selection == section, selectionSpace: selectionSpace) {
            guard section != navigation.selection else { return }
            withAnimation(.spring(response: 0.34, dampingFraction: 0.82)) { navigation.selection = section }
        }
    }

    // MARK: - Pane

    private func pane(for section: SettingsSection) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 3) {
                Text(section.title)
                    .font(.system(size: 19, weight: .semibold))
                    .foregroundStyle(.white)
                Text(section.subtitle)
                    .font(.system(size: 12))
                    .foregroundStyle(.white.opacity(0.5))
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 20)
            .frame(maxWidth: .infinity, alignment: .leading)

            SettingsPalette.hairline.frame(height: 1)

            Form {
                switch section {
                case .metrics: MetricsPane(store: store)
                case .appearance: AppearancePane()
                case .general: GeneralPane()
                case .detail(let metric): MetricDetailPane(metric: metric, store: store)
                }
            }
            .formStyle(.grouped)
            // The pane sits on the window's own dark ground; sections draw as raised cards.
            .scrollContentBackground(.hidden)
            .buttonStyle(SettingsButtonStyle())
        }
    }
}

// MARK: - Sidebar rows

private struct SidebarRow: View {
    let section: SettingsSection
    let isSelected: Bool
    let selectionSpace: Namespace.ID
    let select: () -> Void

    @State private var isHovered = false
    @State private var isPressed = false
    /// Bumped on selection to play the icon's bounce once.
    @State private var bounce = 0

    private static let pill = RoundedRectangle(cornerRadius: 8, style: .continuous)

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: section.icon)
                .font(.system(size: 13))
                .symbolEffect(.bounce, value: bounce)
                .frame(width: 18)
                .foregroundStyle(.white.opacity(isSelected ? 0.95 : isHovered ? 0.85 : 0.6))
                .offset(x: isHovered && !isSelected ? 1.5 : 0)
            Text(section.title)
                .font(.system(size: 13, weight: isSelected ? .medium : .regular))
                .foregroundStyle(.white.opacity(isSelected ? 0.95 : isHovered ? 0.92 : 0.78))
            Spacer(minLength: 4)
        }
        .padding(.leading, 10)
        .padding(.trailing, 8)
        .padding(.vertical, 6)
        .background {
            ZStack {
                if isHovered && !isSelected {
                    Self.pill.fill(SettingsPalette.hovered).transition(.opacity)
                }
                if isSelected {
                    // One pill shared by every row, so it slides to the new selection.
                    Self.pill
                        .fill(SettingsPalette.selected)
                        .overlay {
                            Self.pill.strokeBorder(
                                LinearGradient(colors: [.white.opacity(0.10), .white.opacity(0.02)],
                                               startPoint: .top, endPoint: .bottom),
                                lineWidth: 0.5
                            )
                        }
                        .matchedGeometryEffect(id: "selection", in: selectionSpace)
                }
            }
        }
        .contentShape(Self.pill)
        .scaleEffect(isPressed ? 0.97 : 1)
        .animation(.spring(response: 0.22, dampingFraction: 0.6), value: isPressed)
        .onHover { hovering in
            withAnimation(.easeOut(duration: 0.14)) { isHovered = hovering }
        }
        .simultaneousGesture(
            DragGesture(minimumDistance: 0)
                .onChanged { _ in if !isPressed { isPressed = true } }
                .onEnded { value in
                    isPressed = false
                    if abs(value.translation.width) < 6, abs(value.translation.height) < 6 { select() }
                }
        )
        .onChange(of: isSelected) { _, selected in
            if selected { bounce += 1 }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(isSelected ? [.isButton, .isSelected] : .isButton)
        .accessibilityAction { select() }
    }
}

private struct QuitRow: View {
    let quit: () -> Void
    @State private var isHovered = false

    var body: some View {
        Button(action: quit) {
            HStack(spacing: 10) {
                Image(systemName: "power")
                    .font(.system(size: 12))
                    .frame(width: 18)
                Text("Quit MYU STATS")
                    .font(.system(size: 13))
            }
            .foregroundStyle(isHovered ? SettingsPalette.destructive : Color.white.opacity(0.55))
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(isHovered ? SettingsPalette.hovered : Color.clear)
            }
            .contentShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
        }
        .buttonStyle(.plain)
        .onHover { hovering in
            withAnimation(.easeOut(duration: 0.14)) { isHovered = hovering }
        }
    }
}
