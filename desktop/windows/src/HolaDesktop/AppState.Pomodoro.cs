using System;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hola.Core;

namespace Hola.Desktop
{
    sealed partial class AppState
    {
        Pomodoro pomodoro;
        public Pomodoro Pomodoro => pomodoro;
        public DateTime Now { get; private set; } = DateTime.UtcNow;
        /// <summary>The session that just finished, for a few seconds, so every surface can show the ring.</summary>
        public PomoMode? Rang { get; private set; }
        DispatcherTimer tickTimer;

        public void PomoToggle() { pomodoro.Toggle(DateTime.UtcNow); Rang = null; SavePomo(); TickIfNeeded(); }
        public void PomoReset() { pomodoro.Reset(); Rang = null; SavePomo(); TickIfNeeded(); }
        public void PomoSwitch() { pomodoro.SwitchMode(); Rang = null; SavePomo(); TickIfNeeded(); }

        void SavePomo() { settings.Set(Pomodoro.SettingsKey, pomodoro.ToJson()); Now = DateTime.UtcNow; Notify(); }

        void TickIfNeeded()
        {
            if (pomodoro.IsRunning)
            {
                if (tickTimer != null) return;
                tickTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
                tickTimer.Tick += (_, __) => Tick();
                tickTimer.Start();
            }
            else { tickTimer?.Stop(); tickTimer = null; }
        }

        void Tick()
        {
            Now = DateTime.UtcNow;
            var finished = pomodoro.Tick(Now);
            if (finished is PomoMode f)
            {
                SavePomo();
                Rang = f;
                SystemSounds.Asterisk.Play();
                TickIfNeeded();
                // The ring badge clears itself after a few seconds, like the web widget's 6 s highlight.
                Task.Delay(6000).ContinueWith(_ => { if (Rang == f) { Rang = null; Notify(); } }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            Notify();
        }

        // Launch at login (the Run key under HKCU; Task Manager's 启动应用 lists it)

        public bool LaunchAtLogin { get; private set; }
        public string LaunchAtLoginError { get; private set; }

        public void RefreshLaunchAtLogin() { LaunchAtLogin = Startup.LaunchAtLoginEnabled; Notify(); }

        public void SetLaunchAtLogin(bool on)
        {
            try { Startup.SetLaunchAtLogin(on); LaunchAtLoginError = null; }
            catch (Exception e) { LaunchAtLoginError = e.Message; }
            RefreshLaunchAtLogin();
        }
    }
}
