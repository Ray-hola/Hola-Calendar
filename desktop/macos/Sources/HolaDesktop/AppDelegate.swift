import AppKit
import Combine
import SwiftUI
import HolaCore

@main
@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSPopoverDelegate {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)   // menu-bar only, like LSUIElement
        app.run()
    }

    private var state: AppState!
    private var statusItem: NSStatusItem!
    private var popover: NSPopover!
    private var touchBar: TouchBarController!
    private var island: IslandController!
    private var bag: Set<AnyCancellable> = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        state = AppState()
        touchBar = TouchBarController(state: state)
        island = IslandController(state: state)

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let b = statusItem.button {
            b.image = BrandIcon.statusItem
            b.imagePosition = .imageLeading
            b.font = .monospacedDigitSystemFont(ofSize: 12, weight: .medium)
            b.target = self
            b.action = #selector(statusClicked(_:))
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
            b.toolTip = "哗啦待办"
        }

        popover = NSPopover()
        popover.behavior = .transient
        popover.animates = true
        popover.delegate = self
        popover.contentViewController = NSHostingController(rootView: PopoverView(state: state))

        // The app-level Touch Bar shows whenever this app is frontmost (the popover is open).
        NSApp.touchBar = touchBar.makeTouchBar()

        state.objectWillChange.receive(on: RunLoop.main).sink { [weak self] _ in self?.renderStatusItem() }.store(in: &bag)
        renderStatusItem()
    }

    /// The menu-bar item carries the countdown while a session runs.
    private func renderStatusItem() {
        guard let b = statusItem?.button else { return }
        let p = state.pomodoro
        let title = p.isRunning ? " " + p.clock(at: state.now) : ""
        if b.title != title { b.title = title }
        let open = state.openCount
        b.toolTip = state.signedIn ? "哗啦待办 · 还剩 \(open) 项" : "哗啦待办 · 未登录"
    }

    @objc private func statusClicked(_ sender: NSStatusBarButton) {
        if NSApp.currentEvent?.type == .rightMouseUp { showMenu(sender); return }
        if popover.isShown { popover.performClose(nil) } else { showPopover() }
    }

    private func showPopover() {
        guard let button = statusItem?.button, !popover.isShown else { return }
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        // Becoming active gives the popover key status and brings up the Touch Bar.
        NSApp.activate(ignoringOtherApps: true)
        state.refreshIfStale()
    }

    /// `hola://connect?server=…` from the web app's 「连接桌面 App」: the address
    /// is filled in on the 连接 screen and the popover opens for the person to confirm.
    func application(_ application: NSApplication, open urls: [URL]) {
        guard let server = urls.lazy.compactMap(ConnectLink.server(from:)).first else { return }
        state.openConnectLink(server)
        showPopover()
    }

    private func showMenu(_ sender: NSStatusBarButton) {
        let menu = NSMenu()
        let p = state.pomodoro
        menu.addItem(withTitle: p.isRunning ? "暂停番茄钟" : "开始番茄钟", action: #selector(menuToggle), keyEquivalent: "").target = self
        menu.addItem(withTitle: p.mode == .focus ? "切到休息" : "切到专注", action: #selector(menuSwitch), keyEquivalent: "").target = self
        menu.addItem(.separator())
        let sync = menu.addItem(withTitle: "立即同步", action: #selector(menuSync), keyEquivalent: "")
        sync.target = self; sync.isEnabled = state.signedIn
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出 哗啦待办", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        statusItem.menu = menu
        sender.performClick(nil)
        statusItem.menu = nil   // so the next left click shows the popover again
    }

    @objc private func menuToggle() { state.pomoToggle() }
    @objc private func menuSwitch() { state.pomoSwitch() }
    @objc private func menuSync() { Concurrency.Task { await state.refresh() } }

    func popoverDidClose(_ notification: Notification) {
        // Give focus back to whatever the person was using.
        NSApp.hide(nil)
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply { .terminateNow }
}
