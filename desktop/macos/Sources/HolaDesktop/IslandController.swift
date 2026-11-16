import AppKit
import Combine
import SwiftUI
import HolaCore

/// Where the island goes on a given screen.
struct IslandGeometry: Equatable {
    let screenFrame: NSRect
    let visibleTop: CGFloat
    let hasNotch: Bool
    let notchWidth: CGFloat
    let topInset: CGFloat

    static let wing: CGFloat = 92          // each side of the notch, in points
    static let pillHeight: CGFloat = 28    // no-notch fallback pill
    static let expandedExtra: CGFloat = 68

    init(screen: NSScreen) {
        screenFrame = screen.frame
        visibleTop = screen.visibleFrame.maxY
        let inset = screen.safeAreaInsets.top
        hasNotch = inset > 0
        topInset = inset
        if hasNotch {
            // The notch is what is left between the two usable menu-bar areas.
            let left = screen.auxiliaryTopLeftArea?.width ?? 0
            let right = screen.auxiliaryTopRightArea?.width ?? 0
            notchWidth = max(120, screen.frame.width - left - right)
        } else {
            notchWidth = 0
        }
    }

    func frame(expanded: Bool) -> NSRect {
        let midX = screenFrame.midX
        if hasNotch {
            let w = expanded ? max(380, notchWidth + 2 * Self.wing) : notchWidth + 2 * Self.wing
            let h = expanded ? topInset + Self.expandedExtra : topInset
            return NSRect(x: midX - w / 2, y: screenFrame.maxY - h, width: w, height: h)
        } else {
            let w: CGFloat = expanded ? 340 : 168
            let h: CGFloat = expanded ? Self.pillHeight + Self.expandedExtra : Self.pillHeight
            return NSRect(x: midX - w / 2, y: visibleTop - 6 - h, width: w, height: h)
        }
    }
}

/// A borderless, non-activating panel at the top centre of the screen that
/// shows the running pomodoro and grows on hover to show the current to-do.
/// On a Mac with a notch it wraps the notch in black so it reads as one
/// shape; elsewhere it is a small pill just under the menu bar. It follows
/// the screen the mouse is on and hides when no session is running.
@MainActor final class IslandController {
    private let state: AppState
    private var panel: NSPanel?
    private var hosting: NSHostingView<IslandView>?
    private var bag: Set<AnyCancellable> = []
    private var geometry: IslandGeometry?
    private(set) var expanded = false
    private var hideTask: _Concurrency.Task<Void, Never>?

    init(state: AppState) {
        self.state = state
        state.objectWillChange.receive(on: RunLoop.main).sink { [weak self] _ in self?.update() }.store(in: &bag)
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            _Concurrency.Task { @MainActor in self?.geometry = nil; self?.update() }
        }
        update()
    }

    private var shouldShow: Bool { state.pomodoro.isRunning || state.rang != nil }

    func update() {
        guard shouldShow else { hide(); return }
        let screen = Self.targetScreen()
        let geo = IslandGeometry(screen: screen)
        if geometry != geo { geometry = geo; relayout(animated: false) }
        if panel == nil { show(geo) }
        hosting?.rootView = IslandView(state: state, geometry: geo, expanded: expanded, onHover: { [weak self] in self?.setExpanded($0) })
    }

    /// The screen under the mouse, else the main screen, else the first.
    static func targetScreen() -> NSScreen {
        let mouse = NSEvent.mouseLocation
        return NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main ?? NSScreen.screens[0]
    }

    private func show(_ geo: IslandGeometry) {
        let p = NSPanel(contentRect: geo.frame(expanded: false), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.level = .statusBar
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = !geo.hasNotch
        p.hidesOnDeactivate = false
        p.isMovableByWindowBackground = false
        p.ignoresMouseEvents = false
        p.acceptsMouseMovedEvents = true
        p.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        p.isReleasedWhenClosed = false
        let view = NSHostingView(rootView: IslandView(state: state, geometry: geo, expanded: false, onHover: { [weak self] in self?.setExpanded($0) }))
        p.contentView = view
        hosting = view
        panel = p
        p.alphaValue = 0
        p.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in ctx.duration = 0.25; p.animator().alphaValue = 1 }
    }

    private func hide() {
        guard let p = panel else { return }
        panel = nil; hosting = nil; expanded = false
        NSAnimationContext.runAnimationGroup({ ctx in ctx.duration = 0.2; p.animator().alphaValue = 0 }, completionHandler: { p.orderOut(nil) })
    }

    func setExpanded(_ on: Bool) {
        guard on != expanded else { return }
        expanded = on
        relayout(animated: true)
        update()
    }

    private func relayout(animated: Bool) {
        guard let p = panel, let geo = geometry else { return }
        let f = geo.frame(expanded: expanded)
        if animated {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.22
                ctx.timingFunction = CAMediaTimingFunction(name: .easeOut)
                p.animator().setFrame(f, display: true)
            }
        } else { p.setFrame(f, display: true) }
    }
}

/// The island's face. Black in both styles so it merges with the notch; the
/// style decides typography, progress and mascot.
struct IslandView: View {
    @ObservedObject var state: AppState
    let geometry: IslandGeometry
    let expanded: Bool
    let onHover: (Bool) -> Void

    private var theme: Theme { state.theme }
    private var fg: Color { Theme.warmPaper }

