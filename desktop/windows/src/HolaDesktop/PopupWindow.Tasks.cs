using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hola.Core;

namespace Hola.Desktop
{
    sealed partial class PopupWindow
    {
        TextBlock Notice(int lines)
        {
            noticeText = Ui.Text("", T.Small, T.Accent, wrap: true);
            noticeText.MaxHeight = lines * (T.Small.Size * 1.4);
            return noticeText;
        }

        // Tasks

        ScrollViewer taskList;
        /// <summary>Where the list was scrolled to before a redraw, so ticking a task does not jump it to the top.</summary>
        double taskScroll;

        UIElement TaskSection()
        {
            var panel = new StackPanel();
            var who = new StackPanel { Orientation = Orientation.Horizontal };
            if (state.User != null) who.Children.Add(Ui.Text(T.IsTempo ? "@" + state.User.Name : state.User.Name, T.Small, T.Muted));
            var menu = ServerMenu();
            menu.Margin = new Thickness(6, 0, 0, 0);
            who.Children.Add(menu);
            statusText = Ui.Text("", T.Small, T.Muted);
            panel.Children.Add(Spread(who, statusText));
            if (state.Tasks.Count == 0)
            {
                var none = Ui.Text(T.IsTempo ? "~ 今天没有待办 ~" : "今天没有待办", T.Body, T.Muted);
                none.Margin = new Thickness(0, 8, 0, 8);
                panel.Children.Add(none);
            }
            else
            {
                var list = new StackPanel();
                foreach (var t in state.Tasks) list.Children.Add(TaskRow(t));
                taskList = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
                var keep = taskScroll;
                var shown = taskList;
                shown.Loaded += (_, __) => { shown.ScrollToVerticalOffset(keep); shown.UpdateLayout(); };
                panel.Children.Add(taskList);
            }
            var n = Notice(2); n.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(n);
            return panel;
        }

        UIElement TaskRow(Todo t)
        {
            var user = state.User ?? new User("", "", true);
            var busy = state.Busy.Contains(t.Id);
            var tickable = TodayFilter.CanComplete(t, user) && !busy;
            var name = Ui.Text(t.Name + (t.StackCount > 1 ? $" · {t.StackCount} 期" : ""), T.Body, t.IsOpen ? T.Ink : T.Muted, wrap: true);
            if (!t.IsOpen) name.TextDecorations = TextDecorations.Strikethrough;
            var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            text.Children.Add(name);
            text.Children.Add(Ui.Text(Subtitle(t), T.Small, T.Muted));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Ui.Check(T, !t.IsOpen, tickable));
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            if (busy) { var wait = Ui.Text("…", T.Body, T.Muted); Grid.SetColumn(wait, 2); grid.Children.Add(wait); }
            var row = Ui.Link(T, grid, () => _ = state.Complete(t), tickable ? "标记完成" : t.IsOpen ? "只能记录自己的任务" : StatusText(t), dim: false);
            row.Padding = new Thickness(4);
            row.IsEnabled = tickable;
            // A disabled row still explains itself on hover.
            ToolTipService.SetShowOnDisabled(row, true);
            return row;
        }

        string Subtitle(Todo t)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (t.Time != null) parts.Add(t.Time);
            var me = state.Snapshot?.User;
            if (me?.Admin == true && me.Id != t.Assignee) parts.Add(t.Assignee);
            parts.Add((t.ProjectId == null ? null : state.Snapshot?.Projects.FirstOrDefault(p => p.Id == t.ProjectId)?.Name) ?? "个人");
            parts.Add(t.Priority);
            if (!t.IsOpen) parts.Add(StatusText(t));
            return string.Join(" · ", parts);
        }

        static string StatusText(Todo t)
        {
            switch (t.Status)
            {
                case "done": return "已完成";
                case "pending_approval": case "awaiting_approval": case "submitted": return "待审批";
                case "doing": return "进行中";
                default: return "待开始";
            }
        }

        // Servers

        /// <summary>The current server's host; opens the remembered list and 连接新服务器.</summary>
        Button ServerMenu()
        {
            var host = state.Server?.Host ?? "";
            Button b = null;
            b = Ui.Link(T, Ui.Text(T.IsTempo ? $"[{host}]" : "· " + host, T.Small, T.Muted), () =>
            {
                var m = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
                foreach (var s in state.Servers)
                {
                    var item = new MenuItem { Header = $"{s.Name} · {s.Host}", IsChecked = s.Url == state.Account };
                    var pick = s; item.Click += (_, __) => state.SwitchTo(pick);
                    m.Items.Add(item);
                }
                m.Items.Add(new Separator());
                var add = new MenuItem { Header = "连接新服务器…" }; add.Click += (_, __) => state.ShowConnectScreen(); m.Items.Add(add);
                var current = state.Servers.FirstOrDefault(s => s.Url == state.Account);
                if (current != null) { var f = new MenuItem { Header = "忘记 " + current.Host }; f.Click += (_, __) => state.Forget(current); m.Items.Add(f); }
                m.IsOpen = true;
            }, "切换服务器");
            return b;
        }
    }
}
