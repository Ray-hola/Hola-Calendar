using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

        [STAThread]
        static int Main(string[] args)
        {
            var link = args.Length > 0 ? ConnectLink.Server(args[0]) : null;
            using (var only = new Mutex(true, @"Local\" + Id, out var first))
            {
                if (!first) { Forward(link ?? ""); return 0; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Startup.RegisterLinkHandler();
                var state = new AppState(new Settings(), app.Dispatcher);
                var popup = new PopupWindow(state);
                var island = new IslandWindow(state);
                var tray = new Tray(state, popup);
                Listen(message => app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (message.Length > 0) state.OpenConnectLink(message);
                    tray.Open(atPointer: false);
                })));
                app.Exit += (_, __) => tray.Dispose();
                if (link != null) app.Dispatcher.BeginInvoke(new Action(() => { state.OpenConnectLink(link); tray.Open(atPointer: false); }));
                app.Run();
                GC.KeepAlive(island);
                return 0;
            }
        }

        /// <summary>Gives the running copy a server address from a link ("" just opens its panel).</summary>
        static void Forward(string server)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", Id, PipeDirection.Out))
                {
                    pipe.Connect(3000);
                    var bytes = Encoding.UTF8.GetBytes(server);
                    pipe.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception e) when (e is IOException || e is TimeoutException || e is UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Waits for later starts. Other accounts get no write access to the pipe, and what
        /// arrives is checked again as a server address before it fills anything in.
        /// </summary>
        static void Listen(Action<string> received)
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            pipe.WaitForConnection();
                            var buffer = new MemoryStream();
                            pipe.CopyTo(buffer);
                            var text = Encoding.UTF8.GetString(buffer.ToArray(), 0, (int)Math.Min(buffer.Length, 4096));
                            received(text.Length == 0 ? "" : HolaApi.Normalize(text) ?? "");
                        }
                    }
                    catch (IOException) { Thread.Sleep(200); }
                }
            });
        }
    }
}
