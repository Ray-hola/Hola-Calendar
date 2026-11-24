using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Hola.Desktop
{
    /// <summary>
    /// Anthropic风: 小卡, an original paper-card character. Deliberately *not*
    /// Anthropic's Claude mark or any official Claude character (those are
    /// Anthropic trademarks and this app ships to users). 小卡 is one of the
    /// brand's own cards from the sweep logo, given a face: it bounces while a
    /// focus session runs, dozes during the break and breathes slowly when idle.
    /// Drawn the same way as the macOS app's CardBuddy.
    /// </summary>
    sealed class CardBuddy : Animated
    {
        readonly Mood mood;

        public CardBuddy(Mood mood, double size)
        {
            this.mood = mood; Width = size; Height = size;
            ToolTip = null;
            System.Windows.Automation.AutomationProperties.SetName(this, mood == Mood.Rest ? "小卡在休息" : mood == Mood.Focus ? "小卡在专注" : "小卡");
        }

        static Brush A(Color c, double alpha) { var b = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B)); b.Freeze(); return b; }

        protected override void OnRender(DrawingContext g)
        {
            var t = Seconds;
            double sw = ActualWidth, sh = ActualHeight;
            Color ink = Theme.InkColor, accent = Theme.TerracottaColor;
            var inkB = A(ink, 1);
            double w = sw * 0.72, h = sh * 0.56;
            // Motion
            double dy = 0, squash = 1, tilt = 0;
            switch (mood)
            {
                case Mood.Focus:
                    var phase = (t * 2.6) % 1;   // one hop per ~0.38 s
                    dy = -Math.Abs(Math.Sin(phase * Math.PI)) * sh * 0.14;
                    squash = 1 + Math.Sin(phase * Math.PI * 2) * 0.05;
                    tilt = Math.Sin(t * 2.6 * Math.PI) * 3;
                    break;
                case Mood.Rest:
                    squash = 1 + Math.Sin(t * 1.2) * 0.02;   // slow breathing
                    tilt = -6;
                    break;
                default:
                    squash = 1 + Math.Sin(t * 1.6) * 0.015;
                    break;
            }
            g.PushTransform(new TranslateTransform(sw / 2, sh * 0.6 + dy));
            g.PushTransform(new RotateTransform(tilt));
            g.PushTransform(new ScaleTransform(1 / squash, squash));

            var card = new Rect(-w / 2, -h / 2, w, h);
            // Two faint cards behind, like the logo's sweep.
            foreach (var (i, a) in new[] { (2, 0.18), (1, 0.4) })
            {
                g.PushTransform(new RotateTransform(-8 * i));
                g.PushTransform(new TranslateTransform(-4 * i, 0));
                g.DrawRoundedRectangle(A(accent, a), null, card, h * 0.2, h * 0.2);
                g.Pop(); g.Pop();
            }
            // The card itself: paper with a terracotta edge.
            g.DrawRoundedRectangle(Theme.WarmPaper, new Pen(Theme.Terracotta, Math.Max(1.5, sw / 32)), card, h * 0.2, h * 0.2);

            // Face
            double eyeY = -h * 0.12, eyeDX = w * 0.18, eyeR = Math.Max(1.6, sw / 26);
            var blink = mood != Mood.Rest && t % 3.7 < 0.12;
            foreach (var s in new[] { -1.0, 1.0 })
            {
                if (mood == Mood.Rest || blink)
                    g.DrawLine(new Pen(inkB, eyeR * 0.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                               new Point(s * eyeDX - eyeR * 1.3, eyeY), new Point(s * eyeDX + eyeR * 1.3, eyeY));
                else g.DrawEllipse(inkB, null, new Point(s * eyeDX, eyeY), eyeR, eyeR);
            }
            double my = h * 0.18, mw = w * 0.14;
            if (mood == Mood.Rest)
            {
                g.DrawEllipse(null, new Pen(inkB, eyeR * 0.7), new Point(0, my + mw * 0.05), mw * 0.3, mw * 0.35);
            }
            else
            {
                var fig = new PathFigure { StartPoint = new Point(-mw, my) };
                fig.Segments.Add(new QuadraticBezierSegment(new Point(0, my + mw * (mood == Mood.Focus ? 1.2 : 0.7)), new Point(mw, my), true));
                var mouth = new PathGeometry(); mouth.Figures.Add(fig);
                g.DrawGeometry(null, new Pen(inkB, eyeR * 0.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, mouth);
            }
            // Cheeks when focusing hard.
            if (mood == Mood.Focus)
                foreach (var s in new[] { -1.0, 1.0 })
                    g.DrawEllipse(A(accent, 0.35), null, new Point(s * eyeDX * 1.5, my - eyeR * 0.8), eyeR, eyeR * 0.6);
            // zZ drifting up while resting.
            if (mood == Mood.Rest)
                for (var k = 0; k < 2; k++)
                {
                    var ph = (t * 0.5 + k * 0.5) % 1;
                    var x = w / 2 + (6 + k * 6) + Math.Sin(ph * Math.PI * 2) * 2;
                    var y = -h / 2 - ph * sh * 0.35;
                    var z = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Georgia"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        sw / (k == 0 ? 7 : 9), A(accent, 1 - ph), 1.0);
                    g.DrawText(z, new Point(x - z.Width / 2, y - z.Height / 2));
                }
            g.Pop(); g.Pop(); g.Pop();
        }
    }
}
