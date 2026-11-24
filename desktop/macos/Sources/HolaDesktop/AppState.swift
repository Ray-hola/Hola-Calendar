import AppKit
import Combine
import HolaCore
import ServiceManagement

/// Everything the UI observes: the server session, today's list, the
/// pomodoro, the style, launch-at-login. One instance, main-actor only.
@MainActor final class AppState: ObservableObject {
    // Settings (UserDefaults; nothing secret)
    /// What is typed on the 连接 screen.
    @Published var serverText = ""
    @Published var username: String { didSet { defaults.set(username, forKey: "cn.hola.desktop.username") } }
    /// The server this app talks to; nil until the first 连接.
    @Published private(set) var server: ConnectedServer? {
        didSet { defaults.set(server?.url.absoluteString, forKey: "cn.hola.desktop.server") }
    }
    /// Servers connected to before, newest first. Tokens stay in the Keychain.
    @Published private(set) var servers: [SavedServer] {
        didSet { defaults.set(ServerList.encode(servers), forKey: "cn.hola.desktop.servers") }
    }
    @Published private(set) var connecting = false
    /// The 连接 screen over a known server: "连接新服务器", or a link from the web.
    @Published private(set) var showConnect = false
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
    private var taskRevision = 0
    private var token: String?
    private var api: HolaAPI?
    private var pollTimer: Timer?
    private var tickTimer: Timer?
    private var asleep = false
    private var observers: [NSObjectProtocol] = []
    /// Bumped whenever the person picks or leaves a server, so an answer that
    /// arrives afterwards for the old choice is dropped.
    private var choice = 0
    /// Bumped when a 连接 is cancelled too; only an address check waits on it.
    private var connectChoice = 0
    /// Servers dropped with 移除 while a sign-in to them may still be on its way.
    private var forgotten: Set<String> = []
    /// Bumped by every sign-in and sign-out, so only the newest sign-in can still count late.
    private var attempt = 0

    /// Idle poll interval. The server's own scheduler runs every 30 s, so
    /// nothing is gained by asking more often than this.
    static let pollInterval: TimeInterval = 45
    static let openRefreshAfter: TimeInterval = 10

    var theme: Theme { Theme.of(style) }
    var user: User? { snapshot?.user }
    var openCount: Int { TodayFilter.openCount(tasks) }
    var nextTask: Task? { TodayFilter.next(in: tasks) }
    var account: String { server?.url.absoluteString ?? "" }
    var connectScreen: Bool { showConnect || (!signedIn && server == nil) }
    /// What the server is called here: the name it was remembered by, else its host.
    var serverName: String {
        guard let server else { return "" }
        return servers.first { $0.url == server.url.absoluteString }?.name ?? server.info?.name ?? server.host
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        // A server signed in to before this version is kept, so nobody has to sign in again.
        var known = defaults.string(forKey: "cn.hola.desktop.server").flatMap(HolaAPI.normalize)
        // Before this version an untouched address was never saved: the old default is where its session lives.
        if known == nil, let legacy = HolaAPI.normalize("http://127.0.0.1:4173"), Keychain.load(account: legacy.absoluteString) != nil {
            known = legacy
        }
        server = known.map { ConnectedServer(url: $0, info: nil) }
        servers = ServerList.decode(defaults.data(forKey: "cn.hola.desktop.servers"))
        username = defaults.string(forKey: "cn.hola.desktop.username") ?? ""
        style = Style(rawValue: defaults.string(forKey: Style.defaultsKey) ?? "") ?? .anthropic
        pomodoro = Pomodoro.load(from: defaults)
        restoreSession()
        observeSleep()
        refreshLaunchAtLogin()
        tickIfNeeded()
    }

    // MARK: - Session

    /// Picks up the session kept in the Keychain for the current server, if any.
    @discardableResult private func restoreSession() -> Bool {
        guard let url = server?.url, let t = Keychain.load(account: url.absoluteString) else { return false }
        token = t; api = HolaAPI(baseURL: url); signedIn = true
        if !servers.contains(where: { $0.url == url.absoluteString }) {
            remember(SavedServer(url: url.absoluteString, name: server?.info?.name ?? "哗啦待办", username: username))
        }
        startPolling()
        Concurrency.Task { await refresh() }
        return true
    }

