using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hola.Core;
using static Hola.Tests.T;

namespace Hola.Tests
{
    static class TodayFilterTests
    {
        static readonly User Me = new User("ray", "Ray", false);
        static readonly User Boss = new User("superadmin", "Admin", true);

        static Snapshot Snap(List<Todo> tasks, User user = null, List<Project> projects = null) =>
            new Snapshot(user ?? Me, "2026-11-15", tasks, projects ?? new List<Project>());

        static List<string> Ids(Snapshot s) => TodayFilter.Tasks(s).Select(t => t.Id).ToList();

        public static async Task Run()
        {
            await Test("keeps only my tasks active today", () =>
            {
                var s = Snap(new List<Todo>
                {
                    new Todo("a", "今天", "ray", "2026-11-15"),
                    new Todo("b", "昨天", "ray", "2026-11-14"),
                    new Todo("c", "跨天到期", "ray", "2026-11-10", deadline: "2026-11-16"),
                    new Todo("d", "别人的", "lee", "2026-11-15"),
                    new Todo("e", "明天", "ray", "2026-11-16"),
                });
                Seq(Ids(s), new[] { "a", "c" });
            });

            await Test("drops archived, superseded and hidden-project tasks", () =>
            {
                var s = Snap(new List<Todo>
                {
                    new Todo("a", "递延", "ray", "2026-11-15", status: "deferred"),
                    new Todo("b", "跳过", "ray", "2026-11-15", status: "skipped"),
                    new Todo("c", "驳回", "ray", "2026-11-15", status: "rejected"),
                    new Todo("d", "被替代", "ray", "2026-11-15", superseded: true),
                    new Todo("e", "归档项目", "ray", "2026-11-15", projectId: "p-old"),
                    new Todo("f", "活项目", "ray", "2026-11-15", projectId: "p-live"),
                    new Todo("g", "已完成", "ray", "2026-11-15", status: "done"),
                }, projects: new List<Project> { new Project("p-old", "旧", "archived"), new Project("p-live", "新", "active") });
                Seq(Ids(s), new[] { "f", "g" });
            });

            await Test("orders open first, then priority, then name", () =>
            {
                var ids = Ids(Snap(new List<Todo>
                {
                    new Todo("1", "乙", "ray", "2026-11-15", priority: "P1"),
                    new Todo("2", "甲", "ray", "2026-11-15", priority: "P1"),
                    new Todo("3", "完成的P0", "ray", "2026-11-15", priority: "P0", status: "done"),
                    new Todo("4", "紧急", "ray", "2026-11-15", priority: "P0", status: "doing"),
                    new Todo("5", "次要", "ray", "2026-11-15", priority: "P2"),
                }));
                Eq(ids.First(), "4");
                Eq(ids.Last(), "3");
                Check(ids.IndexOf("2") < ids.IndexOf("1"), "甲 sorts before 乙 by pinyin");
                Check(ids.IndexOf("1") < ids.IndexOf("5"));
            });

            await Test("admin sees everyone but cannot tick", () =>
            {
                var tasks = TodayFilter.Tasks(Snap(new List<Todo> { new Todo("a", "x", "lee", "2026-11-15") }, user: Boss));
                Eq(tasks.Count, 1);
                Check(!TodayFilter.CanComplete(tasks[0], Boss));
            });

            await Test("can complete only own open tasks", () =>
            {
                Check(TodayFilter.CanComplete(new Todo("a", "x", "ray", "2026-11-15"), Me));
                Check(TodayFilter.CanComplete(new Todo("a", "x", "ray", "2026-11-15", status: "doing"), Me));
                Check(!TodayFilter.CanComplete(new Todo("a", "x", "ray", "2026-11-15", status: "done"), Me));
                Check(!TodayFilter.CanComplete(new Todo("a", "x", "lee", "2026-11-15"), Me));
            });

            await Test("next and open count", () =>
            {
                var tasks = new List<Todo> { new Todo("a", "done", "ray", "2026-11-15", status: "done"), new Todo("b", "open", "ray", "2026-11-15") };
                Eq(TodayFilter.Next(TodayFilter.Tasks(Snap(tasks)))?.Id, "b");
                Eq(TodayFilter.OpenCount(tasks), 1);
                Check(TodayFilter.Next(new List<Todo>()) == null);
            });

            await Test("decodes server JSON leniently", () =>
            {
                const string raw = @"{""user"":{""id"":""ray"",""name"":""Ray"",""admin"":false,""role"":""member""},""today"":""2026-11-15"",
                 ""tasks"":[{""id"":""t1"",""name"":""写周报"",""assignee"":""ray"",""date"":""2026-11-15"",""deadline"":"""",""time"":""09:00"",
                           ""duration"":""45"",""priority"":""P0"",""status"":""todo"",""projectId"":null,""stackCount"":2,""stackPeriods"":[""a"",""b""],""extra"":1},
                          {""id"":""broken""}],
                 ""projects"":[{""id"":""p"",""name"":""季度"",""status"":""active""}],""logs"":[],""unknown"":{}}";
                var s = Snapshot.FromJson(Json.Object(raw));
                Eq(s.Tasks.Count, 1);
                var t = s.Tasks[0];
                Eq(t.Duration, 45);
                Check(t.Deadline == null);
                Check(t.ProjectId == null);
                Eq(t.StackCount, 2);
                Seq(t.StackPeriods, new[] { "a", "b" });
                Eq(t.End, "2026-11-15");
                Eq(s.Projects.First().Name, "季度");
                Eq(new Todo("x", "n", "ray", "2026-11-10", deadline: "2026-11-16").End, "2026-11-16");
                // SQLite keeps booleans as 0/1, and the server passes its user row on as is.
                Check(User.FromJson(Json.Object(@"{""id"":""superadmin"",""admin"":1}")).Admin, "admin: 1 is an admin");
                Check(!User.FromJson(Json.Object(@"{""id"":""ray"",""admin"":0}")).Admin);
                Check(!User.FromJson(Json.Object(@"{""id"":""ray"",""admin"":""yes""}")).Admin);
            });
        }
    }
}
