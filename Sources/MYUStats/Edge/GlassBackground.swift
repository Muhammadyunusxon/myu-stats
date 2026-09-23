import AppKit
import SwiftUI

/// Pill and card surface, painted in the style chosen in Settings.
/// Liquid Glass needs macOS 26; older systems get a masked NSVisualEffectView instead.
struct GlassBackground<S: Shape>: View {
    var shape: S
    /// Added on top of the user's darkness, so cards can sit a touch darker than the pill.
    var extraDim: Double = 0

    @AppStorage(SettingsKey.surfaceStyle) private var style: SurfaceStyle = .glass
    @AppStorage(SettingsKey.glassDarkness) private var darkness: Double = 0.28

    var body: some View {
        switch style {
        case .solid:
            shape.fill(Color(white: 0.07))
                .overlay(shape.stroke(Color.white.opacity(0.1), lineWidth: 0.5))
        case .glass, .darkGlass:
            let dim = min(0.9, darkness + extraDim + (style == .darkGlass ? 0.2 : 0))
            if #available(macOS 26.0, *) {
                // A glass tint cannot darken, so the dim goes underneath the glass instead.
                Color.clear
                    .glassEffect(style == .darkGlass ? .clear : .regular, in: shape)
                    .background(shape.fill(Color.black.opacity(dim)))
            } else {
                ZStack {
                    BehindWindowBlur(shape: shape)
                    shape.fill(Color.black.opacity(dim))
                    shape.stroke(Color.white.opacity(0.16), lineWidth: 0.75)
                }
            }
        }
    }
}

/// SwiftUI materials only blur content inside the window; a transparent panel needs
/// NSVisualEffectView in behind-window mode, masked to the shape.
private struct BehindWindowBlur<S: Shape>: NSViewRepresentable {
    var shape: S

    func makeNSView(context: Context) -> MaskedEffectView {
        let view = MaskedEffectView()
        view.material = .hudWindow
        view.blendingMode = .behindWindow
        view.state = .active
        view.appearance = NSAppearance(named: .vibrantDark)
        return view
    }

    func updateNSView(_ view: MaskedEffectView, context: Context) {
        let shape = self.shape
        view.pathProvider = { size in shape.path(in: CGRect(origin: .zero, size: size)).cgPath }
    }
}

final class MaskedEffectView: NSVisualEffectView {
    var pathProvider: ((CGSize) -> CGPath)? {
        didSet { updateMask() }
    }

    override func layout() {
        super.layout()
        updateMask()
    }

    private func updateMask() {
        guard let pathProvider, bounds.width > 0, bounds.height > 0 else { return }
        let size = bounds.size
        let path = pathProvider(size)
        maskImage = NSImage(size: size, flipped: true) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            context.addPath(path)
            context.setFillColor(NSColor.black.cgColor)
            context.fillPath()
            return true
        }
    }
}