    /// Checks the typed address and, when it is a 哗啦待办 server, makes it the
    /// current one. A session kept for it is resumed; otherwise sign-in comes next.
    func connect() async {
        guard !connecting else { return }
        connecting = true; notice = nil
        defer { connecting = false }
        let asked = connectChoice
        do {
            let (api, info) = try await HolaAPI.connect(serverText)
            // Cancelled, or another server picked, while the address was being checked.
            guard connectChoice == asked else { return }
            use(ConnectedServer(url: api.baseURL, info: info))
        } catch {
            guard connectChoice == asked else { return }
            notice = error.localizedDescription
        }
    }

    /// One click from the remembered list: no address check, no sign-out of the others.
    func switchTo(_ saved: SavedServer) {
        guard let url = HolaAPI.normalize(saved.url) else { return }
        use(ConnectedServer(url: url, info: nil))
    }

    private func use(_ next: ConnectedServer) {
        choice += 1; connectChoice += 1
        forgotten.remove(next.url.absoluteString)
        leaveServer()
        server = next; showConnect = false; serverText = ""
        if let saved = servers.first(where: { $0.url == next.url.absoluteString }) { username = saved.username }
        if restoreSession() { notice = nil }
    }

    /// Stops talking to the current server but keeps its session in the Keychain.
    private func leaveServer() {
        stopPolling()
        token = nil; api = nil; signedIn = false; snapshot = nil; tasks = []; lastSync = nil; offline = false
        syncing = false; busy = []; notice = nil
    }

    func showConnectScreen() { showConnect = true; serverText = ""; notice = nil }
    func cancelConnect() { if server != nil { connectChoice += 1; showConnect = false; notice = nil } }

    /// A `hola://connect` link only fills in the address: the person checks it and presses 连接.
    func openConnectLink(_ url: URL) {
        showConnect = true
        serverText = url.absoluteString
        notice = "来自网页的连接，确认地址无误后点「连接」"
    }

    /// Drops a remembered server and its kept session.
    func forget(_ saved: SavedServer) {
        forgotten.insert(saved.url)
        if saved.url == server?.url.absoluteString { choice += 1; signOut(); server = nil }
        else if let t = Keychain.load(account: saved.url), let url = HolaAPI.normalize(saved.url) {
            // Its session is closed on the server too, not only forgotten here.
            Concurrency.Task { try? await HolaAPI(baseURL: url).logout(token: t) }
        }
        Keychain.delete(account: saved.url)
        servers = ServerList.forget(saved.url, in: servers)
    }

    /// Remembers `saved` first; a server pushed off the end of the list loses its kept session.
    /// The current server is never the one pushed off.
    private func remember(_ saved: SavedServer) {
        let current = server?.url.absoluteString
        let next = ServerList.remember(saved, in: servers, keeping: current)
        for gone in servers where !next.contains(where: { $0.url == gone.url }) && gone.url != current {
            Keychain.delete(account: gone.url)
        }
        servers = next
    }

