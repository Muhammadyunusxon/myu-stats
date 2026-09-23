import AppKit
import SwiftUI

@MainActor
final class DropZoneState: ObservableObject {
    @Published var edge: ScreenEdge = .right
    /// True while the cursor is heading for this zone, so releasing moves the pill there.
    @Published var isArmed = false
    @Published var isVisible = false
}

/// Slots shown on the other screen edges while the move handle is dragged.
/// The one the cursor is heading for lights up; releasing there moves the pill to that edge.
@MainActor
final class DropZoneOverlay {
    private static let length: CGFloat = 180
    private static let depth: CGFloat = 64

    private var zones: [ScreenEdge: (panel: EdgePanel, state: DropZoneState)] = [:]

    func show(edges: [ScreenEdge], armed: ScreenEdge?, cursor: NSPoint, screen: NSScreen) {
        for edge in ScreenEdge.allCases where !edges.contains(edge) {
            hide(edge)
        }
        for edge in edges {
            let zone = zone(for: edge)
            zone.panel.setFrame(frame(for: edge, cursor: cursor, screen: screen).integral, display: true)
            if !zone.panel.isVisible { zone.panel.orderFrontRegardless() }
            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) {
                zone.state.isVisible = true
                zone.state.isArmed = edge == armed
            }
        }
    }

    func hide() {
        ScreenEdge.allCases.forEach(hide)
    }

    private func hide(_ edge: ScreenEdge) {
        guard let zone = zones[edge], zone.state.isVisible else { return }
        withAnimation(.easeOut(duration: 0.18)) {
            zone.state.isVisible = false
            zone.state.isArmed = false
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            if !zone.state.isVisible { zone.panel.orderOut(nil) }
        }
    }

    private func zone(for edge: ScreenEdge) -> (panel: EdgePanel, state: DropZoneState) {
        if let existing = zones[edge] { return existing }
        let panel = EdgePanel()
        let state = DropZoneState()
        state.edge = edge
        let host = NSHostingView(rootView: DropZoneView(state: state))
        host.sizingOptions = []
        panel.contentView = host
        zones[edge] = (panel, state)
        return (panel, state)
    }

    /// Follows the cursor along each edge, clamped on screen; the top slot sits under the menu bar.
    private func frame(for edge: ScreenEdge, cursor: NSPoint, screen: NSScreen) -> NSRect {
        let full = screen.frame
        let top = screen.visibleFrame.maxY
        let length = Self.length
        let depth = Self.depth
        switch edge {
        case .left, .right:
            let y = min(max(cursor.y - length / 2, full.minY), top - length)
            let x = edge == .right ? full.maxX - depth : full.minX
            return NSRect(x: x, y: y, width: depth, height: length)
        case .top, .bottom:
            let x = min(max(cursor.x - length / 2, full.minX), full.maxX - length)
            let y = edge == .top ? top - depth : full.minY
            return NSRect(x: x, y: y, width: length, height: depth)
        }
    }
}

private struct DropZoneView: View {
    @ObservedObject var state: DropZoneState

    private var arrow: String {
        switch state.edge {
        case .right: "arrow.right.to.line"
        case .left: "arrow.left.to.line"
        case .top: "arrow.up.to.line"
        case .bottom: "arrow.down.to.line"
        }
    }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 22, style: .continuous)
        ZStack {
            shape.fill(Color.white.opacity(state.isArmed ? 0.16 : 0.05))
            shape.strokeBorder(
                state.isArmed ? Level.accent : Color.white.opacity(0.45),
                style: StrokeStyle(lineWidth: state.isArmed ? 2 : 1.5, dash: state.isArmed ? [] : [6, 5])
            )
            Image(systemName: arrow)
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(state.isArmed ? Level.accent : Color.white.opacity(0.6))
                .scaleEffect(state.isArmed ? 1.15 : 1)
        }
        .padding(6)
        .shadow(color: state.isArmed ? Level.accent.opacity(0.5) : .clear, radius: 10)
        .scaleEffect(state.isVisible ? (state.isArmed ? 1.04 : 1) : 0.85)
        .opacity(state.isVisible ? 1 : 0)
    }
}
