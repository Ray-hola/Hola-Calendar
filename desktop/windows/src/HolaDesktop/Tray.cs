using System;
using System.Windows.Forms;
using Hola.Core;

namespace Hola.Desktop
{
    /// <summary>
    /// The notification-area icon, the Windows counterpart of the macOS menu-bar
    /// item: left click opens the panel, right click the quick menu, and while a
    /// session runs the icon shows the minutes left.
    /// </summary>
    sealed class Tray : IDisposable
    {
        readonly AppState state;
        readonly PopupWindow popup;
        readonly NotifyIcon icon;
        BrandIcon.Owned current;
        int shownMinutes = -2;

        public Tray(AppState state, PopupWindow popup)
        {
            this.state = state;
            this.popup = popup;
            icon = new NotifyIcon { Visible = true, ContextMenuStrip = new ContextMenuStrip() };
            icon.ContextMenuStrip.Opening += (_, e) => { FillMenu(); e.Cancel = false; };
            icon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            state.Changed += Render;
            Render();
        }

        /// <summary>Closes the panel if open; a click that itself just closed it (by taking the focus away) does not reopen it.</summary>
        public void Toggle()
        {
            if (popup.IsVisible) { popup.Hide(); return; }
            if ((DateTime.UtcNow - popup.HiddenAt).TotalMilliseconds < 300) return;
            Open();
        }

        /// <summary>`atPointer`: by the pointer (a tray click); otherwise in the tray's corner of the main screen (a link, a second start).</summary>
        public void Open(bool atPointer = true)
        {
            if (popup.IsVisible) { popup.Activate(); return; }
            popup.ShowNear(atPointer ? Cursor.Position : TrayCorner());
        }

        /// <summary>The work-area corner next to the taskbar's notification area, wherever the taskbar is docked.</summary>
        static System.Drawing.Point TrayCorner()
        {
            var s = Screen.PrimaryScreen;
            System.Drawing.Rectangle b = s.Bounds, w = s.WorkingArea;
            if (w.Top > b.Top) return new System.Drawing.Point(w.Right, w.Top);
            if (w.Left > b.Left) return new System.Drawing.Point(w.Left, w.Bottom);
            return new System.Drawing.Point(w.Right, w.Bottom);
        }

        void Render()
        {
            var p = state.Pomodoro;
            var minutes = p.IsRunning ? (p.Remaining(state.Now) + 59) / 60 : -1;
            if (minutes != shownMinutes)
            {
                shownMinutes = minutes;
                var size = SystemInformation.SmallIconSize.Width;
                var next = new BrandIcon.Owned(minutes < 0 ? BrandIcon.Mark(size) : BrandIcon.Minutes(size, minutes));
                icon.Icon = next.Icon;
                current?.Dispose();
                current = next;
            }
            var tip = "哗啦待办" + (p.IsRunning ? " · " + p.Clock(state.Now) : "") + (state.SignedIn ? $" · 还剩 {state.OpenCount} 项" : " · 未登录");
            if (icon.Text != tip) icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void FillMenu()
        {
            var m = icon.ContextMenuStrip;
            m.Items.Clear();
            var p = state.Pomodoro;
            m.Items.Add(p.IsRunning ? "暂停番茄钟" : "开始番茄钟", null, (_, __) => state.PomoToggle());
            m.Items.Add(p.Mode == PomoMode.Focus ? "切到休息" : "切到专注", null, (_, __) => state.PomoSwitch());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("打开面板", null, (_, __) => Open());
            var sync = m.Items.Add("立即同步", null, (_, __) => _ = state.Refresh());
            sync.Enabled = state.SignedIn;
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("退出 哗啦待办", null, (_, __) => Program.Quit());
        }

        public void Dispose()
        {
            state.Changed -= Render;
            icon.Visible = false;
            icon.Dispose();
            current?.Dispose();
        }
    }
}