    func signIn(password: String) async {
        guard !syncing, !signedIn else { return }
        guard let url = server?.url else { showConnect = true; return }
        guard !username.isEmpty, !password.isEmpty else { notice = "请输入账户和密码"; return }
        let client = HolaAPI(baseURL: url), asked = choice, account = username
        let name = server?.info?.name ?? serverName
        attempt += 1
        let mine = attempt
        syncing = true
        var token: String?, failure: String?
        do {
            token = try await client.login(username: account, password: password)
        } catch let e as APIError {
            failure = e.errorDescription
        } catch {
            failure = error.localizedDescription
        }
        let key = url.absoluteString
        // The same server picked again meanwhile, with no newer sign-in or sign-out since: this one still counts.
        let again = choice != asked && key == server?.url.absoluteString && !signedIn && attempt == mine && !forgotten.contains(key)
        guard choice == asked || again else {
            guard let t = token else { return }
            // Another server was picked: keep this session for when it is picked again.
            // It is closed instead if this server was 移除'd, or is current and signed in or out another way.
            let kept = Keychain.load(account: key)
            if forgotten.contains(key) || key == server?.url.absoluteString || (try? Keychain.save(token: t, account: key)) == nil {
                Concurrency.Task { try? await client.logout(token: t) }
            } else {
                // A session kept from an earlier late sign-in is replaced, so it is closed too.
                if let kept, kept != t { Concurrency.Task { try? await client.logout(token: kept) } }
                remember(SavedServer(url: key, name: name, username: account))
            }
            return
        }
        if again { username = account }
        syncing = false
        if let t = token {
            do {
                try Keychain.save(token: t, account: key)
            } catch {
                notice = "无法保存登录状态：\(error.localizedDescription)"
                Concurrency.Task { try? await client.logout(token: t) }
                return
            }
            self.token = t; api = client; signedIn = true; notice = nil; offline = false
            remember(SavedServer(url: key, name: name, username: account))
            startPolling()
        } else {
            notice = failure
        }
        if signedIn { await refresh() }
    }

    func signOut() {
        attempt += 1
        let old = (api, token)
        stopPolling()
        if let url = old.0?.baseURL { Keychain.delete(account: url.absoluteString) }
        token = nil; api = nil; signedIn = false; snapshot = nil; tasks = []; lastSync = nil; offline = false
        syncing = false; busy = []; notice = nil
        if let a = old.0, let t = old.1 { Concurrency.Task { try? await a.logout(token: t) } }
    }

    // MARK: - Sync

    func refresh() async {
        guard let api, let token, !syncing, busy.isEmpty else { return }
        let revision = taskRevision
        syncing = true
        defer { if self.api === api { syncing = false } }
        do {
            let s = try await api.state(token: token)
            guard self.api === api, taskRevision == revision else { return }
            snapshot = s
            tasks = TodayFilter.tasks(in: s)
            lastSync = Date(); offline = false
            if notice?.hasPrefix("离线") == true { notice = nil }
        } catch APIError.unauthorized {
            guard self.api === api else { return }
            // The session was closed server-side (expired, password changed, admin reset).
            signOut()
            notice = "登录已失效，请重新登录"
        } catch let e as APIError {
            guard self.api === api else { return }
            if case .offline = e { offline = true } else { notice = e.errorDescription }
        } catch { if self.api === api { offline = true } }
    }

    /// Called when the popover opens: refresh if the list is stale.
    func refreshIfStale() {
        guard signedIn, let last = lastSync else { if signedIn { Concurrency.Task { await refresh() } }; return }
        if Date().timeIntervalSince(last) > Self.openRefreshAfter { Concurrency.Task { await refresh() } }
    }

    func complete(_ task: Task) async {
        guard let api, let token, let user, TodayFilter.canComplete(task, as: user), !busy.contains(task.id) else { return }
        busy.insert(task.id)
        taskRevision += 1
        do {
            try await api.complete(task, token: token)
            guard self.api === api else { return }
            if let i = tasks.firstIndex(where: { $0.id == task.id }) { tasks[i].status = "done" }
            tasks.sort { $0.isOpen && !$1.isOpen }
            notice = "「\(task.name)」已完成"
        } catch APIError.unauthorized {
            guard self.api === api else { return }
            signOut(); notice = "登录已失效，请重新登录"
            return
        } catch {
            guard self.api === api else { return }
            notice = error.localizedDescription
        }
        busy.remove(task.id)
        await refresh()
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

/// The server in use: its address and, once checked on 连接, what it said about itself.
struct ConnectedServer: Equatable {
    var url: URL
    var info: ServerInfo?
    var host: String { SavedServer(url: url.absoluteString, name: "", username: "").host }
    var exposesPassword: Bool { ServerAddress.exposesPassword(url) }
}

/// `Task` is the to-do model in HolaCore; the concurrency type is reached through this alias.
enum Concurrency {
    typealias Task = _Concurrency.Task
}
