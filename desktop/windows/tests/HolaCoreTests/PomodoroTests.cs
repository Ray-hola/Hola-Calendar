using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Hola.Core;
using static Hola.Tests.T;

namespace Hola.Tests
{
    static class PomodoroTests
    {
        static readonly DateTime T0 = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);

        public static async Task Run()
        {
            await Test("fresh focus is paused at 25 minutes", () =>
            {
                var p = Pomodoro.Fresh(PomoMode.Focus);
                Check(!p.IsRunning);
                Eq(p.Remaining(T0), 1500);
                Eq(p.Clock(T0), "25:00");
                Eq(p.Progress(T0), 0.0);
            });

            await Test("toggle starts, then pauses keeping the remaining time", () =>
            {
                var p = Pomodoro.Fresh(PomoMode.Focus);
                p.Toggle(T0);
                Check(p.IsRunning);
                Eq(p.Remaining(T0.AddSeconds(47)), 1500 - 47);
                p.Toggle(T0.AddSeconds(47));
                Check(!p.IsRunning);
                Eq(p.Left, 1453);
                Eq(p.Remaining(T0.AddSeconds(500)), 1453);   // paused time does not tick
                p.Toggle(T0.AddSeconds(500));
                Eq(p.Remaining(T0.AddSeconds(503)), 1450);
            });

            await Test("tick flips to a paused rest when focus runs out, rings once", () =>
            {
                var p = Pomodoro.Fresh(PomoMode.Focus);
                p.Toggle(T0);
                Check(p.Tick(T0.AddSeconds(1499)) == null);
                Check(p.Tick(T0.AddSeconds(1500)) == PomoMode.Focus);
                Eq(p.Mode, PomoMode.Rest);
                Check(!p.IsRunning);
                Eq(p.Remaining(T0.AddSeconds(1500)), 300);
                Check(p.Tick(T0.AddSeconds(1501)) == null);
            });

            await Test("a tick long after sleep still finishes the session", () =>
            {
                var p = Pomodoro.Fresh(PomoMode.Rest);
                p.Toggle(T0);
                Check(p.Tick(T0.AddSeconds(9999)) == PomoMode.Rest);
                Eq(p.Mode, PomoMode.Focus);
            });

            await Test("reset and switch", () =>
            {
                var p = Pomodoro.Fresh(PomoMode.Focus);
                p.Toggle(T0);
                p.Reset();
                Eq(p, Pomodoro.Fresh(PomoMode.Focus));
                p.SwitchMode();
                Eq(p, Pomodoro.Fresh(PomoMode.Rest));
                p.SwitchMode();
                Eq(p, Pomodoro.Fresh(PomoMode.Focus));
            });

            await Test("progress and ASCII bar", () =>
            {
                var p = Pomodoro.Fresh(PomoMode.Focus);
                p.Toggle(T0);
                var at = T0.AddSeconds(600);   // 40 %
                Check(Math.Abs(p.Progress(at) - 0.4) < 0.001);
                Eq(p.AsciiBar(at), "[####......] 40%");
                Eq(Pomodoro.Fresh(PomoMode.Rest).AsciiBar(T0), "[..........] 0%");
            });

            await Test("clock formatting", () =>
            {
                Eq(Pomodoro.Clock(0), "00:00");
                Eq(Pomodoro.Clock(65), "01:05");
                Eq(Pomodoro.Clock(1500), "25:00");
            });

            await Test("round-trips through the settings file", () =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "hola-tests-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var file = Path.Combine(dir, "settings.json");
                    var p = Pomodoro.Fresh(PomoMode.Rest); p.Toggle(T0);
                    new Settings(file).Set(Pomodoro.SettingsKey, p.ToJson());
                    var back = Pomodoro.FromJson(new Settings(file).GetObject(Pomodoro.SettingsKey));
                    Eq(back.Mode, PomoMode.Rest);
                    Check(back.IsRunning);
                    Check(Math.Abs((back.EndsAt.Value - p.EndsAt.Value).TotalSeconds) < 0.001);
                    Eq(back.EndsAt.Value.Kind, DateTimeKind.Utc);
                    Eq(Pomodoro.FromJson(new Settings(Path.Combine(dir, "none.json")).GetObject(Pomodoro.SettingsKey)), Pomodoro.Fresh(PomoMode.Focus));
                    Eq(Pomodoro.FromJson(new Dictionary<string, object> { ["mode"] = "nap", ["left"] = 3 }), Pomodoro.Fresh(PomoMode.Focus));
                    // A damaged end time must not stop the app from starting.
                    foreach (var bad in new[] { "NaN", "Infinity", "1e300", "-5", "soon" })
                        Eq(Pomodoro.FromJson(new Dictionary<string, object> { ["mode"] = "rest", ["left"] = 3, ["endsAt"] = bad }), Pomodoro.Fresh(PomoMode.Focus));
                    var later = (DateTime.UtcNow.AddDays(2) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    Eq(Pomodoro.FromJson(new Dictionary<string, object> { ["mode"] = "rest", ["left"] = 3, ["endsAt"] = later }), Pomodoro.Fresh(PomoMode.Focus));
                }
                finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            });
        }
    }
}
