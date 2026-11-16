import Foundation
import HolaCore

/// A URLProtocol stub so the client's wire format is checked without a server.
final class StubProtocol: URLProtocol, @unchecked Sendable {
    nonisolated(unsafe) static var handler: ((URLRequest) -> (Int, [String: String], Data))?
    nonisolated(unsafe) static var requests: [URLRequest] = []

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        StubProtocol.requests.append(request)
        guard let h = StubProtocol.handler else { client?.urlProtocol(self, didFailWithError: URLError(.cannotConnectToHost)); return }
        let (status, headers, body) = h(request)
        let resp = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!
        client?.urlProtocol(self, didReceive: resp, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() {}
}

func apiTests() async {
    func client() -> HolaAPI {
        let c = URLSessionConfiguration.ephemeral
        c.protocolClasses = [StubProtocol.self]
        c.httpShouldSetCookies = false
        StubProtocol.requests = []
        return HolaAPI(baseURL: URL(string: "http://hola.test:4173")!, session: URLSession(configuration: c))
    }
    func bodyJSON(_ r: URLRequest) -> [String: Any] {
        // URLProtocol sees the body as a stream, so read it back.
        guard let stream = r.httpBodyStream else { return [:] }
        stream.open(); defer { stream.close() }
        var data = Data(); var buf = [UInt8](repeating: 0, count: 4096)
        while stream.hasBytesAvailable { let n = stream.read(&buf, maxLength: buf.count); if n <= 0 { break }; data.append(buf, count: n) }
        return (try? JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
    }

    await test("normalizes typed server addresses") {
        check(HolaAPI.normalize("192.168.1.8:4173")?.absoluteString, "http://192.168.1.8:4173")
        check(HolaAPI.normalize(" https://todo.example.com/ ")?.absoluteString, "https://todo.example.com")
        check(HolaAPI.normalize("https://todo.example.com/index.html?x=1")?.absoluteString, "https://todo.example.com")
        check(HolaAPI.normalize("") == nil)
        check(HolaAPI.normalize("http://") == nil)
        check(HolaAPI.normalize("ftp://todo.example.com") == nil)
        check(HolaAPI.normalize("https://user:password@todo.example.com") == nil)
    }

    await test("login sends the X-Work-Calendar header, no Origin, and reads the cookie") {
        StubProtocol.handler = { _ in
            (200, ["Set-Cookie": "wc_session=abc.DEF-123; HttpOnly; SameSite=Strict; Path=/; Max-Age=604800",
                   "Content-Type": "application/json"], Data(#"{"ok":true}"#.utf8))
        }
        let api = client()
        let token = try await api.login(username: "ray", password: "pw")
        check(token, "abc.DEF-123")
        let r = StubProtocol.requests[0]
        check(r.url?.path, "/api/login")
        check(r.httpMethod, "POST")
        check(r.value(forHTTPHeaderField: "X-Work-Calendar"), "1")
        check(r.value(forHTTPHeaderField: "Origin") == nil)
        let body = bodyJSON(r)
        check(body["username"] as? String, "ray")
        check(body["password"] as? String, "pw")
    }

    await test("cookie parsing variants") {
        check(HolaAPI.sessionToken(fromSetCookie: "wc_session=tok; HttpOnly"), "tok")
        check(HolaAPI.sessionToken(fromSetCookie: "other=1, wc_session=tok2; Path=/"), "tok2")
        check(HolaAPI.sessionToken(fromSetCookie: "wc_session=; Max-Age=0") == nil)
        check(HolaAPI.sessionToken(fromSetCookie: "nothing=here") == nil)
    }

    await test("state sends the cookie and decodes") {
        StubProtocol.handler = { r in
            guard r.value(forHTTPHeaderField: "Cookie") == "wc_session=tok" else { return (401, [:], Data(#"{"error":"请登录工作台"}"#.utf8)) }
            return (200, [:], Data(#"{"user":{"id":"ray","name":"Ray","admin":false},"today":"2026-11-15","tasks":[{"id":"t","name":"n","assignee":"ray","date":"2026-11-15"}],"projects":[]}"#.utf8))
        }
        let snap = try await client().state(token: "tok")
        check(snap.user.id, "ray")
        check(snap.tasks.count, 1)
        check(StubProtocol.requests[0].httpMethod, "GET")
    }

    await test("complete posts the web widget's payload") {
        StubProtocol.handler = { _ in (200, [:], Data(#"{"id":"t"}"#.utf8)) }
        let api = client()
        let t = Task(id: "t", name: "n", assignee: "ray", date: "2026-11-15", duration: 45, stackCount: 2, stackPeriods: ["a", "b"])
        try await api.complete(t, token: "tok")
        let body = bodyJSON(StubProtocol.requests[0])
        check(body["action"] as? String, "task.complete")
        let data = body["data"] as? [String: Any] ?? [:]
        check(data["id"] as? String, "t")
        check(data["summary"] as? String, "完成任务")
        check(data["actual"] as? Int, 45)
        check(data["feeling"] as? String, "顺利")
        check(data["stackPeriods"] as? [String], ["a", "b"])

        StubProtocol.requests = []
        try await api.complete(Task(id: "s", name: "n", assignee: "ray", date: "2026-11-15", duration: 0), token: "tok")
        let d2 = bodyJSON(StubProtocol.requests[0])["data"] as? [String: Any] ?? [:]
        check(d2["actual"] as? Int, 30)
        check(d2["stackPeriods"] == nil)
    }

    await test("maps server refusals to typed errors with the server's text") {
        StubProtocol.handler = { _ in (403, [:], Data(#"{"error":"只能记录自己的任务"}"#.utf8)) }
        await checkThrows(APIError.server(403, "只能记录自己的任务")) {
            try await client().complete(Task(id: "t", name: "n", assignee: "x", date: "2026-11-15"), token: "tok")
        }
        StubProtocol.handler = { _ in (401, [:], Data(#"{"error":"请登录工作台"}"#.utf8)) }
        await checkThrows(APIError.unauthorized("请登录工作台")) { _ = try await client().state(token: "stale") }
        StubProtocol.handler = { _ in (409, [:], Data(#"{"error":"本次任务已记录，不能重复提交"}"#.utf8)) }
        await checkThrows(APIError.server(409, "本次任务已记录，不能重复提交")) { try await client().action("task.complete", data: [:], token: "tok") }
        StubProtocol.handler = { _ in (500, [:], Data()) }
        await checkThrows(APIError.server(500, "服务器返回 500")) { _ = try await client().health() }
    }

    await test("a network failure is reported as offline") {
        StubProtocol.handler = nil
        do { _ = try await client().state(token: "tok"); check(false, "expected offline") } catch let e as APIError {
            if case .offline = e { check(true) } else { check(false, "expected offline, got \(e)") }
        }
    }
}
