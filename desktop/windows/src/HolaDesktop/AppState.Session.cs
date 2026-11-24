using System;
using System.Linq;
using System.Threading.Tasks;
using Hola.Core;

namespace Hola.Desktop
{
    sealed partial class AppState
    {
        /// <summary>Picks up the session kept in Credential Manager for the current server, if any.</summary>
        bool RestoreSession()
        {
            var url = Server?.Url;
            var t = url == null ? null : CredentialStore.Load(url);
            if (t == null) return false;
            token = t; api = new HolaApi(url); SignedIn = true;
            if (!Servers.Any(s => s.Url == url)) Remember(new SavedServer(url, Server.Info?.Name ?? "哗啦待办", Username));
            StartPolling();
            Fire(Refresh());
            Notify();
            return true;
        }

        /// <summary>
        /// Checks the typed address and, when it is a 哗啦待办 server, makes it the
        /// current one. A session kept for it is resumed; otherwise sign-in comes next.
        /// </summary>
        public async Task Connect()
        {
            if (Connecting) return;
            Connecting = true; notice = null; Notify();
            var asked = connectChoice;
            try
            {
                var (found, info) = await ServerCheck.Connect(ServerText);
                // Cancelled, or another server picked, while the address was being checked.
                if (connectChoice != asked) return;
                Use(new ConnectedServer(found.BaseUrl, info));
            }
            catch (Exception e)
            {
                if (connectChoice == asked) notice = e.Message;
            }
            finally { Connecting = false; Notify(); }
        }

        /// <summary>One click from the remembered list: no address check, no sign-out of the others.</summary>
        public void SwitchTo(SavedServer saved)
        {
            var url = HolaApi.Normalize(saved.Url);
            if (url != null) Use(new ConnectedServer(url, null));
        }

        void Use(ConnectedServer next)
        {
            choice++; connectChoice++;
            LeaveServer();
            Server = next; ShowConnect = false; ServerText = "";
            var saved = Servers.FirstOrDefault(s => s.Url == next.Url);
            if (saved != null) Username = saved.Username;
            if (RestoreSession()) notice = null;
            Notify();
        }

        /// <summary>Stops talking to the current server but keeps its session in Credential Manager.</summary>
        void LeaveServer()
        {
            StopPolling();
            token = null; api = null; SignedIn = false; Snapshot = null; Tasks = new System.Collections.Generic.List<Todo>(); LastSync = null; Offline = false;
            Syncing = false; Busy = new System.Collections.Generic.HashSet<string>(); notice = null;
            Notify();
        }

        public void ShowConnectScreen() { ShowConnect = true; ServerText = ""; notice = null; Notify(); }

        public void CancelConnect()
        {
            if (Server == null) return;
            connectChoice++; ShowConnect = false; notice = null; Notify();
        }

        /// <summary>A `hola://connect` link only fills in the address: the person checks it and presses 连接.</summary>
        public void OpenConnectLink(string url)
        {
            ShowConnect = true;
            ServerText = url;
            notice = "来自网页的连接，确认地址无误后点「连接」";
            Notify();
        }

        /// <summary>Drops a remembered server and its kept session.</summary>
        public void Forget(SavedServer saved)
        {
            attempt++; latest[saved.Url] = attempt;
            if (saved.Url == Server?.Url) { choice++; SignOut(); Server = null; }
            else
            {
                var t = CredentialStore.Load(saved.Url);
                var url = HolaApi.Normalize(saved.Url);
                // Its session is closed on the server too, not only forgotten here.
                if (t != null && url != null) Fire(new HolaApi(url).Logout(t));
            }
            CredentialStore.Delete(saved.Url);
            Servers = ServerList.Forget(saved.Url, Servers);
            Notify();
        }

        /// <summary>
        /// Remembers `saved` first; a server pushed off the end of the list loses its kept session.
        /// The current server is never the one pushed off.
        /// </summary>
        void Remember(SavedServer saved)
        {
            var current = Server?.Url;
            var next = ServerList.Remember(saved, Servers, current);
            foreach (var gone in Servers.Where(g => !next.Any(n => n.Url == g.Url) && g.Url != current)) CredentialStore.Delete(gone.Url);
            Servers = next;
            Notify();
        }
    }
}
