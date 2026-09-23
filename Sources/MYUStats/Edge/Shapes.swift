import SwiftUI

/// Pill welded to a screen edge; its ends flare back out so it reads as part of the bezel.
/// Drawn for the right edge and mirrored for the left.
struct EdgePillShape: Shape {
    var edge: ScreenEdge
    var flare: CGFloat = EdgeLayout.flare
    var cornerRadius: CGFloat = EdgeLayout.pillCorner

    var animatableData: AnimatablePair<CGFloat, CGFloat> {
        get { AnimatablePair(flare, cornerRadius) }
        set { (flare, cornerRadius) = (newValue.first, newValue.second) }
    }

    func path(in rect: CGRect) -> Path {
        let canonical = EdgeTransform.canonicalSize(rect.size, edge: edge)
        let width = canonical.width
        let height = canonical.height
        let curl = min(flare, height / 2, width / 2)
        let corner = max(0, min(cornerRadius, width - curl, (height - 2 * curl) / 2))
        let top = curl
        let bottom = height - curl

        var path = Path()
        path.move(to: CGPoint(x: width, y: 0))
        path.addArc(center: CGPoint(x: width - curl, y: 0), radius: curl,
                    startAngle: .degrees(0), endAngle: .degrees(90), clockwise: false)
        path.addLine(to: CGPoint(x: corner, y: top))
        path.addArc(center: CGPoint(x: corner, y: top + corner), radius: corner,
                    startAngle: .degrees(270), endAngle: .degrees(180), clockwise: true)
        path.addLine(to: CGPoint(x: 0, y: bottom - corner))
        path.addArc(center: CGPoint(x: corner, y: bottom - corner), radius: corner,
                    startAngle: .degrees(180), endAngle: .degrees(90), clockwise: true)
        path.addLine(to: CGPoint(x: width - curl, y: bottom))
        path.addArc(center: CGPoint(x: width - curl, y: height), radius: curl,
                    startAngle: .degrees(270), endAngle: .degrees(360), clockwise: false)
        path.closeSubpath()

        return path
            .applying(EdgeTransform.fromCanonical(edge: edge, size: rect.size))
            .offsetBy(dx: rect.minX, dy: rect.minY)
    }
}

/// Maps shapes drawn for the right edge (x across toward the bezel, y along it) onto any edge.
enum EdgeTransform {
    static func canonicalSize(_ size: CGSize, edge: ScreenEdge) -> CGSize {
        edge.isVertical ? size : CGSize(width: size.height, height: size.width)
    }

    static func fromCanonical(edge: ScreenEdge, size: CGSize) -> CGAffineTransform {
        switch edge {
        case .right: .identity
        case .left: CGAffineTransform(a: -1, b: 0, c: 0, d: 1, tx: size.width, ty: 0)
        case .top: CGAffineTransform(a: 0, b: -1, c: 1, d: 0, tx: 0, ty: size.height)
        case .bottom: CGAffineTransform(a: 0, b: 1, c: 1, d: 0, tx: 0, ty: 0)
        }
    }
}

/// Rounded card with a tail pointing at the pill. Drawn with the tail on the right, then mapped onto the edge.
struct CardShape: Shape {
    var edge: ScreenEdge
    /// Where the tail meets the card, measured along the edge from the card's start.
    var tailCenter: CGFloat
    var tailLength: CGFloat = EdgeLayout.tailLength
    var tailHalfHeight: CGFloat = EdgeLayout.tailHalfHeight
    var cornerRadius: CGFloat = EdgeLayout.cardCorner

    var animatableData: CGFloat {
        get { tailCenter }
        set { tailCenter = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let canonical = EdgeTransform.canonicalSize(rect.size, edge: edge)
        let body = canonical.width - tailLength
        let height = canonical.height
        let radius = min(cornerRadius, body / 2, height / 2)
        let tailY = min(max(tailCenter, radius + tailHalfHeight), height - radius - tailHalfHeight)

        var path = Path()
        path.move(to: CGPoint(x: radius, y: 0))
        path.addArc(tangent1End: CGPoint(x: body, y: 0), tangent2End: CGPoint(x: body, y: height), radius: radius)
        path.addLine(to: CGPoint(x: body, y: tailY - tailHalfHeight))
        path.addQuadCurve(
            to: CGPoint(x: canonical.width, y: tailY),
            control: CGPoint(x: body + tailLength * 0.15, y: tailY - tailHalfHeight * 0.1)
        )
        path.addQuadCurve(
            to: CGPoint(x: body, y: tailY + tailHalfHeight),
            control: CGPoint(x: body + tailLength * 0.15, y: tailY + tailHalfHeight * 0.1)
        )
        path.addArc(tangent1End: CGPoint(x: body, y: height), tangent2End: CGPoint(x: 0, y: height), radius: radius)
        path.addArc(tangent1End: CGPoint(x: 0, y: height), tangent2End: CGPoint(x: 0, y: 0), radius: radius)
        path.addArc(tangent1End: CGPoint(x: 0, y: 0), tangent2End: CGPoint(x: body, y: 0), radius: radius)
        path.closeSubpath()

        return path
            .applying(EdgeTransform.fromCanonical(edge: edge, size: rect.size))
            .offsetBy(dx: rect.minX, dy: rect.minY)
    }
}
