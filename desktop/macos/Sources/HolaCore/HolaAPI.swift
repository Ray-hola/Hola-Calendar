import Foundation

public enum APIError: Error, Equatable, LocalizedError {
    /// Network layer failure: no route, DNS, refused, timeout. The UI keeps the last list.
    case offline(String)
    /// 401 from the server: the session is gone, sign in again.
    case unauthorized(String)
    /// Any other non-200 answer, with the server's own `error` text (403/409/429...).
    case server(Int, String)
    case badURL
    case badResponse

    public var errorDescription: String? {
        switch self {
        case .offline(let s): return s.isEmpty ? "无法连接服务器" : s
        case .unauthorized(let s): return s.isEmpty ? "请重新登录" : s
        case .server(_, let s): return s
        case .badURL: return "服务器地址无效"
        case .badResponse: return "服务器返回了无法识别的内容"
        }
    }
}

/// Talks to a self-hosted 哗啦待办 server exactly the way the browser does:
/// `wc_session` cookie, `X-Work-Calendar: 1` on every POST, JSON bodies.
/// The URLSession is ephemeral with cookie handling off, so the token never
/// lands in `~/Library/Cookies`; the caller keeps it in the Keychain.
public final class HolaAPI: @unchecked Sendable {
    public let baseURL: URL
    let session: URLSession

    public init(baseURL: URL, session: URLSession? = nil) {
        self.baseURL = baseURL
        if let session { self.session = session } else {
            let c = URLSessionConfiguration.ephemeral
            c.httpCookieAcceptPolicy = .never
            c.httpShouldSetCookies = false
            c.timeoutIntervalForRequest = 15
            c.waitsForConnectivity = false
            self.session = URLSession(configuration: c)
        }
    }

    /// Normalises what a person types into a server URL: adds `http://` when
    /// there is no scheme, strips a trailing slash and any path.
    public static func normalize(_ text: String) -> URL? {
        var s = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return nil }
        if !s.contains("://") { s = "http://" + s }
        guard var comps = URLComponents(string: s), let host = comps.host, !host.isEmpty else { return nil }
        comps.path = ""; comps.query = nil; comps.fragment = nil
        return comps.url
    }

    // MARK: - Calls

    /// Returns the session token found in `Set-Cookie: wc_session=...`.
    public func login(username: String, password: String) async throws -> String {
        let (data, http) = try await send("POST", "/api/login", token: nil,
                                          body: ["username": username, "password": password])
        try Self.check(http, data)
        guard let token = Self.sessionToken(from: http) else { throw APIError.badResponse }
        return token
    }

    public func state(token: String) async throws -> Snapshot {
        let (data, http) = try await send("GET", "/api/state", token: token, body: nil)
        try Self.check(http, data)
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw APIError.badResponse }
        return try Snapshot(json: obj)
    }

    /// The same payload the web widget's `pipComplete` posts.
    public func complete(_ task: Task, token: String) async throws {
        var payload: [String: Any] = ["id": task.id, "summary": "完成任务",
                                      "actual": task.duration > 0 ? task.duration : 30, "feeling": "顺利"]
        if task.stackCount > 1 { payload["stackPeriods"] = task.stackPeriods }
        try await action("task.complete", data: payload, token: token)
    }

    public func action(_ name: String, data: [String: Any], token: String) async throws {
        let (body, http) = try await send("POST", "/api/action", token: token, body: ["action": name, "data": data])
        try Self.check(http, body)
    }

    public func logout(token: String) async throws {
        let (data, http) = try await send("POST", "/api/logout", token: token, body: [:])
        try Self.check(http, data)
    }

    public func health() async throws -> Bool {
        let (data, http) = try await send("GET", "/api/health", token: nil, body: nil)
        try Self.check(http, data)
        return true
    }

    // MARK: - Plumbing

    func send(_ method: String, _ path: String, token: String?, body: [String: Any]?) async throws -> (Data, HTTPURLResponse) {
        guard let url = URL(string: path, relativeTo: baseURL) else { throw APIError.badURL }
        var req = URLRequest(url: url)
        req.httpMethod = method
        req.setValue("1", forHTTPHeaderField: "X-Work-Calendar")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let token { req.setValue("wc_session=\(token)", forHTTPHeaderField: "Cookie") }
        if let body {
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        do {
            let (data, resp) = try await session.data(for: req)
            guard let http = resp as? HTTPURLResponse else { throw APIError.badResponse }
            return (data, http)
        } catch let e as APIError { throw e } catch let e as URLError {
            throw APIError.offline(e.localizedDescription)
        } catch {
            throw APIError.offline(error.localizedDescription)
        }
    }

    static func check(_ http: HTTPURLResponse, _ data: Data) throws {
        guard http.statusCode != 200 else { return }
        let msg = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["error"] as? String ?? ""
        if http.statusCode == 401 { throw APIError.unauthorized(msg) }
        throw APIError.server(http.statusCode, msg.isEmpty ? "服务器返回 \(http.statusCode)" : msg)
    }

    public static func sessionToken(from http: HTTPURLResponse) -> String? {
        // allHeaderFields folds repeated Set-Cookie headers into one string on
        // Apple platforms, so a regex is more reliable than HTTPCookie parsing.
        for (k, v) in http.allHeaderFields {
            guard let key = k as? String, key.caseInsensitiveCompare("Set-Cookie") == .orderedSame,
                  let value = v as? String else { continue }
            if let t = sessionToken(fromSetCookie: value) { return t }
        }
        return nil
    }

    public static func sessionToken(fromSetCookie value: String) -> String? {
        guard let r = value.range(of: #"wc_session=([^;,\s]*)"#, options: .regularExpression) else { return nil }
        let token = String(value[r].dropFirst("wc_session=".count))
        return token.isEmpty ? nil : token
    }
}
