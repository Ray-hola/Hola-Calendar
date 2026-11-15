import Foundation
import HolaCore

// holactl — a tiny command-line client for a 哗啦待办 server. Used by the
// integration test and handy for checking a server from a terminal.
//
//   holactl login  <server> <user>      password from $HOLA_PASSWORD or a prompt
//   holactl today  <server>             today's to-dos for the signed-in user
//   holactl complete <server> <taskId>  tick a to-do
//   holactl logout <server>
//
// The token is kept in the Keychain unless $HOLA_TOKEN is set (tests) or
// `--print-token` is passed to `login`, which prints it instead of storing it.

func fail(_ msg: String) -> Never { FileHandle.standardError.write(Data((msg + "\n").utf8)); exit(1) }

let args = Array(CommandLine.arguments.dropFirst())
guard args.count >= 2, let base = HolaAPI.normalize(args[1]) else {
    fail("usage: holactl login|today|complete|logout <server> [user|taskId] [--print-token]")
}
let api = HolaAPI(baseURL: base)
let account = base.absoluteString
func token() -> String {
    if let t = ProcessInfo.processInfo.environment["HOLA_TOKEN"], !t.isEmpty { return t }
    guard let t = Keychain.load(account: account) else { fail("not signed in to \(account); run: holactl login \(args[1]) <user>") }
    return t
}

let semaphore = DispatchSemaphore(value: 0)
_Concurrency.Task {
    defer { semaphore.signal() }
    do {
        switch args[0] {
        case "login":
            guard args.count >= 3 else { fail("login needs <server> <user>") }
            var password = ProcessInfo.processInfo.environment["HOLA_PASSWORD"] ?? ""
            if password.isEmpty, let p = String(validatingUTF8: getpass("密码 password: ")) { password = p }
            let t = try await api.login(username: args[2], password: password)
            if args.contains("--print-token") { print(t) } else {
                try Keychain.save(token: t, account: account); print("signed in; token stored in Keychain")
            }
        case "today":
            let snap = try await api.state(token: token())
            let tasks = TodayFilter.tasks(in: snap)
            print("\(snap.user.name) · \(snap.today) · 还剩 \(TodayFilter.openCount(tasks)) 项")
            for t in tasks { print("\(t.isOpen ? "[ ]" : "[x]") \(t.id)  \(t.priority)  \(t.name)\(t.stackCount > 1 ? " · \(t.stackCount) 期" : "")") }
        case "complete":
            guard args.count >= 3 else { fail("complete needs <server> <taskId>") }
            let tok = token()
            let snap = try await api.state(token: tok)
            guard let t = snap.tasks.first(where: { $0.id == args[2] }) else { fail("no such task \(args[2])") }
            try await api.complete(t, token: tok)
            print("completed 「\(t.name)」")
        case "logout":
            try await api.logout(token: token())
            Keychain.delete(account: account)
            print("signed out")
        default: fail("unknown command \(args[0])")
        }
    } catch {
        fail("error: \(error.localizedDescription)")
    }
}
semaphore.wait()
