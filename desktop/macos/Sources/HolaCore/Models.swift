import Foundation

/// The slice of `/api/state` the companion needs. Decoded leniently from the
/// raw JSON object so a new server field never breaks an old client.
public struct Snapshot: Equatable, Sendable {
    public var user: User
    public var today: String
    public var tasks: [Task]
    public var projects: [Project]

    public init(user: User, today: String, tasks: [Task], projects: [Project]) {
        self.user = user; self.today = today; self.tasks = tasks; self.projects = projects
    }

    public init(json: [String: Any]) throws {
        guard let u = json["user"] as? [String: Any], let user = User(json: u),
              let today = json["today"] as? String else { throw APIError.badResponse }
        self.user = user
        self.today = today
        self.tasks = ((json["tasks"] as? [[String: Any]]) ?? []).compactMap(Task.init(json:))
        self.projects = ((json["projects"] as? [[String: Any]]) ?? []).compactMap(Project.init(json:))
    }
}

public struct User: Equatable, Sendable {
    public var id: String
    public var name: String
    public var admin: Bool

    public init(id: String, name: String, admin: Bool) { self.id = id; self.name = name; self.admin = admin }

    init?(json: [String: Any]) {
        guard let id = json["id"] as? String else { return nil }
        self.id = id
        self.name = (json["name"] as? String) ?? id
        self.admin = (json["admin"] as? Bool) ?? false
    }
}

public struct Project: Equatable, Sendable {
    public var id: String
    public var name: String
    public var status: String

    public init(id: String, name: String, status: String) { self.id = id; self.name = name; self.status = status }

    init?(json: [String: Any]) {
        guard let id = json["id"] as? String else { return nil }
        self.id = id
        self.name = (json["name"] as? String) ?? ""
        self.status = (json["status"] as? String) ?? "active"
    }
}

public struct Task: Equatable, Identifiable, Sendable {
    public var id: String
    public var name: String
    public var assignee: String
    public var date: String
    public var deadline: String?
    public var time: String?
    public var duration: Int
    public var priority: String
    public var status: String
    public var projectId: String?
    public var superseded: Bool
    public var stackCount: Int
    public var stackPeriods: [String]

    public init(id: String, name: String, assignee: String, date: String, deadline: String? = nil,
                time: String? = nil, duration: Int = 30, priority: String = "P1", status: String = "todo",
                projectId: String? = nil, superseded: Bool = false, stackCount: Int = 1, stackPeriods: [String] = []) {
        self.id = id; self.name = name; self.assignee = assignee; self.date = date; self.deadline = deadline
        self.time = time; self.duration = duration; self.priority = priority; self.status = status
        self.projectId = projectId; self.superseded = superseded; self.stackCount = stackCount; self.stackPeriods = stackPeriods
    }

    init?(json: [String: Any]) {
        guard let id = json["id"] as? String, let name = json["name"] as? String,
              let date = json["date"] as? String else { return nil }
        self.id = id
        self.name = name
        self.assignee = (json["assignee"] as? String) ?? ""
        self.date = date
        self.deadline = (json["deadline"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        self.time = (json["time"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        self.duration = Task.number(json["duration"]) ?? 30
        self.priority = (json["priority"] as? String) ?? "P1"
        self.status = (json["status"] as? String) ?? "todo"
        self.projectId = (json["projectId"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        self.superseded = (json["superseded"] as? Bool) ?? false
        self.stackCount = Task.number(json["stackCount"]) ?? 1
        self.stackPeriods = (json["stackPeriods"] as? [String]) ?? []
    }

    static func number(_ v: Any?) -> Int? {
        if let n = v as? Int { return n }
        if let d = v as? Double { return Int(d) }
        if let s = v as? String, let n = Int(s) { return n }
        return nil
    }

    /// Same as the web app's `taskEnd`: a deadline later than the start day extends the task.
    public var end: String { (deadline.map { $0 > date } ?? false) ? deadline! : date }
    public var isOpen: Bool { status == "todo" || status == "doing" }
}
