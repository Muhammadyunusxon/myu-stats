import AppKit
import SwiftUI

struct AppearancePane: View {
    @AppStorage(SettingsKey.edge) private var edge: ScreenEdge = .right
    @AppStorage(SettingsKey.surfaceStyle) private var style: SurfaceStyle = .glass
    @AppStorage(SettingsKey.glassDarkness) private var darkness: Double = 0.28
    @AppStorage(SettingsKey.autoHide) private var autoHide = true
    @AppStorage(SettingsKey.hideDelay) private var hideDelay: Double = 0.45
    @AppStorage(SettingsKey.verticalOffset) private var verticalOffset: Double = 0
    @AppStorage(SettingsKey.display) private var display = ""
    @State private var screens: [(uuid: String, name: String)] = []

    var body: some View {
        Section("Pill") {
            Picker("Edge", selection: $edge) {
                ForEach(ScreenEdge.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            // An offset along one edge means nothing on another; start the new edge centred.
            .onChange(of: edge) { _, _ in UserDefaults.standard.set(0.0, forKey: SettingsKey.verticalOffset) }
            caption("Pick the edge another edge app is not using.")

            // Only worth showing with a second display, or to switch back once it is gone.
            if screens.count > 1 || !display.isEmpty {
                Picker("Display", selection: $display) {
                    Text("Main display").tag("")
                    ForEach(screens.dropFirst(), id: \.uuid) { Text(verbatim: $0.name).tag($0.uuid) }
                    if !display.isEmpty, !screens.contains(where: { $0.uuid == display }) {
                        Text("Disconnected display").tag(display)
                    }
                }
                .onChange(of: display) { _, _ in UserDefaults.standard.set(0.0, forKey: SettingsKey.verticalOffset) }
                caption("The main display is the one with the menu bar in System Settings › Displays.")
            }

            HStack {
                Text("Position along the edge")
                Spacer()
                Button("Re-centre") { UserDefaults.standard.set(0.0, forKey: SettingsKey.verticalOffset) }
                    .disabled(verticalOffset == 0)
            }
            caption("Drag the arc at the start of the pill to slide it along the edge, or onto any other edge.")

            Picker("Surface", selection: $style) {
                ForEach(SurfaceStyle.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            caption(verbatim: style.explanation)

            LabeledContent("Darkness") {
                HStack {
                    Slider(value: $darkness, in: 0...0.6)
                    Text(Format.percent(darkness / 0.6)).monospacedDigit().frame(width: 38, alignment: .trailing)
                }
            }
            .disabled(style == .solid)
        }
        .onAppear(perform: loadScreens)
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didChangeScreenParametersNotification)) { _ in
            loadScreens()
        }
        Section("Behaviour") {
            Toggle("Auto-hide", isOn: $autoHide)
            caption("When hidden, only a thin tab stays on the edge. Touch the edge next to it to open the pill.")

            LabeledContent("Hide after") {
                HStack {
                    Slider(value: $hideDelay, in: 0.2...2, step: 0.05)
                    Text(String(format: "%.2f s", hideDelay)).monospacedDigit().frame(width: 46, alignment: .trailing)
                }
            }
            .disabled(!autoHide)
        }
    }

    private func loadScreens() {
        screens = NSScreen.screens.compactMap { screen in
            screen.displayUUID.map { ($0, screen.localizedName) }
        }
    }

    private func caption(_ key: LocalizedStringKey) -> some View {
        caption(Text(key))
    }

    /// For text that is already localized, such as a surface style's explanation.
    private func caption(verbatim text: String) -> some View {
        caption(Text(text))
    }

    private func caption(_ text: Text) -> some View {
        text
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}
