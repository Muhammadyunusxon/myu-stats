// Renders the app icon into an .iconset folder: a dark squircle holding the edge pill with three rings.
// Usage: swift Scripts/make-icon.swift <output.iconset>
import AppKit

let canvas: CGFloat = 1024

func rgb(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: r, green: g, blue: b, alpha: a)
}

// Same palette as Level in Formatters.swift.
let green = rgb(0.36, 0.84, 0.52)
let amber = rgb(1.0, 0.76, 0.28)
let blue = rgb(0.42, 0.66, 1.0)

func drawIcon(in ctx: CGContext) {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!

    // macOS icon grid: 824 pt body centred on the 1024 canvas.
    let body = CGRect(x: 100, y: 100, width: 824, height: 824)
    let bodyPath = CGPath(roundedRect: body, cornerWidth: 185, cornerHeight: 185, transform: nil)

    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 28, color: rgb(0, 0, 0, 0.45))
    ctx.addPath(bodyPath)
    ctx.setFillColor(rgb(0.07, 0.07, 0.08))
    ctx.fillPath()
    ctx.restoreGState()

    ctx.saveGState()
    ctx.addPath(bodyPath)
    ctx.clip()
    let background = CGGradient(colorsSpace: space,
                                colors: [rgb(0.17, 0.17, 0.19), rgb(0.045, 0.045, 0.055)] as CFArray,
                                locations: [0, 1])!
    ctx.drawLinearGradient(background, start: CGPoint(x: 512, y: 924), end: CGPoint(x: 512, y: 100), options: [])

    // Soft blue glow behind the pill.
    let glow = CGGradient(colorsSpace: space,
                          colors: [rgb(0.42, 0.66, 1.0, 0.22), rgb(0.42, 0.66, 1.0, 0)] as CFArray,
                          locations: [0, 1])!
    ctx.drawRadialGradient(glow, startCenter: CGPoint(x: 512, y: 560), startRadius: 0,
                           endCenter: CGPoint(x: 512, y: 560), endRadius: 420, options: [])
    ctx.restoreGState()

    // Hairline rim on the body.
    ctx.addPath(CGPath(roundedRect: body.insetBy(dx: 1.5, dy: 1.5), cornerWidth: 183, cornerHeight: 183, transform: nil))
    ctx.setStrokeColor(rgb(1, 1, 1, 0.10))
    ctx.setLineWidth(3)
    ctx.strokePath()

    // Glass pill.
    let pill = CGRect(x: 512 - 124, y: 512 - 318, width: 248, height: 636)
    let pillPath = CGPath(roundedRect: pill, cornerWidth: 124, cornerHeight: 124, transform: nil)

    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -10), blur: 30, color: rgb(0, 0, 0, 0.55))
    ctx.addPath(pillPath)
    ctx.setFillColor(rgb(0.10, 0.10, 0.12))
    ctx.fillPath()
    ctx.restoreGState()

    ctx.saveGState()
    ctx.addPath(pillPath)
    ctx.clip()
    let glass = CGGradient(colorsSpace: space,
                           colors: [rgb(1, 1, 1, 0.16), rgb(1, 1, 1, 0.04), rgb(1, 1, 1, 0.07)] as CFArray,
                           locations: [0, 0.55, 1])!
    ctx.drawLinearGradient(glass, start: CGPoint(x: 512, y: pill.maxY), end: CGPoint(x: 512, y: pill.minY), options: [])
    ctx.restoreGState()

    ctx.addPath(CGPath(roundedRect: pill.insetBy(dx: 1.5, dy: 1.5), cornerWidth: 122.5, cornerHeight: 122.5, transform: nil))
    ctx.setStrokeColor(rgb(1, 1, 1, 0.22))
    ctx.setLineWidth(3)
    ctx.strokePath()

    // Rings: track plus progress arc, clockwise from the top.
    let rings: [(y: CGFloat, color: CGColor, value: CGFloat)] = [
        (512 + 196, green, 0.72),
        (512, amber, 0.46),
        (512 - 196, blue, 0.86),
    ]
    let radius: CGFloat = 70
    let lineWidth: CGFloat = 26
    ctx.setLineCap(.round)
    ctx.setLineWidth(lineWidth)
    for ring in rings {
        let center = CGPoint(x: 512, y: ring.y)
        ctx.addArc(center: center, radius: radius, startAngle: 0, endAngle: .pi * 2, clockwise: false)
        ctx.setStrokeColor(rgb(1, 1, 1, 0.10))
        ctx.strokePath()

        let start = CGFloat.pi / 2
        ctx.saveGState()
        ctx.setShadow(offset: .zero, blur: 18, color: ring.color.copy(alpha: 0.6))
        ctx.addArc(center: center, radius: radius, startAngle: start,
                   endAngle: start - ring.value * .pi * 2, clockwise: true)
        ctx.setStrokeColor(ring.color)
        ctx.strokePath()
        ctx.restoreGState()
    }
}

func render(pixels: Int) -> Data {
    let ctx = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    ctx.scaleBy(x: CGFloat(pixels) / canvas, y: CGFloat(pixels) / canvas)
    drawIcon(in: ctx)
    let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
    return rep.representation(using: .png, properties: [:])!
}

guard CommandLine.arguments.count == 2 else {
    FileHandle.standardError.write("usage: make-icon.swift <output.iconset>\n".data(using: .utf8)!)
    exit(1)
}
let output = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

for points in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let name = scale == 1 ? "icon_\(points)x\(points).png" : "icon_\(points)x\(points)@2x.png"
        try render(pixels: points * scale).write(to: output.appendingPathComponent(name))
    }
}
