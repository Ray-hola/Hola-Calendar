using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hola.Core;
using Microsoft.Win32;

namespace Hola.Desktop
{
    sealed partial class AppState
    {
        /// <summary>Idle poll interval. The server's own scheduler runs every 30 s, so nothing is gained by asking more often.</summary>
        static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(45);
        static readonly TimeSpan OpenRefreshAfter = TimeSpan.FromSeconds(10);

        int taskRevision;
        DispatcherTimer pollTimer;
        bool asleep;

        public async Task Refresh()
        {
            var a = api; var t = token;
            if (a == null || t == null || Syncing || Busy.Count > 0) return;
            var revision = taskRevision;
            Syncing = true; Notify();
            try
            {
                var s = await a.State(t);
                if (!ReferenceEquals(api, a) || taskRevision != revision) return;
                Snapshot = s;
                Tasks = TodayFilter.Tasks(s);
                LastSync = DateTime.Now; Offline = false;
                if (notice != null && notice.StartsWith("离线")) notice = null;
            }
            catch (ApiException e) when (e.Kind == ApiErrorKind.Unauthorized)
            {
                if (!ReferenceEquals(api, a)) return;
                // The session was closed server-side (expired, password changed, admin reset).
                SignOut();
                notice = "登录已失效，请重新登录";
            }
            catch (ApiException e)
            {
                if (!ReferenceEquals(api, a)) return;
                if (e.Kind == ApiErrorKind.Offline) Offline = true; else notice = e.Message;
            }
            catch (Exception) { if (ReferenceEquals(api, a)) Offline = true; }
            finally
            {
                if (ReferenceEquals(api, a)) Syncing = false;
                Notify();
            }
        }

        /// <summary>Called when the panel opens: refresh if the list is stale.</summary>
        public void RefreshIfStale()
        {
            if (!SignedIn) return;
            if (LastSync == null || DateTime.Now - LastSync.Value > OpenRefreshAfter) Fire(Refresh());
        }

        public async Task Complete(Todo task)
        {
            var a = api; var t = token; var user = User;
            if (a == null || t == null || user == null || !TodayFilter.CanComplete(task, user) || Busy.Contains(task.Id)) return;
            Busy.Add(task.Id);
            taskRevision++;
            Notify();
            try
            {
                await a.Complete(task, t);
                if (!ReferenceEquals(api, a)) return;
                Tasks = TodayFilter.Sort(Tasks.Select(x => x.Id == task.Id ? x.With("done") : x));
                notice = $"「{task.Name}」已完成";
            }
            catch (ApiException e) when (e.Kind == ApiErrorKind.Unauthorized)
            {
                if (!ReferenceEquals(api, a)) return;
                SignOut(); notice = "登录已失效，请重新登录"; Notify();
                return;
            }
            catch (Exception e)
            {
                if (!ReferenceEquals(api, a)) return;
                notice = e.Message;
            }
            Busy.Remove(task.Id);
            Notify();
            await Refresh();
        }

        void StartPolling()
        {
            StopPolling();
            pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = PollInterval };
            pollTimer.Tick += (_, __) => { if (!asleep) Fire(Refresh()); };
            pollTimer.Start();
        }

        void StopPolling() { pollTimer?.Stop(); pollTimer = null; }

        /// <summary>Sleep stops the clock; waking settles the pomodoro and syncs at once.</summary>
        void ObserveSleep()
        {
            SystemEvents.PowerModeChanged += (_, e) => dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Mode == PowerModes.Suspend) { asleep = true; return; }
                if (e.Mode != PowerModes.Resume) return;
                asleep = false;
                Tick();   // settle a pomodoro that ran out while asleep
                if (SignedIn) Fire(Refresh());
            }));
        }
    }
}
