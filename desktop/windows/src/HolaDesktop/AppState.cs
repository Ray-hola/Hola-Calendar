using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Hola.Core;

namespace Hola.Desktop
{
    /// <summary>The server in use: its address and, once checked on 连接, what it said about itself.</summary>
    sealed class ConnectedServer
    {
        public readonly string Url;
        public readonly ServerInfo Info;
        public ConnectedServer(string url, ServerInfo info) { Url = url; Info = info; }
        public string Host => new SavedServer(Url, "", "").Host;
        public bool ExposesPassword => ServerAddress.ExposesPassword(Url);
    }

    /// <summary>
    /// Everything the UI shows: the server session, today's list, the pomodoro,
    /// the style, launch at login. One instance, used on the UI thread only, a
    /// port of the macOS app's AppState. `Changed` fires (coalesced) after any change.
    /// </summary>
    sealed partial class AppState
    {
        public event Action Changed;
        readonly Settings settings;
        readonly Dispatcher dispatcher;
        bool changePending;

        // Settings (the settings file; nothing secret)
        /// <summary>What is typed on the 连接 screen.</summary>
        public string ServerText = "";
        string username;
        public string Username { get => username; set { username = value ?? ""; settings.Set("username", username); Notify(); } }
        ConnectedServer server;
        /// <summary>The server this app talks to; null until the first 连接.</summary>
        public ConnectedServer Server { get => server; private set { server = value; settings.Set("server", value?.Url); } }
        List<SavedServer> servers;
        /// <summary>Servers connected to before, newest first. Tokens stay in Credential Manager.</summary>
        public List<SavedServer> Servers { get => servers; private set { servers = value; settings.Set("servers", ServerList.Encode(value)); } }
        public bool Connecting { get; private set; }
        /// <summary>The 连接 screen over a known server: "连接新服务器", or a link from the web.</summary>
        public bool ShowConnect { get; private set; }
        Look look;
        public Look Look { get => look; set { look = value; settings.Set(Looks.SettingsKey, value.Key()); Notify(); } }

        // Session + data
        public bool SignedIn { get; private set; }
        public Snapshot Snapshot { get; private set; }
        public List<Todo> Tasks { get; private set; } = new List<Todo>();
        public HashSet<string> Busy { get; private set; } = new HashSet<string>();
        public bool Syncing { get; private set; }
        public bool Offline { get; private set; }
        public DateTime? LastSync { get; private set; }
        string notice;
        public string Notice { get => notice; set { notice = value; Notify(); } }

        string token;
        HolaApi api;
        /// <summary>Bumped whenever the person picks or leaves a server, so an answer that arrives afterwards for the old choice is dropped.</summary>
        int choice;
        /// <summary>Bumped when a 连接 is cancelled too; only an address check waits on it.</summary>
        int connectChoice;
        /// <summary>Numbers each sign-in, sign-out and 移除; `latest` keeps the newest one per server, so a late sign-in counts only if nothing newer happened to that server.</summary>
        int attempt;
        readonly Dictionary<string, int> latest = new Dictionary<string, int>();

        public Theme Theme => Theme.Of(Look);
        public User User => Snapshot?.User;
        public int OpenCount => TodayFilter.OpenCount(Tasks);
        public Todo NextTask => TodayFilter.Next(Tasks);
        public string Account => Server?.Url ?? "";
        public bool ConnectScreen => ShowConnect || (!SignedIn && Server == null);
        /// <summary>What the server is called here: the name it was remembered by, else its host.</summary>
        public string ServerName => Server == null ? "" : Servers.FirstOrDefault(s => s.Url == Server.Url)?.Name ?? Server.Info?.Name ?? Server.Host;

        public AppState(Settings settings, Dispatcher dispatcher)
        {
            this.settings = settings;
            this.dispatcher = dispatcher;
            var known = HolaApi.Normalize(settings.Get("server"));
            server = known == null ? null : new ConnectedServer(known, null);
            servers = ServerList.Decode(settings.Get("servers"));
            username = settings.Get("username") ?? "";
            look = Looks.Parse(settings.Get(Looks.SettingsKey));
            pomodoro = Pomodoro.FromJson(settings.GetObject(Pomodoro.SettingsKey));
            RestoreSession();
            ObserveSleep();
            RefreshLaunchAtLogin();
            TickIfNeeded();
        }

        /// <summary>Tells the views once, after the current burst of changes.</summary>
        void Notify()
        {
            if (changePending) return;
            changePending = true;
            dispatcher.BeginInvoke(new Action(() => { changePending = false; Changed?.Invoke(); }), DispatcherPriority.DataBind);
        }

        /// <summary>Lets the views say a field they own changed (typing in a box).</summary>
        public void Touch() => Notify();

        /// <summary>A request whose answer nobody waits for; its failure is dropped on purpose.</summary>
        static void Fire(System.Threading.Tasks.Task t) =>
            t.ContinueWith(x => { _ = x.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }
}
