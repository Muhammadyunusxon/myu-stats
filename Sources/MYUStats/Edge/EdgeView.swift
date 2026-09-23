import SwiftUI

@MainActor
@Observable
final class EdgeState {
    var layout = EdgeLayout(edge: .right, cellCount: 0)
    var metrics: [StatMetric] = []
    var hovered: StatMetric?
    /// False while auto-hidden: only a thin handle stays on the edge.
    var isRevealed = true
    var isOrbHovered = false
    var isHandleHovered = false
    var isDetailsHovered = false
    var isDragging = false
    /// The pill dims while a drop on the other edge is armed, so it reads as "leaving".
    var isDropArmed = false
    /// Bumped on each orb click to spin the gear.
    var orbSpins = 0
    /// Measured height of the visible card, fed back so the controller can hit-test it.
    var cardHeight: CGFloat = 360
}

struct EdgeView: View {
    var store: StatsStore
    var state: EdgeState

    var body: some View {
        let layout = state.layout
        let revealed = state.isRevealed
        ZStack(alignment: .topLeading) {
            pill(layout: layout, revealed: revealed)

            ForEach(Array(state.metrics.enumerated()), id: \.element) { index, metric in
                let rect = layout.cellRect(index)
                MetricCell(metric: metric, store: store, isHovered: state.hovered == metric)
                    .frame(width: rect.width, height: rect.height)
                    .scaleEffect(revealed ? 1 : 0.4, anchor: layout.edge.anchor)
                    .opacity(revealed ? 1 : 0)
                    .offset(layout.offset(for: rect.origin, pushedOut: revealed ? 0 : 28))
                    .animation(revealed ? Motion.stagger(index: index) : Motion.fold, value: revealed)
            }

            arcButton(.move, symbol: "arrow.up.and.down.and.arrow.left.and.right",
                      isHovered: state.isHandleHovered || state.isDragging, spins: 0,
                      layout: layout, revealed: revealed, order: 0)
            arcButton(.settings, symbol: "gearshape",
                      isHovered: state.isOrbHovered, spins: state.orbSpins,
                      layout: layout, revealed: revealed, order: state.metrics.count + 1)

            if let hovered = state.hovered, let index = state.metrics.firstIndex(of: hovered) {
                card(for: hovered, index: index, layout: layout)
                    .transition(.asymmetric(
                        insertion: .opacity
                            .combined(with: .scale(scale: 0.88, anchor: layout.edge.anchor))
                            .combined(with: .offset(layout.offset(for: .zero, pushedOut: 16))),
                        removal: .opacity
                            .combined(with: .scale(scale: 0.94, anchor: layout.edge.anchor))
                    ))
            }
        }
        .frame(width: layout.panelSize.width, height: layout.panelSize.height, alignment: .topLeading)
        .opacity(state.isDropArmed ? 0.4 : 1)
        .onPreferenceChange(CardHeightKey.self) { height in
            MainActor.assumeIsolated {
                if height > 0, abs(height - state.cardHeight) > 0.5 { state.cardHeight = height }
            }
        }
    }

    private func arcButton(
        _ button: EdgeLayout.ArcButton, symbol: String, isHovered: Bool, spins: Int,
        layout: EdgeLayout, revealed: Bool, order: Int
    ) -> some View {
        let frame = layout.arcFrame(button)
        return ArcButtonView(isHovered: isHovered, trim: layout.arcTrim(button), symbol: symbol, spins: spins)
            .frame(width: frame.width, height: frame.height)
            .scaleEffect(revealed ? 1 : 0.3)
            .opacity(revealed ? 1 : 0)
            .offset(layout.offset(for: frame.origin, pushedOut: revealed ? 0 : 20))
            .animation(revealed ? Motion.stagger(index: order) : Motion.fold, value: revealed)
    }

