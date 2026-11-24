using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Hola.Desktop
{
    /// <summary>
    /// Per-user registration, no administrator rights needed: the Run key for
    /// launch at login and the `hola://` link handler, both under HKCU.
    /// </summary>
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        /// <summary>Where Task Manager's 启动应用 records an app switched off there.</summary>
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string Name = "HolaDesktop";
        const string ProtocolKey = @"Software\Classes\" + Hola.Core.ConnectLink.Scheme;

        public static string ExePath { get { using (var p = Process.GetCurrentProcess()) return p.MainModule.FileName; } }
        static string Command => "\"" + ExePath + "\"";

        /// <summary>
        /// Run straight from the zip, Explorer starts a copy in %TEMP% that is
        /// cleaned away later; nothing may be registered to point there.
        /// </summary>
        public static bool RunningFromTemp => Long(ExePath).StartsWith(Long(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetLongPathName(string shortPath, System.Text.StringBuilder longPath, int size);

        /// <summary>`C:\Users\RAYHOL~1\...` written out in full, so two spellings of one folder compare equal.</summary>
        static string Long(string path)
        {
            var full = System.IO.Path.GetFullPath(path);
            var sb = new System.Text.StringBuilder(1024);
            var n = GetLongPathName(full, sb, sb.Capacity);
            return n > 0 && n < sb.Capacity ? sb.ToString() : full;
        }

        /// <summary>On when the Run entry starts this very exe and Task Manager has not switched it off.</summary>
        public static bool LaunchAtLoginEnabled
        {
            get
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (!string.Equals(run?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase)) return false;
                }
                using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    // First byte 2 = allowed, 3 = switched off in Task Manager.
                    return !(approved?.GetValue(Name) is byte[] b && b.Length > 0 && b[0] % 2 == 1);
                }
            }
        }

        public static void SetLaunchAtLogin(bool on)
        {
            if (on && RunningFromTemp) throw new InvalidOperationException("App 是从压缩包里直接打开的，请先解压到固定位置再开启");
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) run.SetValue(Name, Command, RegistryValueKind.String); else run.DeleteValue(Name, false);
            }
            // Turning it on here also undoes a 禁用 from Task Manager, so the switch means what it says.
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true)) approved?.DeleteValue(Name, false);
        }

        /// <summary>
        /// Points `hola://` at this exe, so the web app's 「在桌面 App 中打开」 reaches
        /// it. Rewritten on each start, which keeps it right after the exe moves.
        /// </summary>
        public static void RegisterLinkHandler()
        {
            if (RunningFromTemp) return;
            try
            {
                using (var root = Registry.CurrentUser.CreateSubKey(ProtocolKey))
                {
                    root.SetValue("", "URL:哗啦待办");
                    root.SetValue("URL Protocol", "");
                    using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue("", Command + ",0");
                    using (var cmd = root.CreateSubKey(@"shell\open\command")) cmd.SetValue("", Command + " \"%1\"");
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException || e is System.IO.IOException)
            {
                // Links from the web then do nothing; typing the address still works.
            }
        }
    }
}
