using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hola.Core
{
    public enum PomoMode { Focus, Rest }

    public static class PomoModes
    {
        /// <summary>Same lengths as the web widget: 25 minutes of focus, 5 of rest.</summary>
        public static int Seconds(this PomoMode m) => m == PomoMode.Focus ? 25 * 60 : 5 * 60;
        public static PomoMode Other(this PomoMode m) => m == PomoMode.Focus ? PomoMode.Rest : PomoMode.Focus;
    }

    /// <summary>
    /// The pomodoro state machine, a direct port of the web widget's
    /// `pomoRead/pomoAction/renderPomo` (and of the macOS app's Pomodoro).
    /// A value type with the clock passed in, so it is testable.
    /// </summary>
    public struct Pomodoro : IEquatable<Pomodoro>
    {
        public PomoMode Mode;
        /// <summary>Seconds left while paused. Ignored while running (`EndsAt` is authoritative).</summary>
        public int Left;
        /// <summary>Wall-clock end (UTC) while running, null while paused.</summary>
        public DateTime? EndsAt;

        public Pomodoro(PomoMode mode, int? left = null, DateTime? endsAt = null)
        {
            Mode = mode; Left = left ?? mode.Seconds(); EndsAt = endsAt;
        }

        public static Pomodoro Fresh(PomoMode mode) => new Pomodoro(mode);

        public bool IsRunning => EndsAt != null;

        public int Remaining(DateTime now)
        {
            if (EndsAt is DateTime end) return Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds));
            return Math.Max(0, Left);
        }

        /// <summary>Elapsed fraction of the current session, 0...1.</summary>
        public double Progress(DateTime now)
        {
            double total = Mode.Seconds();
            return Math.Min(1, Math.Max(0, (total - Remaining(now)) / total));
        }

        /// <summary>Start when paused, pause when running.</summary>
        public void Toggle(DateTime now)
        {
            if (EndsAt != null) { Left = Remaining(now); EndsAt = null; }
            else EndsAt = now.AddSeconds(Remaining(now));
        }

        public void Reset() => this = Fresh(Mode);

        public void SwitchMode() => this = Fresh(Mode.Other());

        /// <summary>
        /// Call once a second (or on wake). When a running session has run out it
        /// flips to a fresh, *paused* session of the other mode, exactly like the
        /// web widget, and returns the mode that just finished so the UI can ring.
        /// </summary>
        public PomoMode? Tick(DateTime now)
        {
            if (!IsRunning || Remaining(now) != 0) return null;
            var finished = Mode;
            this = Fresh(Mode.Other());
            return finished;
        }

        // Presentation helpers shared by every surface

        public static string Clock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

        public string Clock(DateTime now) => Clock(Remaining(now));

        /// <summary>Tempo-style ASCII bar, e.g. `[####......] 40%`.</summary>
        public string AsciiBar(DateTime now, int width = 10)
        {
            var p = Progress(now);
            var filled = (int)Math.Floor(p * width);
            return "[" + new string('#', filled) + new string('.', Math.Max(0, width - filled)) + "] "
                   + (int)Math.Round(p * 100, MidpointRounding.AwayFromZero) + "%";
        }

        // Persistence (the settings file; not secret)

        public const string SettingsKey = "pomodoro";
        static readonly DateTime Unix = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { ["mode"] = Mode == PomoMode.Focus ? "focus" : "rest", ["left"] = Left };
            if (EndsAt is DateTime end) d["endsAt"] = (end - Unix).TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture);
            return d;
        }

        public static Pomodoro FromJson(Dictionary<string, object> d)
        {
            var mode = d.Str("mode");
            if (mode != "focus" && mode != "rest" || d.Int("left") == null) return Fresh(PomoMode.Focus);
            DateTime? end = null;
            if (d.ContainsKey("endsAt"))
            {
                // A hand-edited or damaged value must not stop the app from starting.
                if (!double.TryParse(d.Str("endsAt"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                    || double.IsNaN(ms) || ms < 0 || ms > (DateTime.MaxValue - Unix).TotalMilliseconds) return Fresh(PomoMode.Focus);
                end = Unix.AddMilliseconds(ms);
            }
            return new Pomodoro(mode == "focus" ? PomoMode.Focus : PomoMode.Rest, d.Int("left"), end);
        }

        public bool Equals(Pomodoro o) => Mode == o.Mode && Left == o.Left && EndsAt == o.EndsAt;
        public override bool Equals(object obj) => obj is Pomodoro p && Equals(p);
        public override int GetHashCode() => (int)Mode ^ Left;
        public override string ToString() => $"{Mode} left={Left} endsAt={EndsAt?.ToString("o") ?? "-"}";
    }
}