    /// One shape for both states: the thin edge tab grows into the full pill, so it unfolds rather than appears.
    private func pill(layout: EdgeLayout, revealed: Bool) -> some View {
        let rect = revealed ? layout.pillRect : layout.tabRect
        let shape = EdgePillShape(
            edge: layout.edge,
            flare: revealed ? EdgeLayout.flare : 0,
            cornerRadius: revealed ? EdgeLayout.pillCorner : EdgeLayout.tabDepth
        )
        return GlassBackground(shape: shape)
            // A light wash keeps the folded tab findable on dark wallpapers.
            .overlay(shape.fill(Color.white.opacity(revealed ? 0 : 0.22)))
            .frame(width: rect.width, height: rect.height)
            .offset(x: rect.minX, y: rect.minY)
            .animation(Motion.unfold, value: revealed)
    }

    private func card(for metric: StatMetric, index: Int, layout: EdgeLayout) -> some View {
        let frame = layout.cardFrame(forCell: index, measured: state.cardHeight)
        let shape = CardShape(edge: layout.edge, tailCenter: layout.tailCenter(forCell: index, in: frame))
        return ZStack(alignment: .topLeading) {
            // Crossfade the contents while the card itself glides to the next ring.
            DetailCard(metric: metric, store: store, isDetailsHovered: state.isDetailsHovered)
                .id(metric)
                .transition(.opacity)
        }
        .animation(Motion.crossfade, value: metric)
        .padding(layout.edge.tailPadding, EdgeLayout.tailLength)
        .fixedSize(horizontal: false, vertical: true)
        .background(GlassBackground(shape: shape, extraDim: 0.07))
        .background(GeometryReader { proxy in
            Color.clear.preference(key: CardHeightKey.self, value: proxy.size.height)
        })
        .offset(x: frame.minX, y: frame.minY)
    }
}

extension ScreenEdge {
    /// The side of a view that faces the screen edge.
    var anchor: UnitPoint {
        switch self {
        case .right: .trailing
        case .left: .leading
        case .top: .top
        case .bottom: .bottom
        }
    }

    /// Side of the card that carries the tail, i.e. the one facing the pill.
    var tailPadding: Edge.Set {
        switch self {
        case .right: .trailing
        case .left: .leading
        case .top: .top
        case .bottom: .bottom
        }
    }
}

extension EdgeLayout {
    /// Offset that places a view at `origin`, pushed `distance` out through the screen edge.
    func offset(for origin: CGPoint, pushedOut distance: CGFloat) -> CGSize {
        CGSize(width: origin.x + outward.dx * distance, height: origin.y + outward.dy * distance)
    }
}

/// Springs throughout, damped just short of bouncy, so everything settles with one soft overshoot.
enum Motion {
    static let unfold = Animation.spring(response: 0.42, dampingFraction: 0.78)
    static let fold = Animation.spring(response: 0.3, dampingFraction: 0.9)
    static let contents = Animation.spring(response: 0.36, dampingFraction: 0.82)
    static let glide = Animation.spring(response: 0.5, dampingFraction: 0.86)
    static let crossfade = Animation.easeInOut(duration: 0.16)

    /// Each ring trails the one above it so the stack unfurls; capped so long stacks stay quick.
    static func stagger(index: Int) -> Animation {
        contents.delay(0.06 + min(Double(index) * 0.045, 0.18))
    }
}

/// At rest a thin glass arc inside a flare's pocket; on hover the arc is pushed into the pill
/// and a disc with a glyph takes its place. The settings gear spins once when clicked.
private struct ArcButtonView: View {
    var isHovered: Bool
    var trim: ClosedRange<CGFloat>
    var symbol: String
    var spins: Int

