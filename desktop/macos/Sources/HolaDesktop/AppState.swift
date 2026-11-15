import AppKit
import Combine
import HolaCore
import ServiceManagement

/// Everything the UI observes: the server session, today's list, the
/// pomodoro, the style, launch-at-login. One instance, main-actor only.
@MainActor final class AppState: ObservableObject {
    // Settings (UserDefaults; nothing secret)
    @Published var serverText: String { didSet { defaults.set(serverText, forKey: "cn.hola.desktop.server") } }
    @Published var username: String { didSet { defaults.set(username, forKey: "cn.hola.desktop.username") } }
    @Published var style: Style { didSet { defaults.set(style.rawValue, forKey: Style.defaultsKey) } }

    // Session + data
    @Published private(set) var signedIn = false
    @Published private(set) var snapshot: Snapshot?
    @Published private(set) var tasks: [Task] = []
    @Published private(set) var busy: Set<String> = []
    @Published private(set) var syncing = false
    @Published private(set) var offline = false
    @Published private(set) var lastSync: Date?
    @Published var notice: String?

    // Pomodoro
    @Published private(set) var pomodoro: Pomodoro
    @Published private(set) var now = Date()
    @Published private(set) var rang: PomoMode?

    // Launch at login
    @Published private(set) var launchAtLogin = false
    @Published private(set) var launchAtLoginError: String?

    let defaults: UserDefaults
    private var token: String?
    private var api: HolaAPI?
    private var pollTimer: Timer?
    private var tickTimer: Timer?
    private var asleep = false
    private var observers: [NSObjectProtocol] = []

    /// Idle poll interval. The server's own scheduler runs every 30 s, so
    /// nothing is gained by asking more often than this.
    static let pollInterval: TimeInterval = 45
    static let openRefreshAfter: TimeInterval = 10

