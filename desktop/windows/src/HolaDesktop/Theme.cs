using System.Windows;
using System.Windows.Media;

namespace Hola.Desktop
{
    /// <summary>
    /// The two looks the web app is getting. Anthropic风 is today's warm editorial
    /// style (paper, terracotta, serif headings); Tempo风 is monospace,
    /// typewriter-retro with ASCII furniture.
    /// </summary>
    enum Look { Anthropic, Tempo }

    static class Looks
    {
        public static string Label(this Look s) => s == Look.Anthropic ? "Anthropic风" : "Tempo风";
        public static string Key(this Look s) => s == Look.Anthropic ? "anthropic" : "tempo";
        public static Look Parse(string key) => key == "tempo" ? Look.Tempo : Look.Anthropic;
        public const string SettingsKey = "style";
    }

    sealed class Font
    {
        public readonly FontFamily Family;
        public readonly double Size;
        public readonly FontWeight Weight;
        public Font(FontFamily family, double size, FontWeight weight) { Family = family; Size = size; Weight = weight; }
    }

    sealed class Theme
    {
        public Look Look;
        public Brush Paper, Ink, Accent, Muted, Line, Panel;
        public Font Title, Body, Small, Clock, Mono, Sub;

        static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
        static Brush Solid(int hex) { var b = new SolidColorBrush(Rgb(hex)); b.Freeze(); return b; }

        public static readonly Color TerracottaColor = Rgb(0xD97757), WarmPaperColor = Rgb(0xFFFDF9), InkColor = Rgb(0x1F1B15);
        public static readonly Brush Terracotta = Solid(0xD97757), WarmPaper = Solid(0xFFFDF9);

        // Chinese text falls back to 微软雅黑, never to 宋体, matching the web app's font rule.
        static readonly FontFamily Serif = new FontFamily("Georgia, Microsoft YaHei UI");
        static readonly FontFamily Sans = new FontFamily("Segoe UI, Microsoft YaHei UI");
        static readonly FontFamily MonoFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        static readonly Theme anthropic = new Theme
        {
            Look = Look.Anthropic,
            Paper = WarmPaper, Ink = Solid(0x1F1B15), Accent = Terracotta, Muted = Solid(0x7C7466), Line = Solid(0xE2DDD0), Panel = Solid(0xF7F4ED),
            Title = new Font(Serif, 17, FontWeights.SemiBold), Body = new Font(Sans, 13, FontWeights.Normal), Small = new Font(Sans, 11, FontWeights.Normal),
            Clock = new Font(Sans, 30, FontWeights.Medium), Mono = new Font(MonoFamily, 12, FontWeights.Normal), Sub = new Font(Serif, 13, FontWeights.SemiBold),
        };

        static readonly Theme tempo = new Theme
        {
            Look = Look.Tempo,
            Paper = Solid(0xF3EFE4), Ink = Solid(0x2B2722), Accent = Solid(0xB44C2E), Muted = Solid(0x7A7368), Line = Solid(0xC9C2B3), Panel = Solid(0xECE6D8),
            Title = new Font(MonoFamily, 13, FontWeights.Bold), Body = new Font(MonoFamily, 12, FontWeights.Normal), Small = new Font(MonoFamily, 11, FontWeights.Normal),
            Clock = new Font(MonoFamily, 28, FontWeights.Bold), Mono = new Font(MonoFamily, 12, FontWeights.Normal), Sub = new Font(MonoFamily, 13, FontWeights.Bold),
        };

        public static Theme Of(Look s) => s == Look.Tempo ? tempo : anthropic;

        public bool IsTempo => Look == Look.Tempo;
        public double Radius => IsTempo ? 0 : 10;
        /// <summary>Section heading: serif in Anthropic风, `== 标题 ==` in Tempo风.</summary>
        public string Heading(string s) => IsTempo ? $"== {s} ==" : s;
        /// <summary>Button caption: `[ 开始 ]` in Tempo风.</summary>
        public string Bracket(string s) => IsTempo ? $"[ {s} ]" : s;
    }
}
