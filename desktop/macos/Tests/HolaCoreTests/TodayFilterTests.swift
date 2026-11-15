import Foundation
import HolaCore

func todayFilterTests() async {
    let me = User(id: "ray", name: "Ray", admin: false)
    let boss = User(id: "superadmin", name: "Admin", admin: true)
    func snap(_ tasks: [Task], user: User? = nil, projects: [Project] = []) -> Snapshot {
        Snapshot(user: user ?? me, today: "2026-11-15", tasks: tasks, projects: projects)
    }

    await test("keeps only my tasks active today") {
        let s = snap([
            Task(id: "a", name: "今天", assignee: "ray", date: "2026-11-15"),
            Task(id: "b", name: "昨天", assignee: "ray", date: "2026-11-14"),
            Task(id: "c", name: "跨天到期", assignee: "ray", date: "2026-11-10", deadline: "2026-11-16"),
            Task(id: "d", name: "别人的", assignee: "lee", date: "2026-11-15"),
            Task(id: "e", name: "明天", assignee: "ray", date: "2026-11-16"),
        ])
        check(TodayFilter.tasks(in: s).map(\.id), ["a", "c"])
    }

    await test("drops archived, superseded and hidden-project tasks") {
        let s = snap([
            Task(id: "a", name: "递延", assignee: "ray", date: "2026-11-15", status: "deferred"),
            Task(id: "b", name: "跳过", assignee: "ray", date: "2026-11-15", status: "skipped"),
            Task(id: "c", name: "驳回", assignee: "ray", date: "2026-11-15", status: "rejected"),
            Task(id: "d", name: "被替代", assignee: "ray", date: "2026-11-15", superseded: true),
            Task(id: "e", name: "归档项目", assignee: "ray", date: "2026-11-15", projectId: "p-old"),
            Task(id: "f", name: "活项目", assignee: "ray", date: "2026-11-15", projectId: "p-live"),
            Task(id: "g", name: "已完成", assignee: "ray", date: "2026-11-15", status: "done"),
        ], projects: [Project(id: "p-old", name: "旧", status: "archived"), Project(id: "p-live", name: "新", status: "active")])
        check(TodayFilter.tasks(in: s).map(\.id), ["f", "g"])
    }

    await test("orders open first, then priority, then name") {
        let s = snap([
            Task(id: "1", name: "乙", assignee: "ray", date: "2026-11-15", priority: "P1"),
            Task(id: "2", name: "甲", assignee: "ray", date: "2026-11-15", priority: "P1"),
            Task(id: "3", name: "完成的P0", assignee: "ray", date: "2026-11-15", priority: "P0", status: "done"),
            Task(id: "4", name: "紧急", assignee: "ray", date: "2026-11-15", priority: "P0", status: "doing"),
            Task(id: "5", name: "次要", assignee: "ray", date: "2026-11-15", priority: "P2"),
        ])
        let ids = TodayFilter.tasks(in: s).map(\.id)
        check(ids.first, "4")
        check(ids.last, "3")
        check(ids.firstIndex(of: "2")! < ids.firstIndex(of: "1")!)
        check(ids.firstIndex(of: "1")! < ids.firstIndex(of: "5")!)
    }

    await test("admin sees everyone but cannot tick") {
        let s = snap([Task(id: "a", name: "x", assignee: "lee", date: "2026-11-15")], user: boss)
        let tasks = TodayFilter.tasks(in: s)
        check(tasks.count, 1)
        check(!TodayFilter.canComplete(tasks[0], as: boss))
    }

    await test("can complete only own open tasks") {
        check(TodayFilter.canComplete(Task(id: "a", name: "x", assignee: "ray", date: "2026-11-15"), as: me))
        check(TodayFilter.canComplete(Task(id: "a", name: "x", assignee: "ray", date: "2026-11-15", status: "doing"), as: me))
        check(!TodayFilter.canComplete(Task(id: "a", name: "x", assignee: "ray", date: "2026-11-15", status: "done"), as: me))
        check(!TodayFilter.canComplete(Task(id: "a", name: "x", assignee: "lee", date: "2026-11-15"), as: me))
    }

    await test("next and open count") {
        let tasks = [Task(id: "a", name: "done", assignee: "ray", date: "2026-11-15", status: "done"),
                     Task(id: "b", name: "open", assignee: "ray", date: "2026-11-15")]
        check(TodayFilter.next(in: TodayFilter.tasks(in: snap(tasks)))?.id, "b")
        check(TodayFilter.openCount(tasks), 1)
        check(TodayFilter.next(in: []) == nil)
    }

    await test("decodes server JSON leniently") {
        let raw = """
        {"user":{"id":"ray","name":"Ray","admin":false,"role":"member"},"today":"2026-11-15",
         "tasks":[{"id":"t1","name":"写周报","assignee":"ray","date":"2026-11-15","deadline":"","time":"09:00",
                   "duration":"45","priority":"P0","status":"todo","projectId":null,"stackCount":2,"stackPeriods":["a","b"],"extra":1},
                  {"id":"broken"}],
         "projects":[{"id":"p","name":"季度","status":"active"}],"logs":[],"unknown":{}}
        """
        let obj = try JSONSerialization.jsonObject(with: Data(raw.utf8)) as! [String: Any]
        let s = try Snapshot(json: obj)
        check(s.tasks.count, 1)
        let t = s.tasks[0]
        check(t.duration, 45)
        check(t.deadline == nil)
        check(t.projectId == nil)
        check(t.stackCount, 2)
        check(t.end, "2026-11-15")
        check(s.projects.first?.name, "季度")
    }
}
