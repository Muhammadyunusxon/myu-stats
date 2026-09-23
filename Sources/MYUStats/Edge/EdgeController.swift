import AppKit
import Combine
import SwiftUI

/// Owns the edge panel: places it on the chosen edge, tracks hover, and runs the
/// settings orb and the move handle (slide along the edge, or drop onto another one).
@MainActor
final class EdgeController {
    private let store: StatsStore
    private let state = EdgeState()
    private let panel = EdgePanel()
    private let dropZones = DropZoneOverlay()
    private let onOpenSettings: () -> Void
    private let onOpenDetails: (StatMetric) -> Void
    private var hoverTimer: Timer?
    private var hideWorkItem: DispatchWorkItem?
    private var observers: [NSObjectProtocol] = []
    private var cancellables: Set<AnyCancellable> = []
    private var mouseMonitor: Any?
    /// Cursor position and along-offset when a move-handle drag began.
    private var dragStart: (mouse: NSPoint, offset: CGFloat)?

    init(store: StatsStore, onOpenSettings: @escaping () -> Void, onOpenDetails: @escaping (StatMetric) -> Void) {
        self.store = store
        self.onOpenSettings = onOpenSettings
        self.onOpenDetails = onOpenDetails
    }

    func show() {
        state.isRevealed = !UserDefaults.standard.bool(forKey: SettingsKey.autoHide)
        let host = NSHostingView(rootView: EdgeView(store: store, state: state))
        host.sizingOptions = []
        panel.contentView = host
        reposition()

        hoverTimer = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.updateHover() }
        }

        // The panel only takes mouse events while the cursor is on the orb or the move handle
        // (see updateHover), so any press reaching it is meant for one of them. A local monitor
        // sidesteps first-click activation, which a non-activating panel would otherwise swallow,
        // and keeps receiving drag events after the cursor leaves the handle.
        mouseMonitor = NSEvent.addLocalMonitorForEvents(
            matching: [.leftMouseDown, .leftMouseDragged, .leftMouseUp]
        ) { [weak self] event in
            let window = event.window
            let type = event.type
            let handled = MainActor.assumeIsolated { () -> Bool in
                self?.handleMouse(type: type, in: window) ?? false
            }
            return handled ? nil : event
        }

        let center = NotificationCenter.default
        for name in [NSApplication.didChangeScreenParametersNotification, UserDefaults.didChangeNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.reposition() }
            })
        }
        // Battery and thermal sensors arrive after the first slow sample; re-layout when either appears.
        store.$battery.combineLatest(store.$thermals)
            .map { ($0 != nil, $1 != nil) }
            .removeDuplicates { $0 == $1 }
            .dropFirst()
            .sink { [weak self] _ in self?.reposition() }
            .store(in: &cancellables)
    }

    // MARK: - Placement

    private func reposition() {
        guard let screen = NSScreen.screens.first else { return }
        let defaults = UserDefaults.standard
        let hidden = defaults.hiddenMetrics
        let metrics = defaults.metricOrder.filter { metric in
            guard !hidden.contains(metric) else { return false }
            switch metric {
            case .battery: return store.battery != nil
            case .thermals: return store.thermals != nil
            default: return true
            }
        }
        let layout = EdgeLayout(edge: defaults.screenEdge, cellCount: metrics.count)

        if state.metrics != metrics { state.metrics = metrics }
        if state.layout != layout { state.layout = layout }
        if let hovered = state.hovered, !metrics.contains(hovered) { setHovered(nil) }

        guard !metrics.isEmpty else {
            panel.orderOut(nil)
            return
        }
        let frame = layout.panelFrame(
            offset: currentOffset(for: layout), screen: screen.frame, usableTop: screen.visibleFrame.maxY
        ).integral
        if panel.frame != frame { panel.setFrame(frame, display: true) }
        panel.orderFrontRegardless()
    }

    private func currentOffset(for layout: EdgeLayout) -> CGFloat {
        guard let dragStart else { return UserDefaults.standard.double(forKey: SettingsKey.verticalOffset) }
        let mouse = NSEvent.mouseLocation
        let delta = layout.edge.isVertical ? mouse.y - dragStart.mouse.y : mouse.x - dragStart.mouse.x
        return dragStart.offset + delta
    }

    // MARK: - Mouse

    private func handleMouse(type: NSEvent.EventType, in window: NSWindow?) -> Bool {
        switch type {
        case .leftMouseDown:
            guard window === panel else { return false }
            if state.isOrbHovered {
                state.orbSpins += 1
                // Let the spin start before the window takes the stage.
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) { [weak self] in self?.onOpenSettings() }
                return true
            }
            if state.isDetailsHovered, let metric = state.hovered {
                onOpenDetails(metric)
                return true
            }
            if state.isHandleHovered {
                dragStart = (NSEvent.mouseLocation, UserDefaults.standard.double(forKey: SettingsKey.verticalOffset))
                state.isDragging = true
                cancelHide()
                if state.hovered != nil { setHovered(nil) }
                NSCursor.closedHand.set()
                return true
            }
            return false
        case .leftMouseDragged:
            guard dragStart != nil else { return false }
            reposition()
            updateDropZones()
            return true
        case .leftMouseUp:
            guard dragStart != nil else { return false }
            finishDrag()
            return true
        default:
            return false
        }
    }

    private func dropTarget(on screen: NSScreen) -> ScreenEdge? {
        EdgeLayout.dropTarget(
            for: NSEvent.mouseLocation, screen: screen.frame,
            usableTop: screen.visibleFrame.maxY, current: state.layout.edge
        )
    }

    private func updateDropZones() {
        guard let screen = NSScreen.screens.first else { return }
        let target = dropTarget(on: screen)
        let others = ScreenEdge.allCases.filter { $0 != state.layout.edge }
        dropZones.show(edges: others, armed: target, cursor: NSEvent.mouseLocation, screen: screen)
        let armed = target != nil
        if state.isDropArmed != armed {
            withAnimation(.easeOut(duration: 0.15)) { state.isDropArmed = armed }
        }
    }

    private func finishDrag() {
        guard let screen = NSScreen.screens.first else { return }
        let defaults = UserDefaults.standard
        let mouse = NSEvent.mouseLocation
        if let target = dropTarget(on: screen) {
            // Land centred on the cursor on the new edge.
            dragStart = nil
            let offset = target.isVertical ? mouse.y - screen.frame.midY : mouse.x - screen.frame.midX
            defaults.set(Double(offset), forKey: SettingsKey.verticalOffset)
            defaults.screenEdge = target
        } else {
            // Store the offset the panel actually ended at, so a clamped drag does not drift later.
            let offset = EdgeLayout.offset(of: panel.frame, screen: screen.frame, vertical: state.layout.edge.isVertical)
            dragStart = nil
            defaults.set(Double(offset), forKey: SettingsKey.verticalOffset)
        }
        dropZones.hide()
        state.isDragging = false
        withAnimation(.easeOut(duration: 0.15)) { state.isDropArmed = false }
        NSCursor.openHand.set()
    }

    // MARK: - Hover

    private func updateHover() {
        // A drag owns the pointer until it ends.
        guard dragStart == nil else { return }
        let mouse = NSEvent.mouseLocation
        let frame = panel.frame
        // Panel-local, y down — the same space EdgeLayout works in.
        let point = CGPoint(x: mouse.x - frame.minX, y: frame.maxY - mouse.y)
        let layout = state.layout
        let autoHide = UserDefaults.standard.bool(forKey: SettingsKey.autoHide)
        if !autoHide {
            setRevealed(true)
        } else if !state.isRevealed {
            setPointerTarget(orb: false, handle: false, details: false)
            if layout.isRevealTrigger(point) { setRevealed(true) }
            return
        }

        let onOrb = layout.isOn(.settings, point)
        let onHandle = !onOrb && layout.isOn(.move, point)
        var onDetails = false
        if let hovered = state.hovered, let index = state.metrics.firstIndex(of: hovered) {
            let card = layout.cardFrame(forCell: index, measured: state.cardHeight)
            onDetails = layout.detailsButtonRect(in: card).contains(point)
        }
        setPointerTarget(orb: onOrb, handle: onHandle, details: onDetails)
        if onDetails {
            cancelHide()
            return
        }
        if onOrb || onHandle {
            cancelHide()
            if state.hovered != nil { setHovered(nil) }
            return
        }

        if let index = (0..<state.metrics.count).first(where: { layout.cellRect($0).contains(point) }) {
            cancelHide()
            setHovered(state.metrics[index])
            return
        }

        var isInside = layout.pillRect.contains(point)
        if let hovered = state.hovered, let index = state.metrics.firstIndex(of: hovered) {
            let card = layout.cardFrame(forCell: index, measured: state.cardHeight)
            isInside = isInside || card.insetBy(dx: -4, dy: -4).contains(point)
        }
        if isInside {
            cancelHide()
        } else {
            scheduleHide(pill: autoHide)
        }
    }

    /// Closes the card after a short grace period, and with auto-hide also tucks the pill away.
    private func scheduleHide(pill: Bool) {
        guard hideWorkItem == nil, state.hovered != nil || (pill && state.isRevealed) else { return }
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.hideWorkItem = nil
                // Card first; the next tick schedules the pill with the user's hide delay.
                if self.state.hovered != nil {
                    self.setHovered(nil)
                } else if pill {
                    self.setRevealed(false)
                }
            }
        }
        hideWorkItem = work
        let pillDelay = UserDefaults.standard.double(forKey: SettingsKey.hideDelay)
        DispatchQueue.main.asyncAfter(deadline: .now() + (state.hovered != nil ? 0.2 : pillDelay), execute: work)
    }

    private func cancelHide() {
        hideWorkItem?.cancel()
        hideWorkItem = nil
    }

    private func setPointerTarget(orb: Bool, handle: Bool, details: Bool) {
        guard state.isOrbHovered != orb || state.isHandleHovered != handle || state.isDetailsHovered != details
        else { return }
        state.isOrbHovered = orb
        state.isHandleHovered = handle
        state.isDetailsHovered = details
        panel.ignoresMouseEvents = !(orb || handle || details)
        if orb || details {
            NSCursor.pointingHand.set()
        } else if handle {
            NSCursor.openHand.set()
        } else {
            NSCursor.arrow.set()
        }
    }

    private func setRevealed(_ revealed: Bool) {
        guard state.isRevealed != revealed else { return }
        withAnimation(revealed ? Motion.unfold : Motion.fold) {
            state.isRevealed = revealed
        }
    }

    private func setHovered(_ metric: StatMetric?) {
        guard state.hovered != metric else { return }
        // Opening springs out of the ring; moving between rings is a slower glide.
        withAnimation(state.hovered == nil || metric == nil ? Motion.contents : Motion.glide) {
            state.hovered = metric
        }
        store.setProcessSampling(
            (metric == .cpu || metric == .memory) && UserDefaults.standard.bool(forKey: SettingsKey.showProcesses),
            for: "card"
        )
    }
}
