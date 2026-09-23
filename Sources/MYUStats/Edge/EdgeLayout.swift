import CoreGraphics

/// Geometry of the pill, its arc buttons and the detail card, in panel-local points (origin top-left, y down).
///
/// Everything is worked out in edge-relative terms — *along* the edge and *across* it, measured
/// from the screen edge inward — and mapped to panel coordinates once, in `rect(along:…)`.
/// That keeps one set of rules for all four edges.
struct EdgeLayout: Equatable {
    static let verticalDepth: CGFloat = 54
    static let horizontalDepth: CGFloat = 60
    static let verticalCellLength: CGFloat = 74
    static let horizontalCellLength: CGFloat = 66
    static let bodyPadding: CGFloat = 6
    static let flare: CGFloat = 30
    static let pillCorner: CGFloat = 22
    static let cardWidth: CGFloat = 248
    static let cardCorner: CGFloat = 16
    static let tailLength: CGFloat = 20
    static let tailHalfHeight: CGFloat = 20
    static let cardGap: CGFloat = 8
    static let minPanelLength: CGFloat = 520
    /// Room across a top or bottom edge for the tallest card.
    static let maxCardDepth: CGFloat = 470
    /// The folded pill: a thin tab on the edge.
    static let tabLength: CGFloat = 60
    static let tabDepth: CGFloat = 5

    // Arc buttons nestle in the pockets of the two flares, concentric with them:
    // the move handle at the start of the pill, the settings orb at the end.
    static let orbStroke: CGFloat = 5
    static let orbArcGap: CGFloat = 9
    static let orbDiameter: CGFloat = 36
    static var orbArcRadius: CGFloat { flare - orbArcGap }
    /// Grows the arc a stroke past the flare so it is buried in the pill while the disc shows.
    static var orbMergeScale: CGFloat { (flare + orbStroke) / orbArcRadius }
    /// Space kept at both ends of the pill so the discs fit and the pill stays centred.
    static let orbMargin: CGFloat = 30

    enum ArcButton { case move, settings }

    var edge: ScreenEdge
    var cellCount: Int

    // MARK: - Sizes

    var depth: CGFloat { edge.isVertical ? Self.verticalDepth : Self.horizontalDepth }
    var cellLength: CGFloat { edge.isVertical ? Self.verticalCellLength : Self.horizontalCellLength }

    var shapeLength: CGFloat {
        CGFloat(cellCount) * cellLength + 2 * Self.bodyPadding + 2 * Self.flare
    }

    var panelLength: CGFloat {
        let fitted = shapeLength + 2 * Self.orbMargin
        return edge.isVertical ? max(fitted, Self.minPanelLength) : max(fitted, Self.cardWidth + 16)
    }

    var panelDepth: CGFloat {
        edge.isVertical
            ? Self.cardWidth + Self.tailLength + Self.cardGap + depth
            : depth + Self.cardGap + Self.maxCardDepth
    }

    var panelSize: CGSize {
        edge.isVertical
            ? CGSize(width: panelDepth, height: panelLength)
            : CGSize(width: panelLength, height: panelDepth)
    }

    var shapeStart: CGFloat { (panelLength - shapeLength) / 2 }
    var shapeEnd: CGFloat { shapeStart + shapeLength }

    // MARK: - Edge mapping

    /// Panel rect for a box `length` long along the edge and `depth` deep, starting `across` in from it.
    func rect(along: CGFloat, length: CGFloat, across: CGFloat, depth: CGFloat) -> CGRect {
        let size = panelSize
        switch edge {
        case .right: return CGRect(x: size.width - across - depth, y: along, width: depth, height: length)
        case .left: return CGRect(x: across, y: along, width: depth, height: length)
        case .top: return CGRect(x: along, y: across, width: length, height: depth)
        case .bottom: return CGRect(x: along, y: size.height - across - depth, width: length, height: depth)
        }
    }

    func point(along: CGFloat, across: CGFloat) -> CGPoint {
        rect(along: along, length: 0, across: across, depth: 0).origin
    }

