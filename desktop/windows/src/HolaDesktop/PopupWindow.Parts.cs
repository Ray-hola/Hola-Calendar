using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hola.Core;

namespace Hola.Desktop
{
    sealed partial class PopupWindow
    {
        // Parts updated in place every second while the pomodoro runs.
        TextBlock headerRight, clock, modeText, rangText, ascii, statusText, noticeText;
        Bar bar;
        Button toggleBtn, switchBtn, connectBtn, loginBtn;
        Border pomoPanel;

        UIElement Header()
        {
            headerRight = Ui.Text("", T.Small, T.Muted);
            headerRight.VerticalAlignment = VerticalAlignment.Bottom;
            headerRight.Margin = new Thickness(8, 0, 0, 2);
            var title = Ui.Text(T.Heading("今日待办"), T.Title, T.Ink);
            return Spread(title, headerRight);
        }

        UIElement PomodoroSection()
        {
            var p = state.Pomodoro;
            clock = Ui.Text("", T.Clock, T.Ink);
            modeText = Ui.Text("", T.Small, T.Muted);
            rangText = Ui.Text("", T.Small, T.Accent);
            foreach (var t in new[] { modeText, rangText }) { t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = new Thickness(0, 0, 0, 6); }
            var top = Ui.Row(8, clock, modeText, rangText);
            UIElement progress;
            if (T.IsTempo) { ascii = Ui.Text("", T.Mono, T.Ink); progress = ascii; bar = null; }
            else { bar = new Bar(T.Line, T.Accent) { Margin = new Thickness(0, 2, 0, 4) }; progress = bar; ascii = null; }
            toggleBtn = Ui.Button(T, "", state.PomoToggle, primary: true);
            switchBtn = Ui.Button(T, "", state.PomoSwitch);
            var buttons = Ui.Row(T.IsTempo ? 2 : 6, toggleBtn, Ui.Button(T, T.Bracket("重置"), state.PomoReset), switchBtn);
            buttons.Margin = new Thickness(0, 4, 0, 0);
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            text.Children.Add(top); text.Children.Add(progress); text.Children.Add(buttons);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var mascot = Mascot.Make(state.Look, Moods.Of(p), 54);
            mascot.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(mascot);
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            pomoPanel = new Border
            {
                Child = grid, Padding = new Thickness(10), Background = T.Panel, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(T.Radius),
            };
            return pomoPanel;
        }

        UIElement Footer()
        {
            var panel = new StackPanel();
            panel.Children.Add(new Border { Height = 1, Background = T.Line, Margin = new Thickness(0, 0, 0, 6) });
            var picker = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (Look look in Enum.GetValues(typeof(Look)))
            {
                var on = look == state.Look;
                var b = Ui.Link(T, Ui.Text(T.IsTempo ? $"[{look.Label()}]" : look.Label(), on ? new Font(T.Small.Family, T.Small.Size, FontWeights.SemiBold) : T.Small, on ? T.Ink : T.Muted),
                                () => state.Look = look);
                b.Padding = new Thickness(8, 3, 8, 3);
                b.Background = on ? T.Paper : T.Panel;
                b.BorderBrush = T.Line; b.BorderThickness = new Thickness(on ? 1 : 0);
                picker.Children.Add(b);
            }
            var pickerBox = new Border { Child = picker, Background = T.Panel, CornerRadius = new CornerRadius(T.IsTempo ? 0 : 6), Padding = new Thickness(1) };
            var auto = new CheckBox
            {
                Content = Ui.Text(T.IsTempo ? "[自启]" : "开机时启动", T.Small, T.Ink), IsChecked = state.LaunchAtLogin, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "登录 Windows 后自动打开，可在任务管理器的「启动应用」里关闭",
            };
            // Checked/Unchecked, not Click: screen readers and UI Automation toggle without clicking.
            auto.Checked += (_, __) => state.SetLaunchAtLogin(true);
            auto.Unchecked += (_, __) => state.SetLaunchAtLogin(false);
            panel.Children.Add(Spread(pickerBox, auto));
            if (state.LaunchAtLoginError != null)
                panel.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 0), Child = Ui.Text("无法设置开机自启：" + state.LaunchAtLoginError, T.Small, T.Accent, wrap: true) });
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            if (state.SignedIn)
            {
                left.Children.Add(Ui.Button(T, T.Bracket("退出登录"), state.SignOut));
                var sync = Ui.Button(T, T.Bracket("立即同步"), () => _ = state.Refresh());
                sync.Margin = new Thickness(6, 0, 0, 0);
                left.Children.Add(sync);
            }
            var quit = Ui.Button(T, T.Bracket("退出"), Program.Quit);
            var actions = Spread(left, quit);
            actions.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(actions);
            return panel;
        }

        void UpdateDynamic()
        {
            var s = state; var p = s.Pomodoro; var now = s.Now;
            if (headerRight != null) headerRight.Text = s.Snapshot == null ? "" : $"{s.Snapshot.Today} · 还剩 {s.OpenCount} 项";
            if (clock != null) clock.Text = p.Clock(now);
            if (modeText != null) modeText.Text = p.Mode == PomoMode.Focus ? "专注 25 分钟" : "休息 5 分钟";
            if (rangText != null)
                rangText.Text = s.Rang == null ? "" : s.Rang == PomoMode.Focus ? (T.IsTempo ? "*ding* 休息一下" : "专注结束，休息一下") : (T.IsTempo ? "*ding* 继续专注" : "休息结束，继续专注");
            if (bar != null) bar.Value = p.Progress(now);
            if (ascii != null) ascii.Text = p.AsciiBar(now, 14);
            Ui.Caption(toggleBtn, T, T.Bracket(p.IsRunning ? "暂停" : "开始"));
            Ui.Caption(switchBtn, T, T.Bracket(p.Mode == PomoMode.Focus ? "切到休息" : "切到专注"));
            if (pomoPanel != null) pomoPanel.BorderBrush = s.Rang != null ? T.Accent : T.Line;
            if (statusText != null) { statusText.Text = StatusLine(); statusText.Foreground = s.Offline ? T.Accent : T.Muted; }
            if (noticeText != null)
            {
                var n = s.Notice;
                noticeText.Text = n == null ? "" : T.IsTempo && (s.ConnectScreen || s.SignedIn) ? "> " + n : n;
                noticeText.Visibility = string.IsNullOrEmpty(n) ? Visibility.Collapsed : Visibility.Visible;
            }
            // A link that arrives while the connect screen is open replaces what the box shows.
            if (connectBtn != null && serverBox.Text != s.ServerText) { serverBox.Text = s.ServerText; serverBox.CaretIndex = serverBox.Text.Length; }
            if (connectBtn != null)
            {
                Ui.Caption(connectBtn, T, T.Bracket(s.Connecting ? "连接中…" : "连接"));
                connectBtn.IsEnabled = !s.Connecting && s.ServerText.Trim().Length > 0;
            }
            if (loginBtn != null) { Ui.Caption(loginBtn, T, T.Bracket(s.Syncing ? "登录中…" : "登录")); loginBtn.IsEnabled = !s.Syncing; }
        }

        string StatusLine()
        {
            var s = state; var tempo = T.IsTempo;
            var last = s.LastSync?.ToString("HH:mm");
            if (s.Syncing) return tempo ? "[syncing]" : "同步中…";
            if (s.Offline && last != null) return tempo ? $"[offline] last {last}" : $"离线 · 上次同步 {last}";
            if (s.Offline) return tempo ? "[offline]" : "离线";
            if (last != null) return tempo ? $"[synced {last}]" : $"已同步 {last}";
            return "";
        }
    }
}
