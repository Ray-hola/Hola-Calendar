import Foundation

/// What `GET /api/info` says about a server before anyone signs in.
public struct ServerInfo: Equatable, Sendable {
    public var name: String
    public var api: Int
    /// A server from before /api/info answered /api/health instead.
    public var legacy: Bool

    public init(name: String, api: Int, legacy: Bool = false) {
        self.name = name; self.api = api; self.legacy = legacy
    }

    /// The newest server API this app speaks.
    public static let supportedAPI = 1
}

/// Turning what a person types into the addresses worth trying.
public enum ServerAddress {
    /// A typed scheme is kept as is. Without one, a public host is tried over
    /// https first so a password never goes out in clear by default; a LAN
    /// address is tried over http first, which is how such servers usually run.
    public static func candidates(for text: String) -> [URL] {
        let typed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if typed.contains("://") { return HolaAPI.normalize(typed).map { [$0] } ?? [] }
        guard let http = HolaAPI.normalize("http://" + typed), let https = HolaAPI.normalize("https://" + typed) else { return [] }
        return isPrivate(http.host ?? "") ? [http, https] : [https, http]
    }

    /// Loopback, private IPv4 ranges, link-local, `.local` names and bare
    /// machine names: places a password over http stays inside the network.
    public static func isPrivate(_ host: String) -> Bool {
        var h = host.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "[]"))
        if h.hasPrefix("::ffff:") && h.contains(".") { h = String(h.dropFirst(7)) }   // IPv4 written as IPv6
        if h == "localhost" || h.hasSuffix(".localhost") || h.hasSuffix(".local") { return true }
        if h.contains(":") {
            // IPv6: loopback, unique local fc00::/7, link-local fe80::/10.
            if h == "::1" { return true }
            guard let first = h.split(separator: ":").first, let word = UInt16(first, radix: 16) else { return false }
            return word & 0xfe00 == 0xfc00 || word & 0xffc0 == 0xfe80
        }
        let labels = h.split(separator: ".", omittingEmptySubsequences: false)
        let parts = labels.compactMap { Int($0) }
        if labels.count == 4, parts.count == 4, parts.allSatisfy({ (0...255).contains($0) }) {
            switch (parts[0], parts[1]) {
            case (10, _), (127, _), (192, 168), (169, 254): return true
            case (172, let b) where (16...31).contains(b): return true
            case (100, let b) where (64...127).contains(b): return true   // CGNAT, used by Tailscale
            default: return false
            }
        }
        // A bare machine name is on this network; a bare number is an IPv4 address in disguise.
        return labels.count == 1 && !h.isEmpty && Int(h) == nil
    }

    /// A password sent here could be read on the way: plain http to a public host.
    public static func exposesPassword(_ url: URL) -> Bool {
        url.scheme == "http" && !isPrivate(url.host ?? "")
    }
}

/// `hola://connect?server=https://work.example.com`, the link the web app's
/// 「连接桌面 App」 opens. It only ever fills in an address; the person still
/// confirms with 连接 and signs in themselves.
public enum ConnectLink {
    public static let scheme = "hola"

    public static func server(from link: URL) -> URL? {
        guard link.scheme?.lowercased() == scheme, link.host?.lowercased() == "connect",
              let raw = URLComponents(url: link, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "server" })?.value,
              raw.contains("://") else { return nil }
        return HolaAPI.normalize(raw)
    }
}

/// A server this Mac has connected to, remembered so switching is one click.
/// The session token is never here: it stays in the Keychain under `url`.
public struct SavedServer: Codable, Equatable, Identifiable, Sendable {
    public var url: String
    public var name: String
    public var username: String
    public var id: String { url }

    public init(url: String, name: String, username: String) {
        self.url = url; self.name = name; self.username = username
    }

    /// The address as people say it: host, and the port when there is one.
    public var host: String {
        guard let u = URL(string: url), let h = u.host else { return url }
        return u.port.map { "\(h):\($0)" } ?? h
    }
}

public enum ServerList {
    public static let limit = 8

    /// `server` moves to the front; an older entry for the same address is replaced.
    /// `keeping` (the server in use) is never the one pushed off the end.
    public static func remember(_ server: SavedServer, in list: [SavedServer], keeping: String? = nil) -> [SavedServer] {
        let all = [server] + list.filter { $0.url != server.url }
        guard let keeping, let kept = all.dropFirst(limit).first(where: { $0.url == keeping }) else {
            return Array(all.prefix(limit))
        }
        return Array(all.prefix(limit - 1)) + [kept]
    }

    public static func forget(_ url: String, in list: [SavedServer]) -> [SavedServer] {
        list.filter { $0.url != url }
    }

    public static func decode(_ data: Data?) -> [SavedServer] {
        data.flatMap { try? JSONDecoder().decode([SavedServer].self, from: $0) } ?? []
    }

    public static func encode(_ list: [SavedServer]) -> Data? { try? JSONEncoder().encode(list) }
}

extension HolaAPI {
    /// Asks the server what it is. A server older than /api/info is accepted
    /// when it answers /api/health; anything else is not a 哗啦待办 server.
    public func info() async throws -> ServerInfo {
        let (data, http) = try await send("GET", "/api/info", token: nil, body: nil)
        if http.statusCode == 404 {
            // Any website says 404 here; an older 哗啦待办 also answers {"ok":true} at /api/health.
            let (data, http) = try await send("GET", "/api/health", token: nil, body: nil)
            guard http.statusCode == 200, let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  obj["ok"] as? Bool == true else { throw APIError.notHola }
            return ServerInfo(name: "哗啦待办", api: 1, legacy: true)
        }
        try Self.check(http, data)
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              obj["product"] as? String == "hola-calendar" else { throw APIError.notHola }
        let api = (obj["api"] as? Int) ?? 1
        guard api <= ServerInfo.supportedAPI else { throw APIError.incompatible(api) }
        return ServerInfo(name: (obj["name"] as? String) ?? "哗啦待办", api: api)
    }

    /// Tries each candidate address in order and returns the first that is a
    /// 哗啦待办 server. When none is, the most telling failure is reported:
    /// "not a 哗啦待办 server" or "too new" beats "unreachable".
    public static func connect(_ text: String, session: URLSession? = nil) async throws -> (HolaAPI, ServerInfo) {
        let urls = ServerAddress.candidates(for: text)
        guard !urls.isEmpty else { throw APIError.badURL }
        var best: APIError?
        for url in urls {
            let api = HolaAPI(baseURL: url, session: session)
            do { return (api, try await api.info()) } catch let e as APIError {
                if best == nil || e.rank > best!.rank { best = e }
            }
        }
        throw best ?? APIError.offline("")
    }
}

extension APIError {
    /// How much a failure says about the address, for picking which one to show.
    var rank: Int {
        switch self {
        case .incompatible: return 4
        case .notHola, .badResponse: return 3
        case .server, .unauthorized: return 2
        case .offline, .badURL: return 1
        }
    }
}