    /// Inverse of `rect`: a panel point as (along, across).
    func edgeCoordinates(of point: CGPoint) -> (along: CGFloat, across: CGFloat) {
        let size = panelSize
        switch edge {
        case .right: return (point.y, size.width - point.x)
        case .left: return (point.y, point.x)
        case .top: return (point.x, point.y)
        case .bottom: return (point.x, size.height - point.y)
        }
    }

    /// Unit vector pointing out through the screen edge.
    var outward: CGVector {
        switch edge {
        case .right: CGVector(dx: 1, dy: 0)
        case .left: CGVector(dx: -1, dy: 0)
        case .top: CGVector(dx: 0, dy: -1)
        case .bottom: CGVector(dx: 0, dy: 1)
        }
    }

    // MARK: - Pill

    var pillRect: CGRect { rect(along: shapeStart, length: shapeLength, across: 0, depth: depth) }

    var tabRect: CGRect {
        rect(along: shapeStart + shapeLength / 2 - Self.tabLength / 2, length: Self.tabLength,
             across: 0, depth: Self.tabDepth)
    }

    func cellCenter(_ index: Int) -> CGFloat {
        shapeStart + Self.flare + Self.bodyPadding + (CGFloat(index) + 0.5) * cellLength
    }

    func cellRect(_ index: Int) -> CGRect {
        rect(along: cellCenter(index) - cellLength / 2, length: cellLength, across: 0, depth: depth)
    }

    /// Touching the screen edge anywhere along the pill's span reveals it. On the top edge the
    /// pill hangs under the menu bar, so the menu bar itself counts as the edge.
    func isRevealTrigger(_ point: CGPoint) -> Bool {
        let position = edgeCoordinates(of: point)
        return position.across <= 3 && position.along >= shapeStart && position.along <= shapeEnd
    }

    // MARK: - Card

    /// Card frame including its tail, centred on the cell and kept inside the panel.
    /// `measured` is the card view's height as laid out (tail included on the top and bottom edges).
    func cardFrame(forCell index: Int, measured: CGFloat) -> CGRect {
        let across = depth + Self.cardGap
        if edge.isVertical {
            let start = min(max(cellCenter(index) - measured / 2, 4), panelLength - measured - 4)
            return rect(along: start, length: measured, across: across, depth: Self.cardWidth + Self.tailLength)
        }
        let start = min(max(cellCenter(index) - Self.cardWidth / 2, 4), panelLength - Self.cardWidth - 4)
        return rect(along: start, length: Self.cardWidth, across: across, depth: measured)
    }

    static let cardPadding: CGFloat = 14
    static let detailsButtonHeight: CGFloat = 26

    /// The card without its tail.
    func cardBody(_ card: CGRect) -> CGRect {
        let tail = Self.tailLength
        switch edge {
        case .right: return CGRect(x: card.minX, y: card.minY, width: card.width - tail, height: card.height)
        case .left: return CGRect(x: card.minX + tail, y: card.minY, width: card.width - tail, height: card.height)
        case .top: return CGRect(x: card.minX, y: card.minY + tail, width: card.width, height: card.height - tail)
        case .bottom: return CGRect(x: card.minX, y: card.minY, width: card.width, height: card.height - tail)
        }
    }

    /// Hit area of the "Details" row at the foot of the card (with a little slack around it).
    func detailsButtonRect(in card: CGRect) -> CGRect {
        let body = cardBody(card)
        let height = Self.detailsButtonHeight
        return CGRect(
            x: body.minX + Self.cardPadding,
            y: body.maxY - Self.cardPadding - height,
            width: body.width - 2 * Self.cardPadding,
            height: height
        ).insetBy(dx: -2, dy: -3)
    }

    /// Where the tail meets the card, measured along the card from its start.
    func tailCenter(forCell index: Int, in frame: CGRect) -> CGFloat {
        cellCenter(index) - (edge.isVertical ? frame.minY : frame.minX)
    }

    // MARK: - Arc buttons

    /// Centre shared by a flare, its resting arc and the hover disc.
    func arcCenter(_ button: ArcButton) -> CGPoint {
        point(along: button == .settings ? shapeEnd : shapeStart, across: Self.flare)
    }

