import Foundation

/// A port of the web widget's `renderPip` selection, so the menu bar shows
/// exactly what the browser's 桌面小组件 shows for the same account.
public enum TodayFilter {
    static let archived: Set<String> = ["deferred", "skipped", "rejected"]
    static let hiddenProject: Set<String> = ["done", "archived"]
    static let rank: [String: Int] = ["P0": 0, "P1": 1, "P2": 2]

    /// Today's tasks, open ones first, then P0 < P1 < P2, then by name.
    public static func tasks(in s: Snapshot) -> [Task] {
        let hidden = Set(s.projects.filter { hiddenProject.contains($0.status) }.map(\.id))
        let visible = s.tasks.filter { t in
            !t.superseded
                && (t.projectId == nil || !hidden.contains(t.projectId!))
                && (s.user.admin || t.assignee == s.user.id)
                && t.date <= s.today && s.today <= t.end
                && !archived.contains(t.status)
        }
        return visible.sorted { a, b in
            if a.isOpen != b.isOpen { return a.isOpen }
            let ra = rank[a.priority] ?? 3, rb = rank[b.priority] ?? 3
            if ra != rb { return ra < rb }
            return a.name.compare(b.name, locale: Locale(identifier: "zh")) == .orderedAscending
        }
    }

    /// Whether the signed-in account may tick this task: the server's `performer`
    /// check, mirrored so the box is disabled instead of producing a 403.
    public static func canComplete(_ t: Task, as user: User) -> Bool {
        !user.admin && t.assignee == user.id && t.isOpen
    }

    /// The task the Touch Bar and the island call "next": the first open one.
    public static func next(in tasks: [Task]) -> Task? { tasks.first(where: \.isOpen) }

    public static func openCount(_ tasks: [Task]) -> Int { tasks.filter(\.isOpen).count }
}
