using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Hola.Core;

namespace Hola.Desktop
{
    /// <summary>What the mascot is doing; derived from the pomodoro.</summary>
    enum Mood { Idle, Focus, Rest }

    static class Moods
    {
        public static Mood Of(Pomodoro p) => p.IsRunning ? (p.Mode == PomoMode.Focus ? Mood.Focus : Mood.Rest) : Mood.Idle;
    }

    /// <summary>
    /// Drives every animated drawing from one 24 fps timer that runs only while
    /// one of them is on screen, so a hidden panel costs nothing.
    /// </summary>
    abstract class Animated : FrameworkElement
    {
        static readonly List<Animated> live = new List<Animated>();
        static DispatcherTimer timer;
        protected static double Seconds => (DateTime.UtcNow - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        protected Animated()
        {
            Loaded += (_, __) => { live.Add(this); Run(); };
            Unloaded += (_, __) => { live.Remove(this); Run(); };
            IsVisibleChanged += (_, __) => Run();
        }

        static void Run()
        {
            var any = live.Exists(a => a.IsVisible);
            if (any && timer == null)
            {
                timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 24) };
                timer.Tick += (_, __) => { foreach (var a in live.ToArray()) if (a.IsVisible && a.Moved()) a.InvalidateVisual(); };
                timer.Start();
            }
            else if (!any && timer != null) { timer.Stop(); timer = null; }
        }

        /// <summary>Whether the picture differs from the last one drawn.</summary>
        protected virtual bool Moved() => true;
    }

    /// <summary>Picks the mascot for the current style.</summary>
    static class Mascot
    {
        public static FrameworkElement Make(Look look, Mood mood, double size, bool dark = false) =>
            look == Look.Anthropic ? new CardBuddy(mood, size) : (FrameworkElement)new PixelSprite(mood, size, dark);
    }

    /// <summary>Progress: a thin terracotta capsule (Anthropic风). Tempo风 uses the ASCII text instead.</summary>
    sealed class Bar : FrameworkElement
    {
        public Brush Track, Fill;
        double value;
        public double Value { get => value; set { if (Math.Abs(this.value - value) > 0.0005) { this.value = value; InvalidateVisual(); } } }

        public Bar(Brush track, Brush fill) { Track = track; Fill = fill; Height = 4; }

        protected override void OnRender(DrawingContext g)
        {
            var w = ActualWidth; var h = ActualHeight;
            g.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            g.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(4, w * value), h), h / 2, h / 2);
        }
    }

    /// <summary>The island's progress circle: a faint track and a terracotta arc from 12 o'clock.</summary>
    sealed class Ring : FrameworkElement
    {
        double value;
        public double Value { get => value; set { if (Math.Abs(this.value - value) > 0.0005) { this.value = value; InvalidateVisual(); } } }

        public Ring() { Width = 14; Height = 14; }

        protected override void OnRender(DrawingContext g)
        {
            const double stroke = 2.5;
            var r = Math.Min(ActualWidth, ActualHeight) / 2 - stroke / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            g.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(64, 0xFF, 0xFD, 0xF9)), stroke), c, r, r);
            if (value <= 0) return;
            var pen = new Pen(Theme.Terracotta, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (value >= 0.999) { g.DrawEllipse(null, pen, c, r, r); return; }
            var a = value * 2 * Math.PI;
            var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = new Point(c.X, c.Y - r) };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, value > 0.5, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            g.DrawGeometry(null, pen, geo);
        }
    }
}
