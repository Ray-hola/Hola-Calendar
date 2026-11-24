using System.Windows;
using System.Windows.Controls;
using Hola.Core;

namespace Hola.Desktop
{
    sealed partial class PopupWindow
    {
        Font SubHeading => T.IsTempo ? T.Title : T.Sub;

        UIElement ConnectSection()
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Text(T.Heading("连接服务器"), SubHeading, T.Ink));
            var hint = Ui.Text("填你们团队哗啦待办的网址。在网页版点头像 →「连接桌面 App」可以自动填好。", T.Small, T.Muted, wrap: true);
            hint.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(hint);
            if (serverBox.Text != state.ServerText) { serverBox.Text = state.ServerText; serverBox.CaretIndex = serverBox.Text.Length; }
            connectBtn = Ui.Button(T, "", Connect, primary: true);
            connectBtn.Margin = new Thickness(8, 0, 0, 0);
            var row = Spread(Ui.Field(T, Detach(serverBox), "work.example.com 或 192.168.1.8:4173"), connectBtn);
            row.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(row);
            var n = Notice(3); n.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(n);
            if (state.Servers.Count > 0)
            {
                var used = Ui.Text(T.IsTempo ? "-- 用过的服务器 --" : "用过的服务器", T.Small, T.Muted);
                used.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(used);
                foreach (var s in state.Servers)
                {
                    var pick = s;
                    var lines = new StackPanel();
                    lines.Children.Add(Ui.Text(s.Host, T.Body, T.Ink));
                    lines.Children.Add(Ui.Text(s.Username.Length == 0 ? s.Name : $"{s.Name} · {s.Username}", T.Small, T.Muted));
                    var remove = Ui.Link(T, Ui.Text(T.IsTempo ? "[x]" : "移除", T.Small, T.Muted), () => state.Forget(pick), "忘记这台服务器和它的登录状态");
                    remove.VerticalAlignment = VerticalAlignment.Center;
                    remove.Margin = new Thickness(6, 0, 0, 0);
                    var item = Spread(Ui.Link(T, lines, () => state.SwitchTo(pick)), remove);
                    item.Margin = new Thickness(0, 3, 0, 3);
                    panel.Children.Add(item);
                }
            }
            if (state.Server != null)
            {
                var cancel = Ui.Button(T, T.Bracket("取消"), state.CancelConnect);
                cancel.HorizontalAlignment = HorizontalAlignment.Left;
                cancel.Margin = new Thickness(0, 8, 0, 0);
                panel.Children.Add(cancel);
            }
            return panel;
        }

        UIElement SignInSection()
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Text(T.Heading("登录工作台"), SubHeading, T.Ink));
            var change = Ui.Link(T, Ui.Text(T.IsTempo ? "[更换]" : "更换", T.Small, T.Accent), state.ShowConnectScreen);
            change.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(Labeled("服务器", Spread(Ui.Text(state.Server?.Host ?? "", T.Body, T.Ink), change)));
            if (state.Server?.ExposesPassword == true)
            {
                var warn = Ui.Text("这台服务器没有用 https，密码会以明文在网上传输。请让管理员开启 https。", T.Small, T.Accent, wrap: true);
                warn.Margin = new Thickness(0, 8, 0, 0);
                panel.Children.Add(warn);
            }
            if (userBox.Text != state.Username) userBox.Text = state.Username;
            panel.Children.Add(Labeled("账户", Ui.Field(T, Detach(userBox), "用户名")));
            panel.Children.Add(Labeled("密码", Ui.Field(T, Detach(passBox), "密码")));
            loginBtn = Ui.Button(T, "", Submit, primary: true);
            var n = Notice(2); n.Margin = new Thickness(8, 0, 0, 0); n.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(loginBtn, Dock.Left);
            row.Children.Add(loginBtn); row.Children.Add(n);
            panel.Children.Add(row);
            var foot = Ui.Text("登录状态只保存在 Windows 凭据管理器里，不写入任何文件。", T.Small, T.Muted, wrap: true);
            foot.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(foot);
            return panel;
        }

        FrameworkElement Labeled(string label, UIElement field)
        {
            var name = Ui.Text(T.IsTempo ? label + ":" : label, T.Small, T.Muted);
            name.Width = 64; name.VerticalAlignment = VerticalAlignment.Center;
            var d = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(name, Dock.Left);
            d.Children.Add(name); d.Children.Add(field);
            return d;
        }

        /// <summary>Takes a kept input out of the previous layout so it can be placed in the new one.</summary>
        static Control Detach(Control input)
        {
            if (input.Parent is Panel p) p.Children.Remove(input);
            return input;
        }
    }
}
