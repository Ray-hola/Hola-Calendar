using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Hola.Desktop
{
    /// <summary>
    /// Tempo风: original pixel sprites, drawn as code grids. `#` ink, `o` accent
    /// (sand / coffee), `w` paper highlight, `s` steam, `.` empty. An hourglass
    /// runs while focusing, a mug steams during the break, and the hourglass sits
    /// still (with a blinking grain) when idle. Same frames as the macOS app.
    /// </summary>
    static class PixelArt
    {
        public static readonly string[][] Hourglass =
        {
            new[] { "#########", ".#wwwww#.", ".#ooooo#.", "..#ooo#..", "...#o#...", "....#....", "...#.#...", "..#...#..", ".#.....#.", ".#.....#.", "#########" },
            new[] { "#########", ".#.wwww#.", ".#.oooo#.", "..#ooo#..", "...#o#...", "....o....", "...#o#...", "..#...#..", ".#.....#.", ".#..o..#.", "#########" },
            new[] { "#########", ".#.....#.", ".#.ooo.#.", "..#ooo#..", "...#o#...", "....o....", "...#.#...", "..#.o.#..", ".#..o..#.", ".#.ooo.#.", "#########" },
            new[] { "#########", ".#.....#.", ".#.....#.", "..#.o.#..", "...#o#...", "....o....", "...#.#...", "..#...#..", ".#.ooo.#.", ".#ooooo#.", "#########" },
        };

        public static readonly string[][] Mug =
        {
            new[] { "..s...s...", ".s..s..s..", "..........", ".#######..", ".#ooooo###", ".#ooooo#.#", ".#ooooo#.#", ".#ooooo###", ".#######..", "..#####..." },
            new[] { "...s...s..", "..s..s....", ".s........", ".#######..", ".#ooooo###", ".#ooooo#.#", ".#ooooo#.#", ".#ooooo###", ".#######..", "..#####..." },
            new[] { ".s...s....", "..s...s.s.", "....s.....", ".#######..", ".#ooooo###", ".#ooooo#.#", ".#ooooo#.#", ".#ooooo###", ".#######..", "..#####..." },
        };

        /// <summary>Idle: the full hourglass with one grain blinking.</summary>
        public static readonly string[][] Idle = { Hourglass[0], Hourglass[0].Select((row, i) => i == 2 ? ".#oo.oo#." : row).ToArray() };
    }

    sealed class PixelSprite : Animated
    {
        readonly Mood mood;
        readonly Brush ink, accent, paper, steam;
        int drawn = -1;

        /// <summary>`dark`: on the black island the ink pixels turn to paper so the sprite stays visible.</summary>
        public PixelSprite(Mood mood, double size, bool dark)
        {
            this.mood = mood; Width = size; Height = size;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            var t = Theme.Of(Look.Tempo);
            ink = dark ? Theme.WarmPaper : t.Ink;
            accent = t.Accent;
            paper = new SolidColorBrush(Color.FromArgb((byte)(dark ? 128 : 230), 255, 255, 255));
            steam = dark ? new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)) : t.Muted;
            System.Windows.Automation.AutomationProperties.SetName(this, mood == Mood.Rest ? "休息中" : mood == Mood.Focus ? "专注中" : "待机");
        }

        string[][] Frames => mood == Mood.Focus ? PixelArt.Hourglass : mood == Mood.Rest ? PixelArt.Mug : PixelArt.Idle;
        /// <summary>Retro: a few frames per second.</summary>
        double Fps => mood == Mood.Idle ? 0.8 : 3.3;
        int Frame => (int)Math.Floor(Seconds * Fps) % Frames.Length;

        protected override bool Moved() => Frame != drawn;

        protected override void OnRender(DrawingContext g)
        {
            drawn = Frame;
            var frame = Frames[drawn];
            int cols = frame.Max(r => r.Length), rows = frame.Length;
            var px = Math.Floor(Math.Min(ActualWidth / cols, ActualHeight / rows));
            double ox = (ActualWidth - px * cols) / 2, oy = (ActualHeight - px * rows) / 2;
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < frame[r].Length; c++)
                {
                    Brush color;
                    switch (frame[r][c]) { case '#': color = ink; break; case 'o': color = accent; break; case 'w': color = paper; break; case 's': color = steam; break; default: color = null; break; }
                    if (color != null) g.DrawRectangle(color, null, new Rect(ox + c * px, oy + r * px, px, px));
                }
        }
    }
}
