using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Hola.Desktop
{
    /// <summary>
    /// The 哗啦待办 sweep mark (three cards swept into one checked card), drawn in
    /// code so the tray gets a crisp icon at any size. Geometry follows
    /// assets/logo/mark-sweep.svg (128×100), like the macOS BrandIcon.
    /// </summary>
    static class BrandIcon
    {
        static readonly Color Terracotta = Color.FromArgb(0xD9, 0x77, 0x57), Paper = Color.FromArgb(0xFF, 0xFD, 0xF9);

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>The mark in terracotta with a paper check, on a transparent square of `size` pixels.</summary>
        public static Bitmap Mark(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size / 128f, dy = (size - 100 * s) / 2;
                void Card(float x, float angle, int alpha)
                {
                    var r = new RectangleF(x * s, 30 * s + dy, 78 * s, 54 * s);
                    var state = g.Save();
                    g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
                    g.RotateTransform(angle);
                    g.TranslateTransform(-(r.X + r.Width / 2), -(r.Y + r.Height / 2));
                    using (var path = Rounded(r, 11 * s)) using (var b = new SolidBrush(Color.FromArgb(alpha, Terracotta))) g.FillPath(b, path);
                    g.Restore(state);
                }
                // y points down here, so the SVG's counter-clockwise -20° is +20°.
                Card(22, 20, 56);
                Card(26, 10, 128);
                Card(30, 0, 255);
                using (var pen = new Pen(Paper, Math.Max(1.5f, 8 * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(53 * s, 57 * s + dy), new PointF(63 * s, 67 * s + dy), new PointF(84 * s, 45 * s + dy) });
            }
            return bmp;
        }

        /// <summary>While a session runs: the minutes left, paper on a terracotta tile, readable at 16 px.</summary>
        public static Bitmap Minutes(int size, int minutes)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var path = Rounded(new RectangleF(0, 0, size - 1, size - 1), size * 0.22f)) using (var b = new SolidBrush(Terracotta)) g.FillPath(b, path);
                var text = Math.Min(99, Math.Max(0, minutes)).ToString();
                using (var font = new System.Drawing.Font("Segoe UI", size * (text.Length > 1 ? 0.56f : 0.66f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Paper))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, font, brush, new RectangleF(0, 0, size, size + size * 0.04f), fmt);
            }
            return bmp;
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        /// <summary>An Icon that owns its handle: Dispose frees it.</summary>
        public sealed class Owned : IDisposable
        {
            public readonly Icon Icon;
            readonly IntPtr handle;
            public Owned(Bitmap bmp) { handle = bmp.GetHicon(); Icon = Icon.FromHandle(handle); bmp.Dispose(); }
            public void Dispose() { Icon.Dispose(); DestroyIcon(handle); }
        }
    }
}