    var theme: Theme { Theme.of(style) }
    var user: User? { snapshot?.user }
    var openCount: Int { TodayFilter.openCount(tasks) }
    var nextTask: Task? { TodayFilter.next(in: tasks) }
    var account: String { HolaAPI.normalize(serverText)?.absoluteString ?? serverText }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        serverText = defaults.string(forKey: "cn.hola.desktop.server") ?? "http://127.0.0.1:4173"
        username = defaults.string(forKey: "cn.hola.desktop.username") ?? ""
        style = Style(rawValue: defaults.string(forKey: Style.defaultsKey) ?? "") ?? .anthropic
        pomodoro = Pomodoro.load(from: defaults)
        restoreSession()
        observeSleep()
        refreshLaunchAtLogin()
        tickIfNeeded()
    }

    // MARK: - Session

    private func restoreSession() {
        guard let url = HolaAPI.normalize(serverText), let t = Keychain.load(account: url.absoluteString) else { return }
        token = t; api = HolaAPI(baseURL: url); signedIn = true
        startPolling()
        Concurrency.Task { await refresh() }
    }

    func signIn(password: String) async {
        guard let url = HolaAPI.normalize(serverText) else { notice = "服务器地址无效"; return }
        guard !username.isEmpty, !password.isEmpty else { notice = "请输入账户和密码"; return }
        let client = HolaAPI(baseURL: url)
        syncing = true; defer { syncing = false }
        do {
            let t = try await client.login(username: username, password: password)
            try Keychain.save(token: t, account: url.absoluteString)
            token = t; api = client; signedIn = true; notice = nil; offline = false
            startPolling()
            await refresh()
        } catch let e as APIError {
            notice = e.errorDescription
        } catch {
            notice = "无法保存登录状态：\(error.localizedDescription)"
        }
    }

    func signOut() {
        let old = (api, token)
        stopPolling()
        if let url = HolaAPI.normalize(serverText) { Keychain.delete(account: url.absoluteString) }
        token = nil; api = nil; signedIn = false; snapshot = nil; tasks = []; lastSync = nil; offline = false
        if let a = old.0, let t = old.1 { Concurrency.Task { try? await a.logout(token: t) } }
    }

    // MARK: - Sync

    func refresh() async {
        guard let api, let token, !syncing else { return }
        syncing = true; defer { syncing = false }
        do {
            let s = try await api.state(token: token)
            snapshot = s
            tasks = TodayFilter.tasks(in: s)
            lastSync = Date(); offline = false
            if notice?.hasPrefix("离线") == true { notice = nil }
        } catch APIError.unauthorized {
            // The session was closed server-side (expired, password changed, admin reset).
            signOut()
            notice = "登录已失效，请重新登录"
        } catch let e as APIError {
            if case .offline = e { offline = true } else { notice = e.errorDescription }
        } catch { offline = true }
    }

    /// Called when the popover opens: refresh if the list is stale.
    func refreshIfStale() {
        guard signedIn, let last = lastSync else { if signedIn { Concurrency.Task { await refresh() } }; return }
        if Date().timeIntervalSince(last) > Self.openRefreshAfter { Concurrency.Task { await refresh() } }
    }

    func complete(_ task: Task) async {
        guard let api, let token, let user, TodayFilter.canComplete(task, as: user), !busy.contains(task.id) else { return }
        busy.insert(task.id); defer { busy.remove(task.id) }
        do {
            try await api.complete(task, token: token)
            if let i = tasks.firstIndex(where: { $0.id == task.id }) { tasks[i].status = "done" }
            notice = "「\(task.name)」已完成"
            await refresh()
        } catch APIError.unauthorized {
            signOut(); notice = "登录已失效，请重新登录"
        } catch let e as APIError {
            // 403 (not mine / admin account) and 409 (already recorded) land here with the server's own words.
            notice = e.errorDescription
            await refresh()
        } catch { notice = error.localizedDescription }
    }

    private func startPolling() {
        stopPolling()
        pollTimer = Timer.scheduledTimer(withTimeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            guard let self else { return }
            Concurrency.Task { @MainActor in
                if !self.asleep { await self.refresh() }
            }
        }
        pollTimer?.tolerance = 10 // let the system coalesce wake-ups
    }

    private func stopPolling() { pollTimer?.invalidate(); pollTimer = nil }

    private func observeSleep() {
        let nc = NSWorkspace.shared.notificationCenter
        observers.append(nc.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            Concurrency.Task { @MainActor in self?.asleep = true }
        })
        observers.append(nc.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            Concurrency.Task { @MainActor in
                guard let self else { return }
                self.asleep = false
                self.tick()                       // settle a pomodoro that ran out while asleep
                if self.signedIn { await self.refresh() }
            }
        })
    }

    // MARK: - Pomodoro

    func pomoToggle() { pomodoro.toggle(at: Date()); rang = nil; savePomo(); tickIfNeeded() }
    func pomoReset() { pomodoro.reset(); rang = nil; savePomo(); tickIfNeeded() }
    func pomoSwitch() { pomodoro.switchMode(); rang = nil; savePomo(); tickIfNeeded() }

    private func savePomo() { pomodoro.save(to: defaults); now = Date() }

    private func tickIfNeeded() {
        if pomodoro.isRunning {
            if tickTimer == nil {
                tickTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
                    Concurrency.Task { @MainActor in self?.tick() }
                }
            }
        } else { tickTimer?.invalidate(); tickTimer = nil }
    }

    private func tick() {
        now = Date()
        if let finished = pomodoro.tick(at: now) {
            savePomo()
            rang = finished
            NSSound(named: "Glass")?.play()
            tickIfNeeded()
            // The ring badge clears itself after a few seconds, like the web widget's 6 s highlight.
            Concurrency.Task { @MainActor [weak self] in
                try? await Concurrency.Task.sleep(nanoseconds: 6_000_000_000)
                if self?.rang == finished { self?.rang = nil }
            }
        }
    }

    // MARK: - Launch at login (SMAppService, macOS 13+)

    func refreshLaunchAtLogin() {
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    func setLaunchAtLogin(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            launchAtLoginError = nil
        } catch {
            // Typical cause: running the bare executable instead of the .app bundle.
            launchAtLoginError = error.localizedDescription
        }
        refreshLaunchAtLogin()
    }
}

/// `Task` is the to-do model in HolaCore; the concurrency type is reached through this alias.
enum Concurrency {
    typealias Task = _Concurrency.Task
}
