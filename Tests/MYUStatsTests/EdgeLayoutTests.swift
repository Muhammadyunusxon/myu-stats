import CoreGraphics
import Foundation
import Testing
@testable import MYUStats

@Suite("EdgeLayout geometry")
struct EdgeLayoutTests {
    static let edges = ScreenEdge.allCases

    private func layout(_ edge: ScreenEdge, cells: Int = 6) -> EdgeLayout {
        EdgeLayout(edge: edge, cellCount: cells)
    }

    private func bounds(_ layout: EdgeLayout) -> CGRect {
        CGRect(origin: .zero, size: layout.panelSize)
    }

    // MARK: - Mapping

    @Test("along/across mapping round-trips", arguments: edges)
    func mappingRoundTrip(edge: ScreenEdge) {
        let layout = layout(edge)
        for (along, across) in [(0.0, 0.0), (40.0, 12.0), (layout.panelLength - 1, layout.panelDepth - 1)] {
            let point = layout.point(along: along, across: across)
            let back = layout.edgeCoordinates(of: point)
            #expect(abs(back.along - along) < 0.001)
            #expect(abs(back.across - across) < 0.001)
        }
    }

    @Test("panel is long along the edge and deep across it", arguments: edges)
    func panelOrientation(edge: ScreenEdge) {
        let layout = layout(edge)
        let size = layout.panelSize
        if edge.isVertical {
            #expect(size.height == layout.panelLength)
            #expect(size.width == layout.panelDepth)
        } else {
            #expect(size.width == layout.panelLength)
            #expect(size.height == layout.panelDepth)
        }
    }

    // MARK: - Pill

    @Test("pill is welded to the screen edge", arguments: edges)
    func pillTouchesEdge(edge: ScreenEdge) {
        let layout = layout(edge)
        let pill = layout.pillRect
        let size = layout.panelSize
        switch edge {
        case .right: #expect(pill.maxX == size.width)
        case .left: #expect(pill.minX == 0)
        case .top: #expect(pill.minY == 0)
        case .bottom: #expect(pill.maxY == size.height)
        }
        #expect(bounds(layout).contains(pill))
    }

    @Test("pill is centred along the panel", arguments: edges)
    func pillCentred(edge: ScreenEdge) {
        let layout = layout(edge)
        #expect(abs(layout.shapeStart - (layout.panelLength - layout.shapeEnd)) < 0.001)
    }

    @Test("cells sit inside the pill body, in order, without overlapping", arguments: edges)
    func cellsInsideBody(edge: ScreenEdge) {
        let layout = layout(edge)
        var previousEnd = layout.shapeStart + EdgeLayout.flare
        for index in 0..<layout.cellCount {
            let cell = layout.cellRect(index)
            #expect(layout.pillRect.contains(cell))
            let start = layout.cellCenter(index) - layout.cellLength / 2
            #expect(start >= previousEnd - 0.001)
            previousEnd = start + layout.cellLength
        }
        #expect(previousEnd <= layout.shapeEnd - EdgeLayout.flare + 0.001)
    }

    @Test("folded tab sits on the edge, centred on the pill", arguments: edges)
    func tabOnEdge(edge: ScreenEdge) {
        let layout = layout(edge)
        let tab = layout.tabRect
        #expect(layout.pillRect.contains(tab))
        let tabCenter = layout.edgeCoordinates(of: CGPoint(x: tab.midX, y: tab.midY))
        let pillCenter = layout.edgeCoordinates(of: CGPoint(x: layout.pillRect.midX, y: layout.pillRect.midY))
        #expect(abs(tabCenter.along - pillCenter.along) < 0.001)
        #expect(tabCenter.across < EdgeLayout.tabDepth)
    }

    // MARK: - Reveal

    @Test("touching the edge along the pill reveals it", arguments: edges)
    func revealTrigger(edge: ScreenEdge) {
        let layout = layout(edge)
        let middle = (layout.shapeStart + layout.shapeEnd) / 2
        #expect(layout.isRevealTrigger(layout.point(along: middle, across: 0)))
        #expect(!layout.isRevealTrigger(layout.point(along: middle, across: 20)))
        #expect(!layout.isRevealTrigger(layout.point(along: layout.shapeStart - 10, across: 0)))
    }

    @Test("on the top edge the menu bar above the panel also reveals the pill")
    func revealFromMenuBar() {
        let layout = layout(.top)
        let middle = (layout.shapeStart + layout.shapeEnd) / 2
        // Negative y: above the panel, i.e. inside the menu bar.
        #expect(layout.isRevealTrigger(CGPoint(x: middle, y: -20)))
    }

    // MARK: - Card

    @Test("card stays inside the panel, clear of the pill, for every cell", arguments: edges)
    func cardPlacement(edge: ScreenEdge) {
        let layout = layout(edge)
        let tallest = edge.isVertical ? layout.panelLength - 8 : EdgeLayout.maxCardDepth
        for measured in [CGFloat(180), tallest] {
            for index in 0..<layout.cellCount {
                let card = layout.cardFrame(forCell: index, measured: measured)
                #expect(bounds(layout).insetBy(dx: -0.001, dy: -0.001).contains(card))
                #expect(!card.intersects(layout.pillRect))
                let across = layout.edgeCoordinates(of: CGPoint(x: card.midX, y: card.midY)).across
                #expect(across > layout.depth)
            }
        }
    }

