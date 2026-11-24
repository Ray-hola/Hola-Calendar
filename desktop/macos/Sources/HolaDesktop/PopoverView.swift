import SwiftUI
import HolaCore

/// The menu-bar popover: sign-in or today's list, the pomodoro, style and
/// launch-at-login switches. Every piece reads the active theme.
struct PopoverView: View {
    @ObservedObject var state: AppState
    @State private var password = ""

    var theme: Theme { state.theme }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header
            pomodoroSection
            if state.connectScreen { connectSection } else if state.signedIn { taskSection } else { signInSection }
            footer
        }
        .padding(14)
        .frame(width: 320)
        .background(theme.paper)
        .foregroundStyle(theme.ink)
        .font(theme.body)
        .onAppear { state.refreshIfStale(); state.refreshLaunchAtLogin() }
        // A typed password belongs to the server it was typed for, never to the next one.
        .onChange(of: state.account) { _ in password = "" }
    }

    // MARK: Header

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(theme.heading("今日待办")).font(theme.title)
            Spacer()
            if let snap = state.snapshot {
                Text(theme.isTempo ? "\(snap.today) · 还剩 \(state.openCount) 项" : "\(snap.today) · 还剩 \(state.openCount) 项")
                    .font(theme.small).foregroundStyle(theme.muted)
            }
        }
    }

    // MARK: Pomodoro

    private var pomodoroSection: some View {
        let p = state.pomodoro, now = state.now
        return HStack(spacing: 12) {
            Mascot(style: state.style, mood: p.mood, size: 54)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(p.clock(at: now)).font(theme.clock)
                    Text(p.mode == .focus ? "专注 25 分钟" : "休息 5 分钟").font(theme.small).foregroundStyle(theme.muted)
                    if let r = state.rang {
                        Text(r == .focus ? (theme.isTempo ? "*ding* 休息一下" : "专注结束，休息一下") : (theme.isTempo ? "*ding* 继续专注" : "休息结束，继续专注"))
                            .font(theme.small).foregroundStyle(theme.accent)
                    }
                }
                ThemedProgress(theme: theme, progress: p.progress(at: now), ascii: p.asciiBar(at: now, width: 14))
                HStack(spacing: theme.isTempo ? 8 : 6) {
                    Button(theme.bracket(p.isRunning ? "暂停" : "开始")) { state.pomoToggle() }
                        .buttonStyle(ThemedButtonStyle(theme: theme, primary: true))
                    Button(theme.bracket("重置")) { state.pomoReset() }.buttonStyle(ThemedButtonStyle(theme: theme))
                    Button(theme.bracket(p.mode == .focus ? "切到休息" : "切到专注")) { state.pomoSwitch() }.buttonStyle(ThemedButtonStyle(theme: theme))
                }
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: theme.isTempo ? 0 : 10).fill(theme.panel))
        .overlay(RoundedRectangle(cornerRadius: theme.isTempo ? 0 : 10).stroke(state.rang != nil ? theme.accent : theme.line))
    }

    // MARK: Tasks

    private var taskSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                if let u = state.user {
                    Text(theme.isTempo ? "@\(u.name)" : u.name).font(theme.small).foregroundStyle(theme.muted)
                }
                serverMenu
                Spacer()
                statusLine
            }
            if state.tasks.isEmpty {
                Text(theme.isTempo ? "~ 今天没有待办 ~" : "今天没有待办").foregroundStyle(theme.muted).padding(.vertical, 8)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 2) {
                        ForEach(state.tasks) { t in taskRow(t) }
                    }
                }
                .frame(maxHeight: 220)
            }
            if let n = state.notice {
                Text(theme.isTempo ? "> \(n)" : n).font(theme.small).foregroundStyle(theme.accent)
                    .lineLimit(2).fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var statusLine: some View {
        Group {
            if state.syncing {
                Text(theme.isTempo ? "[syncing]" : "同步中…")
            } else if state.offline, let last = state.lastSync {
                Text(theme.isTempo ? "[offline] last \(Self.hhmm(last))" : "离线 · 上次同步 \(Self.hhmm(last))")
            } else if state.offline {
                Text(theme.isTempo ? "[offline]" : "离线")
            } else if let last = state.lastSync {
                Text(theme.isTempo ? "[synced \(Self.hhmm(last))]" : "已同步 \(Self.hhmm(last))")
            }
        }
        .font(theme.small).foregroundStyle(state.offline ? theme.accent : theme.muted)
    }

    private func taskRow(_ t: Task) -> some View {
        let user = state.user ?? User(id: "", name: "", admin: true)
        let tickable = TodayFilter.canComplete(t, as: user) && !state.busy.contains(t.id)
        return Button {
            Concurrency.Task { await state.complete(t) }
        } label: {
            HStack(alignment: .top, spacing: 8) {
                ThemedCheck(theme: theme, checked: !t.isOpen, enabled: tickable).padding(.top, 2)
                VStack(alignment: .leading, spacing: 1) {
                    Text(t.name + (t.stackCount > 1 ? " · \(t.stackCount) 期" : ""))
                        .strikethrough(!t.isOpen, color: theme.muted)
                        .foregroundStyle(t.isOpen ? theme.ink : theme.muted)
                    Text(subtitle(t)).font(theme.small).foregroundStyle(theme.muted)
                }
                Spacer(minLength: 0)
                if state.busy.contains(t.id) { ProgressView().controlSize(.small) }
            }
            .padding(.vertical, 4).padding(.horizontal, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!tickable)
        .help(tickable ? "标记完成" : (t.isOpen ? "只能记录自己的任务" : statusText(t)))
    }

    private func subtitle(_ t: Task) -> String {
        var parts: [String] = []
        if let time = t.time { parts.append(time) }
        if state.user?.admin == true, let who = state.snapshot?.user, who.id != t.assignee { parts.append(t.assignee) }
        parts.append(t.projectId.flatMap { id in state.snapshot?.projects.first { $0.id == id }?.name } ?? "个人")
        parts.append(t.priority)
        if !t.isOpen { parts.append(statusText(t)) }
        return parts.joined(separator: " · ")
    }

    private func statusText(_ t: Task) -> String {
        switch t.status {
        case "done": return "已完成"
        case "pending_approval", "awaiting_approval", "submitted": return "待审批"
        case "doing": return "进行中"
        default: return "待开始"
        }
    }

    // MARK: Servers

    /// The current server's host; opens the remembered list and 连接新服务器.
    private var serverMenu: some View {
        Menu {
            ForEach(state.servers) { s in
                Button((s.url == state.account ? "✓ " : "") + "\(s.name) · \(s.host)") { state.switchTo(s) }
            }
            Divider()
            Button("连接新服务器…") { state.showConnectScreen() }
            if let current = state.servers.first(where: { $0.url == state.account }) {
                Button("忘记 \(current.host)") { state.forget(current) }
            }
        } label: {
            Text(theme.isTempo ? "[\(state.server?.host ?? "")]" : "· \(state.server?.host ?? "")")
                .font(theme.small).foregroundStyle(theme.muted)
        }
        .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize()
        .help("切换服务器")
    }

    private var connectSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(theme.heading("连接服务器")).font(theme.isTempo ? theme.title : .system(size: 13, weight: .semibold, design: .serif))
            Text("填你们团队哗啦待办的网址。在网页版点头像 →「连接桌面 App」可以自动填好。")
                .font(theme.small).foregroundStyle(theme.muted).fixedSize(horizontal: false, vertical: true)
            HStack {
                TextField("", text: $state.serverText, prompt: Text("work.example.com 或 192.168.1.8:4173"))
                    .textFieldStyle(.plain).font(theme.body).autocorrectionDisabled()
                    .padding(6).background(fieldBackground)
                    .onSubmit { connect() }
                Button(theme.bracket(state.connecting ? "连接中…" : "连接")) { connect() }
                    .buttonStyle(ThemedButtonStyle(theme: theme, primary: true))
                    .disabled(state.connecting || state.serverText.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            if let n = state.notice {
                Text(theme.isTempo ? "> \(n)" : n).font(theme.small).foregroundStyle(theme.accent)
                    .lineLimit(3).fixedSize(horizontal: false, vertical: true)
            }
            if !state.servers.isEmpty {
                Text(theme.isTempo ? "-- 用过的服务器 --" : "用过的服务器").font(theme.small).foregroundStyle(theme.muted).padding(.top, 2)
                ForEach(state.servers) { s in
                    HStack(spacing: 6) {
                        Button { state.switchTo(s) } label: {
                            VStack(alignment: .leading, spacing: 1) {
                                Text(s.host)
                                Text(s.username.isEmpty ? s.name : "\(s.name) · \(s.username)").font(theme.small).foregroundStyle(theme.muted)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        Button(theme.isTempo ? "[x]" : "移除") { state.forget(s) }
                            .buttonStyle(.plain).font(theme.small).foregroundStyle(theme.muted)
                            .help("忘记这台服务器和它的登录状态")
                    }
                    .padding(.vertical, 3)
                }
            }
            if state.server != nil {
                Button(theme.bracket("取消")) { state.cancelConnect() }.buttonStyle(ThemedButtonStyle(theme: theme))
            }
        }
    }

    private func connect() { Concurrency.Task { await state.connect() } }

    // MARK: Sign in

    private var signInSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(theme.heading("登录工作台")).font(theme.isTempo ? theme.title : .system(size: 13, weight: .semibold, design: .serif))
            HStack {
                Text(theme.isTempo ? "服务器:" : "服务器").font(theme.small).foregroundStyle(theme.muted).frame(width: 64, alignment: .leading)
                Text(state.server?.host ?? "").lineLimit(1).truncationMode(.middle)
                Spacer()
                Button(theme.isTempo ? "[更换]" : "更换") { state.showConnectScreen() }
                    .buttonStyle(.plain).font(theme.small).foregroundStyle(theme.accent)
            }
            if state.server?.exposesPassword == true {
                Text("这台服务器没有用 https，密码会以明文在网上传输。请让管理员开启 https。")
                    .font(theme.small).foregroundStyle(theme.accent).fixedSize(horizontal: false, vertical: true)
            }
            field("账户", text: $state.username, prompt: "用户名")
            HStack {
                Text(theme.isTempo ? "密码:" : "密码").font(theme.small).foregroundStyle(theme.muted).frame(width: 64, alignment: .leading)
                SecureField("", text: $password, prompt: Text("密码"))
                    .textFieldStyle(.plain).font(theme.body)
                    .padding(6).background(fieldBackground)
                    .onSubmit { submit() }
            }
            HStack {
                Button(theme.bracket(state.syncing ? "登录中…" : "登录")) { submit() }
                    .buttonStyle(ThemedButtonStyle(theme: theme, primary: true))
                    .disabled(state.syncing)
                Spacer()
                if let n = state.notice { Text(n).font(theme.small).foregroundStyle(theme.accent).lineLimit(2) }
            }
            Text("登录状态只保存在系统钥匙串里，不写入任何文件。").font(theme.small).foregroundStyle(theme.muted)
        }
    }

    private func submit() {
        let pw = password
        Concurrency.Task { await state.signIn(password: pw); if state.signedIn { password = "" } }
    }

    private func field(_ label: String, text: Binding<String>, prompt: String) -> some View {
        HStack {
            Text(theme.isTempo ? "\(label):" : label).font(theme.small).foregroundStyle(theme.muted).frame(width: 64, alignment: .leading)
            TextField("", text: text, prompt: Text(prompt))
                .textFieldStyle(.plain).font(theme.body)
                .autocorrectionDisabled()
                .padding(6).background(fieldBackground)
        }
    }

    private var fieldBackground: some View {
        RoundedRectangle(cornerRadius: theme.isTempo ? 0 : 6).fill(Color.white.opacity(0.6))
            .overlay(RoundedRectangle(cornerRadius: theme.isTempo ? 0 : 6).stroke(theme.line))
    }

    // MARK: Footer

    private var footer: some View {
        VStack(alignment: .leading, spacing: 6) {
            Rectangle().fill(theme.line).frame(height: 1)
            HStack(spacing: 10) {
                Picker("", selection: $state.style) {
                    ForEach(Style.allCases) { s in Text(theme.isTempo ? "[\(s.label)]" : s.label).tag(s) }
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: 150)
                Spacer()
                Toggle(isOn: Binding(get: { state.launchAtLogin }, set: { state.setLaunchAtLogin($0) })) {
                    Text(theme.isTempo ? "[自启]" : "登录时启动").font(theme.small)
                }
                .toggleStyle(.checkbox).controlSize(.small)
            }
            if let e = state.launchAtLoginError {
                Text("无法设置开机自启：\(e)").font(theme.small).foregroundStyle(theme.accent).lineLimit(2)
            }
            HStack {
                if state.signedIn {
                    Button(theme.bracket("退出登录")) { state.signOut() }.buttonStyle(ThemedButtonStyle(theme: theme))
                    Button(theme.bracket("立即同步")) { Concurrency.Task { await state.refresh() } }.buttonStyle(ThemedButtonStyle(theme: theme))
                }
                Spacer()
                Button(theme.bracket("退出")) { NSApp.terminate(nil) }.buttonStyle(ThemedButtonStyle(theme: theme))
            }
        }
    }

    static func hhmm(_ d: Date) -> String {
        let f = DateFormatter(); f.dateFormat = "HH:mm"; return f.string(from: d)
    }
}
