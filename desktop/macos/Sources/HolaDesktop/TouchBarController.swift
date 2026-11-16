import AppKit
import Combine
import HolaCore

/// The Touch Bar strip shown while the app is frontmost (i.e. while the
/// popover is open): countdown, play/pause, mode switch and the next to-do.
/// Macs without a Touch Bar never ask for it, so nothing here runs there.
/// Putting an item in the Control Strip permanently needs the private
/// DFRFoundation framework, so that is deliberately not done.
@MainActor final class TouchBarController: NSObject, NSTouchBarDelegate {
    private let state: AppState
    private var bag: Set<AnyCancellable> = []
    private weak var clockLabel: NSTextField?
    private weak var taskLabel: NSTextField?
    private weak var toggleButton: NSButton?
    private weak var modeButton: NSButton?

    static let clock = NSTouchBarItem.Identifier("cn.hola.desktop.touchbar.clock")
    static let toggle = NSTouchBarItem.Identifier("cn.hola.desktop.touchbar.toggle")
    static let mode = NSTouchBarItem.Identifier("cn.hola.desktop.touchbar.mode")
    static let task = NSTouchBarItem.Identifier("cn.hola.desktop.touchbar.task")

    init(state: AppState) {
        self.state = state
        super.init()
        state.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.render() }
            .store(in: &bag)
    }

    func makeTouchBar() -> NSTouchBar {
        let bar = NSTouchBar()
        bar.delegate = self
        bar.customizationIdentifier = "cn.hola.desktop.touchbar"
        bar.defaultItemIdentifiers = [Self.toggle, Self.clock, Self.mode, .flexibleSpace, Self.task]
        bar.principalItemIdentifier = Self.task
        return bar
    }

    func touchBar(_ touchBar: NSTouchBar, makeItemForIdentifier id: NSTouchBarItem.Identifier) -> NSTouchBarItem? {
        let item = NSCustomTouchBarItem(identifier: id)
        switch id {
        case Self.clock:
            let l = NSTextField(labelWithString: "25:00")
            l.font = .monospacedDigitSystemFont(ofSize: 15, weight: .semibold)
            clockLabel = l; item.view = l
        case Self.toggle:
            let b = NSButton(image: NSImage(systemSymbolName: "play.fill", accessibilityDescription: "开始")!, target: self, action: #selector(toggle))
            b.bezelColor = NSColor(Theme.terracotta)
            toggleButton = b; item.view = b
        case Self.mode:
            let b = NSButton(title: "切到休息", target: self, action: #selector(switchMode))
            modeButton = b; item.view = b
        case Self.task:
            let l = NSTextField(labelWithString: "")
            l.lineBreakMode = .byTruncatingTail
            l.maximumNumberOfLines = 1
            l.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
            taskLabel = l; item.view = l
        default: return nil
        }
        render()
        return item
    }

    /// Called every second via the state's `now`, and on any other change.
    func render() {
        let p = state.pomodoro, now = state.now
        clockLabel?.stringValue = p.clock(at: now)
        toggleButton?.image = NSImage(systemSymbolName: p.isRunning ? "pause.fill" : "play.fill", accessibilityDescription: p.isRunning ? "暂停" : "开始")
        modeButton?.title = p.mode == .focus ? "切到休息" : "切到专注"
        if let t = state.nextTask {
            taskLabel?.stringValue = "下一项 · \(t.name)"
        } else {
            taskLabel?.stringValue = state.signedIn ? "今天没有待办" : "哗啦待办 · 未登录"
        }
    }

    @objc private func toggle() { state.pomoToggle() }
    @objc private func switchMode() { state.pomoSwitch() }
}

import SwiftUI
extension NSColor {
    convenience init(_ c: Color) { self.init(cgColor: c.cgColor ?? NSColor.systemOrange.cgColor)! }
}
