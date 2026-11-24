using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hola.Core;

// holactl — a tiny command-line client for a 哗啦待办 server. Used by the
// integration test and handy for checking a server from a terminal.
//
//   holactl login  <server> <user>      password from %HOLA_PASSWORD% or a prompt
//   holactl today  <server>             today's to-dos for the signed-in user
//   holactl complete <server> <taskId>  tick a to-do
//   holactl logout <server>
//
// The token is kept in Credential Manager (the same entry the app uses) unless
// %HOLA_TOKEN% is set (tests) or `--print-token` is passed to `login`, which
// prints it instead of storing it.
namespace Hola.Ctl
{
    static class Program
    {
        static int Fail(string msg) { Console.Error.WriteLine(msg); return 1; }

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try { return Run(args).GetAwaiter().GetResult(); }
            catch (ApiException e) { return Fail("error: " + e.Message); }
        }

        static async Task<int> Run(string[] args)
        {
            var server = args.Length >= 2 ? HolaApi.Normalize(args[1]) : null;
            if (server == null) return Fail("usage: holactl login|today|complete|logout <server> [user|taskId] [--print-token]");
            var api = new HolaApi(server);
            string Token()
            {
                var t = Environment.GetEnvironmentVariable("HOLA_TOKEN");
                if (!string.IsNullOrEmpty(t)) return t;
                return CredentialStore.Load(server);
            }
            switch (args[0])
            {
                case "login":
                {
                    if (args.Length < 3) return Fail("login needs <server> <user>");
                    var password = Environment.GetEnvironmentVariable("HOLA_PASSWORD") ?? "";
                    if (password.Length == 0) password = ReadPassword("密码 password: ");
                    var t = await api.Login(args[2], password);
                    if (args.Contains("--print-token")) Console.WriteLine(t);
                    else { CredentialStore.Save(t, server); Console.WriteLine("signed in; token stored in Credential Manager"); }
                    return 0;
                }
                case "today":
                {
                    var tok = Token();
                    if (tok == null) return Fail($"not signed in to {server}; run: holactl login {args[1]} <user>");
                    var snap = await api.State(tok);
                    var tasks = TodayFilter.Tasks(snap);
                    Console.WriteLine($"{snap.User.Name} · {snap.Today} · 还剩 {TodayFilter.OpenCount(tasks)} 项");
                    foreach (var t in tasks)
                        Console.WriteLine($"{(t.IsOpen ? "[ ]" : "[x]")} {t.Id}  {t.Priority}  {t.Name}{(t.StackCount > 1 ? $" · {t.StackCount} 期" : "")}");
                    return 0;
                }
                case "complete":
                {
                    if (args.Length < 3) return Fail("complete needs <server> <taskId>");
                    var tok = Token();
                    if (tok == null) return Fail($"not signed in to {server}; run: holactl login {args[1]} <user>");
                    var snap = await api.State(tok);
                    var task = snap.Tasks.FirstOrDefault(x => x.Id == args[2]);
                    if (task == null) return Fail("no such task " + args[2]);
                    await api.Complete(task, tok);
                    Console.WriteLine($"completed 「{task.Name}」");
                    return 0;
                }
                case "logout":
                {
                    var tok = Token();
                    if (tok == null) return Fail($"not signed in to {server}");
                    await api.Logout(tok);
                    CredentialStore.Delete(server);
                    Console.WriteLine("signed out");
                    return 0;
                }
                default: return Fail("unknown command " + args[0]);
            }
        }

        static string ReadPassword(string prompt)
        {
            Console.Error.Write(prompt);
            if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
            var sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
            }
            Console.Error.WriteLine();
            return sb.ToString();
        }
    }
}
