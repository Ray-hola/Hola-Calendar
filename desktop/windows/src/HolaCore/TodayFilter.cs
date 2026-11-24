using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hola.Core
{
    /// <summary>
    /// A port of the web widget's `renderPip` selection, so the tray shows
    /// exactly what the browser's 桌面小组件 shows for the same account.
    /// </summary>
    public static class TodayFilter
    {
        static readonly HashSet<string> Archived = new HashSet<string> { "deferred", "skipped", "rejected" };
        static readonly HashSet<string> HiddenProject = new HashSet<string> { "done", "archived" };
        static readonly Dictionary<string, int> RankOf = new Dictionary<string, int> { ["P0"] = 0, ["P1"] = 1, ["P2"] = 2 };
        static readonly CompareInfo Chinese = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;

        static int Rank(Todo t) => RankOf.TryGetValue(t.Priority, out var r) ? r : 3;

        /// <summary>Today's tasks, open ones first, then P0 &lt; P1 &lt; P2, then by name.</summary>
        public static List<Todo> Tasks(Snapshot s)
        {
            var hidden = new HashSet<string>(s.Projects.Where(p => HiddenProject.Contains(p.Status)).Select(p => p.Id));
            var visible = s.Tasks.Where(t => !t.Superseded
                && (t.ProjectId == null || !hidden.Contains(t.ProjectId))
                && (s.User.Admin || t.Assignee == s.User.Id)
                && string.CompareOrdinal(t.Date, s.Today) <= 0 && string.CompareOrdinal(s.Today, t.End) <= 0
                && !Archived.Contains(t.Status));
            return Sort(visible);
        }

        public static List<Todo> Sort(IEnumerable<Todo> tasks)
        {
            var list = tasks.ToList();
            list.Sort((a, b) =>
            {
                if (a.IsOpen != b.IsOpen) return a.IsOpen ? -1 : 1;
                int ra = Rank(a), rb = Rank(b);
                if (ra != rb) return ra.CompareTo(rb);
                return Chinese.Compare(a.Name, b.Name);
            });
            return list;
        }

        /// <summary>
        /// Whether the signed-in account may tick this task: the server's `performer`
        /// check, mirrored so the box is disabled instead of producing a 403.
        /// </summary>
        public static bool CanComplete(Todo t, User user) => !user.Admin && t.Assignee == user.Id && t.IsOpen;

        /// <summary>The task the island calls "next": the first open one.</summary>
        public static Todo Next(IEnumerable<Todo> tasks) => tasks.FirstOrDefault(t => t.IsOpen);

        public static int OpenCount(IEnumerable<Todo> tasks) => tasks.Count(t => t.IsOpen);
    }
}
