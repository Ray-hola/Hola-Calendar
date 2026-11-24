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

        /// <summary>Appends to crash.log, skipping a repeat of the last entry and starting over past 1 MB.</summary>
        static void Log(Exception e)
        {
            var text = e.ToString();
            if (text == lastLogged) return;
            lastLogged = text;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLog));
                if (File.Exists(CrashLog) && new FileInfo(CrashLog).Length > 1 << 20) File.Delete(CrashLog);
                File.AppendAllText(CrashLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
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
                    if (GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) && IsOurs((int)pid))
                        AllowSetForegroundWindow(pid);
                    var bytes = Encoding.UTF8.GetBytes(server);
                    pipe.Write(bytes, 0, Math.Min(bytes.Length, MaxMessage));
                }
            }
            catch (Exception e) when (e is IOException || e is TimeoutException || e is UnauthorizedAccessException) { }
        }

        /// <summary>The pipe's owner is this app (any copy of it) in this session, not something squatting on the name.</summary>
        static bool IsOurs(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                using (var me = Process.GetCurrentProcess())
                    return p.SessionId == me.SessionId
                        && string.Equals(p.MainModule.FileVersionInfo.InternalName, me.MainModule.FileVersionInfo.InternalName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
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
