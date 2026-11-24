using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hola.Core;

namespace Hola.Desktop
{
    /// <summary>
    /// Starts the tray app. Only one copy runs per Windows session: a second
    /// start (a `hola://` link from the browser, or a double-click on the exe)
    /// hands its link to the running copy and exits.
    /// </summary>
    static class Program
    {
        static readonly string Id = $"cn.hola.desktop.{Environment.UserName}.{Process.GetCurrentProcess().SessionId}";
        const int MaxMessage = 4096;
        static Tray tray;

        /// <summary>Set once the app is really quitting, so the panel's Alt+F4 only hides it until then.</summary>
        public static bool Quitting { get; private set; }

        public static void Quit()
        {
            Quitting = true;
            Application.Current.Shutdown();
        }

        [STAThread]
        static int Main(string[] args)
        {
            var link = args.Length > 0 ? ConnectLink.Server(args[0]) : null;
            using (var only = new Mutex(true, @"Local\" + Id, out var first))
            {
                if (!first) { Forward(link ?? ""); return 0; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Awaits started before app.Run() must come back to this thread too.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
                CatchCrashes(app);
                AppState state = null;
                // Listening starts before anything is built, so a link from a slow cold start is not dropped:
                // what arrives waits in the dispatcher queue, which only runs once everything below exists.
                Listen(message => app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (message.Length > 0) state.OpenConnectLink(message);
                    tray.Open(atPointer: false);
                })));
                app.SessionEnding += (_, __) => { Quitting = true; state?.Flush(); };
                Startup.RegisterLinkHandler();
                state = new AppState(new Settings(), app.Dispatcher);
                var popup = new PopupWindow(state);
                var island = new IslandWindow(state);
                tray = new Tray(state, popup);
                app.Exit += (_, __) => { Quitting = true; state.Flush(); tray.Dispose(); };
                if (link != null) app.Dispatcher.BeginInvoke(new Action(() => { state.OpenConnectLink(link); tray.Open(atPointer: false); }));
                app.Run();
                GC.KeepAlive(island);
                return 0;
            }
        }

        static string CrashLog => Path.Combine(Path.GetDirectoryName(Settings.DefaultPath), "crash.log");

        static string lastLogged;
        static readonly object logLock = new object();

        /// <summary>Appends to crash.log, skipping a repeat of the last entry and starting over past 1 MB.</summary>
        internal static void Log(Exception e)
        {
            var text = e.ToString();
            lock (logLock)
            {
                if (text == lastLogged) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CrashLog));
                    try
                    {
                        if (File.Exists(CrashLog) && new FileInfo(CrashLog).Length > 1 << 20)
                        {
                            var old = Path.ChangeExtension(CrashLog, ".old.log");
                            File.Delete(old);
                            File.Move(CrashLog, old);
                        }
                    }
                    catch (IOException) { }   // held open elsewhere: keep appending to it
                    File.AppendAllText(CrashLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
                    lastLogged = text;
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// A bug in one click should not take the tray app down: it is written to
        /// crash.log and the app carries on. If the process ends anyway, the tray icon goes with it.
        /// </summary>
        static void CatchCrashes(Application app)
        {
            app.DispatcherUnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
            System.Windows.Forms.Application.ThreadException += (_, e) => Log(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception x) Log(x);
                try { tray?.Dispose(); } catch (Exception) { }
            };
            TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception); e.SetObserved(); };
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(uint processId);

        /// <summary>Gives the running copy a server address from a link ("" just opens its panel).</summary>
        static void Forward(string server)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", Id, PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes, PipeOptions.None,
                    System.Security.Principal.TokenImpersonationLevel.Identification, HandleInheritability.None))
                {
                    pipe.Connect(3000);
                    // This start came from a click, so it may hand the foreground on: the panel then gets the focus.
                    if (GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid))
                    {
                        var owner = Owner((int)pid);
                        if (owner == false) return;   // someone else holds the name: they get nothing
                        if (owner == true) AllowSetForegroundWindow(pid);
                    }
                    var bytes = Encoding.UTF8.GetBytes(server);
                    pipe.Write(bytes, 0, Math.Min(bytes.Length, MaxMessage));
                }
            }
            catch (Exception e) when (e is IOException || e is TimeoutException || e is UnauthorizedAccessException) { }
        }

        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(int pid, out int session);

        /// <summary>
        /// Whether the pipe's owner is this app (any copy of it) in this session: true, false, or
        /// null when it cannot be told. Limited query rights also work on a copy run as administrator.
        /// </summary>
        static bool? Owner(int pid)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var name = new StringBuilder(1024); var size = name.Capacity;
                if (!QueryFullProcessImageName(handle, 0, name, ref size)) return null;
                if (!ProcessIdToSessionId(pid, out var theirs) || !ProcessIdToSessionId(Process.GetCurrentProcess().Id, out var mine)) return null;
                if (theirs != mine) return false;
                // The build stamps the exe's own name inside it, which stays the same when the file is renamed or copied.
                string them = FileVersionInfo.GetVersionInfo(name.ToString()).InternalName, me = FileVersionInfo.GetVersionInfo(Startup.ExePath).InternalName;
                return string.Equals(them, me, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is IOException || e is System.ComponentModel.Win32Exception || e is UnauthorizedAccessException) { return null; }
            finally { CloseHandle(handle); }
        }

        /// <summary>Only this account may connect, and only from this machine.</summary>
        static PipeSecurity Security()
        {
            var s = new PipeSecurity();
            s.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            return s;
        }

        /// <summary>
        /// Waits for later starts. What arrives is read up to 4 KB and checked
        /// again as a server address before it fills anything in. Any failure is
        /// logged and the wait starts over, so links keep working.
        /// </summary>
        static void Listen(Action<string> received)
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, Security()))
                        {
                            pipe.WaitForConnection();
                            var buffer = new byte[MaxMessage];
                            int length = 0, n;
                            while (length < buffer.Length && (n = pipe.Read(buffer, length, buffer.Length - length)) > 0) length += n;
                            var text = Encoding.UTF8.GetString(buffer, 0, length);
                            received(text.Length == 0 ? "" : HolaApi.Normalize(text) ?? "");
                        }
                    }
                    catch (Exception e)
                    {
                        if (!(e is IOException)) Log(e);
                        Thread.Sleep(e is IOException ? 200 : 2000);
                    }
                }
            });
        }
    }
}
