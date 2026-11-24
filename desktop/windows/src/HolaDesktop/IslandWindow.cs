using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Hola.Core;

namespace Hola.Desktop
{
    /// <summary>
    /// The 「灵动岛」. Windows has no notch, so it is the macOS app's no-notch
    /// fallback: a black pill at the top centre of the screen the pointer is on,
    /// shown while a pomodoro runs (or has just rung). It grows on hover to show
    /// the current to-do; a click pauses or resumes. It never takes the focus.
    /// </summary>
    sealed class IslandWindow : Window
    {
        const double PillW = 168, PillH = 28, Extra = 68, WideW = 340, Gap = 6;
        readonly AppState state;
        readonly Border shape;
        bool expanded, shown;
        string builtKey;
        TextBlock clock, modeText, bell, nextName, openText, asciiSmall, asciiWide, hint;
        Ring ring;
        Bar bar;

        Theme T => state.Theme;

        public IslandWindow(AppState state)
        {
            this.state = state;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = false; Focusable = false;
            Width = PillW; Height = PillH; Opacity = 0;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            shape = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand };
            Content = shape;
            MouseEnter += (_, __) => SetExpanded(true);
            MouseLeave += (_, __) => SetExpanded(false);
            MouseLeftButtonUp += (_, __) => state.PomoToggle();
            SourceInitialized += (_, __) => NoActivate();
            Closing += (_, e) => { if (!Program.Quitting) e.Cancel = true; };
            state.Changed += Update;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, __) => Dispatcher.BeginInvoke(new Action(Place));
            Update();
        }

        bool ShouldShow => state.Pomodoro.IsRunning || state.Rang != null;

        void Update()
        {
            if (!ShouldShow) { Fade(false); return; }
            var key = $"{state.Look}|{expanded}|{Moods.Of(state.Pomodoro)}";
            if (key != builtKey) { builtKey = key; Build(); }
            Fill();
            Place();
            Fade(true);
        }

        void SetExpanded(bool on)
        {
            if (on == expanded) return;
            expanded = on;
            Update();
        }

        /// <summary>Top centre of the screen under the pointer, just below the top of its work area.</summary>
        void Place()
        {
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            var scale = Dpi.Scale;
            var area = screen.WorkingArea;
            double w = expanded ? WideW : PillW, h = expanded ? PillH + Extra : PillH;
            Width = w; Height = h;
            Left = (area.Left + area.Width / 2.0) / scale - w / 2;
            Top = area.Top / scale + Gap;
        }

        void Fade(bool visible)
        {
            if (visible == shown) return;
            shown = visible;
            if (visible) { Show(); BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))); return; }
            expanded = false; builtKey = null;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            a.Completed += (_, __) => { if (!shown) Hide(); };
            BeginAnimation(OpacityProperty, a);
        }

        Font F(double size, FontWeight weight, bool serif = false) =>
            new Font(T.IsTempo ? T.Mono.Family : serif ? T.Sub.Family : T.Body.Family, size, weight);

        void Build()
        {
            var fg = Theme.WarmPaper;
            var top = new DockPanel { Height = PillH, Margin = new Thickness(12, 0, 12, 0) };
            clock = Ui.Text("", F(14, T.IsTempo ? FontWeights.Bold : FontWeights.SemiBold), fg);
            clock.VerticalAlignment = VerticalAlignment.Center;
            var mascot = Mascot.Make(state.Look, Moods.Of(state.Pomodoro), 20, dark: true);
            var left = Ui.Row(6, mascot, clock);
            left.VerticalAlignment = VerticalAlignment.Center;
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            bell = Ui.Text("\uEA8F", new Font(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), 10, FontWeights.Normal), Theme.Terracotta);
            bell.Margin = new Thickness(6, 0, 0, 0); bell.VerticalAlignment = VerticalAlignment.Center;
            if (T.IsTempo) { asciiSmall = Ui.Text("", F(11, FontWeights.Normal), fg); right.Children.Add(asciiSmall); ring = null; modeText = null; }
            else
            {
                modeText = Ui.Text("", F(11, FontWeights.Medium, serif: true), fg);
                modeText.Opacity = 0.8; modeText.VerticalAlignment = VerticalAlignment.Center; modeText.Margin = new Thickness(0, 0, 6, 0);
                ring = new Ring { VerticalAlignment = VerticalAlignment.Center };
                right.Children.Add(modeText); right.Children.Add(ring); asciiSmall = null;
            }
            right.Children.Add(bell);
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right); top.Children.Add(left);
            var all = new StackPanel();
            all.Children.Add(top);
            nextName = openText = asciiWide = hint = null; bar = null;
            if (expanded) all.Children.Add(Detail(fg));
            shape.Child = all;
        }

        UIElement Detail(Brush fg)
        {
            var p = new StackPanel { Margin = new Thickness(16, 0, 16, 10), Height = Extra - 10 };
            var label = Ui.Text(T.IsTempo ? "> 当前待办" : "当前待办", F(11, FontWeights.Medium, serif: true), fg);
            label.Opacity = 0.65;
            openText = Ui.Text("", F(11, FontWeights.Normal), fg);
            openText.Opacity = 0.65;
            var head = new DockPanel();
            DockPanel.SetDock(openText, Dock.Right);
            head.Children.Add(openText); head.Children.Add(label);
            p.Children.Add(head);
            nextName = Ui.Text("", T.IsTempo ? F(13, FontWeights.Bold) : F(14, FontWeights.SemiBold, serif: true), fg);
            nextName.Margin = new Thickness(0, 4, 0, 4);
            p.Children.Add(nextName);
            if (T.IsTempo) { asciiWide = Ui.Text("", F(11, FontWeights.Normal), fg); p.Children.Add(asciiWide); }
            else { bar = new Bar(new SolidColorBrush(Color.FromArgb(64, 255, 253, 249)), Theme.Terracotta) { Margin = new Thickness(0, 2, 0, 2) }; p.Children.Add(bar); }
            hint = Ui.Text("", new Font(T.Body.Family, 10, FontWeights.Normal), fg);
            hint.Opacity = 0.5; hint.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(hint);
            return p;
        }

        void Fill()
        {
            var p = state.Pomodoro; var now = state.Now;
            clock.Text = p.Clock(now);
            if (modeText != null) modeText.Text = p.Mode == PomoMode.Focus ? "专注" : "休息";
            if (ring != null) ring.Value = p.Progress(now);
            if (asciiSmall != null) asciiSmall.Text = p.AsciiBar(now, 6);
            bell.Visibility = state.Rang != null ? Visibility.Visible : Visibility.Collapsed;
            if (openText != null) openText.Text = state.SignedIn ? $"还剩 {state.OpenCount} 项" : "未登录";
            if (nextName != null) nextName.Text = state.NextTask?.Name ?? (state.SignedIn ? "今天没有待办了" : "点托盘图标登录");
            if (asciiWide != null) asciiWide.Text = p.AsciiBar(now, 24);
            if (bar != null) bar.Value = p.Progress(now);
            if (hint != null) hint.Text = p.IsRunning ? "点一下暂停" : "点一下继续";
        }

        // Never activated, never in Alt+Tab: it must not take the keyboard from whatever is being typed in.
        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        void NoActivate()
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
    }
}
