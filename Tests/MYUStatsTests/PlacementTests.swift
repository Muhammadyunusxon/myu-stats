import CoreGraphics
import Testing
@testable import MYUStats

@Suite("Placement on screen")
struct PlacementTests {
    /// A 14" MacBook Pro in points, with a 37 pt menu bar.
    static let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
    static let usableTop: CGFloat = 945

    private func frame(_ edge: ScreenEdge, offset: CGFloat = 0) -> (EdgeLayout, CGRect) {
        let layout = EdgeLayout(edge: edge, cellCount: 6)
        return (layout, layout.panelFrame(offset: offset, screen: Self.screen, usableTop: Self.usableTop))
    }

    /// The pill in screen coordinates (y up).
    private func pillOnScreen(_ layout: EdgeLayout, in frame: CGRect) -> CGRect {
        let pill = layout.pillRect
        return CGRect(x: frame.minX + pill.minX, y: frame.maxY - pill.maxY, width: pill.width, height: pill.height)
    }

    @Test("each edge anchors to its side of the screen", arguments: ScreenEdge.allCases)
    func anchoring(edge: ScreenEdge) {
        let (_, frame) = frame(edge)
        switch edge {
        case .right: #expect(frame.maxX == Self.screen.maxX)
        case .left: #expect(frame.minX == Self.screen.minX)
        case .top: #expect(frame.maxY == Self.usableTop)
        case .bottom: #expect(frame.minY == Self.screen.minY)
        }
    }

    @Test("top edge hangs under the menu bar, never over it")
    func topUnderMenuBar() {
        let (layout, frame) = frame(.top)
        #expect(pillOnScreen(layout, in: frame).maxY <= Self.usableTop)
    }

    @Test("zero offset centres the panel along the edge", arguments: ScreenEdge.allCases)
    func centred(edge: ScreenEdge) {
        let (_, frame) = frame(edge)
        if edge.isVertical {
            #expect(abs(frame.midY - Self.screen.midY) < 0.001)
        } else {
            #expect(abs(frame.midX - Self.screen.midX) < 0.001)
        }
    }

    @Test("extreme offsets are clamped so the pill stays on screen",
          arguments: ScreenEdge.allCases, [CGFloat(-5000), 5000])
    func clamped(edge: ScreenEdge, offset: CGFloat) {
        let (layout, frame) = frame(edge, offset: offset)
        let pill = pillOnScreen(layout, in: frame)
        if edge.isVertical {
            #expect(pill.minY >= Self.screen.minY - 0.001)
            #expect(pill.maxY <= Self.usableTop + 0.001)
        } else {
            #expect(pill.minX >= Self.screen.minX - 0.001)
            #expect(pill.maxX <= Self.screen.maxX + 0.001)
        }
    }

    @Test("a stored offset round-trips through the frame", arguments: ScreenEdge.allCases)
    func offsetRoundTrip(edge: ScreenEdge) {
        let (_, frame) = frame(edge, offset: 60)
        #expect(abs(EdgeLayout.offset(of: frame, screen: Self.screen, vertical: edge.isVertical) - 60) < 0.001)
    }

    // MARK: - Drop targets

    @Test("dragging toward another edge targets it")
    func dropTargets() {
        let screen = Self.screen
        let top = Self.usableTop
        #expect(EdgeLayout.dropTarget(for: CGPoint(x: 1490, y: 491), screen: screen, usableTop: top, current: .left) == .right)
        #expect(EdgeLayout.dropTarget(for: CGPoint(x: 20, y: 491), screen: screen, usableTop: top, current: .right) == .left)
        #expect(EdgeLayout.dropTarget(for: CGPoint(x: 756, y: 930), screen: screen, usableTop: top, current: .left) == .top)
        #expect(EdgeLayout.dropTarget(for: CGPoint(x: 756, y: 10), screen: screen, usableTop: top, current: .left) == .bottom)
    }

    @Test("no drop target in the middle of the screen or near the current edge")
    func noDropTarget() {
        let screen = Self.screen
        let top = Self.usableTop
        #expect(EdgeLayout.dropTarget(for: CGPoint(x: 756, y: 491), screen: screen, usableTop: top, current: .left) == nil)
        #expect(EdgeLayout.dropTarget(for: CGPoint(x: 10, y: 491), screen: screen, usableTop: top, current: .left) == nil)
    }

    @Test("corners resolve to the edge the cursor is relatively closer to")
    func cornerBias() {
        // 30 pt from the right (band 378) is closer, relatively, than 60 pt from the top (band 245.5).
        let target = EdgeLayout.dropTarget(
            for: CGPoint(x: Self.screen.maxX - 30, y: Self.usableTop - 60),
            screen: Self.screen, usableTop: Self.usableTop, current: .left
        )
        #expect(target == .right)
    }
}
