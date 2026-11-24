using System;
using System.Collections.Generic;
using System.Linq;

namespace Hola.Core
{
    /// <summary>The slice of `/api/state` the companion needs.</summary>
    public sealed class Snapshot
    {
        public User User;
        public string Today;
        public List<Todo> Tasks;
        public List<Project> Projects;

        public Snapshot(User user, string today, List<Todo> tasks, List<Project> projects)
        {
            User = user; Today = today; Tasks = tasks; Projects = projects;
        }

        public static Snapshot FromJson(Dictionary<string, object> json)
        {
            var user = User.FromJson(json.Obj("user"));
            var today = json.Str("today");
            if (user == null || today == null) throw new ApiException(ApiErrorKind.BadResponse);
            return new Snapshot(user, today,
                json.Objects("tasks").Select(Todo.FromJson).Where(t => t != null).ToList(),
                json.Objects("projects").Select(Project.FromJson).Where(p => p != null).ToList());
        }
    }

    public sealed class User
    {
        public string Id, Name;
        public bool Admin;

        public User(string id, string name, bool admin) { Id = id; Name = name; Admin = admin; }

        public static User FromJson(Dictionary<string, object> j)
        {
            var id = j.Str("id");
            return id == null ? null : new User(id, j.Str("name") ?? id, j.Bool("admin") ?? false);
        }
    }

    public sealed class Project
    {
        public string Id, Name, Status;

        public Project(string id, string name, string status) { Id = id; Name = name; Status = status; }

        public static Project FromJson(Dictionary<string, object> j)
        {
            var id = j.Str("id");
            return id == null ? null : new Project(id, j.Str("name") ?? "", j.Str("status") ?? "active");
        }
    }

    /// <summary>A to-do. Called Task in the Swift client; renamed so it does not clash with System.Threading.Tasks.</summary>
    public sealed class Todo
    {
        public string Id, Name, Assignee, Date, Deadline, Time, Priority, Status, ProjectId;
        public int Duration, StackCount;
        public bool Superseded;
        public List<string> StackPeriods;

        public Todo(string id, string name, string assignee, string date, string deadline = null, string time = null,
                    int duration = 30, string priority = "P1", string status = "todo", string projectId = null,
                    bool superseded = false, int stackCount = 1, List<string> stackPeriods = null)
        {
            Id = id; Name = name; Assignee = assignee; Date = date; Deadline = deadline; Time = time;
            Duration = duration; Priority = priority; Status = status; ProjectId = projectId;
            Superseded = superseded; StackCount = stackCount; StackPeriods = stackPeriods ?? new List<string>();
        }

        static string NonEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        public static Todo FromJson(Dictionary<string, object> j)
        {
            string id = j.Str("id"), name = j.Str("name"), date = j.Str("date");
            if (id == null || name == null || date == null) return null;
            return new Todo(id, name, j.Str("assignee") ?? "", date, NonEmpty(j.Str("deadline")), NonEmpty(j.Str("time")),
                            j.Int("duration") ?? 30, j.Str("priority") ?? "P1", j.Str("status") ?? "todo",
                            NonEmpty(j.Str("projectId")), j.Bool("superseded") ?? false, j.Int("stackCount") ?? 1,
                            j.Strings("stackPeriods"));
        }

        /// <summary>Same as the web app's `taskEnd`: a deadline later than the start day extends the task.</summary>
        public string End => Deadline != null && string.CompareOrdinal(Deadline, Date) > 0 ? Deadline : Date;
        public bool IsOpen => Status == "todo" || Status == "doing";

        public Todo With(string status) =>
            new Todo(Id, Name, Assignee, Date, Deadline, Time, Duration, Priority, status, ProjectId, Superseded, StackCount, StackPeriods);
    }
}