    func arcFrame(_ button: ArcButton) -> CGRect {
        let side = max(Self.orbArcRadius * 2 + Self.orbStroke, Self.orbDiameter)
        let center = arcCenter(button)
        return CGRect(x: center.x - side / 2, y: center.y - side / 2, width: side, height: side)
    }

    /// Quarter of the circle between "toward the pill body" and "toward the screen edge",
    /// as a `Circle().trim` range (0 = +x, 0.25 = +y, 0.5 = −x, 0.75 = −y).
    func arcTrim(_ button: ArcButton) -> ClosedRange<CGFloat> {
        let bezel: CGFloat
        switch edge {
        case .right: bezel = 0
        case .bottom: bezel = 0.25
        case .left: bezel = 0.5
        case .top: bezel = 0.75
        }
        // The settings orb sits at the far end, so the body lies back along the edge.
        let alongForward: CGFloat = edge.isVertical ? 0.25 : 0
        let body = button == .settings ? (alongForward + 0.5).truncatingRemainder(dividingBy: 1) : alongForward
        let low = min(bezel, body)
        let high = max(bezel, body)
        return high - low > 0.5 ? high...1.0 : low...high
    }

    /// The whole pocket inside the flare counts, so the small target is easy to hit on an edge.
    func isOn(_ button: ArcButton, _ point: CGPoint) -> Bool {
        let center = arcCenter(button)
        let dx = point.x - center.x
        let dy = point.y - center.y
        return (dx * dx + dy * dy).squareRoot() <= Self.flare
    }
}

// MARK: - Placement on screen

extension EdgeLayout {
    /// Panel frame in screen coordinates (y up): centred on the edge, nudged by `offset` along it,
    /// and clamped so the pill itself stays on screen. The top edge hangs under the menu bar
    /// (`usableTop`) so it never covers the menu bar or the camera notch.
    func panelFrame(offset: CGFloat, screen: CGRect, usableTop: CGFloat) -> CGRect {
        let size = panelSize
        // Space between the panel's ends and the pill's ends, which may run off screen.
        let startInset = shapeStart - Self.orbMargin
        let endInset = panelLength - shapeEnd - Self.orbMargin

        switch edge {
        case .left, .right:
            let x = edge == .right ? screen.maxX - size.width : screen.minX
            // Screen y grows upward while "along" grows downward, so the start inset sits at the top.
            let y = min(max(screen.midY - size.height / 2 + offset, screen.minY - endInset),
                        usableTop - size.height + startInset)
            return CGRect(x: x, y: y, width: size.width, height: size.height)
        case .top, .bottom:
            let x = min(max(screen.midX - size.width / 2 + offset, screen.minX - startInset),
                        screen.maxX - size.width + endInset)
            let y = edge == .top ? usableTop - size.height : screen.minY
            return CGRect(x: x, y: y, width: size.width, height: size.height)
        }
    }

    /// The offset that would centre an unclamped panel where `frame` actually is.
    static func offset(of frame: CGRect, screen: CGRect, vertical: Bool) -> CGFloat {
        vertical
            ? frame.minY - (screen.midY - frame.height / 2)
            : frame.minX - (screen.midX - frame.width / 2)
    }

    /// The edge a dragged pill is heading for: the nearest edge other than `current`,
    /// once the cursor is inside the outer quarter of the screen on that side.
    static func dropTarget(for mouse: CGPoint, screen: CGRect, usableTop: CGFloat, current: ScreenEdge) -> ScreenEdge? {
        let candidates: [(edge: ScreenEdge, distance: CGFloat, band: CGFloat)] = [
            (.left, mouse.x - screen.minX, screen.width / 4),
            (.right, screen.maxX - mouse.x, screen.width / 4),
            (.top, usableTop - mouse.y, screen.height / 4),
            (.bottom, mouse.y - screen.minY, screen.height / 4),
        ]
        // Compare relative to each band, so a wide screen does not favour the top and bottom.
        guard let nearest = candidates.min(by: { $0.distance / $0.band < $1.distance / $1.band }),
              nearest.edge != current, nearest.distance < nearest.band
        else { return nil }
        return nearest.edge
    }
}