    var body: some View {
        let p = state.pomodoro, now = state.now
        ZStack(alignment: .top) {
            shape.fill(Color.black)
            VStack(spacing: 0) {
                // Top row: wings beside the notch (or the whole pill).
                HStack(spacing: 0) {
                    HStack(spacing: 6) {
                        Mascot(style: state.style, mood: p.mood, size: geometry.hasNotch ? geometry.topInset - 8 : 20)
                            .environment(\.islandDark, true)
                        Text(p.clock(at: now))
                            .font(theme.isTempo ? .system(size: 14, weight: .bold, design: .monospaced) : .system(size: 14, weight: .semibold, design: .rounded).monospacedDigit())
                    }
                    .frame(width: geometry.hasNotch ? IslandGeometry.wing - 8 : nil, alignment: .leading)
                    .padding(.leading, geometry.hasNotch ? 10 : 12)
                    if geometry.hasNotch { Spacer(minLength: geometry.notchWidth) } else { Spacer(minLength: 8) }
                    rightWing(p, now)
                        .frame(width: geometry.hasNotch ? IslandGeometry.wing - 8 : nil, alignment: .trailing)
                        .padding(.trailing, geometry.hasNotch ? 10 : 12)
                }
                .frame(height: geometry.hasNotch ? geometry.topInset : IslandGeometry.pillHeight)
                if expanded { detail(p, now).frame(height: IslandGeometry.expandedExtra) }
            }
        }
        .foregroundStyle(fg)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .contentShape(shape)
        .onHover(perform: onHover)
        .onTapGesture { state.pomoToggle() }
        .animation(.easeOut(duration: 0.2), value: expanded)
    }

    /// Square top edge against the notch, rounded everywhere else.
    private var shape: IslandShape { IslandShape(topRadius: geometry.hasNotch ? 0 : 14, bottomRadius: 14) }

    private func rightWing(_ p: Pomodoro, _ now: Date) -> some View {
        HStack(spacing: 6) {
            if theme.isTempo {
                Text(p.asciiBar(at: now, width: 6)).font(.system(size: 11, design: .monospaced))
            } else {
                Text(p.mode == .focus ? "专注" : "休息").font(.system(size: 11, weight: .medium, design: .serif)).opacity(0.8)
                ZStack {
                    Circle().stroke(fg.opacity(0.25), lineWidth: 2.5)
                    Circle().trim(from: 0, to: p.progress(at: now)).stroke(Theme.terracotta, style: StrokeStyle(lineWidth: 2.5, lineCap: .round)).rotationEffect(.degrees(-90))
                }.frame(width: 14, height: 14)
            }
            if state.rang != nil { Image(systemName: "bell.fill").font(.system(size: 10)).foregroundStyle(Theme.terracotta) }
        }
    }

    private func detail(_ p: Pomodoro, _ now: Date) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(theme.isTempo ? "> 当前待办" : "当前待办").font(theme.isTempo ? .system(size: 11, design: .monospaced) : .system(size: 11, weight: .medium, design: .serif)).opacity(0.65)
                Spacer()
                Text(state.signedIn ? "还剩 \(state.openCount) 项" : "未登录").font(.system(size: 11, design: theme.isTempo ? .monospaced : .default)).opacity(0.65)
            }
            Text(state.nextTask?.name ?? (state.signedIn ? "今天没有待办了" : "点菜单栏图标登录"))
                .font(theme.isTempo ? .system(size: 13, weight: .bold, design: .monospaced) : .system(size: 14, weight: .semibold, design: .serif))
                .lineLimit(1)
            if theme.isTempo {
                Text(p.asciiBar(at: now, width: 24)).font(.system(size: 11, design: .monospaced))
            } else {
                ThemedProgress(theme: theme, progress: p.progress(at: now), ascii: "")
            }
            Text(p.isRunning ? "点一下暂停" : "点一下继续").font(.system(size: 10)).opacity(0.5)
        }
        .padding(.horizontal, 16).padding(.bottom, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// A rectangle whose top and bottom corners have different radii (macOS 13-safe
/// stand-in for UnevenRoundedRectangle, which needs macOS 14).
struct IslandShape: Shape {
    var topRadius: CGFloat
    var bottomRadius: CGFloat
    func path(in r: CGRect) -> Path {
        let t = min(topRadius, r.width / 2, r.height / 2), b = min(bottomRadius, r.width / 2, r.height / 2)
        var p = Path()
        p.move(to: CGPoint(x: r.minX + t, y: r.minY))
        p.addLine(to: CGPoint(x: r.maxX - t, y: r.minY))
        p.addArc(center: CGPoint(x: r.maxX - t, y: r.minY + t), radius: t, startAngle: .degrees(-90), endAngle: .degrees(0), clockwise: false)
        p.addLine(to: CGPoint(x: r.maxX, y: r.maxY - b))
        p.addArc(center: CGPoint(x: r.maxX - b, y: r.maxY - b), radius: b, startAngle: .degrees(0), endAngle: .degrees(90), clockwise: false)
        p.addLine(to: CGPoint(x: r.minX + b, y: r.maxY))
        p.addArc(center: CGPoint(x: r.minX + b, y: r.maxY - b), radius: b, startAngle: .degrees(90), endAngle: .degrees(180), clockwise: false)
        p.addLine(to: CGPoint(x: r.minX, y: r.minY + t))
        p.addArc(center: CGPoint(x: r.minX + t, y: r.minY + t), radius: t, startAngle: .degrees(180), endAngle: .degrees(270), clockwise: false)
        p.closeSubpath()
        return p
    }
}

/// Lets a mascot know it is on the black island, so the pixel sprite swaps
/// its ink for paper.
struct IslandDarkKey: EnvironmentKey { static let defaultValue = false }
extension EnvironmentValues {
    var islandDark: Bool { get { self[IslandDarkKey.self] } set { self[IslandDarkKey.self] = newValue } }
}
