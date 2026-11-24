using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hola.Core;

namespace Hola.Desktop
{
    /// <summary>
    /// The panel that opens from the tray icon, the Windows counterpart of the
    /// macOS popover: sign-in or today's list, the pomodoro, the style and
    /// launch-at-login switches. Every piece reads the active theme.
    /// </summary>
    sealed partial class PopupWindow : Window
    {
        const double PanelWidth = 320, Shadow = 12;
        readonly AppState state;
        readonly Border frame;
        readonly StackPanel root = new StackPanel();
        // Inputs survive a redraw, so typing, focus and caret are never lost.
        readonly TextBox serverBox = new TextBox(), userBox = new TextBox();
        readonly PasswordBox passBox = new PasswordBox();
        string builtKey;
        /// <summary>When the panel last closed, so the tray click that closed it does not reopen it.</summary>
        public DateTime HiddenAt { get; private set; }

        Theme T => state.Theme;

        public PopupWindow(AppState state)
        {
            this.state = state;
            Title = "哗啦待办";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height; Width = PanelWidth + 2 * Shadow;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            frame = new Border
            {
                Margin = new Thickness(Shadow), Padding = new Thickness(14), BorderThickness = new Thickness(1), Child = root,
                Effect = new DropShadowEffect { BlurRadius = Shadow, ShadowDepth = 2, Opacity = 0.22 },
            };
            Content = frame;

            serverBox.TextChanged += (_, __) => { state.ServerText = serverBox.Text; UpdateDynamic(); };
            serverBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Connect(); };
            userBox.TextChanged += (_, __) => { if (state.Username != userBox.Text) state.Username = userBox.Text; };
            passBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
            Deactivated += (_, __) => Hide();
            SizeChanged += (_, __) => Place();
            IsVisibleChanged += (_, __) => { if (!IsVisible) HiddenAt = DateTime.UtcNow; };
            state.Changed += Render;
        }

        System.Drawing.Point anchor;

        /// <summary>Opens above the tray, by the pointer, kept inside the screen's work area.</summary>
        public void ShowNear(System.Drawing.Point cursor)
        {
            anchor = cursor;
            // Drawn off screen first, so it never flashes old content or the wrong size.
            Left = -32000; Top = -32000;
            Show();
            builtKey = null;
            Render();
            UpdateLayout();
            Place();
            Activate();
            state.RefreshIfStale();
            state.RefreshLaunchAtLogin();
        }

        /// <summary>
        /// Bottom-right corner at the anchor, clamped into the work area. Run again
        /// whenever the content changes height (sign-in → list), so it never runs off screen.
        /// </summary>
        void Place()
        {
            if (!IsVisible) return;
            var area = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
            var scale = Dpi.Scale;
            double w = ActualWidth, h = ActualHeight;
            double left = anchor.X / scale - w + Shadow + 8, top = anchor.Y / scale - h + Shadow - 8;
            Left = Math.Max(area.Left / scale - Shadow + 8, Math.Min(left, area.Right / scale - w + Shadow - 8));
            Top = Math.Max(area.Top / scale - Shadow + 8, Math.Min(top, area.Bottom / scale - h + Shadow - 8));
        }

        void Render()
        {
            // A hidden panel is rebuilt when it next opens.
            if (!IsVisible) { builtKey = null; return; }
            var key = LayoutKey();
            if (key != builtKey)
            {
                var focused = Keyboard.FocusedElement;
                builtKey = key;
                Build();
                if (focused == serverBox || focused == userBox || focused == passBox) Dispatcher.BeginInvoke(new Action(() => ((Control)focused).Focus()));
            }
            UpdateDynamic();
        }

        /// <summary>Everything that changes the panel's layout; the clock and status lines update in place.</summary>
        string LayoutKey()
        {
            var s = state;
            var tasks = string.Join("|", s.Tasks.Select(t => $"{t.Id}/{t.Status}/{t.Name}/{t.StackCount}/{Subtitle(t)}/{s.Busy.Contains(t.Id)}"));
            var servers = string.Join("|", s.Servers.Select(x => $"{x.Url}/{x.Name}/{x.Username}"));
            return string.Join("\n", s.Look, s.ConnectScreen, s.SignedIn, s.Account, s.User?.Id, s.User?.Name, s.Server?.ExposesPassword,
                               tasks, servers, s.LaunchAtLogin, s.LaunchAtLoginError, s.ShowConnect, s.Snapshot == null, s.Pomodoro.Mode, s.Pomodoro.IsRunning);
        }

        void Build()
        {
            frame.Background = T.Paper; frame.BorderBrush = T.Line; frame.CornerRadius = new CornerRadius(T.IsTempo ? 0 : 12);
            TextElement(frame);
            root.Children.Clear();
            statusText = noticeText = null; connectBtn = loginBtn = null;
            Add(Header());
            Add(PomodoroSection());
            Add(state.ConnectScreen ? ConnectSection() : state.SignedIn ? TaskSection() : SignInSection());
            Add(Footer());
        }

        void Add(UIElement e)
        {
            if (e is FrameworkElement fe && root.Children.Count > 0) fe.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(e);
        }

        void TextElement(FrameworkElement e)
        {
            TextBlock.SetForeground(e, T.Ink);
            TextBlock.SetFontFamily(e, T.Body.Family);
            TextBlock.SetFontSize(e, T.Body.Size);
        }

        static FrameworkElement Spread(UIElement left, UIElement right)
        {
            var d = new DockPanel { LastChildFill = true };
            if (right != null) { DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); }
            if (left != null) d.Children.Add(left);
            return d;
        }

        void Connect() { if (!state.Connecting && state.ServerText.Trim().Length > 0) _ = state.Connect(); }

        async void Submit()
        {
            var pw = passBox.Password;
            await state.SignIn(pw);
            if (state.SignedIn) passBox.Clear();
        }
    }

    static class Dpi
    {
        /// <summary>Device pixels per WPF unit; the app is system-DPI aware, so one number fits every screen.</summary>
        public static double Scale
        {
            get { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0; }
        }
    }
}