    var body: some View {
        let radius = EdgeLayout.orbArcRadius
        let band = ArcBand(trim: trim, lineWidth: EdgeLayout.orbStroke)
        ZStack {
            GlassBackground(shape: band)
                // Same light wash as the folded tab, so the thin arc reads on dark wallpapers.
                .overlay(band.fill(Color.white.opacity(0.2)))
                .frame(width: radius * 2 + EdgeLayout.orbStroke, height: radius * 2 + EdgeLayout.orbStroke)
                .scaleEffect(isHovered ? EdgeLayout.orbMergeScale : 1)
                .opacity(isHovered ? 0 : 1)

            GlassBackground(shape: Circle())
                .frame(width: EdgeLayout.orbDiameter, height: EdgeLayout.orbDiameter)
                .opacity(isHovered ? 1 : 0)
                .scaleEffect(isHovered ? 1 : 0.6)

            Image(systemName: symbol)
                .font(.system(size: 15))
                .foregroundStyle(.white)
                .opacity(isHovered ? 1 : 0)
                .scaleEffect(isHovered ? 1 : 0.5)
                .rotationEffect(.degrees((isHovered ? 0 : -60) + Double(spins) * 360))
                .animation(.spring(response: 0.55, dampingFraction: 0.72), value: spins)
        }
        .animation(.spring(response: 0.36, dampingFraction: 0.7), value: isHovered)
        // A quick squeeze and bounce on click, so the press is felt before Settings appears.
        .keyframeAnimator(initialValue: CGFloat(1), trigger: spins) { view, scale in
            view.scaleEffect(scale)
        } keyframes: { _ in
            SpringKeyframe(0.84, duration: 0.09, spring: .snappy)
            SpringKeyframe(1, duration: 0.34, spring: .bouncy)
        }
    }
}

/// A stroked slice of a circle, as a fillable shape so glass can be clipped to it.
private struct ArcBand: Shape {
    let trim: ClosedRange<CGFloat>
    let lineWidth: CGFloat

    func path(in rect: CGRect) -> Path {
        Circle()
            .trim(from: trim.lowerBound, to: trim.upperBound)
            .path(in: rect.insetBy(dx: lineWidth / 2, dy: lineWidth / 2))
            .strokedPath(StrokeStyle(lineWidth: lineWidth, lineCap: .round))
    }
}

private struct CardHeightKey: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = max(value, nextValue())
    }
}

private struct MetricCell: View {
    var metric: StatMetric
    var store: StatsStore
    var isHovered: Bool

    var body: some View {
        VStack(spacing: 5) {
            ZStack {
                RingGauge(fraction: fraction, color: color, lineWidth: 3.5)
                Image(systemName: symbol)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(.white)
            }
            .frame(width: 34, height: 34)
            Text(value)
                .font(.system(size: 11, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(.white)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .padding(.horizontal, 4)
        .scaleEffect(isHovered ? 1.08 : 1)
        .animation(.spring(response: 0.3, dampingFraction: 0.7), value: isHovered)
    }

    private var symbol: String {
        if metric == .battery, store.battery?.isCharging == true { return "bolt.fill" }
        return metric.symbol
    }

    private var fraction: Double {
        switch metric {
        case .cpu: store.cpu.total
        case .memory: store.memory.fraction
        // Relative to the recent peak, floored so an idle link does not look saturated.
        case .network: store.network.total / max(store.networkPeak, 250_000)
        case .disk: store.disk?.usedFraction ?? 0
        case .thermals: (store.thermals?.headline ?? 0) / 110
        case .battery: Double(store.battery?.percent ?? 0) / 100
        }
    }

    private var color: Color {
        switch metric {
        case .cpu: Level.color(for: store.cpu.total)
        case .memory: Level.color(for: store.memory.pressure)
        case .network: Level.accent
        case .disk: Level.color(for: store.disk?.usedFraction ?? 0)
        case .thermals: store.thermals?.headline.map(Level.temperatureColor) ?? .gray
        case .battery: store.battery.map(Level.batteryColor) ?? .gray
        }
    }

    private var value: String {
        switch metric {
        case .cpu: Format.percent(store.cpu.total)
        case .memory: Format.percent(store.memory.fraction)
        case .network: Format.shortRate(store.network.total)
        case .disk: store.disk.map { Format.percent($0.usedFraction) } ?? "–"
        case .thermals: store.thermals?.headline.map { String(format: "%.0f°", $0) } ?? "–"
        case .battery: store.battery.map { "\($0.percent)%" } ?? "–"
        }
    }
}