    @Test("card tail points at its ring", arguments: edges)
    func tailAlignsWithRing(edge: ScreenEdge) {
        let layout = layout(edge)
        for index in 0..<layout.cellCount {
            let card = layout.cardFrame(forCell: index, measured: 200)
            let tail = layout.tailCenter(forCell: index, in: card)
            let cardLength = edge.isVertical ? card.height : card.width
            #expect(tail >= 0 && tail <= cardLength)
            let cardStart = edge.isVertical ? card.minY : card.minX
            #expect(abs(cardStart + tail - layout.cellCenter(index)) < 0.001)
        }
    }

    // MARK: - Arc buttons

    @Test("arc buttons sit in the flare pockets at both ends", arguments: edges)
    func arcCentres(edge: ScreenEdge) {
        let layout = layout(edge)
        let settings = layout.edgeCoordinates(of: layout.arcCenter(.settings))
        let move = layout.edgeCoordinates(of: layout.arcCenter(.move))
        #expect(abs(settings.along - layout.shapeEnd) < 0.001)
        #expect(abs(move.along - layout.shapeStart) < 0.001)
        #expect(abs(settings.across - EdgeLayout.flare) < 0.001)
        #expect(abs(move.across - EdgeLayout.flare) < 0.001)
    }

    @Test("hover discs are fully on screen", arguments: edges)
    func discsOnScreen(edge: ScreenEdge) {
        let layout = layout(edge)
        let radius = EdgeLayout.orbDiameter / 2
        for button in [EdgeLayout.ArcButton.move, .settings] {
            let center = layout.arcCenter(button)
            let disc = CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
            #expect(bounds(layout).contains(disc))
            // Not clipped by the screen edge the panel sits on.
            #expect(layout.edgeCoordinates(of: CGPoint(x: disc.midX, y: disc.midY)).across - radius >= 0)
        }
    }

    @Test("arc and disc hit areas do not overlap each other or the rings", arguments: edges)
    func arcHitAreas(edge: ScreenEdge) {
        let layout = layout(edge)
        let settings = layout.arcCenter(.settings)
        let move = layout.arcCenter(.move)
        #expect(layout.isOn(.settings, settings))
        #expect(layout.isOn(.move, move))
        #expect(!layout.isOn(.move, settings))
        #expect(!layout.isOn(.settings, move))
        for index in 0..<layout.cellCount {
            let cell = layout.cellRect(index)
            let center = CGPoint(x: cell.midX, y: cell.midY)
            #expect(!layout.isOn(.settings, center))
            #expect(!layout.isOn(.move, center))
        }
    }

    @Test("each resting arc is a quarter facing the edge and the pill body", arguments: edges)
    func arcTrimFacesPocket(edge: ScreenEdge) {
        let layout = layout(edge)
        let outward = layout.outward
        let alongForward = edge.isVertical ? CGVector(dx: 0, dy: 1) : CGVector(dx: 1, dy: 0)
        for button in [EdgeLayout.ArcButton.move, .settings] {
            let trim = layout.arcTrim(button)
            #expect(abs((trim.upperBound - trim.lowerBound) - 0.25) < 0.001)
            #expect(trim.lowerBound >= 0 && trim.upperBound <= 1)
            // Mid-direction of the arc, in y-down space (0 = +x, 0.25 = +y).
            let angle = (trim.lowerBound + trim.upperBound) / 2 * 2 * .pi
            let direction = CGVector(dx: cos(angle), dy: sin(angle))
            let towardBody = button == .settings
                ? CGVector(dx: -alongForward.dx, dy: -alongForward.dy)
                : alongForward
            #expect(direction.dx * outward.dx + direction.dy * outward.dy > 0.5)
            #expect(direction.dx * towardBody.dx + direction.dy * towardBody.dy > 0.5)
        }
    }

    @Test("resting arc stays inside the flare's pocket")
    func arcInsidePocket() {
        #expect(EdgeLayout.orbArcRadius + EdgeLayout.orbStroke / 2 < EdgeLayout.flare)
        #expect(EdgeLayout.orbDiameter / 2 < EdgeLayout.flare)
        #expect(EdgeLayout.orbMergeScale > 1)
    }
}

@Suite("Details button")
struct DetailsButtonTests {
    @Test("sits at the foot of the card body, never over the tail", arguments: ScreenEdge.allCases)
    func placement(edge: ScreenEdge) {
        let layout = EdgeLayout(edge: edge, cellCount: 6)
        for index in 0..<layout.cellCount {
            let card = layout.cardFrame(forCell: index, measured: 300)
            let body = layout.cardBody(card)
            let button = layout.detailsButtonRect(in: card)
            #expect(card.contains(body))
            // Slack of a few points around the row is allowed, but it stays within the card.
            #expect(card.insetBy(dx: -3, dy: -4).contains(button))
            #expect(button.maxY <= body.maxY)
            #expect(button.minY > body.midY)
            #expect(!button.intersects(layout.pillRect))
            for cell in 0..<layout.cellCount {
                #expect(!button.intersects(layout.cellRect(cell)))
            }
        }
    }
}
