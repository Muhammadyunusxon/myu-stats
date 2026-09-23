import SwiftUI

/// Near-black, flat surfaces: hairlines instead of shadows, white at stepped opacities for text.
enum SettingsPalette {
    static let window = Color(red: 0.055, green: 0.055, blue: 0.063)
    static let sidebar = Color(red: 0.086, green: 0.086, blue: 0.094)
    static let hairline = Color.white.opacity(0.07)
    static let edge = Color.white.opacity(0.09)
    static let selected = Color.white.opacity(0.10)
    static let hovered = Color.white.opacity(0.05)
    static let destructive = Color(red: 1, green: 0.42, blue: 0.4)
}

enum SettingsMetrics {
    static let width: CGFloat = 760
    static let height: CGFloat = 540
    static let sidebarWidth: CGFloat = 200
    /// Band the traffic lights sit in.
    static let headerHeight: CGFloat = 52
    static let cornerRadius: CGFloat = 20
}

/// Capsule button: quiet by default, brighter on hover, a touch smaller while pressed.
struct SettingsButtonStyle: ButtonStyle {
    var prominent = false

    func makeBody(configuration: Configuration) -> some View {
        SettingsButtonBody(configuration: configuration, prominent: prominent)
    }
}

private struct SettingsButtonBody: View {
    let configuration: ButtonStyleConfiguration
    let prominent: Bool
    @Environment(\.isEnabled) private var isEnabled
    @State private var isHovered = false

    var body: some View {
        let pressed = configuration.isPressed
        configuration.label
            .font(.system(size: 12, weight: .medium))
            .foregroundStyle(
                configuration.role == .destructive ? SettingsPalette.destructive
                    : prominent ? Color.black : Color.white.opacity(isHovered ? 1 : 0.9)
            )
            .padding(.horizontal, 12)
            .padding(.vertical, 5)
            .background(Capsule().fill(prominent
                ? Color.white.opacity(pressed ? 0.72 : isHovered ? 0.86 : 0.96)
                : Color.white.opacity(pressed ? 0.20 : isHovered ? 0.14 : 0.08)))
            .overlay {
                if !prominent {
                    Capsule().strokeBorder(.white.opacity(isHovered ? 0.16 : 0.09), lineWidth: 1)
                }
            }
            .contentShape(Capsule())
            .scaleEffect(pressed ? 0.97 : 1)
            .opacity(isEnabled ? 1 : 0.4)
            .onHover { hovering in
                withAnimation(.easeOut(duration: 0.12)) { isHovered = hovering && isEnabled }
            }
            .animation(.easeOut(duration: 0.1), value: pressed)
    }
}

/// Round, borderless icon button (reorder arrows and the like).
struct SettingsIconButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        SettingsIconButtonBody(configuration: configuration)
    }
}

private struct SettingsIconButtonBody: View {
    let configuration: ButtonStyleConfiguration
    @Environment(\.isEnabled) private var isEnabled
    @State private var isHovered = false

    var body: some View {
        configuration.label
            .font(.system(size: 10, weight: .bold))
            .foregroundStyle(Color.white.opacity(isHovered ? 0.95 : 0.6))
            .frame(width: 22, height: 22)
            .background(Circle().fill(.white.opacity(configuration.isPressed ? 0.16 : isHovered ? 0.09 : 0)))
            .contentShape(Circle())
            .scaleEffect(configuration.isPressed ? 0.92 : 1)
            .opacity(isEnabled ? 1 : 0.3)
            .onHover { hovering in
                withAnimation(.easeOut(duration: 0.12)) { isHovered = hovering && isEnabled }
            }
    }
}

/// Pane swap: the old pane blurs out while the new one sharpens in, in the same place.
private struct BlurFade: ViewModifier {
    let radius: CGFloat
    let opacity: Double

    func body(content: Content) -> some View {
        content.blur(radius: radius).opacity(opacity)
    }
}

extension AnyTransition {
    static var blurFade: AnyTransition {
        .modifier(active: BlurFade(radius: 10, opacity: 0), identity: BlurFade(radius: 0, opacity: 1))
    }
}
