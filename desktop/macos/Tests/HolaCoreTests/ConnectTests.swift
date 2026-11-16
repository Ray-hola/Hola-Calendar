import Foundation
import HolaCore

func connectTests() async {
    func stubSession() -> URLSession {
        let c = URLSessionConfiguration.ephemeral
        c.protocolClasses = [StubProtocol.self]
        c.httpShouldSetCookies = false
        StubProtocol.requests = []
        return URLSession(configuration: c)
    }
    let info = Data(#"{"product":"hola-calendar","name":"哗啦待办","api":1}"#.utf8)

    await test("a public host is tried over https first, a LAN host over http first") {
        check(ServerAddress.candidates(for: "work.example.com").map(\.absoluteString), ["https://work.example.com", "http://work.example.com"])
        check(ServerAddress.candidates(for: "192.168.1.8:4173").map(\.absoluteString), ["http://192.168.1.8:4173", "https://192.168.1.8:4173"])
        check(ServerAddress.candidates(for: "nas:4173").first?.scheme, "http")
        check(ServerAddress.candidates(for: "macbook.local").first?.scheme, "http")
        check(ServerAddress.candidates(for: " http://work.example.com/x ").map(\.absoluteString), ["http://work.example.com"])
        check(ServerAddress.candidates(for: "https://192.168.1.8").map(\.absoluteString), ["https://192.168.1.8"])
        check(ServerAddress.candidates(for: "").isEmpty)
        check(ServerAddress.candidates(for: "ftp://x.com").isEmpty)
    }

    await test("private addresses are told apart from public ones") {
        for h in ["localhost", "127.0.0.1", "10.0.0.5", "172.16.0.1", "172.31.255.1", "192.168.0.10", "169.254.1.1", "nas", "mac.local", "::1", "fe80::1", "fd12::1"] {
            check(ServerAddress.isPrivate(h), "\(h) should be private")
        }
        for h in ["work.ovenray.cn", "8.8.8.8", "172.32.0.1", "192.169.0.1", "2001:db8::1", "1.2.3"] {
            check(!ServerAddress.isPrivate(h), "\(h) should be public")
        }
        check(ServerAddress.exposesPassword(URL(string: "http://work.example.com")!))
        check(!ServerAddress.exposesPassword(URL(string: "https://work.example.com")!))
        check(!ServerAddress.exposesPassword(URL(string: "http://192.168.1.8:4173")!))
    }

    await test("connect reads /api/info and keeps the address that answered") {
        StubProtocol.handler = { r in
            r.url?.scheme == "https" && r.url?.path == "/api/info" ? (200, [:], info) : (404, [:], Data())
        }
        let (api, got) = try await HolaAPI.connect("work.example.com", session: stubSession())
        check(api.baseURL.absoluteString, "https://work.example.com")
        check(got, ServerInfo(name: "哗啦待办", api: 1))
        check(StubProtocol.requests.first?.value(forHTTPHeaderField: "Cookie") == nil, "no session goes to a server not yet signed in to")
    }

    await test("connect falls back to http when https does not answer as 哗啦待办") {
        StubProtocol.handler = { r in
            if r.url?.scheme == "https" { return (502, [:], Data()) }
            return (200, [:], info)
        }
        let (api, _) = try await HolaAPI.connect("work.example.com", session: stubSession())
        check(api.baseURL.absoluteString, "http://work.example.com")
    }

    await test("an older server without /api/info is accepted through /api/health") {
        StubProtocol.handler = { r in
            r.url?.path == "/api/health" ? (200, [:], Data(#"{"ok":true,"timezone":"Asia/Shanghai"}"#.utf8)) : (404, [:], Data(#"{"error":"页面不存在"}"#.utf8))
        }
        let (_, got) = try await HolaAPI.connect("https://old.example.com", session: stubSession())
        check(got.legacy)
        check(got.api, 1)
    }

    await test("a website that is not 哗啦待办 is refused, even one that answers 200 to everything") {
        StubProtocol.handler = { _ in (404, [:], Data("<html>Not Found</html>".utf8)) }
        await checkThrows(APIError.notHola) { _ = try await HolaAPI.connect("https://example.com", session: stubSession()) }
        StubProtocol.handler = { _ in (200, ["Content-Type": "text/html"], Data("<html>hello</html>".utf8)) }
        await checkThrows(APIError.notHola) { _ = try await HolaAPI.connect("https://example.com", session: stubSession()) }
        StubProtocol.handler = { _ in (200, [:], Data(#"{"product":"something-else","api":1}"#.utf8)) }
        await checkThrows(APIError.notHola) { _ = try await HolaAPI.connect("https://example.com", session: stubSession()) }
    }

    await test("a server newer than the app is refused with a clear reason") {
        StubProtocol.handler = { _ in (200, [:], Data(#"{"product":"hola-calendar","name":"哗啦待办","api":2}"#.utf8)) }
        await checkThrows(APIError.incompatible(2)) { _ = try await HolaAPI.connect("https://next.example.com", session: stubSession()) }
    }

    await test("the most telling failure wins when no address works") {
        // https: a plain website; http: unreachable. "Not 哗啦待办" says more than "offline".
        StubProtocol.handler = { r in r.url?.scheme == "https" ? (404, [:], Data()) : (200, [:], Data("<html>".utf8)) }
        await checkThrows(APIError.notHola) { _ = try await HolaAPI.connect("work.example.com", session: stubSession()) }
        StubProtocol.handler = nil
        do { _ = try await HolaAPI.connect("work.example.com", session: stubSession()); check(false, "expected offline") } catch let e as APIError {
            if case .offline = e { check(true) } else { check(false, "expected offline, got \(e)") }
        }
        await checkThrows(APIError.badURL) { _ = try await HolaAPI.connect("   ", session: stubSession()) }
    }

    await test("connect links only ever carry a server address") {
        check(ConnectLink.server(from: URL(string: "hola://connect?server=https%3A%2F%2Fwork.example.com")!)?.absoluteString, "https://work.example.com")
        check(ConnectLink.server(from: URL(string: "hola://connect?server=http://192.168.1.8:4173/index.html")!)?.absoluteString, "http://192.168.1.8:4173")
        check(ConnectLink.server(from: URL(string: "hola://connect?server=work.example.com")!) == nil, "a scheme is required")
        check(ConnectLink.server(from: URL(string: "hola://login?server=https://work.example.com")!) == nil)
        check(ConnectLink.server(from: URL(string: "https://connect?server=https://work.example.com")!) == nil)
        check(ConnectLink.server(from: URL(string: "hola://connect?server=javascript:alert(1)")!) == nil)
        check(ConnectLink.server(from: URL(string: "hola://connect?server=https://u:p@work.example.com")!) == nil)
        check(ConnectLink.server(from: URL(string: "hola://connect")!) == nil)
    }

    await test("remembered servers: newest first, one entry per address, bounded") {
        let a = SavedServer(url: "https://a.example.com", name: "A", username: "ray")
        let b = SavedServer(url: "http://192.168.1.8:4173", name: "B", username: "ray")
        var list = ServerList.remember(a, in: [])
        list = ServerList.remember(b, in: list)
        check(list.map(\.url), [b.url, a.url])
        list = ServerList.remember(SavedServer(url: a.url, name: "A", username: "chen"), in: list)
        check(list.map(\.url), [a.url, b.url])
        check(list[0].username, "chen")
        check(ServerList.forget(b.url, in: list).map(\.url), [a.url])
        var many: [SavedServer] = []
        for i in 0..<12 { many = ServerList.remember(SavedServer(url: "https://s\(i).example.com", name: "", username: ""), in: many) }
        check(many.count, ServerList.limit)
        check(many.first?.url, "https://s11.example.com")
        check(ServerList.decode(ServerList.encode(list)), list)
        check(ServerList.decode(Data("not json".utf8)).isEmpty)
        check(ServerList.decode(nil).isEmpty)
        check(a.host, "a.example.com")
        check(b.host, "192.168.1.8:4173")
    }
}
